using Zentory.Domain.Entities;

namespace Zentory.Application.Common.Interfaces;

public interface IJwtService
{
    string GenerateAccessToken(User user, Organization org);
    string GenerateRefreshToken();
}
