using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Infrastructure.Authorization;
using DevOpsPortal.Infrastructure.Deployments;
using DevOpsPortal.Infrastructure.Email;
using DevOpsPortal.Infrastructure.Git;
using DevOpsPortal.Infrastructure.Persistence;
using DevOpsPortal.Infrastructure.Remote;
using DevOpsPortal.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsPortal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        services.AddHttpClient<IGitProviderClient, GitLabProviderClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent", "DevOpsPortal");
        });

        services.AddSingleton<IDeploymentJobQueue, InMemoryDeploymentJobQueue>();
        services.AddHostedService<DeploymentWorker>();

        services.AddSingleton<IComposeCommandExecutor, ComposeCommandExecutor>();
        services.AddHttpClient<IHealthCheckProbe, HealthCheckProbe>(client => client.Timeout = TimeSpan.FromSeconds(30));

        // No secure remote execution mechanism exists yet (see PROJECT_STATE.md's
        // Phase 5 remote-execution correction) — NotConfiguredRemoteExecutionProvider
        // is the only implementation, and is honest that every target server is
        // unreachable rather than silently running Docker locally. Swap this
        // registration, not any caller, when real remote connectivity is built.
        services.AddSingleton<IRemoteExecutionProvider, NotConfiguredRemoteExecutionProvider>();
        services.AddSingleton<IContainerRuntimeProvider, DockerComposeContainerRuntimeProvider>();

        services.Configure<SmtpSettings>(configuration.GetSection(SmtpSettings.SectionName));
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
