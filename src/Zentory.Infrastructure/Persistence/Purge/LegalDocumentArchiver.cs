using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Options;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Purge;

// Paso 3 de la purga (PLAN-0018 RF-79; ADR purga §5; C-7; SR-0018.28): copia a LegalDocumentArchive las facturas y
// cuentas de cobro emitidas de la organización, mientras sus filas y archivos siguen bajo {orgId}/. Idempotente por
// (SourceType, SourceId): una corrida retomada no duplica. Los borradores no se archivan.
//
// El Snapshot se arma campo a campo con lo que figura en el documento, nunca serializando la entidad: así no entran
// RateCost, márgenes, notas internas ni emails de contacto (SR-0018.28 d). Por la decisión C-7, las facturas llevan
// además sus pagos y su estado, y su PdfStorageKey queda nulo hasta que exista el generador de PDF de factura.
public sealed class LegalDocumentArchiver
{
    public const string DraftStatus = "draft";

    // Facturas que se archivan (RF-79): factura (empresa) y cuenta de cobro (freelance), no la nota crédito.
    public static readonly IReadOnlyList<string> ArchivedInvoiceTypes = ["factura", "cobro"];

    private const string PdfContentType = "application/pdf";

    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

    private readonly ZentoryDbContext           _db;
    private readonly IStorageService            _storage;
    private readonly IPayoutInvoicePdfGenerator _pdf;
    private readonly RetentionOptions           _retention;

    public LegalDocumentArchiver(
        ZentoryDbContext            db,
        IStorageService             storage,
        IPayoutInvoicePdfGenerator  pdf,
        IOptions<RetentionOptions>  retention)
    {
        _db        = db;
        _storage   = storage;
        _pdf       = pdf;
        _retention = retention.Value;
    }

    // Archiva lo que falte de la organización y devuelve cuántos documentos archivó en esta llamada.
    public async Task<int> ArchiveAsync(Guid organizationId, DateTime nowUtc, CancellationToken ct = default)
    {
        var organization = await _db.Organizations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(o => o.OrganizationId == organizationId, ct);
        var issuer = await IssuerAsync(organization, ct);

        var archived = await ArchiveInvoicesAsync(organizationId, issuer, nowUtc, ct);
        archived += await ArchivePayoutInvoicesAsync(organizationId, issuer, nowUtc, ct);
        return archived;
    }

    // ── Facturas (C-7) ──────────────────────────────────────────────────────────────────────────────────────────

    private async Task<int> ArchiveInvoicesAsync(Guid organizationId, Issuer issuer, DateTime nowUtc, CancellationToken ct)
    {
        // También las emitidas con borrado lógico: el documento ya salió y la retención es legal (RF-79).
        var invoices = await _db.Invoices.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.OrganizationId == organizationId
                && i.Status != DraftStatus
                && ArchivedInvoiceTypes.Contains(i.DocumentType))
            .ToListAsync(ct);
        var pending = await PendingAsync(LegalDocumentSourceType.Invoice, invoices.Select(i => i.Id), ct);
        if (pending.Count == 0)
            return 0;

        var toArchive = invoices.Where(i => pending.Contains(i.Id)).ToList();
        var ids       = toArchive.Select(i => i.Id).ToList();
        var clientIds = toArchive.Select(i => i.ClientId).Distinct().ToList();

