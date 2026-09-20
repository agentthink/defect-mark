public sealed class ProjectService
{
    readonly MaskFlowStore store;

    public ProjectService(MaskFlowStore store)
    {
        this.store = store;
    }

    public async Task<ProjectSummaryDto> CreateAsync(int userId, ProjectCreate request)
    {
        var project = await store.CreateProjectAsync(userId, request);
        return ToDto(userId, project);
    }

    public IReadOnlyList<ProjectSummaryDto> List(int userId) =>
        store.State.Projects
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => ToDto(userId, x))
            .ToList();

    public ProjectSummaryDto? Detail(int userId, string projectId)
    {
        var project = store.State.Projects.FirstOrDefault(x => x.Id == projectId && x.UserId == userId);
        return project is null ? null : ToDto(userId, project);
    }

    public async Task<ProjectSummaryDto?> UpdateAsync(int userId, string projectId, ProjectCreate request)
    {
        var updated = await store.UpdateProjectAsync(userId, projectId, request);
        return updated is null ? null : ToDto(userId, updated);
    }

    public async Task<ProjectSummaryDto?> CopyAsync(int userId, string projectId, ProjectCopyRequest request)
    {
        var copied = await store.CopyProjectAsync(userId, projectId, request);
        return copied is null ? null : ToDto(userId, copied);
    }

    public Task<bool> DeleteAsync(int userId, string projectId) => store.DeleteProjectAsync(userId, projectId);

    public List<string> GetLabels(int userId, string projectId) => store.GetProjectLabels(userId, projectId);

    public Task<List<string>> SaveLabelsAsync(int userId, string projectId, IEnumerable<string>? labels) =>
        store.SaveProjectLabelsAsync(userId, projectId, labels);

    public Task<List<string>> DeleteLabelAsync(int userId, string projectId, string labelName, string? replaceWith) =>
        store.DeleteProjectLabelAsync(userId, projectId, labelName, replaceWith);

    ProjectSummaryDto ToDto(int userId, Project project)
    {
        var projectFiles = store.State.Files.Where(x => x.UserId == userId && x.ProjectId == project.Id).ToList();
        var fileIds = projectFiles.Select(x => x.Id).ToHashSet();
        var annotationCount = store.State.AnnotationSets.Where(x => x.UserId == userId && fileIds.Contains(x.FileId)).Sum(x => x.Annotations.Count);
        return new ProjectSummaryDto(
            project.Id,
            project.UserId,
            project.Name,
            project.Description,
            project.DataType,
            project.Split,
            projectFiles.Count,
            annotationCount,
            store.GetProjectLabels(userId, project.Id),
            project.CreatedAt,
            project.UpdatedAt);
    }
}
