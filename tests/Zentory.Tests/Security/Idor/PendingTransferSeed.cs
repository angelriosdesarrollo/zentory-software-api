using Microsoft.Extensions.DependencyInjection;
using Zentory.Application.Common.Security;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Security.Idor;

// Propuesta de transferencia pendiente sembrada fuera de una petición (PLAN-0018 RF-64 c), del owner del tenant hacia el
// destino indicado. La comparten los casos IDOR de identidad y acceso y la matriz de roles (CA-2, CA-52): cancelar o
// listar sin pasar por el aviso ni la reautenticación del owner.
public static class PendingTransferSeed
{
    public static async Task<OrganizationOwnershipTransfer> SeedAsync(IServiceProvider services, SeededTenant tenant, User target)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var transfer = OrganizationOwnershipTransfer.Propose(tenant.OrganizationId, tenant.Owner.UserId, target.UserId,
            previewHash: new string('a', 64), DateTime.UtcNow, OrgSecurity.OwnershipTransferLifetime);
        db.OrganizationOwnershipTransfers.Add(transfer);
        await db.SaveChangesAsync();
        return transfer;
    }
}
