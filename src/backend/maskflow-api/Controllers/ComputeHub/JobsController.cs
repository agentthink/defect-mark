using System.Text.Json;
using MaskFlow.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

[Route("v1/jobs")]
[Tags("Compute Jobs")]
public sealed class JobsController : ControllerBase
{
    private readonly MaskFlowStore store;

    public JobsController(MaskFlowStore store)
    {
        this.store = store;
    }

    bool IsAdmin => HttpContext.Items.ContainsKey(MaskFlowHttpItems.AdminAccess);
    string? NodeId => HttpContext.Items[MaskFlowHttpItems.NodeId] as string;

    User RequireScopedUser()
    {
        if (HttpContext.Items[MaskFlowHttpItems.AuthenticatedUser] is User user)
        {
            return user;
        }

        return store.RequireUser(HttpContext);
    }

    bool NodeCanWriteJob(string jobId)
    {
        var nodeId = NodeId;
        return nodeId is not null && store.State.Jobs.Any(x => x.Id == jobId && x.NodeId == nodeId);
    }

    [HttpGet]
    public IActionResult List()
    {
        if (IsAdmin)
        {
            return Ok(new { jobs = store.State.Jobs });
        }

        var user = RequireScopedUser();
        return Ok(new { jobs = store.State.Jobs.Where(x => x.UserId == user.Id).ToList() });
    }

    [HttpGet("{jobId}")]
    public IActionResult Detail(string jobId)
    {
        var job = store.State.Jobs.FirstOrDefault(x => x.Id == jobId);
        if (job is null)
        {
            return NotFound(new { detail = "Job not found" });
        }

        if (!IsAdmin)
        {
            var user = RequireScopedUser();
            if (job.UserId != user.Id)
            {
                return NotFound(new { detail = "Job not found" });
            }
        }

        return Ok(new { job });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] JobCreate request)
    {
        if (!IsAdmin)
        {
            var user = RequireScopedUser();
            var job = await store.AddJobAsync(request with { UserId = user.Id });
            return Ok(new { job });
        }

        if (request.UserId <= 0)
        {
            return BadRequest(new { detail = "userId is required for admin job creation." });
        }

        var adminJob = await store.AddJobAsync(request);
        return Ok(new { job = adminJob });
    }

    [HttpPost("{jobId}/status")]
    public async Task<IActionResult> SetStatus(string jobId, [FromBody] JsonElement body)
    {
        if (!IsAdmin && !NodeCanWriteJob(jobId))
        {
            return NotFound(new { detail = "Job not found" });
        }

        var status = body.TryGetProperty("status", out var s) ? s.GetString() ?? "queued" : "queued";
        var job = await store.SetJobStatusAsync(jobId, status);
        return job is null ? NotFound(new { detail = "Job not found" }) : Ok(new { job });
    }

    [HttpPost("{jobId}/events")]
    public async Task<IActionResult> AddEvent(string jobId, [FromBody] JobEventCreate request)
    {
        if (!IsAdmin && !NodeCanWriteJob(jobId))
        {
            return NotFound(new { detail = "Job not found" });
        }

        var result = await store.AddJobEventAsync(jobId, request);
        return result is null ? NotFound(new { detail = "Job not found" }) : Ok(result);
    }
}
