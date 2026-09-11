using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Auth;
using DevOpsPortal.Application.Dtos.Environments;
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

        var user = await db.Users
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

        var (roles, permissions) = await db.GetRolesAndPermissionsAsync(user.Id, cancellationToken);
        var environmentAccess = await db.UserEnvironmentAccess
            .Where(a => a.UserId == user.Id)
            .Select(a => a.EnvironmentDefinition)
            .OrderBy(e => e.SortOrder)
            .Select(e => new EnvironmentDefinitionDto(e.Id, e.Name, e.SortOrder, e.IsProductionLike, e.IsActive))
            .ToListAsync(cancellationToken);

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var token = jwtTokenService.GenerateToken(user, roles, permissions);

        await auditService.LogAsync(
            "auth.login", AuditResult.Success,
            entityType: "User", entityId: user.Id.ToString(),
            actorUserId: user.Id, actorUsername: user.Username, cancellationToken: cancellationToken);

        return new LoginResponse(token.Token, token.ExpiresAt, UserMapper.ToDto(user, environmentAccess, roles, permissions));
    }
}
