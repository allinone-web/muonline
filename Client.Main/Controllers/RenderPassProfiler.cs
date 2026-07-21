using System.Diagnostics;

namespace Client.Main.Controllers;

/// <summary>
/// Lightweight render-thread profiler for the major CPU-side render passes.
/// It measures command preparation/submission time, not true GPU execution time.
/// </summary>
public static class RenderPassProfiler
{
    public readonly record struct Snapshot(
        double SceneDrawMs,
        double SceneAfterMs,
        double PostProcessMs,
        double FrameworkDrawMs,
        double ShadowMs,
        double WorldBaseMs,
        double WorldObjectsMs,
        double TerrainOpaqueMs,
        double TerrainAfterMs,
        double PreviewMs,
        int PreviewRenders,
        int PreviewCacheHits,
        int PreviewCacheMisses,
        int PreviewBudgetSkips);

    private static double _sceneDrawMs;
    private static double _sceneAfterMs;
    private static double _postProcessMs;
    private static double _frameworkDrawMs;
    private static double _shadowMs;
    private static double _worldBaseMs;
    private static double _worldObjectsMs;
    private static double _terrainOpaqueMs;
    private static double _terrainAfterMs;
    private static double _previewMs;
    private static int _previewRenders;
    private static int _previewCacheHits;
    private static int _previewCacheMisses;
    private static int _previewBudgetSkips;

    public static bool Enabled { get; private set; }

    public static Snapshot Current => new(
        _sceneDrawMs,
        _sceneAfterMs,
        _postProcessMs,
        _frameworkDrawMs,
        _shadowMs,
        _worldBaseMs,
        _worldObjectsMs,
        _terrainOpaqueMs,
        _terrainAfterMs,
        _previewMs,
        _previewRenders,
        _previewCacheHits,
        _previewCacheMisses,
        _previewBudgetSkips);

    public static void BeginFrame(bool enabled)
    {
        Enabled = enabled;
        if (!enabled)
            return;

        _sceneDrawMs = 0;
        _sceneAfterMs = 0;
        _postProcessMs = 0;
        _frameworkDrawMs = 0;
        _shadowMs = 0;
        _worldBaseMs = 0;
        _worldObjectsMs = 0;
        _terrainOpaqueMs = 0;
        _terrainAfterMs = 0;
        _previewMs = 0;
        _previewRenders = 0;
        _previewCacheHits = 0;
        _previewCacheMisses = 0;
        _previewBudgetSkips = 0;
    }

    public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;

    private static double ElapsedMs(long startTimestamp) =>
        Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

    public static void AddSceneDraw(long started)
    {
        if (started != 0) _sceneDrawMs += ElapsedMs(started);
    }

    public static void AddSceneAfter(long started)
    {
        if (started != 0) _sceneAfterMs += ElapsedMs(started);
    }

    public static void AddPostProcess(long started)
    {
        if (started != 0) _postProcessMs += ElapsedMs(started);
    }

    public static void AddFrameworkDraw(long started)
    {
        if (started != 0) _frameworkDrawMs += ElapsedMs(started);
    }

    public static void AddShadow(long started)
    {
        if (started != 0) _shadowMs += ElapsedMs(started);
    }

    public static void AddWorldBase(long started)
    {
        if (started != 0) _worldBaseMs += ElapsedMs(started);
    }

    public static void AddWorldObjects(long started)
    {
        if (started != 0) _worldObjectsMs += ElapsedMs(started);
    }

    public static void AddTerrainOpaque(long started)
    {
        if (started != 0) _terrainOpaqueMs += ElapsedMs(started);
    }

    public static void AddTerrainAfter(long started)
    {
        if (started != 0) _terrainAfterMs += ElapsedMs(started);
    }

    public static void RecordPreviewCacheHit()
    {
        if (Enabled)
            _previewCacheHits++;
    }

    public static void RecordPreviewCacheMiss()
    {
        if (Enabled)
            _previewCacheMisses++;
    }

    public static void RecordPreviewBudgetSkip()
    {
        if (Enabled)
            _previewBudgetSkips++;
    }

    public static void AddPreviewRender(long started)
    {
        if (started == 0)
            return;

        _previewMs += ElapsedMs(started);
        _previewRenders++;
    }
}
