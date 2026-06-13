using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
            ScaleGrowth = 0.6f;
        }

        public override Task LoadContent()
        {
            GenerateProceduralTexture();
            return Task.CompletedTask;
        }

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            if (_texture == null)
                GenerateProceduralTexture();

            if (EmitThisFrame && ActiveCount < MaxParticles)
            {
                CreateParticle(
                    type: 0,
                    position: new Vector3(
                        SpawnWorldPosition.X + RandomRange(-8f, 8f),
                        SpawnWorldPosition.Y + RandomRange(-8f, 8f),
                        SpawnWorldPosition.Z + RandomRange(-4f, 4f)),
                    angle: Vector3.Zero,
                    light: Vector3.One);
            }

            EmitThisFrame = false;
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            float lifetime = RandomRange(0.15f, 0.35f);
            particle.Velocity = new Vector3(
                RandomRange(-20f, 20f),
                RandomRange(0f, 32f),
                RandomRange(10f, 30f));
            particle.LifeTime = lifetime;
            particle.MaxLifeTime = lifetime;
            particle.Scale = RandomRange(0.5f, 1.0f);
            particle.Rotation = RandomRange(0f, MathHelper.TwoPi);
            particle.Gravity = -15f;
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            particle.Position += particle.Velocity * dt;
            particle.Velocity.Z += particle.Gravity * dt;
            particle.Rotation += dt * 3f;
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
        {
            // Red-orange fire, fading to dark red at end of life
            float r = 1f;
            float g = 0.25f + lifeRatio * 0.35f;
            float b = 0f;
            float alpha = lifeRatio * 0.9f;
            return new Color(r, g, b, alpha);
        }

        public override void Dispose()
        {
            _texture?.Dispose();
            _texture = null;
            base.Dispose();
        }

        private void GenerateProceduralTexture()
        {
            const int size = 32;
            var device = GraphicsManager.Instance.GraphicsDevice;
            _texture = new Texture2D(device, size, size);
            var pixels = new Color[size * size];
            float center = size * 0.5f;
            float maxRadius = center;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center + 0.5f;
                    float dy = y - center + 0.5f;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    float t = MathHelper.Clamp(1f - dist / maxRadius, 0f, 1f);
                    float alpha = t * t;
                    pixels[y * size + x] = new Color((byte)255, (byte)255, (byte)255, (byte)(alpha * 255f));
                }
            }

            _texture.SetData(pixels);
            _textureCenter = new Vector2(size * 0.5f, size * 0.5f);
        }
    }
}
