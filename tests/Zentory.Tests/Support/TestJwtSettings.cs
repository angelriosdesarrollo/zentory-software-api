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
    public const string ExpirySetting          = "Jwt:AccessTokenExpiryMinutes";

    // HMAC-SHA256: cada clave mide al menos 32 bytes en UTF-8.
    public const string Key             = "test-factory-access-signing-key-9f3c2a7b4e1d6085";
    public const string CollaboratorKey = "test-factory-collaborator-signing-key-5b8e0d2f7a1c9364";
    public const string Issuer          = "zentory-tests-api";
    public const string Audience        = "zentory-tests-app";

    // Expiración del access token de los hosts de prueba (PLAN-0018 RF-31: 1..15). Se fija aquí para que una variable
    // de entorno de la máquina nunca la cambie.
    public const int    AccessTokenExpiryMinutes = 15;

    // Clave ajena: misma forma y longitud que las de prueba pero distinta de Key y CollaboratorKey, para los tests que
    // necesitan un token cuya firma no valida en el host (PLAN-0001 CA-28). Ningún host la configura.
    public const string ForeignKey      = "test-foreign-access-signing-key-2d7e9a4c1b6f3058";

    // Audiencia de Google de los hosts de prueba (PLAN-0018 RF-81 h): el arranque falla con Google:ClientId vacío, y
    // appsettings.Production.json lo deja vacío. Un valor propio de los tests, nunca el de appsettings.json ni el de la
    // máquina (el doble FakeGoogleTokenValidator no lo usa: ningún test llama a Google).
    public const string GoogleClientIdSetting = "Google:ClientId";
    public const string GoogleClientId        = "zentory-tests-google-client-id.apps.googleusercontent.com";

    // Variantes de longitud para los tests de arranque (PLAN-0018 CA-35), derivadas de Key y CollaboratorKey (ASCII,
    // un byte por carácter): ninguna clave de prueba nace fuera de este archivo.
    public static string KeyWithBytes(int bytes)             => Key[..bytes];
    public static string CollaboratorKeyWithBytes(int bytes) => CollaboratorKey[..bytes];

    public static IReadOnlyDictionary<string, string> AsSettings() => new Dictionary<string, string>
    {
        [KeySetting]             = Key,
        [CollaboratorKeySetting] = CollaboratorKey,
        [IssuerSetting]          = Issuer,
        [AudienceSetting]        = Audience,
        [ExpirySetting]          = AccessTokenExpiryMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        [GoogleClientIdSetting]  = GoogleClientId,
    };
}
