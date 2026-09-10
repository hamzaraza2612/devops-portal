using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Dtos.Auth;
using DevOpsPortal.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsPortal.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService, IUserService userService, ICurrentUserService currentUser) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Unauthorized();

        return Ok(await userService.GetByIdAsync(userId, cancellationToken));
    }
}
