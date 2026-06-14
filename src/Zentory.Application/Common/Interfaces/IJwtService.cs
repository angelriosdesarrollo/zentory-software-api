using Zentory.Domain.Entities;

namespace Zentory.Application.Common.Interfaces;

public interface IJwtService
{
    string GenerateAccessToken(User user, global::Zentory.Domain.Entities.Organization org);
    string GenerateRefreshToken();
}
