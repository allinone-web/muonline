using Microsoft.Xna.Framework;
using System;

namespace Client.Main.Controllers
{
    public sealed class FPSCounter
    {
        private const float ReferenceFps = 25f;
        private const double AverageWindowMs = 500d;

        public static FPSCounter Instance { get; } = new();

        private bool _timeInitialized;
        private double _windowStartTime;
        private double _lastFrameTime;
        private int _windowFrameCount;

        public double WorldTime { get; private set; }
        public double FPS { get; private set; }
        public double FPS_AVG { get; private set; }
        public float FPS_ANIMATION_FACTOR { get; private set; } = 1f;

        public bool RandFPSCheck(int referenceFrames)
        {
            double animationFactor = Math.Min(1.0, FPS_ANIMATION_FACTOR);
            double chance = referenceFrames == 1
                ? animationFactor
                : (1.0 / Math.Max(1, referenceFrames)) * animationFactor;

            return MuGame.Random.NextDouble() <= chance;
        }

        public void CalcFPS(GameTime gameTime)
        {
            WorldTime = gameTime.TotalGameTime.TotalMilliseconds;
            if (!_timeInitialized)
            {
                _timeInitialized = true;
                _windowStartTime = WorldTime;
                _lastFrameTime = WorldTime;
                return;
            }

            double frameIntervalMs = WorldTime - _lastFrameTime;
            _lastFrameTime = WorldTime;

            if (frameIntervalMs > 0d)
            {
                FPS = 1000d / frameIntervalMs;
                FPS_ANIMATION_FACTOR = Math.Min(ReferenceFps / (float)FPS, 2.5f);
            }

            _windowFrameCount++;
            double windowDurationMs = WorldTime - _windowStartTime;
            if (windowDurationMs < AverageWindowMs)
                return;

            FPS_AVG = windowDurationMs > 0d
                ? 1000d * _windowFrameCount / windowDurationMs
                : 0d;
            _windowStartTime = WorldTime;
            _windowFrameCount = 0;
        }
    }
}
