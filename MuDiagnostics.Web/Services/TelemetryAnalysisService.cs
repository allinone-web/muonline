using System.Globalization;
using System.Text;
using Client.Telemetry;

namespace MuDiagnostics.Web.Services;

public sealed class TelemetryAnalysisService
{
    private const double GameWarmupSeconds = 3d;
    private const double SegmentGapSeconds = 2.5d;

    public TelemetryAnalysisReport Analyze(IReadOnlyList<TelemetryEnvelope> envelopes)
    {
        var samples = envelopes
            .Where(x => x.Kind == TelemetryMessageKind.Snapshot && x.Snapshot is not null)
            .Select(AnalysisSample.FromEnvelope)
            .OrderBy(x => x.TimestampUtc)
            .ToArray();
        return AnalyzeSamples(samples, sourceName: "live telemetry");
    }

    public async Task<TelemetryAnalysisReport> AnalyzeCsvAsync(Stream stream, string? sourceName, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        string? headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(headerLine))
            return TelemetryAnalysisReport.Empty(sourceName ?? "CSV");

        string[] headers = ParseCsvLine(headerLine).ToArray();
        var samples = new List<AnalysisSample>(4096);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var values = ParseCsvLine(line).ToArray();
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
                row[headers[i]] = i < values.Length ? values[i] : string.Empty;

