using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using DevOpsPortal.Api.Middleware;
using DevOpsPortal.Api.Services;
using DevOpsPortal.Application;
using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Infrastructure;
using DevOpsPortal.Infrastructure.Persistence;
using DevOpsPortal.Infrastructure.Secrets;
using DevOpsPortal.Infrastructure.Security;
using DevOpsPortal.Infrastructure.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpCurrentUserService>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
if (string.IsNullOrWhiteSpace(jwtSettings.SigningKey) || Encoding.UTF8.GetByteCount(jwtSettings.SigningKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey (env JWT_SIGNING_KEY) must be configured with at least 32 bytes before starting the API.");
}

var secretEncryptionSettings = builder.Configuration.GetSection(SecretEncryptionSettings.SectionName).Get<SecretEncryptionSettings>() ?? new SecretEncryptionSettings();
if (string.IsNullOrWhiteSpace(secretEncryptionSettings.EncryptionKey) || Encoding.UTF8.GetByteCount(secretEncryptionSettings.EncryptionKey) < 32)
{
    throw new InvalidOperationException(
        "Secrets:EncryptionKey (env SECRET_ENCRYPTION_KEY) must be configured with at least 32 bytes before starting the API.");
}

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });

builder.Services.AddAuthorization();

// Partitioned per client IP (falls back to a shared bucket if none is available,
// e.g. some test hosts) — protects the API from brute-force/credential-stuffing and
// general abuse without needing an external gateway. "auth" is deliberately much
// stricter than the API-wide default since login is the highest-value target for
// automated guessing; every other authenticated endpoint is already gated by the
// permission system, so the wider default policy is a resource-exhaustion guard, not
// a substitute for authorization.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            JsonSerializer.Serialize(new { error = "Too many requests. Please try again shortly." }), cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// Granular checks (tagged so /health/{db,worker,integrations} can each expose just
// their own slice) alongside the aggregate /health and a dependency-free /health/live
// for orchestrator liveness probes — see the mapped endpoints below.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: ["ready", "db"])
    .AddCheck<DevOpsPortal.Infrastructure.Deployments.BackgroundWorkerHealthCheck>("worker", tags: ["ready", "worker"])
    .AddCheck<DevOpsPortal.Infrastructure.Integrations.IntegrationsHealthCheck>("integrations", tags: ["integrations"]);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DataSeeder.SeedAsync(db, hasher, app.Configuration, logger);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// /health: everything, for a simple all-in-one probe. /health/live: no dependencies —
// answers "is the process up" for an orchestrator restart policy. /health/ready:
// database + background worker — "can this instance actually serve deployments".
// /health/db, /health/worker, /health/integrations: each component on its own, for
// targeted troubleshooting. All unauthenticated by design (no sensitive data in the
// response), matching the pre-existing /health.
var healthResponseOptions = new HealthCheckOptions { ResponseWriter = WriteHealthCheckResponseAsync };
app.MapHealthChecks("/health", healthResponseOptions);
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteHealthCheckResponseAsync });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready"), ResponseWriter = WriteHealthCheckResponseAsync });
app.MapHealthChecks("/health/db", new HealthCheckOptions { Predicate = c => c.Tags.Contains("db"), ResponseWriter = WriteHealthCheckResponseAsync });
app.MapHealthChecks("/health/worker", new HealthCheckOptions { Predicate = c => c.Tags.Contains("worker"), ResponseWriter = WriteHealthCheckResponseAsync });
app.MapHealthChecks("/health/integrations", new HealthCheckOptions { Predicate = c => c.Tags.Contains("integrations"), ResponseWriter = WriteHealthCheckResponseAsync });

app.Run();

static Task WriteHealthCheckResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var payload = new
    {
        status = report.Status.ToString(),
        // From the Api project's <Version> (csproj) — lets an operator confirm an
        // upgrade actually took effect without digging through container image tags.
        version = typeof(Program).Assembly.GetName().Version?.ToString(3),
        totalDurationMs = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            durationMs = e.Value.Duration.TotalMilliseconds,
        }),
    };
    return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
}

public partial class Program;
