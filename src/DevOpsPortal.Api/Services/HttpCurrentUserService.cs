using System.Security.Claims;
using DevOpsPortal.Application.Abstractions;

namespace DevOpsPortal.Api.Services;

public class HttpCurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var value = Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal?.FindFirstValue("sub");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? Username => Principal?.FindFirstValue(ClaimTypes.Name);

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
