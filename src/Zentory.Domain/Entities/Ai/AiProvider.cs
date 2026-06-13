using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Ai;

public class AiProvider : BaseEntity
{
    public string   Name        { get; private set; } = default!;  // 'anthropic' | 'openai' | 'google'
    public string   DisplayName { get; private set; } = default!;
    public string?  BaseUrl     { get; private set; }
    public bool     Active      { get; private set; } = true;

    private AiProvider() { }

    public static AiProvider Create(string name, string displayName, string? baseUrl = null)
    {
        return new AiProvider { Name = name, DisplayName = displayName, BaseUrl = baseUrl };
    }

    public void Deactivate() { Active = false; }
}
