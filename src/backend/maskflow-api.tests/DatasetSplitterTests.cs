using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

public sealed class DatasetSplitterTests
{
    static DatasetSplitSample Sample(int id, params (string Label, int Count)[] labels) =>
        new(id, labels.ToDictionary(x => x.Label, x => x.Count, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void OrderedImbalancedClasses_AreStratifiedAndShuffled()
    {
        var samples = Enumerable.Range(0, 100).Select(i => Sample(i, ("common", 1)))
            .Concat(Enumerable.Range(100, 10).Select(i => Sample(i, ("rare", 2)))).ToArray();
        var assigned = DatasetSplitter.Assign(samples, new(70, 20, 10), 42);
        Assert.Equal(110, assigned.Count);
        foreach (var (label, expected) in new[] { ("common", new[] { 70, 20, 10 }), ("rare", new[] { 7, 2, 1 }) })
        {
            var matching = samples.Where(x => x.LabelCounts.ContainsKey(label)).ToArray();
            Assert.Equal(expected, DatasetSplitter.Names.Select(s => matching.Count(x => assigned[x.FileId] == s)).ToArray());
        }
        Assert.Contains(Enumerable.Range(0, 70), id => assigned[id] != "train");
    }

    [Fact]
    public void Seed_ReproducesAssignmentsRegardlessOfInputOrder()
    {
        var samples = Enumerable.Range(0, 100).Select(i => Sample(i, ("a", 1), ("b", 2))).ToArray();
        var a = DatasetSplitter.Assign(samples, new(70, 20, 10), 42);
        var b = DatasetSplitter.Assign(samples.Reverse().ToArray(), new(70, 20, 10), 42);
        Assert.All(a, pair => Assert.Equal(pair.Value, b[pair.Key]));
        var c = DatasetSplitter.Assign(samples, new(70, 20, 10), 43);
        Assert.Contains(a, pair => pair.Value != c[pair.Key]);
    }

    [Theory]
    [InlineData(1, 1, 0, 0)]
    [InlineData(2, 1, 1, 0)]
    [InlineData(3, 1, 1, 1)]
    public void ScarceClasses_PrioritizeTrainingAndReportMissingCoverage(int count, int train, int val, int test)
    {
        var samples = Enumerable.Range(0, count).Select(i => Sample(i, ("rare", 5))).ToArray();
        var assigned = DatasetSplitter.Assign(samples, new(70, 20, 10), 42);
        Assert.Equal(new[] { train, val, test }, DatasetSplitter.Names.Select(s => assigned.Values.Count(x => x == s)).ToArray());
        var report = JsonSerializer.SerializeToElement(DatasetSplitter.Report(samples, ["rare"], assigned, new(70, 20, 10), 42));
        Assert.Equal(3 - count, report.GetProperty("warnings").GetArrayLength());
    }

    [Theory]
    [InlineData(100, 0, 0, "train")]
    [InlineData(0, 100, 0, "val")]
    [InlineData(0, 0, 100, "test")]
    public void DisabledSplits_RemainEmptyIncludingBackgroundImages(int train, int val, int test, string expected)
    {
        DatasetSplitSample[] samples = [Sample(1, ("a", 1)), Sample(2), Sample(3, ("b", 10))];
        var assigned = DatasetSplitter.Assign(samples, new(train, val, test), 42);
        Assert.All(assigned.Values, value => Assert.Equal(expected, value));
    }

    [Theory]
    [InlineData(-10, 100, 10)]
    [InlineData(101, 0, -1)]
    [InlineData(70, 20, 20)]
    public void InvalidRatios_AreRejected(int train, int val, int test) =>
        Assert.Throws<BadHttpRequestException>(() => DatasetSplitter.Assign([], new(train, val, test), 42));

    [Fact]
    public void EmptyDataset_ProducesEmptyAssignment() =>
        Assert.Empty(DatasetSplitter.Assign([], new(70, 20, 10), 42));

    [Fact]
    public void DenseImage_IsKeptWholeAndAllocatedBeforeSmallImages()
    {
        var samples = new[] { Sample(1, ("a", 70)) }
            .Concat(Enumerable.Range(2, 9).Select(i => Sample(i, ("a", 1)))).ToArray();
        var assigned = DatasetSplitter.Assign(samples, new(70, 20, 10), 42);
        Assert.Equal("train", assigned[1]);
        Assert.Equal(new[] { 7, 2, 1 }, DatasetSplitter.Names.Select(s => assigned.Values.Count(x => x == s)).ToArray());
    }

    [Fact]
    public void DenseAndSparseImages_BalanceBothInstancesAndImageCounts()
    {
        var samples = Enumerable.Range(0, 100).Select(i => Sample(i, ("a", i < 10 ? 100 : 1))).ToArray();
        var assigned = DatasetSplitter.Assign(samples, new(70, 20, 10), 42);
        Assert.Equal(new[] { 70, 20, 10 }, DatasetSplitter.Names.Select(s => assigned.Values.Count(x => x == s)).ToArray());
        Assert.Equal(new[] { 763, 218, 109 }, DatasetSplitter.Names.Select(s =>
            samples.Where(x => assigned[x.FileId] == s).Sum(x => x.LabelCounts["a"])).ToArray());
    }

    [Fact]
    public void CooccurringClasses_KeepIndependentCoverageWhenFeasible()
    {
        var samples = Enumerable.Range(0, 100).Select(i => i < 10
            ? Sample(i, ("a", 1), ("b", 1)) : Sample(i, ("a", 1))).ToArray();
        var assigned = DatasetSplitter.Assign(samples, new(70, 20, 10), 42);
        Assert.Equal(new[] { 70, 20, 10 }, DatasetSplitter.Names.Select(s => assigned.Values.Count(x => x == s)).ToArray());
        Assert.Equal(new[] { 7, 2, 1 }, DatasetSplitter.Names.Select(s =>
            samples.Count(x => x.LabelCounts.ContainsKey("b") && assigned[x.FileId] == s)).ToArray());
    }

    sealed class MemoryRepository : IMaskFlowRepository
    {
        public Task EnsureSchemaAsync() => Task.CompletedTask;
        public Task<MaskFlowState?> LoadAsync() => Task.FromResult<MaskFlowState?>(new());
        public Task SaveAsync(MaskFlowState state, IReadOnlyCollection<string>? syncProjectLabelIds = null) => Task.CompletedTask;
    }

    [Theory]
    [InlineData("yolo-detect")]
    [InlineData("yolo-segment")]
    [InlineData("classification-crops")]
    public async Task ExportZip_UsesOneSplitPerSourceImageAndMatchingClassTable(string format)
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), "maskflow-split-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        string? archivePath = null;
        try
        {
            var store = new MaskFlowStore(new MemoryRepository());
            var now = DateTimeOffset.UtcNow;
            for (var id = 1; id <= 20; id++)
            {
                var path = Path.Combine(sourceDir, $"{id}.png");
                using (var image = new Image<Rgb24>(16, 16)) await image.SaveAsPngAsync(path);
                var project = id <= 10 ? "p1" : "p2";
                store.State.Files.Add(new(id, 1, project, $"{id}.png", path, new FileInfo(path).Length, "image", "image/png", now, ""));
                List<AnnotationItem> annotations =
                [
                    new("z", id <= 10 ? 0 : 1, "z", new(.5, .5, .5, .5), [.25, .25, .75, .25, .75, .75], 1),
                    new("a", id <= 10 ? 1 : 0, "a", new(.5, .5, .5, .5), [.25, .25, .75, .25, .75, .75], 1)
                ];
                store.State.AnnotationSets.Add(new($"set{id}", 1, id, 16, 16, annotations, "", now, now));
            }
            var export = await store.CreateDatasetExportAsync(1, new(null, null, new(70, 20, 10), format));
            archivePath = export.Path;
            using var zip = ZipFile.OpenRead(archivePath!);
            using var reportStream = zip.GetEntry("split-report.json")!.Open();
            using var report = await JsonDocument.ParseAsync(reportStream);
            var root = report.RootElement;
            Assert.Equal(42, root.GetProperty("seed").GetInt32());
            Assert.Equal(14, root.GetProperty("images").GetProperty("train").GetInt32());
            Assert.Empty(root.GetProperty("warnings").EnumerateArray());
            var assignments = root.GetProperty("files").EnumerateArray()
                .ToDictionary(x => x.GetProperty("fileId").GetInt32(), x => x.GetProperty("split").GetString());
            foreach (var id in Enumerable.Range(1, 20))
            {
                var prefix = format == "classification-crops" ? "classification/" : "images/";
                var entries = zip.Entries.Where(x => x.FullName.StartsWith(prefix)
                    && Path.GetFileName(x.FullName).StartsWith($"{id}_")).ToArray();
                Assert.Equal(format == "classification-crops" ? 2 : 1, entries.Length);
                Assert.All(entries, x => Assert.Equal(assignments[id], x.FullName.Split('/')[1]));
                if (format != "classification-crops")
                {
                    using var reader = new StreamReader(zip.GetEntry($"labels/{assignments[id]}/{id}_{id}.txt")!.Open());
                    var lines = (await reader.ReadToEndAsync()).Split('\n');
                    Assert.StartsWith("1 ", lines[0]);
                    Assert.StartsWith("0 ", lines[1]);
                }
            }
            foreach (var label in root.GetProperty("classes").EnumerateArray())
            {
                Assert.Equal(20, label.GetProperty("annotations").GetInt32());
                Assert.Equal(14, label.GetProperty("splits").GetProperty("train").GetProperty("annotations").GetInt32());
            }
        }
        finally
        {
            if (archivePath is not null && File.Exists(archivePath)) File.Delete(archivePath);
            Directory.Delete(sourceDir, true);
        }
    }
}
