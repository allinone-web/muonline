using Client.Telemetry;

namespace MuDiagnostics.Web.Services;

public sealed class TelemetryAlertEngine
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _lastEmitted = new(StringComparer.Ordinal);
    private readonly TimeSpan _cooldown = TimeSpan.FromSeconds(10);

    public IEnumerable<TelemetryEnvelope> Evaluate(TelemetryEnvelope source)
    {
        var snapshot = source.Snapshot;
        if (snapshot is null)
            yield break;

        TelemetryEnvelope alert;
        if (snapshot.Frame.P95Ms > 33.3 && TryCreate(source, "frame", TelemetrySeverity.Error, $"Frame p95 reached {snapshot.Frame.P95Ms:F1} ms", "frame-p95-critical", out alert))
            yield return alert;
        else if (snapshot.Frame.P95Ms > 20 && TryCreate(source, "frame", TelemetrySeverity.Warning, $"Frame p95 is elevated: {snapshot.Frame.P95Ms:F1} ms", "frame-p95-warning", out alert))
            yield return alert;

        if (snapshot.Runtime.MainThreadQueued > 128 && TryCreate(source, "runtime", TelemetrySeverity.Warning, $"Main-thread queue backlog: {snapshot.Runtime.MainThreadQueued}", "main-queue", out alert))
            yield return alert;

        if (snapshot.Runtime.SchedulerQueued > 128 && TryCreate(source, "runtime", TelemetrySeverity.Warning, $"Scheduler backlog: {snapshot.Runtime.SchedulerQueued}", "scheduler-queue", out alert))
            yield return alert;

        if (snapshot.Frame.Gen2Collections > 0 && TryCreate(source, "memory", TelemetrySeverity.Warning, $"Gen2 collections in profiler window: {snapshot.Frame.Gen2Collections}", "gen2", out alert))
            yield return alert;

        if (snapshot.Animation.GpuSkinningEnabled && !snapshot.Animation.GpuSkinningSupported && TryCreate(source, "animation", TelemetrySeverity.Error, "GPU skinning is enabled but the backend is unavailable", "gpu-skinning", out alert))
            yield return alert;

        if (snapshot.Animation.MultiPoseEnabled && snapshot.Animation.MultiPoseMeshInstances > 0 && snapshot.Animation.MultiPoseDrawCalls == 0 && TryCreate(source, "animation", TelemetrySeverity.Warning, "Multi-pose instances were queued but no draw call was recorded", "multi-pose-draw", out alert))
            yield return alert;

        if (snapshot.Runtime.MainThreadMs > 16.67 &&
            TryCreate(source, "runtime", TelemetrySeverity.Warning,
                $"Main-thread work took {snapshot.Runtime.MainThreadMs:F1} ms",
                "main-thread-stall", out alert))
        {
            yield return alert;
        }

        if (snapshot.Passes.PreviewMs > 8 &&
            TryCreate(source, "ui", TelemetrySeverity.Warning,
                $"Item preview rendering took {snapshot.Passes.PreviewMs:F1} ms ({snapshot.Passes.PreviewRenders} renders)",
                "preview-stall", out alert))
        {
            yield return alert;
        }

        if (snapshot.Animation.CpuFallbackDrawCalls > 24 &&
            TryCreate(source, "animation", TelemetrySeverity.Warning,
                $"CPU model fallback draw calls: {snapshot.Animation.CpuFallbackDrawCalls}",
                "cpu-skinning-fallback", out alert))
        {
            yield return alert;
        }

        if (snapshot.Animation.MultiPoseEnabled &&
            snapshot.Animation.MultiPoseAttempts >= 8 &&
            snapshot.Animation.MultiPoseQueuedObjects == 0 &&
            TryCreate(source, "animation", TelemetrySeverity.Warning,
                $"Multi-pose rejected all {snapshot.Animation.MultiPoseAttempts} attempts " +
                $"(object {snapshot.Animation.MultiPoseRejectedObject}, mesh {snapshot.Animation.MultiPoseRejectedMesh}, " +
                $"buffers {snapshot.Animation.MultiPoseRejectedBuffers}, bones {snapshot.Animation.MultiPoseRejectedBones})",
                "multi-pose-rejected", out alert))
        {
            yield return alert;
        }

        if (snapshot.Runtime.TelemetryDroppedMessages > 0 &&
            TryCreate(source, "diagnostics", TelemetrySeverity.Warning,
                $"Telemetry queue dropped {snapshot.Runtime.TelemetryDroppedMessages} messages",
                "telemetry-drops", out alert))
        {
            yield return alert;
        }
    }

    private bool TryCreate(
        TelemetryEnvelope source,
        string category,
        TelemetrySeverity severity,
        string message,
        string key,
        out TelemetryEnvelope alert)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (_lastEmitted.TryGetValue(key, out var previous) && now - previous < _cooldown)
            {
                alert = null!;
                return false;
            }
            _lastEmitted[key] = now;
        }

        alert = new TelemetryEnvelope
        {
            Kind = TelemetryMessageKind.Event,
            SessionId = source.SessionId,
            TimestampUtc = now,
            Event = new TelemetryEvent
            {
                Category = category,
                Severity = severity,
                Message = message
            }
        };
        return true;
    }
}
