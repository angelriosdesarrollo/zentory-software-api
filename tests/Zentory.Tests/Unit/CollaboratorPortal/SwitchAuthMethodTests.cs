using Zentory.Application.CollaboratorPortal;
using Zentory.Application.CollaboratorPortal.Commands;
using Zentory.Application.Exceptions;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.CollaboratorPortal;

// H-01 / D-4: solo una sesión emitida por magic link puede cambiar de organización. Una sesión
// de solicitud o una sin claim auth_method (anterior al fix) recibe 409 sin re-emitir el JWT.
public sealed class SwitchAuthMethodTests
{
    private const string SharedEmail = "persona.b@org-b.test";

    private readonly FakeCollaboratorRepository     _collaborators = new();
    private readonly FakeOrganizationRepository     _organizations = new();
    private readonly RecordingCollaboratorJwtService _recordingJwt = new();

    private readonly Organization _orgA = Organization.Create("Org A", LegalType.Empresa);
    private readonly Organization _orgB = Organization.Create("Org B", LegalType.Empresa);

    private readonly Collaborator _fromA;
    private readonly Collaborator _fromB;

    public SwitchAuthMethodTests()
    {
        _organizations.Items.AddRange([_orgA, _orgB]);
        _fromA = Collaborator.Create(_orgA.OrganizationId, "Persona A", "hourly_contractor", email: SharedEmail);
        _fromB = Collaborator.Create(_orgB.OrganizationId, "Persona B", "hourly_contractor", email: SharedEmail);
        _collaborators.Items.AddRange([_fromA, _fromB]);
    }

    // Sesión con los claims reales que emite CollaboratorJwtService.
    private FakeCollaboratorPortalContext IssueSession(IReadOnlyList<Guid> collaboratorIds, string authMethod)
    {
        var token = TestCollaboratorJwt.CreateService().GenerateSessionToken(
            SharedEmail, collaboratorIds, _fromA.Id, _orgA.OrganizationId, _orgA.Name, authMethod);
        return FakeCollaboratorPortalContext.FromToken(TestCollaboratorJwt.Read(token));
    }

    private SwitchActiveCollaboratorCommandHandler CreateHandler(FakeCollaboratorPortalContext portal)
        => new(portal, _collaborators, _organizations, _recordingJwt);

    [Fact]
    public async Task CA2_RequestSession_SwitchToCollaboratorOfOrgB_ThrowsNotAMembershipWithoutIssuingToken()
    {
        // La sesión de solicitud (TK-0001) solo trae al colaborador del token.
        var portal = IssueSession([_fromA.Id], CollaboratorAuthMethod.Request);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => CreateHandler(portal).Handle(
            new SwitchActiveCollaboratorCommand(_fromB.Id), CancellationToken.None));

        Assert.Equal("NOT_A_MEMBERSHIP", ex.Code);
        Assert.Equal(0, _recordingJwt.GenerateSessionTokenCalls);
        Assert.Equal(0, _collaborators.ListByEmailCalls);
    }

    [Fact]
    public async Task CA2_RequestSession_WithOrgBCollaboratorInIds_ThrowsSessionNotSwitchableWithoutIssuingToken()
    {
        // Aunque collaborator_ids contuviera a B, el auth_method=request impide el switch.
        var portal = IssueSession([_fromA.Id, _fromB.Id], CollaboratorAuthMethod.Request);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => CreateHandler(portal).Handle(
            new SwitchActiveCollaboratorCommand(_fromB.Id), CancellationToken.None));

        Assert.Equal("SESSION_NOT_SWITCHABLE", ex.Code);
        Assert.Equal(0, _recordingJwt.GenerateSessionTokenCalls);
        Assert.Equal(0, _collaborators.ListByEmailCalls);
    }

    [Fact]
    public async Task CA3_SessionWithoutAuthMethodClaim_SwitchToCollaboratorOfOrgB_ThrowsSessionNotSwitchableWithoutIssuingToken()
    {
        // Sesión emitida antes del fix: el JWT no trae auth_method, pero sí ambos colaboradores.
        var portal = new FakeCollaboratorPortalContext
        {
            Email                  = SharedEmail,
            CollaboratorIds        = [_fromA.Id, _fromB.Id],
            ActiveCollaboratorId   = _fromA.Id,
            ActiveOrganizationId   = _orgA.OrganizationId,
            ActiveOrganizationName = _orgA.Name,
            AuthMethod             = null,
        };

        var ex = await Assert.ThrowsAsync<ConflictException>(() => CreateHandler(portal).Handle(
            new SwitchActiveCollaboratorCommand(_fromB.Id), CancellationToken.None));

        Assert.Equal("SESSION_NOT_SWITCHABLE", ex.Code);
        Assert.Equal(0, _recordingJwt.GenerateSessionTokenCalls);
        Assert.Equal(0, _collaborators.ListByEmailCalls);
    }

    [Fact]
    public async Task CA4_MagicLinkSession_SwitchToCollaboratorOfOrgB_IssuesTokenOnce()
    {
        // Control: el doble sí registra la emisión cuando el switch es legítimo.
        var portal = IssueSession([_fromA.Id, _fromB.Id], CollaboratorAuthMethod.MagicLink);

        var switched = await CreateHandler(portal).Handle(
            new SwitchActiveCollaboratorCommand(_fromB.Id), CancellationToken.None);

        Assert.Equal(1, _recordingJwt.GenerateSessionTokenCalls);
        Assert.Equal(_orgB.OrganizationId, switched.ActiveCollaborator.OrganizationId);
    }
}
