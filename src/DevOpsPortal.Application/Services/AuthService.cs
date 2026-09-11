using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Auth;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

public class AuthService(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    IAuditService auditService) : IAuthService
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var username = request.Username.Trim();

        // Username is globally unique across all tenants (see UserService), so login
        // must look up across every tenant before any tenant context is known —
        // the tenant claim itself only exists once this lookup has already succeeded.
        var user = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower(), cancellationToken);

        if (user is null || !passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            await auditService.LogAsync(
                "auth.login", AuditResult.Failure,
                entityType: "User", entityId: user?.Id.ToString(),
                details: "Invalid credentials", actorUsername: username, cancellationToken: cancellationToken);
            throw new AuthenticationFailedException();
        }

        if (!user.IsActive)
        {
            await auditService.LogAsync(
                "auth.login", AuditResult.Failure,
                entityType: "User", entityId: user.Id.ToString(),
                details: "Account inactive", actorUserId: user.Id, actorUsername: user.Username, cancellationToken: cancellationToken);
            throw new AuthenticationFailedException("This account is inactive.");
        }

        if (user.TenantId is { } tenantId)
        {
            // Tenant has no query filter of its own (it IS the isolation boundary), so
            // this needs no IgnoreQueryFilters — deactivating a tenant locks out every
            // one of its users without having to touch each User row individually.
            var tenantActive = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.IsActive).FirstOrDefaultAsync(cancellationToken);
            if (!tenantActive)
            {
                await auditService.LogAsync(
                    "auth.login", AuditResult.Failure,
                    entityType: "User", entityId: user.Id.ToString(),
                    details: "Tenant inactive", actorUserId: user.Id, actorUsername: user.Username, cancellationToken: cancellationToken);
                throw new AuthenticationFailedException("This account's organization is inactive.");
            }
        }

        var (roles, permissions) = await db.GetRolesAndPermissionsAsync(user.Id, cancellationToken);

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var token = jwtTokenService.GenerateToken(user, roles, permissions);

        await auditService.LogAsync(
            "auth.login", AuditResult.Success,
            entityType: "User", entityId: user.Id.ToString(),
            actorUserId: user.Id, actorUsername: user.Username, cancellationToken: cancellationToken);

        return new LoginResponse(token.Token, token.ExpiresAt, UserMapper.ToDto(user, roles, permissions));
    }
}
