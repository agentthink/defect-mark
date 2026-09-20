public sealed record ProjectSummaryDto(
    string Id,
    int UserId,
    string Name,
    string Description,
    string DataType,
    SplitConfig Split,
    int ImageCount,
    int AnnotationCount,
    List<string> Labels,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
