using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Client.Main.Controllers
{
    /// <summary>
    /// Single main-thread dispatch queue with deterministic per-frame budgets and runtime metrics.
    /// </summary>
    public sealed class MainThreadDispatcher
    {
        private interface IDispatchedAction
        {
            void Invoke(ILogger logger);
        }

        private sealed class SyncAction : IDispatchedAction
        {
            private readonly Action _action;

            public SyncAction(Action action)
            {
                _action = action;
            }

            public void Invoke(ILogger logger)
            {
                _action();
            }
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

            public void Invoke(ILogger logger)
            {
                _action(_state);
            }
        }

        private sealed class AsyncAction : IDispatchedAction
        {
            private readonly Func<Task> _action;

            public AsyncAction(Func<Task> action)
            {
                _action = action;
            }

            public void Invoke(ILogger logger)
            {
                Task task = _action();
                if (!task.IsCompletedSuccessfully)
                {
                    _ = ObserveAsync(task, logger);
                }
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

        private readonly ConcurrentQueue<IDispatchedAction> _queue = new();
        private readonly int _maxActionsPerFrame;
        private readonly TimeSpan _maxActionTimePerFrame;
        private ILogger _logger;

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

        public int PendingCount => _queue.Count;

        public void SetLogger(ILogger logger)
        {
            _logger = logger;
        }

        public void Enqueue(Action action)
        {
            if (action == null)
                return;

            _queue.Enqueue(new SyncAction(action));
        }

        public void Enqueue<TState>(Action<TState> action, TState state)
        {
            if (action == null)
                return;

            _queue.Enqueue(new StatefulAction<TState>(action, state));
        }

        public void Enqueue(Func<Task> action)
        {
            if (action == null)
                return;

            _queue.Enqueue(new AsyncAction(action));
        }

        public int ProcessPending()
        {
            if (_queue.IsEmpty)
            {
                LastProcessedCount = 0;
                LastProcessDurationMs = 0;
                return 0;
            }

            int maxActions = _maxActionsPerFrame;
            TimeSpan maxTime = _maxActionTimePerFrame;

            int processed = 0;
            long frameStart = Stopwatch.GetTimestamp();

            while (processed < maxActions && _queue.TryDequeue(out var action))
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
    }
}
