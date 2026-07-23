using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading;

namespace Client.Main.Controllers
{
    /// <summary>
    /// Compatibility facade for prioritized engine tasks. Work is executed by the shared
    /// <see cref="MainThreadDispatcher"/>, so UI/network dispatch and engine loading use
    /// one deterministic frame budget instead of two competing budgets.
    /// </summary>
    public sealed class TaskScheduler : IDisposable
    {
        private readonly ILogger<TaskScheduler> _logger;
        private readonly MainThreadDispatcher _dispatcher;
        private readonly Stopwatch _uptime = Stopwatch.StartNew();
        private const int MaxTotalQueuedTasks = 150;

        private long _processedTasks;
        private int _queuedTasks;
        private int _processedThisFrame;
        private int _lastFrameProcessedTasks;
        private int _lastFrameQueueAtStart;
        private int _lastFrameQueueRemaining;
        private double _lastFrameProcessingMs;
        private long _lastQueueFullWarningTimestamp;
        private int _generation;
        private bool _disposed;

        public enum Priority
        {
            Critical = 0,
            High = 1,
            Normal = 2,
            Low = 3,
        }

        public TaskScheduler(ILoggerFactory loggerFactory, MainThreadDispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _logger = loggerFactory?.CreateLogger<TaskScheduler>() ??
                      LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TaskScheduler>();
        }

        public bool QueueTask(Action action, Priority priority = Priority.Normal, string name = null)
        {
            if (action == null || _disposed)
                return false;

            if (!TryReserveQueueSlot(out int queued))
            {
                LogQueueFull(priority, queued);
                return false;
            }

            int generation = Volatile.Read(ref _generation);
            try
            {
                _dispatcher.Enqueue(
                    () => Execute(action, priority, generation),
                    MapPriority(priority),
                    name ?? ResolveDelegateName(action));
                return true;
            }
            catch
            {
                Interlocked.Decrement(ref _queuedTasks);
                throw;
            }
        }

        public bool QueueTask(Func<Task> asyncAction, Priority priority = Priority.Normal, string name = null)
        {
            if (asyncAction == null)
                return false;

            return QueueTask(() =>
            {
                Task task = asyncAction();
                if (!task.IsCompletedSuccessfully)
                    _ = ObserveTaskAsync(task, priority);
            }, priority, name ?? ResolveDelegateName(asyncAction));
        }

        internal void BeginFrame()
        {
            _processedThisFrame = 0;
            _lastFrameQueueAtStart = QueuedTaskCount;
        }

        internal void EndFrame(double sharedProcessingMs)
        {
            _lastFrameProcessedTasks = Volatile.Read(ref _processedThisFrame);
            _lastFrameQueueRemaining = QueuedTaskCount;
            _lastFrameProcessingMs = sharedProcessingMs;
        }

        private void Execute(Action action, Priority priority, int generation)
        {
            try
            {
                if (_disposed || generation != Volatile.Read(ref _generation))
                    return;

                long start = Stopwatch.GetTimestamp();
                action();
                double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (elapsedMs > 2.0 && _logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Slow scheduled task ({ProcessingTime:F2}ms) - Priority: {Priority}",
                        elapsedMs,
                        priority);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing scheduled task - Priority: {Priority}", priority);
            }
            finally
            {
                if (generation == Volatile.Read(ref _generation))
                    Interlocked.Decrement(ref _queuedTasks);

                Interlocked.Increment(ref _processedThisFrame);
                Interlocked.Increment(ref _processedTasks);
            }
        }

        private bool TryReserveQueueSlot(out int observedCount)
        {
            while (true)
            {
                observedCount = Volatile.Read(ref _queuedTasks);
                if (observedCount >= MaxTotalQueuedTasks)
                    return false;

                if (Interlocked.CompareExchange(ref _queuedTasks, observedCount + 1, observedCount) == observedCount)
                    return true;
            }
        }

        private void LogQueueFull(Priority priority, int count)
        {
            long now = Stopwatch.GetTimestamp();
            long previous = Volatile.Read(ref _lastQueueFullWarningTimestamp);
            if (previous != 0 && Stopwatch.GetElapsedTime(previous, now) < TimeSpan.FromSeconds(5))
                return;

            if (Interlocked.CompareExchange(ref _lastQueueFullWarningTimestamp, now, previous) == previous)
            {
                _logger.LogWarning("Task queue is full ({Count}). Dropping task with priority {Priority}",
                    count,
                    priority);
            }
        }

        public int QueuedTaskCount => Math.Max(0, Volatile.Read(ref _queuedTasks));
        public int LastFrameProcessedTasks => _lastFrameProcessedTasks;
        public int LastFrameQueueAtStart => _lastFrameQueueAtStart;
        public int LastFrameQueueRemaining => _lastFrameQueueRemaining;
        public double LastFrameProcessingMs => _lastFrameProcessingMs;
        public long TotalProcessedTasks => Interlocked.Read(ref _processedTasks);

        public (long ProcessedTasks, int QueuedTasks, double QueueProcessingRate) GetStatistics()
        {
            double seconds = Math.Max(0.001, _uptime.Elapsed.TotalSeconds);
            return (TotalProcessedTasks, QueuedTaskCount, TotalProcessedTasks / seconds);
        }

        public void ClearQueue()
        {
            Interlocked.Increment(ref _generation);
            Interlocked.Exchange(ref _queuedTasks, 0);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            ClearQueue();
        }

        private static string ResolveDelegateName(Delegate action)
        {
            string methodName = action.Method.Name;
            string declaringType = action.Method.DeclaringType?.Name;
            return string.IsNullOrEmpty(declaringType)
                ? methodName
                : $"{declaringType}.{methodName}";
        }

        private static MainThreadDispatcher.WorkPriority MapPriority(Priority priority)
            => priority switch
            {
                Priority.Critical => MainThreadDispatcher.WorkPriority.Critical,
                Priority.High => MainThreadDispatcher.WorkPriority.High,
                Priority.Low => MainThreadDispatcher.WorkPriority.Low,
                _ => MainThreadDispatcher.WorkPriority.Normal,
            };

        private async Task ObserveTaskAsync(Task task, Priority priority)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing async scheduled task - Priority: {Priority}", priority);
            }
        }
    }
}
