using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Infrastructure.Authorization;
using DevOpsPortal.Infrastructure.Build;
using DevOpsPortal.Infrastructure.Deployments;
using DevOpsPortal.Infrastructure.Email;
using DevOpsPortal.Infrastructure.Git;
using DevOpsPortal.Infrastructure.Notifications;
using DevOpsPortal.Infrastructure.Secrets;
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

        // EnableRetryOnFailure: transient network blips/Postgres restarts shouldn't take
        // the whole API down with them — retries only genuinely transient Npgsql errors,
        // never masks a real query/constraint failure.
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3)));
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

        services.AddHttpClient<IHealthCheckProbe, HealthCheckProbe>(client => client.Timeout = TimeSpan.FromSeconds(30));

        // Phase 12: real SSH-based remote execution (master requirements §5/§7 — the
        // portal VM is separate from every TargetServer). SshRemoteExecutionProvider
        // is honest per-TargetServer too: IsConfigured() is false (nothing attempted)
        // until a TargetServer has Hostname/SshUsername/a stored credential, in which
        // case it behaves exactly like NotConfiguredRemoteExecutionProvider did for
        // every server before this phase. Scoped (not Singleton): depends on
        // ISecretProvider (registered below), which is itself Scoped — and
        // IContainerRuntimeProvider, which is built on top of it, must be Scoped too
        // so a Singleton never captures a Scoped dependency.
        services.AddScoped<IRemoteExecutionProvider, SshRemoteExecutionProvider>();
        services.AddScoped<IContainerRuntimeProvider, DockerComposeContainerRuntimeProvider>();

        services.Configure<SmtpSettings>(configuration.GetSection(SmtpSettings.SectionName));
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        // Registered as INotificationProvider (not the concrete type) so
        // INotificationService's IEnumerable<INotificationProvider> broadcast can
        // add Slack/Teams/webhook providers later with no caller change.
        services.AddSingleton<INotificationProvider, EmailNotificationProvider>();

        // Jenkins is reached over plain HTTP(S) from wherever the portal runs — unlike
        // Phase 5's remote Docker gap, no socket/SSH/agent infrastructure is needed, so
        // this is a real, operational implementation once a BuildServer is configured.
        // Registered as IBuildProvider (not the concrete type) so IBuildService's
        // IEnumerable<IBuildProvider> lookup-by-ProviderType pattern can add another
        // provider later without any caller change (master requirements §1).
        services.AddHttpClient<IBuildProvider, JenkinsBuildProvider>(client => client.Timeout = TimeSpan.FromSeconds(30));

        services.Configure<SecretEncryptionSettings>(configuration.GetSection(SecretEncryptionSettings.SectionName));
        // Scoped, not Singleton: EncryptedSecretProvider depends on the concrete
        // AppDbContext, which AddDbContext<AppDbContext> above registers Scoped.
        services.AddScoped<ISecretProvider, EncryptedSecretProvider>();

        return services;
    }
}
