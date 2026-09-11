using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Secrets;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

/// <summary>
/// Owns SecretReference metadata and is the only Application-layer code that
/// talks to ISecretProvider — every other service that needs a resolved
/// value (DeploymentExecutor, via ResolveForDeploymentAsync; an authorized
/// human, via the explicit, separately-permissioned RevealAsync) goes through
/// this class rather than the provider directly, so permission checks,
/// audit, and environment-isolation logic live in exactly one place. Every
/// other method here returns metadata only — RevealAsync is the sole,
/// deliberate, secrets.reveal-gated exception (see its doc comment).
/// </summary>
public class SecretReferenceService(
    IAppDbContext db, ICurrentUserService currentUser, IAuditService auditService, ISecretProvider secretProvider)
    : ISecretReferenceService
{
    public async Task<IReadOnlyList<SecretReferenceDto>> ListAsync(
        Guid? applicationId, Guid? environmentDefinitionId, SecretCategory? category, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.SecretsView, cancellationToken);
        if (environmentDefinitionId is not null)
            await EnsureEnvironmentAccessAsync(userId, environmentDefinitionId.Value, cancellationToken);

        // Environment-scoped credentials must stay separated even when no explicit
        // environmentDefinitionId filter is supplied (master requirements §12/§19) —
        // Global/Application-scoped secrets aren't tied to a specific environment,
        // so they stay visible to anyone holding SecretsView.
        var accessibleEnvIds = await db.GetAccessibleEnvironmentIdsAsync(userId, cancellationToken);

        var query = db.SecretReferences.AsQueryable();
        if (applicationId is not null) query = query.Where(s => s.ApplicationId == applicationId);
        if (environmentDefinitionId is not null) query = query.Where(s => s.EnvironmentDefinitionId == environmentDefinitionId);
        if (accessibleEnvIds is not null)
            query = query.Where(s => s.EnvironmentDefinitionId == null || accessibleEnvIds.Contains(s.EnvironmentDefinitionId.Value));
        if (category is not null) query = query.Where(s => s.Category == category);

        var secrets = await query.OrderBy(s => s.Name).ToListAsync(cancellationToken);
        var result = new List<SecretReferenceDto>(secrets.Count);
        foreach (var secret in secrets)
            result.Add(await ToDtoAsync(secret, cancellationToken));
        return result;
    }

    public async Task<SecretReferenceDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.SecretsView, cancellationToken);

        var secret = await db.SecretReferences.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException("SecretReference", id);
        if (secret.EnvironmentDefinitionId is { } secretEnvId)
            await EnsureEnvironmentAccessAsync(userId, secretEnvId, cancellationToken);
        return await ToDtoAsync(secret, cancellationToken);
    }

    public async Task<SecretReferenceDto> CreateAsync(CreateSecretReferenceRequest request, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.SecretsManage, cancellationToken);

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name is required.");
        if (string.IsNullOrEmpty(request.Value))
            throw new ValidationException("Value is required.");

        await ValidateScopeAsync(request.Scope, request.ApplicationId, request.EnvironmentDefinitionId, cancellationToken);

        if (await db.SecretReferences.AnyAsync(s =>
                s.Name == name && s.Scope == request.Scope &&
                s.ApplicationId == request.ApplicationId && s.EnvironmentDefinitionId == request.EnvironmentDefinitionId,
                cancellationToken))
        {
            throw new ConflictException($"A secret named '{name}' already exists in this scope.");
        }

        // The plaintext value crosses into the provider here and nowhere else in
        // this method — never assigned to a local held past this call, never
        // logged, never placed in the audit details below.
        var storeKey = await secretProvider.StoreAsync(null, request.Value, cancellationToken);

        var secret = new SecretReference
        {
            Name = name,
            Category = request.Category,
            Scope = request.Scope,
            ApplicationId = request.ApplicationId,
            EnvironmentDefinitionId = request.EnvironmentDefinitionId,
            Description = NormalizeOrNull(request.Description),
            Username = NormalizeOrNull(request.Username),
            Host = NormalizeOrNull(request.Host),
            Port = request.Port,
            DatabaseName = NormalizeOrNull(request.DatabaseName),
            ProviderKey = secretProvider.ProviderKey,
            StoreKey = storeKey,
            CreatedByUserId = userId,
        };
        db.SecretReferences.Add(secret);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("secret.created", AuditResult.Success, "SecretReference", secret.Id.ToString(),
            details: DescribeScope(secret), cancellationToken: cancellationToken);

        return await ToDtoAsync(secret, cancellationToken);
    }

    public async Task<SecretReferenceDto> UpdateAsync(Guid id, UpdateSecretReferenceRequest request, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.SecretsManage, cancellationToken);

        var secret = await db.SecretReferences.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException("SecretReference", id);
        if (secret.EnvironmentDefinitionId is { } secretEnvId)
            await EnsureEnvironmentAccessAsync(userId, secretEnvId, cancellationToken);

        secret.Description = NormalizeOrNull(request.Description);
        secret.Username = NormalizeOrNull(request.Username);
        secret.Host = NormalizeOrNull(request.Host);
        secret.Port = request.Port;
        secret.DatabaseName = NormalizeOrNull(request.DatabaseName);
        secret.IsActive = request.IsActive;
        secret.UpdatedAt = DateTimeOffset.UtcNow;

        var rotated = false;
        if (!string.IsNullOrEmpty(request.Value))
        {
            secret.StoreKey = await secretProvider.StoreAsync(secret.StoreKey, request.Value, cancellationToken);
            rotated = true;
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("secret.updated", AuditResult.Success, "SecretReference", secret.Id.ToString(),
            details: $"{DescribeScope(secret)}; active={secret.IsActive}; valueRotated={rotated}", cancellationToken: cancellationToken);

        return await ToDtoAsync(secret, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.SecretsManage, cancellationToken);

        var secret = await db.SecretReferences.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException("SecretReference", id);
        if (secret.EnvironmentDefinitionId is { } secretEnvId)
            await EnsureEnvironmentAccessAsync(userId, secretEnvId, cancellationToken);

        await secretProvider.DeleteAsync(secret.StoreKey, cancellationToken);

        db.SecretReferences.Remove(secret);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("secret.deleted", AuditResult.Success, "SecretReference", id.ToString(),
            details: DescribeScope(secret), cancellationToken: cancellationToken);
    }

    public async Task<RevealedSecretDto> RevealAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.SecretsReveal, cancellationToken);

        var secret = await db.SecretReferences.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException("SecretReference", id);
        if (secret.EnvironmentDefinitionId is { } secretEnvId)
            await EnsureEnvironmentAccessAsync(userId, secretEnvId, cancellationToken);

        var value = await secretProvider.RetrieveAsync(secret.StoreKey, cancellationToken);
        if (!value.Success || value.Value is null)
            throw new ValidationException($"Failed to retrieve the value for '{secret.Name}'.");

        // Audits that the value was revealed and by whom — never the value itself
        // (same "audit the action, never the secret" principle as secret.referenced).
        await auditService.LogAsync("secret.revealed", AuditResult.Success, "SecretReference", secret.Id.ToString(),
            details: DescribeScope(secret), cancellationToken: cancellationToken);

        return new RevealedSecretDto(value.Value);
    }

    public async Task<IReadOnlyDictionary<string, string>> ResolveForDeploymentAsync(
        Guid applicationId, Guid environmentDefinitionId, Guid actorUserId, string? actorUsername, CancellationToken cancellationToken = default)
    {
        var candidates = await db.SecretReferences
            .Where(s => s.IsActive &&
                ((s.Scope == SecretScope.ApplicationEnvironment && s.ApplicationId == applicationId && s.EnvironmentDefinitionId == environmentDefinitionId) ||
                 (s.Scope == SecretScope.Application && s.ApplicationId == applicationId) ||
                 s.Scope == SecretScope.Global))
            .ToListAsync(cancellationToken);

        // Most specific scope wins on a name collision: ApplicationEnvironment(2)
        // > Application(1) > Global(0) — see SecretScope's doc comment. This is
        // what keeps a DEV-scoped secret from ever being selected while
        // resolving QA/UAT/Production: it simply isn't in `candidates` for any
        // other environmentDefinitionId to begin with.
        var winners = candidates.GroupBy(s => s.Name).Select(g => g.OrderByDescending(s => (int)s.Scope).First());

        var resolved = new Dictionary<string, string>();
        foreach (var secret in winners)
        {
            var value = await secretProvider.RetrieveAsync(secret.StoreKey, cancellationToken);
            if (!value.Success || value.Value is null)
            {
                // Deliberately named-only — never include ErrorMessage here if it
                // could ever carry more than a generic provider failure reason.
                throw new DeploymentExecutionException($"Failed to resolve secret '{secret.Name}' for this deployment.");
            }

            resolved[secret.Name] = value.Value;

            await auditService.LogAsync("secret.referenced", AuditResult.Success, "SecretReference", secret.Id.ToString(),
                details: $"{DescribeScope(secret)} resolved for deployment of application {applicationId} / environment {environmentDefinitionId}",
                actorUserId: actorUserId, actorUsername: actorUsername, cancellationToken: cancellationToken);
        }

        return resolved;
    }

    // ------------------------------------------------------------- internals

    private async Task ValidateScopeAsync(SecretScope scope, Guid? applicationId, Guid? environmentDefinitionId, CancellationToken cancellationToken)
    {
        switch (scope)
        {
            case SecretScope.Global:
                if (applicationId is not null || environmentDefinitionId is not null)
                    throw new ValidationException("Global-scoped secrets must not specify ApplicationId or EnvironmentDefinitionId.");
                break;
            case SecretScope.Application:
                if (applicationId is null)
                    throw new ValidationException("Application-scoped secrets require ApplicationId.");
                if (environmentDefinitionId is not null)
                    throw new ValidationException("Application-scoped secrets must not specify EnvironmentDefinitionId.");
                break;
            case SecretScope.ApplicationEnvironment:
                if (applicationId is null || environmentDefinitionId is null)
                    throw new ValidationException("ApplicationEnvironment-scoped secrets require both ApplicationId and EnvironmentDefinitionId.");
                break;
            default:
                throw new ValidationException("Unknown secret scope.");
        }

        if (applicationId is { } appId && !await db.Applications.AnyAsync(a => a.Id == appId, cancellationToken))
            throw new ValidationException($"Application '{appId}' was not found.");
        if (environmentDefinitionId is { } envId && !await db.EnvironmentDefinitions.AnyAsync(e => e.Id == envId, cancellationToken))
            throw new ValidationException($"EnvironmentDefinition '{envId}' was not found.");
    }

    private static string DescribeScope(SecretReference s) => s.Scope switch
    {
        SecretScope.Global => $"'{s.Name}' ({s.Category}, Global)",
        SecretScope.Application => $"'{s.Name}' ({s.Category}, Application {s.ApplicationId})",
        SecretScope.ApplicationEnvironment => $"'{s.Name}' ({s.Category}, Application {s.ApplicationId} / Environment {s.EnvironmentDefinitionId})",
        _ => s.Name,
    };

    private async Task<SecretReferenceDto> ToDtoAsync(SecretReference s, CancellationToken cancellationToken)
    {
        var applicationName = s.ApplicationId is { } appId
            ? await db.Applications.Where(a => a.Id == appId).Select(a => a.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var environmentName = s.EnvironmentDefinitionId is { } envId
            ? await db.EnvironmentDefinitions.Where(e => e.Id == envId).Select(e => e.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var createdByUsername = await db.Users.Where(u => u.Id == s.CreatedByUserId).Select(u => u.Username).FirstOrDefaultAsync(cancellationToken);

        return new SecretReferenceDto(
            s.Id, s.Name, s.Category, s.Scope, s.ApplicationId, applicationName, s.EnvironmentDefinitionId, environmentName,
            s.Description, s.Username, s.Host, s.Port, s.DatabaseName,
            s.ProviderKey, s.IsActive, s.CreatedByUserId, createdByUsername, s.CreatedAt, s.UpdatedAt);
    }

    private static string? NormalizeOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private Guid RequireUserId() => currentUser.UserId ?? throw new ForbiddenException("Not authenticated.");

    private async Task EnsurePermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken)
    {
        var (_, permissions) = await db.GetRolesAndPermissionsAsync(userId, cancellationToken);
        if (!permissions.Contains(permissionCode))
            throw new ForbiddenException($"Missing required permission '{permissionCode}'.");
    }

    /// <summary>SecretsView/SecretsReveal are granted to anyone with access to
    /// ANY environment (see AppDbContextExtensions) — this confirms the user is
    /// specifically allowed to see/reveal THIS environment's credentials
    /// (master requirements §2/§12/§19).</summary>
    private async Task EnsureEnvironmentAccessAsync(Guid userId, Guid environmentDefinitionId, CancellationToken cancellationToken)
    {
        if (!await db.HasEnvironmentAccessAsync(userId, environmentDefinitionId, cancellationToken))
            throw new ForbiddenException("You do not have access to this environment.");
    }
}
