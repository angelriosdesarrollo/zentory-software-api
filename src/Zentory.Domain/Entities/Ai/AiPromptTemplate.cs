using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Ai;

// Immutable once created. To update a prompt: insert new version, mark old as inactive.
public class AiPromptTemplate : BaseEntity
{
    public Guid    FeatureId      { get; private set; }
    public int     Version        { get; private set; }
    public string  Name           { get; private set; } = default!;
    public string  SystemPrompt   { get; private set; } = default!;
    public string? UserPromptTpl  { get; private set; }
    public string  Variables      { get; private set; } = "[]";  // JSONB: ['project_name', ...]
    public bool    IsActive       { get; private set; } = true;
    public string? CreatedBy      { get; private set; }

    private AiPromptTemplate() { }

    public static AiPromptTemplate Create(
        Guid   featureId,
        int    version,
        string name,
        string systemPrompt,
        string? userPromptTpl = null,
        string  variables     = "[]",
        string? createdBy     = null)
    {
        return new AiPromptTemplate
        {
            FeatureId     = featureId,
            Version       = version,
            Name          = name,
            SystemPrompt  = systemPrompt,
            UserPromptTpl = userPromptTpl,
            Variables     = variables,
            CreatedBy     = createdBy
        };
    }

    public void Deactivate() { IsActive = false; }
}
