using DevOpsPortal.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;

namespace DevOpsPortal.Infrastructure.Authorization;

public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Claims.Any(c => c.Type == PortalClaimTypes.Permission && c.Value == requirement.PermissionCode))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
