using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace Client.Main.Controllers
{
    /// <summary>
    /// Single prioritized main-thread work queue shared by UI/network dispatch and
    /// budgeted engine tasks. A single time budget prevents independent queues from
    /// jointly overrunning the frame.
    /// </summary>
    public sealed class MainThreadDispatcher
    {
        public enum WorkPriority
        {
            Critical = 0,
            High = 1,
            Normal = 2,
            Low = 3,
        }

        private interface IDispatchedAction
        {
            void Invoke(ILogger logger);
        }

        private sealed class SyncAction : IDispatchedAction
        {
            private readonly Action _action;

            public SyncAction(Action action) => _action = action;

            public void Invoke(ILogger logger) => _action();
        }

        private sealed class StatefulAction<TState> : IDispatchedAction
        {
            private readonly Action<TState> _action;
            private readonly TState _state;

            public StatefulAction(Action<TState> action, TState state)
            {
                _action = action;
                _state = state;
            }

            public void Invoke(ILogger logger) => _action(_state);
        }

        private sealed class AsyncAction : IDispatchedAction
        {
            private readonly Func<Task> _action;

            public AsyncAction(Func<Task> action) => _action = action;

            public void Invoke(ILogger logger)
            {
                Task task = _action();
                if (!task.IsCompletedSuccessfully)
                    _ = ObserveAsync(task, logger);
            }

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

        private readonly ConcurrentQueue<IDispatchedAction>[] _queues =
        {
            new(), new(), new(), new()
        };

        private readonly int _maxActionsPerFrame;
        private readonly TimeSpan _maxActionTimePerFrame;
        private ILogger _logger;
        private int _pendingCount;

        public int LastProcessedCount { get; private set; }
        public double LastProcessDurationMs { get; private set; }
        public long TotalProcessedCount { get; private set; }

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

        public void Enqueue(Action action, WorkPriority priority = WorkPriority.Normal)
        {
            if (action == null)
                return;

            EnqueueCore(new SyncAction(action), priority);
        }

        public void Enqueue<TState>(Action<TState> action, TState state, WorkPriority priority = WorkPriority.Normal)
        {
            if (action == null)
                return;

            EnqueueCore(new StatefulAction<TState>(action, state), priority);
        }

        public void Enqueue(Func<Task> action, WorkPriority priority = WorkPriority.Normal)
        {
            if (action == null)
                return;

            EnqueueCore(new AsyncAction(action), priority);
        }

        private void EnqueueCore(IDispatchedAction action, WorkPriority priority)
        {
            int index = Math.Clamp((int)priority, 0, _queues.Length - 1);
            _queues[index].Enqueue(action);
            Interlocked.Increment(ref _pendingCount);
        }

        public int ProcessPending()
            => ProcessPending(_maxActionsPerFrame, _maxActionTimePerFrame);

        public int ProcessPending(int maxActions, TimeSpan maxTime)
        {
            if (PendingCount == 0)
            {
                LastProcessedCount = 0;
                LastProcessDurationMs = 0;
                return 0;
            }

            maxActions = Math.Max(1, maxActions);
            if (maxTime <= TimeSpan.Zero)
                maxTime = TimeSpan.FromMilliseconds(1);

            int processed = 0;
            long frameStart = Stopwatch.GetTimestamp();

            while (processed < maxActions && TryDequeue(out var action))
            {
                try
                {
                    action.Invoke(_logger);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error executing main-thread scheduled action.");
                }

                processed++;
                if (Stopwatch.GetElapsedTime(frameStart) >= maxTime)
                    break;
            }

            LastProcessedCount = processed;
            LastProcessDurationMs = Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;
            TotalProcessedCount += processed;
            return processed;
        }

        private bool TryDequeue(out IDispatchedAction action)
        {
            for (int i = 0; i < _queues.Length; i++)
            {
                if (_queues[i].TryDequeue(out action))
                {
                    Interlocked.Decrement(ref _pendingCount);
                    return true;
                }
            }

            action = null;
            return false;
        }
    }
}
