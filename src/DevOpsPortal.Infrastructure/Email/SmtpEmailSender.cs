using System.Net;
using System.Net.Mail;
using DevOpsPortal.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsPortal.Infrastructure.Email;

public class SmtpEmailSender(IOptions<SmtpSettings> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.FromAddress))
        {
            logger.LogWarning(
                "SMTP is not configured (Smtp:Host / Smtp:FromAddress) — email to {RecipientCount} recipient(s) was not sent: {Subject}",
                message.ToAddresses.Count, message.Subject);
            return false;
        }

        try
        {
            using var client = new SmtpClient(settings.Host, settings.Port) { EnableSsl = settings.EnableSsl };
            if (!string.IsNullOrWhiteSpace(settings.Username))
                client.Credentials = new NetworkCredential(settings.Username, settings.Password);

            using var mail = new MailMessage
            {
                From = new MailAddress(settings.FromAddress, settings.FromName),
                Subject = message.Subject,
                Body = message.Body,
                IsBodyHtml = false,
            };
            foreach (var to in message.ToAddresses)
                mail.To.Add(to);

            await client.SendMailAsync(mail, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            logger.LogError(ex, "Failed to send email ({Subject}) to {RecipientCount} recipient(s)", message.Subject, message.ToAddresses.Count);
            return false;
        }
    }
}
