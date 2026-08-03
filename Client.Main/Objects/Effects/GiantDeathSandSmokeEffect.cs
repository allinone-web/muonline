using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Objects.Effects.Particles;
using Client.Main.Objects.Monsters;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// SourceMain5.2 MonsterDieSandSmoke for MODEL_GIANT.
    /// The original emits BITMAP_SMOKE+1 while the death animation is on frames 8-9.
    /// </summary>
    public sealed class GiantDeathSandSmokeEffect : SourceParticleSystem
    {
        private const int MaxParticles = 128;
        private const float LegacyFramesPerSecond = 25f;
        private const string SmokeTexturePath = "Effect/smoke02.tga";

        private Texture2D _texture;
        private Vector2 _textureCenter;
        private float _legacyAccumulator;
        private bool _hasEmitted;

        protected override Texture2D ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _textureCenter;

        public GiantDeathSandSmokeEffect()
            : base(MaxParticles)
        {
            BlendState = BlendState.Additive;
            MaxDistance = 2500f;
            ReferenceDistance = 800f;
            ScaleGrowth = 0f;
        }

        public override async Task LoadContent()
        {
            _texture = await TextureLoader.Instance.PrepareAndGetTexture(SmokeTexturePath);
            if (_texture != null)
                _textureCenter = new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);
        }

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            if (_texture == null || Parent is not Giant giant)
                return;

            if (giant.CurrentAction != (int)MonsterActionType.Die)
            {
                _legacyAccumulator = 0f;
                _hasEmitted = false;
                return;
            }

            double animationFrame = giant.GetAnimationTime();
            if (_hasEmitted || animationFrame < 8.0 || animationFrame >= 9.0)
                return;

            _legacyAccumulator += dt * LegacyFramesPerSecond;
            int legacyTicks = (int)_legacyAccumulator;
            _legacyAccumulator -= legacyTicks;

            Vector3 ownerPosition = giant.WorldPosition.Translation;
            for (int tick = 0; tick < legacyTicks; tick++)
            {
                // SourceMain5.2 loops 20 times and calls rand_fps_check(1) for each particle.
                for (int i = 0; i < 20; i++)
                {
                    CreateParticle(
                        type: 0,
                        position: new Vector3(
                            ownerPosition.X + MuGame.Random.Next(-32, 32),
                            ownerPosition.Y + MuGame.Random.Next(-32, 32),
                            ownerPosition.Z + MuGame.Random.Next(-16, 16)),
                        angle: giant.Angle,
                        light: Vector3.One);
                }

                _hasEmitted = true;
                break;
            }
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            particle.LifeTime = 32f / LegacyFramesPerSecond;
            particle.MaxLifeTime = particle.LifeTime;
            particle.Scale = 0.9f + (float)MuGame.Random.NextDouble() * 0.3f;
            particle.Velocity = new Vector3(0f, 2f, 1f);
            particle.Rotation = 0f;
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            float legacyDelta = dt * LegacyFramesPerSecond;
            particle.Position += particle.Velocity * legacyDelta;
            particle.Velocity *= MathF.Pow(0.92f, legacyDelta);
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
        {
            return new Color(particle.Light.X, particle.Light.Y, particle.Light.Z, lifeRatio);
        }
    }
}
