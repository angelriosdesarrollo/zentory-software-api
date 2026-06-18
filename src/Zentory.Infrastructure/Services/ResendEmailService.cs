using Microsoft.Extensions.Configuration;
using Resend;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Infrastructure.Services;

public sealed class ResendEmailService : IEmailService
{
    private readonly IResend        _resend;
    private readonly string         _fromEmail;
    private readonly string         _fromName;

    public ResendEmailService(IResend resend, IConfiguration configuration)
    {
        _resend    = resend;
        _fromEmail = configuration["Resend:FromEmail"] ?? "noreply@zentory.co";
        _fromName  = configuration["Resend:FromName"]  ?? "Zentory";
    }

    public async Task SendProposalEmailAsync(
        string    toEmail,
        string    proposalTitle,
        string    clientName,
        string    proposalUrl,
        string?   customMessage,
        CancellationToken ct = default)
    {
        var defaultMsg = $"Te comparto la propuesta técnica <strong>{proposalTitle}</strong>. Puedes revisarla haciendo clic en el botón a continuación.";
        var bodyMsg    = string.IsNullOrWhiteSpace(customMessage)
            ? defaultMsg
            : System.Web.HttpUtility.HtmlEncode(customMessage).Replace("\n", "<br/>");

        var html = $"""
            <!DOCTYPE html>
            <html lang="es">
            <head><meta charset="UTF-8"/><meta name="viewport" content="width=device-width,initial-scale=1"/></head>
            <body style="margin:0;padding:0;background:#f4f5f7;font-family:'Helvetica Neue',Arial,sans-serif;">
              <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f5f7;padding:40px 0;">
                <tr><td align="center">
                  <table width="560" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 1px 4px rgba(0,0,0,.08);">
                    <tr>
                      <td style="background:#0f172a;padding:24px 32px;">
                        <span style="color:#fff;font-size:20px;font-weight:700;letter-spacing:-0.5px;">Zentory</span>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:32px 32px 24px;">
                        <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">{bodyMsg}</p>
                        <table cellpadding="0" cellspacing="0" style="margin:24px 0;">
                          <tr>
                            <td style="background:#2563eb;border-radius:8px;">
                              <a href="{proposalUrl}" target="_blank"
                                 style="display:inline-block;padding:12px 28px;color:#fff;font-size:14px;font-weight:600;text-decoration:none;">
                                Ver propuesta →
                              </a>
                            </td>
                          </tr>
                        </table>
                        <p style="margin:0;font-size:13px;color:#9ca3af;">
                          O copia este enlace en tu navegador:<br/>
                          <a href="{proposalUrl}" style="color:#2563eb;word-break:break-all;">{proposalUrl}</a>
                        </p>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:16px 32px;border-top:1px solid #f3f4f6;background:#fafafa;">
                        <p style="margin:0;font-size:12px;color:#9ca3af;text-align:center;">
                          Enviado desde Zentory · Plataforma para software factories
                        </p>
                      </td>
                    </tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        var email = new EmailMessage
        {
            From    = $"{_fromName} <{_fromEmail}>",
            Subject = $"Propuesta: {proposalTitle}",
            HtmlBody = html,
        };
        email.To.Add(toEmail);

        await _resend.EmailSendAsync(email, ct);
    }

    public async Task SendProposalViewedNotificationAsync(
        string    toEmail,
        string    recipientName,
        string    proposalTitle,
        string    proposalDashboardUrl,
        CancellationToken ct = default)
    {
        var html = $"""
            <!DOCTYPE html>
            <html lang="es">
            <head><meta charset="UTF-8"/><meta name="viewport" content="width=device-width,initial-scale=1"/></head>
            <body style="margin:0;padding:0;background:#f4f5f7;font-family:'Helvetica Neue',Arial,sans-serif;">
              <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f5f7;padding:40px 0;">
                <tr><td align="center">
                  <table width="560" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 1px 4px rgba(0,0,0,.08);">
                    <tr>
                      <td style="background:#0f172a;padding:24px 32px;">
                        <span style="color:#fff;font-size:20px;font-weight:700;letter-spacing:-0.5px;">Zentory</span>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:32px 32px 24px;">
                        <p style="margin:0 0 8px;font-size:13px;font-weight:600;color:#2563eb;text-transform:uppercase;letter-spacing:0.06em;">Notificación de apertura</p>
                        <p style="margin:0 0 16px;font-size:22px;font-weight:700;color:#0f172a;line-height:1.3;">
                          Tu propuesta fue abierta 👀
                        </p>
                        <p style="margin:0 0 20px;font-size:15px;color:#374151;line-height:1.6;">
                          Hola <strong>{System.Web.HttpUtility.HtmlEncode(recipientName)}</strong>, el cliente acaba de abrir tu propuesta
                          <strong>&ldquo;{System.Web.HttpUtility.HtmlEncode(proposalTitle)}&rdquo;</strong>.
                          Es un buen momento para hacer seguimiento.
                        </p>
                        <table cellpadding="0" cellspacing="0" style="margin:24px 0;">
                          <tr>
                            <td style="background:#2563eb;border-radius:8px;">
                              <a href="{proposalDashboardUrl}" target="_blank"
                                 style="display:inline-block;padding:12px 28px;color:#fff;font-size:14px;font-weight:600;text-decoration:none;">
                                Ver propuesta →
                              </a>
                            </td>
                          </tr>
                        </table>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:16px 32px;border-top:1px solid #f3f4f6;background:#fafafa;">
                        <p style="margin:0;font-size:12px;color:#9ca3af;text-align:center;">
                          Enviado desde Zentory · Plataforma para software factories
                        </p>
                      </td>
                    </tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        var email = new EmailMessage
        {
            From     = $"{_fromName} <{_fromEmail}>",
            Subject  = $"Tu propuesta fue abierta: {proposalTitle}",
            HtmlBody = html,
        };
        email.To.Add(toEmail);

        await _resend.EmailSendAsync(email, ct);
    }
}
