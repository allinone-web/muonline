using System.Text.Json;
using System.Text.Json.Serialization;

namespace Client.Telemetry;

public static class TelemetryProtocol
{
    public const int CurrentVersion = 7;
    public const string DefaultPipeName = "muonline-diagnostics-v1";

    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public enum TelemetryMessageKind
{
    Hello,
    Snapshot,
    Event,
    Goodbye
}

public enum TelemetrySeverity
{
    Trace,
    Info,
    Warning,
    Error,
    Critical
}

public sealed record TelemetryEnvelope
{
    public int ProtocolVersion { get; init; } = TelemetryProtocol.CurrentVersion;
    public TelemetryMessageKind Kind { get; init; }
    public required string SessionId { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public TelemetryClientInfo? Client { get; init; }
    public TelemetrySnapshot? Snapshot { get; init; }
    public TelemetryEvent? Event { get; init; }
}

public sealed record TelemetryClientInfo
{
    public required string ProcessName { get; init; }
    public required string MachineName { get; init; }
    public required string Framework { get; init; }
    public required string OperatingSystem { get; init; }
    public required string Architecture { get; init; }
    public required string ClientVersion { get; init; }
    public int ProcessId { get; init; }
    public int ProcessorCount { get; init; }
    public DateTimeOffset StartedUtc { get; init; }
}

public sealed record TelemetryEvent
{
    public required string Category { get; init; }
    public required string Message { get; init; }
    public TelemetrySeverity Severity { get; init; } = TelemetrySeverity.Info;
    public IReadOnlyDictionary<string, string>? Properties { get; init; }
}

public sealed record TelemetrySnapshot
{
    public required SessionTelemetry Session { get; init; }
    public required FrameTelemetry Frame { get; init; }
    public required WorldTelemetry World { get; init; }
    public required RenderingTelemetry Rendering { get; init; }
    public required AnimationTelemetry Animation { get; init; }
    public required RenderPassTelemetry Passes { get; init; }
    public required UpdatePassTelemetry UpdatePasses { get; init; }
    public required SlowFrameTelemetry SlowFrame { get; init; }
    public required RuntimeTelemetry Runtime { get; init; }
    public required AssetTelemetry Assets { get; init; }
}

public sealed record SessionTelemetry
{
    public required string Scene { get; init; }
    public string? WorldName { get; init; }
    public int? WorldIndex { get; init; }
    public int? MapId { get; init; }
    public float? PlayerX { get; init; }
    public float? PlayerY { get; init; }
    public float? PlayerZ { get; init; }
    public float? PlayerTerrainZ { get; init; }
    public float? PlayerExpectedZ { get; init; }
    public float? PlayerHeightError { get; init; }
    public float? PlayerTargetZ { get; init; }
    public float? PlayerMoveTargetZ { get; init; }
    public long FrameIndex { get; init; }
    public double UptimeSeconds { get; init; }
}

public sealed record FrameTelemetry
{
    public long FrameIndex { get; init; }
    public long FrameIntervalFrameIndex { get; init; }
    public long RollingWindowStartFrameIndex { get; init; }
    public long RollingWindowEndFrameIndex { get; init; }
    public int RollingSampleCount { get; init; }
    public long RollingSequence { get; init; }
    public double Fps { get; init; }
    public double Ups { get; init; }
    public double UpdateMs { get; init; }
    public double DrawMs { get; init; }
    public double CpuFrameMs { get; init; }
    public double FrameIntervalMs { get; init; }
    public double FrameIntervalCpuMs { get; init; }
    public double FrameIntervalUnaccountedMs { get; init; }
    public double P50Ms { get; init; }
    public double P95Ms { get; init; }
    public double P99Ms { get; init; }
    public double WorstMs { get; init; }
    public double WallP50Ms { get; init; }
    public double WallP95Ms { get; init; }
    public double WallP99Ms { get; init; }
    public double WallWorstMs { get; init; }
    public double AllocatedKb { get; init; }
    public double UpdateAllocatedKb { get; init; }
    public double DrawAllocatedKb { get; init; }
    public double ProcessAllocatedKb { get; init; }
    public int FramesOver16Ms { get; init; }
    public int FramesOver33Ms { get; init; }
    public int WallFramesOver16Ms { get; init; }
    public int WallFramesOver33Ms { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
    public bool IsActive { get; init; }
    public double InactiveSleepMs { get; init; }
    public bool IsFixedTimeStep { get; init; }
    public double TargetElapsedMs { get; init; }
    public bool VSyncEnabled { get; init; }
}

public sealed record WorldTelemetry
{
    public int CullCandidates { get; init; }
    public int VisibleObjects { get; init; }
    public double CullMs { get; init; }
    public bool CullWasRebuild { get; init; }
    public int ModelObjects { get; init; }
    public int SpriteObjects { get; init; }
    public int TransparentObjects { get; init; }
    public int DedicatedStaticMapObjects { get; init; }
    public int DedicatedParticleSystems { get; init; }
    public int ParticleSprites { get; init; }
    public int ParticleBatchBegins { get; init; }
    public int ParticleSystemsCulled { get; init; }
    public int InactiveParticleSystemsSkipped { get; init; }
    public int StaticMapUpdateSkips { get; init; }
    public int DrawAfterSkips { get; init; }
    public int AnimationUpdates { get; init; }
    public int AnimationSkips { get; init; }
    public int LowQualityObjects { get; init; }
    public double LongestObjectUpdateMs { get; init; }
    public string? LongestObjectUpdateType { get; init; }
    public string? LongestObjectUpdateName { get; init; }
    public int LongestObjectUpdateNetworkId { get; init; }
    public int RenderFailures { get; init; }
    public long LastRenderFailureSequence { get; init; }
    public int LastRenderFailureFrameIndex { get; init; }
    public string? LastRenderFailurePhase { get; init; }
    public string? LastRenderFailureType { get; init; }
    public string? LastRenderFailureName { get; init; }
    public int LastRenderFailureNetworkId { get; init; }
    public string? LastRenderFailureMessage { get; init; }
}

public sealed record RenderingTelemetry
{
    public int TerrainDrawCalls { get; init; }
    public int TerrainTriangles { get; init; }
    public int TerrainBlocks { get; init; }
    public int TerrainCells { get; init; }
    public bool TerrainIndexBatching { get; init; }
    public int TerrainIndexedCells { get; init; }
    public int TerrainStreamedCells { get; init; }
    public int TerrainIndexUploads { get; init; }
    public int TerrainVertexUploads { get; init; }
    public int TerrainUploadedIndices { get; init; }
    public int TerrainUploadedVertices { get; init; }
    public int GrassDrawCalls { get; init; }
    public int RegisteredLights { get; init; }
    public int ActiveLights { get; init; }
    public int VisibleLights { get; init; }
    public int UploadedLights { get; init; }
    public bool TerrainLightingGpu { get; init; }
    public bool ObjectLightingGpu { get; init; }
    public bool FxaaEnabled { get; init; }
    public bool AlphaRgbEnabled { get; init; }
    public int EstimatedDrawCalls { get; init; }
}

public sealed record AnimationTelemetry
{
    public bool GpuSkinningEnabled { get; init; }
    public bool GpuSkinningSupported { get; init; }
    public int GpuSkinnedMeshes { get; init; }
    public int GpuBatchDrawCalls { get; init; }
    public int GpuBatchedMeshes { get; init; }
    public bool StaticInstancingEnabled { get; init; }
    public int StaticInstancedObjects { get; init; }
    public int StaticMeshInstances { get; init; }
    public int StaticDrawCalls { get; init; }
    public int StaticInstanceUploads { get; init; }
    public int StaticInstanceUploadReuses { get; init; }
    public int StaticShadowObjects { get; init; }
    public int StaticShadowDrawCalls { get; init; }
    public int StaticShadowUploads { get; init; }
    public int StaticShadowUploadReuses { get; init; }
    public bool MultiPoseEnabled { get; init; }
    public int MultiPoseObjects { get; init; }
    public int MultiPoseMeshInstances { get; init; }
    public int MultiPoseUniquePoses { get; init; }
    public int MultiPoseDrawCalls { get; init; }
    public int PaletteUploads { get; init; }
    public int PaletteDirtyRows { get; init; }
    public int PaletteCacheHits { get; init; }
    public long PaletteBytes { get; init; }
    public int CpuFallbackDrawCalls { get; init; }
    public int SharedPaletteHits { get; init; }
    public int SharedPaletteMisses { get; init; }
    public int MultiPoseAttempts { get; init; }
    public int MultiPoseQueuedObjects { get; init; }
    public int MultiPoseRejectedObject { get; init; }
    public int MultiPoseRejectedMesh { get; init; }
    public int MultiPoseRejectedBuffers { get; init; }
    public int MultiPoseRejectedBones { get; init; }
    public int MultiPoseRejectedPalette { get; init; }
    public int MultiPoseRejectedUnsupported { get; init; }
    public int MultiPoseRejectedChildren { get; init; }
    public int MultiPoseRejectedTypeOrRenderer { get; init; }
    public int MultiPoseRejectedMutableMesh { get; init; }
    public int MultiPoseRejectedVisibility { get; init; }
    public int MultiPoseRejectedAnimation { get; init; }
    public int MultiPoseRejectedOneShot { get; init; }
    public int MultiPoseRejectedMaterial { get; init; }
}


public sealed record RenderPassTelemetry
{
    public double SceneDrawMs { get; init; }
    public double SceneDrawAllocatedKb { get; init; }
    public double SceneAfterMs { get; init; }
    public double SceneAfterAllocatedKb { get; init; }
    public double PostProcessMs { get; init; }
    public double PostProcessAllocatedKb { get; init; }
    public double FrameworkDrawMs { get; init; }
    public double FrameworkDrawAllocatedKb { get; init; }
    public double ShadowMs { get; init; }
    public double ShadowAllocatedKb { get; init; }
    public double WorldBaseMs { get; init; }
    public double WorldBaseAllocatedKb { get; init; }
    public double WorldObjectsMs { get; init; }
    public double WorldObjectsAllocatedKb { get; init; }
    public double TerrainOpaqueMs { get; init; }
    public double TerrainOpaqueAllocatedKb { get; init; }
    public double TerrainAfterMs { get; init; }
    public double TerrainAfterAllocatedKb { get; init; }
    public double PreviewMs { get; init; }
    public double PreviewAllocatedKb { get; init; }
    public int PreviewRenders { get; init; }
    public int PreviewCacheHits { get; init; }
    public int PreviewCacheMisses { get; init; }
    public int PreviewBudgetSkips { get; init; }
}


public sealed record UpdatePassTelemetry
{
    public double DispatcherMs { get; init; }
    public double GlobalMs { get; init; }
    public double SceneMs { get; init; }
    public double FrameworkMs { get; init; }
    public double UnaccountedMs { get; init; }
    public double SceneExceptionMs { get; init; }
    public double SceneInputMs { get; init; }
    public double SceneControlTreeMs { get; init; }
    public double ScenePostMs { get; init; }
    public double WorldBaseMs { get; init; }
    public double WorldInitializationMs { get; init; }
    public double WorldVisibilityMs { get; init; }
    public double WorldCullMs { get; init; }
    public double WorldHoverMs { get; init; }
    public double GameBuffsMs { get; init; }
    public double GameNotificationsMs { get; init; }
    public double GameScopePumpMs { get; init; }
    public double GameInteractionMs { get; init; }
    public double GamePlayerMenuMs { get; init; }
    public double GameSkillUpdateMs { get; init; }
    public double GameAttackInputMs { get; init; }
    public double GameRightClickSkillMs { get; init; }
    public double GameHotkeysMs { get; init; }
    public double GameHousekeepingMs { get; init; }
    public long SceneExceptionSequence { get; init; }
    public long SceneExceptionFrameIndex { get; init; }
    public string? SceneExceptionType { get; init; }
    public string? SceneExceptionMessage { get; init; }
}

public sealed record SlowFrameTelemetry
{
    public long Sequence { get; init; }
    public long FrameIndex { get; init; }
    public double AgeMs { get; init; }
    public double CpuFrameMs { get; init; }
    public double UpdateMs { get; init; }
    public double DrawMs { get; init; }
    public double AllocatedKb { get; init; }
    public double UpdateAllocatedKb { get; init; }
    public double DrawAllocatedKb { get; init; }
    public double ProcessAllocatedKb { get; init; }
    public double UpdateDispatcherMs { get; init; }
    public double UpdateGlobalMs { get; init; }
    public double UpdateSceneMs { get; init; }
    public double UpdateFrameworkMs { get; init; }
    public double SceneControlTreeMs { get; init; }
    public double ScenePostMs { get; init; }
    public double WorldInitializationMs { get; init; }
    public double WorldVisibilityMs { get; init; }
    public double WorldCullMs { get; init; }
    public double SceneDrawMs { get; init; }
    public double WorldObjectsMs { get; init; }
    public double TerrainOpaqueMs { get; init; }
    public double GameBuffsMs { get; init; }
    public double GameNotificationsMs { get; init; }
    public double GameScopePumpMs { get; init; }
    public double GameInteractionMs { get; init; }
    public double GamePlayerMenuMs { get; init; }
    public double GameSkillUpdateMs { get; init; }
    public double GameAttackInputMs { get; init; }
    public double GameRightClickSkillMs { get; init; }
    public double GameHotkeysMs { get; init; }
    public double GameHousekeepingMs { get; init; }
    public double SceneInputMs { get; init; }
    public double LongestObjectUpdateMs { get; init; }
    public string? LongestObjectUpdateType { get; init; }
    public string? LongestObjectUpdateName { get; init; }
    public int LongestObjectUpdateNetworkId { get; init; }
    public double UpdateUnaccountedMs { get; init; }
    public double SceneExceptionMs { get; init; }
    public long SceneExceptionSequence { get; init; }
    public long SceneExceptionFrameIndex { get; init; }
    public string? SceneExceptionType { get; init; }
    public string? SceneExceptionMessage { get; init; }
    public double SceneDrawAllocatedKb { get; init; }
    public double SceneAfterAllocatedKb { get; init; }
    public double PostProcessAllocatedKb { get; init; }
    public double FrameworkDrawAllocatedKb { get; init; }
    public double ShadowAllocatedKb { get; init; }
    public double WorldBaseAllocatedKb { get; init; }
    public double WorldObjectsAllocatedKb { get; init; }
    public double TerrainOpaqueAllocatedKb { get; init; }
    public double TerrainAfterAllocatedKb { get; init; }
    public double PreviewAllocatedKb { get; init; }
}

public sealed record RuntimeTelemetry
{
    public int MainThreadQueued { get; init; }
    public int MainThreadProcessed { get; init; }
    public double MainThreadMs { get; init; }
    public double MainThreadLongestActionMs { get; init; }
    public double MainThreadLongestActionQueueMs { get; init; }
    public string? MainThreadLongestActionName { get; init; }
    public bool MainThreadBudgetExceeded { get; init; }
    public double MainThreadBudgetOverrunMs { get; init; }
    public long LatestSlowActionSequence { get; init; }
    public string? LatestSlowActionName { get; init; }
    public string? LatestSlowActionPriority { get; init; }
    public double LatestSlowActionMs { get; init; }
    public double LatestSlowActionQueueMs { get; init; }
    public double LatestSlowActionAgeMs { get; init; }
    public int SchedulerQueued { get; init; }
    public int SchedulerProcessed { get; init; }
    public int SimulationSteps { get; init; }
    public double SimulationElapsedMs { get; init; }
    public double SimulationAlpha { get; init; }
    public double ProcessCpuPercent { get; init; }
    public double WorkingSetMb { get; init; }
    public double PrivateMemoryMb { get; init; }
    public double ManagedMemoryMb { get; init; }
    public int ThreadCount { get; init; }
    public long TelemetryDroppedMessages { get; init; }
    public int LastPathLength { get; init; }
    public double LastPathApplyMs { get; init; }
    public double LastPathQueueMs { get; init; }
    public double LastPathFacingMs { get; init; }
    public double LastPathBuildDirectionsMs { get; init; }
    public double LastPathSendScheduleMs { get; init; }
    public long DrawExceptionSequence { get; init; }
    public long DrawExceptionFrameIndex { get; init; }
    public double DrawExceptionAgeMs { get; init; }
    public string? DrawExceptionPhase { get; init; }
    public string? DrawExceptionType { get; init; }
    public string? DrawExceptionMessage { get; init; }
}

public sealed record AssetTelemetry
{
    public int VertexBufferUpdates { get; init; }
    public int IndexBufferUploads { get; init; }
    public int VerticesTransformed { get; init; }
    public int MeshesProcessed { get; init; }
    public int CacheHits { get; init; }
    public int CacheMisses { get; init; }
    public int CacheMissIndexUpload { get; init; }
    public int CacheMissMissingEntry { get; init; }
    public int CacheMissInvalidEntry { get; init; }
    public int CacheMissOwner { get; init; }
    public int CacheMissAsset { get; init; }
    public int CacheMissMesh { get; init; }
    public int CacheMissPose { get; init; }
    public int CacheMissVertexCount { get; init; }
    public int CacheMissColor { get; init; }
    public int CacheBypasses { get; init; }
    public string? TopCacheMissModelName { get; init; }
    public int TopCacheMissModelCount { get; init; }
    public int GpuMeshBuffers { get; init; }
    public int GpuBatchBuffers { get; init; }
    public int MeshTopologies { get; init; }
    public int PrunedGpuMeshes { get; init; }
    public int PrunedGpuBatches { get; init; }
    public int PrunedTopologies { get; init; }
}
