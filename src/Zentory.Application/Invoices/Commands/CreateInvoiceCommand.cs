using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Invoices.Commands;


public record InvoiceItemInput(
    string  Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPct = 0,
    short   SortOrder   = 0);

public record CreateInvoiceCommand(
    Guid                       ClientId,
    DateOnly                   DueAt,
    IReadOnlyList<InvoiceItemInput> Items,
    string                     Currency     = "COP",
    string                     DocumentType = "cobro",
    Guid?                      ProjectId    = null,
    string?                    Notes        = null,
    string?                    PaymentTerms = null) : IRequest<Guid>;

public sealed class CreateInvoiceCommandValidator : AbstractValidator<CreateInvoiceCommand>
{
    private static readonly string[] ValidDocumentTypes = ["cobro", "factura", "nota_credito"];

    public CreateInvoiceCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.DocumentType).Must(t => ValidDocumentTypes.Contains(t))
            .WithMessage("DocumentType must be cobro, factura or nota_credito.");
        RuleFor(x => x.Items).NotEmpty().WithMessage("La factura debe tener al menos un ítem.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Description).NotEmpty().MaximumLength(500);
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.UnitPrice).GreaterThanOrEqualTo(0);
            item.RuleFor(i => i.DiscountPct).InclusiveBetween(0, 100);
        });
    }
}

public sealed class CreateInvoiceCommandHandler : IRequestHandler<CreateInvoiceCommand, Guid>
{
    private readonly IInvoiceRepository  _invoices;
    private readonly IClientRepository   _clients;
    private readonly IUnitOfWork         _uow;
    private readonly ITenantContext      _tenant;
    private readonly IActivityLogService _activityLog;

    public CreateInvoiceCommandHandler(
        IInvoiceRepository  invoices,
        IClientRepository   clients,
        IUnitOfWork         uow,
        ITenantContext      tenant,
        IActivityLogService activityLog)
    {
        _invoices    = invoices;
        _clients     = clients;
        _uow         = uow;
        _tenant      = tenant;
        _activityLog = activityLog;
    }

    public async Task<Guid> Handle(CreateInvoiceCommand request, CancellationToken cancellationToken)
    {
        var client = await _clients.GetByIdAsync(request.ClientId, cancellationToken);
        if (client is null || client.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("Client", request.ClientId);

        var countThisMonth = await _invoices.CountThisMonthAsync(_tenant.OrganizationId, cancellationToken);
        var invoiceNumber  = $"INV-{DateTime.UtcNow:yyyyMM}-{countThisMonth + 1:D3}";

        var invoice = Invoice.Create(
            _tenant.OrganizationId,
            request.ClientId,
            invoiceNumber,
            request.DueAt,
            request.Currency,
            request.DocumentType,
            request.ProjectId,
            request.Notes,
            request.PaymentTerms);

        foreach (var (item, idx) in request.Items.Select((item, i) => (item, i)))
        {
            var invoiceItem = InvoiceItem.Create(
                _tenant.OrganizationId,
                invoice.Id,
                item.Description,
                item.Quantity,
                item.UnitPrice,
                (short)idx,
                item.DiscountPct);

            invoice.AddItem(invoiceItem);
        }

        await _invoices.AddAsync(invoice, cancellationToken);

        await _activityLog.LogAsync(
            "Invoice",
            invoice.Id,
            $"Creó la factura {invoice.InvoiceNumber} por {invoice.Total:N0} {invoice.Currency}",
            invoice.InvoiceNumber,
            ct: cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);

        return invoice.Id;
    }
}
