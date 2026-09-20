using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/billing")]
[Tags("Billing")]
public sealed class BillingController : MaskFlowControllerBase
{
    public BillingController(MaskFlowStore store) : base(store) { }

    [AllowAnonymous]
    [HttpGet("plans")]
    public IActionResult Plans() => Ok(new
    {
        plans = new[]
        {
            new { id = "free", name = "Free", price = 0, quotaBytes = 10L * 1024 * 1024 * 1024, dailyLimit = 50 },
            new { id = "pro", name = "Pro", price = 49, quotaBytes = 50L * 1024 * 1024 * 1024, dailyLimit = 1000 },
            new { id = "team", name = "Team", price = 299, quotaBytes = 500L * 1024 * 1024 * 1024, dailyLimit = 100000 }
        }
    });

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest request)
    {
        var user = CurrentUser();
        var plan = (request.Plan ?? "").Trim().ToLowerInvariant();
        if (plan is not ("free" or "pro" or "team"))
        {
            return BadRequest(new { detail = "Invalid plan." });
        }

        if (plan != "free")
        {
            var devBypass = string.Equals(
                Environment.GetEnvironmentVariable("MASKFLOW_BILLING_DEV_MODE"),
                "true",
                StringComparison.OrdinalIgnoreCase);
            if (!devBypass)
            {
                return StatusCode(402, new { detail = "Paid plans are not available yet. Payment integration pending." });
            }
        }

        if (plan == "team" && !string.Equals(user.Plan, "team", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(403, new { detail = "Team plan requires enterprise approval." });
        }

        await Store.UpdatePlanAsync(user.Id, plan);
        return Ok(new { user = Store.PublicUser(Store.GetUser(user.Id)!) });
    }
}