using Microsoft.AspNetCore.Mvc;

[Tags("AI Quota")]
public sealed class AiQuotaController : MaskFlowControllerBase
{
    public AiQuotaController(MaskFlowStore store) : base(store) { }

    [HttpGet("/api/ai/quota")]
    public async Task<IActionResult> Quota()
    {
        var user = CurrentUser();
        return Ok(new { quota = await Store.GetQuotaAsync(user) });
    }
}