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
    /// Ground-level dust/smoke particle emitter for Budge Dragon.
    /// Particles spawn with random offsets around the dragon's position and drift upward while fading.
    /// Mirrors the original C++ CreateParticle(BITMAP_SMOKE+1) behavior from ZzzCharacter.cpp.
    /// </summary>
    public class BudgeDragonDustEffect : SourceParticleSystem
    {
        private const int MaxParticles = 64;
        private const float LegacyFramesPerSecond = 25f;
        private const string SmokeTexturePath = "Effect/smoke02.tga";
        private Texture2D _texture;
        private Vector2 _textureCenter;
        private float _legacyAccumulator;

        /// <summary>
        /// Original: rand_fps_check(4) in the fall-through branch of MoveCharacterVisual.
        /// </summary>
        protected override Texture2D ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _textureCenter;

        public BudgeDragonDustEffect()
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
            if (_texture == null || Parent is MonsterObject { IsDead: true })
                return;

            _legacyAccumulator += dt * LegacyFramesPerSecond;
            int legacyTicks = (int)_legacyAccumulator;
            _legacyAccumulator -= legacyTicks;

            Vector3 ownerWorldPos = WorldPosition.Translation;

            for (int i = 0; i < legacyTicks; i++)
            {
                if (MuGame.Random.Next(4) != 0)
                    continue;

                CreateParticle(
                    type: 0,
                    position: new Vector3(
                        ownerWorldPos.X + RandomRange(-32f, 32f),
                        ownerWorldPos.Y + RandomRange(-32f, 32f),
                        ownerWorldPos.Z + RandomRange(-16f, 16f)),
                    angle: Angle,
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

            if (World?.Terrain != null)
            {
                float groundZ = World.Terrain.RequestTerrainHeight(particle.Position.X, particle.Position.Y);
                particle.Position.Z = groundZ + (_texture?.Height ?? 0) * particle.Scale * 0.5f;
            }
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
        {
            return new Color(particle.Light.X, particle.Light.Y, particle.Light.Z, lifeRatio);
        }
    }
}
