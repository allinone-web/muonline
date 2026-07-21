using System.Text.Json;
using System.Text.Json.Serialization;

namespace Client.Telemetry;

public static class TelemetryProtocol
{
    public const int CurrentVersion = 3;
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
    public int AnimationUpdates { get; init; }
    public int AnimationSkips { get; init; }
    public int LowQualityObjects { get; init; }
}

public sealed record RenderingTelemetry
{
    public int TerrainDrawCalls { get; init; }
    public int TerrainTriangles { get; init; }
    public int TerrainBlocks { get; init; }
    public int TerrainCells { get; init; }
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
}


public sealed record RenderPassTelemetry
{
    public double SceneDrawMs { get; init; }
    public double SceneAfterMs { get; init; }
    public double PostProcessMs { get; init; }
    public double FrameworkDrawMs { get; init; }
    public double ShadowMs { get; init; }
    public double WorldBaseMs { get; init; }
    public double WorldObjectsMs { get; init; }
    public double TerrainOpaqueMs { get; init; }
    public double TerrainAfterMs { get; init; }
    public double PreviewMs { get; init; }
    public int PreviewRenders { get; init; }
    public int PreviewCacheHits { get; init; }
    public int PreviewCacheMisses { get; init; }
    public int PreviewBudgetSkips { get; init; }
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
}

public sealed record AssetTelemetry
{
    public int VertexBufferUpdates { get; init; }
    public int IndexBufferUploads { get; init; }
    public int VerticesTransformed { get; init; }
    public int MeshesProcessed { get; init; }
    public int CacheHits { get; init; }
    public int CacheMisses { get; init; }
    public int GpuMeshBuffers { get; init; }
    public int GpuBatchBuffers { get; init; }
    public int MeshTopologies { get; init; }
    public int PrunedGpuMeshes { get; init; }
    public int PrunedGpuBatches { get; init; }
    public int PrunedTopologies { get; init; }
}
