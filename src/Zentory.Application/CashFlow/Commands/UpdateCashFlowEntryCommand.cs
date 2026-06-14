using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.CashFlow.Commands;

public record UpdateCashFlowEntryCommand(
    Guid     Id,
    string   Description,
    decimal  Amount,
    string   Currency,
    DateOnly TransactionDate,
    short?   CategoryId   = null,
    Guid?    ProjectId    = null,
    decimal  ExchangeRate = 1m) : IRequest<Unit>;

public sealed class UpdateCashFlowEntryCommandValidator
    : AbstractValidator<UpdateCashFlowEntryCommand>
{
    public UpdateCashFlowEntryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
    }
}

public sealed class UpdateCashFlowEntryCommandHandler
    : IRequestHandler<UpdateCashFlowEntryCommand, Unit>
{
    private readonly ICashFlowEntryRepository _repo;
    private readonly IUnitOfWork              _uow;
    private readonly ITenantContext           _tenant;

    public UpdateCashFlowEntryCommandHandler(
        ICashFlowEntryRepository repo,
        IUnitOfWork              uow,
        ITenantContext           tenant)
    {
        _repo   = repo;
        _uow    = uow;
        _tenant = tenant;
    }

    public async Task<Unit> Handle(UpdateCashFlowEntryCommand request, CancellationToken ct)
    {
        var entry = await _repo.GetByIdAsync(request.Id, ct);
        if (entry is null || entry.OrganizationId != _tenant.OrganizationId || entry.DeletedAt != null)
            throw new NotFoundException("CashFlowEntry", request.Id);

        entry.Update(
            request.Description,
            request.Amount,
            request.Currency,
            request.ExchangeRate,
            request.TransactionDate,
            request.CategoryId,
            request.ProjectId);

        await _repo.UpdateAsync(entry, ct);
        await _uow.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
