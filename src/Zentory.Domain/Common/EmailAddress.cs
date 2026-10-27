namespace Zentory.Domain.Common;

// Forma canónica de un email (PLAN-0018 RF-27, D-10): sin espacios alrededor y en minúsculas invariantes. Se aplica antes
// de buscar y antes de guardar; el check ck_users_email_normalized de la base rechaza cualquier otra forma, así el índice
// único ix_users_email da la unicidad sin distinguir mayúsculas (sin citext ni índice por expresión).
public static class EmailAddress
{
    public static string Normalize(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToLowerInvariant();
    }
}
