using System.Security.Cryptography;
using System.Text;
using Zentory.Application.CollaboratorPortal;
using Zentory.Application.CollaboratorPortal.Commands;
using Zentory.Application.CollaboratorPortal.DTOs;
using Zentory.Application.Exceptions;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.CollaboratorPortal;

// H-01: una sesión de solicitud (PILA / cuenta de cobro) queda limitada al colaborador del
// token; solo magic_link abre una sesión multi-org por email (y puede hacer switch).
public sealed class ExchangeTokenSessionScopeTests : IDisposable
{
    private const string SharedEmail = "persona.b@org-b.test";

    private readonly ZentoryDbContext                         _db = TestDbContextFactory.Create();
    private readonly FakeCollaboratorRepository               _collaborators = new();
    private readonly FakeOrganizationRepository               _organizations = new();
    private readonly FakePilaVerificationRepository           _pila = new();
    private readonly FakeCollaboratorPayoutInvoiceRepository  _payoutInvoices = new();
    private readonly CollaboratorJwtService                   _jwt = TestCollaboratorJwt.CreateService();

    private readonly Organization _orgA = Organization.Create("Org A", LegalType.Empresa);
    private readonly Organization _orgB = Organization.Create("Org B", LegalType.Empresa);

    public ExchangeTokenSessionScopeTests()
    {
        _organizations.Items.AddRange([_orgA, _orgB]);
    }

    public void Dispose() => _db.Dispose();

    private ExchangeCollaboratorTokenCommandHandler CreateExchangeHandler()
        => new(_db, _collaborators, _organizations, _pila, _payoutInvoices, _jwt, new FakeUnitOfWork(_db));

    private Collaborator AddCollaborator(Organization org, string name, string email)
    {
        var collaborator = Collaborator.Create(org.OrganizationId, name, "hourly_contractor", email: email);
        _collaborators.Items.Add(collaborator);
        return collaborator;
    }

    // Escenario F-1: el colaborador de A tiene un token de solicitud vigente y la empresa A le
    // cambia el email al de un colaborador existente de la org B.
    private (Collaborator FromA, Collaborator FromB) ArrangeF1()
    {
        var fromB = AddCollaborator(_orgB, "Persona B", SharedEmail);
        var fromA = AddCollaborator(_orgA, "Persona A", "persona.a@org-a.test");
        fromA.Update(fromA.Name, fromA.Type, fromA.Status, fromA.Currency, SharedEmail,
            fromA.Phone, fromA.Role, fromA.HourlyRate, fromA.MonthlyRate, fromA.IdNumber, fromA.ArlRiskLevel);
        return (fromA, fromB);
    }

    private void AssertSessionLimitedTo(CollaboratorSessionDto session, Collaborator fromA)
    {
        var token = TestCollaboratorJwt.Read(session.AccessToken);

        Assert.Equal([fromA.Id], TestCollaboratorJwt.CollaboratorIds(token));
        Assert.Equal(fromA.Id.ToString(), TestCollaboratorJwt.Claim(token, "active_collaborator_id"));
        Assert.Equal(_orgA.OrganizationId.ToString(), TestCollaboratorJwt.Claim(token, "active_org_id"));
        Assert.Equal(CollaboratorAuthMethod.Request, TestCollaboratorJwt.Claim(token, "auth_method"));

        var membership = Assert.Single(session.Memberships);
        Assert.Equal(fromA.Id, membership.CollaboratorId);
        Assert.Equal(_orgA.OrganizationId, membership.OrganizationId);
        Assert.Equal(fromA.Id, session.ActiveCollaborator.CollaboratorId);

        // La ruta de solicitud no debe buscar por email (era el origen de la fuga).
        Assert.Equal(0, _collaborators.ListByEmailCalls);
    }

    [Fact]
    public async Task CA1_PilaRequest_WithEmailSharedWithOtherOrg_IssuesSessionOnlyForTokenCollaborator()
    {
        var (fromA, _) = ArrangeF1();
        var row = PilaVerification.Create(_orgA.OrganizationId, fromA.Id, "2026-09");
        _pila.Items.Add(row);

        var session = await CreateExchangeHandler().Handle(
            new ExchangeCollaboratorTokenCommand("pila_request", row.Token.ToString()), CancellationToken.None);

        AssertSessionLimitedTo(session, fromA);
        Assert.Equal(new CollaboratorHighlightDto("pila", "2026-09", row.Id), session.Highlight);
    }

