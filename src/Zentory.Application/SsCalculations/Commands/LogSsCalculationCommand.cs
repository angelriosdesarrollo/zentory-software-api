using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.SsCalculations.DTOs;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.SsCalculations.Commands;

// Persiste el cálculo de PILA personal del propio dueño de la cuenta (CollaboratorId
// siempre null aquí — el flujo de equipo usa PilaVerification, no esta entidad).
// Un período recalculado actualiza la fila existente en vez de duplicarla.
public sealed record LogSsCalculationCommand(
    string  Period,
    decimal Income,
    string  Currency,
    decimal Ibc,
    decimal Salud,
    decimal Pension,
    decimal Arl,
    decimal TotalContribution,
    decimal? SmlvUsed) : IRequest<SsCalculationLogDto>;

public sealed class LogSsCalculationCommandValidator : AbstractValidator<LogSsCalculationCommand>
{
    public LogSsCalculationCommandValidator()
    {
        RuleFor(x => x.Period).Matches(@"^\d{4}-\d{2}$")
            .WithMessage("Period must be in 'YYYY-MM' format.");
        RuleFor(x => x.Income).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty();
    }
}

public sealed class LogSsCalculationCommandHandler
    : IRequestHandler<LogSsCalculationCommand, SsCalculationLogDto>
{
    private readonly IZentoryDbContext _db;
    private readonly IUnitOfWork       _uow;
    private readonly ITenantContext    _tenant;

    public LogSsCalculationCommandHandler(IZentoryDbContext db, IUnitOfWork uow, ITenantContext tenant)
    {
        _db     = db;
        _uow    = uow;
        _tenant = tenant;
    }

    public async Task<SsCalculationLogDto> Handle(LogSsCalculationCommand request, CancellationToken ct)
    {
        var resultJson = JsonSerializer.Serialize(new
        {
            ibc     = request.Ibc,
            salud   = request.Salud,
            pension = request.Pension,
            arl     = request.Arl,
        });

        var existing = await _db.SsCalculationLogs
            .Where(s => s.OrganizationId == _tenant.OrganizationId
                     && s.CollaboratorId == null
                     && s.Period == request.Period)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            existing = SsCalculationLog.Create(
                _tenant.OrganizationId, _tenant.UserId, "CO", request.Period,
                request.Income, request.Currency, resultJson,
                request.TotalContribution, request.SmlvUsed);
            _db.SsCalculationLogs.Add(existing);
        }
        else
        {
            existing.Recalculate(request.Income, resultJson, request.TotalContribution, request.SmlvUsed);
        }

        await _uow.SaveChangesAsync(ct);

        return new SsCalculationLogDto(
            existing.Id, existing.Period, existing.Income, existing.Currency, existing.Result,
            existing.TotalContribution, existing.SmlvUsed, existing.Status, existing.FiledAt,
            existing.DocumentFileName, existing.DocumentFileSize);
    }
}
