using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Options;
using Zentory.Application.Organization.Commands;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Purge;
using Zentory.Tests.Support;
using OrganizationEntity = Zentory.Domain.Entities.Organization;

namespace Zentory.Tests.Integration.Purge;

// PLAN-0018 T12b sobre el host real: purga física de organizaciones (RF-78), archivo legal (RF-79) y su orden de rev. 2.2
// (ADR purga §4; SR-0018.27, SR-0018.28, SR-0018.34). CA-107 y CA-122 completos sobre el escenario de la spec, y la parte
// de CA-108 que se cierra aquí (CA-T12B-1: Snapshot sin datos internos y forma de la clave del PDF).
//
// Cada test arranca su propio host (una base InMemory y un almacenamiento por test): el job purga todo lo vencido de la
// base, así que compartirla entre tests mezclaría sus escenarios.
public sealed partial class OrganizationPurgeTests
{
    private const string IssuerNit          = "900123456";
    private const string IssuerLegalName    = "Emisora Purga SAS";
    private const string InvoiceNumber      = "FE-0007";
    private const string ClientInternalNote = "NOTA-INTERNA-CLIENTE";
    private const string PaymentNote        = "NOTA-INTERNA-PAGO";
    private const string InvoiceNote        = "NOTA-INTERNA-FACTURA";
    private const string ClientEmail        = "cliente-oculto@zentory.test";
    private const string ContactEmail       = "contacto-oculto@zentory.test";
    private const decimal RateCost          = 98765.43m;

    // ── CA-107: el escenario completo ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA107_Job_PurgaASinTocarBCDNiEYArchivaSusDocumentosEmitidos()
    {
        await using var host = new PurgeHost();
        var scenario = await host.SeedScenarioAsync();
        var before = await host.FootprintsAsync(scenario.B, scenario.C, scenario.D, scenario.E);

        var result = await host.RunJobAsync();

        Assert.Equal([scenario.A.Id], result.Purged);
        Assert.Empty(result.Failed);

        // A: ni filas de tenant, ni archivos {A}/, ni la fila de Organization.
        Assert.Empty(await host.TenantRowsAsync(scenario.A.Id));
        Assert.Null(await host.OrganizationAsync(scenario.A.Id));
        Assert.DoesNotContain(host.Files.Objects.Keys, key => key.StartsWith($"{scenario.A.Id}/", StringComparison.Ordinal));

        // B, C, D y E intactas frente a la instantánea, incluidas la nota y el archivo de B que mencionan el Guid de A.
        Assert.Equal(before, await host.FootprintsAsync(scenario.B, scenario.C, scenario.D, scenario.E));
        Assert.Contains($"{scenario.B.Id}/otros/{scenario.A.Id}.pdf", host.Files.Objects.Keys);
        Assert.Contains(await host.ClientNotesAsync(scenario.B.Id), note => note.Contains(scenario.A.Id.ToString(), StringComparison.Ordinal));

        // D perdió la carrera con su restauración: activa, sin PurgeStartedAt. C sigue eliminada dentro del plazo.
        var d = await host.OrganizationAsync(scenario.D.Id);
        Assert.NotNull(d);
        Assert.Null(d!.DeletedAt);
        Assert.Null(d.PurgeStartedAt);
        var c = await host.OrganizationAsync(scenario.C.Id);
        Assert.NotNull(c!.DeletedAt);
        Assert.Null(c.PurgeStartedAt);
        Assert.NotNull(await host.OrganizationAsync(scenario.E.Id));
    }

    [Fact]
    public async Task PLAN0018_CA107_ArchivoLegal_FacturaEmitidaConSnapshotCompletoYSinPdfCuentasDeCobroConPdfYSinBorradores()
    {
        await using var host = new PurgeHost();
        var scenario = await host.SeedScenarioAsync();

        await host.RunJobAsync();

        var archive = await host.ArchiveAsync();
        Assert.Equal(3, archive.Count);
        Assert.DoesNotContain(archive, a => a.SourceId == scenario.A.DraftInvoiceId);

        // Factura emitida (C-7): Snapshot con encabezado, cliente, ítems, totales, pagos y estado; sin PDF.
        var invoice = Assert.Single(archive, a => a.SourceType == LegalDocumentSourceType.Invoice);
        Assert.Equal(scenario.A.IssuedInvoiceId, invoice.SourceId);
        Assert.Equal(InvoiceNumber, invoice.DocumentNumber);
        Assert.Equal(IssuerNit, invoice.IssuerTaxId);
        Assert.Equal(IssuerLegalName, invoice.IssuerLegalName);
        Assert.Null(invoice.PdfStorageKey);
        using (var snapshot = JsonDocument.Parse(invoice.Snapshot))
        {
            var root = snapshot.RootElement;
            Assert.Equal(InvoiceNumber, root.GetProperty("header").GetProperty("number").GetString());
            Assert.Equal(IssuerNit, root.GetProperty("header").GetProperty("issuer").GetProperty("taxId").GetString());
            Assert.Equal(scenario.A.ClientName, root.GetProperty("client").GetProperty("name").GetString());
            Assert.Equal(2, root.GetProperty("items").GetArrayLength());
            Assert.Equal(300m, root.GetProperty("totals").GetProperty("total").GetDecimal());
            Assert.Equal(100m, root.GetProperty("payments")[0].GetProperty("amount").GetDecimal());
            Assert.Equal("partial", root.GetProperty("status").GetString());
        }

        // Cuentas de cobro emitidas: la que tenía PDF lo conserva copiado; la que no lo tenía lo recibe generado.
        var payouts = archive.Where(a => a.SourceType == LegalDocumentSourceType.CollaboratorPayoutInvoice).ToList();
        Assert.Equal(2, payouts.Count);
        Assert.All(payouts, payout =>
        {
            Assert.NotNull(payout.PdfStorageKey);
            Assert.True(host.Files.Objects.ContainsKey(payout.PdfStorageKey!), "Falta el PDF archivado.");
            Assert.False(string.IsNullOrWhiteSpace(payout.Snapshot));
        });
        var copied = Assert.Single(payouts, p => p.SourceId == scenario.A.PayoutWithPdfId);
        Assert.Equal(PurgeHost.PayoutPdfContent, host.Files.Objects[copied.PdfStorageKey!].Content);
        var generated = Assert.Single(payouts, p => p.SourceId == scenario.A.PayoutWithoutPdfId);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(host.Files.Objects[generated.PdfStorageKey!].Content[..4]));

