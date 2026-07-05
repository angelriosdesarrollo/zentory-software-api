using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class ProjectFile : TenantEntity
{
    public Guid   ProjectId    { get; private set; }
    public string Name         { get; private set; } = default!;
    public string FileType     { get; private set; } = default!;
    public string Size         { get; private set; } = default!;
    public string UploadedBy   { get; private set; } = default!;
    public DateTime UploadedAt { get; private set; }
    public string StorageKey   { get; private set; } = default!;

    private ProjectFile() { }

    public static ProjectFile Create(
        Guid     organizationId,
        Guid     projectId,
        string   name,
        string   fileType,
        string   size,
        string   uploadedBy,
        string   storageKey,
        DateTime? uploadedAt = null)
    {
        return new ProjectFile
        {
            OrganizationId = organizationId,
            ProjectId      = projectId,
            Name           = name,
            FileType       = fileType,
            Size           = size,
            UploadedBy     = uploadedBy,
            StorageKey     = storageKey,
            UploadedAt     = uploadedAt ?? DateTime.UtcNow,
        };
    }
}
