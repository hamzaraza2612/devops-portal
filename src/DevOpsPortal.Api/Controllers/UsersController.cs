using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Dtos.Users;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsPortal.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(IUserService userService, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.UsersView)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await userService.GetAllAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.UsersView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await userService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.UsersManage)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        var created = await userService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.UsersManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken) =>
        Ok(await userService.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/reset-password")]
    [RequirePermission(PermissionCodes.UsersManage)]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] AdminResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await userService.ResetPasswordAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpPost("me/change-password")]
    public async Task<IActionResult> ChangeOwnPassword([FromBody] ChangeOwnPasswordRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Unauthorized();

        await userService.ChangeOwnPasswordAsync(userId, request, cancellationToken);
        return NoContent();
    }
}
