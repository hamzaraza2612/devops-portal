using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Infrastructure.Security;

namespace DevOpsPortal.Api.Middleware;

/// <summary>
/// Runs after authentication: copies the authenticated user's tenant claim
/// (if any) into the scoped IMutableTenantContext so AppDbContext's global
/// query filters see it for the rest of the request. A request with no tenant
/// claim (anonymous, or a platform administrator) leaves it null.
/// </summary>
public class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IMutableTenantContext tenantContext)
    {
        var claim = context.User.FindFirst(PortalClaimTypes.TenantId)?.Value;
        if (Guid.TryParse(claim, out var tenantId))
            tenantContext.SetTenantId(tenantId);

        await next(context);
    }
}
