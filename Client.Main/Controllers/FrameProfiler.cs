using System.Diagnostics;

namespace Client.Main.Controllers
{
    /// <summary>
    /// Allocation-free CPU frame profiler. Current-frame values are refreshed every frame,
    /// while percentile and GC values are recomputed less frequently over a rolling window.
    /// GPU execution time still requires an external profiler such as PIX or RenderDoc.
    /// </summary>
    public sealed class FrameProfiler
    {
        public readonly record struct Snapshot(
            long FrameIndex,
            long FrameIntervalFrameIndex,
            long RollingWindowStartFrameIndex,
            long RollingWindowEndFrameIndex,
            int RollingSampleCount,
            long RollingSequence,
            double UpdateMs,
            double DrawMs,
            double CpuFrameMs,
            double FrameIntervalMs,
            double FrameIntervalCpuMs,
            double FrameIntervalUnaccountedMs,
            double P50Ms,
            double P95Ms,
            double P99Ms,
            double WorstMs,
            double WallP50Ms,
            double WallP95Ms,
            double WallP99Ms,
            double WallWorstMs,
            double AllocatedKb,
            double ProcessAllocatedKb,
            int FramesOver16Ms,
            int FramesOver33Ms,
            int WallFramesOver16Ms,
            int WallFramesOver33Ms,
            int Gen0Collections,
            int Gen1Collections,
            int Gen2Collections);

        private const int SampleCapacity = 300;
        private const int RecomputeIntervalFrames = 30;

        private readonly double[] _cpuSamples = new double[SampleCapacity];
        private readonly double[] _wallSamples = new double[SampleCapacity];
        private readonly double[] _sortedCpuSamples = new double[SampleCapacity];
        private readonly double[] _sortedWallSamples = new double[SampleCapacity];

        private int _cpuSampleCount;
        private int _wallSampleCount;
        private int _cpuSampleWriteIndex;
        private int _wallSampleWriteIndex;
        private int _framesSinceRecompute;

        private long _frameIndex;
        private long _frameIntervalFrameIndex;
        private long _rollingWindowStartFrameIndex;
        private long _rollingWindowEndFrameIndex;
        private long _rollingSequence;
        private long _frameStart;
        private long _updateStart;
        private long _drawStart;
        private long _allocatedAtUpdateStartThread;
        private long _allocatedAtUpdateStartProcess;

        private double _lastUpdateMs;
        private double _lastDrawMs;
        private double _lastCpuFrameMs;
        private double _lastFrameIntervalMs;
        private double _lastFrameIntervalCpuMs;
        private double _lastFrameIntervalUnaccountedMs;
        private double _lastAllocatedKb;
        private double _lastProcessAllocatedKb;

        private double _p50Ms;
        private double _p95Ms;
        private double _p99Ms;
        private double _worstMs;
        private double _wallP50Ms;
        private double _wallP95Ms;
        private double _wallP99Ms;
        private double _wallWorstMs;
        private int _framesOver16Ms;
        private int _framesOver33Ms;
        private int _wallFramesOver16Ms;
        private int _wallFramesOver33Ms;
        private int _gen0Collections;
        private int _gen1Collections;
        private int _gen2Collections;
        private int _gen0WindowStart = GC.CollectionCount(0);
        private int _gen1WindowStart = GC.CollectionCount(1);
        private int _gen2WindowStart = GC.CollectionCount(2);

        public Snapshot Current => new(
            _frameIndex,
            _frameIntervalFrameIndex,
            _rollingWindowStartFrameIndex,
            _rollingWindowEndFrameIndex,
            _cpuSampleCount,
            _rollingSequence,
            _lastUpdateMs,
            _lastDrawMs,
            _lastCpuFrameMs,
            _lastFrameIntervalMs,
            _lastFrameIntervalCpuMs,
            _lastFrameIntervalUnaccountedMs,
            _p50Ms,
            _p95Ms,
            _p99Ms,
            _worstMs,
            _wallP50Ms,
            _wallP95Ms,
            _wallP99Ms,
            _wallWorstMs,
            _lastAllocatedKb,
            _lastProcessAllocatedKb,
            _framesOver16Ms,
            _framesOver33Ms,
            _wallFramesOver16Ms,
            _wallFramesOver33Ms,
            _gen0Collections,
            _gen1Collections,
            _gen2Collections);

        public void BeginUpdate(long frameIndex)
        {
            long now = Stopwatch.GetTimestamp();

            if (_frameStart != 0)
            {
                _lastFrameIntervalMs = Stopwatch.GetElapsedTime(_frameStart, now).TotalMilliseconds;
                _lastFrameIntervalCpuMs = _lastCpuFrameMs;
                _lastFrameIntervalUnaccountedMs = Math.Max(0d, _lastFrameIntervalMs - _lastFrameIntervalCpuMs);
                _frameIntervalFrameIndex = _frameIndex;
                AddWallSample(_lastFrameIntervalMs);
            }

            _frameIndex = frameIndex;
            _frameStart = now;
            _updateStart = now;
            _allocatedAtUpdateStartThread = GC.GetAllocatedBytesForCurrentThread();
            _allocatedAtUpdateStartProcess = GC.GetTotalAllocatedBytes(false);
        }

