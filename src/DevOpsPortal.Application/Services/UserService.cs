using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Users;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

public class UserService(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    IAuditService auditService,
    ICurrentTenantService currentTenantService) : IUserService
{
    public async Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var users = await db.Users.OrderBy(u => u.Username).ToListAsync(cancellationToken);
        var result = new List<UserDto>(users.Count);
        foreach (var user in users)
        {
            var (roles, permissions) = await db.GetRolesAndPermissionsAsync(user.Id, cancellationToken);
            result.Add(UserMapper.ToDto(user, roles, permissions));
        }
        return result;
    }

    public async Task<UserDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id);
        var (roles, permissions) = await db.GetRolesAndPermissionsAsync(user.Id, cancellationToken);
        return UserMapper.ToDto(user, roles, permissions);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim();

        // Username/Email are globally unique across every tenant (see AppDbContext) —
        // IgnoreQueryFilters so this check catches a collision with any tenant's user,
        // not just the current one.
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Username.ToLower() == username.ToLower(), cancellationToken))
            throw new ConflictException($"Username '{username}' is already in use.");
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email.ToLower() == email.ToLower(), cancellationToken))
            throw new ConflictException($"Email '{email}' is already in use.");

        ValidatePassword(request.Password);
        var roles = await ResolveRolesAsync(request.RoleIds, cancellationToken);

        var user = new User
        {
            TenantId = currentTenantService.RequireTenantId(),
            Username = username,
            Email = email,
            FullName = request.FullName.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            IsActive = true,
        };
        foreach (var role in roles)
            user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("user.create", AuditResult.Success, "User", user.Id.ToString(),
            details: $"Created user '{user.Username}' with roles [{string.Join(", ", roles.Select(r => r.Name))}]",
            cancellationToken: cancellationToken);

        var (roleNames, permissions) = await db.GetRolesAndPermissionsAsync(user.Id, cancellationToken);
        return UserMapper.ToDto(user, roleNames, permissions);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id);

        var email = request.Email.Trim();
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id != id && u.Email.ToLower() == email.ToLower(), cancellationToken))
            throw new ConflictException($"Email '{email}' is already in use.");

        var roles = await ResolveRolesAsync(request.RoleIds, cancellationToken);

        user.Email = email;
        user.FullName = request.FullName.Trim();
        user.IsActive = request.IsActive;

        var existingLinks = await db.UserRoles.Where(ur => ur.UserId == id).ToListAsync(cancellationToken);
        foreach (var link in existingLinks)
            db.UserRoles.Remove(link);
        foreach (var role in roles)
            db.UserRoles.Add(new UserRole { UserId = id, RoleId = role.Id });

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("user.update", AuditResult.Success, "User", user.Id.ToString(),
            details: $"Updated user '{user.Username}'; active={user.IsActive}; roles=[{string.Join(", ", roles.Select(r => r.Name))}]",
            cancellationToken: cancellationToken);

        var (roleNames, permissions) = await db.GetRolesAndPermissionsAsync(user.Id, cancellationToken);
        return UserMapper.ToDto(user, roleNames, permissions);
    }

    public async Task ResetPasswordAsync(Guid id, AdminResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id);

        ValidatePassword(request.NewPassword);
        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("user.reset_password", AuditResult.Success, "User", user.Id.ToString(),
            details: $"Password reset by admin for '{user.Username}'", cancellationToken: cancellationToken);
    }

    public async Task ChangeOwnPasswordAsync(Guid userId, ChangeOwnPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User", userId);

        if (!passwordHasher.Verify(user.PasswordHash, request.CurrentPassword))
        {
            await auditService.LogAsync("user.change_password", AuditResult.Failure, "User", user.Id.ToString(),
                details: "Current password did not match", cancellationToken: cancellationToken);
            throw new ValidationException("Current password is incorrect.");
        }

        ValidatePassword(request.NewPassword);
        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("user.change_password", AuditResult.Success, "User", user.Id.ToString(),
            cancellationToken: cancellationToken);
    }

    private async Task<List<Role>> ResolveRolesAsync(IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
            throw new ValidationException("At least one role must be assigned.");

        var distinctIds = roleIds.Distinct().ToList();
        var roles = await db.Roles.Where(r => distinctIds.Contains(r.Id)).ToListAsync(cancellationToken);
        if (roles.Count != distinctIds.Count)
            throw new ValidationException("One or more role IDs are invalid.");
        return roles;
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ValidationException("Password must be at least 8 characters long.");
    }
}
