using System.Text;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities.Ai;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals.Commands;

// Content / EnrichedContent viajan en el mismo formato que SaveProposalSectionsCommand.SectionInput.Content:
// HTML del editor de secciones, codificado en base64 unicode-safe (ver encodeContent/decodeContent en
// zentory-app/lib/api/proposals.ts). El resultado nunca se guarda automáticamente — el usuario decide
// si lo acepta antes de que llegue a SaveProposalSectionsCommand.
public record EnrichProposalSectionCommand(
    Guid   ProposalId,
    string SectionType,
    string Content) : IRequest<EnrichProposalSectionResult>;

public record EnrichProposalSectionResult(string EnrichedContent);

public sealed class EnrichProposalSectionCommandHandler
    : IRequestHandler<EnrichProposalSectionCommand, EnrichProposalSectionResult>
{
    private const string FeatureKey = "proposal_section_enrich";
    private static readonly string[] PlanOrder = [Plan.Free, Plan.Pro, Plan.Studio];

    private readonly IProposalRepository        _proposals;
    private readonly IAiFeatureConfigRepository _featureConfigs;
    private readonly IAiUsageLogRepository      _usageLogs;
    private readonly IAiTextGenerationService   _generator;
    private readonly IUnitOfWork                _uow;
    private readonly ITenantContext             _tenant;

    public EnrichProposalSectionCommandHandler(
        IProposalRepository        proposals,
        IAiFeatureConfigRepository featureConfigs,
        IAiUsageLogRepository      usageLogs,
        IAiTextGenerationService   generator,
        IUnitOfWork                uow,
        ITenantContext             tenant)
    {
        _proposals      = proposals;
        _featureConfigs = featureConfigs;
        _usageLogs      = usageLogs;
        _generator      = generator;
        _uow            = uow;
        _tenant         = tenant;
    }

    public async Task<EnrichProposalSectionResult> Handle(
        EnrichProposalSectionCommand request, CancellationToken ct)
    {
        var proposal = await _proposals.GetByIdAsync(request.ProposalId, ct);
        if (proposal is null || proposal.OrganizationId != _tenant.OrganizationId || proposal.DeletedAt.HasValue)
            throw new NotFoundException("Proposal", request.ProposalId);

        if (proposal.Status is "accepted" or "rejected")
            throw new ConflictException("PROPOSAL_CLOSED", "No se puede editar una propuesta aceptada o rechazada.");

        var config = await _featureConfigs.GetActiveByFeatureKeyAsync(FeatureKey, ct);
        if (config is null)
            throw new ServiceUnavailableException(FeatureKey, retryAfterSeconds: 60);

        if (Array.IndexOf(PlanOrder, _tenant.Plan) < Array.IndexOf(PlanOrder, config.MinPlan))
            throw new ForbiddenException(ForbiddenReason.PlanRequired, config.MinPlan);

        if (config.MonthlyReqLimit.HasValue)
        {
            var used = await _usageLogs.CountThisMonthAsync(_tenant.OrganizationId, config.FeatureId, ct);
            if (used >= config.MonthlyReqLimit.Value)
            {
                var now       = DateTime.UtcNow;
                var nextReset = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
                throw new QuotaExceededException(FeatureKey, config.MonthlyReqLimit.Value, used, nextReset);
            }
        }

        var html = DecodeBase64Utf8(request.Content);

        var startedAt = DateTime.UtcNow;
        var result    = await _generator.GenerateAsync(
            config.ModelExternalId, config.SystemPrompt, html, config.MaxOutputTokens, ct);
        var latencyMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;

        var costUsd = result.InputTokens  / 1000m * config.InputCostPer1k
                    + result.OutputTokens / 1000m * config.OutputCostPer1k;

        var usageLog = AiUsageLog.Create(
            organizationId: _tenant.OrganizationId,
            featureId:      config.FeatureId,
            modelId:        config.ModelId,
            inputTokens:    result.InputTokens,
            outputTokens:   result.OutputTokens,
            costUsd:        costUsd,
            userId:         _tenant.UserId,
            contextType:    "proposal",
            contextId:      proposal.Id,
            latencyMs:      latencyMs);

        await _usageLogs.AddAsync(usageLog, ct);
        await _uow.SaveChangesAsync(ct);

        return new EnrichProposalSectionResult(EncodeBase64Utf8(result.Text));
    }

    private static string DecodeBase64Utf8(string base64)
        => string.IsNullOrEmpty(base64) ? string.Empty : Encoding.UTF8.GetString(Convert.FromBase64String(base64));

    private static string EncodeBase64Utf8(string text)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
}