        var items = (await _db.InvoiceItems.AsNoTracking()
                .Where(item => item.OrganizationId == organizationId && ids.Contains(item.InvoiceId))
                .ToListAsync(ct))
            .ToLookup(item => item.InvoiceId);
        var payments = (await _db.InvoicePayments.AsNoTracking()
                .Where(payment => payment.OrganizationId == organizationId && ids.Contains(payment.InvoiceId))
                .ToListAsync(ct))
            .ToLookup(payment => payment.InvoiceId);
        var clients = await _db.Clients.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        foreach (var invoice in toArchive)
        {
            var issuedAt = invoice.IssuedAt.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var snapshot = InvoiceSnapshot(invoice, issuer, clients.GetValueOrDefault(invoice.ClientId),
                items[invoice.Id], payments[invoice.Id]);

            _db.LegalDocumentArchives.Add(LegalDocumentArchive.Create(
                LegalDocumentSourceType.Invoice, invoice.Id, invoice.InvoiceNumber, issuedAt, issuer.TaxId,
                issuer.LegalName, invoice.DianCufe, snapshot, pdfStorageKey: null, nowUtc, _retention.LegalDocumentsYears));
        }

        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return toArchive.Count;
    }

    private static string InvoiceSnapshot(
        Invoice                     invoice,
        Issuer                      issuer,
        Client?                     client,
        IEnumerable<InvoiceItem>    items,
        IEnumerable<InvoicePayment> payments)
        => JsonSerializer.Serialize(new
        {
            header = new
            {
                documentType = invoice.DocumentType,
                number       = invoice.InvoiceNumber,
                issuedAt     = invoice.IssuedAt,
                dueAt        = invoice.DueAt,
                currency     = invoice.Currency,
                exchangeRate = invoice.ExchangeRate,
                paymentTerms = invoice.PaymentTerms,
                issuer       = new { legalName = issuer.LegalName, taxId = issuer.TaxId, issuer.Address, issuer.City },
            },
            // Receptor tal como figura en la factura: sin el email, el teléfono ni las notas del cliente.
            client = client is null
                ? null
                : new { name = client.Name, taxId = client.Nit, address = client.Address, city = client.City },
            items = items.OrderBy(i => i.SortOrder).Select(i => new
            {
                description = i.Description,
                quantity    = i.Quantity,
                unitId      = i.UnitId,
                unitPrice   = i.UnitPrice,
                discountPct = i.DiscountPct,
                taxTypeId   = i.TaxTypeId,
                total       = i.Total,
            }),
            totals = new
            {
                subtotal          = invoice.Subtotal,
                taxAmount         = invoice.TaxAmount,
                total             = invoice.Total,
                amountPaid        = invoice.AmountPaid,
                amountDue         = invoice.AmountDue,
                totalBaseCurrency = invoice.TotalBaseCurrency,
            },
            // Pagos registrados (C-7), sin sus notas internas ni quién los registró.
            payments = payments.OrderBy(p => p.PaidAt).Select(p => new
            {
                amount    = p.Amount,
                currency  = p.Currency,
                paidAt    = p.PaidAt,
                method    = p.PaymentMethod,
                reference = p.Reference,
            }),
            status = invoice.Status,
            dian   = invoice.DianCufe is null && invoice.DianStatus is null
                ? null
                : new { cufe = invoice.DianCufe, status = invoice.DianStatus, submittedAt = invoice.DianSubmittedAt },
        }, SnapshotJson);

    // ── Cuentas de cobro ────────────────────────────────────────────────────────────────────────────────────────

    private async Task<int> ArchivePayoutInvoicesAsync(Guid organizationId, Issuer company, DateTime nowUtc, CancellationToken ct)
    {
        var payouts = await _db.CollaboratorPayoutInvoices.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && p.Status != DraftStatus)
            .ToListAsync(ct);
        var pending = await PendingAsync(LegalDocumentSourceType.CollaboratorPayoutInvoice, payouts.Select(p => p.Id), ct);
        if (pending.Count == 0)
            return 0;

        var toArchive       = payouts.Where(p => pending.Contains(p.Id)).ToList();
        var collaboratorIds = toArchive.Select(p => p.CollaboratorId).Distinct().ToList();
        var collaborators   = await _db.Collaborators.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && collaboratorIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        foreach (var payout in toArchive)
        {
            // Quien emite la cuenta de cobro es el colaborador; la organización es el receptor.
            var collaborator = collaborators.GetValueOrDefault(payout.CollaboratorId);
            var issuedAt     = payout.GeneratedAt ?? payout.SentAt ?? payout.SignedAt ?? payout.CreatedAt;
            var pdfKey       = await ArchivePdfAsync(payout, collaborator, company, issuedAt, ct);

            _db.LegalDocumentArchives.Add(LegalDocumentArchive.Create(
                LegalDocumentSourceType.CollaboratorPayoutInvoice, payout.Id, PayoutDocumentNumber(payout),
                DateTime.SpecifyKind(issuedAt, DateTimeKind.Utc), collaborator?.IdNumber ?? string.Empty,
                collaborator?.Name ?? string.Empty, dianCufe: null, PayoutSnapshot(payout, collaborator, company),
                pdfKey, nowUtc, _retention.LegalDocumentsYears));

            // Una fila por documento: si la corrida se corta, el PDF copiado ya tiene su fila y no se vuelve a copiar.
            await _db.SaveChangesAsync(ct);
        }

        _db.ChangeTracker.Clear();
        return toArchive.Count;
    }

    // Copia el PDF existente a legal-archive/{Guid}.pdf; si la emitida no tiene PDF guardado (o ya no está en el
    // almacenamiento), lo genera con el generador existente (ADR purga §5).
    private async Task<string> ArchivePdfAsync(
        CollaboratorPayoutInvoice payout, Collaborator? collaborator, Issuer company, DateTime issuedAt, CancellationToken ct)
    {
        var destination = LegalDocumentArchive.NewPdfStorageKey();

        if (payout.StorageKey is { Length: > 0 } source
            && (await _storage.ListKeysAsync(source, ct)).Contains(source, StringComparer.Ordinal))
        {
            await _storage.CopyAsync(source, destination, ct);
            return destination;
        }

        var bytes = _pdf.Generate(new PayoutInvoicePdfModel(
            company.CompanyName,
            collaborator?.Name ?? string.Empty,
            collaborator?.IdNumber,
            payout.Period,
            payout.Concept,
            payout.Amount,
            payout.Currency,
            issuedAt,
            LegalName:    company.LegalName,
            Nit:          company.TaxId,
            Address:      company.Address,
            City:         company.City,
            SignedByName: payout.SignedByName,
            SignedAt:     payout.SignedAt));
        using var stream = new MemoryStream(bytes);
        await _storage.UploadAsync(destination, stream, PdfContentType, ct);
        return destination;
    }

    private static string PayoutDocumentNumber(CollaboratorPayoutInvoice payout) => $"CC-{payout.Period}";

    private static string PayoutSnapshot(CollaboratorPayoutInvoice payout, Collaborator? collaborator, Issuer company)
        => JsonSerializer.Serialize(new
        {
            header = new
            {
                number   = PayoutDocumentNumber(payout),
                period   = payout.Period,
                currency = payout.Currency,
                issuer   = new { name = collaborator?.Name, idNumber = collaborator?.IdNumber },
                recipient = new { legalName = company.LegalName, taxId = company.TaxId, company.Address, company.City },
            },
            concept        = payout.Concept,
            amount         = payout.Amount,
            declaredAmount = payout.DeclaredAmount,
            source         = payout.Source,
            status         = payout.Status,
            generatedAt    = payout.GeneratedAt,
            sentAt         = payout.SentAt,
            signedByName   = payout.SignedByName,
            signedAt       = payout.SignedAt,
        }, SnapshotJson);

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Ids que aún no están archivados con ese SourceType.
    private async Task<HashSet<Guid>> PendingAsync(string sourceType, IEnumerable<Guid> sourceIds, CancellationToken ct)
    {
        var ids = sourceIds.ToList();
        if (ids.Count == 0)
            return [];
        var existing = await _db.LegalDocumentArchives.AsNoTracking()
            .Where(a => a.SourceType == sourceType && ids.Contains(a.SourceId))
            .Select(a => a.SourceId)
            .ToListAsync(ct);
        return ids.Except(existing).ToHashSet();
    }

    // Emisor tal como figura en los documentos: el perfil de la organización, con su nombre como respaldo.
    private async Task<Issuer> IssuerAsync(Organization organization, CancellationToken ct)
    {
        var profile = await _db.OrganizationSettings.AsNoTracking()
            .Where(s => s.OrganizationId == organization.OrganizationId && OrgSettingKey.Profile.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        string? Value(string key) => profile.GetValueOrDefault(key) is { Length: > 0 } value ? value : null;

        return new Issuer(
            organization.Name,
            Value(OrgSettingKey.ProfileLegalName) ?? organization.Name,
            Value(OrgSettingKey.ProfileNit) ?? string.Empty,
            Value(OrgSettingKey.ProfileAddress),
            Value(OrgSettingKey.ProfileCity));
    }

    private sealed record Issuer(string CompanyName, string LegalName, string TaxId, string? Address, string? City);
}
