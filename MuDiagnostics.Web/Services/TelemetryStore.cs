using System.Globalization;
using System.Text;
using Client.Telemetry;

namespace MuDiagnostics.Web.Services;

public sealed class TelemetryStore
{
    private readonly object _gate = new();
    private readonly List<TelemetryEnvelope> _snapshots;
    private readonly List<TelemetryEnvelope> _events;
    private readonly int _maxSnapshots;
    private readonly int _maxEvents;
    private TelemetryEnvelope? _latest;
    private TelemetryClientInfo? _client;
    private string? _activeSessionId;
    private DateTimeOffset? _connectedAtUtc;
    private DateTimeOffset? _lastReceivedUtc;
    private bool _pipeConnected;

    public TelemetryStore(DiagnosticsServerOptions options)
    {
        _maxSnapshots = Math.Max(300, options.HistoryMinutes * 60 * 10);
        _maxEvents = options.MaxEvents;
        _snapshots = new List<TelemetryEnvelope>(Math.Min(_maxSnapshots, 10_000));
        _events = new List<TelemetryEnvelope>(Math.Min(_maxEvents, 2_000));
    }

    public void SetConnected(TelemetryEnvelope hello)
    {
        lock (_gate)
        {
            _pipeConnected = true;
            _activeSessionId = hello.SessionId;
            _client = hello.Client;
            _connectedAtUtc = DateTimeOffset.UtcNow;
            _lastReceivedUtc = hello.TimestampUtc;
        }
    }

