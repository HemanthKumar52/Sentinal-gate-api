using System.Net;
using System.Net.Mail;

namespace SentinelGate.Notification.Service.Services;

public class EmailNotifier
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailNotifier> _logger;

    public EmailNotifier(IConfiguration configuration, ILogger<EmailNotifier> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string to, string subject, string body)
    {
        try
        {
            var smtpHost = _configuration["Notification:Smtp:Host"] ?? "localhost";
            var smtpPort = int.Parse(_configuration["Notification:Smtp:Port"] ?? "587");
            var smtpUser = _configuration["Notification:Smtp:Username"] ?? string.Empty;
            var smtpPass = _configuration["Notification:Smtp:Password"] ?? string.Empty;
            var fromAddress = _configuration["Notification:Smtp:FromAddress"] ?? "noreply@sentinelgate.io";
            var enableSsl = bool.Parse(_configuration["Notification:Smtp:EnableSsl"] ?? "true");

            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                Credentials = new NetworkCredential(smtpUser, smtpPass),
                EnableSsl = enableSsl
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(fromAddress, "SentinelGate"),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            mailMessage.To.Add(to);

            await client.SendMailAsync(mailMessage);

            _logger.LogInformation("Email sent successfully to {To} with subject '{Subject}'", to, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", to);
            return false;
        }
    }
}
