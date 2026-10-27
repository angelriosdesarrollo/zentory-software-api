using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Interceptors;

public sealed class AuditInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenant;

    // Only financially or legally sensitive tables are audited.
    // Never audit AuditLog itself (avoids loops) or ActivityLog.
    private static readonly HashSet<Type> AuditedTypes = new()
    {
        typeof(Proposal),
        typeof(ProposalItem),
        typeof(Invoice),
        typeof(InvoiceItem),
        typeof(InvoicePayment),
        typeof(Project),
        typeof(ProjectBillingEntry),
        typeof(OrganizationMember),
    };

    // Table names the interceptor must never write to, as a secondary guard.
    private static readonly HashSet<string> ExcludedTableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "audit_log",
        "activity_logs",
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    public AuditInterceptor(ITenantContext tenant)
    {
        _tenant = tenant;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
            return await base.SavingChangesAsync(eventData, result, cancellationToken);

        try
        {
            var auditEntries = await BuildAuditEntriesAsync(eventData.Context, cancellationToken);
            if (auditEntries.Count > 0)
                eventData.Context.Set<AuditLog>().AddRange(auditEntries);
        }
        catch
        {
            // The interceptor must never break the main save operation.
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private async Task<List<AuditLog>> BuildAuditEntriesAsync(DbContext context, CancellationToken ct)
    {
        var entries = new List<AuditLog>();

        // Unauthenticated contexts (seeder, purge) are not audited.
        if (!_tenant.IsAuthenticated)
            return entries;

        Guid? orgId  = TryGetOrganizationId();
        Guid? userId = TryGetUserId();
        var claimIsCompany = IsCompany(TryGetClaimLegalType());
        var legalTypeByOrg = new Dictionary<Guid, bool>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (!AuditedTypes.Contains(entry.Entity.GetType())) continue;
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            var action = entry.State switch
            {
                EntityState.Added    => "INSERT",
                EntityState.Modified => "UPDATE",
                EntityState.Deleted  => "DELETE",
                _                    => null,
            };
            if (action is null) continue;

            var recordId = GetRecordId(entry);
            if (recordId == Guid.Empty) continue;

            var tableName = GetTableName(context, entry);

            // Secondary guard: skip if the resolved table name is in the exclusion list.
            if (ExcludedTableNames.Contains(tableName)) continue;

            string? oldValues = entry.State is EntityState.Modified or EntityState.Deleted
                ? SerializeProperties(entry.Properties, useOriginalValue: true)
                : null;

            string? newValues = entry.State is EntityState.Added or EntityState.Modified
                ? SerializeProperties(entry.Properties, useOriginalValue: false)
                : null;

            // PLAN-0018 REV-0005 H-2: the audited entity's own organization wins over the active one in the token.
            // The plan writes OrganizationMember rows in ANOTHER organization (ownership transfer, invitation
            // acceptance, organization creation); those rows belong to that tenant's log, and "company only" is
            // decided with that organization's LegalType, not with the claim.
            var entityOrgId = TryResolveOrgFromEntity(entry);
            Guid? resolvedOrgId = entityOrgId ?? orgId;

            bool isCompany;
            if (entityOrgId is null || entityOrgId == orgId)
                isCompany = claimIsCompany;
            else
                isCompany = await IsCompanyOrganizationAsync(context, entityOrgId.Value, legalTypeByOrg, ct);
            if (!isCompany) continue;

            try
            {
                entries.Add(AuditLog.Record(
                    tableName:      tableName,
                    recordId:       recordId,
                    action:         action,
                    organizationId: resolvedOrgId,
                    userId:         userId,
                    oldValues:      oldValues,
                    newValues:      newValues));
            }
            catch
            {
                // Skip individual entries that fail; do not abort the full batch.
            }
        }

        return entries;
    }

    private static Guid GetRecordId(EntityEntry entry)
    {
        // BaseEntity always exposes "Id" as a Guid.
        var idProp = entry.Properties.FirstOrDefault(p =>
            string.Equals(p.Metadata.Name, "Id", StringComparison.Ordinal));

        return idProp?.CurrentValue is Guid g ? g : Guid.Empty;
    }

    private static string GetTableName(DbContext context, EntityEntry entry)
    {
        return context.Model
                      .FindEntityType(entry.Entity.GetType())
                      ?.GetTableName()
               ?? entry.Entity.GetType().Name;
    }

    private static string? SerializeProperties(
        IEnumerable<PropertyEntry> properties,
        bool useOriginalValue)
    {
        try
        {
            var dict = new Dictionary<string, object?>();

            foreach (var prop in properties)
            {
                var name = prop.Metadata.Name;

                // Never persist password hashes, even if the entity somehow has one.
                if (string.Equals(name, "PasswordHash", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, "password_hash", StringComparison.OrdinalIgnoreCase)) continue;

                dict[name] = useOriginalValue ? prop.OriginalValue : prop.CurrentValue;
            }

            if (dict.Count == 0) return null;

            return JsonSerializer.Serialize(dict, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private Guid? TryGetOrganizationId()
    {
        try { return _tenant.IsAuthenticated ? _tenant.OrganizationId : null; }
        catch { return null; }
    }

    private string? TryGetClaimLegalType()
    {
        try { return _tenant.LegalType; }
        catch { return null; }
    }

    private static bool IsCompany(string? legalType) =>
        string.Equals(legalType, Zentory.Domain.Constants.LegalType.Empresa, StringComparison.OrdinalIgnoreCase);

    // LegalType of the audited entity's organization: the tracked entity when the unit of work already has it,
    // otherwise one read by key (Organization has no tenant filter, only the soft-delete one; the organization may be
    // another than the token's).
    private static async Task<bool> IsCompanyOrganizationAsync(
        DbContext context, Guid organizationId, Dictionary<Guid, bool> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(organizationId, out var cached)) return cached;

        string? legalType = context.ChangeTracker.Entries<Organization>()
            .Where(e => e.Entity.OrganizationId == organizationId)
            .Select(e => e.Entity.LegalType)
            .FirstOrDefault();

        legalType ??= await context.Set<Organization>()
            .AsNoTracking()
            .Where(o => o.OrganizationId == organizationId)
            .Select(o => o.LegalType)
            .FirstOrDefaultAsync(ct);

        return cache[organizationId] = IsCompany(legalType);
    }

    private Guid? TryGetUserId()
    {
        try { return _tenant.IsAuthenticated ? _tenant.UserId : null; }
        catch { return null; }
    }

    // Fallback: read OrganizationId directly from the entity when running outside
    // an authenticated HTTP request (background jobs, dev seeder).
    private static Guid? TryResolveOrgFromEntity(EntityEntry entry)
    {
        try
        {
            var orgProp = entry.Properties.FirstOrDefault(p =>
                string.Equals(p.Metadata.Name, "OrganizationId", StringComparison.Ordinal));

            return orgProp?.CurrentValue is Guid g && g != Guid.Empty ? g : null;
        }
        catch { return null; }
    }
}
