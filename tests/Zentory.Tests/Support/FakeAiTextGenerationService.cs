using System.Collections.Concurrent;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Tests.Support;

// Doble de IAiTextGenerationService para el host de pruebas: nunca llama a Anthropic. Registra cada petición y
// devuelve una respuesta fija (o la que el test configure).
public sealed class FakeAiTextGenerationService : IAiTextGenerationService
{
    public sealed record Request(string ModelId, string SystemPrompt, string UserPrompt, int MaxOutputTokens);

    private readonly ConcurrentQueue<Request> _requests = new();

    public Func<Request, AiGenerationResult> Respond { get; set; } =
        _ => new AiGenerationResult("fake-ai-response", InputTokens: 0, OutputTokens: 0);

    public IReadOnlyCollection<Request> Requests => _requests.ToArray();

    public Task<AiGenerationResult> GenerateAsync(
        string            modelId,
        string            systemPrompt,
        string            userPrompt,
        int               maxOutputTokens,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var request = new Request(modelId, systemPrompt, userPrompt, maxOutputTokens);
        _requests.Enqueue(request);
        return Task.FromResult(Respond(request));
    }
}
