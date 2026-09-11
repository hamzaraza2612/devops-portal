using DevOpsPortal.Application.Dtos.Secrets;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Infrastructure.Persistence;
using DevOpsPortal.Infrastructure.Secrets;
using DevOpsPortal.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class SecretReferenceServiceTests
{
    private sealed record Fixture(
        SecretReferenceService Sut,
        AppDbContext Db,
        FakeCurrentUserService CurrentUser,
        ManagedApplication App,
        EnvironmentDefinition DevEnv,
        EnvironmentDefinition QaEnv,
        Guid ManageUserId,
        Guid ViewOnlyUserId,
        Guid NoPermissionUserId);

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var db = TestDb.CreateInMemory();
        await TestDb.SeedRolesAndPermissionsAsync(db);
        await TestDb.SeedEnvironmentDefinitionsAsync(db);

        var app = new ManagedApplication { Name = "Sample", Slug = "sample", DeploymentMode = DeploymentMode.LegacyFilesystem };
        db.Applications.Add(app);
        await db.SaveChangesAsync();

        var devEnv = db.EnvironmentDefinitions.Single(e => e.Name == EnvironmentNames.Dev);
        var qaEnv = db.EnvironmentDefinitions.Single(e => e.Name == EnvironmentNames.Qa);

        var currentUser = new FakeCurrentUserService();
        var currentTenant = new FakeCurrentTenantService();
        var audit = new AuditService(db, currentUser, currentTenant);
        var secretProvider = new EncryptedSecretProvider(
            db, Options.Create(new SecretEncryptionSettings { EncryptionKey = "0123456789abcdef0123456789abcdef" }), NullLogger<EncryptedSecretProvider>.Instance);
        var sut = new SecretReferenceService(db, currentUser, currentTenant, audit, secretProvider);

        var manageUserId = await TestDb.CreateUserWithPermissionsAsync(db, "devops", PermissionCodes.SecretsView, PermissionCodes.SecretsManage);
        var viewOnlyUserId = await TestDb.CreateUserWithPermissionsAsync(db, "viewer", PermissionCodes.SecretsView);
        var noPermissionUserId = await TestDb.CreateUserWithPermissionsAsync(db, "developer", PermissionCodes.ApplicationsView);

        return new Fixture(sut, db, currentUser, app, devEnv, qaEnv, manageUserId, viewOnlyUserId, noPermissionUserId);
    }

    // -------------------------------------------------------------- authorization / unauthorized access

    [Fact]
    public async Task CreateAsync_WithoutSecretsManagePermission_ThrowsForbidden()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.NoPermissionUserId;

        await Assert.ThrowsAsync<ForbiddenException>(() => f.Sut.CreateAsync(
            new CreateSecretReferenceRequest("smtp-password", SecretCategory.Smtp, SecretScope.Global, null, null, null, "s3cr3t")));
    }

    [Fact]
    public async Task CreateAsync_ViewOnlyUser_CannotManage_ThrowsForbidden()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ViewOnlyUserId;

        await Assert.ThrowsAsync<ForbiddenException>(() => f.Sut.CreateAsync(
            new CreateSecretReferenceRequest("smtp-password", SecretCategory.Smtp, SecretScope.Global, null, null, null, "s3cr3t")));
    }

    [Fact]
    public async Task ListAsync_WithoutSecretsViewPermission_ThrowsForbidden()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.NoPermissionUserId;

        await Assert.ThrowsAsync<ForbiddenException>(() => f.Sut.ListAsync(null, null, null));
    }

    [Fact]
    public async Task ViewOnlyUser_CanListAndGet_ButNotCreateUpdateDelete()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;
        var created = await f.Sut.CreateAsync(new CreateSecretReferenceRequest("api-key", SecretCategory.Api, SecretScope.Global, null, null, null, "value"));

        f.CurrentUser.UserId = f.ViewOnlyUserId;
        var listed = await f.Sut.ListAsync(null, null, null);
        Assert.Single(listed);
        await f.Sut.GetAsync(created.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() => f.Sut.UpdateAsync(created.Id, new UpdateSecretReferenceRequest(null, true, "new")));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Sut.DeleteAsync(created.Id));
    }

    // -------------------------------------------------------------- API response safety

    [Fact]
    public void SecretReferenceDto_HasNoValueProperty()
    {
        var propertyNames = typeof(SecretReferenceDto).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(propertyNames, n => n.Contains("Value", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, n => n.Contains("StoreKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateAsync_ReturnedDtoNeverContainsTheStoreKeyOrRawValue()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;

        var dto = await f.Sut.CreateAsync(new CreateSecretReferenceRequest(
            "db-password", SecretCategory.Database, SecretScope.Global, null, null, null, "hunter2-value"));

        var serialized = System.Text.Json.JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("hunter2-value", serialized);
    }

    // -------------------------------------------------------------- validation

    [Theory]
    [InlineData(SecretScope.Global, true, false)]
    [InlineData(SecretScope.Global, false, true)]
    [InlineData(SecretScope.Application, false, false)]
    [InlineData(SecretScope.Application, true, true)]
    [InlineData(SecretScope.ApplicationEnvironment, false, false)]
    [InlineData(SecretScope.ApplicationEnvironment, true, false)]
    public async Task CreateAsync_WithInconsistentScopeAndIds_ThrowsValidation(SecretScope scope, bool withApplicationId, bool withEnvironmentId)
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;

        await Assert.ThrowsAsync<ValidationException>(() => f.Sut.CreateAsync(new CreateSecretReferenceRequest(
            "x", SecretCategory.Other, scope,
            withApplicationId ? f.App.Id : null, withEnvironmentId ? f.DevEnv.Id : null, null, "value")));
    }

    [Fact]
    public async Task CreateAsync_DuplicateNameInSameScope_ThrowsConflict()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;
        await f.Sut.CreateAsync(new CreateSecretReferenceRequest("db-password", SecretCategory.Database, SecretScope.Global, null, null, null, "v1"));

        await Assert.ThrowsAsync<ConflictException>(() =>
            f.Sut.CreateAsync(new CreateSecretReferenceRequest("db-password", SecretCategory.Database, SecretScope.Global, null, null, null, "v2")));
    }

    [Fact]
    public async Task CreateAsync_SameNameDifferentScope_IsAllowed()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;

        await f.Sut.CreateAsync(new CreateSecretReferenceRequest("db-password", SecretCategory.Database, SecretScope.Global, null, null, null, "global-value"));
        var appScoped = await f.Sut.CreateAsync(new CreateSecretReferenceRequest(
            "db-password", SecretCategory.Database, SecretScope.Application, f.App.Id, null, null, "app-value"));

        Assert.NotNull(appScoped);
        Assert.Equal(2, (await f.Sut.ListAsync(null, null, null)).Count);
    }

    [Fact]
    public async Task GetAsync_UnknownId_ThrowsNotFound()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;

        await Assert.ThrowsAsync<NotFoundException>(() => f.Sut.GetAsync(Guid.NewGuid()));
    }

    // ------------------------------------------------------------------ rotation

    [Fact]
    public async Task UpdateAsync_WithNewValue_RotatesValue_ResolvableImmediately()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;
        var created = await f.Sut.CreateAsync(new CreateSecretReferenceRequest(
            "db-password", SecretCategory.Database, SecretScope.ApplicationEnvironment, f.App.Id, f.DevEnv.Id, null, "old-value"));

        await f.Sut.UpdateAsync(created.Id, new UpdateSecretReferenceRequest("rotated", true, "new-value"));

        var resolved = await f.Sut.ResolveForDeploymentAsync(f.App.Id, f.DevEnv.Id, f.ManageUserId, "devops");
        Assert.Equal("new-value", resolved["db-password"]);
    }

    [Fact]
    public async Task UpdateAsync_WithoutValue_OnlyChangesMetadata_ValueUnchanged()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;
        var created = await f.Sut.CreateAsync(new CreateSecretReferenceRequest(
            "db-password", SecretCategory.Database, SecretScope.ApplicationEnvironment, f.App.Id, f.DevEnv.Id, null, "unchanged-value"));

        var updated = await f.Sut.UpdateAsync(created.Id, new UpdateSecretReferenceRequest("just metadata", true, null));

        Assert.Equal("just metadata", updated.Description);
        var resolved = await f.Sut.ResolveForDeploymentAsync(f.App.Id, f.DevEnv.Id, f.ManageUserId, "devops");
        Assert.Equal("unchanged-value", resolved["db-password"]);
    }

    [Fact]
    public async Task DeleteAsync_RemovesReferenceAndValue()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;
        var created = await f.Sut.CreateAsync(new CreateSecretReferenceRequest(
            "db-password", SecretCategory.Database, SecretScope.ApplicationEnvironment, f.App.Id, f.DevEnv.Id, null, "value"));

        await f.Sut.DeleteAsync(created.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => f.Sut.GetAsync(created.Id));
        var resolved = await f.Sut.ResolveForDeploymentAsync(f.App.Id, f.DevEnv.Id, f.ManageUserId, "devops");
        Assert.False(resolved.ContainsKey("db-password"));
    }

    // -------------------------------------------------------------- secret isolation (environment-aware resolution)

    [Fact]
    public async Task ResolveForDeploymentAsync_DevScopedSecret_IsNeverResolvedForQa()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;
        await f.Sut.CreateAsync(new CreateSecretReferenceRequest(
            "db-password", SecretCategory.Database, SecretScope.ApplicationEnvironment, f.App.Id, f.DevEnv.Id, null, "dev-only-value"));

        var devResolved = await f.Sut.ResolveForDeploymentAsync(f.App.Id, f.DevEnv.Id, f.ManageUserId, "devops");
        var qaResolved = await f.Sut.ResolveForDeploymentAsync(f.App.Id, f.QaEnv.Id, f.ManageUserId, "devops");

        Assert.Equal("dev-only-value", devResolved["db-password"]);
        Assert.False(qaResolved.ContainsKey("db-password"));
        Assert.DoesNotContain("dev-only-value", qaResolved.Values);
    }

    [Fact]
    public async Task ResolveForDeploymentAsync_ApplicationScopedSecret_IsAvailableToEveryEnvironmentOfThatApp()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;
        await f.Sut.CreateAsync(new CreateSecretReferenceRequest(
            "gitlab-token", SecretCategory.GitLab, SecretScope.Application, f.App.Id, null, null, "shared-token"));

        var devResolved = await f.Sut.ResolveForDeploymentAsync(f.App.Id, f.DevEnv.Id, f.ManageUserId, "devops");
        var qaResolved = await f.Sut.ResolveForDeploymentAsync(f.App.Id, f.QaEnv.Id, f.ManageUserId, "devops");

        Assert.Equal("shared-token", devResolved["gitlab-token"]);
        Assert.Equal("shared-token", qaResolved["gitlab-token"]);
    }

    [Fact]
    public async Task ResolveForDeploymentAsync_GlobalSecret_IsAvailableEverywhere()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;
        await f.Sut.CreateAsync(new CreateSecretReferenceRequest("smtp-password", SecretCategory.Smtp, SecretScope.Global, null, null, null, "smtp-value"));

        var devResolved = await f.Sut.ResolveForDeploymentAsync(f.App.Id, f.DevEnv.Id, f.ManageUserId, "devops");

        Assert.Equal("smtp-value", devResolved["smtp-password"]);
    }

    [Fact]
    public async Task ResolveForDeploymentAsync_MostSpecificScopeWinsOnNameCollision_WithoutLeakingTheMoreSpecificOneToOtherEnvironments()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;
        await f.Sut.CreateAsync(new CreateSecretReferenceRequest(
            "api-key", SecretCategory.Api, SecretScope.Application, f.App.Id, null, null, "app-wide-value"));
        await f.Sut.CreateAsync(new CreateSecretReferenceRequest(
            "api-key", SecretCategory.Api, SecretScope.ApplicationEnvironment, f.App.Id, f.DevEnv.Id, null, "dev-specific-value"));

        var devResolved = await f.Sut.ResolveForDeploymentAsync(f.App.Id, f.DevEnv.Id, f.ManageUserId, "devops");
        var qaResolved = await f.Sut.ResolveForDeploymentAsync(f.App.Id, f.QaEnv.Id, f.ManageUserId, "devops");

        Assert.Equal("dev-specific-value", devResolved["api-key"]); // most specific wins for DEV
        Assert.Equal("app-wide-value", qaResolved["api-key"]); // QA falls back to the app-wide one
        Assert.DoesNotContain("dev-specific-value", qaResolved.Values); // never leaks
    }

    [Fact]
    public async Task ResolveForDeploymentAsync_InactiveSecret_IsNotResolved()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;
        var created = await f.Sut.CreateAsync(new CreateSecretReferenceRequest(
            "db-password", SecretCategory.Database, SecretScope.ApplicationEnvironment, f.App.Id, f.DevEnv.Id, null, "value"));
        await f.Sut.UpdateAsync(created.Id, new UpdateSecretReferenceRequest(null, false, null));

        var resolved = await f.Sut.ResolveForDeploymentAsync(f.App.Id, f.DevEnv.Id, f.ManageUserId, "devops");

        Assert.False(resolved.ContainsKey("db-password"));
    }

    [Fact]
    public async Task ResolveForDeploymentAsync_UnrelatedApplication_NeverResolvesAnotherApplicationsSecret()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;
        var otherApp = new ManagedApplication { Name = "Other", Slug = "other", DeploymentMode = DeploymentMode.LegacyFilesystem };
        f.Db.Applications.Add(otherApp);
        await f.Db.SaveChangesAsync();

        await f.Sut.CreateAsync(new CreateSecretReferenceRequest(
            "db-password", SecretCategory.Database, SecretScope.Application, f.App.Id, null, null, "sample-app-value"));

        var otherResolved = await f.Sut.ResolveForDeploymentAsync(otherApp.Id, f.DevEnv.Id, f.ManageUserId, "devops");

        Assert.False(otherResolved.ContainsKey("db-password"));
    }

    // ------------------------------------------------------------------- audit / redaction

    [Fact]
    public async Task CreateAsync_AuditsSecretCreated_WithoutTheValue()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;

        await f.Sut.CreateAsync(new CreateSecretReferenceRequest("db-password", SecretCategory.Database, SecretScope.Global, null, null, null, "hunter2-secret"));

        var entry = await f.Db.AuditLogs.SingleAsync(a => a.Action == "secret.created");
        Assert.DoesNotContain("hunter2-secret", entry.Details ?? string.Empty);
    }

    [Fact]
    public async Task UpdateAsync_AuditsSecretUpdated_WithoutTheValue()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;
        var created = await f.Sut.CreateAsync(new CreateSecretReferenceRequest("db-password", SecretCategory.Database, SecretScope.Global, null, null, null, "old"));

        await f.Sut.UpdateAsync(created.Id, new UpdateSecretReferenceRequest(null, true, "rotated-secret-value"));

        var entry = await f.Db.AuditLogs.SingleAsync(a => a.Action == "secret.updated");
        Assert.DoesNotContain("rotated-secret-value", entry.Details ?? string.Empty);
        Assert.Contains("valueRotated=True", entry.Details);
    }

    [Fact]
    public async Task DeleteAsync_AuditsSecretDeleted_WithoutTheValue()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;
        var created = await f.Sut.CreateAsync(new CreateSecretReferenceRequest("db-password", SecretCategory.Database, SecretScope.Global, null, null, null, "hunter2-secret"));

        await f.Sut.DeleteAsync(created.Id);

        var entry = await f.Db.AuditLogs.SingleAsync(a => a.Action == "secret.deleted");
        Assert.DoesNotContain("hunter2-secret", entry.Details ?? string.Empty);
    }

    [Fact]
    public async Task ResolveForDeploymentAsync_AuditsSecretReferenced_ForEachSecret_WithoutTheValue_AttributedToTheDeployRequester()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;
        await f.Sut.CreateAsync(new CreateSecretReferenceRequest(
            "db-password", SecretCategory.Database, SecretScope.ApplicationEnvironment, f.App.Id, f.DevEnv.Id, null, "hunter2-secret"));

        var deployRequesterId = Guid.NewGuid();
        await f.Sut.ResolveForDeploymentAsync(f.App.Id, f.DevEnv.Id, deployRequesterId, "the-deploy-requester");

        var entry = await f.Db.AuditLogs.SingleAsync(a => a.Action == "secret.referenced");
        Assert.DoesNotContain("hunter2-secret", entry.Details ?? string.Empty);
        Assert.Equal(deployRequesterId, entry.UserId);
        Assert.Equal("the-deploy-requester", entry.Username);
    }

    [Fact]
    public async Task ResolveForDeploymentAsync_WhenProviderFailsToRetrieve_ThrowsDeploymentExecutionException_NamingTheSecretNotTheValue()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ManageUserId;
        var created = await f.Sut.CreateAsync(new CreateSecretReferenceRequest(
            "db-password", SecretCategory.Database, SecretScope.ApplicationEnvironment, f.App.Id, f.DevEnv.Id, null, "value"));

        // Simulate the underlying ciphertext having gone missing (e.g. corrupted
        // store) without touching SecretReference's own metadata.
        var reference = await f.Db.SecretReferences.SingleAsync(s => s.Id == created.Id);
        var valueRecord = await f.Db.SecretValues.SingleAsync(v => v.StoreKey == reference.StoreKey);
        f.Db.SecretValues.Remove(valueRecord);
        await f.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DeploymentExecutionException>(() =>
            f.Sut.ResolveForDeploymentAsync(f.App.Id, f.DevEnv.Id, f.ManageUserId, "devops"));
        Assert.Contains("db-password", ex.Message);
    }
}
