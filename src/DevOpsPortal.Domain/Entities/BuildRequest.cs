using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// One request to build an application from source via a configured
/// <see cref="BuildServer"/>/job. Requesting a build never deploys anything —
/// see master requirements §3 ("do not automatically deploy merely because a
/// build succeeds"). A successful, resolved build produces exactly one
/// <see cref="Release"/> (see Release.BuildRequestId); a failed one does not.
/// </summary>
public class BuildRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ApplicationId { get; set; }
    public ManagedApplication Application { get; set; } = null!;

    /// <summary>Snapshot of which BuildServer/job were used — configuration can
    /// change later without altering the historical record of what was requested.</summary>
    public Guid BuildServerId { get; set; }
    public BuildServer BuildServer { get; set; } = null!;
    public string JobName { get; set; } = string.Empty;

    public string? Branch { get; set; }

    /// <summary>Caller-supplied or resolved commit SHA this build is for. May be
    /// null at request time if the provider job itself resolves the commit
    /// (e.g. "build HEAD of branch X") — in that case it stays null until known.</summary>
    public string? CommitSha { get; set; }

    /// <summary>Only meaningful when the application's ImageTagStrategy is SemVer —
    /// there is no way to derive a semantic version automatically, so it must be
    /// supplied by the caller at request time. Null for every other strategy.</summary>
    public string? RequestedSemVer { get; set; }

    public BuildStatus Status { get; set; } = BuildStatus.Requested;

    /// <summary>Jenkins queue item id returned immediately by the trigger call —
    /// not a build number. Null once BuildNumber is resolved (kept for traceability
    /// even after resolution).</summary>
    public string? ProviderQueueItemId { get; set; }

    /// <summary>Only known once the provider's queue item resolves to an executable
    /// build — see BuildStatus.Queued vs Running.</summary>
    public int? BuildNumber { get; set; }

    /// <summary>Link to the build on the provider (e.g. Jenkins build page), for a
    /// human to open — never used by this portal to construct further requests.</summary>
    public string? BuildUrl { get; set; }

    /// <summary>Set when Status is Failed — the provider's own failure reason, or a
    /// portal-side error (e.g. "no build provider registered"). Sanitized before
    /// storage; never contains raw secrets.</summary>
    public string? ErrorMessage { get; set; }

    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public Release? Release { get; set; }
}
