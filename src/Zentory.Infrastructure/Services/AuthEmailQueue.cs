using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Infrastructure.Services;

// IAuthEmailQueue sobre un Channel acotado y un BackgroundService (PLAN-0018 D-14; SR-0018.10). Los correos viven solo
// en memoria hasta enviarse: ni el token, ni la URL con token, ni el destinatario se persisten o se registran. Los logs
// llevan solo la etiqueta kind y el tipo de la excepción; el mensaje de una excepción del proveedor puede incluir el
// destinatario o el contenido, así que no se registra.
public sealed class AuthEmailQueue : BackgroundService, IAuthEmailQueue
{
    public const int Capacity = 256;

    private readonly Channel<WorkItem>       _channel;
    private readonly IServiceScopeFactory    _scopes;
    private readonly ILogger<AuthEmailQueue> _logger;

    public AuthEmailQueue(IServiceScopeFactory scopes, ILogger<AuthEmailQueue> logger)
    {
        _scopes  = scopes;
        _logger  = logger;
        _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(Capacity)
        {
            // Con Wait, TryWrite devuelve false si está llena: el request nunca se bloquea esperando cupo.
            FullMode     = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });
    }

    public bool TryEnqueue(string kind, Func<IEmailService, CancellationToken, Task> send)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(send);

        if (_channel.Writer.TryWrite(new WorkItem(kind, send)))
            return true;

        _logger.LogWarning("Auth email queue full; dropped email of kind {Kind}", kind);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
                await SendAsync(item, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado: lo que quede en la cola se pierde (aceptado por RF-23).
        }
    }

    private async Task SendAsync(WorkItem item, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
            await item.Send(email, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("Auth email of kind {Kind} failed: {ExceptionType}", item.Kind, ex.GetType().Name);
        }
    }

    private sealed record WorkItem(string Kind, Func<IEmailService, CancellationToken, Task> Send);
}
