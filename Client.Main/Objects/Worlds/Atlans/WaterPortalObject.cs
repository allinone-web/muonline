using Client.Main.Content;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Atlans
{
    /// <summary>
    /// Visible Atlans water portal (map object type 23 / Object24.bmd).
    /// The effect uses the model's looping animation, additive water material,
    /// pulsating brightness and the shared wt00..wt31 water flipbook.
    /// </summary>
    public sealed class WaterPortalObject : ModelObject
    {
        private const int WaterFrameCount = 32;
        private const float WaterFramesPerSecond = 25f;

        // The original velocity is 0.05 animation frame per 25 Hz legacy tick.
        private const float PortalAnimationFramesPerSecond = 0.05f * 25f;

        private readonly Texture2D[] _waterFrames = new Texture2D[WaterFrameCount];
        private int[] _waterMeshIndices = Array.Empty<int>();
        private int _currentWaterFrame = -1;

        protected override bool AllowDynamicLightingShader => false;
        protected override bool AllowMapObjectInstancing => false;
        protected override bool RequiresPerFrameAnimation => true;
        protected override bool PreserveBlendMeshesInLowQuality => true;

        public override async Task Load()
        {
            // Object24 is the actual water portal model used by this client.
            Model = await BMDLoader.Instance.Prepare("Object8/Object24.bmd");
            ResolveWaterMeshes();

            // Keep possible structural meshes in the opaque pass. The water material is
            // routed independently to DrawAfter with exact MU-style One + One blending.
            BlendState = BlendState.Opaque;
            BlendMesh = -1;
            BlendMeshState = Blendings.OneOneAdditive;
            BlendMeshLight = 0.5f;
            LightEnabled = true;
            IsTransparent = false;
            RenderShadow = false;
            Color = Color.White;

            CurrentAction = 0;
            AnimationSpeed = PortalAnimationFramesPerSecond;
            ContinuousAnimation = true;

            await base.Load();

            CacheWaterFramesFromTerrain();
            ApplyWaterFrame(0);
        }

        protected override bool IsBlendMesh(int mesh)
        {
            for (int i = 0; i < _waterMeshIndices.Length; i++)
            {
                if (_waterMeshIndices[i] == mesh)
                    return true;
            }

            return base.IsBlendMesh(mesh);
        }

        public override void Update(GameTime gameTime)
        {
            if (Status == GameControlStatus.Ready)
            {
                float worldTimeMilliseconds = (float)gameTime.TotalGameTime.TotalMilliseconds;
                BlendMeshLight = MathF.Sin(worldTimeMilliseconds * 0.004f) * 0.3f + 0.5f;

                int frame = (int)(gameTime.TotalGameTime.TotalSeconds * WaterFramesPerSecond)
                    % WaterFrameCount;
                ApplyWaterFrame(frame);
            }

            base.Update(gameTime);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            SamplerState previousSampler = GraphicsDevice.SamplerStates[0];
            try
            {
                GraphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
                base.DrawAfter(gameTime);
            }
            finally
            {
                GraphicsDevice.SamplerStates[0] = previousSampler;
            }
        }

        private void ResolveWaterMeshes()
        {
            if (Model?.Meshes == null || Model.Meshes.Length == 0)
            {
                _waterMeshIndices = Array.Empty<int>();
                return;
            }

            var indices = new List<int>(Model.Meshes.Length);
            for (int mesh = 0; mesh < Model.Meshes.Length; mesh++)
            {
                if (Model.Meshes[mesh].Texture == 0)
                    indices.Add(mesh);
            }

            // The previous implementation modified mesh 0 directly, so retain that layout
            // as a fallback for Object24 files whose importer does not preserve slot numbers.
            if (indices.Count == 0)
                indices.Add(0);

            _waterMeshIndices = indices.ToArray();
        }

        private void CacheWaterFramesFromTerrain()
        {
            for (int frame = 0; frame < WaterFrameCount; frame++)
                _waterFrames[frame] = World?.Terrain?.GetWaterAnimationFrame(frame);
        }

        private void ApplyWaterFrame(int frame)
        {
            if (frame == _currentWaterFrame || (uint)frame >= WaterFrameCount)
                return;

            Texture2D texture = _waterFrames[frame];
            if (texture == null || texture.IsDisposed)
            {
                texture = World?.Terrain?.GetWaterAnimationFrame(frame);
                _waterFrames[frame] = texture;
            }

            if (texture == null || texture.IsDisposed)
                return;

            for (int i = 0; i < _waterMeshIndices.Length; i++)
                SetMeshTextureOverride(_waterMeshIndices[i], texture);

            _currentWaterFrame = frame;
        }
    }
}
