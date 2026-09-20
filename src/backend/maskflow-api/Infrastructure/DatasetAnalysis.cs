public sealed record DatasetExportPlan(
    List<FileItem> AllFiles, List<FileItem> Files, Dictionary<int, AnnotationSet> Annotations,
    List<string> Labels, List<DatasetSplitSample> Samples, string Format, string DataType)
{
    public static DatasetExportPlan Create(MaskFlowState state, int userId, ExportRequest request)
    {
        var project = state.Projects.FirstOrDefault(x => x.Id == request.ProjectId && x.UserId == userId);
        if (request.ProjectId is not null && project is null)
            throw new BadHttpRequestException("Project not found.", 404);
        var format = (request.Format ?? "yolo").Trim().ToLowerInvariant() switch
        {
            "yolo-detect" or "detect" or "detection" => "yolo-detect",
            "yolo-segment" or "segment" or "segmentation" => "yolo-segment",
            "classification" or "classification-crops" or "crops" => "classification-crops",
            _ => "yolo"
        };
        var dataType = format == "yolo-segment" ? "segmentation" : format == "yolo-detect" ? "detection"
            : MaskFlowStore.NormalizeDataType(project?.DataType);
        var files = state.Files.Where(x => x.UserId == userId && x.Kind == "image"
            && (request.ProjectId is null || x.ProjectId == request.ProjectId)).OrderBy(x => x.Id).ToList();
        var ids = files.Select(x => x.Id).ToHashSet();
        var sets = state.AnnotationSets.Where(x => x.UserId == userId && ids.Contains(x.FileId)).ToDictionary(x => x.FileId);
        var labels = request.ProjectId is not null
            ? state.ProjectLabels.GetValueOrDefault(request.ProjectId, []).ToList()
            : sets.Values.SelectMany(x => x.Annotations).Where(x => !string.IsNullOrWhiteSpace(x.Label))
                .Select(x => x.Label!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        sets = sets.ToDictionary(x => x.Key, x => x.Value with
        {
            Annotations = MaskFlowStore.NormalizeAnnotations(x.Value.Annotations, labels)
        });
        var exported = files.Where(x => sets.ContainsKey(x.Id)
            && (format != "classification-crops" || sets[x.Id].Annotations.Any(MaskFlowStore.IsExportableAnnotation))).ToList();
        var samples = exported.Select(file => new DatasetSplitSample(file.Id,
            sets[file.Id].Annotations.Where(MaskFlowStore.IsExportableAnnotation)
                .GroupBy(x => x.Label!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase))).ToList();
        return new(files, exported, sets, labels, samples, format, dataType);
    }

    public int MissingSegments => Files.Sum(file => Annotations[file.Id].Annotations.Count(x =>
        MaskFlowStore.IsExportableAnnotation(x) && !MaskFlowStore.HasValidSegment(x)));

    public void ValidateExport()
    {
        if (Files.Count == 0) throw new BadHttpRequestException("当前项目没有可导出的已标注图片。", 400);
        if (Labels.Count == 0) throw new BadHttpRequestException("请先添加项目标签后再导出。", 400);
        if (Format != "classification-crops" && DataType == "segmentation" && MissingSegments > 0)
            throw new BadHttpRequestException($"有 {MissingSegments} 个已分配标签的目标缺少有效分割轮廓，请补充分割或改用 YOLO 检测格式。", 400);
    }
}

public static class DatasetAnalysis
{
    public static object Analyze(DatasetExportPlan plan, SplitConfig split, int seed)
    {
        DatasetSplitter.Validate(split);
        var samples = plan.Samples;
        var counts = plan.Labels.ToDictionary(label => label,
            label => samples.Count(x => x.LabelCounts.ContainsKey(label)), StringComparer.OrdinalIgnoreCase);
        var totalObjects = samples.Sum(x => x.LabelCounts.Values.Sum());
        var min = counts.Count == 0 ? 0 : counts.Values.Min();
        // Transparent planning heuristics, not an estimate of model accuracy or an optimized ratio.
        var recommendation = min < 2 ? new SplitConfig(100, 0, 0)
            : min < 15 ? new SplitConfig(80, 20, 0)
            : min < 50 ? new SplitConfig(60, 20, 20)
            : min < 100 ? new SplitConfig(70, 20, 10) : new SplitConfig(80, 10, 10);
        var reason = min < 2 ? "至少一个类别不足 2 张原图，暂不建议留出评估集；先补样，再重新分析。仅训练划分不能用于评估泛化。"
            : min < 15 ? "最少类别不足 15 张原图，先保留训练与验证两组，暂不单独留测试集；有条件时进行分组交叉验证并补充独立测试样本。"
            : min < 50 ? "最少类别为 15–49 张原图，增加验证和测试份额，争取各留出至少 3 张原图；评估仍可能不稳定。"
            : min < 100 ? "最少类别为 50–99 张原图，按 70/20/10 兼顾训练与评估，目标为每类至少约 10 张验证、5 张测试原图。"
            : "每类至少有 100 张原图，按 80/10/10 增加训练份额，同时争取每类至少约 10 张验证及测试原图。";
        var assigned = DatasetSplitter.Assign(samples, split, seed);
        var recommended = DatasetSplitter.Assign(samples, recommendation, seed);
        var max = counts.Count == 0 ? 0 : counts.Values.Max();
        var target = Math.Max(30, (int)Math.Ceiling(max / 3.0));
        var rows = plan.Labels.Select(label =>
        {
            var matching = samples.Where(x => x.LabelCounts.ContainsKey(label)).ToArray();
            var objects = matching.Sum(x => x.LabelCounts[label]);
            return new
            {
                label, images = matching.Length, annotations = objects,
                objectPercent = totalObjects == 0 ? 0 : Math.Round(objects * 100.0 / totalObjects, 1),
                imagePercent = samples.Count == 0 ? 0 : Math.Round(matching.Length * 100.0 / samples.Count, 1),
                suggestedAdditionalImages = Math.Max(0, target - matching.Length),
                targetImages = target,
                splits = Distribution(matching, assigned, label), recommendedSplits = Distribution(matching, recommended, label)
            };
        }).ToArray();
        var warnings = new List<string>();
        if (samples.Count == 0) warnings.Add("没有可导出的已标注图片，请先上传图片并保存标注。");
        if (plan.Labels.Count == 0) warnings.Add("项目尚无类别，请先添加标签。");
        if (min == 0 && plan.Labels.Count > 0) warnings.Add("存在没有样本的类别，模型无法从当前数据学习这些类别。");
        if (min > 0 && max / (double)min > 3) warnings.Add("类别原图数超过 3:1，建议优先补充少数类别；自然业务分布不均时，不必强行补成相同数量。");
        var unannotated = plan.AllFiles.Count(x => !plan.Annotations.ContainsKey(x.Id));
        if (unannotated > 0) warnings.Add($"有 {unannotated} 张图片尚未保存标注，不会参与本次导出。");
        var unassigned = plan.Annotations.Values.Sum(x => x.Annotations.Count(a => !MaskFlowStore.IsExportableAnnotation(a)));
        if (unassigned > 0) warnings.Add($"有 {unassigned} 个目标未分配有效类别，不会作为训练标签导出。");
        var unconfirmed = plan.Annotations.Values.Sum(x => x.Annotations.Count(a => MaskFlowStore.IsExportableAnnotation(a) && !a.Confirmed));
        if (unconfirmed > 0) warnings.Add($"有 {unconfirmed} 个已分配类别的目标尚未人工确认，导出前建议复核。");
        var blockedSegments = plan.Format != "classification-crops" && plan.DataType == "segmentation" && plan.MissingSegments > 0;
        if (blockedSegments) warnings.Add($"有 {plan.MissingSegments} 个目标缺少有效轮廓，分割导出暂不可用；请补充分割或改用检测格式。");
        int[] ratios = [split.Train, split.Val, split.Test];
        foreach (var row in rows)
            foreach (var i in Enumerable.Range(0, 3).Where(i => ratios[i] > 0 && row.splits[DatasetSplitter.Names[i]].Images == 0))
                warnings.Add($"当前划分的 {DatasetSplitter.Names[i]} 集合缺少「{row.label}」原图，请补样或调整比例。");
        return new
        {
            totalImages = plan.AllFiles.Count, exportImages = samples.Count, totalAnnotations = totalObjects,
            unannotatedImages = unannotated, unassignedAnnotations = unassigned, unconfirmedAnnotations = unconfirmed,
            missingSegments = plan.MissingSegments, canExport = samples.Count > 0 && plan.Labels.Count > 0 && !blockedSegments,
            seed, requestedSplit = split, recommendedSplit = recommendation, recommendationReason = reason,
            recommendationAvailable = samples.Count > 0 && plan.Labels.Count > 0,
            supplementBasis = $"补样参考目标为每类至少 {target} 张原图：取 30 与最多类别原图数的三分之一中的较大值。这是可调整的采集起点，不是训练达标线。优先增加不同批次、光照、角度和困难场景的真实图片，重复裁剪不能替代独立样本。",
            limitation = "建议只基于已保存标签统计，未经模型训练验证，不代表最优比例。同一原图不会跨集合，但不同文件中的同一对象、连续帧或近重复图片仍需按拍摄批次人工隔离。多标签共现时实际比例可能偏离目标；原图占比之和可超过 100%。",
            classes = rows, warnings,
            preview = DatasetSplitter.Report(samples, plan.Labels, assigned, split, seed),
            recommendedPreview = DatasetSplitter.Report(samples, plan.Labels, recommended, recommendation, seed)
        };
    }

    sealed record SplitCount(int Images, int Annotations);
    static Dictionary<string, SplitCount> Distribution(DatasetSplitSample[] samples, Dictionary<int, string> assigned, string label) =>
        DatasetSplitter.Names.ToDictionary(name => name, name => new SplitCount(
            samples.Count(x => assigned[x.FileId] == name),
            samples.Where(x => assigned[x.FileId] == name).Sum(x => x.LabelCounts[label])));
}
