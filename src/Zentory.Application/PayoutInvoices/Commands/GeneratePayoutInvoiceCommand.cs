using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PayoutInvoices.Commands;

public record GeneratePayoutInvoiceCommand(
    Guid    CollaboratorId,
    string  Period,
    string  Concept,
    decimal Amount) : IRequest<Guid>;

public sealed class GeneratePayoutInvoiceCommandValidator : AbstractValidator<GeneratePayoutInvoiceCommand>
{
    public GeneratePayoutInvoiceCommandValidator()
    {
        RuleFor(x => x.Period).Matches(@"^\d{4}-\d{2}$")
            .WithMessage("Period must be in 'YYYY-MM' format.");
        RuleFor(x => x.Concept).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

public sealed class GeneratePayoutInvoiceCommandHandler : IRequestHandler<GeneratePayoutInvoiceCommand, Guid>
{
    private readonly ICollaboratorPayoutInvoiceRepository _invoices;
    private readonly ICollaboratorRepository               _collaborators;
    private readonly IOrganizationRepository               _organizations;
    private readonly IStorageService                       _storage;
    private readonly IPayoutInvoicePdfGenerator             _pdf;
    private readonly IUnitOfWork                            _uow;
    private readonly ITenantContext                         _tenant;

    public GeneratePayoutInvoiceCommandHandler(
        ICollaboratorPayoutInvoiceRepository invoices,
        ICollaboratorRepository               collaborators,
        IOrganizationRepository               organizations,
        IStorageService                       storage,
        IPayoutInvoicePdfGenerator            pdf,
        IUnitOfWork                           uow,
        ITenantContext                        tenant)
    {
        _invoices      = invoices;
        _collaborators = collaborators;
        _organizations = organizations;
        _storage       = storage;
        _pdf           = pdf;
        _uow           = uow;
        _tenant        = tenant;
    }

    public async Task<Guid> Handle(GeneratePayoutInvoiceCommand request, CancellationToken cancellationToken)
    {
        var collaborator = await _collaborators.GetByIdAsync(request.CollaboratorId, cancellationToken);
        if (collaborator is null || collaborator.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("Collaborator", request.CollaboratorId);

        var organization = await _organizations.GetByIdAsync(_tenant.OrganizationId, cancellationToken);

        var invoice = CollaboratorPayoutInvoice.Create(
            _tenant.OrganizationId,
            request.CollaboratorId,
            request.Period,
            request.Concept,
            request.Amount,
            collaborator.Currency,
            source: "generated",
            createdBy: _tenant.UserId);

        var pdfBytes = _pdf.Generate(new PayoutInvoicePdfModel(
            organization?.Name ?? string.Empty,
            collaborator.Name,
            collaborator.IdNumber,
            request.Period,
            request.Concept,
            request.Amount,
            collaborator.Currency,
            DateTime.UtcNow));

        var key      = $"payout-invoices/{_tenant.OrganizationId}/{request.CollaboratorId}/{invoice.Id}.pdf";
        var fileName = $"cuenta-cobro-{request.Period}.pdf";
        using (var stream = new MemoryStream(pdfBytes))
        {
            await _storage.UploadAsync(key, stream, "application/pdf", cancellationToken);
        }

        invoice.MarkGenerated(key, fileName, pdfBytes.LongLength, "application/pdf");
        collaborator.UpdatePayoutInvoiceStatus(invoice.Status, invoice.Period);

        await _invoices.AddAsync(invoice, cancellationToken);
        await _collaborators.UpdateAsync(collaborator, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return invoice.Id;
    }
}
