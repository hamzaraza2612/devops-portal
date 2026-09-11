using System.Text.RegularExpressions;
using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Repositories;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

public partial class RepositoryService(
    IAppDbContext db, IAuditService auditService, ISecretProvider secretProvider, IGitProviderClient gitProviderClient)
    : IRepositoryService
{
    public async Task<IReadOnlyList<RepositoryDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Repositories.OrderBy(r => r.Name)
            .Select(r => ToDto(r))
            .ToListAsync(cancellationToken);

    public async Task<RepositoryDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Repository", id);
        return ToDto(repo);
    }

    public async Task<RepositoryDto> CreateAsync(CreateRepositoryRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        ValidateUrl(request.Url);
        ValidateAccessTokenEnvVarName(request.AccessTokenEnvVarName);

        if (await db.Repositories.AnyAsync(r => r.Name.ToLower() == name.ToLower(), cancellationToken))
            throw new ConflictException($"Repository name '{name}' is already in use.");

        var repo = new Repository
        {
            Name = name,
            Url = request.Url.Trim(),
            Provider = request.Provider,
            Description = request.Description?.Trim(),
            DefaultBranch = string.IsNullOrWhiteSpace(request.DefaultBranch) ? null : request.DefaultBranch.Trim(),
            Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim(),
            AccessTokenEnvVarName = NormalizeEnvVarName(request.AccessTokenEnvVarName),
            IsActive = true,
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("repository.create", AuditResult.Success, "Repository", repo.Id.ToString(),
            details: $"Created repository '{repo.Name}'", cancellationToken: cancellationToken);

        return ToDto(repo);
    }

    public async Task<RepositoryDto> UpdateAsync(Guid id, UpdateRepositoryRequest request, CancellationToken cancellationToken = default)
    {
        var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Repository", id);

        var name = request.Name.Trim();
        ValidateUrl(request.Url);
        ValidateAccessTokenEnvVarName(request.AccessTokenEnvVarName);

        if (await db.Repositories.AnyAsync(r => r.Id != id && r.Name.ToLower() == name.ToLower(), cancellationToken))
            throw new ConflictException($"Repository name '{name}' is already in use.");

        repo.Name = name;
        repo.Url = request.Url.Trim();
        repo.Provider = request.Provider;
        repo.Description = request.Description?.Trim();
        repo.DefaultBranch = string.IsNullOrWhiteSpace(request.DefaultBranch) ? null : request.DefaultBranch.Trim();
        repo.Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim();
        repo.AccessTokenEnvVarName = NormalizeEnvVarName(request.AccessTokenEnvVarName);
        repo.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("repository.update", AuditResult.Success, "Repository", repo.Id.ToString(),
            details: $"Updated repository '{repo.Name}'; active={repo.IsActive}", cancellationToken: cancellationToken);

        return ToDto(repo);
    }

    /// <summary>Safe to hard-delete at any time — Repository.Id is referenced by
    /// ManagedApplication.RepositoryId with DeleteBehavior.SetNull (see
    /// AppDbContext), so any application using this repository just loses its
    /// repository link (visible immediately on that application, never a
    /// silent data-integrity problem) rather than being blocked or cascaded.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Repository", id);

        var linkedAppCount = await db.Applications.CountAsync(a => a.RepositoryId == id, cancellationToken);

        db.Repositories.Remove(repo);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("repository.delete", AuditResult.Success, "Repository", id.ToString(),
            details: $"Deleted repository '{repo.Name}'" + (linkedAppCount > 0 ? $" ({linkedAppCount} application(s) had their repository link cleared)" : string.Empty),
            cancellationToken: cancellationToken);
    }

    public async Task<RepositoryDto> SetAccessTokenAsync(Guid id, SetRepositoryAccessTokenRequest request, CancellationToken cancellationToken = default)
    {
        var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Repository", id);

        if (string.IsNullOrEmpty(request.Value))
            throw new ValidationException("Value is required.");

        // The plaintext value crosses into the provider here and nowhere else —
        // never logged, never placed in the audit details below.
        repo.AccessTokenStoreKey = await secretProvider.StoreAsync(repo.AccessTokenStoreKey, request.Value, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("repository.access_token.set", AuditResult.Success, "Repository", repo.Id.ToString(),
            details: $"GitLab access token set for repository '{repo.Name}'", cancellationToken: cancellationToken);

        return ToDto(repo);
    }

    public async Task<RepositoryConnectionTestResultDto> TestConnectionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Repository", id);

        var result = await gitProviderClient.TestConnectionAsync(repo, cancellationToken);
        var testedAt = DateTimeOffset.UtcNow;

        await auditService.LogAsync(
            "repository.test_connection", result.Connected ? AuditResult.Success : AuditResult.Failure, "Repository", repo.Id.ToString(),
            details: result.Connected
                ? $"CONNECTED to '{result.ProjectName}'" + (result.AuthenticatedAs is null ? "" : $" as '{result.AuthenticatedAs}'")
                : $"FAILED: {result.ErrorMessage}",
            cancellationToken: cancellationToken);

        return new RepositoryConnectionTestResultDto(result.Connected, result.AuthenticatedAs, result.ProjectName, result.ErrorMessage, testedAt);
    }

    /// <summary>Rejects anything but a plain http(s) URL — in particular, URLs with
    /// embedded userinfo credentials (https://user:pass@host/...), the exact
    /// anti-pattern the legacy deploy script uses for its own (interactive,
    /// per-session) git operations. Repository access credentials belong in the
    /// separate credential store (later phase), never inline here.</summary>
    private static void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new ValidationException("Repository URL must be an absolute http(s) URL.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ValidationException("Repository URL must not contain embedded credentials.");
    }

    /// <summary>This field must only ever be the *name* of an environment variable,
    /// never a value that looks like an actual token — a coarse guard against a
    /// caller pasting a real secret in by mistake.</summary>
    private static void ValidateAccessTokenEnvVarName(string? envVarName)
    {
        if (string.IsNullOrWhiteSpace(envVarName))
            return;
        if (!EnvVarNamePattern().IsMatch(envVarName.Trim()))
            throw new ValidationException("AccessTokenEnvVarName must look like an environment variable name (e.g. 'GITLAB_TOKEN_MYAPP'), not a secret value.");
    }

    private static string? NormalizeEnvVarName(string? envVarName) =>
        string.IsNullOrWhiteSpace(envVarName) ? null : envVarName.Trim();

    private static RepositoryDto ToDto(Repository r) =>
        new(r.Id, r.Name, r.Url, r.Provider, r.Description, r.DefaultBranch, r.Username,
            r.AccessTokenStoreKey is not null, r.AccessTokenEnvVarName, r.IsActive, r.CreatedAt);

    [GeneratedRegex("^[A-Z][A-Z0-9_]{2,99}$")]
    private static partial Regex EnvVarNamePattern();
}
