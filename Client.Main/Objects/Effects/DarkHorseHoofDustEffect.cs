#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Dust kicked up under the Dark Horse's hooves while it runs.
    /// Mirrors the original C++ behavior from SourceMain5.2:
    /// - GOBoid.cpp MoveMount, MODEL_DARK_HORSE, PLAYER_RUN_RIDE_HORSE case:
    ///   CreateParticle(BITMAP_SMOKE + 1, ...) at the mount position with ±32 X/Y
    ///   and ±16 Z jitter, emitted at rand_fps_check(2) rate (~12.5/s at any FPS).
    /// - BITMAP_SMOKE + 1 = Effect/smoke02.tga (ZzzOpenData.cpp).
    /// - ZzzEffectParticle.cpp (case BITMAP_SMOKE + 1): lifetime ~32 frames, scale
    ///   grows over life, light fades linearly, and Position.Z snaps to the terrain
    ///   height each frame so the puffs hug the ground under the hooves.
    /// </summary>
    public sealed class DarkHorseHoofDustEffect : SourceParticleSystem
    {
        private const string SmokeTexturePath = "Effect/smoke02.tga";
        private const int MaxParticles = 64;
        private const float EmissionRate = 12f; // rand_fps_check(2) @ 25 FPS ≈ 12.5/s

        private Texture2D _texture = null!;
        private Vector2 _textureCenter;
        private float _emissionAccumulator;

        /// <summary>True while the Dark Horse is running; the vehicle toggles this.</summary>
        public bool Emitting { get; set; }

        protected override Texture2D? ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _textureCenter;

        public DarkHorseHoofDustEffect()
            : base(MaxParticles)
        {
            BlendState = BlendState.Additive;
            MaxDistance = 2000f;
            ReferenceDistance = 800f;
            ScaleGrowth = 1.2f;
        }

        public override async Task LoadContent()
        {
            await TextureLoader.Instance.Prepare(SmokeTexturePath);
            _texture = TextureLoader.Instance.GetTexture2D(SmokeTexturePath) ?? GraphicsManager.Instance.Pixel;
            _textureCenter = new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);
        }

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            if (!Emitting || _texture == null)
                return;

            _emissionAccumulator += EmissionRate * dt;
            int emitCount = (int)_emissionAccumulator;
            if (emitCount <= 0)
                return;
            _emissionAccumulator -= emitCount;

            // Particles are stored in world space; WorldPosition includes the vehicle + player transforms.
            Matrix worldMatrix = WorldPosition;
            Vector3 horsePos = worldMatrix.Translation;
            float groundZ = World?.Terrain != null
                ? World.Terrain.RequestTerrainHeight(horsePos.X, horsePos.Y)
                : horsePos.Z;

            for (int i = 0; i < emitCount; i++)
            {
                CreateParticle(
                    type: 0,
                    position: new Vector3(
                        horsePos.X + RandomRange(-32f, 32f),
                        horsePos.Y + RandomRange(-32f, 32f),
                        groundZ),
                    angle: Vector3.Zero,
                    light: new Vector3(1f, 1f, 1f));
            }
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            // Original lifetime ~32 frames (≈1.3s at 25 FPS).
            float lifetime = RandomRange(1.0f, 1.4f);
            particle.LifeTime = lifetime;
            particle.MaxLifeTime = lifetime;
            // Original starts small and grows; growth is applied by ScaleGrowth in the base class.
            particle.Scale = RandomRange(0.6f, 1.2f);
            particle.Rotation = RandomRange(0f, MathHelper.TwoPi);
            // Original: Velocity = rotate((0,3,0), mount angle) with 0.9 decay per frame.
            particle.Velocity = new Vector3(
                RandomRange(-8f, 8f),
                RandomRange(-8f, 8f),
                RandomRange(0f, 10f));
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            // Original: Position += Velocity * factor; Velocity *= 0.9.
            particle.Position += particle.Velocity * dt;
            particle.Velocity *= 1f - MathHelper.Clamp(dt * 0.9f, 0f, 0.3f);
            particle.Rotation += dt * 0.3f;

            // Original: Position.Z = RequestTerrainHeight(...) + Height * Scale * 0.5
            // so the dust puffs hug the ground under the hooves (follows slopes).
            if (World?.Terrain != null)
            {
                float groundZ = World.Terrain.RequestTerrainHeight(particle.Position.X, particle.Position.Y);
                particle.Position.Z = groundZ + 6f;
            }
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
        {
            // Original: Luminosity = LifeTime / 32 → linear fade from 1.0 to 0.
            float alpha = MathHelper.Clamp(lifeRatio, 0f, 1f) * 0.6f;
            return new Color(particle.Light.X, particle.Light.Y, particle.Light.Z, alpha);
        }
    }
}