            if (AnalysisSample.TryFromCsv(row, out var sample))
                samples.Add(sample);
        }

        return AnalyzeSamples(samples.OrderBy(x => x.TimestampUtc).ToArray(), sourceName ?? "CSV");
    }

    public string ToMarkdown(TelemetryAnalysisReport report)
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine("# MU Online diagnostics analysis");
        sb.AppendLine();
        sb.AppendLine($"Source: **{report.SourceName}**  ");
        sb.AppendLine($"Samples: **{report.SampleCount}**  ");
        sb.AppendLine($"Duration: **{report.DurationSeconds:F1} s**  ");
        sb.AppendLine($"Primary bottleneck: **{report.PrimaryBottleneck}**");
        sb.AppendLine();
        sb.AppendLine(report.Summary);
        sb.AppendLine();
        sb.AppendLine("## Primary steady-state segment");
        sb.AppendLine();
        var primary = report.PrimarySegment;
        if (primary is not null)
        {
            sb.AppendLine($"- Scene: `{primary.Scene}`; world `{primary.WorldIndex?.ToString() ?? "—"}`; map `{primary.MapId?.ToString() ?? "—"}`");
            sb.AppendLine($"- FPS median / 5th percentile: **{primary.FpsMedian:F1} / {primary.FpsP05:F1}**");
            sb.AppendLine($"- CPU frame median / p95 / p99: **{primary.CpuFrameMedianMs:F2} / {primary.CpuFrameP95Ms:F2} / {primary.CpuFrameP99Ms:F2} ms**");
            sb.AppendLine($"- Full frame interval median / p95 / p99: **{primary.FrameIntervalMedianMs:F2} / {primary.FrameIntervalP95Ms:F2} / {primary.FrameIntervalP99Ms:F2} ms**");
            sb.AppendLine($"- Unaccounted interval time median: **{primary.UnaccountedMedianMs:F2} ms**");
            sb.AppendLine($"- Update / Draw median: **{primary.UpdateMedianMs:F2} / {primary.DrawMedianMs:F2} ms**");
            sb.AppendLine($"- Draw share: **{primary.DrawSharePercent:F0}%**");
            sb.AppendLine($"- Allocations: **{primary.AllocatedKbMedian:F1} KB/frame**, approximately **{primary.AllocationMbPerSecond:F1} MB/s**");
            sb.AppendLine($"- Working-set trend: **{primary.WorkingSetSlopeMbPerMinute:F1} MB/min**");
            sb.AppendLine($"- Visible objects / GPU-skinned meshes median: **{primary.VisibleObjectsMedian:F0} / {primary.GpuSkinnedMeshesMedian:F0}**");
        }
        sb.AppendLine();
        sb.AppendLine("## Recommendations");
        sb.AppendLine();
        foreach (var recommendation in report.Recommendations)
            sb.AppendLine($"- **{recommendation.Title}** — {recommendation.Detail}");
        sb.AppendLine();
        sb.AppendLine("## Segments");
        sb.AppendLine();
        sb.AppendLine("| Scene | World | Duration | FPS med | CPU frame p95 | Draw med | Visible | WS slope |" );
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var segment in report.Segments)
        {
            sb.AppendLine($"| {segment.Scene} | {segment.WorldIndex?.ToString() ?? "—"} | {segment.DurationSeconds:F1}s | {segment.FpsMedian:F1} | {segment.CpuFrameP95Ms:F2}ms | {segment.DrawMedianMs:F2}ms | {segment.VisibleObjectsMedian:F0} | {segment.WorkingSetSlopeMbPerMinute:F1} MB/min |");
        }
        return sb.ToString();
    }

    private TelemetryAnalysisReport AnalyzeSamples(IReadOnlyList<AnalysisSample> samples, string sourceName)
    {
        if (samples.Count == 0)
            return TelemetryAnalysisReport.Empty(sourceName);

        var segments = BuildSegments(samples);
        var primary = segments.LastOrDefault(x => string.Equals(x.Scene, "GameScene", StringComparison.OrdinalIgnoreCase) && x.SteadySampleCount >= 10)
            ?? segments.LastOrDefault();

        var primarySamples = primary is null
            ? Array.Empty<AnalysisSample>()
            : GetSegmentSamples(samples, primary.SegmentId, steadyOnly: true);
        var spikes = DetectSpikes(primarySamples);
        string bottleneck = DetermineBottleneck(primary, spikes);
        var recommendations = BuildRecommendations(primary, spikes, samples);
        var quality = BuildDataQuality(samples, segments);

        string summary = primary is null
            ? "No stable segment was available for analysis."
            : BuildSummary(primary, bottleneck);

        return new TelemetryAnalysisReport
        {
            SourceName = sourceName,
            GeneratedUtc = DateTimeOffset.UtcNow,
            SampleCount = samples.Count,
            DurationSeconds = Math.Max(0, (samples[^1].TimestampUtc - samples[0].TimestampUtc).TotalSeconds),
            PrimaryBottleneck = bottleneck,
            Summary = summary,
            PrimarySegment = primary,
            Segments = segments,
            Spikes = spikes,
            Recommendations = recommendations,
            DataQuality = quality
        };
    }

    private static List<SegmentAnalysis> BuildSegments(IReadOnlyList<AnalysisSample> samples)
    {
        var result = new List<SegmentAnalysis>();
        int segmentId = 0;
        int start = 0;
        for (int i = 1; i <= samples.Count; i++)
        {
            bool boundary = i == samples.Count ||
                !SameContext(samples[i - 1], samples[i]) ||
                (samples[i].TimestampUtc - samples[i - 1].TimestampUtc).TotalSeconds > SegmentGapSeconds;
            if (!boundary)
                continue;

            segmentId++;
            var slice = samples.Skip(start).Take(i - start).ToArray();
            result.Add(AnalyzeSegment(segmentId, slice));
            start = i;
        }
        return result;
    }

    private static bool SameContext(AnalysisSample a, AnalysisSample b) =>
        string.Equals(a.SessionId, b.SessionId, StringComparison.Ordinal) &&
        string.Equals(a.Scene, b.Scene, StringComparison.Ordinal) &&
        a.WorldIndex == b.WorldIndex &&
        a.MapId == b.MapId;

    private static SegmentAnalysis AnalyzeSegment(int segmentId, AnalysisSample[] raw)
    {
        DateTimeOffset start = raw[0].TimestampUtc;
        DateTimeOffset end = raw[^1].TimestampUtc;
        double duration = Math.Max(0, (end - start).TotalSeconds);
        double warmup = string.Equals(raw[0].Scene, "GameScene", StringComparison.OrdinalIgnoreCase) ? GameWarmupSeconds : 1d;
        AnalysisSample[] steady = raw.Where(x => (x.TimestampUtc - start).TotalSeconds >= warmup).ToArray();
        if (steady.Length < Math.Min(5, raw.Length))
            steady = raw;

        double[] cpuFrames = steady.Select(x => x.CpuFrameMs).ToArray();
        double[] frameIntervals = steady.Select(x => x.FrameIntervalMs).Where(IsFinitePositive).ToArray();
        if (frameIntervals.Length == 0)
            frameIntervals = cpuFrames;
        double[] fps = steady.Select(x => x.Fps).Where(IsFinitePositive).ToArray();
        double[] allocationsPerSecond = steady
            .Where(x => IsFinitePositive(x.Fps))
            .Select(x => x.AllocatedKb * x.Fps / 1024d)
            .ToArray();

        return new SegmentAnalysis
        {
            SegmentId = segmentId,
            SessionId = raw[0].SessionId,
            Scene = raw[0].Scene,
            WorldIndex = raw[0].WorldIndex,
            MapId = raw[0].MapId,
            StartUtc = start,
            EndUtc = end,
            DurationSeconds = duration,
            SampleCount = raw.Length,
            SteadySampleCount = steady.Length,
            FpsMedian = Median(fps),
            FpsP05 = Percentile(fps, 0.05),
            CpuFrameMedianMs = Median(cpuFrames),
            CpuFrameP95Ms = Percentile(cpuFrames, 0.95),
            CpuFrameP99Ms = Percentile(cpuFrames, 0.99),
            FrameIntervalMedianMs = Median(frameIntervals),
            FrameIntervalP95Ms = Percentile(frameIntervals, 0.95),
            FrameIntervalP99Ms = Percentile(frameIntervals, 0.99),
            UnaccountedMedianMs = Median(steady.Select(x => x.FrameIntervalUnaccountedMs)),
            InactiveSamplePercent = 100d * SafeDivide(steady.Count(x => !x.IsActive), steady.Length),
            UpdateMedianMs = Median(steady.Select(x => x.UpdateMs)),
            DrawMedianMs = Median(steady.Select(x => x.DrawMs)),
            MainThreadMaxMs = steady.Max(x => x.MainThreadMs),
            MainThreadLongestActionMaxMs = steady.Max(x => x.MainThreadLongestActionMs),
            DrawSharePercent = 100d * SafeDivide(Median(steady.Select(x => x.DrawMs)), Median(steady.Select(x => x.UpdateMs + x.DrawMs))),
            RollingP95MedianMs = Median(steady.Select(x => x.P95Ms)),
            RollingP99MedianMs = Median(steady.Select(x => x.P99Ms)),
            AllocatedKbMedian = Median(steady.Select(x => x.AllocatedKb)),
            AllocationMbPerSecond = Median(allocationsPerSecond),
            WorkingSetSlopeMbPerMinute = RobustTrendPerMinute(steady, x => x.WorkingSetMb),
            ManagedMemorySlopeMbPerMinute = RobustTrendPerMinute(steady, x => x.ManagedMemoryMb),
            VisibleObjectsMedian = Median(steady.Select(x => (double)x.VisibleObjects)),
            GpuSkinnedMeshesMedian = Median(steady.Select(x => (double)x.GpuSkinnedMeshes)),
            EstimatedDrawCallsMedian = Median(steady.Select(x => (double)x.EstimatedDrawCalls)),
            CullP95Ms = Percentile(steady.Select(x => x.CullMs), 0.95),
            MultiPoseObjectsMax = steady.Max(x => x.MultiPoseObjects),
            MultiPoseDrawCallsMax = steady.Max(x => x.MultiPoseDrawCalls),
            MultiPoseAttemptsTotal = steady.Sum(x => x.MultiPoseAttempts),
            MultiPoseQueuedTotal = steady.Sum(x => x.MultiPoseQueuedObjects),
            SceneDrawMedianMs = Median(steady.Select(x => x.SceneDrawMs)),
            WorldObjectsMedianMs = Median(steady.Select(x => x.WorldObjectsMs)),
            TerrainMedianMs = Median(steady.Select(x => x.TerrainOpaqueMs + x.TerrainAfterMs)),
            ShadowMedianMs = Median(steady.Select(x => x.ShadowMs)),
            PreviewP95Ms = Percentile(steady.Select(x => x.PreviewMs), 0.95),
            PostProcessMedianMs = Median(steady.Select(x => x.PostProcessMs))
        };
    }

    private static AnalysisSample[] GetSegmentSamples(IReadOnlyList<AnalysisSample> samples, int segmentId, bool steadyOnly)
    {
        int current = 0;
        int start = 0;
        for (int i = 1; i <= samples.Count; i++)
        {
            bool boundary = i == samples.Count || !SameContext(samples[i - 1], samples[i]) ||
                (samples[i].TimestampUtc - samples[i - 1].TimestampUtc).TotalSeconds > SegmentGapSeconds;
            if (!boundary)
                continue;
            current++;
            if (current == segmentId)
            {
                var slice = samples.Skip(start).Take(i - start).ToArray();
                if (!steadyOnly)
                    return slice;
                double warmup = string.Equals(slice[0].Scene, "GameScene", StringComparison.OrdinalIgnoreCase) ? GameWarmupSeconds : 1d;
                var steady = slice.Where(x => (x.TimestampUtc - slice[0].TimestampUtc).TotalSeconds >= warmup).ToArray();
                return steady.Length >= Math.Min(5, slice.Length) ? steady : slice;
            }
            start = i;
        }
        return Array.Empty<AnalysisSample>();
    }

    private static List<SpikeAnalysis> DetectSpikes(IReadOnlyList<AnalysisSample> samples)
    {
        if (samples.Count < 5)
            return new List<SpikeAnalysis>();
        double median = Median(samples.Select(x => x.EffectiveFrameMs));
        double mad = Median(samples.Select(x => Math.Abs(x.EffectiveFrameMs - median)));
        double threshold = Math.Max(16.667, median + Math.Max(6d, mad * 4d));
        return samples
            .Where(x => x.EffectiveFrameMs >= threshold)
            .OrderByDescending(x => x.EffectiveFrameMs)
            .Take(20)
            .Select(x => new SpikeAnalysis
            {
                TimestampUtc = x.TimestampUtc,
                FrameMs = x.EffectiveFrameMs,
                UpdateMs = x.UpdateMs,
                DrawMs = x.DrawMs,
                MainThreadMs = x.MainThreadMs,
                AllocatedKb = x.AllocatedKb,
                VisibleObjects = x.VisibleObjects,
                GpuSkinnedMeshes = x.GpuSkinnedMeshes,
                Category = ClassifySpike(x),
                DominantPass = DominantPass(x)
            })
            .ToList();
    }

    private static string ClassifySpike(AnalysisSample x)
    {
        if (x.HasSignificantExternalWait) return x.IsActive ? "external-wait" : "inactive-window";
        if (x.MainThreadLongestActionMs >= 3d || x.MainThreadMs >= Math.Max(3, Math.Max(x.UpdateMs, x.DrawMs))) return "main-thread";
        if (x.PreviewMs >= 2) return "ui-preview";
        if (x.ShadowMs >= 2) return "shadow";
        if (x.DrawMs > x.UpdateMs * 1.5) return "render";
        if (x.UpdateMs > x.DrawMs) return "update";
        return "mixed";
    }

    private static string DominantPass(AnalysisSample x)
    {
        var passes = new (string Name, double Value)[]
        {
            ("world objects", x.WorldObjectsMs), ("terrain", x.TerrainOpaqueMs + x.TerrainAfterMs),
            ("shadow", x.ShadowMs), ("preview", x.PreviewMs), ("post-process", x.PostProcessMs),
            ("scene after", x.SceneAfterMs), ("framework", x.FrameworkDrawMs)
        };
        var best = passes.OrderByDescending(p => p.Value).First();
        return best.Value > 0.05 ? $"{best.Name} ({best.Value:F2} ms)" : "not available";
    }

    private static string DetermineBottleneck(SegmentAnalysis? primary, IReadOnlyList<SpikeAnalysis> spikes)
    {
        if (primary is null) return "insufficient data";
        if (primary.InactiveSamplePercent > 25d && primary.FrameIntervalP95Ms > Math.Max(25d, primary.CpuFrameP95Ms * 2d))
            return "inactive-window throttling";
        if (primary.UnaccountedMedianMs > Math.Max(4d, primary.CpuFrameMedianMs * 0.75d))
            return "external wait / frame pacing";
        if (Math.Max(primary.MainThreadMaxMs, primary.MainThreadLongestActionMaxMs) > 10)
            return "main-thread stalls";
        if (primary.DrawMedianMs > Math.Max(3, primary.UpdateMedianMs * 2))
        {
            if (primary.WorldObjectsMedianMs > Math.Max(primary.TerrainMedianMs, primary.ShadowMedianMs) && primary.WorldObjectsMedianMs > 0.5)
                return "render-bound: world models";
            if (primary.TerrainMedianMs > Math.Max(primary.WorldObjectsMedianMs, primary.ShadowMedianMs) && primary.TerrainMedianMs > 0.5)
                return "render-bound: terrain/grass";
            return "render-bound (GPU or render submission)";
        }
        if (primary.UpdateMedianMs > Math.Max(3, primary.DrawMedianMs * 1.2))
            return "update-bound";
        if (primary.AllocationMbPerSecond > 12)
            return "allocation/GC pressure";
        return spikes.Count > 0 ? "intermittent frame spikes" : "balanced";
    }

    private static List<AnalysisRecommendation> BuildRecommendations(
        SegmentAnalysis? primary,
        IReadOnlyList<SpikeAnalysis> spikes,
        IReadOnlyList<AnalysisSample> allSamples)
    {
        var result = new List<AnalysisRecommendation>();
        if (primary is null)
            return result;

        if (primary.InactiveSamplePercent > 25d && primary.FrameIntervalP95Ms > primary.CpuFrameP95Ms * 2d)
            result.Add(new("Separate inactive-window samples", $"{primary.InactiveSamplePercent:F0}% of steady samples were captured while the game was inactive. Full frame p95 was {primary.FrameIntervalP95Ms:F1} ms versus CPU p95 {primary.CpuFrameP95Ms:F1} ms; exclude these samples from render benchmarking or set InactiveSleepTime to zero only for controlled tests."));
        else if (primary.UnaccountedMedianMs > 4d)
            result.Add(new("Inspect frame pacing", $"Median time outside Update+Draw was {primary.UnaccountedMedianMs:F1} ms. Check VSync, Present waits, window focus and driver scheduling before optimizing CPU code."));

        if (primary.DrawMedianMs > primary.UpdateMedianMs * 2)
            result.Add(new("Prioritize rendering", $"Draw median is {primary.DrawMedianMs:F2} ms versus {primary.UpdateMedianMs:F2} ms for Update. Optimize model/material passes before culling or game logic."));

        if (primary.GpuSkinnedMeshesMedian > 150 && primary.MultiPoseObjectsMax <= 1)
            result.Add(new("Verify multi-pose eligibility", $"The scene renders about {primary.GpuSkinnedMeshesMedian:F0} GPU-skinned meshes, but at most {primary.MultiPoseObjectsMax} multi-pose object. Use the new rejection counters in a crowd scene to identify material or policy exclusions."));

        if (primary.AllocationMbPerSecond > 5)
            result.Add(new("Reduce per-frame allocations", $"Estimated allocation rate is {primary.AllocationMbPerSecond:F1} MB/s ({primary.AllocatedKbMedian:F0} KB/frame). Focus on model/UI temporary collections and string formatting."));

        if (primary.DurationSeconds >= 300 && primary.WorkingSetSlopeMbPerMinute > 5 && primary.ManagedMemorySlopeMbPerMinute > 2)
            result.Add(new("Watch sustained memory growth", $"Working set and managed memory increased by approximately {primary.WorkingSetSlopeMbPerMinute:F1} and {primary.ManagedMemorySlopeMbPerMinute:F1} MB/min. Repeat a fixed route to verify whether assets return to a stable plateau."));
        else if (primary.DurationSeconds < 300 && (primary.WorkingSetSlopeMbPerMinute > 8 || primary.ManagedMemorySlopeMbPerMinute > 5))
            result.Add(new("Record a longer memory run", "The current segment is too short to classify memory growth reliably. Record 15–30 minutes across repeated world transitions before treating the trend as a leak."));

        double longestMainAction = Math.Max(primary.MainThreadMaxMs, primary.MainThreadLongestActionMaxMs);
        if (longestMainAction > 10)
            result.Add(new("Split slow main-thread actions", $"A main-thread action reached {longestMainAction:F1} ms. Use the recorded action name and queue delay to split texture uploads or scene publication into budgeted batches."));

        if (primary.CullP95Ms > 1)
            result.Add(new("Inspect culling rebuilds", $"Culling p95 reached {primary.CullP95Ms:F2} ms. Correlate spikes with camera movement and world transitions; steady cached culling should remain well below 1 ms."));

        if (spikes.Any(x => x.Category == "ui-preview"))
            result.Add(new("Throttle item previews", "Preview rendering appears in detected spikes. Keep animated previews budgeted and preload geometry before opening inventory or NPC shops."));

        if (result.Count == 0)
            result.Add(new("Collect a heavier scenario", "Current steady-state data looks balanced. Record at least five minutes with many identical monsters, shadows enabled, inventory and NPC shop interactions."));
        return result;
    }

    private static DataQualityAnalysis BuildDataQuality(IReadOnlyList<AnalysisSample> samples, IReadOnlyList<SegmentAnalysis> segments)
    {
        double[] intervals = samples.Zip(samples.Skip(1), (a, b) => (b.TimestampUtc - a.TimestampUtc).TotalSeconds)
            .Where(x => x > 0 && x < 10).ToArray();
        return new DataQualityAnalysis
        {
            MedianSampleIntervalMs = Median(intervals) * 1000d,
            SceneTransitionCount = Math.Max(0, segments.Count - 1),
            HasRenderPassBreakdown = samples.Any(x => x.SceneDrawMs > 0 || x.WorldObjectsMs > 0 || x.TerrainOpaqueMs > 0),
            HasMultiPoseRejectionCounters = samples.Any(x => x.MultiPoseAttempts > 0),
            Note = samples.Any(x => x.FrameIntervalMs > 0)
                ? "Protocol v3 frame indices distinguish current CPU/pass metrics from the preceding full frame interval. Use frameIntervalMs for pacing and cpuFrameMs for CPU cost."
                : segments.Count > 1
                    ? "Legacy telemetry: rolling p95/p99 can contain frames from the previous scene and current fields may be sampled at different refresh points."
                    : "Legacy single-context recording without full frame interval metrics."
        };
    }

    private static string BuildSummary(SegmentAnalysis primary, string bottleneck)
    {
        return $"The primary steady-state segment is {primary.Scene} / world {primary.WorldIndex?.ToString() ?? "—"}. " +
               $"Median FPS was {primary.FpsMedian:F1}, CPU frame p95 was {primary.CpuFrameP95Ms:F2} ms, " +
               $"full frame interval p95 was {primary.FrameIntervalP95Ms:F2} ms, and Draw represented {primary.DrawSharePercent:F0}% " +
               $"of Update+Draw time. The current classification is {bottleneck}.";
    }

    private static double RobustTrendPerMinute(IReadOnlyList<AnalysisSample> samples, Func<AnalysisSample, double> selector)
    {
        if (samples.Count < 10)
            return 0;

        int window = Math.Clamp(samples.Count / 10, 5, 60);
        var first = samples.Take(window).ToArray();
        var last = samples.Skip(samples.Count - window).ToArray();
        double firstValue = Median(first.Select(selector));
        double lastValue = Median(last.Select(selector));
        double firstMilliseconds = Median(first.Select(x => (double)x.TimestampUtc.ToUnixTimeMilliseconds()));
        double lastMilliseconds = Median(last.Select(x => (double)x.TimestampUtc.ToUnixTimeMilliseconds()));
        double minutes = Math.Max(0, lastMilliseconds - firstMilliseconds) / 60000d;
        return minutes <= 0.01 ? 0 : (lastValue - firstValue) / minutes;
    }

    private static double Median(IEnumerable<double> values) => Percentile(values, 0.5);

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        double[] sorted = values.Where(double.IsFinite).OrderBy(x => x).ToArray();
        if (sorted.Length == 0) return 0;
        double position = Math.Clamp(percentile, 0, 1) * (sorted.Length - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper) return sorted[lower];
        double fraction = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }

    private static double SafeDivide(double numerator, double denominator) => Math.Abs(denominator) < 0.000001 ? 0 : numerator / denominator;
    private static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0;

    private static IEnumerable<string> ParseCsvLine(string line)
    {
        var value = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else value.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',')
            {
                yield return value.ToString();
                value.Clear();
            }
            else value.Append(c);
        }
        yield return value.ToString();
    }
}

