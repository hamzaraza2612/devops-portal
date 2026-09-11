using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Environments;
using DevOpsPortal.Application.Dtos.Users;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

public class UserService(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    IAuditService auditService) : IUserService
{
    public async Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var users = await db.Users.OrderBy(u => u.Username).ToListAsync(cancellationToken);
        var result = new List<UserDto>(users.Count);
        foreach (var user in users)
        {
            var environmentAccess = await GetEnvironmentAccessAsync(user.Id, cancellationToken);
            var (roles, permissions) = await db.GetRolesAndPermissionsAsync(user.Id, cancellationToken);
            result.Add(UserMapper.ToDto(user, environmentAccess, roles, permissions));
        }
        return result;
    }

    public async Task<UserDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id);
        var environmentAccess = await GetEnvironmentAccessAsync(user.Id, cancellationToken);
        var (roles, permissions) = await db.GetRolesAndPermissionsAsync(user.Id, cancellationToken);
        return UserMapper.ToDto(user, environmentAccess, roles, permissions);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim();

        if (await db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower(), cancellationToken))
            throw new ConflictException($"Username '{username}' is already in use.");
        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower(), cancellationToken))
            throw new ConflictException($"Email '{email}' is already in use.");

        ValidatePassword(request.Password);
        var environments = await ResolveEnvironmentsAsync(request.EnvironmentDefinitionIds, cancellationToken);

        var user = new User
        {
            Username = username,
            Email = email,
            FullName = request.FullName.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            IsActive = true,
            IsAdmin = request.IsAdmin,
            CanApproveProduction = request.CanApproveProduction,
        };
        foreach (var env in environments)
            user.EnvironmentAccess.Add(new UserEnvironmentAccess { UserId = user.Id, EnvironmentDefinitionId = env.Id });

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("user.create", AuditResult.Success, "User", user.Id.ToString(),
            details: $"Created user '{user.Username}'; admin={user.IsAdmin}; canApproveProduction={user.CanApproveProduction}; environments=[{string.Join(", ", environments.Select(e => e.Name))}]",
            cancellationToken: cancellationToken);

        var environmentAccess = ToDtos(environments);
        var (roles, permissions) = await db.GetRolesAndPermissionsAsync(user.Id, cancellationToken);
        return UserMapper.ToDto(user, environmentAccess, roles, permissions);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id);

        var email = request.Email.Trim();
        if (await db.Users.AnyAsync(u => u.Id != id && u.Email.ToLower() == email.ToLower(), cancellationToken))
            throw new ConflictException($"Email '{email}' is already in use.");

        var environments = await ResolveEnvironmentsAsync(request.EnvironmentDefinitionIds, cancellationToken);

        user.Email = email;
        user.FullName = request.FullName.Trim();
        user.IsActive = request.IsActive;
        user.IsAdmin = request.IsAdmin;
        user.CanApproveProduction = request.CanApproveProduction;

        var existingLinks = await db.UserEnvironmentAccess.Where(a => a.UserId == id).ToListAsync(cancellationToken);
        foreach (var link in existingLinks)
            db.UserEnvironmentAccess.Remove(link);
        foreach (var env in environments)
            db.UserEnvironmentAccess.Add(new UserEnvironmentAccess { UserId = id, EnvironmentDefinitionId = env.Id });

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("user.update", AuditResult.Success, "User", user.Id.ToString(),
            details: $"Updated user '{user.Username}'; active={user.IsActive}; admin={user.IsAdmin}; canApproveProduction={user.CanApproveProduction}; environments=[{string.Join(", ", environments.Select(e => e.Name))}]",
            cancellationToken: cancellationToken);

        var environmentAccess = ToDtos(environments);
        var (roles, permissions) = await db.GetRolesAndPermissionsAsync(user.Id, cancellationToken);
        return UserMapper.ToDto(user, environmentAccess, roles, permissions);
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

    private async Task<List<EnvironmentDefinition>> ResolveEnvironmentsAsync(IReadOnlyList<Guid> environmentDefinitionIds, CancellationToken cancellationToken)
    {
        var distinctIds = environmentDefinitionIds.Distinct().ToList();
        if (distinctIds.Count == 0)
            return [];

        var environments = await db.EnvironmentDefinitions.Where(e => distinctIds.Contains(e.Id)).ToListAsync(cancellationToken);
        if (environments.Count != distinctIds.Count)
            throw new ValidationException("One or more environment IDs are invalid.");
        return environments;
    }

    private async Task<IReadOnlyList<EnvironmentDefinitionDto>> GetEnvironmentAccessAsync(Guid userId, CancellationToken cancellationToken)
    {
        var environments = await db.UserEnvironmentAccess
            .Where(a => a.UserId == userId)
            .Select(a => a.EnvironmentDefinition)
            .OrderBy(e => e.SortOrder)
            .ToListAsync(cancellationToken);
        return ToDtos(environments);
    }

    private static IReadOnlyList<EnvironmentDefinitionDto> ToDtos(IEnumerable<EnvironmentDefinition> environments) =>
        environments
            .OrderBy(e => e.SortOrder)
            .Select(e => new EnvironmentDefinitionDto(e.Id, e.Name, e.SortOrder, e.IsProductionLike, e.IsActive))
            .ToList();

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ValidationException("Password must be at least 8 characters long.");
    }
}