        // Ninguna fila archivada lleva la organización, y todas vencen en IssuedAt + Retention:LegalDocumentsYears.
        Assert.Null(host.Model.FindEntityType(typeof(LegalDocumentArchive))!.FindProperty("OrganizationId"));
        Assert.All(archive, a =>
        {
            Assert.DoesNotContain(scenario.A.Id.ToString(), a.Snapshot, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(a.IssuedAt.AddYears(RetentionOptions.DefaultLegalDocumentsYears), a.RetainUntil);
        });
    }

    [Fact]
    public async Task PLAN0018_CA107_Auditoria_QuedaUnAuditLogDePurgaDeASinNombreNiNit()
    {
        await using var host = new PurgeHost();
        var scenario = await host.SeedScenarioAsync();

        await host.RunJobAsync();

        var audit = Assert.Single(await host.PurgeAuditLogsAsync());
        Assert.Equal(scenario.A.Id, audit.OrganizationId);
        Assert.Equal(scenario.A.Id, audit.RecordId);
        Assert.Equal("organizations", audit.TableName);
        Assert.NotNull(audit.NewValues);
        Assert.DoesNotContain(scenario.A.Name, audit.NewValues!, StringComparison.Ordinal);
        Assert.DoesNotContain(IssuerNit, audit.NewValues!, StringComparison.Ordinal);
        Assert.DoesNotContain(IssuerLegalName, audit.NewValues!, StringComparison.Ordinal);
        Assert.Null(audit.OldValues);
        using var values = JsonDocument.Parse(audit.NewValues!);
        Assert.Equal(3, values.RootElement.GetProperty("documentsArchived").GetInt32());
        Assert.True(values.RootElement.GetProperty("filesDeleted").GetInt32() >= 4);
        Assert.True(values.RootElement.GetProperty("rowsByTable").EnumerateObject().Any());
    }

    [Fact]
    public async Task PLAN0018_CA107_FacturacionDelOwner_SeConservaYLasCincoTablasQuedanSinOrganizationId()
    {
        await using var host = new PurgeHost();
        var scenario = await host.SeedScenarioAsync();

        await host.RunJobAsync();

        var billing = await host.OwnerBillingAsync(scenario.A);
        Assert.NotNull(billing.Subscription);
        Assert.NotNull(billing.PaymentMethod);
        Assert.NotNull(billing.Customer);
        Assert.Null(billing.Customer!.OrganizationId);
        Assert.Null(Assert.Single(billing.Invoices).OrganizationId);
        Assert.Null(Assert.Single(billing.Payments).OrganizationId);
        Assert.Null(Assert.Single(billing.Credits).OrganizationId);
        Assert.Null(Assert.Single(billing.Redemptions).OrganizationId);
    }

    [Fact]
    public async Task PLAN0018_CA107_JobDetenidoTrasMarcarElInicio_RestoreDelOwnerResponde404YLaCorridaSiguienteTermina()
    {
        await using var host = new PurgeHost();
        var scenario = await host.SeedScenarioAsync();

        host.StopBeforeDelete = true;
        var stopped = await host.RunJobAsync();
        Assert.Equal([scenario.A.Id], stopped.Failed);
        Assert.NotNull((await host.OrganizationAsync(scenario.A.Id))!.PurgeStartedAt);

        // Sin la marca, A estaría dentro del plazo: así el 404 solo puede venir de PurgeStartedAt. El control (C, también
        // dentro del plazo y sin marcar) sí se restaura.
        await OrganizationRestoreApi.MarkDeletedAsync(host.Services, scenario.A.Id, scenario.A.Owner.UserId, host.Now.AddDays(-5));
        using var http = host.ClientWithoutOrganization(scenario.A.Owner);
        using (var blocked = await OrganizationRestoreApi.RestoreAsync(http, scenario.A.Id))
            Assert.Equal(HttpStatusCode.NotFound, blocked.StatusCode);
        var a = await host.OrganizationAsync(scenario.A.Id);
        Assert.NotNull(a!.DeletedAt);
        Assert.NotNull(a.PurgeStartedAt);

        using var ownerOfC = host.ClientWithoutOrganization(scenario.C.Owner);
        using (var control = await OrganizationRestoreApi.RestoreAsync(ownerOfC, scenario.C.Id))
            Assert.Equal(HttpStatusCode.NoContent, control.StatusCode);

        host.StopBeforeDelete = false;
        var resumed = await host.RunJobAsync();

        Assert.Equal([scenario.A.Id], resumed.Purged);
        Assert.Null(await host.OrganizationAsync(scenario.A.Id));
        Assert.Empty(await host.TenantRowsAsync(scenario.A.Id));
    }

    [Fact]
    public async Task PLAN0018_CA107_AlmacenamientoQueFallaAlBorrar_FilasBorradasOrganizacionMarcadaYLaSiguienteCorridaCierra()
    {
        await using var host = new PurgeHost();
        var scenario = await host.SeedScenarioAsync();

        host.FailDeletes = true;
        var failed = await host.RunJobAsync();

        Assert.Equal([scenario.A.Id], failed.Failed);
        Assert.Empty(await host.TenantRowsAsync(scenario.A.Id));
        var marked = await host.OrganizationAsync(scenario.A.Id);
        Assert.NotNull(marked);
        Assert.NotNull(marked!.PurgeStartedAt);
        Assert.Contains(host.Files.Objects.Keys, key => key.StartsWith($"{scenario.A.Id}/", StringComparison.Ordinal));
        Assert.Empty(await host.PurgeAuditLogsAsync());

        host.FailDeletes = false;
        var resumed = await host.RunJobAsync();

        Assert.Equal([scenario.A.Id], resumed.Purged);
        Assert.DoesNotContain(host.Files.Objects.Keys, key => key.StartsWith($"{scenario.A.Id}/", StringComparison.Ordinal));
        Assert.Null(await host.OrganizationAsync(scenario.A.Id));
        Assert.Single(await host.PurgeAuditLogsAsync());
        Assert.Equal(3, (await host.ArchiveAsync()).Count);
    }

