using DevOpsPortal.Application.Dtos.Tenants;

namespace DevOpsPortal.Application.Services;

/// <summary>Platform-administrator-only: tenant CRUD is never delegated to a
/// tenant's own roles (see PermissionCodes.TenantsView/TenantsManage).</summary>
public interface ITenantService
{
    Task<IReadOnlyList<TenantDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<TenantDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CreateTenantResponse> CreateAsync(CreateTenantRequest request, CancellationToken cancellationToken = default);
    Task<TenantDto> UpdateAsync(Guid id, UpdateTenantRequest request, CancellationToken cancellationToken = default);
}
