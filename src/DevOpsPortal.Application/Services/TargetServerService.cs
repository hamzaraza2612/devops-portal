using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.TargetServers;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

public class TargetServerService(
    IAppDbContext db, IAuditService auditService, ISecretProvider secretProvider, IRemoteExecutionProvider remoteExecutionProvider)
    : ITargetServerService
{
    public async Task<IReadOnlyList<TargetServerDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var servers = await db.TargetServers
            .Include(s => s.AllowedDeploymentRoots)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
        return servers.Select(ToDto).ToList();
    }

    public async Task<TargetServerDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var server = await db.TargetServers
            .Include(s => s.AllowedDeploymentRoots)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException("TargetServer", id);
        return ToDto(server);
    }

    public async Task<TargetServerDto> CreateAsync(CreateTargetServerRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        if (await db.TargetServers.AnyAsync(s => s.Name.ToLower() == name.ToLower(), cancellationToken))
            throw new ConflictException($"Target server name '{name}' is already in use.");

        var server = new TargetServer
        {
            Name = name,
            Description = request.Description?.Trim(),
            Hostname = request.Hostname?.Trim(),
            SshPort = request.SshPort > 0 ? request.SshPort : 22,
            SshUsername = string.IsNullOrWhiteSpace(request.SshUsername) ? null : request.SshUsername.Trim(),
            SshAuthMethod = request.SshAuthMethod,
            IsActive = true,
        };
        db.TargetServers.Add(server);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("targetserver.create", AuditResult.Success, "TargetServer", server.Id.ToString(),
            details: $"Created target server '{server.Name}'", cancellationToken: cancellationToken);

        return ToDto(server);
    }

    public async Task<TargetServerDto> UpdateAsync(Guid id, UpdateTargetServerRequest request, CancellationToken cancellationToken = default)
    {
        var server = await db.TargetServers
            .Include(s => s.AllowedDeploymentRoots)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException("TargetServer", id);

        var name = request.Name.Trim();
        if (await db.TargetServers.AnyAsync(s => s.Id != id && s.Name.ToLower() == name.ToLower(), cancellationToken))
            throw new ConflictException($"Target server name '{name}' is already in use.");

        server.Name = name;
        server.Description = request.Description?.Trim();
        server.Hostname = request.Hostname?.Trim();
        server.SshPort = request.SshPort > 0 ? request.SshPort : 22;
        server.SshUsername = string.IsNullOrWhiteSpace(request.SshUsername) ? null : request.SshUsername.Trim();
        server.SshAuthMethod = request.SshAuthMethod;
        server.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("targetserver.update", AuditResult.Success, "TargetServer", server.Id.ToString(),
            details: $"Updated target server '{server.Name}'; active={server.IsActive}", cancellationToken: cancellationToken);

        return ToDto(server);
    }

    public async Task<AllowedDeploymentRootDto> AddAllowedRootAsync(
        Guid targetServerId, CreateAllowedDeploymentRootRequest request, CancellationToken cancellationToken = default)
    {
        var server = await db.TargetServers.FirstOrDefaultAsync(s => s.Id == targetServerId, cancellationToken)
            ?? throw new NotFoundException("TargetServer", targetServerId);

        if (!DeploymentPathValidator.TryNormalize(request.RootPath, out var normalizedPath))
            throw new ValidationException("RootPath must be an absolute path with no '.' or '..' segments.");

        if (await db.AllowedDeploymentRoots.AnyAsync(
                r => r.TargetServerId == targetServerId && r.RootPath == normalizedPath, cancellationToken))
            throw new ConflictException($"'{normalizedPath}' is already an allowed root on this target server.");

        var root = new AllowedDeploymentRoot
        {
            TargetServerId = targetServerId,
            RootPath = normalizedPath,
            Description = request.Description?.Trim(),
            IsActive = true,
        };
        db.AllowedDeploymentRoots.Add(root);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("targetserver.allowedroot.add", AuditResult.Success, "TargetServer", targetServerId.ToString(),
            details: $"Added allowed deployment root '{normalizedPath}'", cancellationToken: cancellationToken);

        return ToRootDto(root);
    }

    public async Task<AllowedDeploymentRootDto> UpdateAllowedRootAsync(
        Guid targetServerId, Guid rootId, UpdateAllowedDeploymentRootRequest request, CancellationToken cancellationToken = default)
    {
        var root = await db.AllowedDeploymentRoots
            .FirstOrDefaultAsync(r => r.Id == rootId && r.TargetServerId == targetServerId, cancellationToken)
            ?? throw new NotFoundException("AllowedDeploymentRoot", rootId);

        if (!DeploymentPathValidator.TryNormalize(request.RootPath, out var normalizedPath))
            throw new ValidationException("RootPath must be an absolute path with no '.' or '..' segments.");

        if (await db.AllowedDeploymentRoots.AnyAsync(
                r => r.Id != rootId && r.TargetServerId == targetServerId && r.RootPath == normalizedPath, cancellationToken))
            throw new ConflictException($"'{normalizedPath}' is already an allowed root on this target server.");

        root.RootPath = normalizedPath;
        root.Description = request.Description?.Trim();
        root.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("targetserver.allowedroot.update", AuditResult.Success, "TargetServer", targetServerId.ToString(),
            details: $"Updated allowed deployment root '{normalizedPath}'; active={root.IsActive}", cancellationToken: cancellationToken);

        return ToRootDto(root);
    }

    public async Task<TargetServerDto> SetSshCredentialAsync(Guid id, SetSshCredentialRequest request, CancellationToken cancellationToken = default)
    {
        var server = await db.TargetServers
            .Include(s => s.AllowedDeploymentRoots)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException("TargetServer", id);

        if (string.IsNullOrEmpty(request.Value))
            throw new ValidationException("Value is required.");

        // The plaintext value crosses into the provider here and nowhere else —
        // never logged, never placed in the audit details below (same pattern as
        // SecretReferenceService.CreateAsync).
        server.SshCredentialStoreKey = await secretProvider.StoreAsync(server.SshCredentialStoreKey, request.Value, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("targetserver.ssh_credential.set", AuditResult.Success, "TargetServer", server.Id.ToString(),
            details: $"SSH credential ({server.SshAuthMethod}) set for target server '{server.Name}'", cancellationToken: cancellationToken);

        return ToDto(server);
    }

    public async Task<TargetServerDto> SetSshPassphraseAsync(Guid id, SetSshPassphraseRequest request, CancellationToken cancellationToken = default)
    {
        var server = await db.TargetServers
            .Include(s => s.AllowedDeploymentRoots)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException("TargetServer", id);

        if (string.IsNullOrEmpty(request.Value))
        {
            if (server.SshPassphraseStoreKey is { } existingKey)
            {
                await secretProvider.DeleteAsync(existingKey, cancellationToken);
                server.SshPassphraseStoreKey = null;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            server.SshPassphraseStoreKey = await secretProvider.StoreAsync(server.SshPassphraseStoreKey, request.Value, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditService.LogAsync("targetserver.ssh_passphrase.set", AuditResult.Success, "TargetServer", server.Id.ToString(),
            details: $"SSH private key passphrase {(string.IsNullOrEmpty(request.Value) ? "cleared" : "set")} for target server '{server.Name}'",
            cancellationToken: cancellationToken);

        return ToDto(server);
    }

    public async Task<TargetServerConnectionTestResultDto> TestConnectionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var server = await db.TargetServers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException("TargetServer", id);

        var result = await remoteExecutionProvider.TestConnectionAsync(server, cancellationToken);
        var testedAt = DateTimeOffset.UtcNow;

        await auditService.LogAsync(
            "targetserver.test_connection", result.SshConnected ? AuditResult.Success : AuditResult.Failure, "TargetServer", server.Id.ToString(),
            details: result.SshConnected
                ? $"SSH OK; Docker {(result.DockerAvailable ? $"OK ({result.DockerVersion})" : "NOT AVAILABLE")}; Compose {(result.ComposeAvailable ? $"OK ({result.ComposeVersion})" : "NOT AVAILABLE")}"
                : $"SSH FAILED: {result.ErrorMessage}",
            cancellationToken: cancellationToken);

        return new TargetServerConnectionTestResultDto(
            result.SshConnected, result.AuthenticatedUser, result.OsInfo,
            result.DockerAvailable, result.DockerVersion, result.ComposeAvailable, result.ComposeVersion,
            result.ErrorMessage, testedAt);
    }

    private static AllowedDeploymentRootDto ToRootDto(AllowedDeploymentRoot r) =>
        new(r.Id, r.TargetServerId, r.RootPath, r.Description, r.IsActive);

    private static TargetServerDto ToDto(TargetServer s) =>
        new(s.Id, s.Name, s.Description, s.Hostname,
            s.SshPort, s.SshUsername, s.SshAuthMethod, s.SshCredentialStoreKey is not null, s.SshPassphraseStoreKey is not null,
            s.IsActive, s.CreatedAt,
            s.AllowedDeploymentRoots.OrderBy(r => r.RootPath).Select(ToRootDto).ToList());
}
