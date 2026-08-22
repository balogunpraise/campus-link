using System.Security.Claims;
using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Auth;
using CampusLink.Core.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusLink.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, GetIpAddress(), cancellationToken);

        return result.Succeeded ? Ok(result.Value) : ToProblem(result);
    }

    // Accepts a username, email address, or phone number in `identifier`.
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, GetIpAddress(), cancellationToken);

        return result.Succeeded ? Ok(result.Value) : ToProblem(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.RefreshAsync(request.RefreshToken, GetIpAddress(), cancellationToken);

        return result.Succeeded ? Ok(result.Value) : ToProblem(result);
    }

    // Logout: kills the refresh token. The access token stays valid until it
    // expires — that's the trade-off of stateless JWTs, so keep them short.
    [HttpPost("revoke")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(RevokeRequest request, CancellationToken cancellationToken)
    {
        await authService.RevokeAsync(request.RefreshToken, GetIpAddress(), cancellationToken);

        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out var id))
            return Unauthorized();

        var result = await authService.GetCurrentUserAsync(id, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : ToProblem(result);
    }

    // Behind a proxy the socket address is the load balancer, so prefer the
    // forwarded header when one is present.
    private string GetIpAddress() =>
        Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded)
            ? forwarded.ToString().Split(',')[0].Trim()
            : HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private IActionResult ToProblem<T>(Result<T> result)
    {
        var status = result.ErrorCode switch
        {
            AuthErrorCode.InvalidCredentials => StatusCodes.Status401Unauthorized,
            AuthErrorCode.InvalidToken => StatusCodes.Status401Unauthorized,
            AuthErrorCode.AccountLocked => StatusCodes.Status423Locked,
            AuthErrorCode.EmailNotConfirmed => StatusCodes.Status403Forbidden,
            AuthErrorCode.Conflict => StatusCodes.Status409Conflict,
            AuthErrorCode.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest
        };

        return Problem(
            title: result.ErrorCode.ToString(),
            detail: string.Join(" ", result.Errors),
            statusCode: status);
    }
}
