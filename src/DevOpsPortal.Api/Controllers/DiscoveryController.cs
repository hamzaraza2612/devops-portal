using DevOpsPortal.Application.Dtos.Discovery;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsPortal.Api.Controllers;

/// <summary>
/// Read-only helpers for turning an existing docker-compose.yml (pasted or
/// uploaded by an admin) into a structured preview. Never touches a live
/// server, never executes anything, and never persists or deploys on its own —
/// the admin reviews the result and configures the application through the
/// normal Applications/TargetServers endpoints.
/// </summary>
[ApiController]
[Route("api/discovery")]
[Authorize]
[RequirePermission(PermissionCodes.ApplicationsManage)]
public class DiscoveryController(IComposeFileAnalyzer composeFileAnalyzer) : ControllerBase
{
    [HttpPost("analyze-compose")]
    public IActionResult AnalyzeCompose([FromBody] ComposeAnalysisRequest request) =>
        Ok(composeFileAnalyzer.Analyze(request.Content));
}