public sealed record TelemetryAnalysisReport
{
    public required string SourceName { get; init; }
    public DateTimeOffset GeneratedUtc { get; init; }
    public int SampleCount { get; init; }
    public double DurationSeconds { get; init; }
    public required string PrimaryBottleneck { get; init; }
    public required string Summary { get; init; }
    public SegmentAnalysis? PrimarySegment { get; init; }
    public required IReadOnlyList<SegmentAnalysis> Segments { get; init; }
    public required IReadOnlyList<SpikeAnalysis> Spikes { get; init; }
    public required IReadOnlyList<AnalysisRecommendation> Recommendations { get; init; }
    public required DataQualityAnalysis DataQuality { get; init; }

    public static TelemetryAnalysisReport Empty(string sourceName) => new()
    {
        SourceName = sourceName,
        GeneratedUtc = DateTimeOffset.UtcNow,
        PrimaryBottleneck = "insufficient data",
        Summary = "No telemetry samples were available.",
        Segments = Array.Empty<SegmentAnalysis>(),
        Spikes = Array.Empty<SpikeAnalysis>(),
        Recommendations = Array.Empty<AnalysisRecommendation>(),
        DataQuality = new DataQualityAnalysis { Note = "No data." }
    };
}

public sealed record SegmentAnalysis
{
    public int SegmentId { get; init; }
    public required string SessionId { get; init; }
    public required string Scene { get; init; }
    public int? WorldIndex { get; init; }
    public int? MapId { get; init; }
    public DateTimeOffset StartUtc { get; init; }
    public DateTimeOffset EndUtc { get; init; }
    public double DurationSeconds { get; init; }
    public int SampleCount { get; init; }
    public int SteadySampleCount { get; init; }
    public double FpsMedian { get; init; }
    public double FpsP05 { get; init; }
    public double CpuFrameMedianMs { get; init; }
    public double CpuFrameP95Ms { get; init; }
    public double CpuFrameP99Ms { get; init; }
    public double FrameIntervalMedianMs { get; init; }
    public double FrameIntervalP95Ms { get; init; }
    public double FrameIntervalP99Ms { get; init; }
    public double UnaccountedMedianMs { get; init; }
    public double InactiveSamplePercent { get; init; }
    public double UpdateMedianMs { get; init; }
    public double DrawMedianMs { get; init; }
    public double MainThreadMaxMs { get; init; }
    public double MainThreadLongestActionMaxMs { get; init; }
    public double DrawSharePercent { get; init; }
    public double RollingP95MedianMs { get; init; }
    public double RollingP99MedianMs { get; init; }
    public double AllocatedKbMedian { get; init; }
    public double AllocationMbPerSecond { get; init; }
    public double WorkingSetSlopeMbPerMinute { get; init; }
    public double ManagedMemorySlopeMbPerMinute { get; init; }
    public double VisibleObjectsMedian { get; init; }
    public double GpuSkinnedMeshesMedian { get; init; }
    public double EstimatedDrawCallsMedian { get; init; }
    public double CullP95Ms { get; init; }
    public int MultiPoseObjectsMax { get; init; }
    public int MultiPoseDrawCallsMax { get; init; }
    public int MultiPoseAttemptsTotal { get; init; }
    public int MultiPoseQueuedTotal { get; init; }
    public double SceneDrawMedianMs { get; init; }
    public double WorldObjectsMedianMs { get; init; }
    public double TerrainMedianMs { get; init; }
    public double ShadowMedianMs { get; init; }
    public double PreviewP95Ms { get; init; }
    public double PostProcessMedianMs { get; init; }
}

