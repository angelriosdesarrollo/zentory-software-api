namespace Zentory.Application.CollaboratorPortal;

// Valores del claim auth_method del JWT del portal: dicen cómo se abrió la sesión.
// Solo una sesión de magic_link prueba control del email, así que solo esa puede
// abarcar varias organizaciones; una de solicitud queda atada al colaborador del token.
public static class CollaboratorAuthMethod
{
    public const string MagicLink = "magic_link";
    public const string Request   = "request";
}
