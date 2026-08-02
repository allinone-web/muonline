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
    /// Fire particle emitter used during Budge Dragon's attack animation.
    /// Spawns short-lived fire particles from the dragon's head (bone 7).
    /// Mirrors the original C++ CreateParticle(BITMAP_FIRE) behavior from ZzzCharacter.cpp.
    /// </summary>
    public class BudgeDragonFireAttackEffect : SourceParticleSystem
    {
        private const int MaxParticles = 32;
        private const float LegacyFramesPerSecond = 25f;
        private const string FireTexturePath = "Effect/Fire01.jpg";
        private Texture2D _texture;
        private Vector2 _textureCenter;

        /// <summary>
        /// Set by the owner to control emission. Fire particles are spawned when this is true.
        /// </summary>
        public bool EmitThisFrame { get; set; }

        /// <summary>
        /// World-space position where fire particles should spawn (bone 7 — dragon's head).
        /// </summary>
        public Vector3 SpawnWorldPosition { get; set; }

        protected override Texture2D ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _textureCenter;

        public BudgeDragonFireAttackEffect()
            : base(MaxParticles)
        {
            BlendState = BlendState.Additive;
            MaxDistance = 2500f;
            ReferenceDistance = 800f;
            ScaleGrowth = 0f;
        }

        public override async Task LoadContent()
        {
            _texture = await TextureLoader.Instance.PrepareAndGetTexture(FireTexturePath);
            if (_texture != null)
                _textureCenter = new Vector2(_texture.Width * 0.125f, _texture.Height * 0.5f);
        }

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            if (EmitThisFrame && ActiveCount < MaxParticles)
            {
                CreateParticle(
                    type: 0,
                    position: SpawnWorldPosition,
                    angle: Vector3.Zero,
                    light: Vector3.One);
            }

            EmitThisFrame = false;
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            particle.LifeTime = 24f / LegacyFramesPerSecond;
            particle.MaxLifeTime = particle.LifeTime;
            particle.Velocity = new Vector3(
                0f,
                -(32 + MuGame.Random.Next(16)) * 0.1f,
                0f);
            particle.Scale = (10 + MuGame.Random.Next(4)) * 0.01f;
            particle.Rotation = MathHelper.ToRadians(MuGame.Random.Next(360));
            particle.Gravity = 0f;
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            float legacyDelta = dt * LegacyFramesPerSecond;
            particle.Gravity += 0.004f * legacyDelta;
            particle.Scale += particle.Gravity * legacyDelta;
            particle.Velocity *= MathF.Pow(0.98f, legacyDelta);
            particle.Position.Z += particle.Gravity * 10f * legacyDelta;

            float lifeInFrames = particle.LifeTime * LegacyFramesPerSecond;
            particle.Frame = MathHelper.Clamp((int)((23f - lifeInFrames) / 6f), 0, 3);
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
        {
            return new Color(particle.Light.X, particle.Light.Y, particle.Light.Z, lifeRatio);
        }

        protected override Rectangle? GetParticleSourceRectangle(Texture2D texture, in SourceParticle particle)
        {
            return new Rectangle(
                particle.Frame * (texture.Width / 4),
                0,
                texture.Width / 4,
                texture.Height);
        }
    }
}
