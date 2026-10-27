using Microsoft.Extensions.Configuration;
using Resend;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;

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

    // Aviso del tercer fallo de reautenticación (PLAN-0018 RF-81 g; SR-0018.42): sin credencial, nonce ni enlace con token.
    public async Task SendReauthenticationFailureWarningAsync(
        string    toEmail,
        string    recipientName,
        DateTime  occurredAtUtc,
        CancellationToken ct = default)
    {
        var name = System.Web.HttpUtility.HtmlEncode(recipientName);

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
                          Registramos varios intentos fallidos de confirmar tu identidad para una acción sensible de tu
                          organización (eliminarla o transferir su propiedad). El último fue el {occurredAtUtc:dd/MM/yyyy HH:mm} (UTC).
                        </p>
                        <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
                          Si fuiste tú, no tienes que hacer nada. Si no, cambia tu contraseña y cierra las sesiones abiertas.
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
            Subject  = "Intentos fallidos de confirmar tu identidad en Zentory",
            HtmlBody = html,
        };
        await SendAsync(email, toEmail, ct);
    }

    // Aviso de eliminación a un admin o member (PLAN-0018 RF-65 f; D-39): sin tokens ni enlaces con token, nombres
    // codificados (SR-0018.22). El asunto no lleva el nombre de la organización: es texto del encabezado, no HTML.
    public async Task SendOrganizationDeletedNoticeAsync(
        string    toEmail,
        string    recipientName,
        string    organizationName,
        CancellationToken ct = default)
    {
        var name         = System.Web.HttpUtility.HtmlEncode(recipientName);
        var organization = System.Web.HttpUtility.HtmlEncode(organizationName);

        var html = AccountNoticeLayout($"""
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">Hola <strong>{name}</strong>,</p>
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
              Se eliminó la organización <strong>{organization}</strong> en Zentory. Ya no tienes acceso a sus datos.
            </p>
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
              Si crees que es un error, comunícate con el propietario de la organización.
            </p>
            """);

        var email = new EmailMessage
        {
            From     = $"{_fromName} <{_fromEmail}>",
            Subject  = "Se eliminó una organización en la que participas",
            HtmlBody = html,
        };
        await SendAsync(email, toEmail, ct);
    }

    // Aviso de eliminación al owner con la fecha límite para restaurar (PLAN-0018 RF-65 f; D-39; SR-0018.42).
    public async Task SendOrganizationDeletedOwnerNoticeAsync(
        string    toEmail,
        string    recipientName,
        string    organizationName,
        DateTime  restoreDeadlineUtc,
        CancellationToken ct = default)
    {
        var name         = System.Web.HttpUtility.HtmlEncode(recipientName);
        var organization = System.Web.HttpUtility.HtmlEncode(organizationName);
        var deadline     = restoreDeadlineUtc.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        var html = AccountNoticeLayout($"""
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">Hola <strong>{name}</strong>,</p>
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
              Se eliminó <strong>{organization}</strong>; puedes restaurarla hasta el {deadline} (UTC). Después de esa
              fecha se borra de forma definitiva.
            </p>
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
              Si no fuiste tú, restáurala desde "Organizaciones eliminadas", cambia tu contraseña y cierra las sesiones abiertas.
            </p>
            """);

        var email = new EmailMessage
        {
            From     = $"{_fromName} <{_fromEmail}>",
            Subject  = "Se eliminó una de tus organizaciones en Zentory",
            HtmlBody = html,
        };
        await SendAsync(email, toEmail, ct);
    }

    // Enlace de reset de contraseña (PLAN-0018 RF-23, RF-25; SR-0018.22): el nombre y la URL se codifican; la URL viene
    // de App:BaseUrl, nunca del Host de la petición. El enlace lleva el token en claro: no se registra.
    public async Task SendPasswordResetEmailAsync(
        string    toEmail,
        string    recipientName,
        string    resetUrl,
        DateTime  expiresAtUtc,
        CancellationToken ct = default)
    {
        var name    = System.Web.HttpUtility.HtmlEncode(recipientName);
        var url     = System.Web.HttpUtility.HtmlEncode(resetUrl);
        var expires = expiresAtUtc.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        var html = AccountNoticeLayout($"""
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">Hola <strong>{name}</strong>,</p>
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
              Recibimos una solicitud para restablecer la contraseña de tu cuenta de Zentory. El enlace sirve una sola vez
              y vence el {expires} (UTC).
            </p>
            <table cellpadding="0" cellspacing="0" style="margin:0 0 24px;">
              <tr>
                <td style="background:#2563eb;border-radius:8px;">
                  <a href="{url}" style="display:inline-block;padding:12px 24px;color:#fff;font-size:15px;font-weight:600;text-decoration:none;">Restablecer contraseña</a>
                </td>
              </tr>
            </table>
            <p style="margin:0 0 16px;font-size:13px;color:#9ca3af;">
              O copia este enlace en tu navegador:<br/>
              <a href="{url}" style="color:#2563eb;word-break:break-all;">{url}</a>
            </p>
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
              Si no lo pediste, ignora este correo: tu contraseña no cambia.
            </p>
            """);

        var email = new EmailMessage
        {
            From     = $"{_fromName} <{_fromEmail}>",
            Subject  = "Restablece tu contraseña de Zentory",
            HtmlBody = html,
        };
        await SendAsync(email, toEmail, ct);
    }

    // Enlace de verificación de email (PLAN-0018 RF-20, RF-25; SR-0018.11 b, SR-0018.22): el nombre y la URL se
    // codifican; la URL viene de App:BaseUrl, nunca del Host de la petición. El enlace lleva el token en claro: no se
    // registra. Avisa que no se confirme si el destinatario no creó la cuenta.
    public async Task SendEmailVerificationEmailAsync(
        string    toEmail,
        string    recipientName,
        string    verifyUrl,
        DateTime  expiresAtUtc,
        CancellationToken ct = default)
    {
        var name    = System.Web.HttpUtility.HtmlEncode(recipientName);
        var url     = System.Web.HttpUtility.HtmlEncode(verifyUrl);
        var expires = expiresAtUtc.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        var html = AccountNoticeLayout($"""
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">Hola <strong>{name}</strong>,</p>
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
              Confirma tu correo para terminar de configurar tu cuenta de Zentory. El enlace sirve una sola vez y vence el
              {expires} (UTC).
            </p>
            <table cellpadding="0" cellspacing="0" style="margin:0 0 24px;">
              <tr>
                <td style="background:#2563eb;border-radius:8px;">
                  <a href="{url}" style="display:inline-block;padding:12px 24px;color:#fff;font-size:15px;font-weight:600;text-decoration:none;">Confirmar mi correo</a>
                </td>
              </tr>
            </table>
            <p style="margin:0 0 16px;font-size:13px;color:#9ca3af;">
              O copia este enlace en tu navegador:<br/>
              <a href="{url}" style="color:#2563eb;word-break:break-all;">{url}</a>
            </p>
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
              Si no creaste una cuenta en Zentory, no confirmes este correo e ignóralo.
            </p>
            """);

        var email = new EmailMessage
        {
            From     = $"{_fromName} <{_fromEmail}>",
            Subject  = "Confirma tu correo en Zentory",
            HtmlBody = html,
        };
        await SendAsync(email, toEmail, ct);
    }

    // Invitación a una organización (PLAN-0018 RF-49 c, RF-25; SR-0018.22): los nombres del invitado, del invitador y de
    // la organización y la URL se codifican; la URL viene de App:BaseUrl, nunca del Host de la petición. El enlace lleva
    // el token en claro: no se registra. El asunto no lleva valores de usuario.
    public async Task SendOrganizationInvitationEmailAsync(
        string    toEmail,
        string?   inviteeName,
        string    inviterName,
        string    organizationName,
        string    role,
        string    acceptUrl,
        DateTime  expiresAtUtc,
        CancellationToken ct = default)
    {
        var greeting     = string.IsNullOrWhiteSpace(inviteeName)
            ? "Hola,"
            : $"Hola <strong>{System.Web.HttpUtility.HtmlEncode(inviteeName)}</strong>,";
        var inviter      = System.Web.HttpUtility.HtmlEncode(inviterName);
        var organization = System.Web.HttpUtility.HtmlEncode(organizationName);
        var roleName     = role == OrgRole.Admin ? "administrador" : "miembro";
        var url          = System.Web.HttpUtility.HtmlEncode(acceptUrl);
        var expires      = expiresAtUtc.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        var html = AccountNoticeLayout($"""
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">{greeting}</p>
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
              <strong>{inviter}</strong> te invitó a unirte a <strong>{organization}</strong> en Zentory como {roleName}.
              La invitación sirve una sola vez y vence el {expires} (UTC).
            </p>
            <table cellpadding="0" cellspacing="0" style="margin:0 0 24px;">
              <tr>
                <td style="background:#2563eb;border-radius:8px;">
                  <a href="{url}" style="display:inline-block;padding:12px 24px;color:#fff;font-size:15px;font-weight:600;text-decoration:none;">Aceptar la invitación</a>
                </td>
              </tr>
            </table>
            <p style="margin:0 0 16px;font-size:13px;color:#9ca3af;">
              O copia este enlace en tu navegador:<br/>
              <a href="{url}" style="color:#2563eb;word-break:break-all;">{url}</a>
            </p>
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
              Si no esperabas esta invitación, ignora este correo.
            </p>
            """);

        var email = new EmailMessage
        {
            From     = $"{_fromName} <{_fromEmail}>",
            Subject  = "Te invitaron a una organización en Zentory",
            HtmlBody = html,
        };
        await SendAsync(email, toEmail, ct);
    }

    // Propuesta de transferencia al destino (PLAN-0018 RF-64 c; SR-0018.22): sin token ni enlace con token; el nombre del
    // proponente y el de la organización se codifican. El asunto no lleva valores de usuario.
    public async Task SendOwnershipTransferProposedEmailAsync(
        string    toEmail,
        string    recipientName,
        string    proposerName,
        string    organizationName,
        DateTime  expiresAtUtc,
        CancellationToken ct = default)
    {
        var name         = System.Web.HttpUtility.HtmlEncode(recipientName);
        var proposer     = System.Web.HttpUtility.HtmlEncode(proposerName);
        var organization = System.Web.HttpUtility.HtmlEncode(organizationName);
        var expires      = expiresAtUtc.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        var html = AccountNoticeLayout($"""
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">Hola <strong>{name}</strong>,</p>
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
              <strong>{proposer}</strong> te propuso transferirte la propiedad de <strong>{organization}</strong>.
              Revisa el aviso en Zentory antes del {expires} (UTC): ahí ves qué cambia para ti y puedes aceptar o rechazar.
            </p>
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
              Si no esperabas esta propuesta, ignora este correo: nada cambia mientras no la aceptes.
            </p>
            """);

        var email = new EmailMessage
        {
            From     = $"{_fromName} <{_fromEmail}>",
            Subject  = "Te propusieron la propiedad de una organización en Zentory",
            HtmlBody = html,
        };
        await SendAsync(email, toEmail, ct);
    }

    // Aviso al owner de la propuesta hecha desde su cuenta (PLAN-0018 RF-64 c; SR-0018.42): sin tokens; nombres codificados.
    public async Task SendOwnershipTransferProposedOwnerNoticeAsync(
        string    toEmail,
        string    recipientName,
        string    targetName,
        string    organizationName,
        DateTime  expiresAtUtc,
        CancellationToken ct = default)
    {
        var name         = System.Web.HttpUtility.HtmlEncode(recipientName);
        var target       = System.Web.HttpUtility.HtmlEncode(targetName);
        var organization = System.Web.HttpUtility.HtmlEncode(organizationName);
        var expires      = expiresAtUtc.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        var html = AccountNoticeLayout($"""
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">Hola <strong>{name}</strong>,</p>
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
              Desde tu cuenta se propuso transferir la propiedad de <strong>{organization}</strong> a
              <strong>{target}</strong>. La propuesta vence el {expires} (UTC) y nada cambia mientras no la acepte.
            </p>
            <p style="margin:0 0 16px;font-size:15px;color:#374151;line-height:1.6;">
              Si no fuiste tú, cancela la propuesta desde Zentory, cambia tu contraseña y cierra las sesiones abiertas.
            </p>
            """);

        var email = new EmailMessage
        {
            From     = $"{_fromName} <{_fromEmail}>",
            Subject  = "Se propuso transferir una de tus organizaciones en Zentory",
            HtmlBody = html,
        };
        await SendAsync(email, toEmail, ct);
    }

    // Marco común de los avisos de identidad y acceso (eliminación, reset, verificación); el contenido llega ya codificado.
    private static string AccountNoticeLayout(string content) => $"""
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
                    {content}
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
}
