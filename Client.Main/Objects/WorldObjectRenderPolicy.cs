namespace Client.Main.Objects
{
    public readonly struct WorldObjectRenderPolicy
    {
        public static readonly WorldObjectRenderPolicy Default = new();

        public WorldObjectRenderPolicy(
            bool forceVisible = false,
            bool forceVisibleInLoginWorld = false,
            bool alwaysUpdate = false,
            bool preserveBlendMeshesInLowQuality = false)
        {
            ForceVisible = forceVisible;
            ForceVisibleInLoginWorld = forceVisibleInLoginWorld;
            AlwaysUpdate = alwaysUpdate;
            PreserveBlendMeshesInLowQuality = preserveBlendMeshesInLowQuality;
        }

        public bool ForceVisible { get; }
        public bool ForceVisibleInLoginWorld { get; }
        public bool AlwaysUpdate { get; }
        public bool PreserveBlendMeshesInLowQuality { get; }

        public WorldObjectRenderPolicy With(
            bool? forceVisible = null,
            bool? forceVisibleInLoginWorld = null,
            bool? alwaysUpdate = null,
            bool? preserveBlendMeshesInLowQuality = null)
        {
            return new WorldObjectRenderPolicy(
                forceVisible ?? ForceVisible,
                forceVisibleInLoginWorld ?? ForceVisibleInLoginWorld,
                alwaysUpdate ?? AlwaysUpdate,
                preserveBlendMeshesInLowQuality ?? PreserveBlendMeshesInLowQuality);
        }
    }
}
