using DevOpsPortal.Application.Abstractions;

namespace DevOpsPortal.Infrastructure.Deployments;

/// <summary>The `docker compose` argument list for each allowed ComposeOperation —
/// shared by every executor that needs to run one (local ComposeCommandExecutor,
/// SshRemoteExecutionProvider) so the allow-listed argument set is defined in
/// exactly one place.</summary>
internal static class ComposeOperationArgs
{
    public static string[] For(ComposeOperation operation) => operation switch
    {
        ComposeOperation.Up => ["up", "-d"],
        ComposeOperation.Down => ["down"],
        ComposeOperation.DownWithVolumes => ["down", "-v"],
        ComposeOperation.Restart => ["restart"],
        ComposeOperation.Start => ["start"],
        ComposeOperation.Stop => ["stop"],
        ComposeOperation.Ps => ["ps", "-a", "--format", "json"],
        ComposeOperation.Pull => ["pull"],
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported compose operation."),
    };
}
