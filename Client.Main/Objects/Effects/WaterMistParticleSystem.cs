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
    /// Water mist emitter backed by the SourceMain-style fixed particle pool.
    /// </summary>
    public class WaterMistParticleSystem : SourceParticleSystem
    {
        private const int MaxParticles = 256;
        private Texture2D _texture;
        private Vector2 _textureCenter;
        private float _emissionAccumulator;

        public float EmissionRate { get; set; } = 8f;
        public Vector2 ScaleRange { get; set; } = new(0.4f, 0.8f);
        public Vector2 LifetimeRange { get; set; } = new(2.5f, 4f);
        public Vector2 HorizontalVelocityRange { get; set; } = new(-8f, 8f);
        public Vector2 UpwardVelocityRange { get; set; } = new(12f, 22f);
        public float UpwardAcceleration { get; set; } = -3f;
        public Color ParticleColor { get; set; } = new(200, 220, 255, 180);
        public Vector2 SpawnRadius { get; set; } = new(6f, 6f);
        public bool UseDistanceEmissionScaling { get; set; } = true;
        public bool UseDistanceScaling { get; set; } = true;
        public bool UseConstantWorldSize { get; set; }
        public Vector2 Wind { get; set; } = Vector2.Zero;

        protected override Texture2D ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _textureCenter;

        public WaterMistParticleSystem()
            : base(MaxParticles)
        {
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

            float emissionScale = 1f;
            if (UseDistanceEmissionScaling && Camera.Instance != null)
            {
                float maxDistanceSq = MaxDistance * MaxDistance;
                float distanceSq = Vector3.DistanceSquared(Camera.Instance.Position, Position);
                if (distanceSq > maxDistanceSq)
                    return;

                float distanceRatio = distanceSq / maxDistanceSq;
                emissionScale = 1f - distanceRatio * distanceRatio;
            }

            _emissionAccumulator += EmissionRate * emissionScale * dt;
            int emitCount = (int)_emissionAccumulator;
            _emissionAccumulator -= emitCount;

            for (int i = 0; i < emitCount; i++)
            {
                CreateParticle(
                    type: 0,
                    position: new Vector3(
                        Position.X + RandomRange(-SpawnRadius.X, SpawnRadius.X),
                        Position.Y + RandomRange(-SpawnRadius.Y, SpawnRadius.Y),
                        Position.Z),
                    angle: Vector3.Zero,
                    light: Vector3.One);
            }
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            float lifetime = RandomRange(LifetimeRange.X, LifetimeRange.Y);
            particle.Velocity = new Vector3(
                RandomRange(HorizontalVelocityRange.X, HorizontalVelocityRange.Y),
                RandomRange(HorizontalVelocityRange.X, HorizontalVelocityRange.Y),
                RandomRange(UpwardVelocityRange.X, UpwardVelocityRange.Y));
            particle.LifeTime = lifetime;
            particle.MaxLifeTime = lifetime;
            particle.Scale = RandomRange(ScaleRange.X, ScaleRange.Y);
            particle.Rotation = RandomRange(0f, MathHelper.TwoPi);
            particle.Gravity = UpwardAcceleration;
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            particle.Position.X += particle.Velocity.X * dt + Wind.X * dt;
            particle.Position.Y += particle.Velocity.Y * dt + Wind.Y * dt;
            particle.Position.Z += particle.Velocity.Z * dt;
            particle.Velocity.Z += UpwardAcceleration * dt;
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio) =>
            ParticleColor * (lifeRatio * lifeRatio);

        protected override float GetParticleScale(in SourceParticle particle, float lifeRatio, float distanceScale, float perspectiveScale)
        {
            float effectiveDistanceScale = UseDistanceScaling ? distanceScale : 1f;
            float effectivePerspectiveScale = UseConstantWorldSize ? 1f : perspectiveScale;
            float growth = 1f + ScaleGrowth * (1f - lifeRatio);
            return particle.Scale * growth * effectiveDistanceScale * effectivePerspectiveScale;
        }

        public override void Dispose()
        {
            _texture?.Dispose();
            _texture = null;
            base.Dispose();
        }

        private void GenerateProceduralTexture()
        {
            const int size = 64;
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
