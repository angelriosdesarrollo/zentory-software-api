using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.CashFlow.Commands;

public record CreateCashFlowEntryCommand(
    string   Type,              // "income" | "expense"
    string   Description,
    decimal  Amount,
    string   Currency,
    DateOnly TransactionDate,
    short?   CategoryId    = null,
    Guid?    ProjectId     = null,
    decimal  ExchangeRate  = 1m,
    bool     IsRecurring   = false,
    string?  RecurrenceRule= null) : IRequest<Guid>;

public sealed class CreateCashFlowEntryCommandValidator
    : AbstractValidator<CreateCashFlowEntryCommand>
{
    public CreateCashFlowEntryCommandValidator()
    {
        RuleFor(x => x.Type).NotEmpty().Must(t => t is "income" or "expense")
            .WithMessage("Type debe ser 'income' o 'expense'.");
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
    }
}

public sealed class CreateCashFlowEntryCommandHandler
    : IRequestHandler<CreateCashFlowEntryCommand, Guid>
{
    private readonly ICashFlowEntryRepository _repo;
    private readonly IUnitOfWork              _uow;
    private readonly ITenantContext           _tenant;

    public CreateCashFlowEntryCommandHandler(
        ICashFlowEntryRepository repo,
        IUnitOfWork              uow,
        ITenantContext           tenant)
    {
        _repo   = repo;
        _uow    = uow;
        _tenant = tenant;
    }

    public async Task<Guid> Handle(CreateCashFlowEntryCommand request, CancellationToken ct)
    {
        var amountBase = request.Amount * request.ExchangeRate;

        var entry = CashFlowEntry.CreateManual(
            organizationId:  _tenant.OrganizationId,
            type:            request.Type,
            description:     request.Description,
            amount:          request.Amount,
            currency:        request.Currency,
            exchangeRate:    request.ExchangeRate,
            amountBase:      amountBase,
            transactionDate: request.TransactionDate,
            categoryId:      request.CategoryId,
            projectId:       request.ProjectId,
            isRecurring:     request.IsRecurring,
            recurrenceRule:  request.RecurrenceRule,
            createdBy:       _tenant.UserId);

        await _repo.AddAsync(entry, ct);
        await _uow.SaveChangesAsync(ct);

        return entry.Id;
    }
}
