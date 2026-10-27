using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Seeding.Demo;

/// <summary>
/// Un escenario demo por organización (PLAN-0018 RF-56 a, D-5). Inserta por ID fijo o clave natural, nunca modifica
/// filas existentes y toma todas sus fechas del día de referencia de <see cref="DemoSeedContext"/>.
/// </summary>
public interface IDemoScenario
{
    string Name { get; }

    Task SeedAsync(DemoSeedContext context, CancellationToken ct = default);
}

/// <summary>Persona demo con ID fijo. El email es la clave natural si la fila ya existe con otro ID.</summary>
public sealed record DemoPerson(Guid Id, string Email, string FirstName, string LastName);

/// <summary>
/// Personas compartidas entre escenarios (§3.0.A). Todas usan <see cref="EmailDomain"/> salvo la persona principal,
/// que conserva su email para no romper el recorrido E2E ni los tokens de desarrollo.
/// </summary>
public static class DemoPeople
{
    // Dominio reservado para pruebas: ningún correo demo puede llegar a un buzón real (RF-56 d).
    public const string EmailDomain = "demo.zentory.test";

    public static string Email(string localPart) => $"{localPart}@{EmailDomain}";

    public static readonly DemoPerson Principal = new(
        new Guid("b2000000-0000-0000-0000-000000000002"), "dev@zentorysoftware.com", "Camila", "Perez");

    public static readonly DemoPerson ConstructionOwner = new(
        new Guid("b2000000-0000-0000-0000-000000000005"), Email("ricardo.mendez"), "Ricardo", "Méndez");

    public static readonly DemoPerson AgencyAdmin = new(
        new Guid("b2000000-0000-0000-0000-000000000006"), Email("andres.rojas"), "Andrés", "Rojas");

    public static readonly DemoPerson AgencyMember = new(
        new Guid("b2000000-0000-0000-0000-000000000007"), Email("sara.molina"), "Sara", "Molina");

    // Colaboradora del portal: la misma persona trabaja para la Agencia y para la Constructora (switch del portal).
    public const string PortalCollaboratorName = "Laura Patiño";
    public static readonly string PortalCollaboratorEmail = Email("laura.patino");
}

/// <summary>
/// IDs fijos de las filas hijas, derivados de la organización y de una clave legible. La misma clave da siempre el
/// mismo ID, así que una segunda corrida (otro día) encuentra la fila y no la vuelve a insertar ni mueve sus fechas.
/// </summary>
public static class DemoIds
{
    public static Guid For(Guid scope, string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope:N}:{key}"));
        var guid  = bytes.AsSpan(0, 16).ToArray();

        // Formato de UUID versión 5 (basado en nombre) y variante RFC 4122.
        guid[6] = (byte)((guid[6] & 0x0F) | 0x50);
        guid[8] = (byte)((guid[8] & 0x3F) | 0x80);
        return new Guid(guid, bigEndian: true);
    }
}

/// <summary>Estados de una factura demo que la UI distingue (borrador, enviada, pagada, vencida, pago parcial).</summary>
public enum DemoInvoiceState { Draft, Sent, Paid, Overdue, Partial }

