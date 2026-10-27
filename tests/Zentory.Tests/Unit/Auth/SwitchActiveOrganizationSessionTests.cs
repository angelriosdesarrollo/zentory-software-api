using Microsoft.EntityFrameworkCore;
using Zentory.Application.Auth.Commands;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Repositories;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Auth;

// PLAN-0018 T18b, CA-T18B-2 (parte de CA-130; RF-90 c, D-47): POST /organization/switch emite el access token con el sid
// del token que llama (ITenantContext.SessionId) y nunca copia User.CurrentSessionId, aunque un login nuevo lo haya
// cambiado entre el middleware y el comando. Unitario sobre el handler, para que siga valiendo cuando T18c corte en el
// middleware las sesiones reemplazadas con 401.
public sealed class SwitchActiveOrganizationSessionTests
{
    [Fact]
    public async Task PLAN0018_CA130_Switch_ConSidDistintoDelVigente_EmiteConElSidDelTokenYNoCopiaCurrentSessionId()
    {
        await using var db = TestDbContextFactory.Create();
        var organization = Organization.Create("Destino", LegalType.Freelance);
        var user = User.Create($"switch-{Guid.NewGuid():N}@zentory.test", "Sara", "Switch", OrgRole.Owner);
        var current = user.StartSession();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var callerSession = Guid.NewGuid();
        Assert.NotEqual(current, callerSession);
        var tenant = new FakeTenantContext { UserId = user.UserId, SessionId = callerSession };
        var handler = new SwitchActiveOrganizationCommandHandler(
            new SingleMembershipReader(organization),
            new SingleOrganizationRepository(organization),
            new UserRepository(db),
            TestJwtService.Create(),
            tenant,
            new UnitOfWork(db));

        var response = await handler.Handle(new SwitchActiveOrganizationCommand(organization.OrganizationId), default);

        Assert.Equal(callerSession, AuthSessionApi.SessionIdOf(response.AccessToken));
        Assert.Equal(organization.OrganizationId.ToString(), AuthSessionApi.ClaimOf(response.AccessToken, TestUserJwt.ActiveOrgClaim));
        Assert.Equal(current, user.CurrentSessionId);
        var stored = await db.Users.AsNoTracking().SingleAsync(u => u.UserId == user.UserId);
        Assert.Equal(current, stored.CurrentSessionId);
    }
}
