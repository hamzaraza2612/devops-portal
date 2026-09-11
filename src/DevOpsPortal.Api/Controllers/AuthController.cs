using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Dtos.Auth;
using DevOpsPortal.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevOpsPortal.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService, IUserService userService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>Rate-limited well below the API-wide default (see Program.cs's "auth"
    /// policy) — login is the highest-value target for automated credential
    /// guessing, so it gets its own, much stricter bucket.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
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
