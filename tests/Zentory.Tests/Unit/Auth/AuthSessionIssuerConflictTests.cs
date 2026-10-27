using Microsoft.EntityFrameworkCore;
using Zentory.Application.Auth.Services;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Repositories;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Auth;

// PLAN-0018 T18b, CA-T18B-4 (parte de CA-133; RF-90 g, R-28, D-47): StartSessionAsync ante un conflicto de concurrencia
// en su SaveChanges. El conflicto es real y determinista: el doble de IUnitOfWork, antes de guardar, hace en otro contexto
// sobre la misma base InMemory lo que haría un login concurrente (revoca el refresh anterior) y una operación de
// propiedad (regenera OwnershipStamp, D-45). InMemory aplica los tokens de concurrencia IsRevoked y OwnershipStamp igual
// que el UPDATE ... WHERE de PostgreSQL.
public sealed class AuthSessionIssuerConflictTests
{
    private const string PendingGoogleId = "google-pendiente-de-guardar";

    [Fact]
    public async Task PLAN0018_CA133_StartSessionAsync_ConflictoSimuladoEnSaveChanges_ReintentaConValoresDeLaBaseYMismoSid()
    {
        var scenario = await SeedAsync();
        await using var db = scenario.NewContext();
        var user = await db.Users.SingleAsync(u => u.UserId == scenario.UserId);
        user.LinkGoogleAccount(PendingGoogleId);
        Guid? competitorStamp = null;
        var uow = new ConflictingUnitOfWork(db, conflicts: 1, competitor: async () =>
            competitorStamp = await scenario.ConcurrentLoginAndOwnershipChangeAsync());

        var issued = await NewIssuer(db, uow, scenario.Organization).StartSessionAsync(user, preferredOrgId: null);

        Assert.InRange(uow.Attempts, 2, AuthSessionIssuer.MaxStartSessionAttempts);
        var sid = AuthSessionApi.SessionIdOf(issued.AccessToken);
        Assert.Equal(sid, user.CurrentSessionId);

        await using var check = scenario.NewContext();
        var stored = await check.Users.AsNoTracking().SingleAsync(u => u.UserId == scenario.UserId);
        Assert.Equal(sid, stored.CurrentSessionId);
        // El cambio pendiente del usuario se guardó, y el sello de la otra operación no se pisó.
        Assert.Equal(PendingGoogleId, stored.GoogleId);
        Assert.Equal(competitorStamp, stored.OwnershipStamp);

        var tokens = await check.RefreshTokens.AsNoTracking().Where(t => t.UserId == scenario.UserId).ToListAsync();
        Assert.True(tokens.Single(t => t.TokenHash == RefreshToken.HashOf(scenario.PreviousRefreshToken)).IsRevoked);
        var current = tokens.Single(t => t.TokenHash == RefreshToken.HashOf(issued.RefreshToken));
        Assert.Equal(sid, current.SessionId);
        Assert.False(current.IsRevoked);
        Assert.Equal(2, tokens.Count);
    }

    [Fact]
    public async Task PLAN0018_CA133_StartSessionAsync_ConflictoEnCadaIntento_LanzaTrasElUltimoSinCambiarLaSesionVigente()
    {
        var scenario = await SeedAsync();
        await using var db = scenario.NewContext();
        var user = await db.Users.SingleAsync(u => u.UserId == scenario.UserId);
        var uow = new ConflictingUnitOfWork(db, conflicts: int.MaxValue,
            competitor: () => scenario.ConcurrentLoginAndOwnershipChangeAsync());

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => NewIssuer(db, uow, scenario.Organization).StartSessionAsync(user, preferredOrgId: null));

        Assert.Equal(AuthSessionIssuer.MaxStartSessionAttempts, uow.Attempts);
        await using var check = scenario.NewContext();
        var stored = await check.Users.AsNoTracking().SingleAsync(u => u.UserId == scenario.UserId);
        Assert.Equal(scenario.PreviousSessionId, stored.CurrentSessionId);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static AuthSessionIssuer NewIssuer(ZentoryDbContext db, IUnitOfWork uow, Organization organization)
        => new(
            new SingleMembershipReader(organization),
            new SingleOrganizationRepository(organization),
            new NoDeletedOrganizations(),
            new RefreshTokenRepository(db),
            TestJwtService.Create(),
            uow);

    // Usuario con una sesión vigente y su refresh token, en una base InMemory propia del test.
    private static async Task<Scenario> SeedAsync()
    {
        var databaseName = $"zentory-issuer-conflict-{Guid.NewGuid():N}";
        var organization = Organization.Create("Conflicto", LegalType.Freelance);
        var user = User.Create($"conflicto-{Guid.NewGuid():N}@zentory.test", "Carla", "Conflicto", OrgRole.Owner);
        var previousSessionId = user.StartSession();
        var previousRefresh = $"test-refresh-anterior-{Guid.NewGuid():N}";

        var scenario = new Scenario(databaseName, organization, user.UserId, previousSessionId, previousRefresh);
        await using var db = scenario.NewContext();
        db.Users.Add(user);
        db.RefreshTokens.Add(RefreshToken.Create(user.UserId, previousRefresh, DateTime.UtcNow.AddDays(1), previousSessionId));
        await db.SaveChangesAsync();
        return scenario;
    }

    private sealed record Scenario(
        string       DatabaseName,
        Organization Organization,
        Guid         UserId,
        Guid         PreviousSessionId,
        string       PreviousRefreshToken)
    {
        public ZentoryDbContext NewContext()
            => new(new DbContextOptionsBuilder<ZentoryDbContext>().UseInMemoryDatabase(DatabaseName).Options);

        // Lo que otra operación confirma entre la lectura y el guardado del inicio de sesión: un login que revoca el
        // refresh anterior y una operación de propiedad que regenera el sello. Devuelve el sello que dejó.
        public async Task<Guid> ConcurrentLoginAndOwnershipChangeAsync()
        {
            await using var other = NewContext();
            var user = await other.Users.SingleAsync(u => u.UserId == UserId);
            user.TouchOwnership();
            foreach (var token in await other.RefreshTokens.Where(t => t.UserId == UserId && !t.IsRevoked).ToListAsync())
                token.Revoke();
            await other.SaveChangesAsync();
            return user.OwnershipStamp;
        }
    }

    // Antes de cada uno de los primeros `conflicts` guardados ejecuta la operación competidora; después guarda de verdad.
    private sealed class ConflictingUnitOfWork(ZentoryDbContext db, int conflicts, Func<Task> competitor) : IUnitOfWork
    {
        public int Attempts { get; private set; }

        public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Attempts++;
            if (Attempts <= conflicts)
                await competitor();
            return await db.SaveChangesAsync(ct);
        }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
            => operation(ct);
    }
}
