using Zentory.Application.CollaboratorPortal.Commands;
using Zentory.Application.Exceptions;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.OrganizationDeletion;

// PLAN-0018 CA-T11A-1 (parte de CA-103; RF-17 rev. 4; D-31): la regla del canje no depende de que el repositorio de
// organizaciones aplique el filtro global. Con un repositorio que sí devuelve la organización eliminada, un token de
// solicitud de esa organización sigue respondiendo como inexistente (404) y no abre sesión.
public sealed class ExchangeDeletedOrganizationGuardTests : IDisposable
{
    private const string CollaboratorType = "hourly_contractor";
    private const string Period           = "2026-11";

    private readonly ZentoryDbContext                        _db = TestDbContextFactory.Create();
    private readonly FakeCollaboratorRepository              _collaborators = new();
    private readonly FakeOrganizationRepository              _organizations = new();
    private readonly FakePilaVerificationRepository          _pila = new();
    private readonly FakeCollaboratorPayoutInvoiceRepository _payoutInvoices = new();

    private readonly Organization _org = Organization.Create("Org eliminada", LegalType.Empresa);
    private readonly Collaborator _collaborator;

    public ExchangeDeletedOrganizationGuardTests()
    {
        _organizations.Items.Add(_org);
        _collaborator = Collaborator.Create(_org.OrganizationId, "Persona", CollaboratorType, email: "persona@org.test");
        _collaborators.Items.Add(_collaborator);
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("pila_request")]
    [InlineData("payout_invoice_request")]
    public async Task PLAN0018_CA103_Canje_RepositorioQueDevuelveLaOrganizacionEliminada_Responde404SinSesion(string kind)
    {
        var token = SeedRequestToken(kind);

        // Control: con la organización vigente el mismo token abre sesión.
        var session = await Handler().Handle(new ExchangeCollaboratorTokenCommand(kind, token), CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(session.AccessToken));

        _org.MarkDeleted(Guid.NewGuid(), DateTime.UtcNow);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Handler().Handle(new ExchangeCollaboratorTokenCommand(kind, token), CancellationToken.None));
    }

    private string SeedRequestToken(string kind)
    {
        if (kind == "pila_request")
        {
            var pila = PilaVerification.Create(_org.OrganizationId, _collaborator.Id, Period);
            _pila.Items.Add(pila);
            return pila.Token.ToString();
        }

        var invoice = CollaboratorPayoutInvoice.Create(_org.OrganizationId, _collaborator.Id, Period, "Honorarios", 1_000m);
        _payoutInvoices.Items.Add(invoice);
        return invoice.PublicToken.ToString();
    }

    private ExchangeCollaboratorTokenCommandHandler Handler()
        => new(_db, _collaborators, _organizations, _pila, _payoutInvoices, TestCollaboratorJwt.CreateService(),
            new FakeUnitOfWork(_db));
}
