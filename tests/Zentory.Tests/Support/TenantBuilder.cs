using System.Net.Http.Headers;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Billing.Commands;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Tests.Support;

// Dos organizaciones, A y B, sembradas sobre el host de pruebas (PLAN-0018 RF-13; ADR-0001 §6; D-25 parte 1). Cada una
// tiene su owner, su legalType y su plan, y los miembros que se pidan con WithMember. Antes de sembrar construye un
// ZentoryDbContext anónimo y consulta con él, para reproducir el orden real de arranque (R-1 de PLAN-0001).
//
// El plan se siembra como Subscription vigente del owner, que es de donde el middleware lo resuelve en cada petición
// (PLAN-0018 T25; D-26; R-12): el claim plan de los tokens es informativo, sale de esa misma suscripción y no autoriza
// nada. Cambiar el plan con AssignPlanAsync rige en la petición siguiente, con los mismos tokens. Desde la parte 3 la
// Subscription la crea AssignPlanBySeedCommand (D-25, RF-37), la única vía que
// escribe el plan: con el índice único filtrado de M3, nunca quedan dos vigentes por owner.
public sealed class TenantBuilder(ZentoryApiFactory factory)
{
    private readonly TenantOptions _a = new("A", LegalType.Empresa, Plan.Studio);
    private readonly TenantOptions _b = new("B", LegalType.Empresa, Plan.Studio);

    public TenantBuilder ForA(Action<TenantOptions> configure) { configure(_a); return this; }
    public TenantBuilder ForB(Action<TenantOptions> configure) { configure(_b); return this; }

    // Atajo: un miembro con ese rol en A, la organización desde la que se ataca en los casos IDOR.
    public TenantBuilder WithMember(string role) { _a.WithMember(role); return this; }

    public async Task<TenantPair> BuildAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();

        // Contexto sin usuario primero: el scope de la factory no tiene HttpContext.
        await db.Clients.CountAsync();

        var a = SeedTenant(db, _a);
        var b = SeedTenant(db, _b);
        await EnsureBillingPlanAsync(db, _a.Plan);
        await EnsureBillingPlanAsync(db, _b.Plan);
        await db.SaveChangesAsync();

        // El plan del owner por la misma vía que en producción (D-25 parte 3): free no crea suscripción.
        await AssignPlanAsync(factory.Services, a.Owner, a.Plan);
        await AssignPlanAsync(factory.Services, b.Owner, b.Plan);

        return new TenantPair(factory, a, b);
    }

    // Operador y destino con los que el builder firma la auditoría de AssignPlanBySeedCommand.
    public const string SeedOperator = "tenant-builder";
    public const string SeedTarget   = "test-host/zentory_test";

    // Asigna el plan al owner con AssignPlanBySeedCommand en su propio scope, como la CLI. Falla si el comando lo rechaza.
    // Antes deja en el catálogo el BillingPlan que el comando exige (también free, para las bajadas de T25).
    public static async Task<AssignPlanBySeedResult> AssignPlanAsync(IServiceProvider services, User owner, string plan)
    {
        using (var catalog = services.CreateScope())
        {
            var db = catalog.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            await EnsureBillingPlanAsync(db, plan);
            await db.SaveChangesAsync();
        }

        using var scope = services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new AssignPlanBySeedCommand(owner.Email, plan, SeedOperator, SeedTarget));
        if (!result.Succeeded)
            throw new InvalidOperationException($"AssignPlanBySeedCommand rechazó el plan del builder: {result.Error}");
        return result;
    }

    // La factory no siembra el catálogo: el builder crea el BillingPlan que el comando exige si aún no existe.
    public static async Task EnsureBillingPlanAsync(ZentoryDbContext db, string plan)
    {
        if (db.BillingPlans.Local.Any(p => p.Name == plan) || await db.BillingPlans.AnyAsync(p => p.Name == plan)) return;
        db.BillingPlans.Add(BillingPlan.Create(plan, plan, 0m, 0m));
    }

    private static SeededTenant SeedTenant(ZentoryDbContext db, TenantOptions options)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var organization = Organization.Create($"Org {options.Label} {suffix}", options.LegalType);
        var owner = NewUser(organization, $"owner-{options.Label.ToLowerInvariant()}-{suffix}", OrgRole.Owner,
            options.OwnerPassword, options.OwnerGoogleId);
        organization.SetOwner(owner.UserId);

        db.Organizations.Add(organization);
        db.Users.Add(owner);
        db.OrganizationMembers.Add(OrganizationMember.Create(organization.OrganizationId, owner.UserId, OrgRole.Owner));

        var members = new List<SeededMember>();
        foreach (var role in options.MemberRoles)
        {
            var member = NewUser(organization, $"{role}-{options.Label.ToLowerInvariant()}-{Guid.NewGuid():N}", role,
                options.MemberPassword);
            db.Users.Add(member);
            db.OrganizationMembers.Add(
                OrganizationMember.Create(organization.OrganizationId, member.UserId, role, invitedBy: owner.UserId));
            members.Add(new SeededMember(member, role));
        }

        return new SeededTenant(organization, owner, options.Plan, members);
    }

    // La contraseña se guarda con BCrypt de costo bajo: solo la verifica la reautenticación de los tests (RF-81 b).
    private static User NewUser(
        Organization organization, string localPart, string role, string? password = null, string? googleId = null)
        => User.Create($"{localPart}@zentory.test", "Prueba", localPart, role,
            passwordHash: password is null ? null : BCrypt.Net.BCrypt.HashPassword(password, 4),
            organizationId: organization.OrganizationId, googleId: googleId);
}

