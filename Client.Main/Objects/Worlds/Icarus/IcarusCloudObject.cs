using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Icarus
{
    /// <summary>
    /// Asset-driven Icarus cloud geometry (map object types 0-5).
    /// The model is rendered as an emissive additive layer and does not depend
    /// on terrain lighting or the short lightning-flash cloud model.
    /// </summary>
    public sealed class IcarusCloudObject : MapTileObject
    {
        private static readonly Vector3 CloudDiffuseColor = new(0.72f, 0.82f, 1.0f);

        public override bool ForceVisibleInWorld => true;
        protected override bool AllowDynamicLightingShader => false;
        protected override bool AllowMapObjectInstancing => false;
        protected override bool PreserveBlendMeshesInLowQuality => true;

        public IcarusCloudObject()
        {
            LightEnabled = false;
            Light = Vector3.One;
            UseSunLight = false;
            Color = Color.White;

            // All cloud meshes belong to the transparent pass. The generic DrawModel
            // path is intentionally retained because static map models may keep only
            // GPU buffers after initialization.
            BlendState = Blendings.OneOneAdditive;
            BlendMesh = -2;
            BlendMeshState = Blendings.OneOneAdditive;
            BlendMeshLight = 1f;
            IsTransparent = true;
            AffectedByTransparency = false;
            DepthState = GraphicsManager.ReadOnlyDepth;
            RenderShadow = false;
        }

        public override async Task Load()
        {
            Texture2D cloudTexture = null;
            try
            {
                cloudTexture = await TextureLoader.Instance.PrepareAndGetTexture(
                    "Effect/clouds.jpg");
            }
            catch
            {
                // Keep the model's original material if the shared texture is absent.
            }

            await base.Load();

            if (cloudTexture == null || Model?.Meshes == null)
                return;

            for (int mesh = 0; mesh < Model.Meshes.Length; mesh++)
                SetMeshTextureOverride(mesh, cloudTexture);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible || Model?.Meshes == null)
                return;

            AlphaTestEffect effect = GraphicsManager.Instance.AlphaTestEffect3D;
            if (effect == null)
            {
                base.DrawAfter(gameTime);
                return;
            }

            bool previousVertexColorEnabled = effect.VertexColorEnabled;
            Vector3 previousDiffuseColor = effect.DiffuseColor;
            float previousAlpha = effect.Alpha;
            SamplerState previousSampler = GraphicsDevice.SamplerStates[0];

            try
            {
                // Set the state before invoking the normal model pass. DrawModel can then
                // choose CPU, GPU-skinned or batched buffers without losing the emissive
                // cloud material settings.
                effect.VertexColorEnabled = false;
                effect.DiffuseColor = CloudDiffuseColor;
                effect.Alpha = 1f;
                GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

                base.DrawAfter(gameTime);
            }
            finally
            {
                GraphicsDevice.SamplerStates[0] = previousSampler;
                effect.VertexColorEnabled = previousVertexColorEnabled;
                effect.DiffuseColor = previousDiffuseColor;
                effect.Alpha = previousAlpha;
            }
        }
    }
}
