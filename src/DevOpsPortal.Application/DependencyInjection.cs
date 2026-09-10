using DevOpsPortal.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsPortal.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IAuditService, AuditService>();
        return services;
    }
}
