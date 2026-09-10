using Microsoft.AspNetCore.Authorization;

namespace DevOpsPortal.Infrastructure.Authorization;

public class RequirePermissionAttribute(string permissionCode) : AuthorizeAttribute(PermissionPolicyProvider.Prefix + permissionCode);
