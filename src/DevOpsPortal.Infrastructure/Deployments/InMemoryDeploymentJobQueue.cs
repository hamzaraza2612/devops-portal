using System.Threading.Channels;
using DevOpsPortal.Application.Abstractions;

namespace DevOpsPortal.Infrastructure.Deployments;

/// <summary>In-process queue backed by an unbounded Channel — survives only for the
/// life of the API process (see PROJECT_STATE.md known limitations: a deployment
/// queued right before a restart is lost; a real broker is a Phase 4 concern).</summary>
public class InMemoryDeploymentJobQueue : IDeploymentJobQueue
{
    private readonly Channel<DeploymentJob> _channel = Channel.CreateUnbounded<DeploymentJob>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false,
    });

    public void Enqueue(DeploymentJob job) => _channel.Writer.TryWrite(job);

    public async Task<DeploymentJob> DequeueAsync(CancellationToken cancellationToken) =>
        await _channel.Reader.ReadAsync(cancellationToken);
}
