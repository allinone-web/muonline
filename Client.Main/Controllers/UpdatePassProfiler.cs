using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Client.Main.Controllers;

/// <summary>
/// Allocation-free CPU profiler for the major Update-loop stages. The profiler is intentionally
/// coarse-grained: its purpose is to locate a slow subsystem in runtime telemetry before using a
/// sampling profiler for method-level investigation.
/// </summary>
public static class UpdatePassProfiler
{
    public readonly record struct Snapshot(
        double DispatcherMs,
        double GlobalMs,
        double SceneMs,
        double FrameworkMs,
        double UnaccountedMs,
        double SceneExceptionMs,
        double SceneInputMs,
        double SceneControlTreeMs,
        double ScenePostMs,
        double WorldBaseMs,
        double WorldInitializationMs,
        double WorldVisibilityMs,
        double WorldCullMs,
        double WorldHoverMs,
        double GameBuffsMs,
        double GameNotificationsMs,
        double GameScopePumpMs,
        double GameInteractionMs,
        double GamePlayerMenuMs,
        double GameSkillUpdateMs,
        double GameAttackInputMs,
        double GameRightClickSkillMs,
        double GameHotkeysMs,
        double GameHousekeepingMs,
        long SceneExceptionSequence,
        long SceneExceptionFrameIndex,
        string SceneExceptionType,
        string SceneExceptionMessage);

#if PERFORMANCE_RELEASE
    public static Snapshot Current => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BeginFrame(bool enabled = true) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Start() => 0L;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddDispatcher(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddGlobal(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddScene(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddFramework(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddSceneException(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddSceneInput(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddSceneControlTree(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddScenePost(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddWorldBase(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddWorldInitialization(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddWorldVisibility(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddWorldCull(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddWorldHover(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddGameBuffs(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddGameNotifications(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddGameScopePump(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddGameInteraction(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddGamePlayerMenu(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddGameSkillUpdate(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddGameAttackInput(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddGameRightClickSkill(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddGameHotkeys(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddGameHousekeeping(long started) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordSceneException(Exception exception, long frameIndex) { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EndFrame(double measuredUpdateMs) { }
#else
    private static bool _enabled;
    private static double _dispatcherMs;
    private static double _globalMs;
    private static double _sceneMs;
    private static double _frameworkMs;
    private static double _unaccountedMs;
    private static double _sceneExceptionMs;
    private static double _sceneInputMs;
    private static double _sceneControlTreeMs;
    private static double _scenePostMs;
    private static double _worldBaseMs;
    private static double _worldInitializationMs;
    private static double _worldVisibilityMs;
    private static double _worldCullMs;
    private static double _worldHoverMs;
    private static double _gameBuffsMs;
    private static double _gameNotificationsMs;
    private static double _gameScopePumpMs;
    private static double _gameInteractionMs;
    private static double _gamePlayerMenuMs;
    private static double _gameSkillUpdateMs;
    private static double _gameAttackInputMs;
    private static double _gameRightClickSkillMs;
    private static double _gameHotkeysMs;
    private static double _gameHousekeepingMs;
    private static long _sceneExceptionSequence;
    private static long _latestSceneExceptionFrameIndex;
    private static string _latestSceneExceptionType = string.Empty;
    private static string _latestSceneExceptionMessage = string.Empty;

    public static Snapshot Current { get; private set; }

    public static void BeginFrame(bool enabled = true)
    {
        _enabled = enabled;
        _dispatcherMs = 0d;
        _globalMs = 0d;
        _sceneMs = 0d;
        _frameworkMs = 0d;
        _unaccountedMs = 0d;
        _sceneExceptionMs = 0d;
        _sceneInputMs = 0d;
        _sceneControlTreeMs = 0d;
        _scenePostMs = 0d;
        _worldBaseMs = 0d;
        _worldInitializationMs = 0d;
        _worldVisibilityMs = 0d;
        _worldCullMs = 0d;
        _worldHoverMs = 0d;
        _gameBuffsMs = 0d;
        _gameNotificationsMs = 0d;
        _gameScopePumpMs = 0d;
        _gameInteractionMs = 0d;
        _gamePlayerMenuMs = 0d;
        _gameSkillUpdateMs = 0d;
        _gameAttackInputMs = 0d;
        _gameRightClickSkillMs = 0d;
        _gameHotkeysMs = 0d;
        _gameHousekeepingMs = 0d;
        Current = default;
    }

    public static long Start() => _enabled ? Stopwatch.GetTimestamp() : 0L;

    public static void AddDispatcher(long started) => _dispatcherMs += Elapsed(started);
    public static void AddGlobal(long started) => _globalMs += Elapsed(started);
    public static void AddScene(long started) => _sceneMs += Elapsed(started);
    public static void AddFramework(long started) => _frameworkMs += Elapsed(started);
    public static void AddSceneException(long started) => _sceneExceptionMs += Elapsed(started);
    public static void AddSceneInput(long started) => _sceneInputMs += Elapsed(started);
    public static void AddSceneControlTree(long started) => _sceneControlTreeMs += Elapsed(started);
    public static void AddScenePost(long started) => _scenePostMs += Elapsed(started);
    public static void AddWorldBase(long started) => _worldBaseMs += Elapsed(started);
    public static void AddWorldInitialization(long started) => _worldInitializationMs += Elapsed(started);
    public static void AddWorldVisibility(long started) => _worldVisibilityMs += Elapsed(started);
    public static void AddWorldCull(long started) => _worldCullMs += Elapsed(started);
    public static void AddWorldHover(long started) => _worldHoverMs += Elapsed(started);
    public static void AddGameBuffs(long started) => _gameBuffsMs += Elapsed(started);
    public static void AddGameNotifications(long started) => _gameNotificationsMs += Elapsed(started);
    public static void AddGameScopePump(long started) => _gameScopePumpMs += Elapsed(started);
    public static void AddGameInteraction(long started) => _gameInteractionMs += Elapsed(started);
    public static void AddGamePlayerMenu(long started) => _gamePlayerMenuMs += Elapsed(started);
    public static void AddGameSkillUpdate(long started) => _gameSkillUpdateMs += Elapsed(started);
    public static void AddGameAttackInput(long started) => _gameAttackInputMs += Elapsed(started);
    public static void AddGameRightClickSkill(long started) => _gameRightClickSkillMs += Elapsed(started);
    public static void AddGameHotkeys(long started) => _gameHotkeysMs += Elapsed(started);
    public static void AddGameHousekeeping(long started) => _gameHousekeepingMs += Elapsed(started);

    public static void RecordSceneException(Exception exception, long frameIndex)
    {
        if (exception == null)
            return;

        Interlocked.Increment(ref _sceneExceptionSequence);
        Volatile.Write(ref _latestSceneExceptionFrameIndex, frameIndex);
        Volatile.Write(ref _latestSceneExceptionType, exception.GetType().FullName ?? exception.GetType().Name);
        string message = exception.Message ?? string.Empty;
        if (message.Length > 512)
            message = message[..512];
        Volatile.Write(ref _latestSceneExceptionMessage, message);
    }

    public static void EndFrame(double measuredUpdateMs)
    {
        double accounted = _dispatcherMs + _globalMs + _sceneMs + _frameworkMs;
        _unaccountedMs = Math.Max(0d, measuredUpdateMs - accounted);

        Current = new Snapshot(
            _dispatcherMs,
            _globalMs,
            _sceneMs,
            _frameworkMs,
            _unaccountedMs,
            _sceneExceptionMs,
            _sceneInputMs,
            _sceneControlTreeMs,
            _scenePostMs,
            _worldBaseMs,
            _worldInitializationMs,
            _worldVisibilityMs,
            _worldCullMs,
            _worldHoverMs,
            _gameBuffsMs,
            _gameNotificationsMs,
            _gameScopePumpMs,
            _gameInteractionMs,
            _gamePlayerMenuMs,
            _gameSkillUpdateMs,
            _gameAttackInputMs,
            _gameRightClickSkillMs,
            _gameHotkeysMs,
            _gameHousekeepingMs,
            Volatile.Read(ref _sceneExceptionSequence),
            Volatile.Read(ref _latestSceneExceptionFrameIndex),
            Volatile.Read(ref _latestSceneExceptionType) ?? string.Empty,
            Volatile.Read(ref _latestSceneExceptionMessage) ?? string.Empty);
    }

    private static double Elapsed(long started)
    {
        return !_enabled || started == 0L
            ? 0d
            : Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
#endif
}
