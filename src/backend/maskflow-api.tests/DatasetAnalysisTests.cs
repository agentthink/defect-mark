using System.Text.Json;
using Microsoft.AspNetCore.Http;

public sealed class DatasetAnalysisTests
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    static MaskFlowState State(int count, bool masks = true)
    {
        var state = new MaskFlowState();
        var now = DateTimeOffset.UtcNow;
        state.Projects.Add(new("p", 1, "Test", "", "segmentation", new(70, 20, 10), 0, 0, now, now));
        state.ProjectLabels["p"] = ["a", "b"];
        for (var id = 1; id <= count; id++)
        {
            state.Files.Add(new(id, 1, "p", $"{id}.jpg", "unused", 1, "image", "image/jpeg", now, ""));
            state.AnnotationSets.Add(new($"s{id}", 1, id, 1920, 1080,
            [
                new("a", 99, "a", new(.5, .5, .1, .1), masks ? [.1, .1, .5, .1, .5, .5] : null, 1, false),
                new("b1", 99, "b", new(.5, .5, .1, .1), masks ? [.1, .1, .5, .1, .5, .5] : null, 1, true),
                new("b2", 99, "b", new(.5, .5, .1, .1), masks ? [.1, .1, .5, .1, .5, .5] : null, 1, true)
            ], "", now, now));
        }
        return state;
    }

    [Theory]
    [InlineData(1, 100, 0, 0)]
    [InlineData(10, 80, 20, 0)]
    [InlineData(30, 60, 20, 20)]
    [InlineData(70, 70, 20, 10)]
    [InlineData(100, 80, 10, 10)]
    public void Recommendation_UsesIndependentImagesNotObjectCount(int images, int train, int val, int test)
    {
        var plan = DatasetExportPlan.Create(State(images), 1, new("p", null, null, "yolo-detect"));
        var report = JsonSerializer.SerializeToElement(DatasetAnalysis.Analyze(plan, new(70, 20, 10), 42), JsonOptions);
        Assert.Equal(new SplitConfig(train, val, test), report.GetProperty("recommendedSplit").Deserialize<SplitConfig>(JsonOptions));
        var rows = report.GetProperty("classes").EnumerateArray().ToArray();
        Assert.Equal(33.3, rows[0].GetProperty("objectPercent").GetDouble());
        Assert.Equal(100, rows[0].GetProperty("imagePercent").GetDouble());
        foreach (var name in DatasetSplitter.Names)
            Assert.Equal(rows[0].GetProperty("splits").GetProperty(name).GetProperty("annotations").GetInt32() * 2,
                rows[1].GetProperty("splits").GetProperty(name).GetProperty("annotations").GetInt32());
    }

    [Fact]
    public void AnalysisAndExport_ShareCanonicalClassesAndDoNotMutateStoredAnnotations()
    {
        var state = State(10);
        state.ProjectLabels["p"] = ["b", "a"];
        var plan = DatasetExportPlan.Create(state, 1, new("p", null, null, "yolo-detect"));
        Assert.Equal(1, plan.Annotations[1].Annotations[0].ClassId);
        Assert.Equal(99, state.AnnotationSets[0].Annotations[0].ClassId);
        Assert.False(plan.Annotations[1].Annotations[0].Confirmed);
        plan.ValidateExport();
    }

    [Fact]
    public void MissingSegments_BlockOnlySegmentationExport()
    {
        var state = State(10, masks: false);
        var plan = DatasetExportPlan.Create(state, 1, new("p", null, null, "yolo-segment"));
        Assert.Equal(30, plan.MissingSegments);
        Assert.Throws<BadHttpRequestException>(plan.ValidateExport);
        var report = JsonSerializer.SerializeToElement(DatasetAnalysis.Analyze(plan, new(70, 20, 10), 42), JsonOptions);
        Assert.False(report.GetProperty("canExport").GetBoolean());
        foreach (var format in new[] { "yolo-detect", "classification-crops" })
            DatasetExportPlan.Create(state, 1, new("p", null, null, format)).ValidateExport();
    }

    [Fact]
    public void EmptyOrMissingClasses_ProduceActionableSupplementCounts()
    {
        var state = State(0);
        var plan = DatasetExportPlan.Create(state, 1, new("p", null, null, "yolo-detect"));
        var report = JsonSerializer.SerializeToElement(DatasetAnalysis.Analyze(plan, new(70, 20, 10), 42), JsonOptions);
        Assert.False(report.GetProperty("canExport").GetBoolean());
        Assert.False(report.GetProperty("recommendationAvailable").GetBoolean());
        Assert.All(report.GetProperty("classes").EnumerateArray(), x => Assert.Equal(30, x.GetProperty("suggestedAdditionalImages").GetInt32()));
    }

    [Fact]
    public void Analysis_IsOwnerScoped()
    {
        var state = State(10);
        Assert.Throws<BadHttpRequestException>(() => DatasetExportPlan.Create(state, 2, new("p", null, null)));
        Assert.Empty(DatasetExportPlan.Create(state, 2, new(null, null, null)).Files);
    }

    [Fact]
    public void InvalidPolygons_AreNotAcceptedAsSegments()
    {
        var item = State(1).AnnotationSets[0].Annotations[0];
        Assert.False(MaskFlowStore.HasValidSegment(item with { Segment = [.1, .1, .2, .2, .3, .3] }));
        Assert.False(MaskFlowStore.HasValidSegment(item with { Segment = [.1, .1, .2, .2, .3, .3, .4] }));
        Assert.False(MaskFlowStore.HasValidSegment(item with { Segment = [.1, .1, double.NaN, .2, .3, .3] }));
    }
}
