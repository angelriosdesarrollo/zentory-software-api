using Microsoft.Extensions.Configuration;
using Resend;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Infrastructure.Services;

public sealed class ResendEmailService : IEmailService
{
    private readonly IResend        _resend;
    private readonly string         _fromEmail;
    private readonly string         _fromName;
    private readonly string?        _devOverrideRecipient;

    public ResendEmailService(IResend resend, IConfiguration configuration)
    {
        _resend    = resend;
        _fromEmail = configuration["Resend:FromEmail"] ?? "noreply@zentory.co";
        _fromName  = configuration["Resend:FromName"]  ?? "Zentory";

        // Si está seteado (solo en appsettings.Development.json, gitignored), todos los correos
        // se redirigen a esta dirección en vez del destinatario real — necesario porque el dominio
        // de pruebas de Resend (onboarding@resend.dev) solo permite enviar a la cuenta registrada.
        _devOverrideRecipient = configuration["Resend:DevOverrideRecipient"];
    }

    private Task SendAsync(EmailMessage email, string toEmail, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_devOverrideRecipient))
        {
            email.Subject = $"[dev → {toEmail}] {email.Subject}";
            email.To.Add(_devOverrideRecipient);
        }
        else
        {
            email.To.Add(toEmail);
        }

        return _resend.EmailSendAsync(email, ct);
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
        await SendAsync(email, toEmail, ct);
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
        await SendAsync(email, toEmail, ct);
    }

    public async Task SendPilaRequestEmailAsync(
        string    toEmail,
        string    collaboratorName,
        string    companyName,
        string    period,
        string    uploadUrl,
        DateTime  expiresAt,
        CancellationToken ct = default)
    {
        var name    = System.Web.HttpUtility.HtmlEncode(collaboratorName);
        var company = System.Web.HttpUtility.HtmlEncode(companyName);

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
                        <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">Hola <strong>{name}</strong>,</p>
                        <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
                          <strong>{company}</strong> necesita verificar que realizaste tu pago de seguridad social (PILA) correspondiente a
                          <strong>{period}</strong>, como parte del cumplimiento de la Ley 1150 de 2007.
                        </p>
                        <table cellpadding="0" cellspacing="0" style="margin:24px 0;">
                          <tr>
                            <td style="background:#2563eb;border-radius:8px;">
                              <a href="{uploadUrl}" target="_blank"
                                 style="display:inline-block;padding:12px 28px;color:#fff;font-size:14px;font-weight:600;text-decoration:none;">
                                Subir comprobante →
                              </a>
                            </td>
                          </tr>
                        </table>
                        <p style="margin:0 0 16px;font-size:13px;color:#6b7280;">
                          Solo necesitas tu comprobante de pago en PDF o imagen. El enlace vence el {expiresAt:dd/MM/yyyy}.
                        </p>
                        <p style="margin:0;font-size:13px;color:#9ca3af;">
                          O copia este enlace en tu navegador:<br/>
                          <a href="{uploadUrl}" style="color:#2563eb;word-break:break-all;">{uploadUrl}</a>
                        </p>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:16px 32px;border-top:1px solid #f3f4f6;background:#fafafa;">
                        <p style="margin:0;font-size:12px;color:#9ca3af;text-align:center;">
                          Equipo de {company}, vía Zentory · Plataforma para software factories
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
            Subject  = $"{companyName} solicita tu comprobante de pago de planilla — {period}",
            HtmlBody = html,
        };
        await SendAsync(email, toEmail, ct);
    }

    public async Task SendPayoutInvoiceGeneratedEmailAsync(
        string    toEmail,
        string    collaboratorName,
        string    companyName,
        string    period,
        decimal   amount,
        string    currency,
        string    downloadUrl,
        string    portalUrl,
        CancellationToken ct = default)
    {
        var name    = System.Web.HttpUtility.HtmlEncode(collaboratorName);
        var company = System.Web.HttpUtility.HtmlEncode(companyName);

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
                        <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
                          Hola <strong>{name}</strong>, <strong>{company}</strong> generó tu cuenta de cobro correspondiente a
                          <strong>{period}</strong> por un valor de <strong>{amount:N0} {currency}</strong>.
                        </p>
                        <table cellpadding="0" cellspacing="0" style="margin:24px 0;">
                          <tr>
                            <td style="background:#2563eb;border-radius:8px;">
                              <a href="{downloadUrl}" target="_blank"
                                 style="display:inline-block;padding:12px 28px;color:#fff;font-size:14px;font-weight:600;text-decoration:none;">
                                Descargar cuenta de cobro →
                              </a>
                            </td>
                          </tr>
                        </table>
                        <p style="margin:0 0 16px;font-size:13px;color:#9ca3af;">
                          O copia este enlace en tu navegador:<br/>
                          <a href="{downloadUrl}" style="color:#2563eb;word-break:break-all;">{downloadUrl}</a>
                        </p>
                        <p style="margin:0 0 8px;font-size:14px;color:#374151;line-height:1.6;">
                          Si necesitas firmarla, descárgala, fírmala fuera de la plataforma y súbela de vuelta desde tu portal de colaborador:
                        </p>
                        <p style="margin:0;font-size:13px;color:#9ca3af;">
                          <a href="{portalUrl}" style="color:#2563eb;word-break:break-all;">{portalUrl}</a>
                        </p>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:16px 32px;border-top:1px solid #f3f4f6;background:#fafafa;">
                        <p style="margin:0;font-size:12px;color:#9ca3af;text-align:center;">
                          Equipo de {company}, vía Zentory · Plataforma para software factories
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
            Subject  = $"Tu cuenta de cobro de {period} está lista",
            HtmlBody = html,
        };
        await SendAsync(email, toEmail, ct);
    }

    public async Task SendPayoutInvoiceRequestEmailAsync(
        string    toEmail,
        string    collaboratorName,
        string    companyName,
        string    period,
        string    uploadUrl,
        DateTime  expiresAt,
        CancellationToken ct = default)
    {
        var name    = System.Web.HttpUtility.HtmlEncode(collaboratorName);
        var company = System.Web.HttpUtility.HtmlEncode(companyName);

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
                        <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">Hola <strong>{name}</strong>,</p>
                        <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
                          <strong>{company}</strong> te solicita tu cuenta de cobro correspondiente a <strong>{period}</strong>.
                        </p>
                        <table cellpadding="0" cellspacing="0" style="margin:24px 0;">
                          <tr>
                            <td style="background:#2563eb;border-radius:8px;">
                              <a href="{uploadUrl}" target="_blank"
                                 style="display:inline-block;padding:12px 28px;color:#fff;font-size:14px;font-weight:600;text-decoration:none;">
                                Subir cuenta de cobro →
                              </a>
                            </td>
                          </tr>
                        </table>
                        <p style="margin:0 0 16px;font-size:13px;color:#6b7280;">
                          El enlace vence el {expiresAt:dd/MM/yyyy}.
                        </p>
                        <p style="margin:0;font-size:13px;color:#9ca3af;">
                          O copia este enlace en tu navegador:<br/>
                          <a href="{uploadUrl}" style="color:#2563eb;word-break:break-all;">{uploadUrl}</a>
                        </p>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:16px 32px;border-top:1px solid #f3f4f6;background:#fafafa;">
                        <p style="margin:0;font-size:12px;color:#9ca3af;text-align:center;">
                          Equipo de {company}, vía Zentory · Plataforma para software factories
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
            Subject  = $"{companyName} te solicita tu cuenta de cobro — {period}",
            HtmlBody = html,
        };
        await SendAsync(email, toEmail, ct);
    }

    public async Task SendCollaboratorPortalAccessEmailAsync(
        string    toEmail,
        string    collaboratorName,
        string    magicLinkUrl,
        DateTime  expiresAt,
        CancellationToken ct = default)
    {
        var name = System.Web.HttpUtility.HtmlEncode(collaboratorName);

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
                        <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">Hola <strong>{name}</strong>,</p>
                        <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
                          Pediste acceder a tu portal de colaborador para subir o revisar tus planillas PILA y cuentas de cobro.
                        </p>
                        <table cellpadding="0" cellspacing="0" style="margin:24px 0;">
                          <tr>
                            <td style="background:#2563eb;border-radius:8px;">
                              <a href="{magicLinkUrl}" target="_blank"
                                 style="display:inline-block;padding:12px 28px;color:#fff;font-size:14px;font-weight:600;text-decoration:none;">
                                Entrar al portal →
                              </a>
                            </td>
                          </tr>
                        </table>
                        <p style="margin:0 0 16px;font-size:13px;color:#6b7280;">
                          El enlace vence el {expiresAt:dd/MM/yyyy HH:mm} y solo se puede usar una vez. Si tú no lo pediste, ignora este correo.
                        </p>
                        <p style="margin:0;font-size:13px;color:#9ca3af;">
                          O copia este enlace en tu navegador:<br/>
                          <a href="{magicLinkUrl}" style="color:#2563eb;word-break:break-all;">{magicLinkUrl}</a>
                        </p>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:16px 32px;border-top:1px solid #f3f4f6;background:#fafafa;">
                        <p style="margin:0;font-size:12px;color:#9ca3af;text-align:center;">
                          Vía Zentory · Plataforma para software factories
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
            Subject  = "Tu enlace de acceso al portal de colaborador",
            HtmlBody = html,
        };
        await SendAsync(email, toEmail, ct);
    }

    public async Task SendPilaRejectedEmailAsync(
        string    toEmail,
        string    collaboratorName,
        string    companyName,
        string    period,
        string    notes,
        string    portalUrl,
        CancellationToken ct = default)
    {
        var name    = System.Web.HttpUtility.HtmlEncode(collaboratorName);
        var company = System.Web.HttpUtility.HtmlEncode(companyName);
        var reason  = System.Web.HttpUtility.HtmlEncode(notes);

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
                        <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">Hola <strong>{name}</strong>,</p>
                        <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
                          <strong>{company}</strong> rechazó tu comprobante de pago de planilla (PILA) correspondiente a <strong>{period}</strong>.
                        </p>
                        <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 20px;background:#fef2f2;border:1px solid #fecaca;border-radius:8px;">
                          <tr>
                            <td style="padding:12px 16px;">
                              <p style="margin:0;font-size:13px;color:#b91c1c;"><strong>Motivo:</strong> {reason}</p>
                            </td>
                          </tr>
                        </table>
                        <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
                          Ingresa a tu portal para subir el comprobante correcto.
                        </p>
                        <table cellpadding="0" cellspacing="0" style="margin:24px 0;">
                          <tr>
                            <td style="background:#2563eb;border-radius:8px;">
                              <a href="{portalUrl}" target="_blank"
                                 style="display:inline-block;padding:12px 28px;color:#fff;font-size:14px;font-weight:600;text-decoration:none;">
                                Ir al portal →
                              </a>
                            </td>
                          </tr>
                        </table>
                        <p style="margin:0;font-size:13px;color:#9ca3af;">
                          O copia este enlace en tu navegador:<br/>
                          <a href="{portalUrl}" style="color:#2563eb;word-break:break-all;">{portalUrl}</a>
                        </p>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:16px 32px;border-top:1px solid #f3f4f6;background:#fafafa;">
                        <p style="margin:0;font-size:12px;color:#9ca3af;text-align:center;">
                          Equipo de {company}, vía Zentory · Plataforma para software factories
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
            Subject  = $"{companyName} rechazó tu planilla de {period}",
            HtmlBody = html,
        };
        await SendAsync(email, toEmail, ct);
    }

    public async Task SendPayoutInvoiceRejectedEmailAsync(
        string    toEmail,
        string    collaboratorName,
        string    companyName,
        string    period,
        string    notes,
        string    portalUrl,
        CancellationToken ct = default)
    {
        var name    = System.Web.HttpUtility.HtmlEncode(collaboratorName);
        var company = System.Web.HttpUtility.HtmlEncode(companyName);
        var reason  = System.Web.HttpUtility.HtmlEncode(notes);

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
                        <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">Hola <strong>{name}</strong>,</p>
                        <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
                          <strong>{company}</strong> rechazó tu cuenta de cobro correspondiente a <strong>{period}</strong>.
                        </p>
                        <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 20px;background:#fef2f2;border:1px solid #fecaca;border-radius:8px;">
                          <tr>
                            <td style="padding:12px 16px;">
                              <p style="margin:0;font-size:13px;color:#b91c1c;"><strong>Motivo:</strong> {reason}</p>
                            </td>
                          </tr>
                        </table>
                        <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
                          Ingresa a tu portal para subir la cuenta de cobro correcta.
                        </p>
                        <table cellpadding="0" cellspacing="0" style="margin:24px 0;">
                          <tr>
                            <td style="background:#2563eb;border-radius:8px;">
                              <a href="{portalUrl}" target="_blank"
                                 style="display:inline-block;padding:12px 28px;color:#fff;font-size:14px;font-weight:600;text-decoration:none;">
                                Ir al portal →
                              </a>
                            </td>
                          </tr>
                        </table>
                        <p style="margin:0;font-size:13px;color:#9ca3af;">
                          O copia este enlace en tu navegador:<br/>
                          <a href="{portalUrl}" style="color:#2563eb;word-break:break-all;">{portalUrl}</a>
                        </p>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:16px 32px;border-top:1px solid #f3f4f6;background:#fafafa;">
                        <p style="margin:0;font-size:12px;color:#9ca3af;text-align:center;">
                          Equipo de {company}, vía Zentory · Plataforma para software factories
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
            Subject  = $"{companyName} rechazó tu cuenta de cobro de {period}",
            HtmlBody = html,
        };
        await SendAsync(email, toEmail, ct);
    }
}
