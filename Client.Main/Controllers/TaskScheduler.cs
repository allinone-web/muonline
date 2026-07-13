using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Client.Main.Controllers
{
    /// <summary>
    /// Intelligent task scheduler for main thread actions with priority management and backpressure control
    /// to prevent micro-freezing during network packet processing and UI updates.
    /// </summary>
    public class TaskScheduler : IDisposable
    {
        private readonly ILogger<TaskScheduler> _logger;
        private readonly ConcurrentPriorityQueue _taskQueue;
        private readonly CancellationTokenSource _cts = new();

        // Configuration
        // Lower per-frame budget to smooth out spikes when many spawn/load tasks enqueue at once
        private readonly int _maxTasksPerFrame = 6;
        private readonly int _maxTotalQueuedTasks = 150;
        private readonly TimeSpan _maxProcessingTimePerFrame = TimeSpan.FromMilliseconds(2);

        // Statistics
        private long _processedTasks;
        private readonly Stopwatch _uptimeStopwatch = new();
        private readonly Stopwatch _frameStopwatch = new();
        private int _lastFrameProcessedTasks;
        private int _lastFrameQueueAtStart;
        private int _lastFrameQueueRemaining;
        private double _lastFrameProcessingMs;
        private long _lastBacklogWarningTimestamp;
        private long _lastQueueFullWarningTimestamp;

        // Priority levels
        public enum Priority
        {
            Critical = 0,     // Immediate processing (damage notifications, critical updates)
            High = 1,         // High priority (player movements, NPC spawns in view)
            Normal = 2,       // Standard tasks (UI updates, equipment changes)
            Low = 3,          // Background tasks (model loading, texture caching)
        }

        private sealed class TaskItem
        {
            public TaskItem(Action action, Priority priority)
            {
                Action = action;
                TaskPriority = priority;
            }

            public Action Action { get; }
            public Priority TaskPriority { get; }
        }

        public TaskScheduler(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory?.CreateLogger<TaskScheduler>() ??
                     LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TaskScheduler>();
            _taskQueue = new ConcurrentPriorityQueue();
            _uptimeStopwatch.Start();
        }

        /// <summary>
        /// Queues a task for main thread execution with specified priority
        /// </summary>
        public bool QueueTask(Action action, Priority priority = Priority.Normal)
        {
            if (action == null) return false;

            if (_taskQueue.Count >= _maxTotalQueuedTasks)
            {
                long now = Stopwatch.GetTimestamp();
                long previous = Volatile.Read(ref _lastQueueFullWarningTimestamp);
                if (previous == 0 ||
                    (Stopwatch.GetElapsedTime(previous, now) >= TimeSpan.FromSeconds(5) &&
                     Interlocked.CompareExchange(ref _lastQueueFullWarningTimestamp, now, previous) == previous))
                {
                    Volatile.Write(ref _lastQueueFullWarningTimestamp, now);
                    _logger.LogWarning("Task queue is full ({Count}). Dropping task with priority {Priority}",
                                      _taskQueue.Count, priority);
                }
                return false;
            }

            var taskItem = new TaskItem(action, priority);
            _taskQueue.Enqueue(taskItem);

            // _logger.LogDebug("Queued task with priority {Priority}. Queue size: {Count}",
            //                 priority, _taskQueue.Count);

            return true;
        }

        /// <summary>
        /// Queues an asynchronous task and observes failures explicitly.
        /// </summary>
        public bool QueueTask(Func<Task> asyncAction, Priority priority = Priority.Normal)
        {
            if (asyncAction == null) return false;

            return QueueTask(() =>
            {
                Task task = asyncAction();
                if (!task.IsCompletedSuccessfully)
                {
                    _ = ObserveTaskAsync(task, priority);
                }
            }, priority);
        }

        /// <summary>
        /// Processes queued tasks on the main thread. Should be called each frame.
        /// </summary>
        public void ProcessFrame()
        {
            if (_cts.IsCancellationRequested) return;

            _frameStopwatch.Restart();
            var processedThisFrame = 0;
            int queuedAtStart = _taskQueue.Count;
            int maxTasksThisFrame = _maxTasksPerFrame;
            TimeSpan maxProcessingTime = _maxProcessingTimePerFrame;

            // A backlog may increase the item cap, but never the fixed time budget.
            // This improves throughput without sacrificing frame-time stability.
            if (queuedAtStart > 40)
                maxTasksThisFrame = Math.Min(12, maxTasksThisFrame + queuedAtStart / 40);

            while (processedThisFrame < maxTasksThisFrame)
            {
                if (_frameStopwatch.Elapsed >= maxProcessingTime)
                {
                    _logger.LogDebug("Frame processing time limit reached ({ElapsedMs}ms). Remaining tasks: {Count}",
                                    _frameStopwatch.Elapsed.TotalMilliseconds, _taskQueue.Count);
                    break;
                }

                if (!_taskQueue.TryDequeue(out var taskItem))
                    break;

                try
                {
                    var taskStartTime = Stopwatch.GetTimestamp();
                    taskItem.Action();
                    var processingTime = Stopwatch.GetElapsedTime(taskStartTime).TotalMilliseconds;

                    processedThisFrame++;
                    Interlocked.Increment(ref _processedTasks);

                    if (processingTime > 2.0 && _logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("Slow scheduled task ({ProcessingTime:F2}ms) - Priority: {Priority}",
                                         processingTime, taskItem.TaskPriority);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing scheduled task - Priority: {Priority}",
                                    taskItem.TaskPriority);
                }
            }

            int remaining = _taskQueue.Count;
            _lastFrameProcessedTasks = processedThisFrame;
            _lastFrameQueueAtStart = queuedAtStart;
            _lastFrameQueueRemaining = remaining;
            _lastFrameProcessingMs = _frameStopwatch.Elapsed.TotalMilliseconds;
            if (remaining > 50)
            {
                long now = Stopwatch.GetTimestamp();
                long previous = Volatile.Read(ref _lastBacklogWarningTimestamp);
                if (Stopwatch.GetElapsedTime(previous, now) >= TimeSpan.FromSeconds(5) &&
                    Interlocked.CompareExchange(ref _lastBacklogWarningTimestamp, now, previous) == previous)
                {
                    _logger.LogWarning("Task queue is backing up. Current count: {Count}", remaining);
                }
            }
        }

        /// <summary>
        /// Gets the number of currently queued tasks
        /// </summary>
        public int QueuedTaskCount => _taskQueue.Count;
        public int LastFrameProcessedTasks => _lastFrameProcessedTasks;
        public int LastFrameQueueAtStart => _lastFrameQueueAtStart;
        public int LastFrameQueueRemaining => _lastFrameQueueRemaining;
        public double LastFrameProcessingMs => _lastFrameProcessingMs;
        public long TotalProcessedTasks => Interlocked.Read(ref _processedTasks);

        /// <summary>
        /// Gets statistics about task processing
        /// </summary>
        public (long ProcessedTasks, int QueuedTasks, double QueueProcessingRate) GetStatistics()
        {
            var elapsedSec = Math.Max(_uptimeStopwatch.Elapsed.TotalSeconds, 0.001);
            var rate = _processedTasks / elapsedSec;
            return (_processedTasks, _taskQueue.Count, rate);
        }

        /// <summary>
        /// Clears all queued tasks
        /// </summary>
        public void ClearQueue()
        {
            _taskQueue.Clear();
            _logger.LogInformation("Task queue cleared");
        }

        /// <summary>
        /// Disposes resources
        /// </summary>
        public void Dispose()
        {
            _cts.Cancel();
            ClearQueue();
        }

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

        // Simple concurrent priority queue implementation
        private sealed class ConcurrentPriorityQueue
        {
            private readonly object _lock = new();
            private readonly Queue<TaskItem> _criticalQueue = new();
            private readonly Queue<TaskItem> _highQueue = new();
            private readonly Queue<TaskItem> _normalQueue = new();
            private readonly Queue<TaskItem> _lowQueue = new();
            private int _count;

            public void Enqueue(TaskItem item)
            {
                lock (_lock)
                {
                    switch (item.TaskPriority)
                    {
                        case Priority.Critical:
                            _criticalQueue.Enqueue(item);
                            break;
                        case Priority.High:
                            _highQueue.Enqueue(item);
                            break;
                        case Priority.Normal:
                            _normalQueue.Enqueue(item);
                            break;
                        default:
                            _lowQueue.Enqueue(item);
                            break;
                    }

                    _count++;
                }
            }

            public bool TryDequeue(out TaskItem item)
            {
                lock (_lock)
                {
                    // Fast-path: empty
                    if (_count == 0)
                    {
                        item = default;
                        return false;
                    }

                    // Check queues by priority order
                    if (_criticalQueue.Count > 0)
                    {
                        item = _criticalQueue.Dequeue();
                        _count--;
                        return true;
                    }

                    if (_highQueue.Count > 0)
                    {
                        item = _highQueue.Dequeue();
                        _count--;
                        return true;
                    }

                    if (_normalQueue.Count > 0)
                    {
                        item = _normalQueue.Dequeue();
                        _count--;
                        return true;
                    }

                    if (_lowQueue.Count > 0)
                    {
                        item = _lowQueue.Dequeue();
                        _count--;
                        return true;
                    }

                    item = default;
                    return false;
                }
            }

            public void Clear()
            {
                lock (_lock)
                {
                    _criticalQueue.Clear();
                    _highQueue.Clear();
                    _normalQueue.Clear();
                    _lowQueue.Clear();
                    _count = 0;
                }
            }

            public int Count => Volatile.Read(ref _count);
        }
    }
}