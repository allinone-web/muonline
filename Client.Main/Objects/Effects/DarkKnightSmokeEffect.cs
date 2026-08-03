using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// SourceMain5.2 smoke emission shared by MODEL_DARK_KNIGHT, MODEL_LARVA and
    /// MODEL_CHAIN_SCORPION while the monster is alive.
    /// </summary>
    public sealed class SourceMonsterSmokeEffect : SourceParticleSystem
    {
        private const int MaxParticles = 96;
        private const float LegacyFramesPerSecond = 25f;
        private const string SmokeTexturePath = "Effect/smoke02.tga";

        private Texture2D _texture;
        private Vector2 _textureCenter;
        private float _legacyAccumulator;

        protected override Texture2D ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _textureCenter;

        public SourceMonsterSmokeEffect()
            : base(MaxParticles)
        {
            BlendState = BlendState.Additive;
            MaxDistance = 2000f;
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
            if (_texture == null || Parent is not MonsterObject monster || monster.IsDead)
                return;

            _legacyAccumulator += dt * LegacyFramesPerSecond;
            int legacyTicks = (int)_legacyAccumulator;
            _legacyAccumulator -= legacyTicks;

            Vector3 ownerPosition = monster.WorldPosition.Translation;
            for (int tick = 0; tick < legacyTicks; tick++)
            {
                if (MuGame.Random.Next(4) != 0)
                    continue;

                CreateParticle(
                    type: 0,
                    position: new Vector3(
                        ownerPosition.X + MuGame.Random.Next(-32, 32),
                        ownerPosition.Y + MuGame.Random.Next(-32, 32),
                        ownerPosition.Z + MuGame.Random.Next(-16, 16)),
                    angle: monster.Angle,
                    light: Vector3.One);
            }
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            particle.LifeTime = 32f / LegacyFramesPerSecond;
            particle.MaxLifeTime = particle.LifeTime;
            particle.Scale = (32 + MuGame.Random.Next(32)) * 0.01f;
            particle.Velocity = new Vector3(0f, 3f, 0f);
            particle.Rotation = 0f;
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            float legacyDelta = dt * LegacyFramesPerSecond;
            particle.Position += particle.Velocity * legacyDelta;
            particle.Velocity *= MathF.Pow(0.9f, legacyDelta);
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
        {
            return new Color(particle.Light.X, particle.Light.Y, particle.Light.Z, lifeRatio);
        }
    }
}
