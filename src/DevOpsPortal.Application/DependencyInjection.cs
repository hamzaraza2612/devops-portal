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
        services.AddScoped<IApplicationService, ApplicationService>();
        services.AddScoped<IApplicationEnvironmentService, ApplicationEnvironmentService>();
        services.AddScoped<IRepositoryService, RepositoryService>();
        services.AddScoped<ITargetServerService, TargetServerService>();
        services.AddScoped<IEnvironmentDefinitionService, EnvironmentDefinitionService>();
        services.AddScoped<IComposeFileAnalyzer, ComposeFileAnalyzer>();
        services.AddScoped<IDeploymentExecutor, DeploymentExecutor>();
        services.AddScoped<IDeploymentService, DeploymentService>();
        services.AddScoped<IBuildConfigurationService, BuildConfigurationService>();
        return services;
    }
}
