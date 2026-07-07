using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.SsCalculations.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.SsCalculations.Commands;

public sealed record MarkSsCalculationFiledCommand(
    Guid    Id,
    string? StorageKey  = null,
    string? FileName    = null,
    long?   FileSize    = null,
    string? ContentType = null) : IRequest<SsCalculationLogDto>;

public sealed class MarkSsCalculationFiledCommandValidator : AbstractValidator<MarkSsCalculationFiledCommand>
{
    public MarkSsCalculationFiledCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public sealed class MarkSsCalculationFiledCommandHandler
    : IRequestHandler<MarkSsCalculationFiledCommand, SsCalculationLogDto>
{
    private readonly IZentoryDbContext _db;
    private readonly IUnitOfWork       _uow;
    private readonly ITenantContext    _tenant;

    public MarkSsCalculationFiledCommandHandler(IZentoryDbContext db, IUnitOfWork uow, ITenantContext tenant)
    {
        _db     = db;
        _uow    = uow;
        _tenant = tenant;
    }

    public async Task<SsCalculationLogDto> Handle(MarkSsCalculationFiledCommand request, CancellationToken ct)
    {
        var log = await _db.SsCalculationLogs
            .Where(s => s.Id == request.Id
                     && s.OrganizationId == _tenant.OrganizationId
                     && s.CollaboratorId == null)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("SsCalculationLog", request.Id);

        log.MarkFiled(request.StorageKey, request.FileName, request.FileSize, request.ContentType);
        await _uow.SaveChangesAsync(ct);

        return new SsCalculationLogDto(
            log.Id, log.Period, log.Income, log.Currency, log.Result,
            log.TotalContribution, log.SmlvUsed, log.Status, log.FiledAt,
            log.DocumentFileName, log.DocumentFileSize);
    }
}
