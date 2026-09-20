using Microsoft.AspNetCore.Mvc;

[Route("v1/settlements")]
[Tags("Settlements")]
public sealed class SettlementsController : ControllerBase
{
    private readonly MaskFlowStore store;

    public SettlementsController(MaskFlowStore store)
    {
        this.store = store;
    }

    [HttpGet]
    public IActionResult List() => Ok(new { settlements = store.State.Settlements });

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SettlementCreate request)
    {
        var settlement = await store.AddSettlementAsync(request);
        return Ok(new { settlement });
    }
}