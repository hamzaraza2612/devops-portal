using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Builds;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

/// <summary>
/// CRUD for configured build servers (e.g. Jenkins instances) — the
/// build-provider counterpart of ITargetServerService. Never stores or
/// returns a credential: ApiTokenEnvVarName is validated as a plausible
/// environment-variable name only, mirroring RepositoryService's handling of
/// Repository.AccessTokenEnvVarName.
/// </summary>
public class BuildServerService(IAppDbContext db, IAuditService auditService) : IBuildServerService
{
    private static readonly System.Text.RegularExpressions.Regex EnvVarNamePattern =
        new(@"^[A-Z][A-Z0-9_]{2,99}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public async Task<IReadOnlyList<BuildServerDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.BuildServers.OrderBy(s => s.Name).Select(s => ToDto(s)).ToListAsync(cancellationToken);

    public async Task<BuildServerDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var server = await db.BuildServers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException("BuildServer", id);
        return ToDto(server);
    }

    public async Task<BuildServerDto> CreateAsync(CreateBuildServerRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        if (await db.BuildServers.AnyAsync(s => s.Name.ToLower() == name.ToLower(), cancellationToken))
            throw new ConflictException($"Build server name '{name}' is already in use.");

        ValidateBaseUrl(request.BaseUrl);
        ValidateEnvVarNameIfSet(request.ApiTokenEnvVarName);

        var server = new BuildServer
        {
            Name = name,
            Description = request.Description?.Trim(),
            ProviderType = request.ProviderType,
            BaseUrl = request.BaseUrl.Trim().TrimEnd('/'),
            Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim(),
            ApiTokenEnvVarName = string.IsNullOrWhiteSpace(request.ApiTokenEnvVarName) ? null : request.ApiTokenEnvVarName.Trim(),
            IsActive = true,
        };
        db.BuildServers.Add(server);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("buildserver.create", AuditResult.Success, "BuildServer", server.Id.ToString(),
            details: $"Created build server '{server.Name}' ({server.ProviderType})", cancellationToken: cancellationToken);

        return ToDto(server);
    }

    public async Task<BuildServerDto> UpdateAsync(Guid id, UpdateBuildServerRequest request, CancellationToken cancellationToken = default)
    {
        var server = await db.BuildServers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException("BuildServer", id);

        var name = request.Name.Trim();
        if (await db.BuildServers.AnyAsync(s => s.Id != id && s.Name.ToLower() == name.ToLower(), cancellationToken))
            throw new ConflictException($"Build server name '{name}' is already in use.");

        ValidateBaseUrl(request.BaseUrl);
        ValidateEnvVarNameIfSet(request.ApiTokenEnvVarName);

        server.Name = name;
        server.Description = request.Description?.Trim();
        server.BaseUrl = request.BaseUrl.Trim().TrimEnd('/');
        server.Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim();
        server.ApiTokenEnvVarName = string.IsNullOrWhiteSpace(request.ApiTokenEnvVarName) ? null : request.ApiTokenEnvVarName.Trim();
        server.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("buildserver.update", AuditResult.Success, "BuildServer", server.Id.ToString(),
            details: $"Updated build server '{server.Name}'; active={server.IsActive}", cancellationToken: cancellationToken);

        return ToDto(server);
    }

    private static void ValidateBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ValidationException("BaseUrl must be an absolute http(s) URL.");
    }

    private static void ValidateEnvVarNameIfSet(string? envVarName)
    {
        if (!string.IsNullOrWhiteSpace(envVarName) && !EnvVarNamePattern.IsMatch(envVarName.Trim()))
            throw new ValidationException("ApiTokenEnvVarName must look like an environment variable name (e.g. JENKINS_TOKEN_MAIN).");
    }

    private static BuildServerDto ToDto(BuildServer s) =>
        new(s.Id, s.Name, s.Description, s.ProviderType, s.BaseUrl, s.Username, s.ApiTokenEnvVarName, s.IsActive, s.CreatedAt);
}
