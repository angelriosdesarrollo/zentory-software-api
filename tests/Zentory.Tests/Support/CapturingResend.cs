using System.Reflection;
using Resend;

namespace Zentory.Tests.Support;

// IResend que guarda los EmailMessage enviados (proxy: la interfaz tiene muchos métodos que los tests no usan). Sirve para
// probar las plantillas reales de ResendEmailService (codificación de valores, ausencia de tokens) sin llamar a Resend.
public class CapturingResend : DispatchProxy
{
    public List<EmailMessage> Messages { get; } = [];

    public static IResend Create() => DispatchProxy.Create<IResend, CapturingResend>();

    // El proxy creado por Create, para leer los mensajes.
    public static CapturingResend Of(IResend resend) => (CapturingResend)(object)resend;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != nameof(IResend.EmailSendAsync))
            throw new NotSupportedException(targetMethod?.Name);

        Messages.Add((EmailMessage)args![0]!);
        var resultType = targetMethod.ReturnType.GetGenericArguments()[0];
        return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType)
            .Invoke(null, [resultType.IsValueType ? Activator.CreateInstance(resultType) : null]);
    }
}
