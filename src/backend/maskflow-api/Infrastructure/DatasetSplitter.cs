public sealed record DatasetSplitSample(int FileId, IReadOnlyDictionary<string, int> LabelCounts);

public static class DatasetSplitter
{
    public static readonly string[] Names = ["train", "val", "test"];

    public static void Validate(SplitConfig split)
    {
        int[] ratios = [split.Train, split.Val, split.Test];
        if (ratios.Any(x => x < 0 || x > 100) || ratios.Sum() != 100)
            throw new BadHttpRequestException("Split ratios must be between 0 and 100 and sum to 100.", 400);
    }

    public static Dictionary<int, string> Assign(IReadOnlyList<DatasetSplitSample> samples, SplitConfig split, int seed)
    {
        Validate(split);
        int[] ratios = [split.Train, split.Val, split.Test];
        var random = new Random(seed);
        var shuffled = samples.OrderBy(x => x.FileId).ToArray();
        random.Shuffle(shuffled);
        var labels = samples.SelectMany(x => x.LabelCounts.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var members = labels.ToDictionary(label => label,
            label => shuffled.Where(x => x.LabelCounts.ContainsKey(label)).ToArray(), StringComparer.OrdinalIgnoreCase);
        var imageTargets = labels.ToDictionary(label => label,
            label => AllocateCounts(members[label].Length, ratios), StringComparer.OrdinalIgnoreCase);
        var instanceTotals = labels.ToDictionary(label => label,
            label => members[label].Sum(x => (double)x.LabelCounts[label]), StringComparer.OrdinalIgnoreCase);
        var imageCounts = labels.ToDictionary(label => label, _ => new int[3], StringComparer.OrdinalIgnoreCase);
        var instanceCounts = labels.ToDictionary(label => label, _ => new double[3], StringComparer.OrdinalIgnoreCase);
        var totalTargets = AllocateCounts(samples.Count, ratios);
        var totalCounts = new int[3];
        var assigned = new Dictionary<int, string>();
        var remaining = new HashSet<string>(labels, StringComparer.OrdinalIgnoreCase);

        // Iterative multilabel stratification: scarce labels first; keep each source image indivisible.
        while (remaining.Count > 0)
        {
            var label = remaining.OrderBy(x => members[x].Count(s => !assigned.ContainsKey(s.FileId)))
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase).First();
            remaining.Remove(label);
            foreach (var sample in members[label].Where(x => !assigned.ContainsKey(x.FileId))
                         .OrderByDescending(x => x.LabelCounts[label]))
            {
                var candidates = Enumerable.Range(0, 3).Where(s => ratios[s] > 0).ToArray();
                var withCapacity = candidates.Where(s => imageTargets[label][s] > imageCounts[label][s]).ToArray();
                var target = (withCapacity.Length > 0 ? withCapacity : candidates)
                    .OrderByDescending(s => instanceTotals[label] * ratios[s] / 100 - instanceCounts[label][s])
                    .ThenByDescending(s => imageTargets[label][s] - imageCounts[label][s])
                    .ThenByDescending(s => sample.LabelCounts.Keys.Sum(other =>
                        (imageTargets[other][s] - imageCounts[other][s]) / (double)Math.Max(1, members[other].Length)))
                    .ThenByDescending(s => totalTargets[s] - totalCounts[s])
                    .ThenBy(s => s).First();
                AssignSample(sample, target);
            }
        }

        // Empty annotation sets are background images and follow the remaining image quota.
        foreach (var sample in shuffled.Where(x => !assigned.ContainsKey(x.FileId)))
        {
            var target = Enumerable.Range(0, 3).Where(s => ratios[s] > 0)
                .OrderByDescending(s => totalTargets[s] - totalCounts[s]).ThenBy(s => s).First();
            AssignSample(sample, target);
        }
        return assigned;

        void AssignSample(DatasetSplitSample sample, int target)
        {
            assigned.Add(sample.FileId, Names[target]);
            totalCounts[target]++;
            foreach (var (label, count) in sample.LabelCounts)
            {
                imageCounts[label][target]++;
                instanceCounts[label][target] += count;
            }
        }
    }

    static int[] AllocateCounts(int count, int[] ratios)
    {
        var targets = ratios.Select(x => count * x / 100.0).ToArray();
        var result = targets.Select(x => (int)Math.Floor(x)).ToArray();
        var active = Enumerable.Range(0, 3).Where(s => ratios[s] > 0).ToArray();
        foreach (var s in active.OrderByDescending(s => targets[s] - result[s]).ThenBy(s => s)
                     .Take(count - result.Sum())) result[s]++;

        // Cover enabled sets when possible; scarce classes prioritize train, then val, then test.
        var minimum = new int[3];
        foreach (var s in active.Take(count)) minimum[s] = 1;
        foreach (var s in active.Where(s => result[s] < minimum[s]))
        {
            var donor = active.Where(i => result[i] > minimum[i])
                .OrderByDescending(i => result[i] - targets[i]).First();
            result[donor]--;
            result[s]++;
        }
        return result;
    }

    public static object Report(IReadOnlyList<DatasetSplitSample> samples, IReadOnlyList<string> labels,
        IReadOnlyDictionary<int, string> assignments, SplitConfig split, int seed)
    {
        int[] ratios = [split.Train, split.Val, split.Test];
        var warnings = new List<string>();
        var classes = labels.Select(label =>
        {
            var matching = samples.Where(x => x.LabelCounts.ContainsKey(label)).ToArray();
            var distribution = Names.ToDictionary(name => name, name => new
            {
                images = matching.Count(x => assignments[x.FileId] == name),
                annotations = matching.Where(x => assignments[x.FileId] == name).Sum(x => x.LabelCounts[label])
            });
            foreach (var s in Enumerable.Range(0, 3).Where(s => ratios[s] > 0 && distribution[Names[s]].images == 0))
                warnings.Add($"Label '{label}' has no images in {Names[s]}; only {matching.Length} source images are available. Add independent images or adjust ratios.");
            return new { label, images = matching.Length, annotations = matching.Sum(x => x.LabelCounts[label]), splits = distribution };
        }).ToArray();
        return new
        {
            strategy = "iterative-multilabel-source-image-v1", seed, requestedPercent = split,
            images = Names.ToDictionary(name => name, name => assignments.Values.Count(x => x == name)),
            classes, warnings,
            files = assignments.OrderBy(x => x.Key).Select(x => new { fileId = x.Key, split = x.Value }).ToArray(),
            note = "Source images and their crops stay together. Class coverage takes priority over exact ratios; indivisible groups may prevent exact balance. Separate files of the same subject or near-duplicates are not detected."
        };
    }
}
