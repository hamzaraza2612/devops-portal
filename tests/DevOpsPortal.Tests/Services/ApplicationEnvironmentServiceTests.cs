using DevOpsPortal.Application.Dtos.Applications;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Infrastructure.Persistence;
using DevOpsPortal.Tests.Common;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class ApplicationEnvironmentServiceTests
{
    private static async Task<(ApplicationEnvironmentService Sut, AppDbContext Db, ManagedApplication App, EnvironmentDefinition EnvDef, TargetServer Server)>
        CreateSutAsync(DeploymentMode mode = DeploymentMode.LegacyFilesystem)
    {
        var db = TestDb.CreateInMemory();
        await TestDb.SeedEnvironmentDefinitionsAsync(db);

        var app = new ManagedApplication { Name = "Sample", Slug = "sample", DeploymentMode = mode };
        db.Applications.Add(app);

        var server = new TargetServer { Name = "server-1", IsActive = true };
        server.AllowedDeploymentRoots.Add(new AllowedDeploymentRoot { RootPath = "/mnt/data/apps", IsActive = true, TargetServerId = server.Id });
        db.TargetServers.Add(server);

        await db.SaveChangesAsync();
        var envDef = db.EnvironmentDefinitions.Single(e => e.Name == EnvironmentNames.Dev);

        var currentUser = new FakeCurrentUserService();
        var sut = new ApplicationEnvironmentService(db, currentUser, new AuditService(db, currentUser), new FakeGitProviderClient());
        return (sut, db, app, envDef, server);
    }

    private static UpsertApplicationEnvironmentRequest ValidRequest(
        Guid targetServerId, string? rootPath = "/mnt/data/apps/sample", bool syncSourceFromRepository = false) =>
        new(targetServerId, "develop", rootPath, "publish", "Backups", null, "docker-compose.yml",
            null, "SampleApi", "SampleApi", null, false, syncSourceFromRepository, HealthCheckType.None, null, 30, 5, null, true);

    [Fact]
    public async Task UpsertAsync_WithPathUnderAllowedRoot_Succeeds()
    {
        var (sut, _, app, envDef, server) = await CreateSutAsync();

        var dto = await sut.UpsertAsync(app.Id, envDef.Id, ValidRequest(server.Id));

        Assert.Equal("/mnt/data/apps/sample", dto.DeploymentRootPath);
        Assert.Equal("DEV", dto.EnvironmentName);
        Assert.Equal("server-1", dto.TargetServerName);
    }

    [Fact]
    public async Task UpsertAsync_WithPathOutsideAllowedRoots_ThrowsValidation()
    {
        var (sut, _, app, envDef, server) = await CreateSutAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.UpsertAsync(app.Id, envDef.Id, ValidRequest(server.Id, "/mnt/data/other/sample")));
    }

    [Fact]
    public async Task UpsertAsync_WithSiblingPrefixPath_ThrowsValidation()
    {
        // "/mnt/data/apps2" must not be treated as under allowed root "/mnt/data/apps"
        var (sut, _, app, envDef, server) = await CreateSutAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.UpsertAsync(app.Id, envDef.Id, ValidRequest(server.Id, "/mnt/data/apps2/sample")));
    }

    [Fact]
    public async Task UpsertAsync_WithInactiveAllowedRoot_ThrowsValidation()
    {
        var (sut, db, app, envDef, server) = await CreateSutAsync();
        var root = db.AllowedDeploymentRoots.Single(r => r.TargetServerId == server.Id);
        root.IsActive = false;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.UpsertAsync(app.Id, envDef.Id, ValidRequest(server.Id)));
    }

    [Fact]
    public async Task UpsertAsync_WithMissingRootPathForLegacyMode_ThrowsValidation()
    {
        var (sut, _, app, envDef, server) = await CreateSutAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.UpsertAsync(app.Id, envDef.Id, ValidRequest(server.Id, rootPath: null)));
    }

    [Fact]
    public async Task UpsertAsync_WithSyncSourceFromRepositoryButNoRepositoryConfigured_ThrowsValidation()
    {
        var (sut, _, app, envDef, server) = await CreateSutAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.UpsertAsync(app.Id, envDef.Id, ValidRequest(server.Id, syncSourceFromRepository: true)));
    }

    [Fact]
    public async Task UpsertAsync_WithSyncSourceFromRepositoryAndRepositoryConfigured_Succeeds()
    {
        var (sut, db, app, envDef, server) = await CreateSutAsync();
        app.RepositoryId = Guid.NewGuid();
        db.Repositories.Add(new Repository { Id = app.RepositoryId.Value, Name = "repo", Url = "https://gitlab.example.com/group/repo.git", Provider = RepositoryProvider.GitLab });
        await db.SaveChangesAsync();

        var dto = await sut.UpsertAsync(app.Id, envDef.Id, ValidRequest(server.Id, syncSourceFromRepository: true));

        Assert.True(dto.SyncSourceFromRepository);
    }

    [Fact]
    public async Task UpsertAsync_ContainerImageMode_DoesNotRequireRootPath()
    {
        var (sut, _, app, envDef, server) = await CreateSutAsync(DeploymentMode.ContainerImage);

        var dto = await sut.UpsertAsync(app.Id, envDef.Id, ValidRequest(server.Id, rootPath: null));

        Assert.Null(dto.DeploymentRootPath);
    }

    [Fact]
    public async Task UpsertAsync_WithInactiveTargetServer_ThrowsValidation()
    {
        var (sut, db, app, envDef, server) = await CreateSutAsync();
        server.IsActive = false;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => sut.UpsertAsync(app.Id, envDef.Id, ValidRequest(server.Id)));
    }

    [Fact]
    public async Task UpsertAsync_CalledTwice_UpdatesExistingRowInsteadOfDuplicating()
    {
        var (sut, db, app, envDef, server) = await CreateSutAsync();

        await sut.UpsertAsync(app.Id, envDef.Id, ValidRequest(server.Id));
        await sut.UpsertAsync(app.Id, envDef.Id, ValidRequest(server.Id) with { BranchName = "main" });

        var rows = db.ApplicationEnvironments.Where(ae => ae.ApplicationId == app.Id).ToList();
        Assert.Single(rows);
        Assert.Equal("main", rows[0].BranchName);
    }

    [Fact]
    public async Task UpsertAsync_HealthCheckEnabledWithoutEndpoint_ThrowsValidation()
    {
        var (sut, _, app, envDef, server) = await CreateSutAsync();
        var request = ValidRequest(server.Id) with { HealthCheckType = HealthCheckType.Http, HealthCheckEndpoint = null };

        await Assert.ThrowsAsync<ValidationException>(() => sut.UpsertAsync(app.Id, envDef.Id, request));
    }

    [Fact]
    public async Task UpsertAsync_WithTraversalInComposeFilePath_ThrowsValidation()
    {
        var (sut, _, app, envDef, server) = await CreateSutAsync();
        var request = ValidRequest(server.Id) with { ComposeFilePath = "../../etc/passwd" };

        await Assert.ThrowsAsync<ValidationException>(() => sut.UpsertAsync(app.Id, envDef.Id, request));
    }

    [Fact]
    public async Task GetForApplicationAsync_WithUnknownApplication_ThrowsNotFound()
    {
        var (sut, _, _, _, _) = await CreateSutAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => sut.GetForApplicationAsync(Guid.NewGuid()));
    }
}
