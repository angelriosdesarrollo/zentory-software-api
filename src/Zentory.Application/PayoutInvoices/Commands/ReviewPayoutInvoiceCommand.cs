using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Projects;
using Zentory.Application.Projects.Queries;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PayoutInvoices.Commands;

// Mismo patrón que VerifyPilaCommand: la empresa aprueba o rechaza una cuenta de cobro
// que ya tiene documento (generada+firmada, o subida). Un rechazo exige motivo.
// Al aprobar, ProjectId indica a qué proyecto (o null = gasto de empresa) se imputa el
// pago — genera automáticamente el gasto correspondiente en ProjectExpenseStore. Para
// hourly_contractor el ProjectId se ignora: el reparto se deriva de los TimeEntry reales
// (puede tocar varios proyectos), la cuenta de cobro aprobada tiene prioridad sobre el
// estimado por horas — ver comentario en el handler.
public record ReviewPayoutInvoiceCommand(
    Guid    Id,
    bool    Approved,
    string? Notes = null,
    Guid?   ProjectId = null) : IRequest;

public sealed class ReviewPayoutInvoiceCommandValidator : AbstractValidator<ReviewPayoutInvoiceCommand>
{
    public ReviewPayoutInvoiceCommandValidator()
    {
        RuleFor(x => x.Notes)
            .NotEmpty()
            .When(x => !x.Approved)
            .WithMessage("Debes indicar el motivo del rechazo.");
    }
}

public sealed class ReviewPayoutInvoiceCommandHandler : IRequestHandler<ReviewPayoutInvoiceCommand>
{
    private static readonly string[] ReviewableStatuses = ["generated", "signed", "uploaded_manually"];

    private readonly ICollaboratorPayoutInvoiceRepository _invoices;
    private readonly ICollaboratorRepository               _collaborators;
    private readonly IOrganizationRepository               _organizations;
    private readonly IEmailService                         _email;
    private readonly IApplicationSettings                  _settings;
    private readonly IUnitOfWork                            _uow;
    private readonly ITenantContext                         _tenant;
    private readonly ProjectExpenseStore                    _expenses;
    private readonly ITimeEntryRepository                   _timeEntries;

    public ReviewPayoutInvoiceCommandHandler(
        ICollaboratorPayoutInvoiceRepository invoices,
        ICollaboratorRepository               collaborators,
        IOrganizationRepository               organizations,
        IEmailService                         email,
        IApplicationSettings                  settings,
        IUnitOfWork                           uow,
        ITenantContext                        tenant,
        ProjectExpenseStore                    expenses,
        ITimeEntryRepository                   timeEntries)
    {
        _invoices      = invoices;
        _collaborators = collaborators;
        _organizations = organizations;
        _email         = email;
        _settings      = settings;
        _uow           = uow;
        _tenant        = tenant;
        _expenses      = expenses;
        _timeEntries   = timeEntries;
    }