    [Fact]
    public async Task PLAN0018_CA107_PurgadorConGuidVacio_LanzaSinBorrarNingunaFilaNiArchivo()
    {
        await using var host = new PurgeHost();
        await host.SeedScenarioAsync();
        var rows  = await OrganizationDeletionApi.RowCountsAsync(host.Services);
        var files = host.Files.Objects.Keys.Order(StringComparer.Ordinal).ToList();

        using (var scope = host.Services.CreateScope())
        {
            var purger = scope.ServiceProvider.GetRequiredService<OrganizationDataPurger>();
            await Assert.ThrowsAsync<ArgumentException>(() => purger.PurgeAsync(Guid.Empty, host.Now));
        }
        Assert.Throws<ArgumentException>(() => OrganizationStoragePrefix.For(Guid.Empty));

        Assert.Equal(rows, await OrganizationDeletionApi.RowCountsAsync(host.Services));
        Assert.Equal(files, host.Files.Objects.Keys.Order(StringComparer.Ordinal).ToList());
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("{A}")]
    public async Task PLAN0018_CA107_BorradoDeArchivosPorPrefijoInvalido_LanzaSinBorrarNada(string shape)
    {
        await using var host = new PurgeHost();
        var scenario = await host.SeedScenarioAsync();
        var files = host.Files.Objects.Keys.Order(StringComparer.Ordinal).ToList();
        var prefix = shape.Replace("{A}", scenario.A.Id.ToString(), StringComparison.Ordinal);

        using (var scope = host.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
            await Assert.ThrowsAsync<ArgumentException>(() => storage.DeleteOrganizationFilesAsync(prefix));
        }

        Assert.Equal(files, host.Files.Objects.Keys.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task PLAN0018_CA107_SegundaCorrida_NoFallaNiDuplica()
    {
        await using var host = new PurgeHost();
        await host.SeedScenarioAsync();
        await host.RunJobAsync();
        var rows    = await OrganizationDeletionApi.RowCountsAsync(host.Services);
        var files   = host.Files.Objects.Keys.Order(StringComparer.Ordinal).ToList();
        var archive = (await host.ArchiveAsync()).Select(a => a.Id).Order().ToList();

        var second = await host.RunJobAsync();

        Assert.Empty(second.Purged);
        Assert.Empty(second.Failed);
        Assert.Equal(0, second.ExpiredArchivesDeleted);
        Assert.Equal(rows, await OrganizationDeletionApi.RowCountsAsync(host.Services));
        Assert.Equal(files, host.Files.Objects.Keys.Order(StringComparer.Ordinal).ToList());
        Assert.Equal(archive, (await host.ArchiveAsync()).Select(a => a.Id).Order().ToList());
        Assert.Single(await host.PurgeAuditLogsAsync());
    }

    [Fact]
    public async Task PLAN0018_CA107_ArchivoVencido_ConElRelojEnRetainUntilMasUnDia_SeBorranLaFilaYSuPdf()
    {
        await using var host = new PurgeHost();
        await host.SeedScenarioAsync();
        await host.RunJobAsync();
        var archive = await host.ArchiveAsync();
        var pdfs = archive.Select(a => a.PdfStorageKey).OfType<string>().ToList();
        Assert.NotEmpty(pdfs);

        // Un día antes del vencimiento más temprano no se borra nada.
        host.Clock.SetUtcNow(new DateTimeOffset(archive.Min(a => a.RetainUntil), TimeSpan.Zero).AddDays(-1));
        Assert.Equal(0, (await host.RunJobAsync()).ExpiredArchivesDeleted);
        Assert.Equal(archive.Count, (await host.ArchiveAsync()).Count);

        host.Clock.SetUtcNow(new DateTimeOffset(archive.Max(a => a.RetainUntil), TimeSpan.Zero).AddDays(1));
        var expired = await host.RunJobAsync();

        Assert.Equal(archive.Count, expired.ExpiredArchivesDeleted);
        Assert.Empty(await host.ArchiveAsync());
        Assert.All(pdfs, key => Assert.DoesNotContain(key, host.Files.Objects.Keys));
    }

    [Fact]
    public async Task PLAN0018_CA107_Plazos_PurgeAfterDays10YLegalDocumentsYears1_CambianQuienSePurgaYCuandoVenceElArchivo()
    {
        await using var host = new PurgeHost(purgeAfterDays: 10, legalDocumentsYears: 1);
        var scenario = await host.SeedScenarioAsync();

        var result = await host.RunJobAsync();

        // C (eliminada hace 29 días) ya venció con 10 días de plazo; D sigue perdiendo la carrera con su restauración.
        Assert.Equal(new[] { scenario.A.Id, scenario.C.Id }.Order(), result.Purged.Order());
        Assert.Null(await host.OrganizationAsync(scenario.C.Id));
        Assert.NotNull(await host.OrganizationAsync(scenario.D.Id));
        var archive = await host.ArchiveAsync();
        Assert.NotEmpty(archive);
        Assert.All(archive, a => Assert.Equal(a.IssuedAt.AddYears(1), a.RetainUntil));
    }

    // ── CA-122: el catálogo aplicado sobre el escenario ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA122_TrasPurgarA_SuGuidNoApareceEnNingunaColumnaUuidSalvoEnElAuditLogDeLaPurga()
    {
        await using var host = new PurgeHost();
        var scenario = await host.SeedScenarioAsync();
        Assert.NotEmpty(await host.UuidOccurrencesAsync(scenario.A.Id));

        await host.RunJobAsync();

        var occurrences = await host.UuidOccurrencesAsync(scenario.A.Id);
        var purgeAudit = Assert.Single(await host.PurgeAuditLogsAsync());
        Assert.All(occurrences, occurrence =>
        {
            Assert.Equal(nameof(AuditLog), occurrence.Type);
            Assert.Equal(purgeAudit.Id, occurrence.RowId);
        });
    }

    [Fact]
    public async Task PLAN0018_CA122_ProjectFinancialsYPilaVerificationDocumentDeA_YSusArchivos_YaNoExisten()
    {
        await using var host = new PurgeHost();
        var scenario = await host.SeedScenarioAsync();

        await host.RunJobAsync();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        Assert.False(await db.ProjectFinancials.IgnoreQueryFilters().AnyAsync(f => f.ProjectId == scenario.A.ProjectId));
        Assert.False(await db.PilaVerificationDocuments.IgnoreQueryFilters().AnyAsync(d => d.Id == scenario.A.PilaDocumentId));
        Assert.DoesNotContain(scenario.A.PilaDocumentKey, host.Files.Objects.Keys);
        Assert.DoesNotContain(scenario.A.ProjectFileKey, host.Files.Objects.Keys);
        // Los de B siguen: el borrado por padre solo toma los padres de A.
        Assert.True(await db.ProjectFinancials.IgnoreQueryFilters().AnyAsync(f => f.ProjectId == scenario.B.ProjectId));
    }

    [Fact]
    public async Task PLAN0018_CA122_UsuariosConOrganizationIdOLastActiveOrganizationIdIgualAA_QuedanEnNull()
    {
        await using var host = new PurgeHost();
        var scenario = await host.SeedScenarioAsync();

        await host.RunJobAsync();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var owner = await db.Users.AsNoTracking().SingleAsync(u => u.UserId == scenario.A.Owner.UserId);
        var lastActive = await db.Users.AsNoTracking().SingleAsync(u => u.UserId == scenario.A.LastActiveUserId);
        Assert.Null(owner.OrganizationId);
        Assert.Null(lastActive.LastActiveOrganizationId);
        Assert.False(await db.Users.AnyAsync(u => u.OrganizationId == scenario.A.Id || u.LastActiveOrganizationId == scenario.A.Id));
    }

    [Fact]
    public async Task PLAN0018_CA122_TokenDeColaboradorSoloEnASeBorraYElDeUnoTambienEnBSeConserva()
    {
        await using var host = new PurgeHost();
        var scenario = await host.SeedScenarioAsync();

        await host.RunJobAsync();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        Assert.False(await db.CollaboratorAccessTokens.AnyAsync(t => t.Email == scenario.A.OnlyInAEmail));
        Assert.True(await db.CollaboratorAccessTokens.AnyAsync(t => t.Email == scenario.A.AlsoInBEmail));
    }

    [Fact]
    public async Task PLAN0018_CA122_ArchivoConPrefijoDeASinFilaAsociada_TambienSeBorra()
    {
        await using var host = new PurgeHost();
        var scenario = await host.SeedScenarioAsync();
        Assert.Contains(scenario.A.OrphanUploadKey, host.Files.Objects.Keys);

        await host.RunJobAsync();

        Assert.DoesNotContain(scenario.A.OrphanUploadKey, host.Files.Objects.Keys);
    }

    // ── CA-T12B-1 (parte de CA-108): confidencialidad del archivo ───────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA108_PdfArchivado_TieneFormaLegalArchiveGuidPdfSinNumeroNitNiOrganizationId()
    {
        await using var host = new PurgeHost();
        var scenario = await host.SeedScenarioAsync();

        await host.RunJobAsync();

        var keys = (await host.ArchiveAsync()).Select(a => a.PdfStorageKey).OfType<string>().ToList();
        Assert.Equal(2, keys.Count);
        Assert.All(keys, key =>
        {
            Assert.Matches(ArchivedPdfKey(), key);
            Assert.DoesNotContain(scenario.A.Id.ToString(), key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(IssuerNit, key, StringComparison.Ordinal);
            Assert.DoesNotContain(InvoiceNumber, key, StringComparison.Ordinal);
            Assert.DoesNotContain(PurgeHost.PayoutPeriod, key, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task PLAN0018_CA108_SnapshotDeFactura_NoContieneRateCostNotasInternasNiEmailsDeContacto()
    {
        await using var host = new PurgeHost();
        await host.SeedScenarioAsync();

        await host.RunJobAsync();

        var invoice = Assert.Single(await host.ArchiveAsync(), a => a.SourceType == LegalDocumentSourceType.Invoice);
        foreach (var hidden in new[] { ClientInternalNote, PaymentNote, InvoiceNote, ClientEmail, ContactEmail, "rateCost", "98765.43" })
            Assert.DoesNotContain(hidden, invoice.Snapshot, StringComparison.OrdinalIgnoreCase);
        foreach (var archived in await host.ArchiveAsync())
        {
            Assert.DoesNotContain("notes", archived.Snapshot, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("email", archived.Snapshot, StringComparison.OrdinalIgnoreCase);
        }
    }

    [GeneratedRegex(@"^legal-archive/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.pdf$")]
    private static partial Regex ArchivedPdfKey();

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record SeededOrg(Guid Id, string Name, User Owner, Guid ProjectId);

    // Lo que el escenario siembra en A y los tests consultan después.
    private sealed record OrgA(
        Guid   Id,
        string Name,
        User   Owner,
        Guid   ProjectId,
        string ClientName,
        Guid   IssuedInvoiceId,
        Guid   DraftInvoiceId,
        Guid   PayoutWithPdfId,
        Guid   PayoutWithoutPdfId,
        Guid   PilaDocumentId,
        string PilaDocumentKey,
        string ProjectFileKey,
        string OrphanUploadKey,
        string OnlyInAEmail,
        string AlsoInBEmail,
        Guid   LastActiveUserId,
        Guid   OwnerCustomerId);

    private sealed record Scenario(OrgA A, SeededOrg B, SeededOrg C, SeededOrg D, SeededOrg E);

    private sealed record OwnerBilling(
        Subscription?                   Subscription,
        PaymentMethod?                  PaymentMethod,
        BillingCustomer?                Customer,
        IReadOnlyList<BillingInvoice>   Invoices,
        IReadOnlyList<BillingPayment>   Payments,
        IReadOnlyList<BillingCredit>    Credits,
        IReadOnlyList<CouponRedemption> Redemptions);

    private sealed record UuidOccurrence(string Type, string Property, Guid? RowId);

    // Host de un test: reloj simulado, almacenamiento en memoria que puede fallar al borrar, purgador que puede detenerse
    // después de marcar el inicio, y la restauración de D justo antes de que se confirme su marca (el sello no coincide).
    private sealed class PurgeHost : IAsyncDisposable
    {
        public const string PayoutPeriod = "2026-09";
        public static readonly byte[] PayoutPdfContent = "%PDF-cuenta-de-cobro-original"u8.ToArray();

        private readonly ZentoryApiFactory _base = new();
        private readonly WebApplicationFactory<Program> _host;
        private Guid? _restoreBeforePurgeStart;
        private Guid? _restoreOwner;

        public PurgeHost(int? purgeAfterDays = null, int? legalDocumentsYears = null)
        {
            _host = _base.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);

                services.RemoveAll<IStorageService>();
                services.AddSingleton<IStorageService>(new FlakyStorage(this));

                services.AddScoped<OrganizationDataPurger>();
                services.RemoveAll<IOrganizationDataPurger>();
                services.AddScoped<IOrganizationDataPurger>(sp => StopBeforeDelete
                    ? new StoppedPurger(sp.GetRequiredService<OrganizationDataPurger>())
                    : sp.GetRequiredService<OrganizationDataPurger>());

                services.AddSingleton<IInterceptor>(new RestoreBeforePurgeStart(this));

                if (purgeAfterDays is { } days)
                    services.PostConfigure<OrganizationsOptions>(o => o.PurgeAfterDays = days);
                if (legalDocumentsYears is { } years)
                    services.PostConfigure<RetentionOptions>(o => o.LegalDocumentsYears = years);
            }));
        }

        public FakeTimeProvider   Clock { get; } = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        public FakeStorageService Files { get; } = new();
        public bool               FailDeletes      { get; set; }
        public bool               StopBeforeDelete { get; set; }

        public IServiceProvider Services => _host.Services;
        public DateTime         Now      => Clock.GetUtcNow().UtcDateTime;

        public Microsoft.EntityFrameworkCore.Metadata.IModel Model
        {
            get
            {
                using var scope = Services.CreateScope();
                return scope.ServiceProvider.GetRequiredService<ZentoryDbContext>().Model;
            }
        }

        public async Task<OrganizationPurgeRunResult> RunJobAsync()
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RunOrganizationPurgeCommand());
        }

        public HttpClient ClientWithoutOrganization(User user)
        {
            var http = _host.CreateClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", OrganizationRestoreApi.TokenWithoutOrganization(_host.Services, user));
            return http;
        }

        // ── Escenario de CA-107 ─────────────────────────────────────────────────────────────────────────────────

        public async Task<Scenario> SeedScenarioAsync()
        {
            // Sin límite de organizaciones propias: el control de restauración no debe chocar con max_owned_orgs.
            await OrganizationRestoreApi.EnsureOwnedOrganizationLimitAsync(Services, Plan.Studio, LegalType.Empresa, null);

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            var plan = await db.BillingPlans.FirstAsync(p => p.Name == Plan.Studio);

            var (aOrg, aOwner, aCustomer) = NewOrganization(db, plan, "A");
            var (bOrg, bOwner, _)         = NewOrganization(db, plan, "B");
            var (cOrg, cOwner, _)         = NewOrganization(db, plan, "C");
            var (dOrg, dOwner, _)         = NewOrganization(db, plan, "D");
            var eOrg = OrganizationEntity.Create($"Org E {Guid.NewGuid():N}", LegalType.Empresa);
            eOrg.SetOwner(aOwner.UserId);
            db.Organizations.Add(eOrg);
            db.OrganizationMembers.Add(OrganizationMember.Create(eOrg.OrganizationId, aOwner.UserId, OrgRole.Owner));

            var a = aOrg.OrganizationId;
            var b = bOrg.OrganizationId;

            // Perfil del emisor, tal como figura en sus documentos.
            db.OrganizationSettings.Add(OrganizationSettings.Set(a, OrgSettingKey.ProfileLegalName, IssuerLegalName));
            db.OrganizationSettings.Add(OrganizationSettings.Set(a, OrgSettingKey.ProfileNit, IssuerNit));

            // Cliente, proyecto, horas con RateCost y facturas (una emitida con ítems y pago, una en borrador).
            var clientName = $"Cliente A {Guid.NewGuid():N}";
            var client = Client.Create(a, clientName, "Contacto", email: ClientEmail, nit: "800111222", notes: ClientInternalNote);
            db.Clients.Add(client);
            db.ClientContacts.Add(ClientContact.Create(a, client.Id, "Contacto", email: ContactEmail));
            var project = Project.Create(a, client.Id, "Proyecto A", BillingType.Hourly, 1000m);
            db.Projects.Add(project);
            db.ProjectFinancials.Add(ProjectFinancials.Initialize(project.Id));
            var timeEntry = TimeEntry.Create(a, project.Id, DateOnly.FromDateTime(Now), 2m, RateCost);
            db.TimeEntries.Add(timeEntry);

            var issued = Invoice.Create(a, client.Id, InvoiceNumber, DateOnly.FromDateTime(Now.AddDays(30)),
                documentType: "factura", projectId: project.Id, notes: InvoiceNote);
            var first  = InvoiceItem.Create(a, issued.Id, "Horas", 2m, 100m, timeEntryId: timeEntry.Id);
            var second = InvoiceItem.Create(a, issued.Id, "Licencia", 1m, 100m, sortOrder: 1);
            issued.AddItem(first);
            issued.AddItem(second);
            issued.MarkAsSent();
            issued.RecordPayment(100m);
            db.Invoices.Add(issued);
            db.InvoiceItems.AddRange(first, second);
            db.InvoicePayments.Add(InvoicePayment.Create(a, issued.Id, 100m, "COP", DateOnly.FromDateTime(Now),
                paymentMethod: "transfer", reference: "TRX-1", notes: PaymentNote));
            var draft = Invoice.Create(a, client.Id, "FE-0008", DateOnly.FromDateTime(Now.AddDays(30)), documentType: "factura");
            db.Invoices.Add(draft);

            // Colaboradores: uno solo en A y otro también en B, cada uno con su token de acceso al portal.
            var onlyInA = $"solo-a-{Guid.NewGuid():N}@zentory.test";
            var alsoInB = $"tambien-b-{Guid.NewGuid():N}@zentory.test";
            var collaborator = Collaborator.Create(a, "Colaborador A", "hourly_contractor", email: onlyInA, idNumber: "1020304050");
            db.Collaborators.Add(collaborator);
            db.Collaborators.Add(Collaborator.Create(a, "Colaborador AB", "hourly_contractor", email: alsoInB));
            db.Collaborators.Add(Collaborator.Create(b, "Colaborador AB", "hourly_contractor", email: alsoInB));
            db.CollaboratorAccessTokens.Add(CollaboratorAccessToken.Create(onlyInA, $"hash-{Guid.NewGuid():N}", Now.AddDays(1)));
            db.CollaboratorAccessTokens.Add(CollaboratorAccessToken.Create(alsoInB, $"hash-{Guid.NewGuid():N}", Now.AddDays(1)));

            // Cuentas de cobro emitidas: con PDF guardado y sin él. Más una en borrador, que no se archiva.
            var payoutKey = $"{a}/payout-invoices/{collaborator.Id}/cuenta-cobro-{PayoutPeriod}.pdf";
            var withPdf = CollaboratorPayoutInvoice.Create(a, collaborator.Id, PayoutPeriod, "Servicios", 500m);
            withPdf.MarkGenerated(payoutKey, "cuenta.pdf", PayoutPdfContent.Length, "application/pdf");
            var withoutPdf = CollaboratorPayoutInvoice.Create(a, collaborator.Id, "2026-08", "Servicios", 400m);
            withoutPdf.MarkSent();
            var payoutDraft = CollaboratorPayoutInvoice.Create(a, collaborator.Id, "2026-10", "Servicios", 300m);
            db.CollaboratorPayoutInvoices.AddRange(withPdf, withoutPdf, payoutDraft);
            Files.Seed(payoutKey, "application/pdf", PayoutPdfContent);

            // PILA con su documento (hijo por padre) y un archivo de proyecto, con sus archivos bajo {A}/.
            var pila = PilaVerification.Create(a, collaborator.Id, PayoutPeriod);
            var pilaKey = $"{a}/pila/{pila.Id}/planilla.pdf";
            var pilaDocument = PilaVerificationDocument.Create(pila.Id, pilaKey);
            db.PilaVerifications.Add(pila);
            db.PilaVerificationDocuments.Add(pilaDocument);
            Files.Seed(pilaKey);
            var projectFileKey = $"{a}/projects/{project.Id}/contrato.pdf";
            db.ProjectFiles.Add(ProjectFile.Create(a, project.Id, "contrato.pdf", "pdf", "1 KB", "AO", projectFileKey));
            Files.Seed(projectFileKey);
            var orphanKey = $"{a}/uploads/{Guid.NewGuid()}/sin-confirmar.pdf";
            Files.Seed(orphanKey);

            // Registros de la organización con su nombre dentro (se purgan: llevan valores del tenant).
            db.AuditLogs.Add(AuditLog.Record("clients", client.Id, "UPDATE", a, oldValues: $"{{\"name\":\"{aOrg.Name}\"}}"));
            db.ActivityLogs.Add(ActivityLog.Create(a, "client", client.Id, "AO", "created"));
            db.Notifications.Add(Notification.Create(a, aOwner.UserId, "info", aOrg.Name));

            // Un usuario de B que tenía A como última organización activa.
            var lastActive = User.Create($"ultima-a-{Guid.NewGuid():N}@zentory.test", "Ultima", "A", OrgRole.Member);
            lastActive.SetLastActiveOrganization(a);
            db.Users.Add(lastActive);
            db.OrganizationMembers.Add(OrganizationMember.Create(b, lastActive.UserId, OrgRole.Member));

            // Facturación del owner de A: método de pago, factura, pago, crédito y cupón de la suscripción.
            db.PaymentMethods.Add(PaymentMethod.Create(aCustomer.Id, $"pm_{Guid.NewGuid():N}", "card"));
            db.BillingInvoices.Add(BillingInvoice.Create(a, aCustomer.Id, 10m));
            db.BillingPayments.Add(BillingPayment.Create(a, 10m, "USD", "succeeded"));
            db.BillingCredits.Add(BillingCredit.Create(a, 5m, "promo"));
            var coupon = CouponCode.Create($"PURGA{Guid.NewGuid():N}"[..12], "percent", 10m);
            db.CouponCodes.Add(coupon);
            db.CouponRedemptions.Add(CouponRedemption.Create(coupon.Id, a));

            // B: una nota con el Guid de A como texto y un archivo cuya clave lo menciona, más su proyecto con financieros.
            db.Clients.Add(Client.Create(b, "Cliente B", "Contacto B", notes: $"Referencia a {a}"));
            var bProject = Project.Create(b, Guid.NewGuid(), "Proyecto B", BillingType.FixedPrice, 10m);
            db.Projects.Add(bProject);
            db.ProjectFinancials.Add(ProjectFinancials.Initialize(bProject.Id));
            Files.Seed($"{b}/otros/{a}.pdf");

            // C, D y E: una fila y un archivo cada una.
            foreach (var org in new[] { cOrg, dOrg, eOrg })
            {
                db.Clients.Add(Client.Create(org.OrganizationId, $"Cliente {org.Name}", "Contacto"));
                Files.Seed($"{org.OrganizationId}/docs/archivo.pdf");
            }

            // A y D eliminadas hace 31 días; C hace 29. D se restaura justo antes de que se confirme su marca.
            aOrg.MarkDeleted(aOwner.UserId, Now.AddDays(-31));
            cOrg.MarkDeleted(cOwner.UserId, Now.AddDays(-29));
            dOrg.MarkDeleted(dOwner.UserId, Now.AddDays(-31));
            await db.SaveChangesAsync();

            _restoreBeforePurgeStart = dOrg.OrganizationId;
            _restoreOwner            = dOwner.UserId;

            return new Scenario(
                new OrgA(a, aOrg.Name, aOwner, project.Id, clientName, issued.Id, draft.Id, withPdf.Id, withoutPdf.Id,
                    pilaDocument.Id, pilaKey, projectFileKey, orphanKey, onlyInA, alsoInB, lastActive.UserId, aCustomer.Id),
                new SeededOrg(b, bOrg.Name, bOwner, bProject.Id),
                new SeededOrg(cOrg.OrganizationId, cOrg.Name, cOwner, Guid.Empty),
                new SeededOrg(dOrg.OrganizationId, dOrg.Name, dOwner, Guid.Empty),
                new SeededOrg(eOrg.OrganizationId, eOrg.Name, aOwner, Guid.Empty));
        }

        // Organización con owner, membresía y la facturación de su suscripción (como TenantBuilder).
        private (OrganizationEntity Organization, User Owner, BillingCustomer Customer) NewOrganization(
            ZentoryDbContext db, BillingPlan plan, string label)
        {
            var organization = OrganizationEntity.Create($"Org {label} {Guid.NewGuid():N}", LegalType.Empresa);
            var owner = User.Create($"owner-{label.ToLowerInvariant()}-{Guid.NewGuid():N}@zentory.test", "Owner", label,
                OrgRole.Owner, organizationId: organization.OrganizationId);
            organization.SetOwner(owner.UserId);
            var customer = BillingCustomer.Create(organization.OrganizationId, $"cus_{owner.UserId:N}", owner.Email, organization.Name);
            var subscription = Subscription.Create(owner.UserId, customer.Id, plan.Id);
            subscription.Activate($"sub_{owner.UserId:N}", Now.AddDays(-60), Now.AddDays(30));

            db.Organizations.Add(organization);
            db.Users.Add(owner);
            db.OrganizationMembers.Add(OrganizationMember.Create(organization.OrganizationId, owner.UserId, OrgRole.Owner));
            db.BillingCustomers.Add(customer);
            db.Subscriptions.Add(subscription);
            return (organization, owner, customer);
        }

        // ── Lecturas de comprobación (sin filtros: ven también lo eliminado) ────────────────────────────────────

        public async Task<OrganizationEntity?> OrganizationAsync(Guid id)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            return await db.Organizations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(o => o.OrganizationId == id);
        }

        public async Task<List<LegalDocumentArchive>> ArchiveAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            return await db.LegalDocumentArchives.AsNoTracking().ToListAsync();
        }

        public async Task<List<AuditLog>> PurgeAuditLogsAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            return await db.AuditLogs.AsNoTracking().Where(l => l.Action == OrganizationDataPurger.AuditAction).ToListAsync();
        }

