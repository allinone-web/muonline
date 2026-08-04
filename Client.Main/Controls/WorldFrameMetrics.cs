namespace Client.Main.Controls
{
    /// <summary>
    /// Per-frame world object metrics used by the debug panel and performance logging.
    /// </summary>
    public sealed class WorldFrameMetrics
    {
        public int CullCandidates { get; internal set; }
        public int VisibleObjects { get; internal set; }
        public bool CullWasRebuild { get; internal set; }
        public float CullMs { get; internal set; }
        public int SolidBehindObjects { get; internal set; }
        public int SolidInFrontObjects { get; internal set; }
        public int TransparentObjects { get; internal set; }
        public int SpriteBatchObjects { get; internal set; }
        public int DedicatedParticleSystems { get; internal set; }
        public int ParticleSprites { get; internal set; }
        public int ParticleBatchBegins { get; internal set; }
        public int ParticleSystemsCulled { get; internal set; }
        public int InactiveParticleSystemsSkipped { get; internal set; }
        public int ModelObjects { get; internal set; }
        public int DedicatedStaticMapObjects { get; internal set; }
        public int StaticMapUpdateSkips { get; internal set; }
        public int DrawAfterSkips { get; internal set; }
        public int AnimationUpdates { get; internal set; }
        public int AnimationSkips { get; internal set; }
        public int LowQualityObjects { get; internal set; }
        public double LongestObjectUpdateMs { get; internal set; }
        public string LongestObjectUpdateType { get; internal set; }
        public string LongestObjectUpdateName { get; internal set; }
        public ushort LongestObjectUpdateNetworkId { get; internal set; }
        public int RenderFailures { get; internal set; }
        public long LastRenderFailureSequence { get; internal set; }
        public int LastRenderFailureFrameIndex { get; internal set; }
        public string LastRenderFailurePhase { get; internal set; }
        public string LastRenderFailureType { get; internal set; }
        public string LastRenderFailureName { get; internal set; }
        public ushort LastRenderFailureNetworkId { get; internal set; }
        public string LastRenderFailureMessage { get; internal set; }

        public void Reset()
        {
            CullCandidates = 0;
            VisibleObjects = 0;
            CullWasRebuild = false;
            CullMs = 0f;
            SolidBehindObjects = 0;
            SolidInFrontObjects = 0;
            TransparentObjects = 0;
            SpriteBatchObjects = 0;
            DedicatedParticleSystems = 0;
            ParticleSprites = 0;
            ParticleBatchBegins = 0;
            ParticleSystemsCulled = 0;
            InactiveParticleSystemsSkipped = 0;
            ModelObjects = 0;
            DedicatedStaticMapObjects = 0;
            StaticMapUpdateSkips = 0;
            DrawAfterSkips = 0;
            AnimationUpdates = 0;
            AnimationSkips = 0;
            LowQualityObjects = 0;
            LongestObjectUpdateMs = 0d;
            LongestObjectUpdateType = null;
            LongestObjectUpdateName = null;
            LongestObjectUpdateNetworkId = 0;
            RenderFailures = 0;
            // Last failure details are retained across frames so a transient render
            // fault remains visible in diagnostics after the object recovers.
        }
    }
}
