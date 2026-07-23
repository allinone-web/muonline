using System.Diagnostics;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Telemetry;

namespace Client.Main.Diagnostics;

internal sealed class TelemetrySnapshotBuilder : IDisposable
{
    private const int ProcessMetricSampleIntervalMs = 1000;
    private readonly long _startedTimestamp = Stopwatch.GetTimestamp();
    private readonly object _processMetricSync = new();
    private long _lastProcessorTicks;
    private long _lastCpuTimestamp;
    private long _nextProcessMetricTimestamp;
    private int _processMetricRefreshScheduled;
    private int _disposed;
    private double _lastCpuPercent;
    private double _lastWorkingSetMb;
    private double _lastPrivateMemoryMb;
    private int _lastThreadCount;

    public TelemetrySnapshotBuilder()
    {
        InitializeProcessMetrics();
    }

    public TelemetrySnapshot Build(MuGame game, long telemetryDroppedMessages)
    {
        ArgumentNullException.ThrowIfNull(game);

        var frame = MuGame.FramePerformance;
        var world = game.ActiveScene?.World;
        var walkableWorld = world as WalkableWorldControl;
        var worldMetrics = world?.FrameMetrics;
        var terrain = world?.Terrain;
        var terrainMetrics = terrain?.FrameMetrics;
        var bmd = BMDLoader.Instance;
        var passes = RenderPassProfiler.Current;
        var updatePasses = UpdatePassProfiler.Current;
        var slowFrame = MuGame.LatestSlowFrame;
        var slowAction = MuGame.MainThreadLatestSlowAction;
        var drawException = MuGame.LatestDrawException;
        var player = walkableWorld?.Walker;

        float? playerTerrainZ = null;
        float? playerExpectedZ = null;
        float? playerHeightError = null;
        float? playerTargetZ = null;
        float? playerMoveTargetZ = player?.MoveTargetPosition.Z;
        if (player != null && terrain?.Status == GameControlStatus.Ready)
        {
            float terrainZ = terrain.RequestTerrainHeight(player.Position.X, player.Position.Y);
            float expectedZ = terrainZ + walkableWorld.ExtraHeight + player.ExtraHeight;
            playerTerrainZ = terrainZ;
            playerExpectedZ = expectedZ;
            playerHeightError = player.Position.Z - expectedZ;
            playerTargetZ = player.TargetPosition.Z;
        }

        ScheduleProcessMetricRefresh();

        // GpuSkinnedMeshes counts mesh instances, including meshes emitted through
        // batching and hardware instancing. Subtract those instances to estimate the
        // remaining one-draw-per-mesh path. The previous formula omitted this dominant
        // cost and reported about 19 draws while Lorencia was issuing over 200.
        int individuallyDrawnGpuSkinnedMeshes = Math.Max(
            0,
            ModelObject.LastFrameGpuSkinnedMeshesDrawn
            - ModelObject.LastFrameGpuSkinnedBatchedMeshes
            - ModelObject.LastFrameStaticMapInstancedMeshInstances
            - ModelObject.LastFrameWalkerCrowdMultiPoseMeshInstances);

        int estimatedDrawCalls = (terrainMetrics?.DrawCalls ?? 0)
            + individuallyDrawnGpuSkinnedMeshes
            + ModelObject.LastFrameGpuSkinnedBatchDrawCalls
            + ModelObject.LastFrameStaticMapInstancedDrawCalls
            + ModelObject.LastFrameWalkerCrowdMultiPoseDrawCalls
            + ModelObject.LastFrameModelFallbackDrawCalls;

        return new TelemetrySnapshot
        {
            Session = new SessionTelemetry
            {
                Scene = game.ActiveScene?.GetType().Name ?? "None",
                WorldName = world is null ? null : $"World {world.WorldIndex}",
                WorldIndex = world?.WorldIndex,
                MapId = world?.MapId,
                PlayerX = player?.Position.X,
                PlayerY = player?.Position.Y,
                PlayerZ = player?.Position.Z,
                PlayerTerrainZ = playerTerrainZ,
                PlayerExpectedZ = playerExpectedZ,
                PlayerHeightError = playerHeightError,
                PlayerTargetZ = playerTargetZ,
                PlayerMoveTargetZ = playerMoveTargetZ,
                FrameIndex = MuGame.FrameIndex,
                UptimeSeconds = Stopwatch.GetElapsedTime(_startedTimestamp).TotalSeconds
            },
            Frame = new FrameTelemetry
            {
                FrameIndex = frame.FrameIndex,
                FrameIntervalFrameIndex = frame.FrameIntervalFrameIndex,
                RollingWindowStartFrameIndex = frame.RollingWindowStartFrameIndex,
                RollingWindowEndFrameIndex = frame.RollingWindowEndFrameIndex,
                RollingSampleCount = frame.RollingSampleCount,
                RollingSequence = frame.RollingSequence,
                Fps = FPSCounter.Instance.FPS_AVG,
                Ups = UPSCounter.Instance.UPS_AVG,
                UpdateMs = frame.UpdateMs,
                DrawMs = frame.DrawMs,
                CpuFrameMs = frame.CpuFrameMs,
                FrameIntervalMs = frame.FrameIntervalMs,
                FrameIntervalCpuMs = frame.FrameIntervalCpuMs,
                FrameIntervalUnaccountedMs = frame.FrameIntervalUnaccountedMs,
                P50Ms = frame.P50Ms,
                P95Ms = frame.P95Ms,
                P99Ms = frame.P99Ms,
                WorstMs = frame.WorstMs,
                WallP50Ms = frame.WallP50Ms,
                WallP95Ms = frame.WallP95Ms,
                WallP99Ms = frame.WallP99Ms,
                WallWorstMs = frame.WallWorstMs,
                AllocatedKb = frame.AllocatedKb,
                UpdateAllocatedKb = frame.UpdateAllocatedKb,
                DrawAllocatedKb = frame.DrawAllocatedKb,
                ProcessAllocatedKb = frame.ProcessAllocatedKb,
                FramesOver16Ms = frame.FramesOver16Ms,
                FramesOver33Ms = frame.FramesOver33Ms,
                WallFramesOver16Ms = frame.WallFramesOver16Ms,
                WallFramesOver33Ms = frame.WallFramesOver33Ms,
                Gen0Collections = frame.Gen0Collections,
                Gen1Collections = frame.Gen1Collections,
                Gen2Collections = frame.Gen2Collections,
                IsActive = game.IsActive,
                InactiveSleepMs = game.InactiveSleepTime.TotalMilliseconds,
                IsFixedTimeStep = game.IsFixedTimeStep,
                TargetElapsedMs = game.TargetElapsedTime.TotalMilliseconds,
                VSyncEnabled = game.IsVSyncEnabled
            },
            World = new WorldTelemetry
            {
                CullCandidates = worldMetrics?.CullCandidates ?? 0,
                VisibleObjects = worldMetrics?.VisibleObjects ?? 0,
                CullMs = worldMetrics?.CullMs ?? 0,
                CullWasRebuild = worldMetrics?.CullWasRebuild ?? false,
                ModelObjects = worldMetrics?.ModelObjects ?? 0,
                SpriteObjects = worldMetrics?.SpriteBatchObjects ?? 0,
                TransparentObjects = worldMetrics?.TransparentObjects ?? 0,
                AnimationUpdates = worldMetrics?.AnimationUpdates ?? 0,
                AnimationSkips = worldMetrics?.AnimationSkips ?? 0,
                LowQualityObjects = worldMetrics?.LowQualityObjects ?? 0,
                LongestObjectUpdateMs = worldMetrics?.LongestObjectUpdateMs ?? 0d,
                LongestObjectUpdateType = worldMetrics?.LongestObjectUpdateType,
                LongestObjectUpdateName = worldMetrics?.LongestObjectUpdateName,
                LongestObjectUpdateNetworkId = worldMetrics?.LongestObjectUpdateNetworkId ?? 0,
                RenderFailures = worldMetrics?.RenderFailures ?? 0,
                LastRenderFailureSequence = worldMetrics?.LastRenderFailureSequence ?? 0,
                LastRenderFailureFrameIndex = worldMetrics?.LastRenderFailureFrameIndex ?? 0,
                LastRenderFailurePhase = worldMetrics?.LastRenderFailurePhase,
                LastRenderFailureType = worldMetrics?.LastRenderFailureType,
                LastRenderFailureName = worldMetrics?.LastRenderFailureName,
                LastRenderFailureNetworkId = worldMetrics?.LastRenderFailureNetworkId ?? 0,
                LastRenderFailureMessage = worldMetrics?.LastRenderFailureMessage
            },
            Rendering = new RenderingTelemetry
            {
                TerrainDrawCalls = terrainMetrics?.DrawCalls ?? 0,
                TerrainTriangles = terrainMetrics?.DrawnTriangles ?? 0,
                TerrainBlocks = terrainMetrics?.DrawnBlocks ?? 0,
                TerrainCells = terrainMetrics?.DrawnCells ?? 0,
                TerrainIndexBatching = terrainMetrics?.UsedIndexBatching ?? false,
                TerrainIndexedCells = terrainMetrics?.IndexedCells ?? 0,
                TerrainStreamedCells = terrainMetrics?.StreamedCells ?? 0,
                TerrainIndexUploads = terrainMetrics?.IndexUploads ?? 0,
                TerrainVertexUploads = terrainMetrics?.VertexUploads ?? 0,
                TerrainUploadedIndices = terrainMetrics?.UploadedIndices ?? 0,
                TerrainUploadedVertices = terrainMetrics?.UploadedVertices ?? 0,
                GrassDrawCalls = terrainMetrics?.GrassFlushes ?? 0,
                RegisteredLights = terrain?.LastFrameRegisteredDynamicLights ?? 0,
                ActiveLights = terrain?.LastFrameActiveDynamicLights ?? 0,
                VisibleLights = terrain?.LastFrameVisibleDynamicLights ?? 0,
                UploadedLights = terrain?.LastUploadedDynamicLights ?? 0,
                TerrainLightingGpu = terrain?.IsGpuTerrainLighting == true,
                ObjectLightingGpu = Constants.ENABLE_DYNAMIC_LIGHTING_SHADER && GraphicsManager.Instance.DynamicLightingEffect != null,
                FxaaEnabled = GraphicsManager.Instance.IsFXAAEnabled,
                AlphaRgbEnabled = GraphicsManager.Instance.IsAlphaRGBEnabled,
                EstimatedDrawCalls = estimatedDrawCalls
            },
            Animation = new AnimationTelemetry
            {
                GpuSkinningEnabled = Constants.ENABLE_GPU_SKINNING,
                GpuSkinningSupported = ModelObject.IsGpuSkinningBackendSupported,
                GpuSkinnedMeshes = ModelObject.LastFrameGpuSkinnedMeshesDrawn,
                GpuBatchDrawCalls = ModelObject.LastFrameGpuSkinnedBatchDrawCalls,
                GpuBatchedMeshes = ModelObject.LastFrameGpuSkinnedBatchedMeshes,
                StaticInstancingEnabled = Constants.ENABLE_MAP_OBJECT_INSTANCING && ModelObject.IsStaticMapInstancingBackendSupported,
                StaticInstancedObjects = ModelObject.LastFrameStaticMapInstancedObjects,
                StaticMeshInstances = ModelObject.LastFrameStaticMapInstancedMeshInstances,
                StaticDrawCalls = ModelObject.LastFrameStaticMapInstancedDrawCalls,
                MultiPoseEnabled = ModelObject.IsWalkerCrowdMultiPoseActive,
                MultiPoseObjects = ModelObject.LastFrameWalkerCrowdMultiPoseObjects,
                MultiPoseMeshInstances = ModelObject.LastFrameWalkerCrowdMultiPoseMeshInstances,
                MultiPoseUniquePoses = ModelObject.LastFrameWalkerCrowdMultiPoseUniquePoses,
                MultiPoseDrawCalls = ModelObject.LastFrameWalkerCrowdMultiPoseDrawCalls,
                PaletteUploads = ModelObject.LastFrameWalkerCrowdMultiPosePaletteUploads,
                PaletteDirtyRows = ModelObject.LastFrameWalkerCrowdMultiPoseDirtyRows,
                PaletteCacheHits = ModelObject.LastFrameWalkerCrowdMultiPosePaletteCacheHits,
                PaletteBytes = ModelObject.LastFrameWalkerCrowdMultiPosePaletteBytes,
                CpuFallbackDrawCalls = ModelObject.LastFrameModelFallbackDrawCalls,
                SharedPaletteHits = ModelObject.LastFrameSharedAnimationPaletteHits,
                SharedPaletteMisses = ModelObject.LastFrameSharedAnimationPaletteMisses,
                MultiPoseAttempts = ModelObject.LastFrameWalkerCrowdMultiPoseAttempts,
                MultiPoseQueuedObjects = ModelObject.LastFrameWalkerCrowdMultiPoseQueuedObjects,
                MultiPoseRejectedObject = ModelObject.LastFrameWalkerCrowdMultiPoseRejectedObject,
                MultiPoseRejectedMesh = ModelObject.LastFrameWalkerCrowdMultiPoseRejectedMesh,
                MultiPoseRejectedBuffers = ModelObject.LastFrameWalkerCrowdMultiPoseRejectedBuffers,
                MultiPoseRejectedBones = ModelObject.LastFrameWalkerCrowdMultiPoseRejectedBones,
                MultiPoseRejectedPalette = ModelObject.LastFrameWalkerCrowdMultiPoseRejectedPalette,
                MultiPoseRejectedUnsupported = ModelObject.LastFrameWalkerCrowdMultiPoseRejectedUnsupported,
                MultiPoseRejectedChildren = ModelObject.LastFrameWalkerCrowdMultiPoseRejectedChildren,
                MultiPoseRejectedTypeOrRenderer = ModelObject.LastFrameWalkerCrowdMultiPoseRejectedTypeOrRenderer,
                MultiPoseRejectedMutableMesh = ModelObject.LastFrameWalkerCrowdMultiPoseRejectedMutableMesh,
                MultiPoseRejectedVisibility = ModelObject.LastFrameWalkerCrowdMultiPoseRejectedVisibility,
                MultiPoseRejectedAnimation = ModelObject.LastFrameWalkerCrowdMultiPoseRejectedAnimation,
                MultiPoseRejectedOneShot = ModelObject.LastFrameWalkerCrowdMultiPoseRejectedOneShot,
                MultiPoseRejectedMaterial = ModelObject.LastFrameWalkerCrowdMultiPoseRejectedMaterial
            },
            Passes = new RenderPassTelemetry
            {
                SceneDrawMs = passes.SceneDrawMs,
                SceneDrawAllocatedKb = passes.SceneDrawAllocatedKb,
                SceneAfterMs = passes.SceneAfterMs,
                SceneAfterAllocatedKb = passes.SceneAfterAllocatedKb,
                PostProcessMs = passes.PostProcessMs,
                PostProcessAllocatedKb = passes.PostProcessAllocatedKb,
                FrameworkDrawMs = passes.FrameworkDrawMs,
                FrameworkDrawAllocatedKb = passes.FrameworkDrawAllocatedKb,
                ShadowMs = passes.ShadowMs,
                ShadowAllocatedKb = passes.ShadowAllocatedKb,
                WorldBaseMs = passes.WorldBaseMs,
                WorldBaseAllocatedKb = passes.WorldBaseAllocatedKb,
                WorldObjectsMs = passes.WorldObjectsMs,
                WorldObjectsAllocatedKb = passes.WorldObjectsAllocatedKb,
                TerrainOpaqueMs = passes.TerrainOpaqueMs,
                TerrainOpaqueAllocatedKb = passes.TerrainOpaqueAllocatedKb,
                TerrainAfterMs = passes.TerrainAfterMs,
                TerrainAfterAllocatedKb = passes.TerrainAfterAllocatedKb,
                PreviewMs = passes.PreviewMs,
                PreviewAllocatedKb = passes.PreviewAllocatedKb,
                PreviewRenders = passes.PreviewRenders,
                PreviewCacheHits = passes.PreviewCacheHits,
                PreviewCacheMisses = passes.PreviewCacheMisses,
                PreviewBudgetSkips = passes.PreviewBudgetSkips
            },
            UpdatePasses = new UpdatePassTelemetry
            {
                DispatcherMs = updatePasses.DispatcherMs,
                GlobalMs = updatePasses.GlobalMs,
                SceneMs = updatePasses.SceneMs,
                FrameworkMs = updatePasses.FrameworkMs,
                UnaccountedMs = updatePasses.UnaccountedMs,
                SceneExceptionMs = updatePasses.SceneExceptionMs,
                SceneInputMs = updatePasses.SceneInputMs,
                SceneControlTreeMs = updatePasses.SceneControlTreeMs,
                ScenePostMs = updatePasses.ScenePostMs,
                WorldBaseMs = updatePasses.WorldBaseMs,
                WorldInitializationMs = updatePasses.WorldInitializationMs,
                WorldVisibilityMs = updatePasses.WorldVisibilityMs,
                WorldCullMs = updatePasses.WorldCullMs,
                WorldHoverMs = updatePasses.WorldHoverMs,
                GameBuffsMs = updatePasses.GameBuffsMs,
                GameNotificationsMs = updatePasses.GameNotificationsMs,
                GameScopePumpMs = updatePasses.GameScopePumpMs,
                GameInteractionMs = updatePasses.GameInteractionMs,
                GamePlayerMenuMs = updatePasses.GamePlayerMenuMs,
                GameSkillUpdateMs = updatePasses.GameSkillUpdateMs,
                GameAttackInputMs = updatePasses.GameAttackInputMs,
                GameRightClickSkillMs = updatePasses.GameRightClickSkillMs,
                GameHotkeysMs = updatePasses.GameHotkeysMs,
                GameHousekeepingMs = updatePasses.GameHousekeepingMs,
                SceneExceptionSequence = updatePasses.SceneExceptionSequence,
                SceneExceptionFrameIndex = updatePasses.SceneExceptionFrameIndex,
                SceneExceptionType = updatePasses.SceneExceptionType,
                SceneExceptionMessage = updatePasses.SceneExceptionMessage
            },
            SlowFrame = new SlowFrameTelemetry
            {
                Sequence = slowFrame.Sequence,
                FrameIndex = slowFrame.FrameIndex,
                AgeMs = slowFrame.Sequence > 0
                    ? Stopwatch.GetElapsedTime(slowFrame.ObservedTimestamp).TotalMilliseconds
                    : 0d,
                CpuFrameMs = slowFrame.CpuFrameMs,
                UpdateMs = slowFrame.UpdateMs,
                DrawMs = slowFrame.DrawMs,
                AllocatedKb = slowFrame.AllocatedKb,
                UpdateAllocatedKb = slowFrame.UpdateAllocatedKb,
                DrawAllocatedKb = slowFrame.DrawAllocatedKb,
                ProcessAllocatedKb = slowFrame.ProcessAllocatedKb,
                UpdateDispatcherMs = slowFrame.UpdatePasses.DispatcherMs,
                UpdateGlobalMs = slowFrame.UpdatePasses.GlobalMs,
                UpdateSceneMs = slowFrame.UpdatePasses.SceneMs,
                UpdateFrameworkMs = slowFrame.UpdatePasses.FrameworkMs,
                SceneControlTreeMs = slowFrame.UpdatePasses.SceneControlTreeMs,
                ScenePostMs = slowFrame.UpdatePasses.ScenePostMs,
                WorldInitializationMs = slowFrame.UpdatePasses.WorldInitializationMs,
                WorldVisibilityMs = slowFrame.UpdatePasses.WorldVisibilityMs,
                WorldCullMs = slowFrame.UpdatePasses.WorldCullMs,
                SceneDrawMs = slowFrame.RenderPasses.SceneDrawMs,
                WorldObjectsMs = slowFrame.RenderPasses.WorldObjectsMs,
                TerrainOpaqueMs = slowFrame.RenderPasses.TerrainOpaqueMs,
                GameBuffsMs = slowFrame.UpdatePasses.GameBuffsMs,
                GameNotificationsMs = slowFrame.UpdatePasses.GameNotificationsMs,
                GameScopePumpMs = slowFrame.UpdatePasses.GameScopePumpMs,
                GameInteractionMs = slowFrame.UpdatePasses.GameInteractionMs,
                GamePlayerMenuMs = slowFrame.UpdatePasses.GamePlayerMenuMs,
                GameSkillUpdateMs = slowFrame.UpdatePasses.GameSkillUpdateMs,
                GameAttackInputMs = slowFrame.UpdatePasses.GameAttackInputMs,
                GameRightClickSkillMs = slowFrame.UpdatePasses.GameRightClickSkillMs,
                GameHotkeysMs = slowFrame.UpdatePasses.GameHotkeysMs,
                GameHousekeepingMs = slowFrame.UpdatePasses.GameHousekeepingMs,
                SceneInputMs = slowFrame.UpdatePasses.SceneInputMs,
                LongestObjectUpdateMs = slowFrame.LongestObjectUpdateMs,
                LongestObjectUpdateType = slowFrame.LongestObjectUpdateType,
                LongestObjectUpdateName = slowFrame.LongestObjectUpdateName,
                LongestObjectUpdateNetworkId = slowFrame.LongestObjectUpdateNetworkId,
                UpdateUnaccountedMs = slowFrame.UpdatePasses.UnaccountedMs,
                SceneExceptionMs = slowFrame.UpdatePasses.SceneExceptionMs,
                SceneExceptionSequence = slowFrame.UpdatePasses.SceneExceptionSequence,
                SceneExceptionFrameIndex = slowFrame.UpdatePasses.SceneExceptionFrameIndex,
                SceneExceptionType = slowFrame.UpdatePasses.SceneExceptionType,
                SceneExceptionMessage = slowFrame.UpdatePasses.SceneExceptionMessage,
                SceneDrawAllocatedKb = slowFrame.RenderPasses.SceneDrawAllocatedKb,
                SceneAfterAllocatedKb = slowFrame.RenderPasses.SceneAfterAllocatedKb,
                PostProcessAllocatedKb = slowFrame.RenderPasses.PostProcessAllocatedKb,
                FrameworkDrawAllocatedKb = slowFrame.RenderPasses.FrameworkDrawAllocatedKb,
                ShadowAllocatedKb = slowFrame.RenderPasses.ShadowAllocatedKb,
                WorldBaseAllocatedKb = slowFrame.RenderPasses.WorldBaseAllocatedKb,
                WorldObjectsAllocatedKb = slowFrame.RenderPasses.WorldObjectsAllocatedKb,
                TerrainOpaqueAllocatedKb = slowFrame.RenderPasses.TerrainOpaqueAllocatedKb,
                TerrainAfterAllocatedKb = slowFrame.RenderPasses.TerrainAfterAllocatedKb,
                PreviewAllocatedKb = slowFrame.RenderPasses.PreviewAllocatedKb
            },
            Runtime = new RuntimeTelemetry
            {
                MainThreadQueued = MuGame.MainThreadPendingActions,
                MainThreadProcessed = MuGame.MainThreadProcessedActionsLastFrame,
                MainThreadMs = MuGame.MainThreadProcessingMs,
                MainThreadLongestActionMs = MuGame.MainThreadLongestActionMs,
                MainThreadLongestActionQueueMs = MuGame.MainThreadLongestActionQueueMs,
                MainThreadLongestActionName = MuGame.MainThreadLongestActionName,
                MainThreadBudgetExceeded = MuGame.MainThreadBudgetExceeded,
                MainThreadBudgetOverrunMs = MuGame.MainThreadBudgetOverrunMs,
                LatestSlowActionSequence = slowAction.Sequence,
                LatestSlowActionName = slowAction.Name,
                LatestSlowActionPriority = slowAction.Sequence > 0 ? slowAction.Priority.ToString() : null,
                LatestSlowActionMs = slowAction.DurationMs,
                LatestSlowActionQueueMs = slowAction.QueueMs,
                LatestSlowActionAgeMs = slowAction.Sequence > 0
                    ? Stopwatch.GetElapsedTime(slowAction.ObservedTimestamp).TotalMilliseconds
                    : 0d,
                SchedulerQueued = MuGame.TaskScheduler?.QueuedTaskCount ?? 0,
                SchedulerProcessed = MuGame.TaskScheduler?.LastFrameProcessedTasks ?? 0,
                SimulationSteps = MuGame.LastSimulationStepCount,
                SimulationElapsedMs = MuGame.LastSimulationAcceptedElapsedMs,
                SimulationAlpha = MuGame.LastSimulationAccumulationAlpha,
                ProcessCpuPercent = Volatile.Read(ref _lastCpuPercent),
                WorkingSetMb = Volatile.Read(ref _lastWorkingSetMb),
                PrivateMemoryMb = Volatile.Read(ref _lastPrivateMemoryMb),
                ManagedMemoryMb = GC.GetTotalMemory(false) / 1048576d,
                ThreadCount = Volatile.Read(ref _lastThreadCount),
                TelemetryDroppedMessages = telemetryDroppedMessages,
                LastPathLength = WalkerObject.LastPathLength,
                LastPathApplyMs = WalkerObject.LastPathApplyMs,
                LastPathQueueMs = WalkerObject.LastPathQueueMs,
                LastPathFacingMs = WalkerObject.LastPathFacingMs,
                LastPathBuildDirectionsMs = WalkerObject.LastPathBuildDirectionsMs,
                LastPathSendScheduleMs = WalkerObject.LastPathSendScheduleMs,
                DrawExceptionSequence = drawException.Sequence,
                DrawExceptionFrameIndex = drawException.FrameIndex,
                DrawExceptionAgeMs = drawException.Sequence > 0
                    ? Stopwatch.GetElapsedTime(drawException.ObservedTimestamp).TotalMilliseconds
                    : 0d,
                DrawExceptionPhase = drawException.Phase,
                DrawExceptionType = drawException.ExceptionType,
                DrawExceptionMessage = drawException.Message
            },
            Assets = new AssetTelemetry
            {
                VertexBufferUpdates = bmd.LastFrameVBUpdates,
                IndexBufferUploads = bmd.LastFrameIBUploads,
                VerticesTransformed = bmd.LastFrameVerticesTransformed,
                MeshesProcessed = bmd.LastFrameMeshesProcessed,
                CacheHits = bmd.LastFrameCacheHits,
                CacheMisses = bmd.LastFrameCacheMisses,
                CacheMissIndexUpload = bmd.LastFrameCacheMissIndexUpload,
                CacheMissMissingEntry = bmd.LastFrameCacheMissMissingEntry,
                CacheMissInvalidEntry = bmd.LastFrameCacheMissInvalidEntry,
                CacheMissOwner = bmd.LastFrameCacheMissOwner,
                CacheMissAsset = bmd.LastFrameCacheMissAsset,
                CacheMissMesh = bmd.LastFrameCacheMissMesh,
                CacheMissPose = bmd.LastFrameCacheMissPose,
                CacheMissVertexCount = bmd.LastFrameCacheMissVertexCount,
                CacheMissColor = bmd.LastFrameCacheMissColor,
                CacheBypasses = bmd.LastFrameCacheBypasses,
                TopCacheMissModelName = bmd.LastFrameTopCacheMissModelName,
                TopCacheMissModelCount = bmd.LastFrameTopCacheMissModelCount,
                GpuMeshBuffers = bmd.GpuMeshBufferCacheCount,
                GpuBatchBuffers = bmd.GpuBatchBufferCacheCount,
                MeshTopologies = bmd.MeshTopologyCacheCount,
                PrunedGpuMeshes = bmd.LastFrameGpuMeshBuffersPruned,
                PrunedGpuBatches = bmd.LastFrameGpuBatchBuffersPruned,
                PrunedTopologies = bmd.LastFrameMeshTopologiesPruned
            }
        };
    }

