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
        public int ModelObjects { get; internal set; }
        public int AnimationUpdates { get; internal set; }
        public int AnimationSkips { get; internal set; }
        public int LowQualityObjects { get; internal set; }

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
            ModelObjects = 0;
            AnimationUpdates = 0;
            AnimationSkips = 0;
            LowQualityObjects = 0;
        }
    }
}
