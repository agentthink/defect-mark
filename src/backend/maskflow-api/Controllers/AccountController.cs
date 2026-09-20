using Microsoft.AspNetCore.Mvc;

[Tags("Account")]
public sealed class AccountController : MaskFlowControllerBase
{
    public AccountController(MaskFlowStore store) : base(store) { }

    [HttpGet("/api/me")]
    public IActionResult Me()
    {
        var user = CurrentUser();
        return Ok(new { user = Store.PublicUser(user) });
    }

    [HttpGet("/api/account/profile")]
    public IActionResult Profile()
    {
        var user = CurrentUser();
        return Ok(new { user = Store.PublicUser(user) });
    }

    [HttpPut("/api/account/profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] ProfileRequest request)
    {
        var user = CurrentUser();
        await Store.UpdateProfileAsync(user.Id, request);
        return Ok(new { user = Store.PublicUser(Store.GetUser(user.Id)!) });
    }

    [HttpPost("/api/account/password")]
    public async Task<IActionResult> ChangePassword([FromBody] PasswordChangeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            return BadRequest(new { detail = "New password must be at least 8 characters." });
        }

        var user = CurrentUser();
        var changed = await Store.ChangePasswordAsync(user.Id, request.CurrentPassword, request.NewPassword, MaskFlowStore.ExtractBearerToken(HttpContext));
        return changed ? Ok(new { ok = true }) : BadRequest(new { detail = "Current password is incorrect." });
    }

    [HttpGet("/api/account/notifications")]
    public IActionResult Notifications()
    {
        var user = CurrentUser();
        return Ok(new { settings = Store.State.NotificationSettings.GetValueOrDefault(user.Id.ToString(), NotificationSettings.Default()) });
    }

    [HttpPut("/api/account/notifications")]
    public async Task<IActionResult> UpdateNotifications([FromBody] NotificationSettings request)
    {
        var user = CurrentUser();
        var settings = await Store.UpdateNotificationSettingsAsync(user.Id, request);
        return Ok(new { settings });
    }

    [HttpGet("/api/account/api-tokens")]
    public IActionResult ApiTokens()
    {
        var user = CurrentUser();
        return Ok(new { tokens = Store.State.ApiTokens.Where(x => x.UserId == user.Id && x.RevokedAt is null) });
    }

    [HttpPost("/api/account/api-tokens")]
    public async Task<IActionResult> CreateApiToken([FromBody] ApiTokenCreate request)
    {
        var user = CurrentUser();
        var (token, plain) = await Store.CreateApiTokenAsync(user.Id, request.Name);
        return Ok(new { token = token with { TokenHash = "" }, value = plain });
    }

    [HttpDelete("/api/account/api-tokens/{tokenId}")]
    public async Task<IActionResult> RevokeApiToken(string tokenId)
    {
        var user = CurrentUser();
        var revoked = await Store.RevokeApiTokenAsync(user.Id, tokenId);
        return revoked ? Ok(new { ok = true }) : NotFound(new { detail = "Token not found." });
    }

    [HttpGet("/api/account/team")]
    public IActionResult Team()
    {
        var user = CurrentUser();
        return Ok(new { members = Store.State.TeamMembers.Where(x => x.UserId == user.Id) });
    }

    [HttpPost("/api/account/team")]
    public async Task<IActionResult> AddTeamMember([FromBody] TeamMemberCreate request)
    {
        var user = CurrentUser();
        var member = await Store.AddTeamMemberAsync(user.Id, request);
        return Ok(new { member });
    }

    [HttpDelete("/api/account/team/{memberId}")]
    public async Task<IActionResult> RemoveTeamMember(string memberId)
    {
        var user = CurrentUser();
        try
        {
            var removed = await Store.RemoveTeamMemberAsync(user.Id, memberId);
            return removed ? Ok(new { ok = true }) : NotFound(new { detail = "Team member not found." });
        }
        catch (BadHttpRequestException ex)
        {
            return BadRequest(new { detail = ex.Message });
        }
    }

    [HttpGet("/api/account/devices")]
    public IActionResult Devices()
    {
        var user = CurrentUser();
        return Ok(new { devices = Store.State.Devices.Where(x => x.UserId == user.Id) });
    }

    [HttpPost("/api/account/devices/{deviceId}/revoke")]
    public async Task<IActionResult> RevokeDevice(string deviceId)
    {
        var user = CurrentUser();
        var device = await Store.RevokeDeviceAsync(user.Id, deviceId);
        return device is null ? NotFound(new { detail = "Device not found." }) : Ok(new { device });
    }
}