public sealed record SpikeAnalysis
{
    public DateTimeOffset TimestampUtc { get; init; }
    public double FrameMs { get; init; }
    public double UpdateMs { get; init; }
    public double DrawMs { get; init; }
    public double MainThreadMs { get; init; }
    public double AllocatedKb { get; init; }
    public int VisibleObjects { get; init; }
    public int GpuSkinnedMeshes { get; init; }
    public required string Category { get; init; }
    public required string DominantPass { get; init; }
}

public sealed record AnalysisRecommendation(string Title, string Detail);

public sealed record DataQualityAnalysis
{
    public double MedianSampleIntervalMs { get; init; }
    public int SceneTransitionCount { get; init; }
    public bool HasRenderPassBreakdown { get; init; }
    public bool HasMultiPoseRejectionCounters { get; init; }
    public required string Note { get; init; }
}

internal sealed record AnalysisSample
{
    public DateTimeOffset TimestampUtc { get; init; }
    public required string SessionId { get; init; }
    public required string Scene { get; init; }
    public int? WorldIndex { get; init; }
    public int? MapId { get; init; }
    public double Fps { get; init; }
    public double UpdateMs { get; init; }
    public double DrawMs { get; init; }
    public double ExplicitCpuFrameMs { get; init; }
    public double FrameIntervalMs { get; init; }
    public double FrameIntervalCpuMs { get; init; }
    public double FrameIntervalUnaccountedMs { get; init; }
    public double WallP95Ms { get; init; }
    public bool IsActive { get; init; } = true;
    public double P95Ms { get; init; }
    public double P99Ms { get; init; }
    public double AllocatedKb { get; init; }
    public int VisibleObjects { get; init; }
    public double CullMs { get; init; }
    public int EstimatedDrawCalls { get; init; }
    public int GpuSkinnedMeshes { get; init; }
    public int MultiPoseObjects { get; init; }
    public int MultiPoseDrawCalls { get; init; }
    public int MultiPoseAttempts { get; init; }
    public int MultiPoseQueuedObjects { get; init; }
    public double MainThreadMs { get; init; }
    public double MainThreadLongestActionMs { get; init; }
    public string? MainThreadLongestActionName { get; init; }
    public double WorkingSetMb { get; init; }
    public double ManagedMemoryMb { get; init; }
    public double SceneDrawMs { get; init; }
    public double SceneAfterMs { get; init; }
    public double PostProcessMs { get; init; }
    public double FrameworkDrawMs { get; init; }
    public double ShadowMs { get; init; }
    public double WorldObjectsMs { get; init; }
    public double TerrainOpaqueMs { get; init; }
    public double TerrainAfterMs { get; init; }
    public double PreviewMs { get; init; }
    public double CpuFrameMs => ExplicitCpuFrameMs > 0d ? ExplicitCpuFrameMs : UpdateMs + DrawMs;
    public double EffectiveIntervalCpuMs => FrameIntervalCpuMs > 0d ? FrameIntervalCpuMs : CpuFrameMs;
    public bool HasSignificantExternalWait =>
        FrameIntervalMs > 0d &&
        FrameIntervalUnaccountedMs >= Math.Max(5d, EffectiveIntervalCpuMs);
    public double EffectiveFrameMs => HasSignificantExternalWait ? FrameIntervalMs : CpuFrameMs;

