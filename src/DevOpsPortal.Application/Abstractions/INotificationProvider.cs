namespace DevOpsPortal.Application.Abstractions;

/// <summary>Recipients are channel-specific addresses — email addresses for
/// EmailNotificationProvider today; a future Slack/Teams provider would take
/// channel or user IDs instead. Deliberately provider-agnostic, mirroring
/// IBuildProvider/ISecretProvider's shape.</summary>
public record NotificationMessage(IReadOnlyList<string> Recipients, string Subject, string Body);

/// <summary>
/// A channel capable of delivering a notification. INotificationService
/// (Application) broadcasts every event to every registered provider — never
/// throwing if one fails, so one channel's outage can't block another or
/// block the workflow event itself (master requirements: notification
/// delivery must never gate approvals/deployments — same principle Phase 3
/// established for the original CTO email). The only implementation today is
/// EmailNotificationProvider (Infrastructure, wraps IEmailSender/SMTP); a
/// future Slack/Teams/webhook provider is a new class + one DI registration,
/// no change to INotificationService or any caller.
/// </summary>
public interface INotificationProvider
{
    Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}
