using Microsoft.AspNetCore.Mvc;

[Tags("Pricing")]
public sealed class PricingController : ControllerBase
{
    private readonly MaskFlowStore store;

    public PricingController(MaskFlowStore store)
    {
        this.store = store;
    }

    [HttpGet("/v1/pricing")]
    public IActionResult List() => Ok(new { rules = store.State.PricingRules });

    [HttpPost("/v1/pricing")]
    public async Task<IActionResult> Create([FromBody] PricingCreate request)
    {
        var rule = await store.AddPricingRuleAsync(request);
        return Ok(new { rule });
    }
}