// Configuración de una de las dos organizaciones del builder.
public sealed class TenantOptions(string label, string legalType, string plan)
{
    private readonly List<string> _memberRoles = [];

    public string Label     { get; } = label;
    public string LegalType { get; private set; } = legalType;
    public string Plan      { get; private set; } = plan;

    public IReadOnlyList<string> MemberRoles => _memberRoles;

    public TenantOptions WithLegalType(string legalType) { LegalType = legalType; return this; }
    public TenantOptions WithPlan(string plan)           { Plan = plan;           return this; }

    // Credenciales para la reautenticación (RF-81): por defecto los usuarios sembrados no tienen contraseña ni Google.
    public string? OwnerPassword  { get; private set; }
    public string? OwnerGoogleId  { get; private set; }
    public string? MemberPassword { get; private set; }

    public TenantOptions WithOwnerPassword(string password)  { OwnerPassword = password;  return this; }
    public TenantOptions WithOwnerGoogleId(string googleId)  { OwnerGoogleId = googleId;  return this; }
    public TenantOptions WithMemberPassword(string password) { MemberPassword = password; return this; }

    public TenantOptions WithMember(string role)
    {
        if (!OrgRole.Assignable.Contains(role))
            throw new ArgumentException("WithMember solo admite roles asignables; el owner lo crea el builder.", nameof(role));
        _memberRoles.Add(role);
        return this;
    }
}

public sealed record SeededMember(User User, string Role);

public sealed record SeededTenant(Organization Organization, User Owner, string Plan, IReadOnlyList<SeededMember> Members)
{
    public Guid   OrganizationId => Organization.OrganizationId;
    public string LegalType      => Organization.LegalType;

    public User Member(string role) => Members.First(m => m.Role == role).User;
}

// Resultado del builder: las dos organizaciones y los clientes HTTP autenticados de cada una.
public sealed class TenantPair(ZentoryApiFactory factory, SeededTenant a, SeededTenant b)
{
    public SeededTenant A { get; } = a;
    public SeededTenant B { get; } = b;

    // Token real del owner (o del miembro indicado) con la organización activa del tenant. Su claim plan es el plan
    // efectivo al emitirlo (informativo); el plan que autoriza se lee de la suscripción del owner en cada petición.
    public string TokenFor(SeededTenant tenant, User? user = null)
    {
        user ??= tenant.Owner;
        var role = user.UserId == tenant.Owner.UserId
            ? OrgRole.Owner
            : tenant.Members.Single(m => m.User.UserId == user.UserId).Role;
        return factory.Tokens.ForUser(user, tenant.Organization, role);
    }

    public HttpClient ClientFor(SeededTenant tenant, User? user = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(tenant, user));
        return client;
    }

    // Siembra filas de cualquiera de las dos organizaciones fuera de una petición, como el seed real.
    public async Task SeedAsync(params object[] entities)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }
}