    [Fact]
    public async Task CA1_PayoutInvoiceRequest_WithEmailSharedWithOtherOrg_IssuesSessionOnlyForTokenCollaborator()
    {
        var (fromA, _) = ArrangeF1();
        var row = CollaboratorPayoutInvoice.Create(_orgA.OrganizationId, fromA.Id, "2026-09", "Servicios", 1_000_000m);
        _payoutInvoices.Items.Add(row);

        var session = await CreateExchangeHandler().Handle(
            new ExchangeCollaboratorTokenCommand("payout_invoice_request", row.PublicToken.ToString()), CancellationToken.None);

        AssertSessionLimitedTo(session, fromA);
        Assert.Equal(new CollaboratorHighlightDto("payout_invoice", "2026-09", row.Id), session.Highlight);
    }

    [Fact]
    public async Task CA1_PilaRequest_ForSoftDeletedCollaborator_ThrowsNotFoundInsteadOfFallingBackToOtherOrg()
    {
        var (fromA, _) = ArrangeF1();
        fromA.SoftDelete();
        var row = PilaVerification.Create(_orgA.OrganizationId, fromA.Id, "2026-09");
        _pila.Items.Add(row);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateExchangeHandler().Handle(
            new ExchangeCollaboratorTokenCommand("pila_request", row.Token.ToString()), CancellationToken.None));
    }

    [Fact]
    public async Task CA4_MagicLink_WithMembershipsInTwoOrgs_IssuesMultiOrgSessionAndSwitchToOrgBKeepsMagicLink()
    {
        var fromA = AddCollaborator(_orgA, "Persona A", SharedEmail);
        var fromB = AddCollaborator(_orgB, "Persona B", SharedEmail);

        const string rawToken = "magic-link-raw-token";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        _db.CollaboratorAccessTokens.Add(CollaboratorAccessToken.Create(SharedEmail, hash, DateTime.UtcNow.AddMinutes(15)));
        await _db.SaveChangesAsync();

        var session = await CreateExchangeHandler().Handle(
            new ExchangeCollaboratorTokenCommand("magic_link", rawToken), CancellationToken.None);

        var token = TestCollaboratorJwt.Read(session.AccessToken);
        Assert.Equal(
            new[] { fromA.Id, fromB.Id }.OrderBy(id => id),
            TestCollaboratorJwt.CollaboratorIds(token).OrderBy(id => id));
        Assert.Equal(CollaboratorAuthMethod.MagicLink, TestCollaboratorJwt.Claim(token, "auth_method"));
        Assert.Equal(
            new[] { fromA.Id, fromB.Id }.OrderBy(id => id),
            session.Memberships.Select(m => m.CollaboratorId).OrderBy(id => id));
        Assert.Null(session.Highlight);

        // Con esa misma sesión (claims reales del JWT), switch hacia el colaborador de B.
        var portal = FakeCollaboratorPortalContext.FromToken(token);
        var switchHandler = new SwitchActiveCollaboratorCommandHandler(portal, _collaborators, _organizations, _jwt);

        var switched = await switchHandler.Handle(new SwitchActiveCollaboratorCommand(fromB.Id), CancellationToken.None);

        var switchedToken = TestCollaboratorJwt.Read(switched.AccessToken);
        Assert.Equal(_orgB.OrganizationId.ToString(), TestCollaboratorJwt.Claim(switchedToken, "active_org_id"));
        Assert.Equal(fromB.Id.ToString(), TestCollaboratorJwt.Claim(switchedToken, "active_collaborator_id"));
        Assert.Equal(CollaboratorAuthMethod.MagicLink, TestCollaboratorJwt.Claim(switchedToken, "auth_method"));
        Assert.Equal(_orgB.OrganizationId, switched.ActiveCollaborator.OrganizationId);
    }

    [Fact]
    public async Task CA4_MagicLink_AfterExchange_MarksAccessTokenAsUsed()
    {
        AddCollaborator(_orgA, "Persona A", SharedEmail);
        const string rawToken = "magic-link-single-use";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        var accessToken = CollaboratorAccessToken.Create(SharedEmail, hash, DateTime.UtcNow.AddMinutes(15));
        _db.CollaboratorAccessTokens.Add(accessToken);
        await _db.SaveChangesAsync();

        await CreateExchangeHandler().Handle(new ExchangeCollaboratorTokenCommand("magic_link", rawToken), CancellationToken.None);

        Assert.NotNull(accessToken.UsedAt);
        await Assert.ThrowsAsync<ConflictException>(() => CreateExchangeHandler().Handle(
            new ExchangeCollaboratorTokenCommand("magic_link", rawToken), CancellationToken.None));
    }
}
