using DevOpsPortal.Application.Abstractions;

namespace DevOpsPortal.Infrastructure.Notifications;

/// <summary>Thin adapter onto the existing IEmailSender/SmtpEmailSender (SMTP
/// config, credential handling, and the graceful-no-op-when-unconfigured
/// behavior all already live there, untouched by this phase) — this class
/// exists only to give email a place in the provider-agnostic
/// INotificationProvider list alongside whatever channel (Slack/Teams/
/// webhook) is added next.</summary>
public class EmailNotificationProvider(IEmailSender emailSender) : INotificationProvider
{
    public Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default) =>
        emailSender.SendAsync(new EmailMessage(message.Recipients, message.Subject, message.Body), cancellationToken);
}
