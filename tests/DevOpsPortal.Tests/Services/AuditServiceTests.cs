using DevOpsPortal.Application.Dtos.Audit;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Tests.Common;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class AuditServiceTests
{
    [Fact]
    public async Task LogAsync_FillsActorFromCurrentUserWhenNotSpecified()
    {
        var db = TestDb.CreateInMemory();
        var userId = Guid.NewGuid();
        var currentUser = new FakeCurrentUserService { UserId = userId, Username = "kate", IpAddress = "10.0.0.5" };
        var sut = new AuditService(db, currentUser);

        await sut.LogAsync("application.create", AuditResult.Success, "Application", "app-1");

        var entry = Assert.Single(db.AuditLogs);
        Assert.Equal(userId, entry.UserId);
        Assert.Equal("kate", entry.Username);
        Assert.Equal("10.0.0.5", entry.IpAddress);
    }

    [Fact]
    public async Task QueryAsync_FiltersByActionAndPagesResults()
    {
        var db = TestDb.CreateInMemory();
        var sut = new AuditService(db, new FakeCurrentUserService());

        for (var i = 0; i < 3; i++)
            await sut.LogAsync("auth.login", AuditResult.Success);
        await sut.LogAsync("user.create", AuditResult.Success);

        var loginPage = await sut.QueryAsync(new AuditQuery(Action: "auth.login", Page: 1, PageSize: 2));

        Assert.Equal(3, loginPage.TotalCount);
        Assert.Equal(2, loginPage.Items.Count);
        Assert.All(loginPage.Items, i => Assert.Equal("auth.login", i.Action));
    }
}
