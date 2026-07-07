namespace Zentory.Application.Common.Interfaces;

public sealed record AiGenerationResult(string Text, int InputTokens, int OutputTokens);

/// <summary>
/// Generación de texto vía LLM. Un único método genérico, pensado para reusarse en cualquier
/// feature de IA futuro (no solo enriquecer propuestas) — el modelo y el system prompt vienen
/// siempre de ai.feature_configs / ai.prompt_templates, nunca hardcodeados en el caller.
/// </summary>
public interface IAiTextGenerationService
{
    Task<AiGenerationResult> GenerateAsync(
        string            modelId,
        string            systemPrompt,
        string            userPrompt,
        int               maxOutputTokens,
        CancellationToken ct = default);
}
