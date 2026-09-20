using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

[Route("v1/nodes")]
[Tags("Compute Nodes")]
public sealed class NodesController : ControllerBase
{
    private readonly MaskFlowStore store;

    public NodesController(MaskFlowStore store)
    {
        this.store = store;
    }

    [HttpGet]
    public IActionResult List() => Ok(new { nodes = store.State.Nodes.Select(x => x.Public()).ToList() });

    [HttpGet("{nodeId}")]
    public IActionResult Detail(string nodeId)
    {
        var node = store.State.Nodes.FirstOrDefault(x => x.Id == nodeId);
        return node is null ? NotFound(new { detail = "Node not found" }) : Ok(new { node = node.Public() });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] NodeRegister request)
    {
        var apiKey = "mf_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var node = await store.RegisterNodeAsync(request, apiKey);
        return Ok(new { node = node.Public(apiKey) });
    }

    [HttpPost("{nodeId}/heartbeat")]
    public async Task<IActionResult> Heartbeat(string nodeId, [FromBody] NodeHeartbeat request)
    {
        var node = await store.HeartbeatNodeAsync(nodeId, request);
        return node is null ? NotFound(new { detail = "Node not found" }) : Ok(new { node = node.Public() });
    }

    [HttpPost("{nodeId}/status")]
    public async Task<IActionResult> SetStatus(string nodeId, [FromBody] JsonElement body)
    {
        var status = body.TryGetProperty("status", out var s) ? s.GetString() ?? "offline" : "offline";
        var approve = body.TryGetProperty("approve", out var a) && a.GetBoolean();
        var node = await store.NodeStatusAsync(nodeId, status, approve);
        return node is null ? NotFound(new { detail = "Node not found" }) : Ok(new { node = node.Public() });
    }

    [HttpPost("{nodeId}/jobs/poll")]
    public async Task<IActionResult> PollJob(string nodeId)
    {
        var (nodeFound, job) = await store.PollJobAsync(nodeId);
        if (!nodeFound)
        {
            return NotFound(new { detail = "Node not found" });
        }

        return Ok(new { job });
    }
}
