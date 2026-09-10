namespace DevOpsPortal.Domain.Enums;

/// <summary>
/// Lifecycle of a <see cref="Entities.BuildRequest"/>, modeled honestly after
/// Jenkins's real two-phase async flow: triggering a job only ever returns a
/// queue item reference, never a build number — the queue item resolves to a
/// build number (and only then can "building" vs. "complete" be observed) some
/// unknown time later. There is no synchronous "build finished" response.
/// </summary>
public enum BuildStatus
{
    /// <summary>Persisted locally; the trigger call to the build provider has not
    /// necessarily succeeded yet (see BuildRequest.ErrorMessage if it failed).</summary>
    Requested = 0,

    /// <summary>The provider accepted the trigger and returned a queue reference;
    /// no build number is known yet.</summary>
    Queued = 1,

    /// <summary>The queue item resolved to a build number and the build is running.</summary>
    Running = 2,

    Succeeded = 3,

    /// <summary>Either the provider rejected/failed the trigger call itself, or the
    /// build ran and finished with a non-success result (FAILURE/ABORTED/UNSTABLE
    /// in Jenkins terms) — BuildRequest.ErrorMessage distinguishes which.</summary>
    Failed = 4,
}
