using Microsoft.AspNetCore.Mvc;

[Route("v1/pools")]
[Tags("Compute Pools")]
public sealed class PoolsController : ControllerBase
{
    private readonly MaskFlowStore store;

    public PoolsController(MaskFlowStore store)
    {
        this.store = store;
    }

    [HttpGet]
    public IActionResult List() => Ok(new { pools = store.State.Pools });

    [HttpGet("{poolId}")]
    public IActionResult Detail(string poolId)
    {
        var pool = store.State.Pools.FirstOrDefault(x => x.Id == poolId);
        return pool is null ? NotFound(new { detail = "Pool not found" }) : Ok(new { pool });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PoolCreate request)
    {
        var pool = await store.AddPoolAsync(request);
        return Ok(new { pool });
    }
}