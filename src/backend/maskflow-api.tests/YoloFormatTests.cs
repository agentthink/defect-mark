namespace MaskFlow.Api.Tests;

public class YoloFormatTests
{
    static AnnotationItem Sample(string dataTypeLabel = "person") =>
        new(
            "ann_1",
            0,
            dataTypeLabel,
            new YoloBox(0.5, 0.5, 0.2, 0.4),
            [0.1, 0.1, 0.3, 0.1, 0.3, 0.3, 0.1, 0.3],
            1.0,
            true);

    [Fact]
    public void BuildYoloLine_Detection_UsesBoundingBox()
    {
        var line = MaskFlowStore.BuildYoloLine(Sample(), "detection");
        Assert.Equal("0 0.5 0.5 0.2 0.4", line);
    }

    [Fact]
    public void BuildYoloLine_Segmentation_UsesPolygonWhenAvailable()
    {
        var line = MaskFlowStore.BuildYoloLine(Sample(), "segmentation");
        Assert.StartsWith("0 0.1 0.1 0.3 0.1", line);
    }

    [Fact]
    public void BuildYoloLine_Segmentation_RejectsBoundingBoxWithoutPolygon()
    {
        var item = Sample() with { Segment = null };
        Assert.Throws<Microsoft.AspNetCore.Http.BadHttpRequestException>(() => MaskFlowStore.BuildYoloLine(item, "segmentation"));
        Assert.Empty(MaskFlowStore.BuildYoloTxt([item], "segmentation"));
    }

    [Fact]
    public void ResolveYoloTask_MapsProjectDataType()
    {
        Assert.Equal("detect", MaskFlowStore.ResolveYoloTask("detection"));
        Assert.Equal("segment", MaskFlowStore.ResolveYoloTask("segmentation"));
    }
}
