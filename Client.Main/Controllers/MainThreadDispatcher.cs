using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace Client.Main.Controllers
{
    /// <summary>
    /// Single prioritized main-thread work queue shared by UI/network dispatch and
    /// budgeted engine tasks. A single time budget prevents independent queues from
    /// jointly overrunning the frame. Individual actions cannot be preempted, therefore
    /// slow-action diagnostics are retained so oversized work can be split at its source.
    /// </summary>
    public sealed class MainThreadDispatcher
    {
        private const double SlowActionWarningThresholdMs = 8d;

        public enum WorkPriority
        {
            Critical = 0,
            High = 1,
            Normal = 2,
            Low = 3,
        }

        public readonly record struct SlowActionSnapshot(
            long Sequence,
            string Name,
            WorkPriority Priority,
            double DurationMs,
            double QueueMs,
            long ObservedTimestamp);

        private interface IDispatchedAction
        {
            void Invoke(ILogger logger);
            string GetDisplayName();
        }

        private sealed class SyncAction : IDispatchedAction
        {
            private readonly Action _action;
            private readonly string _name;

            public SyncAction(Action action, string name)
            {
                _action = action;
                _name = string.IsNullOrWhiteSpace(name) ? ResolveDelegateName(action) : name;
            }

            public void Invoke(ILogger logger) => _action();
            public string GetDisplayName() => _name;
        }

        private sealed class StatefulAction<TState> : IDispatchedAction
        {
            private readonly Action<TState> _action;
            private readonly TState _state;
            private readonly string _name;

            public StatefulAction(Action<TState> action, TState state, string name)
            {
                _action = action;
                _state = state;
                _name = string.IsNullOrWhiteSpace(name) ? ResolveDelegateName(action) : name;
            }

            public void Invoke(ILogger logger) => _action(_state);
            public string GetDisplayName() => _name;
        }

        private sealed class AsyncAction : IDispatchedAction
        {
            private readonly Func<Task> _action;
            private readonly string _name;

            public AsyncAction(Func<Task> action, string name)
            {
                _action = action;
                _name = string.IsNullOrWhiteSpace(name) ? ResolveDelegateName(action) : name;
            }

            public void Invoke(ILogger logger)
            {
                Task task = _action();
                if (!task.IsCompletedSuccessfully)
                    _ = ObserveAsync(task, logger);
            }

            public string GetDisplayName() => _name;

            private static async Task ObserveAsync(Task task, ILogger logger)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Error executing async main-thread scheduled action.");
                }
            }
        }

        private readonly record struct QueuedAction(
            IDispatchedAction Action,
            WorkPriority Priority,
            long EnqueuedTimestamp);

        private readonly ConcurrentQueue<QueuedAction>[] _queues =
        {
            new(), new(), new(), new()
        };

        // Continuations which must not run in the same Update that scheduled them. They are
        // promoted exactly once at the beginning of the next ProcessPending call, preventing
        // async scene-loading phases from chaining together inside one dispatcher action.
        private readonly ConcurrentQueue<QueuedAction>[] _nextFrameQueues =
        {
            new(), new(), new(), new()
        };

        private readonly int _maxActionsPerFrame;
        private readonly TimeSpan _maxActionTimePerFrame;
        private ILogger _logger;
        private int _pendingCount;
        private long _slowActionSequence;
        private SlowActionSnapshot _latestSlowAction;

        public int LastProcessedCount { get; private set; }
        public double LastProcessDurationMs { get; private set; }
        public long TotalProcessedCount { get; private set; }
        public double LastLongestActionDurationMs { get; private set; }
        public double LastLongestActionQueueMs { get; private set; }
        public string LastLongestActionName { get; private set; } = string.Empty;
        public bool LastBudgetExceeded { get; private set; }
        public double LastBudgetOverrunMs { get; private set; }
        public SlowActionSnapshot LatestSlowAction => _latestSlowAction;

        public MainThreadDispatcher(ILogger logger, int maxActionsPerFrame, TimeSpan maxActionTimePerFrame)
        {
            _logger = logger;
            _maxActionsPerFrame = Math.Max(1, maxActionsPerFrame);
            _maxActionTimePerFrame = maxActionTimePerFrame <= TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(1)
                : maxActionTimePerFrame;
        }

        public int PendingCount => Math.Max(0, Volatile.Read(ref _pendingCount));

        public void SetLogger(ILogger logger) => _logger = logger;

        public void Enqueue(
            Action action,
            WorkPriority priority = WorkPriority.Normal,
            string name = null)
        {
            if (action == null)
                return;

            EnqueueCore(new SyncAction(action, name), priority);
        }

        public void Enqueue<TState>(
            Action<TState> action,
            TState state,
            WorkPriority priority = WorkPriority.Normal,
            string name = null)
        {
            if (action == null)
                return;

            EnqueueCore(new StatefulAction<TState>(action, state, name), priority);
        }

        public void Enqueue(
            Func<Task> action,
            WorkPriority priority = WorkPriority.Normal,
            string name = null)
        {
            if (action == null)
                return;

            EnqueueCore(new AsyncAction(action, name), priority);
        }

        public void EnqueueNextFrame(
            Action action,
            WorkPriority priority = WorkPriority.Normal,
            string name = null)
        {
            if (action == null)
                return;

            EnqueueNextFrameCore(new SyncAction(action, name), priority);
        }

        private void EnqueueCore(IDispatchedAction action, WorkPriority priority)
        {
            int index = Math.Clamp((int)priority, 0, _queues.Length - 1);
            var normalizedPriority = (WorkPriority)index;
            _queues[index].Enqueue(new QueuedAction(action, normalizedPriority, Stopwatch.GetTimestamp()));
            Interlocked.Increment(ref _pendingCount);
        }

        private void EnqueueNextFrameCore(IDispatchedAction action, WorkPriority priority)
        {
            int index = Math.Clamp((int)priority, 0, _nextFrameQueues.Length - 1);
            var normalizedPriority = (WorkPriority)index;
            _nextFrameQueues[index].Enqueue(new QueuedAction(action, normalizedPriority, Stopwatch.GetTimestamp()));
            Interlocked.Increment(ref _pendingCount);
        }

        public int ProcessPending()
            => ProcessPending(_maxActionsPerFrame, _maxActionTimePerFrame);

        public int ProcessPending(int maxActions, TimeSpan maxTime)
        {
            ResetLastFrameMetrics();
            PromoteNextFrameActions();
            if (PendingCount == 0)
                return 0;

            maxActions = Math.Max(1, maxActions);
            if (maxTime <= TimeSpan.Zero)
                maxTime = TimeSpan.FromMilliseconds(1);

            int processed = 0;
            long frameStart = Stopwatch.GetTimestamp();

            while (processed < maxActions && TryDequeue(out var queued))
            {
                long actionStarted = Stopwatch.GetTimestamp();
                double queueMs = Stopwatch.GetElapsedTime(queued.EnqueuedTimestamp, actionStarted).TotalMilliseconds;

                try
                {
                    queued.Action.Invoke(_logger);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error executing main-thread scheduled action.");
                }

                double actionDurationMs = Stopwatch.GetElapsedTime(actionStarted).TotalMilliseconds;
                processed++;
                RecordActionMetrics(queued, actionDurationMs, queueMs);

                if (Stopwatch.GetElapsedTime(frameStart) >= maxTime)
                    break;
            }

            LastProcessedCount = processed;
            LastProcessDurationMs = Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;
            LastBudgetExceeded = LastProcessDurationMs > maxTime.TotalMilliseconds;
            LastBudgetOverrunMs = Math.Max(0d, LastProcessDurationMs - maxTime.TotalMilliseconds);
            TotalProcessedCount += processed;
            return processed;
        }

        private void ResetLastFrameMetrics()
        {
            LastProcessedCount = 0;
            LastProcessDurationMs = 0d;
            LastLongestActionDurationMs = 0d;
            LastLongestActionQueueMs = 0d;
            LastLongestActionName = string.Empty;
            LastBudgetExceeded = false;
            LastBudgetOverrunMs = 0d;
        }

        private void RecordActionMetrics(QueuedAction queued, double durationMs, double queueMs)
        {
            if (durationMs > LastLongestActionDurationMs)
            {
                LastLongestActionDurationMs = durationMs;
                LastLongestActionQueueMs = queueMs;
                LastLongestActionName = queued.Action.GetDisplayName();
            }

            if (durationMs < SlowActionWarningThresholdMs)
                return;

            string name = queued.Action.GetDisplayName();
            long sequence = Interlocked.Increment(ref _slowActionSequence);
            _latestSlowAction = new SlowActionSnapshot(
                sequence,
                name,
                queued.Priority,
                durationMs,
                queueMs,
                Stopwatch.GetTimestamp());

            _logger?.LogWarning(
                "Slow main-thread action {ActionName} ({Priority}) took {DurationMs:F2} ms after {QueueMs:F2} ms in queue.",
                name,
                queued.Priority,
                durationMs,
                queueMs);
        }

        private void PromoteNextFrameActions()
        {
            for (int i = 0; i < _nextFrameQueues.Length; i++)
            {
                while (_nextFrameQueues[i].TryDequeue(out QueuedAction queued))
                    _queues[i].Enqueue(queued);
            }
        }

        private bool TryDequeue(out QueuedAction action)
        {
            for (int i = 0; i < _queues.Length; i++)
            {
                if (_queues[i].TryDequeue(out action))
                {
                    Interlocked.Decrement(ref _pendingCount);
                    return true;
                }
            }

            action = default;
            return false;
        }

        private static string ResolveDelegateName(Delegate action)
        {
            string methodName = action.Method.Name;
            string declaringType = action.Method.DeclaringType?.Name;
            return string.IsNullOrEmpty(declaringType)
                ? methodName
                : $"{declaringType}.{methodName}";
        }
    }
}
