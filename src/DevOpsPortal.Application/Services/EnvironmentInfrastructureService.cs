using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Containers;
using DevOpsPortal.Application.Dtos.Infrastructure;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

/// <summary>
/// See IEnvironmentInfrastructureService's doc comment. This class never
/// converts a connection failure into empty-looking data: SshConnected/
/// DockerAvailable/ErrorMessage on EnvironmentServerInfoDto always say exactly
/// what was and wasn't reached, and the frontend is expected to render that
/// distinction rather than a bare "no containers" (master requirement §12
/// "failure handling" — never silently show empty data for a real failure).
/// </summary>
public class EnvironmentInfrastructureService(
    IAppDbContext db,
    ICurrentUserService currentUser,
    IAuditService auditService,
    IRemoteExecutionProvider remoteExecutionProvider) : IEnvironmentInfrastructureService
{
    private const int DefaultTailLines = 200;
    private const int MaxTailLines = 5000;

    public async Task<EnvironmentInfrastructureDto> GetInfrastructureAsync(Guid environmentDefinitionId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.ContainersView, cancellationToken);
        await EnsureEnvironmentAccessAsync(userId, environmentDefinitionId, cancellationToken);

        var environmentDefinition = await db.EnvironmentDefinitions
            .Include(e => e.PrimaryTargetServer)
            .FirstOrDefaultAsync(e => e.Id == environmentDefinitionId, cancellationToken)
            ?? throw new NotFoundException("EnvironmentDefinition", environmentDefinitionId);

        if (environmentDefinition.PrimaryTargetServer is not { } server)
        {
            return new EnvironmentInfrastructureDto(
                environmentDefinitionId, environmentDefinition.Name,
                new EnvironmentServerInfoDto(
                    null, null, null, IsConfigured: false, SshConnected: false, null, null, null,
                    false, null, false, null, null,
                    "No target server is assigned to this environment yet — an admin can assign one from the Environments admin page.",
                    DateTimeOffset.UtcNow),
                []);
        }

        var connectionTask = remoteExecutionProvider.TestConnectionAsync(server, cancellationToken);
        var metricsTask = remoteExecutionProvider.GetHostMetricsAsync(server, cancellationToken);
        await Task.WhenAll(connectionTask, metricsTask);
        var connection = connectionTask.Result;
        var metrics = metricsTask.Result;

        var hostMetrics = connection.SshConnected ? ToHostMetricsDto(metrics) : null;

        var serverInfo = new EnvironmentServerInfoDto(
            server.Id, server.Name, server.Hostname, IsConfigured: true,
            connection.SshConnected, connection.AuthenticatedUser, connection.OsInfo, connection.UptimeInfo,
            connection.DockerAvailable, connection.DockerVersion, connection.ComposeAvailable, connection.ComposeVersion,
            hostMetrics, connection.ErrorMessage, DateTimeOffset.UtcNow);

        // Never silently turn "couldn't connect"/"Docker isn't available" into an
        // empty container list that looks the same as "genuinely zero
        // containers" — the caller must be able to tell these apart from
        // serverInfo alone (master requirement §12).
        if (!connection.SshConnected || !connection.DockerAvailable)
            return new EnvironmentInfrastructureDto(environmentDefinitionId, environmentDefinition.Name, serverInfo, []);

        var discovery = await remoteExecutionProvider.DiscoverContainersAsync(server, cancellationToken);
        if (!discovery.Success)
        {
            var failureInfo = serverInfo with { ErrorMessage = discovery.Error ?? "Container discovery failed." };
            return new EnvironmentInfrastructureDto(environmentDefinitionId, environmentDefinition.Name, failureInfo, []);
        }

        var containers = await MapDiscoveredContainersAsync(environmentDefinitionId, server.Id, discovery, cancellationToken);
        return new EnvironmentInfrastructureDto(environmentDefinitionId, environmentDefinition.Name, serverInfo, containers);
    }

    public async Task<ContainerActionResultDto> RunContainerActionAsync(
        Guid environmentDefinitionId, string containerId, RemoteContainerAction action, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.ContainersControl, cancellationToken);
        await EnsureEnvironmentAccessAsync(userId, environmentDefinitionId, cancellationToken);

        var server = await ResolveConfiguredServerAsync(environmentDefinitionId, cancellationToken);

        // Never trust a browser-supplied container id blindly — re-confirm it is
        // really a container on THIS environment's target server right now
        // (master requirement §4/§10), not just once at page load.
        var discovery = await remoteExecutionProvider.DiscoverContainersAsync(server, cancellationToken);
        if (!discovery.Success)
            throw new ValidationException(discovery.Error ?? "Could not reach the target server to verify this container.");

        var known = DockerDiscoveryParser.ParseInspectArray(discovery.InspectJson);
        if (!known.Any(c => string.Equals(c.ContainerId, containerId, StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException("This container was not found on the target server for this environment.");

        var result = await remoteExecutionProvider.RunContainerActionAsync(server, containerId, action, cancellationToken);
        var sanitizedDetail = LogSanitizer.Sanitize(result.Success ? result.Message : result.Error ?? "Action failed.");

        await auditService.LogAsync(
            $"environment.infrastructure.container.{action.ToString().ToLowerInvariant()}",
            result.Success ? AuditResult.Success : AuditResult.Failure, "TargetServer", server.Id.ToString(),
            details: $"Container {containerId} on '{server.Name}': {sanitizedDetail}", cancellationToken: cancellationToken);

        return new ContainerActionResultDto(result.Success, sanitizedDetail, DateTimeOffset.UtcNow);
    }

    public async Task<DiscoveredContainerLogsDto> GetContainerLogsAsync(
        Guid environmentDefinitionId, string containerId, int tailLines, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.ContainersView, cancellationToken);
        await EnsureEnvironmentAccessAsync(userId, environmentDefinitionId, cancellationToken);

        if (tailLines > MaxTailLines)
            throw new ValidationException($"tailLines cannot exceed {MaxTailLines}.");

        var server = await ResolveConfiguredServerAsync(environmentDefinitionId, cancellationToken);

        var discovery = await remoteExecutionProvider.DiscoverContainersAsync(server, cancellationToken);
        if (!discovery.Success)
            return new DiscoveredContainerLogsDto(containerId, false, string.Empty, discovery.Error ?? "Could not reach the target server.");

        var known = DockerDiscoveryParser.ParseInspectArray(discovery.InspectJson);
        if (!known.Any(c => string.Equals(c.ContainerId, containerId, StringComparison.OrdinalIgnoreCase)))
            return new DiscoveredContainerLogsDto(containerId, false, string.Empty, "This container was not found on the target server for this environment.");

        var effectiveTailLines = tailLines <= 0 ? DefaultTailLines : tailLines;
        var logsResult = await remoteExecutionProvider.GetContainerLogsAsync(server, containerId, effectiveTailLines, cancellationToken);

        return logsResult.Success
            ? new DiscoveredContainerLogsDto(containerId, true, LogSanitizer.Sanitize(logsResult.Logs), null)
            : new DiscoveredContainerLogsDto(containerId, false, string.Empty, logsResult.Error ?? "Failed to fetch logs.");
    }

    private async Task<TargetServer> ResolveConfiguredServerAsync(Guid environmentDefinitionId, CancellationToken cancellationToken)
    {
        var environmentDefinition = await db.EnvironmentDefinitions
            .Include(e => e.PrimaryTargetServer)
            .FirstOrDefaultAsync(e => e.Id == environmentDefinitionId, cancellationToken)
            ?? throw new NotFoundException("EnvironmentDefinition", environmentDefinitionId);

        return environmentDefinition.PrimaryTargetServer
            ?? throw new ValidationException("No target server is assigned to this environment yet.");
    }

    private async Task<List<DiscoveredContainerDto>> MapDiscoveredContainersAsync(
        Guid environmentDefinitionId, Guid targetServerId, RemoteContainerDiscoveryResult discovery, CancellationToken cancellationToken)
    {
        var raw = DockerDiscoveryParser.ParseInspectArray(discovery.InspectJson);
        var statsByName = DockerDiscoveryParser.ParseStatsLines(discovery.StatsJson);

        // Only ContainerName is used to recognize a configured application's
        // container — the one field an operator sets specifically to identify
        // it, versus guessing from compose project/service naming conventions
        // that vary across compose versions and would risk a false match.
        var configuredByContainerName = await db.ApplicationEnvironments
            .Include(ae => ae.Application)
            .Where(ae => ae.EnvironmentDefinitionId == environmentDefinitionId && ae.TargetServerId == targetServerId &&
                         ae.ContainerName != null && ae.ContainerName != "")
            .ToDictionaryAsync(ae => ae.ContainerName!, ae => ae, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var result = new List<DiscoveredContainerDto>(raw.Count);
        foreach (var container in raw)
        {
            statsByName.TryGetValue(container.Name, out var stats);
            var mapped = configuredByContainerName.GetValueOrDefault(container.Name);

            result.Add(new DiscoveredContainerDto(
                container.ContainerId, container.Name, container.Image, container.ImageTag,
                container.State, container.DockerHealthStatus, container.CreatedAt, container.StartedAt,
                container.RestartCount, container.Ports, stats,
                IsMapped: mapped is not null,
                ApplicationId: mapped?.ApplicationId,
                ApplicationName: mapped?.Application.Name,
                EnvironmentDefinitionId: mapped is not null ? environmentDefinitionId : null,
                CanRecreateWithVolumes: mapped is not null && mapped.UseDownWithVolumesOnDeploy));
        }
        return result;
    }

    private static HostMetricsDto ToHostMetricsDto(RemoteHostMetricsResult metrics)
    {
        var (load1, load5, load15) = DockerDiscoveryParser.ParseLoadAvg(metrics.LoadAvgRaw);
        var (memTotal, memUsed, memAvailable) = DockerDiscoveryParser.ParseFreeBytes(metrics.MemRaw);
        var (diskTotal, diskUsed, diskAvailable, diskPercent) = DockerDiscoveryParser.ParseDfKb(metrics.DiskRaw);

        return new HostMetricsDto(
            load1, load5, load15,
            memTotal, memUsed, memAvailable,
            diskTotal, diskUsed, diskAvailable, diskPercent);
    }

    private Guid RequireUserId() => currentUser.UserId ?? throw new ForbiddenException("Not authenticated.");

    private async Task EnsurePermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken)
    {
        var (_, permissions) = await db.GetRolesAndPermissionsAsync(userId, cancellationToken);
        if (!permissions.Contains(permissionCode))
            throw new ForbiddenException($"Missing required permission '{permissionCode}'.");
    }

    private async Task EnsureEnvironmentAccessAsync(Guid userId, Guid environmentDefinitionId, CancellationToken cancellationToken)
    {
        if (!await db.HasEnvironmentAccessAsync(userId, environmentDefinitionId, cancellationToken))
            throw new ForbiddenException("You do not have access to this environment.");
    }
}
