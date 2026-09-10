using System.Text.RegularExpressions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Applications;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

public partial class ApplicationService(IAppDbContext db, IAuditService auditService) : IApplicationService
{
    public async Task<IReadOnlyList<ApplicationDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var apps = await db.Applications
            .Include(a => a.Repository)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);
        return apps.Select(ToDto).ToList();
    }

    public async Task<ApplicationDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var app = await db.Applications
            .Include(a => a.Repository)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new NotFoundException("Application", id);
        return ToDto(app);
    }

    public async Task<ApplicationDto> CreateAsync(CreateApplicationRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        var slug = request.Slug.Trim().ToLowerInvariant();

        if (!SlugPattern().IsMatch(slug))
            throw new ValidationException("Slug must be lowercase alphanumeric segments separated by single hyphens (e.g. 'dms-api').");
        if (await db.Applications.AnyAsync(a => a.Slug == slug, cancellationToken))
            throw new ConflictException($"Slug '{slug}' is already in use.");

        await EnsureRepositoryValidAsync(request.RepositoryId, cancellationToken);
        ValidateSourcePath(request.SourcePath);

        var app = new ManagedApplication
        {
            Name = name,
            Slug = slug,
            Description = request.Description?.Trim(),
            DeploymentMode = request.DeploymentMode,
            RepositoryId = request.RepositoryId,
            SourcePath = NormalizeSourcePath(request.SourcePath),
            IsActive = true,
        };
        db.Applications.Add(app);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("application.create", AuditResult.Success, "Application", app.Id.ToString(),
            details: $"Created application '{app.Name}' (slug={app.Slug}, mode={app.DeploymentMode})", cancellationToken: cancellationToken);

        return await GetByIdAsync(app.Id, cancellationToken);
    }

    public async Task<ApplicationDto> UpdateAsync(Guid id, UpdateApplicationRequest request, CancellationToken cancellationToken = default)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new NotFoundException("Application", id);

        await EnsureRepositoryValidAsync(request.RepositoryId, cancellationToken);
        ValidateSourcePath(request.SourcePath);

        app.Name = request.Name.Trim();
        app.Description = request.Description?.Trim();
        app.DeploymentMode = request.DeploymentMode;
        app.RepositoryId = request.RepositoryId;
        app.SourcePath = NormalizeSourcePath(request.SourcePath);
        app.IsActive = request.IsActive;
        app.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("application.update", AuditResult.Success, "Application", app.Id.ToString(),
            details: $"Updated application '{app.Name}'; active={app.IsActive}", cancellationToken: cancellationToken);

        return await GetByIdAsync(app.Id, cancellationToken);
    }

    private async Task EnsureRepositoryValidAsync(Guid? repositoryId, CancellationToken cancellationToken)
    {
        if (repositoryId is null)
            return;
        if (!await db.Repositories.AnyAsync(r => r.Id == repositoryId, cancellationToken))
            throw new ValidationException("RepositoryId does not refer to a known repository.");
    }

    /// <summary>SourcePath is a subdirectory *within the repository checkout*
    /// (monorepo support) — must stay relative and inside the repo, unlike the
    /// host filesystem paths validated by DeploymentPathValidator.</summary>
    private static void ValidateSourcePath(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return;
        if (!DeploymentPathValidator.IsSafeRelativePath(sourcePath))
            throw new ValidationException("SourcePath must be a relative path with no '.' or '..' segments.");
    }

    private static string? NormalizeSourcePath(string? sourcePath) =>
        string.IsNullOrWhiteSpace(sourcePath) ? null : sourcePath.Trim().Trim('/');

    private static ApplicationDto ToDto(ManagedApplication a) => new(
        a.Id, a.Name, a.Slug, a.Description, a.DeploymentMode,
        a.RepositoryId, a.Repository?.Name, a.SourcePath, a.IsActive, a.CreatedAt, a.UpdatedAt);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
