using Anthropic;
using Anthropic.Models.Messages;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Infrastructure.Services;

public sealed class AnthropicTextGenerationService : IAiTextGenerationService
{
    private readonly AnthropicClient _client;

    public AnthropicTextGenerationService(AnthropicClient client) => _client = client;

    public async Task<AiGenerationResult> GenerateAsync(
        string modelId, string systemPrompt, string userPrompt, int maxOutputTokens, CancellationToken ct = default)
    {
        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model     = modelId,
            MaxTokens = maxOutputTokens,
            System    = systemPrompt,
            Messages  = [new() { Role = Role.User, Content = userPrompt }]
        });

        var text = response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(b => b.Text)
            .FirstOrDefault() ?? string.Empty;

        return new AiGenerationResult(text, (int)response.Usage.InputTokens, (int)response.Usage.OutputTokens);
    }
}
