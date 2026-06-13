using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
}
