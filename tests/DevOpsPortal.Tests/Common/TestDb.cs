using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Tests.Common;

/// <summary>
/// Phase 12 replaced the Role/Permission catalog and multi-tenancy with a
/// single-organization model: User.IsAdmin (full access), User.
/// CanApproveProduction (independent "CTO" flag), and per-environment
/// UserEnvironmentAccess rows — see AppDbContextExtensions.
/// GetRolesAndPermissionsAsync for how these synthesize into the same
/// PermissionCodes claims every service still checks. These helpers build
/// test users directly from that model rather than a Role/Permission catalog.
/// </summary>
public static class TestDb
{
    public static AppDbContext CreateInMemory()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    public static async Task SeedEnvironmentDefinitionsAsync(AppDbContext db)
    {
        foreach (var (name, sortOrder, isProductionLike) in EnvironmentNames.All)
        {
            db.EnvironmentDefinitions.Add(new EnvironmentDefinition
            {
                Name = name, SortOrder = sortOrder, IsProductionLike = isProductionLike,
            });
        }
        await db.SaveChangesAsync();
    }

    public static async Task<Guid> CreateAdminAsync(AppDbContext db, string username)
    {
        var user = new User
        {
            Username = username,
            Email = $"{username}@example.local",
            FullName = username,
            PasswordHash = "x",
            IsActive = true,
            IsAdmin = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Creates a user with access to exactly the given environments (by
    /// EnvironmentDefinition.Name, e.g. "DEV", "QA") and, independently,
    /// CanApproveProduction — mirrors the real synthesis model 1:1, so a test
    /// asserting behavior for "a QA-only user" gets exactly the same effective
    /// permissions QA-only access grants in production. Requires
    /// EnvironmentDefinitions already seeded (SeedEnvironmentDefinitionsAsync).</summary>
    public static async Task<Guid> CreateUserWithEnvironmentAccessAsync(
        AppDbContext db, string username, IReadOnlyList<string> environmentNames, bool canApproveProduction = false)
    {
        var user = new User
        {
            Username = username,
            Email = $"{username}@example.local",
            FullName = username,
            PasswordHash = "x",
            IsActive = true,
            CanApproveProduction = canApproveProduction,
        };
        db.Users.Add(user);

        foreach (var name in environmentNames)
        {
            var env = await db.EnvironmentDefinitions.FirstOrDefaultAsync(e => e.Name == name)
                ?? throw new InvalidOperationException($"EnvironmentDefinition '{name}' is not seeded in this test db.");
            user.EnvironmentAccess.Add(new UserEnvironmentAccess { UserId = user.Id, EnvironmentDefinitionId = env.Id });
        }

        await db.SaveChangesAsync();
        return user.Id;
    }

    public static Task<Guid> CreateUserWithEnvironmentAccessAsync(AppDbContext db, string username, params string[] environmentNames) =>
        CreateUserWithEnvironmentAccessAsync(db, username, (IReadOnlyList<string>)environmentNames);

    /// <summary>A user with no environment access and no admin/CTO flag —
    /// authenticated, but every permission check should deny them.</summary>
    public static Task<Guid> CreateUserWithNoAccessAsync(AppDbContext db, string username) =>
        CreateUserWithEnvironmentAccessAsync(db, username, []);
}

public class FakeCurrentUserService : ICurrentUserService
{
    public Guid? UserId { get; set; }
    public string? Username { get; set; }
    public string? IpAddress { get; set; } = "127.0.0.1";
}

/// <summary>In-memory stand-in for the real encrypted secret store — good
/// enough for tests that only need round-tripping, not real encryption.</summary>
public class FakeSecretProvider : ISecretProvider
{
    private readonly Dictionary<string, string> _values = new();

    public string ProviderKey => "fake";

    /// <summary>Test-only introspection — lets a test assert that a Delete
    /// operation actually cleaned up its stored credential(s) rather than
    /// leaving them orphaned in the store.</summary>
    public int StoredValueCount => _values.Count;

    public Task<string> StoreAsync(string? existingStoreKey, string plaintextValue, CancellationToken cancellationToken = default)
    {
        var key = existingStoreKey ?? Guid.NewGuid().ToString();
        _values[key] = plaintextValue;
        return Task.FromResult(key);
    }

    public Task<SecretValueResult> RetrieveAsync(string storeKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_values.TryGetValue(storeKey, out var value)
            ? SecretValueResult.Ok(value)
            : SecretValueResult.Fail("Fake provider: unknown store key."));

    public Task DeleteAsync(string storeKey, CancellationToken cancellationToken = default)
    {
        _values.Remove(storeKey);
        return Task.CompletedTask;
    }
}

/// <summary>Defaults to reporting every method unreachable — exercises the
/// "graceful failure" path without any real network access. Pass
/// promoteBranchResult to configure PromoteBranchAsync's outcome for tests
/// that specifically exercise branch promotion (success or a named failure).</summary>
public class FakeGitProviderClient(GitProviderResult<string>? promoteBranchResult = null, GitProviderResult<byte[]>? archiveResult = null) : IGitProviderClient
{
    public Task<GitProviderResult<GitCommitInfo>> GetLatestCommitAsync(
        Repository repository, string branch, CancellationToken cancellationToken = default) =>
        Task.FromResult(GitProviderResult<GitCommitInfo>.Fail("Fake provider: not reachable in tests."));

    public Task<GitProviderResult<IReadOnlyList<GitCommitInfo>>> GetRecentCommitsAsync(
        Repository repository, string branch, int count, CancellationToken cancellationToken = default) =>
        Task.FromResult(GitProviderResult<IReadOnlyList<GitCommitInfo>>.Fail("Fake provider: not reachable in tests."));

    public Task<GitProviderResult<string>> PromoteBranchAsync(
        Repository repository, string sourceBranch, string targetBranch, CancellationToken cancellationToken = default) =>
        Task.FromResult(promoteBranchResult ?? GitProviderResult<string>.Fail("Fake provider: not reachable in tests."));

    public Task<GitConnectionTestResult> TestConnectionAsync(Repository repository, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GitConnectionTestResult(false, null, null, "Fake provider: not reachable in tests."));

    public Task<GitProviderResult<byte[]>> DownloadRepositoryArchiveAsync(
        Repository repository, string refName, CancellationToken cancellationToken = default) =>
        Task.FromResult(archiveResult ?? GitProviderResult<byte[]>.Fail("Fake provider: not reachable in tests."));
}