    public static AnalysisSample FromEnvelope(TelemetryEnvelope envelope)
    {
        var s = envelope.Snapshot!;
        return new AnalysisSample
        {
            TimestampUtc = envelope.TimestampUtc,
            SessionId = envelope.SessionId,
            Scene = s.Session.Scene,
            WorldIndex = s.Session.WorldIndex,
            MapId = s.Session.MapId,
            Fps = s.Frame.Fps,
            UpdateMs = s.Frame.UpdateMs,
            DrawMs = s.Frame.DrawMs,
            ExplicitCpuFrameMs = s.Frame.CpuFrameMs,
            FrameIntervalMs = s.Frame.FrameIntervalMs,
            FrameIntervalCpuMs = s.Frame.FrameIntervalCpuMs,
            FrameIntervalUnaccountedMs = s.Frame.FrameIntervalUnaccountedMs,
            WallP95Ms = s.Frame.WallP95Ms,
            IsActive = envelope.ProtocolVersion < 3 || s.Frame.IsActive,
            P95Ms = s.Frame.P95Ms,
            P99Ms = s.Frame.P99Ms,
            AllocatedKb = s.Frame.AllocatedKb,
            VisibleObjects = s.World.VisibleObjects,
            CullMs = s.World.CullMs,
            EstimatedDrawCalls = s.Rendering.EstimatedDrawCalls,
            GpuSkinnedMeshes = s.Animation.GpuSkinnedMeshes,
            MultiPoseObjects = s.Animation.MultiPoseObjects,
            MultiPoseDrawCalls = s.Animation.MultiPoseDrawCalls,
            MultiPoseAttempts = s.Animation.MultiPoseAttempts,
            MultiPoseQueuedObjects = s.Animation.MultiPoseQueuedObjects,
            MainThreadMs = s.Runtime.MainThreadMs,
            MainThreadLongestActionMs = s.Runtime.MainThreadLongestActionMs,
            MainThreadLongestActionName = s.Runtime.MainThreadLongestActionName,
            WorkingSetMb = s.Runtime.WorkingSetMb,
            ManagedMemoryMb = s.Runtime.ManagedMemoryMb,
            SceneDrawMs = s.Passes.SceneDrawMs,
            SceneAfterMs = s.Passes.SceneAfterMs,
            PostProcessMs = s.Passes.PostProcessMs,
            FrameworkDrawMs = s.Passes.FrameworkDrawMs,
            ShadowMs = s.Passes.ShadowMs,
            WorldObjectsMs = s.Passes.WorldObjectsMs,
            TerrainOpaqueMs = s.Passes.TerrainOpaqueMs,
            TerrainAfterMs = s.Passes.TerrainAfterMs,
            PreviewMs = s.Passes.PreviewMs
        };
    }

