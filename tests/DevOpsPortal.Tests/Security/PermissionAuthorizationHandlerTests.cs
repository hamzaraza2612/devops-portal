using System.Security.Claims;
using DevOpsPortal.Infrastructure.Authorization;
using DevOpsPortal.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace DevOpsPortal.Tests.Security;

public class PermissionAuthorizationHandlerTests
{
    private static AuthorizationHandlerContext Context(ClaimsPrincipal user, string permissionCode)
    {
        var requirement = new PermissionRequirement(permissionCode);
        return new AuthorizationHandlerContext([requirement], user, null);
    }

    [Fact]
    public async Task Succeeds_WhenUserHasMatchingPermissionClaim()
    {
        var identity = new ClaimsIdentity([new Claim(PortalClaimTypes.Permission, "users.manage")], "test");
        var context = Context(new ClaimsPrincipal(identity), "users.manage");

        await new PermissionAuthorizationHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Fails_WhenUserLacksPermissionClaim()
    {
        var identity = new ClaimsIdentity([new Claim(PortalClaimTypes.Permission, "users.view")], "test");
        var context = Context(new ClaimsPrincipal(identity), "users.manage");

        await new PermissionAuthorizationHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
