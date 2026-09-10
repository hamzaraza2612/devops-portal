using Microsoft.AspNetCore.Authorization;

namespace DevOpsPortal.Infrastructure.Authorization;

public class PermissionRequirement(string permissionCode) : IAuthorizationRequirement
{
    public string PermissionCode { get; } = permissionCode;
}
