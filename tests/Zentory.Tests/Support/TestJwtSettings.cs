namespace Zentory.Tests.Support;

// Única fuente de la configuración JWT de prueba (PLAN-0001, RF-13, D-5): la usan ZentoryApiFactory y, a través del
// contenedor de la factory, TestTokens. Las claves siguen la convención "test-...signing-key..." que vigila
// HostConfigurationPolicyTests para que nunca lleguen a un appsettings versionado. Issuer y Audience son distintos de
// los de appsettings.json, para que un token de prueba nunca valide contra un host configurado de verdad.
public static class TestJwtSettings
{
    public const string KeySetting             = "Jwt:Key";
    public const string CollaboratorKeySetting = "Jwt:CollaboratorKey";
    public const string IssuerSetting          = "Jwt:Issuer";
    public const string AudienceSetting        = "Jwt:Audience";

    // HMAC-SHA256: cada clave mide al menos 32 bytes en UTF-8.
    public const string Key             = "test-factory-access-signing-key-9f3c2a7b4e1d6085";
    public const string CollaboratorKey = "test-factory-collaborator-signing-key-5b8e0d2f7a1c9364";
    public const string Issuer          = "zentory-tests-api";
    public const string Audience        = "zentory-tests-app";

    public static IReadOnlyDictionary<string, string> AsSettings() => new Dictionary<string, string>
    {
        [KeySetting]             = Key,
        [CollaboratorKeySetting] = CollaboratorKey,
        [IssuerSetting]          = Issuer,
        [AudienceSetting]        = Audience,
    };
}
