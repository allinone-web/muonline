using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Client.Main.Graphics
{
    /// <summary>
    /// Reuses color-only UI render targets to avoid repeated GPU allocations when windows are
    /// opened, resized, or invalidated. Targets are returned only after a short frame delay so
    /// DirectX never receives a resource that may still be referenced by an in-flight draw.
    /// </summary>
    public static class UiRenderTargetPool
    {
        private const int MaxTargetsPerSize = 4;
        private const long MaxPooledPixels = 12L * 1024L * 1024L;
        private const int MinFramesBeforeReuse = 4;
        private const int MaxIdleFrames = 1800;
        private const int PruneIntervalFrames = 120;

        private readonly record struct TargetKey(int Width, int Height);

        private readonly struct PoolEntry
        {
            public PoolEntry(RenderTarget2D target, int returnedFrame)
            {
                Target = target;
                ReturnedFrame = returnedFrame;
            }

            public RenderTarget2D Target { get; }
            public int ReturnedFrame { get; }
        }

        private static readonly Dictionary<TargetKey, Queue<PoolEntry>> _pools = new();
        private static GraphicsDevice _graphicsDevice;
        private static int _lastPruneFrame;
        private static long _pooledPixels;

        public static RenderTarget2D Rent(GraphicsDevice graphicsDevice, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(graphicsDevice);
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            EnsureGraphicsDevice(graphicsDevice);
            PruneIfNeeded();

            var key = new TargetKey(width, height);
            if (_pools.TryGetValue(key, out var queue))
            {
                int currentFrame = MuGame.FrameIndex;
                int attempts = queue.Count;
                while (attempts-- > 0 && queue.Count > 0)
                {
                    var entry = queue.Dequeue();
                    var target = entry.Target;
                    if (target != null)
                        _pooledPixels -= (long)target.Width * target.Height;
                    if (target == null || target.IsDisposed || target.GraphicsDevice != graphicsDevice)
                    {
                        target?.Dispose();
                        continue;
                    }

                    int age = unchecked(currentFrame - entry.ReturnedFrame);
                    if (age >= MinFramesBeforeReuse)
                        return target;

                    queue.Enqueue(entry);
                    _pooledPixels += (long)target.Width * target.Height;
                }
            }

            return new RenderTarget2D(
                graphicsDevice,
                width,
                height,
                false,
                SurfaceFormat.Color,
                DepthFormat.None);
        }

        public static void Return(RenderTarget2D target)
        {
            if (target == null || target.IsDisposed)
                return;

            var graphicsDevice = target.GraphicsDevice;
            if (graphicsDevice == null)
            {
                target.Dispose();
                return;
            }

            EnsureGraphicsDevice(graphicsDevice);
            if (target.GraphicsDevice != _graphicsDevice)
            {
                target.Dispose();
                return;
            }

            var key = new TargetKey(target.Width, target.Height);
            if (!_pools.TryGetValue(key, out var queue))
            {
                queue = new Queue<PoolEntry>(MaxTargetsPerSize);
                _pools.Add(key, queue);
            }

            long targetPixels = (long)target.Width * target.Height;
            if (queue.Count >= MaxTargetsPerSize || _pooledPixels + targetPixels > MaxPooledPixels)
            {
                target.Dispose();
                return;
            }

            queue.Enqueue(new PoolEntry(target, MuGame.FrameIndex));
            _pooledPixels += targetPixels;
            PruneIfNeeded();
        }

        public static void Clear()
        {
            foreach (var queue in _pools.Values)
            {
                while (queue.Count > 0)
                    queue.Dequeue().Target?.Dispose();
            }

            _pools.Clear();
            _pooledPixels = 0;
            _lastPruneFrame = MuGame.FrameIndex;
        }

        private static void EnsureGraphicsDevice(GraphicsDevice graphicsDevice)
        {
            if (ReferenceEquals(_graphicsDevice, graphicsDevice))
                return;

            if (_graphicsDevice != null)
            {
                _graphicsDevice.DeviceResetting -= OnDeviceInvalidated;
                _graphicsDevice.DeviceReset -= OnDeviceInvalidated;
                _graphicsDevice.DeviceLost -= OnDeviceInvalidated;
            }

            Clear();
            _graphicsDevice = graphicsDevice;
            _graphicsDevice.DeviceResetting += OnDeviceInvalidated;
            _graphicsDevice.DeviceReset += OnDeviceInvalidated;
            _graphicsDevice.DeviceLost += OnDeviceInvalidated;
        }

        private static void PruneIfNeeded()
        {
            int currentFrame = MuGame.FrameIndex;
            if (unchecked(currentFrame - _lastPruneFrame) < PruneIntervalFrames)
                return;

            _lastPruneFrame = currentFrame;
            List<TargetKey> emptyKeys = null;

            foreach (var pair in _pools)
            {
                var queue = pair.Value;
                int count = queue.Count;
                while (count-- > 0 && queue.Count > 0)
                {
                    var entry = queue.Dequeue();
                    var target = entry.Target;
                    if (target != null)
                        _pooledPixels -= (long)target.Width * target.Height;
                    int age = unchecked(currentFrame - entry.ReturnedFrame);
                    if (target == null || target.IsDisposed || age > MaxIdleFrames)
                    {
                        target?.Dispose();
                        continue;
                    }

                    queue.Enqueue(entry);
                    _pooledPixels += (long)target.Width * target.Height;
                }

                if (queue.Count == 0)
                {
                    emptyKeys ??= new List<TargetKey>();
                    emptyKeys.Add(pair.Key);
                }
            }

            if (emptyKeys == null)
                return;

            for (int i = 0; i < emptyKeys.Count; i++)
                _pools.Remove(emptyKeys[i]);
        }

        private static void OnDeviceInvalidated(object sender, EventArgs e) => Clear();
    }
}
