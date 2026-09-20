using Microsoft.AspNetCore.Mvc;

[Route("api/tasks")]
[Tags("Tasks")]
public sealed class TasksController : MaskFlowControllerBase
{
    public TasksController(MaskFlowStore store) : base(store) { }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TaskCreate request)
    {
        var user = CurrentUser();
        var task = await Store.CreateTaskAsync(user.Id, request.Type, request.Title, request.ProjectId, request.FileId, request.ImageCount);
        return Ok(new { task });
    }

    [HttpGet]
    public IActionResult List()
    {
        var user = CurrentUser();
        return Ok(new { tasks = Store.State.Tasks.Where(x => x.UserId == user.Id).OrderByDescending(x => x.CreatedAt) });
    }

    [HttpGet("{taskId}")]
    public IActionResult Detail(string taskId)
    {
        var user = CurrentUser();
        var task = Store.State.Tasks.FirstOrDefault(x => x.Id == taskId && x.UserId == user.Id);
        return task is null ? NotFound(new { detail = "Task not found." }) : Ok(new { task });
    }

    [HttpPost("{taskId}/cancel")]
    public async Task<IActionResult> Cancel(string taskId)
    {
        var user = CurrentUser();
        var task = await Store.UpdateTaskAsync(user.Id, taskId, "cancelled", 0.0, null);
        return task is null ? NotFound(new { detail = "Task not found." }) : Ok(new { task });
    }

    [HttpPost("{taskId}/retry")]
    public async Task<IActionResult> Retry(string taskId)
    {
        var user = CurrentUser();
        var task = await Store.UpdateTaskAsync(user.Id, taskId, "queued", 0.0, null);
        return task is null ? NotFound(new { detail = "Task not found." }) : Ok(new { task });
    }
}