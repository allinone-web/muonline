using System.Diagnostics;

namespace Client.Main.Controllers
{
    /// <summary>
    /// Allocation-free rolling CPU frame profiler. It intentionally measures CPU time only;
    /// GPU timings still require an external profiler such as PIX or RenderDoc.
    /// </summary>
    public sealed class FrameProfiler
    {
        public readonly record struct Snapshot(
            double UpdateMs,
            double DrawMs,
            double P50Ms,
            double P95Ms,
            double P99Ms,
            double WorstMs,
            double AllocatedKb,
            int FramesOver16Ms,
            int FramesOver33Ms,
            int Gen0Collections,
            int Gen1Collections,
            int Gen2Collections);

        private const int SampleCapacity = 300;
        private const int RecomputeIntervalFrames = 30;

        private readonly double[] _samples = new double[SampleCapacity];
        private readonly double[] _sortedSamples = new double[SampleCapacity];
        private int _sampleCount;
        private int _sampleWriteIndex;
        private int _framesSinceRecompute;
        private long _updateStart;
        private long _drawStart;
        private long _allocatedAtUpdateStart;
        private double _lastUpdateMs;
        private double _lastDrawMs;
        private double _lastAllocatedKb;
        private int _gen0WindowStart = GC.CollectionCount(0);
        private int _gen1WindowStart = GC.CollectionCount(1);
        private int _gen2WindowStart = GC.CollectionCount(2);
        private Snapshot _snapshot;

        public Snapshot Current => _snapshot;

        public void BeginUpdate()
        {
            _updateStart = Stopwatch.GetTimestamp();
            _allocatedAtUpdateStart = GC.GetTotalAllocatedBytes(false);
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

            long allocated = GC.GetTotalAllocatedBytes(false) - _allocatedAtUpdateStart;
            _lastAllocatedKb = Math.Max(0, allocated) / 1024d;
            AddSample(_lastUpdateMs + _lastDrawMs);
        }

        private void AddSample(double frameMs)
        {
            _samples[_sampleWriteIndex] = frameMs;
            _sampleWriteIndex = (_sampleWriteIndex + 1) % SampleCapacity;
            if (_sampleCount < SampleCapacity)
                _sampleCount++;

            _framesSinceRecompute++;
            if (_framesSinceRecompute < RecomputeIntervalFrames && _sampleCount > 1)
                return;

            _framesSinceRecompute = 0;
            RecomputeSnapshot();
        }

        private void RecomputeSnapshot()
        {
            if (_sampleCount == 0)
                return;

            int over16 = 0;
            int over33 = 0;
            double worst = 0;
            for (int i = 0; i < _sampleCount; i++)
            {
                double value = _samples[i];
                _sortedSamples[i] = value;
                if (value > worst) worst = value;
                if (value > 16.6667) over16++;
                if (value > 33.3333) over33++;
            }

            Array.Sort(_sortedSamples, 0, _sampleCount);
            int gen0Now = GC.CollectionCount(0);
            int gen1Now = GC.CollectionCount(1);
            int gen2Now = GC.CollectionCount(2);
            _snapshot = new Snapshot(
                _lastUpdateMs,
                _lastDrawMs,
                Percentile(0.50),
                Percentile(0.95),
                Percentile(0.99),
                worst,
                _lastAllocatedKb,
                over16,
                over33,
                gen0Now - _gen0WindowStart,
                gen1Now - _gen1WindowStart,
                gen2Now - _gen2WindowStart);

            _gen0WindowStart = gen0Now;
            _gen1WindowStart = gen1Now;
            _gen2WindowStart = gen2Now;
        }

        private double Percentile(double percentile)
        {
            if (_sampleCount == 0)
                return 0;

            int index = (int)Math.Ceiling((_sampleCount - 1) * percentile);
            return _sortedSamples[Math.Clamp(index, 0, _sampleCount - 1)];
        }
    }
}
