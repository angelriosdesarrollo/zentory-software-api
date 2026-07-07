namespace Zentory.Application.Common.Interfaces;

public record GoogleUserInfo(string Subject, string Email, string FirstName, string LastName);

public interface IGoogleTokenValidator
{
    Task<GoogleUserInfo> ValidateAsync(string idToken, CancellationToken ct = default);
}
