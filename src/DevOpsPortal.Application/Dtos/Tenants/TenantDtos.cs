namespace DevOpsPortal.Application.Dtos.Tenants;

public record TenantDto(Guid Id, string Name, string Slug, string? Description, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

/// <summary>Provisions a new tenant plus its default role/environment set and one
/// initial tenant-admin user (see TenantService.CreateAsync) — a brand new tenant
/// otherwise has nobody who could log in to manage it. Leave InitialAdminPassword
/// unset to have one generated and returned once in CreateTenantResponse.</summary>
public record CreateTenantRequest(
    string Name,
    string Slug,
    string? Description,
    string InitialAdminUsername,
    string InitialAdminEmail,
    string? InitialAdminPassword);

/// <summary>GeneratedPassword is populated only when InitialAdminPassword was left
/// unset on the request — this is the only time it is ever available in plaintext,
/// so the caller must capture and hand it off now.</summary>
public record CreateTenantResponse(TenantDto Tenant, string InitialAdminUsername, string? GeneratedPassword);

public record UpdateTenantRequest(string Name, string? Description, bool IsActive);
