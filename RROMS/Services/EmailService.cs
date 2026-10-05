using System.Net;
using System.Net.Mail;

namespace RROMS.Services;

public interface IEmailService
{
    /// <summary>True when SmtpSettings has a real host, username and password (placeholders do not count).</summary>
    bool IsConfigured { get; }

    /// <summary>Sends one plain-text email. Never throws: the result says what went wrong in plain words.</summary>
    Task<(bool Success, string Message)> SendAsync(string toEmail, string subject, string body);
}

/// <summary>
/// Sends mail through any SMTP server (Gmail, Outlook, Mailtrap, your own). All values come from the
/// SmtpSettings section - see INTEGRATIONS.md for the Gmail "app password" walkthrough.
/// </summary>
public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    private bool Enabled => _config.GetValue<bool?>("SmtpSettings:Enabled") ?? true;

    public bool IsConfigured =>
        Enabled
        && IntegrationSettings.IsReal(_config["SmtpSettings:Host"])
        && IntegrationSettings.IsReal(_config["SmtpSettings:Username"])
        && IntegrationSettings.IsReal(_config["SmtpSettings:Password"]);

    public async Task<(bool Success, string Message)> SendAsync(string toEmail, string subject, string body)
    {
        if (!IsConfigured)
            return (false, "Email isn't set up yet. Fill in SmtpSettings (Host, Username, Password) - see INTEGRATIONS.md.");

        try
        {
            var host = _config["SmtpSettings:Host"]!;
            var port = _config.GetValue<int?>("SmtpSettings:Port") ?? 587;
            var useSsl = _config.GetValue<bool?>("SmtpSettings:UseSsl") ?? true;
            var username = _config["SmtpSettings:Username"]!;
            var password = _config["SmtpSettings:Password"]!;
            var fromName = _config["SmtpSettings:FromName"] ?? "RROMS Restaurant";
            var configuredFrom = _config["SmtpSettings:FromAddress"];
            // Gmail and most providers only allow sending "from" the signed-in mailbox, so fall back to it.
            var fromAddress = IntegrationSettings.IsReal(configuredFrom) ? configuredFrom! : username;

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = useSsl,
                Credentials = new NetworkCredential(username, password),
                Timeout = 15000
            };

            using var mail = new MailMessage
            {
                From = new MailAddress(fromAddress, fromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };
            mail.To.Add(toEmail);

            await client.SendMailAsync(mail);
            return (true, "Sent.");
        }
        catch (Exception ex)
        {
            // Email is a courtesy channel - a mail failure must never break a reservation/order flow.
            _logger.LogWarning(ex, "Failed to send email to {Email}", toEmail);
            return (false, ex.Message);
        }
    }
}
