using System;
using System.Threading.Tasks;
using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Short-lived BMD effect used by the Dark Raven attack sequence.
    /// The model paths and additive/depth settings mirror SourceMain's DarkSpirit attack effects.
    /// </summary>
    internal sealed class DarkRavenAttackModelEffect : ModelObject
    {
        private readonly string _modelPath;
        private readonly string _fallbackModelPath;
        private float _lifeFrames;
        private readonly float _maxLifeFrames;
        private float _growthPerFrame;
        private readonly float _growthAcceleration;
        private readonly float _fadeBase;
        private readonly float _fadeStartFrame;
        private readonly float _rotationYPerFrame;

        public DarkRavenAttackModelEffect(
            string modelPath,
            string fallbackModelPath,
            Vector3 position,
            Vector3 angle,
            Vector3 light,
            float scale,
            int blendMesh,
            float lifeFrames,
            float growthPerFrame = 0f,
            float growthAcceleration = 0f,
            float fadeBase = 1f,
            float fadeStartFrame = 0f,
            float rotationYPerFrame = 0f)
        {
            _modelPath = modelPath;
            _fallbackModelPath = fallbackModelPath;
            _lifeFrames = lifeFrames;
            _maxLifeFrames = lifeFrames;
            _growthPerFrame = growthPerFrame;
            _growthAcceleration = growthAcceleration;
            _fadeBase = fadeBase;
            _fadeStartFrame = fadeStartFrame;
            _rotationYPerFrame = rotationYPerFrame;

            Position = position;
            Angle = angle;
            Light = light;
            Scale = scale;
            BlendMesh = blendMesh;
            BlendMeshLight = 1f;

            ContinuousAnimation = true;
            AnimationSpeed = 4f;
            LightEnabled = true;
            RenderShadow = false;
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = Blendings.OneOneAdditive;
            BlendMeshState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
        }

        protected override bool ForceTwoSidedMeshes => true;

        public override async Task Load()
        {
            try
            {
                Model = await BMDLoader.Instance.Prepare(_modelPath);
            }
            catch when (!string.IsNullOrEmpty(_fallbackModelPath))
            {
                Model = await BMDLoader.Instance.Prepare(_fallbackModelPath);
            }

            if (Model == null && !string.IsNullOrEmpty(_fallbackModelPath))
                Model = await BMDLoader.Instance.Prepare(_fallbackModelPath);

            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;
            Scale += _growthPerFrame * factor;
            _growthPerFrame += _growthAcceleration * factor;
            if (_rotationYPerFrame != 0f)
            {
                Vector3 angle = Angle;
                angle.Y += _rotationYPerFrame * factor;
                Angle = angle;
            }

            float age = _maxLifeFrames - _lifeFrames;
            if (_fadeBase < 1f && age >= _fadeStartFrame)
                BlendMeshLight *= MathF.Pow(_fadeBase, factor);
            else if (_fadeBase >= 1f)
                BlendMeshLight = MathHelper.Clamp(_lifeFrames / _maxLifeFrames, 0f, 1f);

            _lifeFrames -= factor;

            if (_lifeFrames <= 0f)
                RemoveSelf();
        }

        private void RemoveSelf()
        {
            World?.RemoveObject(this);
            Dispose();
        }
    }
}
