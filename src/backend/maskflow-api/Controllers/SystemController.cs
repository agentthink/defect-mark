using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[AllowAnonymous]
[Tags("System")]
public sealed class SystemController : ControllerBase
{
    [HttpGet("/")]
    public IActionResult Root() => Ok(new { name = "MaskFlow API", docs = "/swagger" });

    [HttpGet("/api/status")]
    public IActionResult Status() => Ok(new { ok = true, service = "maskflow-api" });
}