        public async Task<List<string>> ClientNotesAsync(Guid organizationId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            return await db.Clients.IgnoreQueryFilters()
                .Where(c => c.OrganizationId == organizationId && c.Notes != null)
                .Select(c => c.Notes!)
                .ToListAsync();
        }

        public async Task<OwnerBilling> OwnerBillingAsync(OrgA a)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            var customerId = a.OwnerCustomerId;
            return new OwnerBilling(
                await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == a.Owner.UserId),
                await db.PaymentMethods.AsNoTracking().SingleOrDefaultAsync(p => p.CustomerId == customerId),
                await db.BillingCustomers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == customerId),
                await db.BillingInvoices.AsNoTracking().Where(i => i.CustomerId == customerId).ToListAsync(),
                await db.BillingPayments.AsNoTracking().Where(p => p.Amount == 10m && p.Status == "succeeded").ToListAsync(),
                await db.BillingCredits.AsNoTracking().Where(c => c.Type == "promo").ToListAsync(),
                await db.CouponRedemptions.AsNoTracking().ToListAsync());
        }

        // Filas de cualquier tipo del modelo con OrganizationId == id (sin la de Organization, que se mira aparte).
        public async Task<List<string>> TenantRowsAsync(Guid organizationId, bool ignoreOwnership = false)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            var rows = new List<string>();
            foreach (var type in db.Model.GetEntityTypes().Where(t => !t.IsOwned() && t.ClrType != typeof(OrganizationEntity)))
            {
                if (type.FindProperty(OrganizationDataCatalog.OrganizationIdProperty) is null)
                    continue;
                foreach (var row in await RowsAsync(db, type.ClrType))
                {
                    // El AuditLog de la purga es lo único que queda con el Id de A (RF-78 6).
                    if (row is AuditLog { Action: OrganizationDataPurger.AuditAction })
                        continue;
                    var entry = db.Entry(row);
                    if (entry.Property(OrganizationDataCatalog.OrganizationIdProperty).CurrentValue is Guid value && value == organizationId)
                        rows.Add(Describe(entry, ignoreOwnership));
                }
            }
            return rows.Order(StringComparer.Ordinal).ToList();
        }

        // Instantánea por organización: sus filas (todas las columnas) y sus archivos con su contenido. La de D no mira el
        // OwnershipStamp de su owner: restaurar D lo regenera a propósito (D-45), y eso no es obra de la purga.
        public async Task<Dictionary<Guid, List<string>>> FootprintsAsync(SeededOrg b, SeededOrg c, SeededOrg d, SeededOrg e)
        {
            var footprints = new Dictionary<Guid, List<string>>();
            foreach (var organization in new[] { b, c, d, e })
            {
                var footprint = await TenantRowsAsync(organization.Id, ignoreOwnership: organization == d);
                footprint.AddRange(Files.Objects.Values
                    .Where(o => o.Key.StartsWith($"{organization.Id}/", StringComparison.Ordinal))
                    .Select(o => $"file:{o.Key}:{Convert.ToBase64String(o.Content)}")
                    .Order(StringComparer.Ordinal));
                footprints[organization.Id] = footprint;
            }
            return footprints;
        }

        // Toda columna Guid/Guid? de todo el modelo que valga id: tipo, propiedad y la clave de la fila.
        public async Task<List<UuidOccurrence>> UuidOccurrencesAsync(Guid id)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            var found = new List<UuidOccurrence>();
            foreach (var type in db.Model.GetEntityTypes().Where(t => !t.IsOwned()))
            {
                var uuids = type.GetProperties().Where(p => p.ClrType == typeof(Guid) || p.ClrType == typeof(Guid?)).ToList();
                if (uuids.Count == 0)
                    continue;
                foreach (var row in await RowsAsync(db, type.ClrType))
                {
                    var entry = db.Entry(row);
                    foreach (var property in uuids.Where(p => entry.Property(p.Name).CurrentValue is Guid value && value == id))
                        found.Add(new UuidOccurrence(type.ClrType.Name, property.Name,
                            entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id")?.CurrentValue as Guid?));
                }
            }
            return found;
        }

        private static string Describe(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, bool ignoreOwnership)
            => entry.Metadata.ClrType.Name + "|" + string.Join(";", entry.Properties
                .Where(p => !(ignoreOwnership && entry.Entity is User
                    && p.Metadata.Name is nameof(User.OwnershipStamp) or nameof(User.UpdatedAt)))
                .OrderBy(p => p.Metadata.Name, StringComparer.Ordinal)
                .Select(p => $"{p.Metadata.Name}={Convert.ToString(p.CurrentValue, System.Globalization.CultureInfo.InvariantCulture)}"));

        private static readonly MethodInfo RowsMethod =
            typeof(PurgeHost).GetMethod(nameof(RowsOfAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

        private static Task<List<object>> RowsAsync(DbContext db, Type type)
            => (Task<List<object>>)RowsMethod.MakeGenericMethod(type).Invoke(null, [db])!;

        private static async Task<List<object>> RowsOfAsync<TEntity>(DbContext db) where TEntity : class
            => (await db.Set<TEntity>().IgnoreQueryFilters().ToListAsync()).Cast<object>().ToList();

        public async ValueTask DisposeAsync()
        {
            await _host.DisposeAsync();
            await _base.DisposeAsync();
        }

        // ── Dobles ──────────────────────────────────────────────────────────────────────────────────────────────

        // Almacenamiento en memoria que, con FailDeletes, falla al borrar (CA-107, "Orden"). No sobrescribe el borrado
        // por prefijo: la implementación por defecto de IStorageService valida el prefijo y llama a este DeleteAsync.
        private sealed class FlakyStorage(PurgeHost owner) : IStorageService
        {
            public Task<PresignedUploadUrl> GeneratePresignedUploadUrlAsync(string key, string contentType, TimeSpan expiresIn, CancellationToken ct = default)
                => owner.Files.GeneratePresignedUploadUrlAsync(key, contentType, expiresIn, ct);

            public Task<string> GeneratePresignedDownloadUrlAsync(string key, TimeSpan expiresIn, CancellationToken ct = default)
                => owner.Files.GeneratePresignedDownloadUrlAsync(key, expiresIn, ct);

            public Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default)
                => owner.Files.UploadAsync(key, content, contentType, ct);

            public Task DeleteAsync(string key, CancellationToken ct = default)
                => owner.FailDeletes
                    ? throw new IOException("El almacenamiento no responde (doble de prueba).")
                    : owner.Files.DeleteAsync(key, ct);

            public Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct = default)
                => owner.Files.ListKeysAsync(prefix, ct);

            public Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default)
                => owner.Files.CopyAsync(sourceKey, destinationKey, ct);
        }

        // Purgador que marca el inicio y se detiene antes de borrar nada (CA-107, "Bloqueo frente a restore").
        private sealed class StoppedPurger(OrganizationDataPurger inner) : IOrganizationDataPurger
        {
            public Task<IReadOnlyList<Guid>> StartDuePurgesAsync(DateTime cutoffUtc, DateTime nowUtc, CancellationToken ct = default)
                => inner.StartDuePurgesAsync(cutoffUtc, nowUtc, ct);

            public Task<OrganizationPurgeReport> PurgeAsync(Guid organizationId, DateTime nowUtc, CancellationToken ct = default)
                => throw new InvalidOperationException("Job detenido antes de borrar (doble de prueba).");

            public Task<int> DeleteExpiredArchiveAsync(DateTime nowUtc, CancellationToken ct = default)
                => inner.DeleteExpiredArchiveAsync(nowUtc, ct);
        }

        // Restaura D en otro contexto justo cuando el job va a confirmar su PurgeStartedAt: el guardado del job lleva el
        // sello viejo y falla por concurrencia (CA-107, D). Una sola vez.
        private sealed class RestoreBeforePurgeStart(PurgeHost owner) : SaveChangesInterceptor
        {
            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                if (owner._restoreBeforePurgeStart is { } target && eventData.Context is { } context
                    && context.ChangeTracker.Entries<OrganizationEntity>().Any(e => e.State == EntityState.Modified
                        && e.Entity.OrganizationId == target && e.Entity.PurgeStartedAt is not null))
                {
                    owner._restoreBeforePurgeStart = null;
                    await OrganizationRestoreApi.RestoreDirectlyAsync(owner.Services, target, owner._restoreOwner!.Value);
                }
                return await base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }
    }
}
