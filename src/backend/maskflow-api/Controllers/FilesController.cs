using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

[Route("api/files")]
[Tags("Files")]
public sealed class FilesController : MaskFlowControllerBase
{
    public FilesController(MaskFlowStore store) : base(store) { }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload()
    {
        var user = CurrentUser();
        var form = await Request.ReadFormAsync();
        var projectId = form["projectId"].FirstOrDefault();
        var files = new List<FileItem>();
        foreach (var upload in form.Files)
        {
            files.Add(await Store.SaveUploadAsync(user, upload, projectId));
        }

        return Ok(new { files, user = Store.PublicUser(Store.GetUser(user.Id)!) });
    }

    [HttpGet]
    public IActionResult List([FromQuery] string? projectId)
    {
        var user = CurrentUser();
        var files = Store.State.Files
            .Where(x => x.UserId == user.Id && (string.IsNullOrWhiteSpace(projectId) || x.ProjectId == projectId))
            .OrderByDescending(x => x.CreatedAt)
            .Select(file =>
            {
                var set = Store.GetAnnotationSet(user.Id, file.Id);
                return new
                {
                    file.Id,
                    file.UserId,
                    file.ProjectId,
                    file.Name,
                    file.Size,
                    file.Kind,
                    file.ContentType,
                    file.CreatedAt,
                    file.DownloadUrl,
                    Annotated = set is not null,
                    AnnotationCount = set?.Annotations.Count ?? 0
                };
            });
        return Ok(new { files, user = Store.PublicUser(Store.GetUser(user.Id)!) });
    }

    [HttpGet("{fileId:int}/download")]
    public async Task<IActionResult> Download(int fileId)
    {
        var user = CurrentUser();
        var file = Store.State.Files.FirstOrDefault(x => x.Id == fileId && x.UserId == user.Id);
        if (file is null || !await Store.StoredObjectExistsAsync(file.Path)) return NotFound(new { detail = "File not found." });
        var provider = new FileExtensionContentTypeProvider();
        provider.TryGetContentType(file.Name, out var contentType);
        return File(await Store.OpenStoredObjectAsync(file.Path), contentType ?? "application/octet-stream", file.Name);
    }

    [HttpDelete("{fileId:int}")]
    public async Task<IActionResult> Delete(int fileId)
    {
        var user = CurrentUser();
        var deleted = await Store.DeleteFileAsync(user.Id, fileId);
        return deleted ? Ok(new { ok = true, user = Store.PublicUser(Store.GetUser(user.Id)!) }) : NotFound(new { detail = "File not found." });
    }
}