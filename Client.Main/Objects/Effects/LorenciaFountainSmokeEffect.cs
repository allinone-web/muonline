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
    /// SourceMain-compatible BITMAP_SMOKE subtype 0 emitter used by Lorencia waterspouts.
    /// </summary>
    public sealed class LorenciaFountainSmokeEffect : SourceParticleSystem
    {
        private const string SmokeTexturePath = "Effect/smoke01.jpg";
        private const int Capacity = 128;
        private const double LegacyStepSeconds = 1.0 / 25.0;
        private const int MaxCatchUpTicks = 8;
        private const float EmissionDistance = 3200f;

        private static readonly BlendState MuRgbAdditiveBlend = new()
        {
            ColorBlendFunction = BlendFunction.Add,
            ColorSourceBlend = Blend.One,
            ColorDestinationBlend = Blend.One,
            AlphaBlendFunction = BlendFunction.Add,
            AlphaSourceBlend = Blend.One,
            AlphaDestinationBlend = Blend.One
        };

        private readonly ModelObject _owner;
        private Texture2D? _texture;
        private Vector2 _textureCenter;
        private double _legacyAccumulator;

        protected override Texture2D? ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _textureCenter;

        public LorenciaFountainSmokeEffect(ModelObject owner)
            : base(Capacity)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            BlendState = MuRgbAdditiveBlend;
            DepthState = DepthStencilState.DepthRead;
            MaxDistance = EmissionDistance;
            MinDistanceScale = 1f;
            ScaleGrowth = 0f;
        }

        public override async Task LoadContent()
        {
            _texture = await TextureLoader.Instance.PrepareAndGetTexture(SmokeTexturePath);
            if (_texture != null)
                _textureCenter = new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);
        }

        public override void Update(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || Camera.Instance == null)
                return;

            UpdateProjectionScale();

            double elapsed = Math.Min(
                gameTime.ElapsedGameTime.TotalSeconds,
                LegacyStepSeconds * MaxCatchUpTicks);
            _legacyAccumulator += elapsed;

            while (_legacyAccumulator >= LegacyStepSeconds)
            {
                // SourceMain moves existing particles before creating the next pair.
                UpdateParticles(1f);
                EmitLegacyTick(gameTime.TotalGameTime.TotalMilliseconds);
                _legacyAccumulator -= LegacyStepSeconds;
            }
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            particle.LifeTime = 16f;
            particle.MaxLifeTime = 16f;
            particle.Scale = MuGame.Random.Next(48, 80) * 0.01f;
            particle.Rotation = 0f;
            particle.Gravity = 0f;
            particle.Velocity = Vector3.Zero;
            particle.EnableMove = false;
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float animationFactor)
        {
            float luminosity = particle.LifeTime / 8f;
            particle.Light = new Vector3(luminosity);
            particle.Gravity += 0.2f * animationFactor;
            particle.Position.Z += particle.Gravity * animationFactor;
            particle.Scale += 0.05f * animationFactor;
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
        {
            Vector3 light = Vector3.Clamp(particle.Light, Vector3.Zero, Vector3.One);
            return new Color(light.X, light.Y, light.Z, 1f);
        }

        protected override float GetParticleScale(
            in SourceParticle particle,
            float lifeRatio,
            float distanceScale,
            float perspectiveScale) => particle.Scale * perspectiveScale;

        private void EmitLegacyTick(double worldTimeMilliseconds)
        {
            if (_texture == null || !_owner.Visible || _owner.World == null)
                return;

            Vector3 ownerPosition = _owner.WorldPosition.Translation;
            if (Vector3.DistanceSquared(Camera.Instance.Position, ownerPosition) > EmissionDistance * EmissionDistance)
                return;

            Matrix[] bones = _owner.GetBoneTransforms();
            if (bones == null || bones.Length <= 4 || MuGame.Random.Next(0, 2) != 0)
                return;

            Vector3 objectLight = ResolveObjectLight(ownerPosition);
            SpawnFromBone(
                bones[1],
                new Vector3(MuGame.Random.Next(-16, 16), -20f, MuGame.Random.Next(-16, 16)),
                objectLight,
                worldTimeMilliseconds);
            SpawnFromBone(
                bones[4],
                new Vector3(MuGame.Random.Next(-16, 16), -80f, MuGame.Random.Next(-16, 16)),
                objectLight,
                worldTimeMilliseconds);
        }

        private void SpawnFromBone(
            Matrix boneTransform,
            Vector3 localOffset,
            Vector3 objectLight,
            double worldTimeMilliseconds)
        {
            Vector3 localPosition = Vector3.Transform(localOffset, boneTransform);
            Vector3 worldPosition = Vector3.Transform(localPosition, _owner.WorldPosition);
            int index = CreateParticle(0, worldPosition, _owner.Angle, objectLight);
            if (index >= 0)
                Particles[index].Rotation = MathHelper.ToRadians((float)(worldTimeMilliseconds % 360.0));
        }

        private Vector3 ResolveObjectLight(Vector3 ownerPosition)
        {
            Vector3 light = Vector3.One;
            if (_owner.World?.Terrain != null)
            {
                light = _owner.World.Terrain.EvaluateTerrainLight(ownerPosition.X, ownerPosition.Y);
                light += _owner.Light;
            }

            return Vector3.Clamp(light, Vector3.Zero, Vector3.One);
        }

        private void UpdateProjectionScale()
        {
            var camera = Camera.Instance;
            var device = GraphicsManager.Instance.GraphicsDevice;
            if (camera == null || device == null)
                return;

            float renderScale = MathF.Max(Constants.RENDER_SCALE, 0.0001f);
            ReferenceDistance = device.Viewport.Height * camera.Projection.M22 * 0.5f / renderScale;
        }
    }
}
