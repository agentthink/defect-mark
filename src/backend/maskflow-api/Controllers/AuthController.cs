using MaskFlow.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/auth")]
[Tags("Auth")]
public sealed class AuthController : ControllerBase
{
    private readonly MaskFlowStore store;
    private readonly LoginRateLimiter loginRateLimiter;

    public AuthController(MaskFlowStore store, LoginRateLimiter loginRateLimiter)
    {
        this.store = store;
        this.loginRateLimiter = loginRateLimiter;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { detail = "Email and password are required." });
        }

        if (request.Password.Length < 8)
        {
            return BadRequest(new { detail = "Password must be at least 8 characters." });
        }

        var result = await store.RegisterAsync(request.Email, request.Password, request.Username, HttpContext);
        return result is null ? Conflict(new { detail = "Email already exists." }) : Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var rateKey = $"{HttpContext.Connection.RemoteIpAddress}:{request.Email.Trim().ToLowerInvariant()}";
        if (loginRateLimiter.IsLocked(rateKey, out var retryAfterSeconds))
        {
            Response.Headers.RetryAfter = retryAfterSeconds.ToString();
            return StatusCode(429, new { detail = $"Too many login attempts. Retry after {retryAfterSeconds} seconds." });
        }

        var result = await store.LoginAsync(request.Email, request.Password, HttpContext);
        if (result is null)
        {
            loginRateLimiter.RecordFailure(rateKey);
            return Unauthorized(new { detail = "Invalid email or password." });
        }

        loginRateLimiter.RecordSuccess(rateKey);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public IActionResult ForgotPassword([FromBody] ForgotPasswordRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { detail = "Email is required." });
        }

        var rateKey = $"forgot:{HttpContext.Connection.RemoteIpAddress}:{request.Email.Trim().ToLowerInvariant()}";
        if (loginRateLimiter.IsLocked(rateKey, out var retryAfterSeconds))
        {
            Response.Headers.RetryAfter = retryAfterSeconds.ToString();
            return StatusCode(429, new { detail = $"Too many reset attempts. Retry after {retryAfterSeconds} seconds." });
        }

        loginRateLimiter.RecordFailure(rateKey);
        var token = store.CreatePasswordResetToken(request.Email);

        // Always acknowledge the request to avoid email enumeration.
        if (token is not null && store.PasswordResetReturnsToken)
        {
            return Ok(new
            {
                ok = true,
                message = "已生成重置码（30 分钟内有效）。请设置新密码完成找回。",
                resetToken = token,
                delivery = "inline"
            });
        }

        return Ok(new
        {
            ok = true,
            message = "如果该邮箱已注册，请使用重置码完成密码重置。当前环境未开启重置码回显时，请联系管理员或配置 MASKFLOW_PASSWORD_RESET_INLINE=true。"
        });
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest? request)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Token)
            || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { detail = "Email, token and newPassword are required." });
        }

        if (request.NewPassword.Length < 8)
        {
            return BadRequest(new { detail = "Password must be at least 8 characters." });
        }

        var rateKey = $"reset:{HttpContext.Connection.RemoteIpAddress}:{request.Email.Trim().ToLowerInvariant()}";
        if (loginRateLimiter.IsLocked(rateKey, out var retryAfterSeconds))
        {
            Response.Headers.RetryAfter = retryAfterSeconds.ToString();
            return StatusCode(429, new { detail = $"Too many reset attempts. Retry after {retryAfterSeconds} seconds." });
        }

        var changed = await store.ResetPasswordWithTokenAsync(request.Email, request.Token, request.NewPassword);
        if (!changed)
        {
            loginRateLimiter.RecordFailure(rateKey);
            return BadRequest(new { detail = "重置码无效或已过期，请重新获取。" });
        }

        loginRateLimiter.RecordSuccess(rateKey);
        return Ok(new { ok = true, message = "密码已重置，请使用新密码登录。" });
    }
}
