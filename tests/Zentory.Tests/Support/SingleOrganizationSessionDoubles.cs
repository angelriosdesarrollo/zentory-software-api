using Microsoft.Extensions.Configuration;
using Zentory.Application.Auth.Services;
using Zentory.Application.Organization.Services;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Tests.Support;

// Dobles de la emisión de sesión en tests unitarios (PLAN-0018 D-16, D-47): un usuario owner de una sola organización
// vigente y sin organizaciones eliminadas. Los usan AuthSessionIssuerConflictTests y SwitchActiveOrganizationSessionTests.
public sealed class SingleMembershipReader(Organization organization) : IUserMembershipReader
{
    private UserMembership Membership => new(
        organization.OrganizationId, organization.Name, organization.LegalType, OwnerId: null,
        Plan.Free, OrgRole.Owner, DateTime.UtcNow.AddDays(-1));

    public Task<IReadOnlyList<UserMembership>> ListActiveAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UserMembership>>([Membership]);

    public Task<UserMembership?> FindActiveAsync(Guid userId, Guid organizationId, CancellationToken ct = default)
        => Task.FromResult<UserMembership?>(organizationId == organization.OrganizationId ? Membership : null);
}

public sealed class SingleOrganizationRepository(Organization organization) : IOrganizationRepository
{
    public Task<Organization?> GetByIdAsync(Guid organizationId, CancellationToken ct = default)
        => Task.FromResult<Organization?>(organizationId == organization.OrganizationId ? organization : null);

    public Task<IReadOnlyList<Organization>> ListActiveByLegalTypeAsync(string legalType, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Organization>>([organization]);

    public Task AddAsync(Organization organization, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class NoDeletedOrganizations : IDeletedOrganizationReader
{
    public Task<IReadOnlyList<DeletedOrganization>> ListRestorableAsync(Guid ownerId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DeletedOrganization>>([]);

    public Task<Organization?> FindRestorableAsync(Guid ownerId, Guid organizationId, CancellationToken ct = default)
        => Task.FromResult<Organization?>(null);

    public Task<bool> HasRestorableAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(false);
}

// JwtService real fuera del host, con la configuración de TestJwtSettings.
public static class TestJwtService
{
    public static Zentory.Infrastructure.Services.JwtService Create()
        => new(new ConfigurationBuilder()
            .AddInMemoryCollection(TestJwtSettings.AsSettings()
                .Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build());
}
