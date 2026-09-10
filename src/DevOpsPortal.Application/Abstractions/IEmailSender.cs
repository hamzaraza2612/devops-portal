namespace DevOpsPortal.Application.Abstractions;

public record EmailMessage(IReadOnlyList<string> ToAddresses, string Subject, string Body);

/// <summary>
/// Abstracted so the SMTP implementation (or a future provider) can change
/// without touching callers. SendAsync returns false rather than throwing on
/// failure/missing configuration — a CTO-approval email that can't be sent
/// must never block the approval record itself from being created (master
/// requirements §10: production deployment stays blocked on approval status
/// in the database, not on whether an email happened to go out).
/// </summary>
public interface IEmailSender
{
    Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