    public static bool TryFromCsv(IReadOnlyDictionary<string, string> row, out AnalysisSample sample)
    {
        sample = null!;
        if (!DateTimeOffset.TryParse(Get(row, "timestampUtc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            return false;

        sample = new AnalysisSample
        {
            TimestampUtc = timestamp,
            SessionId = Get(row, "sessionId") ?? "csv-session",
            Scene = Get(row, "scene") ?? "Unknown",
            WorldIndex = NullableInt(row, "worldIndex"),
            MapId = NullableInt(row, "mapId"),
            Fps = Number(row, "fps"),
            UpdateMs = Number(row, "updateMs"),
            DrawMs = Number(row, "drawMs"),
            ExplicitCpuFrameMs = Number(row, "cpuFrameMs"),
            FrameIntervalMs = Number(row, "frameIntervalMs"),
            FrameIntervalCpuMs = Number(row, "frameIntervalCpuMs"),
            FrameIntervalUnaccountedMs = Number(row, "frameIntervalUnaccountedMs"),
            WallP95Ms = Number(row, "wallP95Ms"),
            IsActive = Boolean(row, "isActive", defaultValue: true),
            P95Ms = Number(row, "p95Ms"),
            P99Ms = Number(row, "p99Ms"),
            AllocatedKb = Number(row, "allocatedKb"),
            VisibleObjects = Integer(row, "visibleObjects"),
            CullMs = Number(row, "cullMs"),
            EstimatedDrawCalls = Integer(row, "estimatedDrawCalls"),
            GpuSkinnedMeshes = Integer(row, "gpuSkinnedMeshes"),
            MultiPoseObjects = Integer(row, "multiPoseObjects"),
            MultiPoseDrawCalls = Integer(row, "multiPoseDrawCalls"),
            MultiPoseAttempts = Integer(row, "multiPoseAttempts"),
            MultiPoseQueuedObjects = Integer(row, "multiPoseQueuedObjects"),
            MainThreadMs = Number(row, "mainThreadMs"),
            MainThreadLongestActionMs = Number(row, "mainLongestActionMs"),
            MainThreadLongestActionName = Get(row, "mainLongestActionName"),
            WorkingSetMb = Number(row, "workingSetMb"),
            ManagedMemoryMb = Number(row, "managedMemoryMb"),
            SceneDrawMs = Number(row, "sceneDrawMs"),
            SceneAfterMs = Number(row, "sceneAfterMs"),
            PostProcessMs = Number(row, "postProcessMs"),
            FrameworkDrawMs = Number(row, "frameworkDrawMs"),
            ShadowMs = Number(row, "shadowMs"),
            WorldObjectsMs = Number(row, "worldObjectsMs"),
            TerrainOpaqueMs = Number(row, "terrainOpaqueMs"),
            TerrainAfterMs = Number(row, "terrainAfterMs"),
            PreviewMs = Number(row, "previewMs")
        };
        return true;
    }

    private static string? Get(IReadOnlyDictionary<string, string> row, string key) => row.TryGetValue(key, out var value) ? value : null;
    private static double Number(IReadOnlyDictionary<string, string> row, string key) => double.TryParse(Get(row, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static int Integer(IReadOnlyDictionary<string, string> row, string key) => int.TryParse(Get(row, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static bool Boolean(IReadOnlyDictionary<string, string> row, string key, bool defaultValue = false) => bool.TryParse(Get(row, key), out var value) ? value : defaultValue;
    private static int? NullableInt(IReadOnlyDictionary<string, string> row, string key) => int.TryParse(Get(row, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}
