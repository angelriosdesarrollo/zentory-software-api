namespace Zentory.Application.Common.Interfaces;

// Cola en proceso de los correos de identidad y acceso (PLAN-0018 D-14, RF-22, RF-23, RF-65 f, RF-81 g; SR-0018.10):
// el comando encola y responde sin esperar el envío. No usa Hangfire: el correo puede llevar un token o una URL con
// token, y nada de eso debe quedar persistido. Se acepta perder un correo si el proceso cae.
public interface IAuthEmailQueue
{
    // kind es una etiqueta fija y sin datos (p. ej. "reauth-failure-warning"): es lo único del correo que puede llegar
    // a los logs. send recibe el IEmailService de un scope propio, fuera del request. Devuelve false si la cola está
    // llena y el correo se descartó.
    bool TryEnqueue(string kind, Func<IEmailService, CancellationToken, Task> send);
}
