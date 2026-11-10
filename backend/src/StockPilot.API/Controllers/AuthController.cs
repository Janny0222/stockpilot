using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StockPilot.API.Extensions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Features.Auth;
using StockPilot.Application.Features.Auth.Common;

namespace StockPilot.API.Controllers;

public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

public sealed record TokenRequest(string Token);

public sealed record EmailRequest(string Email);

/// <summary>
/// Thin by design: translate HTTP ⟷ use case, manage the refresh cookie, nothing else.
/// All rules live in the Application handlers.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(IHostEnvironment environment) : ControllerBase
{
    internal const string RefreshCookieName = "sp_rt";
    private const string RefreshCookiePath = "/api/v1/auth";

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<RegisterCompanyResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(
        RegisterCompanyCommand command, [FromServices] RegisterCompanyHandler handler, CancellationToken ct)
    {
        var result = await handler.Handle(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AccessTokenResponse>(StatusCodes.Status200OK)]
    public async Task<AccessTokenResponse> Login(LoginCommand command, [FromServices] LoginHandler handler, CancellationToken ct)
    {
        var result = await handler.Handle(command, ct);
        return IssueSession(result);
    }

    /// <summary>Exchanges the HttpOnly refresh cookie for a new access token (and a rotated cookie).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AccessTokenResponse>(StatusCodes.Status200OK)]
    public async Task<AccessTokenResponse> Refresh([FromServices] RefreshSessionHandler handler, CancellationToken ct)
    {
        try
        {
            var result = await handler.Handle(new RefreshSessionCommand(Request.Cookies[RefreshCookieName]), ct);
            return IssueSession(result);
        }
        catch (AuthenticationFailedException)
        {
            ClearRefreshCookie();
            throw;
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromServices] LogoutHandler handler, CancellationToken ct)
    {
        await handler.Handle(new LogoutCommand(Request.Cookies[RefreshCookieName]), ct);
        ClearRefreshCookie();
        return NoContent();
    }

    [HttpPost("logout-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll([FromServices] LogoutAllHandler handler, CancellationToken ct)
    {
        await handler.Handle(ct);
        ClearRefreshCookie();
        return NoContent();
    }

    [HttpPost("verify-email")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> VerifyEmail(TokenRequest request, [FromServices] VerifyEmailHandler handler, CancellationToken ct)
    {
        await handler.Handle(new VerifyEmailCommand(request.Token), ct);
        return NoContent();
    }

    [HttpPost("resend-verification")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ResendVerification(
        EmailRequest request, [FromServices] ResendVerificationHandler handler, CancellationToken ct)
    {
        await handler.Handle(new ResendVerificationCommand(request.Email), ct);
        return Accepted();
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(
        EmailRequest request, [FromServices] ForgotPasswordHandler handler, CancellationToken ct)
    {
        await handler.Handle(new ForgotPasswordCommand(request.Email), ct);
        return Accepted();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordCommand command, [FromServices] ResetPasswordHandler handler, CancellationToken ct)
    {
        await handler.Handle(command, ct);
        ClearRefreshCookie();
        return NoContent();
    }

    /// <summary>Who invited whom, for the accept page. POST so the token never lands in a URL or access log.</summary>
    [HttpPost("invitation")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<InvitationDto>(StatusCodes.Status200OK)]
    public Task<InvitationDto> Invitation(TokenRequest request, [FromServices] GetInvitationHandler handler, CancellationToken ct) =>
        handler.Handle(request.Token, ct);

    /// <summary>Sets the first password and signs the new user in (same response and cookie as login).</summary>
    [HttpPost("accept-invitation")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AccessTokenResponse>(StatusCodes.Status200OK)]
    public async Task<AccessTokenResponse> AcceptInvitation(
        AcceptInvitationCommand command, [FromServices] AcceptInvitationHandler handler, CancellationToken ct)
    {
        var result = await handler.Handle(command, ct);
        return IssueSession(result);
    }

    [HttpGet("me")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public Task<MeResponse> Me([FromServices] GetCurrentUserHandler handler, CancellationToken ct) => handler.Handle(ct);

    [HttpGet("sessions")]
    [ProducesResponseType<IReadOnlyList<SessionDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<SessionDto>> Sessions([FromServices] ListSessionsHandler handler, CancellationToken ct) =>
        handler.Handle(Request.Cookies[RefreshCookieName], ct);

    [HttpDelete("sessions/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeSession(Guid id, [FromServices] RevokeSessionHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, ct);
        return NoContent();
    }

    private AccessTokenResponse IssueSession(AuthResult result)
    {
        Response.Cookies.Append(RefreshCookieName, result.RefreshToken, CookieOptions(result.RefreshTokenExpiresAt));
        Response.Headers.CacheControl = "no-store";
        return new AccessTokenResponse(result.AccessToken, result.AccessTokenExpiresAt);
    }

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookieName, CookieOptions(expires: null));

    /// <summary>
    /// HttpOnly: JavaScript (and therefore XSS) can't read it.
    /// Secure: HTTPS only (browsers treat http://localhost as secure, so dev works).
    /// SameSite=Strict + scoped Path: only sent to /api/v1/auth/* from our own site, so no CSRF.
    /// </summary>
    private CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = RefreshCookiePath,
        Expires = expires,
        IsEssential = true,
        Domain = null,
        MaxAge = expires is null || environment.IsEnvironment("Testing") ? null : expires - DateTimeOffset.UtcNow,
    };
}