        public void EndUpdate()
        {
            if (_updateStart == 0)
                return;

            _lastUpdateMs = Stopwatch.GetElapsedTime(_updateStart).TotalMilliseconds;
            _updateStart = 0;
        }

        public void BeginDraw() => _drawStart = Stopwatch.GetTimestamp();

        public void EndDraw()
        {
            if (_drawStart == 0)
                return;

            _lastDrawMs = Stopwatch.GetElapsedTime(_drawStart).TotalMilliseconds;
            _drawStart = 0;
            _lastCpuFrameMs = _lastUpdateMs + _lastDrawMs;

            long threadAllocated = GC.GetAllocatedBytesForCurrentThread() - _allocatedAtUpdateStartThread;
            long processAllocated = GC.GetTotalAllocatedBytes(false) - _allocatedAtUpdateStartProcess;
            _lastAllocatedKb = Math.Max(0L, threadAllocated) / 1024d;
            _lastProcessAllocatedKb = Math.Max(0L, processAllocated) / 1024d;

            AddCpuSample(_lastCpuFrameMs);
        }

        private void AddCpuSample(double frameMs)
        {
            _cpuSamples[_cpuSampleWriteIndex] = frameMs;
            _cpuSampleWriteIndex = (_cpuSampleWriteIndex + 1) % SampleCapacity;
            if (_cpuSampleCount < SampleCapacity)
                _cpuSampleCount++;

            _rollingWindowEndFrameIndex = _frameIndex;
            _rollingWindowStartFrameIndex = Math.Max(1L, _rollingWindowEndFrameIndex - _cpuSampleCount + 1L);

            _framesSinceRecompute++;
            if (_framesSinceRecompute < RecomputeIntervalFrames && _cpuSampleCount > 1)
                return;

            RecomputeRollingSnapshot();
        }

        private void AddWallSample(double frameMs)
        {
            _wallSamples[_wallSampleWriteIndex] = frameMs;
            _wallSampleWriteIndex = (_wallSampleWriteIndex + 1) % SampleCapacity;
            if (_wallSampleCount < SampleCapacity)
                _wallSampleCount++;
        }

        private void RecomputeRollingSnapshot()
        {
            if (_cpuSampleCount == 0)
                return;

            _framesSinceRecompute = 0;
            _framesOver16Ms = 0;
            _framesOver33Ms = 0;
            _worstMs = CopyAndMeasure(
                _cpuSamples,
                _sortedCpuSamples,
                _cpuSampleCount,
                ref _framesOver16Ms,
                ref _framesOver33Ms);

            _p50Ms = Percentile(_sortedCpuSamples, _cpuSampleCount, 0.50);
            _p95Ms = Percentile(_sortedCpuSamples, _cpuSampleCount, 0.95);
            _p99Ms = Percentile(_sortedCpuSamples, _cpuSampleCount, 0.99);

            if (_wallSampleCount > 0)
            {
                _wallFramesOver16Ms = 0;
                _wallFramesOver33Ms = 0;
                _wallWorstMs = CopyAndMeasure(
                    _wallSamples,
                    _sortedWallSamples,
                    _wallSampleCount,
                    ref _wallFramesOver16Ms,
                    ref _wallFramesOver33Ms);

                _wallP50Ms = Percentile(_sortedWallSamples, _wallSampleCount, 0.50);
                _wallP95Ms = Percentile(_sortedWallSamples, _wallSampleCount, 0.95);
                _wallP99Ms = Percentile(_sortedWallSamples, _wallSampleCount, 0.99);
            }

            int gen0Now = GC.CollectionCount(0);
            int gen1Now = GC.CollectionCount(1);
            int gen2Now = GC.CollectionCount(2);
            _gen0Collections = gen0Now - _gen0WindowStart;
            _gen1Collections = gen1Now - _gen1WindowStart;
            _gen2Collections = gen2Now - _gen2WindowStart;
            _gen0WindowStart = gen0Now;
            _gen1WindowStart = gen1Now;
            _gen2WindowStart = gen2Now;
            _rollingSequence++;
        }

        private static double CopyAndMeasure(
            double[] source,
            double[] destination,
            int count,
            ref int framesOver16Ms,
            ref int framesOver33Ms)
        {
            double worst = 0d;
            for (int i = 0; i < count; i++)
            {
                double value = source[i];
                destination[i] = value;
                if (value > worst)
                    worst = value;
                if (value > 16.6667d)
                    framesOver16Ms++;
                if (value > 33.3333d)
                    framesOver33Ms++;
            }

            Array.Sort(destination, 0, count);
            return worst;
        }

        private static double Percentile(double[] sortedSamples, int count, double percentile)
        {
            if (count == 0)
                return 0d;

            int index = (int)Math.Ceiling((count - 1) * percentile);
            return sortedSamples[Math.Clamp(index, 0, count - 1)];
        }
    }
}
