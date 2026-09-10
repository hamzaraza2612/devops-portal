using System.Text.RegularExpressions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Repositories;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

public partial class RepositoryService(IAppDbContext db, IAuditService auditService) : IRepositoryService
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
        repo.AccessTokenEnvVarName = NormalizeEnvVarName(request.AccessTokenEnvVarName);
        repo.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("repository.update", AuditResult.Success, "Repository", repo.Id.ToString(),
            details: $"Updated repository '{repo.Name}'; active={repo.IsActive}", cancellationToken: cancellationToken);

        return ToDto(repo);
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
        new(r.Id, r.Name, r.Url, r.Provider, r.Description, r.AccessTokenEnvVarName, r.IsActive, r.CreatedAt);

    [GeneratedRegex("^[A-Z][A-Z0-9_]{2,99}$")]
    private static partial Regex EnvVarNamePattern();
}
