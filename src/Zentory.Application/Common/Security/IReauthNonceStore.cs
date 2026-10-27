namespace Zentory.Application.Common.Security;

// Registro de nonce de reautenticación consumidos (PLAN-0018 RF-84 d; D-40): la emisión no guarda nada, el consumo sí,
// para que un nonce (y cualquier id_token que lo lleve) sirva una sola vez. La implementación del MVP vive en la
// memoria del proceso (una sola instancia); PLAN-0002 la cambia por una caché distribuida sin tocar los comandos.
public interface IReauthNonceStore
{
    // true si el nonce ya se consumió y su registro sigue vigente (hasta su exp).
    bool IsConsumed(string nonce);

    // Marca el nonce como consumido hasta expiresAt. Comprobar y registrar es atómico: de dos llamadas concurrentes con
    // el mismo nonce, exactamente una devuelve true. Un nonce ya consumido o ya vencido devuelve false.
    bool TryConsume(string nonce, DateTimeOffset expiresAt);

    // Deshace un consumo cuando la escritura de la acción falla después de consumir (409 de concurrencia, error de base):
    // el reintento legítimo con el mismo nonce vuelve a funcionar.
    void Release(string nonce);
}
