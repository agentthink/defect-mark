public interface IMaskFlowRepository
{
    Task EnsureSchemaAsync();
    Task<MaskFlowState?> LoadAsync();
    Task SaveAsync(MaskFlowState state, IReadOnlyCollection<string>? syncProjectLabelIds = null);
}