    public async Task Handle(ReviewPayoutInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.GetByIdAsync(request.Id, cancellationToken);
        if (invoice is null || invoice.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("CollaboratorPayoutInvoice", request.Id);

        if (!ReviewableStatuses.Contains(invoice.Status))
            throw new ConflictException("NOT_REVIEWABLE", "Esta cuenta de cobro todavía no tiene documento para revisar.");

        if (request.Approved)
        {
            invoice.Approve();
            await _invoices.UpdateAsync(invoice, cancellationToken);

            var collaborator = await _collaborators.GetByIdAsync(invoice.CollaboratorId, cancellationToken);
            if (collaborator is not null)
            {
                collaborator.UpdatePayoutInvoiceStatus(invoice.Status, invoice.Period);
                await _collaborators.UpdateAsync(collaborator, cancellationToken);
            }

            await _uow.SaveChangesAsync(cancellationToken);

            if (collaborator?.Type == "hourly_contractor")
            {
                // La cuenta de cobro aprobada tiene prioridad sobre el estimado por horas: el
                // monto real pagado reemplaza (no se suma a) el costo estimado de esas mismas
                // horas en GetProfitabilityStatsQuery. Se deriva el reparto por proyecto de los
                // mismos TimeEntry 'approved' que ya usa GetSuggestedPayoutAmountQuery (puede
                // tocar varios proyectos a la vez, por eso no se usa request.ProjectId aquí).
                var (from, to) = ParsePeriodRange(invoice.Period);
                var entries = await _timeEntries.ListAsync(
                    _tenant.OrganizationId,
                    from: from,
                    to: to,
                    status: "approved",
                    projectId: null,
                    collaboratorId: invoice.CollaboratorId,
                    ct: cancellationToken);

                if (entries.Count > 0)
                {
                    foreach (var group in entries.GroupBy(e => e.ProjectId))
                    {
                        var hours = group.Sum(e => e.Hours);
                        _expenses.Add(new ProjectExpenseDto(
                            Id:                    Guid.NewGuid(),
                            ProjectId:             group.Key,
                            Date:                  DateTime.UtcNow.ToString("yyyy-MM-dd"),
                            Category:              "subcontratista",
                            Description:           $"Cuenta de cobro {invoice.Period} — horas facturadas ({hours}h)",
                            Amount:                group.Sum(e => e.Hours * e.RateCost),
                            Currency:              group.First().Currency,
                            Vendor:                collaborator.Name,
                            Billable:              false,
                            Status:                "aprobado",
                            CreatedBy:             _tenant.UserInitials,
                            Source:                "payout_invoice",
                            SourcePayoutInvoiceId: invoice.Id));
                    }

                    // Saca esas horas del cálculo estimado (GetProfitabilityStatsQuery excluye
                    // 'billed') ahora que su costo real ya quedó registrado arriba — evita el
                    // doble conteo sin tener que tocar Project.HoursUsed (que sigue reflejando
                    // progreso real del proyecto, no si ya se pagó o no).
                    await _timeEntries.BatchMarkBilledAsync(entries.Select(e => e.Id), cancellationToken);
                }
                else
                {
                    // Sin horas aprobadas que respalden el monto (ej. cuenta de cobro autoservicio
                    // sin time entries) — no hay a qué proyecto imputarlo con certeza, queda como
                    // gasto de empresa para no perder el registro del pago real.
                    _expenses.Add(new ProjectExpenseDto(
                        Id:                    Guid.NewGuid(),
                        ProjectId:             null,
                        Date:                  DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        Category:              "subcontratista",
                        Description:           $"Cuenta de cobro {invoice.Period} — {invoice.Concept}",
                        Amount:                invoice.Amount,
                        Currency:              invoice.Currency,
                        Vendor:                collaborator.Name,
                        Billable:              false,
                        Status:                "aprobado",
                        CreatedBy:             _tenant.UserInitials,
                        Source:                "payout_invoice",
                        SourcePayoutInvoiceId: invoice.Id));
                }
            }
            else
            {
                // fixed_contractor (u otro tipo): no existe ningún otro lugar donde su costo se
                // impute a un proyecto, así que el gasto creado aquí es la única fuente de
                // atribución que tendrán — request.ProjectId decide a cuál (o null = empresa).
                _expenses.Add(new ProjectExpenseDto(
                    Id:                    Guid.NewGuid(),
                    ProjectId:             request.ProjectId,
                    Date:                  DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    Category:              "subcontratista",
                    Description:           $"Cuenta de cobro {invoice.Period} — {invoice.Concept}",
                    Amount:                invoice.Amount,
                    Currency:              invoice.Currency,
                    Vendor:                collaborator?.Name,
                    Billable:              false,
                    Status:                "aprobado",
                    CreatedBy:             _tenant.UserInitials,
                    Source:                "payout_invoice",
                    SourcePayoutInvoiceId: invoice.Id));
            }
        }
        else
        {
            invoice.Reject(request.Notes!);
            // Mismo motivo que en PILA: token reusable regenerado para que el enlace del
            // correo de rechazo nunca esté vencido.
            invoice.RegenerateToken();
            await _invoices.UpdateAsync(invoice, cancellationToken);

            var collaborator = await _collaborators.GetByIdAsync(invoice.CollaboratorId, cancellationToken);
            if (collaborator is not null)
            {
                collaborator.UpdatePayoutInvoiceStatus(invoice.Status, invoice.Period);
                await _collaborators.UpdateAsync(collaborator, cancellationToken);
            }

            await _uow.SaveChangesAsync(cancellationToken);

            if (collaborator is not null && !string.IsNullOrWhiteSpace(collaborator.Email))
            {
                var organization = await _organizations.GetByIdAsync(_tenant.OrganizationId, cancellationToken);
                var portalUrl = $"{_settings.BaseUrl}/portal/entrar?token={invoice.PublicToken}&kind=payout_invoice_request";
                await _email.SendPayoutInvoiceRejectedEmailAsync(
                    collaborator.Email!, collaborator.Name, organization?.Name ?? string.Empty,
                    invoice.Period, request.Notes!, portalUrl, cancellationToken);
            }
        }
    }

    private static (DateTime From, DateTime To) ParsePeriodRange(string period)
    {
        var parts = period.Split('-');
        var year  = int.Parse(parts[0]);
        var month = int.Parse(parts[1]);
        var from  = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var to    = from.AddMonths(1).AddTicks(-1);
        return (from, to);
    }
}
