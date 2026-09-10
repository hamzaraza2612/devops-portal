namespace DevOpsPortal.Application.Abstractions;

/// <summary>Resolved from the current HTTP request by the API layer.</summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Username { get; }
    string? IpAddress { get; }
}
