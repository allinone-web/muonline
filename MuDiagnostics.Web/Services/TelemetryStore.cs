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
        sb.AppendLine("timestampUtc,sessionId,scene,worldIndex,mapId,fps,ups,updateMs,drawMs,p50Ms,p95Ms,p99Ms,worstMs,allocatedKb,framesOver16Ms,framesOver33Ms,gen0Collections,gen1Collections,gen2Collections,cullCandidates,visibleObjects,cullMs,cullWasRebuild,modelObjects,spriteObjects,transparentObjects,animationUpdates,animationSkips,lowQualityObjects,terrainDrawCalls,terrainTriangles,terrainBlocks,terrainCells,grassDrawCalls,registeredLights,activeLights,visibleLights,uploadedLights,estimatedDrawCalls,gpuSkinnedMeshes,cpuFallbackDrawCalls,gpuBatchDrawCalls,gpuBatchedMeshes,sharedPaletteHits,sharedPaletteMisses,staticInstancedObjects,staticMeshInstances,staticDrawCalls,multiPoseObjects,multiPoseMeshInstances,multiPosePoses,multiPoseDrawCalls,multiPoseAttempts,multiPoseQueuedObjects,multiPoseRejectedObject,multiPoseRejectedMesh,multiPoseRejectedBuffers,multiPoseRejectedBones,multiPoseRejectedPalette,paletteUploads,paletteDirtyRows,paletteCacheHits,paletteBytes,sceneDrawMs,sceneAfterMs,postProcessMs,frameworkDrawMs,shadowMs,worldBaseMs,worldObjectsMs,terrainOpaqueMs,terrainAfterMs,previewMs,previewRenders,previewCacheHits,previewCacheMisses,previewBudgetSkips,mainQueue,mainProcessed,mainThreadMs,schedulerQueue,schedulerProcessed,simulationSteps,simulationElapsedMs,simulationAlpha,cpuPercent,workingSetMb,privateMemoryMb,managedMemoryMb,threadCount,telemetryDroppedMessages,vbUpdates,ibUploads,verticesTransformed,meshesProcessed,bmdCacheHits,bmdCacheMisses,gpuMeshBuffers,gpuBatchBuffers,meshTopologies,prunedGpuMeshes,prunedGpuBatches,prunedTopologies,frameIndex,frameIntervalFrameIndex,rollingWindowStartFrameIndex,rollingWindowEndFrameIndex,rollingSampleCount,rollingSequence,cpuFrameMs,frameIntervalMs,frameIntervalCpuMs,frameIntervalUnaccountedMs,wallP50Ms,wallP95Ms,wallP99Ms,wallWorstMs,processAllocatedKb,wallFramesOver16Ms,wallFramesOver33Ms,isActive,inactiveSleepMs,isFixedTimeStep,targetElapsedMs,vSyncEnabled,mainLongestActionMs,mainLongestActionQueueMs,mainLongestActionName,mainBudgetExceeded,mainBudgetOverrunMs,latestSlowActionSequence,latestSlowActionName,latestSlowActionPriority,latestSlowActionMs,latestSlowActionQueueMs,latestSlowActionAgeMs,updateAllocatedKb,drawAllocatedKb,updateDispatcherMs,updateGlobalMs,updateSceneMs,updateFrameworkMs,sceneInputMs,sceneControlTreeMs,scenePostMs,worldUpdateBaseMs,worldInitializationMs,worldVisibilityMs,worldUpdateCullMs,worldHoverMs,multiPoseRejectedUnsupported,multiPoseRejectedChildren,multiPoseRejectedTypeOrRenderer,multiPoseRejectedMutableMesh,multiPoseRejectedVisibility,multiPoseRejectedAnimation,multiPoseRejectedOneShot,multiPoseRejectedMaterial,lastPathLength,lastPathApplyMs,lastPathQueueMs,lastPathFacingMs,lastPathBuildDirectionsMs,lastPathSendScheduleMs,bmdMissIndexUpload,bmdMissMissingEntry,bmdMissInvalidEntry,bmdMissOwner,bmdMissAsset,bmdMissMesh,bmdMissPose,bmdMissVertexCount,bmdMissColor,bmdCacheBypasses,bmdTopMissModelName,bmdTopMissModelCount,slowFrameSequence,slowFrameIndex,slowFrameAgeMs,slowFrameCpuMs,slowFrameUpdateMs,slowFrameDrawMs,slowFrameAllocatedKb,slowFrameUpdateAllocatedKb,slowFrameDrawAllocatedKb,slowFrameProcessAllocatedKb,slowUpdateDispatcherMs,slowUpdateGlobalMs,slowUpdateSceneMs,slowUpdateFrameworkMs,slowSceneControlTreeMs,slowScenePostMs,slowWorldInitializationMs,slowWorldVisibilityMs,slowWorldCullMs,slowSceneDrawMs,slowWorldObjectsMs,slowTerrainOpaqueMs,gameBuffsMs,gameNotificationsMs,gameScopePumpMs,gameInteractionMs,gameHousekeepingMs,longestObjectUpdateMs,longestObjectUpdateType,longestObjectUpdateName,longestObjectUpdateNetworkId,slowGameBuffsMs,slowGameNotificationsMs,slowGameScopePumpMs,slowGameInteractionMs,slowGameHousekeepingMs,slowLongestObjectUpdateMs,slowLongestObjectUpdateType,slowLongestObjectUpdateName,slowLongestObjectUpdateNetworkId,renderSceneDrawAllocatedKb,renderSceneAfterAllocatedKb,renderPostProcessAllocatedKb,renderFrameworkDrawAllocatedKb,renderShadowAllocatedKb,renderWorldBaseAllocatedKb,renderWorldObjectsAllocatedKb,renderTerrainOpaqueAllocatedKb,renderTerrainAfterAllocatedKb,renderPreviewAllocatedKb,updateUnaccountedMs,updateSceneExceptionMs,updateSceneExceptionSequence,updateSceneExceptionFrameIndex,updateSceneExceptionType,updateSceneExceptionMessage,slowUpdateUnaccountedMs,slowSceneExceptionMs,slowSceneExceptionSequence,slowSceneExceptionFrameIndex,slowSceneExceptionType,slowSceneExceptionMessage,slowSceneDrawAllocatedKb,slowSceneAfterAllocatedKb,slowPostProcessAllocatedKb,slowFrameworkDrawAllocatedKb,slowShadowAllocatedKb,slowWorldBaseAllocatedKb,slowWorldObjectsAllocatedKb,slowTerrainOpaqueAllocatedKb,slowTerrainAfterAllocatedKb,slowPreviewAllocatedKb,terrainIndexBatching,terrainIndexedCells,terrainStreamedCells,terrainIndexUploads,terrainVertexUploads,terrainUploadedIndices,terrainUploadedVertices,gamePlayerMenuMs,gameSkillUpdateMs,gameAttackInputMs,gameRightClickSkillMs,gameHotkeysMs,slowSceneInputMs,slowGamePlayerMenuMs,slowGameSkillUpdateMs,slowGameAttackInputMs,slowGameRightClickSkillMs,slowGameHotkeysMs,playerTerrainZ,playerExpectedZ,playerHeightError,playerTargetZ,playerMoveTargetZ,renderFailures,lastRenderFailureSequence,lastRenderFailureFrameIndex,lastRenderFailurePhase,lastRenderFailureType,lastRenderFailureName,lastRenderFailureNetworkId,lastRenderFailureMessage,drawExceptionSequence,drawExceptionFrameIndex,drawExceptionAgeMs,drawExceptionPhase,drawExceptionType,drawExceptionMessage");

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
                snapshot.Runtime.LatestSlowActionAgeMs,
                snapshot.Frame.UpdateAllocatedKb,
                snapshot.Frame.DrawAllocatedKb,
                snapshot.UpdatePasses.DispatcherMs,
                snapshot.UpdatePasses.GlobalMs,
                snapshot.UpdatePasses.SceneMs,
                snapshot.UpdatePasses.FrameworkMs,
                snapshot.UpdatePasses.SceneInputMs,
                snapshot.UpdatePasses.SceneControlTreeMs,
                snapshot.UpdatePasses.ScenePostMs,
                snapshot.UpdatePasses.WorldBaseMs,
                snapshot.UpdatePasses.WorldInitializationMs,
                snapshot.UpdatePasses.WorldVisibilityMs,
                snapshot.UpdatePasses.WorldCullMs,
                snapshot.UpdatePasses.WorldHoverMs,
                snapshot.Animation.MultiPoseRejectedUnsupported,
                snapshot.Animation.MultiPoseRejectedChildren,
                snapshot.Animation.MultiPoseRejectedTypeOrRenderer,
                snapshot.Animation.MultiPoseRejectedMutableMesh,
                snapshot.Animation.MultiPoseRejectedVisibility,
                snapshot.Animation.MultiPoseRejectedAnimation,
                snapshot.Animation.MultiPoseRejectedOneShot,
                snapshot.Animation.MultiPoseRejectedMaterial,
                snapshot.Runtime.LastPathLength,
                snapshot.Runtime.LastPathApplyMs,
                snapshot.Runtime.LastPathQueueMs,
                snapshot.Runtime.LastPathFacingMs,
                snapshot.Runtime.LastPathBuildDirectionsMs,
                snapshot.Runtime.LastPathSendScheduleMs,
                snapshot.Assets.CacheMissIndexUpload,
                snapshot.Assets.CacheMissMissingEntry,
                snapshot.Assets.CacheMissInvalidEntry,
                snapshot.Assets.CacheMissOwner,
                snapshot.Assets.CacheMissAsset,
                snapshot.Assets.CacheMissMesh,
                snapshot.Assets.CacheMissPose,
                snapshot.Assets.CacheMissVertexCount,
                snapshot.Assets.CacheMissColor,
                snapshot.Assets.CacheBypasses,
                snapshot.Assets.TopCacheMissModelName,
                snapshot.Assets.TopCacheMissModelCount,
                snapshot.SlowFrame.Sequence,
                snapshot.SlowFrame.FrameIndex,
                snapshot.SlowFrame.AgeMs,
                snapshot.SlowFrame.CpuFrameMs,
                snapshot.SlowFrame.UpdateMs,
                snapshot.SlowFrame.DrawMs,
                snapshot.SlowFrame.AllocatedKb,
                snapshot.SlowFrame.UpdateAllocatedKb,
                snapshot.SlowFrame.DrawAllocatedKb,
                snapshot.SlowFrame.ProcessAllocatedKb,
                snapshot.SlowFrame.UpdateDispatcherMs,
                snapshot.SlowFrame.UpdateGlobalMs,
                snapshot.SlowFrame.UpdateSceneMs,
                snapshot.SlowFrame.UpdateFrameworkMs,
                snapshot.SlowFrame.SceneControlTreeMs,
                snapshot.SlowFrame.ScenePostMs,
                snapshot.SlowFrame.WorldInitializationMs,
                snapshot.SlowFrame.WorldVisibilityMs,
                snapshot.SlowFrame.WorldCullMs,
                snapshot.SlowFrame.SceneDrawMs,
                snapshot.SlowFrame.WorldObjectsMs,
                snapshot.SlowFrame.TerrainOpaqueMs,
                snapshot.UpdatePasses.GameBuffsMs,
                snapshot.UpdatePasses.GameNotificationsMs,
                snapshot.UpdatePasses.GameScopePumpMs,
                snapshot.UpdatePasses.GameInteractionMs,
                snapshot.UpdatePasses.GameHousekeepingMs,
                snapshot.World.LongestObjectUpdateMs,
                snapshot.World.LongestObjectUpdateType,
                snapshot.World.LongestObjectUpdateName,
                snapshot.World.LongestObjectUpdateNetworkId,
                snapshot.SlowFrame.GameBuffsMs,
                snapshot.SlowFrame.GameNotificationsMs,
                snapshot.SlowFrame.GameScopePumpMs,
                snapshot.SlowFrame.GameInteractionMs,
                snapshot.SlowFrame.GameHousekeepingMs,
                snapshot.SlowFrame.LongestObjectUpdateMs,
                snapshot.SlowFrame.LongestObjectUpdateType,
                snapshot.SlowFrame.LongestObjectUpdateName,
                snapshot.SlowFrame.LongestObjectUpdateNetworkId,
                snapshot.Passes.SceneDrawAllocatedKb,
                snapshot.Passes.SceneAfterAllocatedKb,
                snapshot.Passes.PostProcessAllocatedKb,
                snapshot.Passes.FrameworkDrawAllocatedKb,
                snapshot.Passes.ShadowAllocatedKb,
                snapshot.Passes.WorldBaseAllocatedKb,
                snapshot.Passes.WorldObjectsAllocatedKb,
                snapshot.Passes.TerrainOpaqueAllocatedKb,
                snapshot.Passes.TerrainAfterAllocatedKb,
                snapshot.Passes.PreviewAllocatedKb,
                snapshot.UpdatePasses.UnaccountedMs,
                snapshot.UpdatePasses.SceneExceptionMs,
                snapshot.UpdatePasses.SceneExceptionSequence,
                snapshot.UpdatePasses.SceneExceptionFrameIndex,
                snapshot.UpdatePasses.SceneExceptionType,
                snapshot.UpdatePasses.SceneExceptionMessage,
                snapshot.SlowFrame.UpdateUnaccountedMs,
                snapshot.SlowFrame.SceneExceptionMs,
                snapshot.SlowFrame.SceneExceptionSequence,
                snapshot.SlowFrame.SceneExceptionFrameIndex,
                snapshot.SlowFrame.SceneExceptionType,
                snapshot.SlowFrame.SceneExceptionMessage,
                snapshot.SlowFrame.SceneDrawAllocatedKb,
                snapshot.SlowFrame.SceneAfterAllocatedKb,
                snapshot.SlowFrame.PostProcessAllocatedKb,
                snapshot.SlowFrame.FrameworkDrawAllocatedKb,
                snapshot.SlowFrame.ShadowAllocatedKb,
                snapshot.SlowFrame.WorldBaseAllocatedKb,
                snapshot.SlowFrame.WorldObjectsAllocatedKb,
                snapshot.SlowFrame.TerrainOpaqueAllocatedKb,
                snapshot.SlowFrame.TerrainAfterAllocatedKb,
                snapshot.SlowFrame.PreviewAllocatedKb,
                snapshot.Rendering.TerrainIndexBatching,
                snapshot.Rendering.TerrainIndexedCells,
                snapshot.Rendering.TerrainStreamedCells,
                snapshot.Rendering.TerrainIndexUploads,
                snapshot.Rendering.TerrainVertexUploads,
                snapshot.Rendering.TerrainUploadedIndices,
                snapshot.Rendering.TerrainUploadedVertices,
                snapshot.UpdatePasses.GamePlayerMenuMs,
                snapshot.UpdatePasses.GameSkillUpdateMs,
                snapshot.UpdatePasses.GameAttackInputMs,
                snapshot.UpdatePasses.GameRightClickSkillMs,
                snapshot.UpdatePasses.GameHotkeysMs,
                snapshot.SlowFrame.SceneInputMs,
                snapshot.SlowFrame.GamePlayerMenuMs,
                snapshot.SlowFrame.GameSkillUpdateMs,
                snapshot.SlowFrame.GameAttackInputMs,
                snapshot.SlowFrame.GameRightClickSkillMs,
                snapshot.SlowFrame.GameHotkeysMs,
                snapshot.Session.PlayerTerrainZ,
                snapshot.Session.PlayerExpectedZ,
                snapshot.Session.PlayerHeightError,
                snapshot.Session.PlayerTargetZ,
                snapshot.Session.PlayerMoveTargetZ,
                snapshot.World.RenderFailures,
                snapshot.World.LastRenderFailureSequence,
                snapshot.World.LastRenderFailureFrameIndex,
                snapshot.World.LastRenderFailurePhase,
                snapshot.World.LastRenderFailureType,
                snapshot.World.LastRenderFailureName,
                snapshot.World.LastRenderFailureNetworkId,
                snapshot.World.LastRenderFailureMessage,
                snapshot.Runtime.DrawExceptionSequence,
                snapshot.Runtime.DrawExceptionFrameIndex,
                snapshot.Runtime.DrawExceptionAgeMs,
                snapshot.Runtime.DrawExceptionPhase,
                snapshot.Runtime.DrawExceptionType,
                snapshot.Runtime.DrawExceptionMessage
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
