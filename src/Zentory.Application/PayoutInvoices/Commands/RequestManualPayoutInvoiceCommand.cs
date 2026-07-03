using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PayoutInvoices.Commands;

public record RequestManualPayoutInvoiceCommand(
    Guid   CollaboratorId,
    string Period) : IRequest<Guid>;

public sealed class RequestManualPayoutInvoiceCommandValidator : AbstractValidator<RequestManualPayoutInvoiceCommand>
{
    public RequestManualPayoutInvoiceCommandValidator()
    {
        RuleFor(x => x.Period).Matches(@"^\d{4}-\d{2}$")
            .WithMessage("Period must be in 'YYYY-MM' format.");
    }
}

public sealed class RequestManualPayoutInvoiceCommandHandler : IRequestHandler<RequestManualPayoutInvoiceCommand, Guid>
{
    private readonly ICollaboratorPayoutInvoiceRepository _invoices;
    private readonly ICollaboratorRepository               _collaborators;
    private readonly IOrganizationRepository               _organizations;
    private readonly IUnitOfWork                            _uow;
    private readonly ITenantContext                         _tenant;
    private readonly IEmailService                          _email;
    private readonly IApplicationSettings                   _settings;

    public RequestManualPayoutInvoiceCommandHandler(
        ICollaboratorPayoutInvoiceRepository invoices,
        ICollaboratorRepository               collaborators,
        IOrganizationRepository               organizations,
        IUnitOfWork                           uow,
        ITenantContext                        tenant,
        IEmailService                         email,
        IApplicationSettings                  settings)
    {
        _invoices      = invoices;
        _collaborators = collaborators;
        _organizations = organizations;
        _uow           = uow;
        _tenant        = tenant;
        _email         = email;
        _settings      = settings;
    }

    public async Task<Guid> Handle(RequestManualPayoutInvoiceCommand request, CancellationToken cancellationToken)
    {
        var collaborator = await _collaborators.GetByIdAsync(request.CollaboratorId, cancellationToken);
        if (collaborator is null || collaborator.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("Collaborator", request.CollaboratorId);

        if (string.IsNullOrWhiteSpace(collaborator.Email))
            throw new ConflictException("NO_EMAIL", "El colaborador no tiene correo registrado.");

        var organization = await _organizations.GetByIdAsync(_tenant.OrganizationId, cancellationToken);

        var invoice = CollaboratorPayoutInvoice.Create(
            _tenant.OrganizationId,
            request.CollaboratorId,
            request.Period,
            concept: "Cuenta de cobro",
            amount: collaborator.MonthlyRate ?? collaborator.HourlyRate ?? 0,
            currency: collaborator.Currency,
            source: "manual_upload",
            createdBy: _tenant.UserId);

        await _invoices.AddAsync(invoice, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        var url = $"{_settings.BaseUrl}/portal/entrar?token={invoice.PublicToken}&kind=payout_invoice_request";
        await _email.SendPayoutInvoiceRequestEmailAsync(
            collaborator.Email!,
            collaborator.Name,
            organization?.Name ?? string.Empty,
            request.Period,
            url,
            invoice.TokenExpiresAt ?? DateTime.UtcNow.AddDays(30),
            cancellationToken);

        return invoice.Id;
    }
}