/// <summary>
/// Fábricas de filas demo de una organización: cada fila sale con su ID fijo (<see cref="DemoIds"/>) y con fechas
/// relativas al día de referencia. Solo construye: insertar le toca a <see cref="DemoSeedContext"/>.
/// </summary>
public sealed class DemoOrganizationSeed
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-CO");

    public DemoOrganizationSeed(DemoSeedContext context, Guid organizationId)
    {
        Context        = context;
        OrganizationId = organizationId;
    }

    public DemoSeedContext Context { get; }

    public Guid OrganizationId { get; }

    public Guid Id(string key) => DemoIds.For(OrganizationId, key);

    // Etiqueta de periodo de los cobros ("sep 2026"), del mes del día relativo.
    public string Period(int offset) => Context.Day(offset).ToString("MMM yyyy", Spanish).Replace(".", string.Empty);

    public Client Client(string key, string name, string contactName, string contactLocalPart, string phone, string city, string nit)
        => Context.WithId(
            Domain.Entities.Client.Create(OrganizationId, name, contactName, DemoPeople.Email(contactLocalPart), phone, city, nit: nit),
            Id($"client:{key}"));

    public Collaborator Collaborator(
        string key, string name, string type, string role, string currency = "COP",
        decimal? hourlyRate = null, decimal? monthlyRate = null, string? email = null)
        => Context.WithId(
            Domain.Entities.Collaborator.Create(OrganizationId, name, type, currency, email: email, role: role,
                hourlyRate: hourlyRate, monthlyRate: monthlyRate),
            Id($"collaborator:{key}"));

    public Project Project(
        Guid id, Guid clientId, string name, BillingType billingType, decimal contractValue, string currency,
        int hoursTotal, int startOffset, int endOffset, string workType,
        ProjectStatus status = ProjectStatus.Active, int hoursUsed = 0)
    {
        var project = Domain.Entities.Project.Create(OrganizationId, clientId, name, billingType, contractValue, currency,
            hoursTotal, Context.UtcDay(startOffset), Context.UtcDay(endOffset), type: workType, id: id);
        if (status != ProjectStatus.Active) project.ChangeStatus(status);
        if (hoursUsed > 0) project.LogHours(hoursUsed);
        return project;
    }

    public ProjectCollaborator Assign(Guid projectId, Collaborator collaborator, decimal? rateBilled = null)
        => Context.WithId(
            ProjectCollaborator.Assign(OrganizationId, projectId, collaborator.Id,
                collaborator.HourlyRate ?? collaborator.MonthlyRate ?? 0m, collaborator.Currency, rateBilled, collaborator.Role),
            Id($"assignment:{projectId:N}:{collaborator.Id:N}"));

    /// <summary>Horas: <c>pending</c>, <c>approved</c> o <c>billed</c>. Sin colaborador, las registra el owner.</summary>
    public TimeEntry Time(
        string key, Guid projectId, Collaborator? collaborator, int offset, decimal hours, decimal rate,
        string description, string status = "pending", string currency = "COP", Guid? createdBy = null)
    {
        var entry = Context.WithId(
            TimeEntry.Create(OrganizationId, projectId, Context.Day(offset), hours, rate, currency,
                billable: true, rateBilled: rate, description: description,
                collaboratorId: collaborator?.Id, createdBy: createdBy),
            Id($"time:{key}"));
        if (status is "approved" or "billed") entry.Approve();
        if (status == "billed") entry.MarkBilled();
        return entry;
    }

    public Invoice Invoice(
        string number, Guid clientId, Guid? projectId, string currency, string documentType,
        int issuedOffset, int dueOffset, DemoInvoiceState state, params (string Description, decimal Quantity, decimal UnitPrice)[] items)
    {
        var invoice = Context.WithId(
            Domain.Entities.Invoice.Create(OrganizationId, clientId, number, Context.Day(dueOffset), currency, documentType, projectId),
            Id($"invoice:{number}"));
        Context.Set(invoice, i => i.IssuedAt, Context.Day(issuedOffset));

        for (var index = 0; index < items.Length; index++)
            invoice.AddItem(InvoiceItem.Create(OrganizationId, invoice.Id, items[index].Description,
                items[index].Quantity, items[index].UnitPrice, (short)index));

        if (state == DemoInvoiceState.Draft) return invoice;

        invoice.MarkAsSent();
        Context.Set(invoice, i => i.SentAt, Context.UtcDay(issuedOffset, 15));
        switch (state)
        {
            case DemoInvoiceState.Paid:    invoice.RecordPayment(invoice.Total); break;
            case DemoInvoiceState.Partial: invoice.RecordPayment(Math.Round(invoice.Total * 0.4m, 0)); break;
            case DemoInvoiceState.Overdue: invoice.MarkAsOverdue(); break;
        }
        return invoice;
    }

    // Pago registrado de una factura pagada o con pago parcial, por lo que ya abonó.
    public InvoicePayment Payment(Invoice invoice, int paidOffset, string method = "transfer")
        => Context.WithId(
            InvoicePayment.Create(OrganizationId, invoice.Id, invoice.AmountPaid, invoice.Currency, Context.Day(paidOffset),
                method, reference: $"TRX-{invoice.InvoiceNumber}"),
            Id($"payment:{invoice.InvoiceNumber}"));

    /// <summary>Propuesta en <c>draft</c>, <c>sent</c>, <c>viewed</c>, <c>accepted</c>, <c>rejected</c> o <c>expired</c>.</summary>
    public Proposal Proposal(
        string key, Guid clientId, string title, string currency, string status, int sentOffset, int validityDays,
        params (string Description, decimal UnitPrice)[] items)
    {
        var proposal = Context.WithId(
            Domain.Entities.Proposal.Create(OrganizationId, clientId, title, currency,
                expiresAt: Context.UtcDay(sentOffset + validityDays)),
            Id($"proposal:{key}"));
        for (var index = 0; index < items.Length; index++)
            proposal.AddItem(ProposalItem.Create(OrganizationId, proposal.Id, items[index].Description, 1m, items[index].UnitPrice, (short)index));

        if (status == "draft") return proposal;

        proposal.MarkAsSent();
        Context.Set(proposal, p => p.SentAt, Context.UtcDay(sentOffset, 11));
        if (status != "sent")
        {
            proposal.RecordView();
            Context.Set(proposal, p => p.FirstViewedAt, Context.UtcDay(sentOffset + 1, 16));
            Context.Set(proposal, p => p.LastViewedAt, Context.UtcDay(sentOffset + 2, 9));
        }

        switch (status)
        {
            case "accepted":
                proposal.MarkAsAccepted();
                Context.Set(proposal, p => p.AcceptedAt, Context.UtcDay(sentOffset + 4, 10));
                break;
            case "rejected":
                proposal.MarkAsRejected();
                Context.Set(proposal, p => p.RejectedAt, Context.UtcDay(sentOffset + 6, 10));
                break;
            case "expired":
                proposal.ForceStatus("expired");
                break;
        }
        return proposal;
    }

    public ProjectMilestone Milestone(string key, Guid projectId, string name, decimal value, int dueOffset, string status)
        => Context.WithId(
            ProjectMilestone.Create(OrganizationId, projectId, name, value, Context.Day(dueOffset), status),
            Id($"milestone:{key}"));

    public ProjectTask Task(
        string key, Guid projectId, string title, string status, string priority, Collaborator? assignee, int dueOffset,
        ProjectMilestone? milestone = null, int? startOffset = null, int hours = 0, params ProjectTask[] dependsOn)
        => Context.WithId(
            ProjectTask.Create(OrganizationId, projectId, title, status, priority, assigneeId: assignee?.Id,
                dueDate: Context.Day(dueOffset), milestoneId: milestone?.Id,
                startDate: startOffset is { } start ? Context.Day(start) : null, hours: hours,
                dependencies: [.. dependsOn.Select(t => t.Id.ToString())]),
            Id($"task:{key}"));

    public ProjectDeliverable Deliverable(
        string key, Guid projectId, string name, string type, int dueOffset, ProjectMilestone? milestone, string status,
        string? approvedBy = null)
        => Context.WithId(
            ProjectDeliverable.Create(OrganizationId, projectId, name, type, Context.Day(dueOffset), milestone?.Id, status, approvedBy),
            Id($"deliverable:{key}"));

    public ProjectBillingEntry Billing(
        string key, Guid projectId, string concept, decimal rate, int dueOffset, int? hours, decimal? amount,
        string status, string? invoiceRef = null)
        => Context.WithId(
            ProjectBillingEntry.Create(OrganizationId, projectId, concept, Period(dueOffset), rate, Context.Day(dueOffset),
                hours, amount, status, invoiceRef),
            Id($"billing:{key}"));

    public ProjectFile File(string key, Guid projectId, string name, string fileType, string size, string uploadedBy, int offset)
        => Context.WithId(
            ProjectFile.Create(OrganizationId, projectId, name, fileType, size, uploadedBy,
                $"{OrganizationId}/projects/{projectId}/seed/{key}.{fileType}", Context.UtcDay(offset, 10)),
            Id($"file:{key}"));

    public ProjectActivityLog Activity(string key, Guid projectId, string initials, string action, string module, int offset, int hour)
        => Context.WithId(
            ProjectActivityLog.Create(OrganizationId, projectId, initials, action, module, Context.UtcDay(offset, hour)),
            Id($"activity:{key}"));

    public CashFlowEntry Income(Invoice invoice, string payer, int offset)
        => Context.WithId(
            CashFlowEntry.CreateFromInvoice(OrganizationId, invoice.Id, $"Pago {invoice.InvoiceNumber} — {payer}",
                invoice.AmountPaid, invoice.Currency, 1m, invoice.AmountPaid, Context.Day(offset)),
            Id($"cash:income:{invoice.InvoiceNumber}"));

    // Un gasto por mes en los últimos meses (cada 30 días hacia atrás desde el día de referencia).
    public IEnumerable<CashFlowEntry> MonthlyExpense(
        string key, string description, decimal amount, string currency, short categoryId, int months, int firstOffset = -2)
        => Enumerable.Range(0, months).Select(month => Context.WithId(
            CashFlowEntry.CreateManual(OrganizationId, "expense", description, amount, currency, 1m, amount,
                Context.Day(firstOffset - (30 * month)), categoryId: categoryId),
            Id($"cash:expense:{key}:{month}")));
}
