using System.Text;

namespace Zentory.Application.Common.Security;

// Política de contraseñas (PLAN-0018 RF-26; SR-0018.14): register, reset-password, aceptación de invitación con cuenta
// nueva y DemoData:Password. El tope de 72 bytes UTF-8 existe porque BCrypt ignora lo que pasa de ahí: dos contraseñas
// con el mismo prefijo de 72 bytes darían el mismo hash.
public static class PasswordPolicy
{
    public const int MinLength    = 8;
    public const int MaxLength    = 128;
    public const int MaxUtf8Bytes = 72;

    // Mensaje sin el valor: se usa en respuestas 422 y en errores de arranque.
    public static readonly string Requirement =
        $"between {MinLength} and {MaxLength} characters and at most {MaxUtf8Bytes} bytes in UTF-8";

    // Las dos reglas a la vez. Como cada carácter ocupa al menos un byte, el tope efectivo es de 72 caracteres ASCII; el
    // máximo de 128 caracteres solo decide por separado (HasValidLength) y queda como límite del contrato.
    public static bool IsSatisfiedBy(string? password)
        => password is not null && HasValidLength(password) && IsWithinByteLimit(password);

    // La longitud cuenta unidades UTF-16 (string.Length), igual que FluentValidation y el length del frontend.
    public static bool HasValidLength(string password)
        => password.Length is >= MinLength and <= MaxLength;

    public static bool IsWithinByteLimit(string password)
        => Encoding.UTF8.GetByteCount(password) <= MaxUtf8Bytes;
}