    public void SetDisconnected(string? sessionId)
    {
        lock (_gate)
        {
            if (sessionId is null || string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal))
                _pipeConnected = false;
        }
    }

    public void Add(TelemetryEnvelope envelope)
    {
        lock (_gate)
        {
            _activeSessionId = envelope.SessionId;
            _lastReceivedUtc = envelope.TimestampUtc;

            if (envelope.Kind == TelemetryMessageKind.Snapshot && envelope.Snapshot is not null)
            {
                _latest = envelope;
                _snapshots.Add(envelope);
                TrimList(_snapshots, _maxSnapshots);
            }
            else if (envelope.Kind == TelemetryMessageKind.Event && envelope.Event is not null)
            {
                _events.Add(envelope);
                TrimList(_events, _maxEvents);
            }
        }
    }

    public TelemetryStatus GetStatus()
    {
        lock (_gate)
        {
            return new TelemetryStatus
            {
                PipeConnected = _pipeConnected,
                ActiveSessionId = _activeSessionId,
                ConnectedAtUtc = _connectedAtUtc,
                LastReceivedUtc = _lastReceivedUtc,
                Client = _client,
                Latest = _latest,
                SnapshotCount = _snapshots.Count,
                EventCount = _events.Count
            };
        }
    }

    public IReadOnlyList<TelemetryEnvelope> GetHistory(TimeSpan duration)
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - duration;
        lock (_gate)
            return _snapshots.Where(x => x.TimestampUtc >= cutoff).ToArray();
    }

    public IReadOnlyList<TelemetryEnvelope> GetEvents(int limit)
    {
        lock (_gate)
        {
            int count = Math.Min(Math.Clamp(limit, 1, _maxEvents), _events.Count);
            return _events.Skip(_events.Count - count).ToArray();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _snapshots.Clear();
            _events.Clear();
            _latest = null;
        }
    }

    public string ExportCsv(TimeSpan duration)
    {
        var history = GetHistory(duration);
        var sb = new StringBuilder(Math.Max(1024, history.Count * 896));
        sb.AppendLine("timestampUtc,sessionId,scene,worldIndex,mapId,fps,ups,updateMs,drawMs,p50Ms,p95Ms,p99Ms,worstMs,allocatedKb,framesOver16Ms,framesOver33Ms,gen0Collections,gen1Collections,gen2Collections,cullCandidates,visibleObjects,cullMs,cullWasRebuild,modelObjects,spriteObjects,transparentObjects,animationUpdates,animationSkips,lowQualityObjects,terrainDrawCalls,terrainTriangles,terrainBlocks,terrainCells,grassDrawCalls,registeredLights,activeLights,visibleLights,uploadedLights,estimatedDrawCalls,gpuSkinnedMeshes,cpuFallbackDrawCalls,gpuBatchDrawCalls,gpuBatchedMeshes,sharedPaletteHits,sharedPaletteMisses,staticInstancedObjects,staticMeshInstances,staticDrawCalls,multiPoseObjects,multiPoseMeshInstances,multiPosePoses,multiPoseDrawCalls,multiPoseAttempts,multiPoseQueuedObjects,multiPoseRejectedObject,multiPoseRejectedMesh,multiPoseRejectedBuffers,multiPoseRejectedBones,multiPoseRejectedPalette,paletteUploads,paletteDirtyRows,paletteCacheHits,paletteBytes,sceneDrawMs,sceneAfterMs,postProcessMs,frameworkDrawMs,shadowMs,worldBaseMs,worldObjectsMs,terrainOpaqueMs,terrainAfterMs,previewMs,previewRenders,previewCacheHits,previewCacheMisses,previewBudgetSkips,mainQueue,mainProcessed,mainThreadMs,schedulerQueue,schedulerProcessed,simulationSteps,simulationElapsedMs,simulationAlpha,cpuPercent,workingSetMb,privateMemoryMb,managedMemoryMb,threadCount,telemetryDroppedMessages,vbUpdates,ibUploads,verticesTransformed,meshesProcessed,bmdCacheHits,bmdCacheMisses,gpuMeshBuffers,gpuBatchBuffers,meshTopologies,prunedGpuMeshes,prunedGpuBatches,prunedTopologies,frameIndex,frameIntervalFrameIndex,rollingWindowStartFrameIndex,rollingWindowEndFrameIndex,rollingSampleCount,rollingSequence,cpuFrameMs,frameIntervalMs,frameIntervalCpuMs,frameIntervalUnaccountedMs,wallP50Ms,wallP95Ms,wallP99Ms,wallWorstMs,processAllocatedKb,wallFramesOver16Ms,wallFramesOver33Ms,isActive,inactiveSleepMs,isFixedTimeStep,targetElapsedMs,vSyncEnabled,mainLongestActionMs,mainLongestActionQueueMs,mainLongestActionName,mainBudgetExceeded,mainBudgetOverrunMs,latestSlowActionSequence,latestSlowActionName,latestSlowActionPriority,latestSlowActionMs,latestSlowActionQueueMs,latestSlowActionAgeMs");

        foreach (var envelope in history)
        {
            var snapshot = envelope.Snapshot;
            if (snapshot is null)
                continue;

            object?[] values =
            [
                envelope.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
                envelope.SessionId,
                snapshot.Session.Scene,
                snapshot.Session.WorldIndex,
                snapshot.Session.MapId,
                snapshot.Frame.Fps,
                snapshot.Frame.Ups,
                snapshot.Frame.UpdateMs,
                snapshot.Frame.DrawMs,
                snapshot.Frame.P50Ms,
                snapshot.Frame.P95Ms,
                snapshot.Frame.P99Ms,
                snapshot.Frame.WorstMs,
                snapshot.Frame.AllocatedKb,
                snapshot.Frame.FramesOver16Ms,
                snapshot.Frame.FramesOver33Ms,
                snapshot.Frame.Gen0Collections,
                snapshot.Frame.Gen1Collections,
                snapshot.Frame.Gen2Collections,
                snapshot.World.CullCandidates,
                snapshot.World.VisibleObjects,
                snapshot.World.CullMs,
                snapshot.World.CullWasRebuild,
                snapshot.World.ModelObjects,
                snapshot.World.SpriteObjects,
                snapshot.World.TransparentObjects,
                snapshot.World.AnimationUpdates,
                snapshot.World.AnimationSkips,
                snapshot.World.LowQualityObjects,
                snapshot.Rendering.TerrainDrawCalls,
                snapshot.Rendering.TerrainTriangles,
                snapshot.Rendering.TerrainBlocks,
                snapshot.Rendering.TerrainCells,
                snapshot.Rendering.GrassDrawCalls,
                snapshot.Rendering.RegisteredLights,
                snapshot.Rendering.ActiveLights,
                snapshot.Rendering.VisibleLights,
                snapshot.Rendering.UploadedLights,
                snapshot.Rendering.EstimatedDrawCalls,
                snapshot.Animation.GpuSkinnedMeshes,
                snapshot.Animation.CpuFallbackDrawCalls,
                snapshot.Animation.GpuBatchDrawCalls,
                snapshot.Animation.GpuBatchedMeshes,
                snapshot.Animation.SharedPaletteHits,
                snapshot.Animation.SharedPaletteMisses,
                snapshot.Animation.StaticInstancedObjects,
                snapshot.Animation.StaticMeshInstances,
                snapshot.Animation.StaticDrawCalls,
                snapshot.Animation.MultiPoseObjects,
                snapshot.Animation.MultiPoseMeshInstances,
                snapshot.Animation.MultiPoseUniquePoses,
                snapshot.Animation.MultiPoseDrawCalls,
                snapshot.Animation.MultiPoseAttempts,
                snapshot.Animation.MultiPoseQueuedObjects,
                snapshot.Animation.MultiPoseRejectedObject,
                snapshot.Animation.MultiPoseRejectedMesh,
                snapshot.Animation.MultiPoseRejectedBuffers,
                snapshot.Animation.MultiPoseRejectedBones,
                snapshot.Animation.MultiPoseRejectedPalette,
                snapshot.Animation.PaletteUploads,
                snapshot.Animation.PaletteDirtyRows,
                snapshot.Animation.PaletteCacheHits,
                snapshot.Animation.PaletteBytes,
                snapshot.Passes.SceneDrawMs,
                snapshot.Passes.SceneAfterMs,
                snapshot.Passes.PostProcessMs,
                snapshot.Passes.FrameworkDrawMs,
                snapshot.Passes.ShadowMs,
                snapshot.Passes.WorldBaseMs,
                snapshot.Passes.WorldObjectsMs,
                snapshot.Passes.TerrainOpaqueMs,
                snapshot.Passes.TerrainAfterMs,
                snapshot.Passes.PreviewMs,
                snapshot.Passes.PreviewRenders,
                snapshot.Passes.PreviewCacheHits,
                snapshot.Passes.PreviewCacheMisses,
                snapshot.Passes.PreviewBudgetSkips,
                snapshot.Runtime.MainThreadQueued,
                snapshot.Runtime.MainThreadProcessed,
                snapshot.Runtime.MainThreadMs,
                snapshot.Runtime.SchedulerQueued,
                snapshot.Runtime.SchedulerProcessed,
                snapshot.Runtime.SimulationSteps,
                snapshot.Runtime.SimulationElapsedMs,
                snapshot.Runtime.SimulationAlpha,
                snapshot.Runtime.ProcessCpuPercent,
                snapshot.Runtime.WorkingSetMb,
                snapshot.Runtime.PrivateMemoryMb,
                snapshot.Runtime.ManagedMemoryMb,
                snapshot.Runtime.ThreadCount,
                snapshot.Runtime.TelemetryDroppedMessages,
                snapshot.Assets.VertexBufferUpdates,
                snapshot.Assets.IndexBufferUploads,
                snapshot.Assets.VerticesTransformed,
                snapshot.Assets.MeshesProcessed,
                snapshot.Assets.CacheHits,
                snapshot.Assets.CacheMisses,
                snapshot.Assets.GpuMeshBuffers,
                snapshot.Assets.GpuBatchBuffers,
                snapshot.Assets.MeshTopologies,
                snapshot.Assets.PrunedGpuMeshes,
                snapshot.Assets.PrunedGpuBatches,
                snapshot.Assets.PrunedTopologies,
                snapshot.Frame.FrameIndex,
                snapshot.Frame.FrameIntervalFrameIndex,
                snapshot.Frame.RollingWindowStartFrameIndex,
                snapshot.Frame.RollingWindowEndFrameIndex,
                snapshot.Frame.RollingSampleCount,
                snapshot.Frame.RollingSequence,
                snapshot.Frame.CpuFrameMs,
                snapshot.Frame.FrameIntervalMs,
                snapshot.Frame.FrameIntervalCpuMs,
                snapshot.Frame.FrameIntervalUnaccountedMs,
                snapshot.Frame.WallP50Ms,
                snapshot.Frame.WallP95Ms,
                snapshot.Frame.WallP99Ms,
                snapshot.Frame.WallWorstMs,
                snapshot.Frame.ProcessAllocatedKb,
                snapshot.Frame.WallFramesOver16Ms,
                snapshot.Frame.WallFramesOver33Ms,
                snapshot.Frame.IsActive,
                snapshot.Frame.InactiveSleepMs,
                snapshot.Frame.IsFixedTimeStep,
                snapshot.Frame.TargetElapsedMs,
                snapshot.Frame.VSyncEnabled,
                snapshot.Runtime.MainThreadLongestActionMs,
                snapshot.Runtime.MainThreadLongestActionQueueMs,
                snapshot.Runtime.MainThreadLongestActionName,
                snapshot.Runtime.MainThreadBudgetExceeded,
                snapshot.Runtime.MainThreadBudgetOverrunMs,
                snapshot.Runtime.LatestSlowActionSequence,
                snapshot.Runtime.LatestSlowActionName,
                snapshot.Runtime.LatestSlowActionPriority,
                snapshot.Runtime.LatestSlowActionMs,
                snapshot.Runtime.LatestSlowActionQueueMs,
                snapshot.Runtime.LatestSlowActionAgeMs
            ];

            for (int i = 0; i < values.Length; i++)
                AppendCsv(sb, values[i], last: i == values.Length - 1);
        }

        return sb.ToString();
    }

    private static void AppendCsv(StringBuilder sb, object? value, bool last = false)
    {
        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        bool quote = text.IndexOfAny([',', '"', '\n', '\r']) >= 0;
        if (quote)
            sb.Append('"').Append(text.Replace("\"", "\"\"")).Append('"');
        else
            sb.Append(text);
        sb.Append(last ? '\n' : ',');
    }

    private static void TrimList(List<TelemetryEnvelope> list, int maximum)
    {
        if (list.Count <= maximum)
            return;
        int removeCount = Math.Max(1, list.Count - maximum + maximum / 10);
        list.RemoveRange(0, Math.Min(removeCount, list.Count));
    }
}

public sealed record TelemetryStatus
{
    public bool PipeConnected { get; init; }
    public string? ActiveSessionId { get; init; }
    public DateTimeOffset? ConnectedAtUtc { get; init; }
    public DateTimeOffset? LastReceivedUtc { get; init; }
    public TelemetryClientInfo? Client { get; init; }
    public TelemetryEnvelope? Latest { get; init; }
    public int SnapshotCount { get; init; }
    public int EventCount { get; init; }
}
