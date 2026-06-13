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
    /// Ground-level dust/smoke particle emitter for Budge Dragon.
    /// Particles spawn with random offsets around the dragon's position and drift upward while fading.
    /// Mirrors the original C++ CreateParticle(BITMAP_SMOKE+1) behavior from ZzzCharacter.cpp.
    /// </summary>
    public class BudgeDragonDustEffect : SourceParticleSystem
    {
        private const int MaxParticles = 64;
        private Texture2D _texture;
        private Vector2 _textureCenter;
        private float _emissionAccumulator;

        /// <summary>
        /// Emission rate in particles per second. Original: rand_fps_check(4) at 25 FPS ≈ 6.25/s.
        /// Increased for better visibility.
        /// </summary>
        public float EmissionRate { get; set; } = 14f;

        protected override Texture2D ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _textureCenter;

        public BudgeDragonDustEffect()
            : base(MaxParticles)
        {
            BlendState = BlendState.Additive;
            MaxDistance = 2000f;
            ReferenceDistance = 800f;
            ScaleGrowth = 1.5f;
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

            _emissionAccumulator += EmissionRate * dt;
            int emitCount = (int)_emissionAccumulator;
            _emissionAccumulator -= emitCount;

            // Convert owner's world position to world space for particle spawning.
            // Particles are stored in world space.
            Matrix worldMatrix = WorldPosition;
            Vector3 ownerWorldPos = worldMatrix.Translation;

            for (int i = 0; i < emitCount; i++)
            {
                CreateParticle(
                    type: 0,
                    position: new Vector3(
                        ownerWorldPos.X + RandomRange(-32f, 32f),
                        ownerWorldPos.Y + RandomRange(-32f, 32f),
                        ownerWorldPos.Z + RandomRange(-16f, 16f)),
                    angle: Vector3.Zero,
                    light: new Vector3(0.55f, 0.48f, 0.4f));
            }
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            float lifetime = RandomRange(1.5f, 3.0f);
            float speed = RandomRange(12f, 25f);
            particle.Velocity = new Vector3(
                RandomRange(-6f, 6f),
                RandomRange(-6f, 6f),
                speed);
            particle.LifeTime = lifetime;
            particle.MaxLifeTime = lifetime;
            particle.Scale = RandomRange(1.0f, 2.0f);
            particle.Rotation = RandomRange(0f, MathHelper.TwoPi);
            particle.Gravity = -2f;
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            particle.Position += particle.Velocity * dt;
            particle.Velocity.Z += particle.Gravity * dt;
            particle.Rotation += dt * 0.3f;
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
        {
            float alpha = lifeRatio * lifeRatio * 0.55f;
            return new Color(particle.Light.X, particle.Light.Y, particle.Light.Z, alpha);
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
                    float alpha = t * t * (3f - 2f * t);
                    pixels[y * size + x] = new Color((byte)255, (byte)255, (byte)255, (byte)(alpha * 255f));
                }
            }

            _texture.SetData(pixels);
            _textureCenter = new Vector2(size * 0.5f, size * 0.5f);
        }
    }
}