    private void InitializeProcessMetrics()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            long now = Stopwatch.GetTimestamp();
            _lastProcessorTicks = process.TotalProcessorTime.Ticks;
            _lastCpuTimestamp = now;
            _nextProcessMetricTimestamp = now + GetProcessMetricIntervalTicks();
            Volatile.Write(ref _lastWorkingSetMb, process.WorkingSet64 / 1048576d);
            Volatile.Write(ref _lastPrivateMemoryMb, process.PrivateMemorySize64 / 1048576d);
            Volatile.Write(ref _lastThreadCount, process.Threads.Count);
        }
        catch
        {
            _lastCpuTimestamp = Stopwatch.GetTimestamp();
            _nextProcessMetricTimestamp = _lastCpuTimestamp + GetProcessMetricIntervalTicks();
        }
    }

    private void ScheduleProcessMetricRefresh()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        long now = Stopwatch.GetTimestamp();
        if (now < Volatile.Read(ref _nextProcessMetricTimestamp))
            return;

        if (Interlocked.CompareExchange(ref _processMetricRefreshScheduled, 1, 0) != 0)
            return;

        Volatile.Write(ref _nextProcessMetricTimestamp, now + GetProcessMetricIntervalTicks());
        _ = Task.Run(RefreshProcessMetricsWorker);
    }

    private void RefreshProcessMetricsWorker()
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            using var process = Process.GetCurrentProcess();
            process.Refresh();

            long now = Stopwatch.GetTimestamp();
            long processorTicks = process.TotalProcessorTime.Ticks;
            double cpuPercent;

            lock (_processMetricSync)
            {
                double elapsedMs = Stopwatch.GetElapsedTime(_lastCpuTimestamp, now).TotalMilliseconds;
                double processorMs = TimeSpan.FromTicks(processorTicks - _lastProcessorTicks).TotalMilliseconds;
                cpuPercent = elapsedMs > 0d
                    ? Math.Clamp(
                        processorMs / (elapsedMs * Math.Max(1, Environment.ProcessorCount)) * 100d,
                        0d,
                        100d)
                    : _lastCpuPercent;

                _lastProcessorTicks = processorTicks;
                _lastCpuTimestamp = now;
            }

            Volatile.Write(ref _lastCpuPercent, cpuPercent);
            Volatile.Write(ref _lastWorkingSetMb, process.WorkingSet64 / 1048576d);
            Volatile.Write(ref _lastPrivateMemoryMb, process.PrivateMemorySize64 / 1048576d);
            Volatile.Write(ref _lastThreadCount, process.Threads.Count);
        }
        catch
        {
            // Diagnostics must never affect the game loop. Keep the previous sample.
        }
        finally
        {
            Interlocked.Exchange(ref _processMetricRefreshScheduled, 0);
        }
    }

    private static long GetProcessMetricIntervalTicks() =>
        Math.Max(1L, (long)(ProcessMetricSampleIntervalMs / 1000d * Stopwatch.Frequency));

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
