using Client.Main.Content;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Icarus
{
    /// <summary>
    /// Short-lived, oversized cloud model used by the original Icarus global lightning flash.
    /// The object is loaded once and toggled by <see cref="IcarusStormSystem"/> to avoid
    /// allocating or loading GPU resources during a storm tick.
    /// </summary>
    internal sealed class IcarusFlashCloudObject : ModelObject
    {
        protected override bool AllowDynamicLightingShader => false;
        protected override bool AllowMapObjectInstancing => false;
        protected override bool PreserveBlendMeshesInLowQuality => true;

        public IcarusFlashCloudObject()
        {
            Hidden = true;
            Scale = 10f;
            BlendState = BlendState.Opaque;
            BlendMesh = -1;
            BlendMeshState = Blendings.OneOneAdditive;
            BlendMeshLight = 1f;
            LightEnabled = false;
            Light = Vector3.Zero;
            IsTransparent = false;
            RenderShadow = false;
            Color = Color.White;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-600f, -600f, -250f),
                new Vector3(600f, 600f, 250f));
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Object11/cloud.bmd");
            Texture2D cloudTexture = null;
            try
            {
                cloudTexture = await TextureLoader.Instance.PrepareAndGetTexture(
                    "Effect/clouds.jpg");
            }
            catch
            {
                // The model is optional; lightning and local flashes remain functional.
            }
            CurrentAction = 0;
            AnimationSpeed = 0f;
            ContinuousAnimation = false;
            await base.Load();

            if (cloudTexture != null && Model?.Meshes != null)
            {
                bool textureApplied = false;
                for (int mesh = 0; mesh < Model.Meshes.Length; mesh++)
                {
                    if (Model.Meshes[mesh].Texture != 0)
                        continue;

                    SetMeshTextureOverride(mesh, cloudTexture);
                    textureApplied = true;
                }

                if (!textureApplied && Model.Meshes.Length > 0)
                    SetMeshTextureOverride(0, cloudTexture);
            }
        }

        protected override bool IsBlendMesh(int mesh)
        {
            if (Model?.Meshes != null &&
                (uint)mesh < (uint)Model.Meshes.Length &&
                Model.Meshes[mesh].Texture == 0)
            {
                return true;
            }

            return base.IsBlendMesh(mesh);
        }

        public void Show(Vector3 heroPosition, Vector3 light)
        {
            Position = heroPosition + new Vector3(0f, 200f, -190f);
            Light = light;
            BlendMeshLight = 1f;
            InvalidateBuffers(MeshDirtyFlags.Lighting | MeshDirtyFlags.Material | MeshDirtyFlags.Transform);
            Hidden = false;
        }

        public void Hide()
        {
            Hidden = true;
        }
    }
}
