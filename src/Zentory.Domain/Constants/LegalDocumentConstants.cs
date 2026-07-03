namespace Zentory.Domain.Constants;

/// <summary>
/// Version accepted together at registration (checkbox covers Terms + Privacy Policy as one
/// consent event — see docs/legal/TERMS_OF_SERVICE.md and PRIVACY_POLICY.md in zentory-app).
/// Bump this when either document changes materially; existing users are NOT retroactively
/// re-prompted by this constant alone — that requires a separate re-consent flow.
/// </summary>
public static class LegalDocuments
{
    public const string CurrentTermsVersion = "v0.1";
}
