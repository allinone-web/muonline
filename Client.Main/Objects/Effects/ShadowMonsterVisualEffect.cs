using Client.Data.BMD;
using Client.Data.Texture;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Smoke cloud emitted from the animated Shadow bones.
    ///
    /// smoke02.tga is an alpha texture in the original client. Non-premultiplied
    /// alpha blending matches its GL_SRC_ALPHA render path and keeps the texture
    /// rectangle fully transparent.
    /// </summary>
    public sealed class ShadowMonsterVisualEffect : EffectObject
    {
        private const int MaxParticles = 96;
        private const float LegacyFramesPerSecond = 25f;
        private const float SmokeScaleMultiplier = 2f;
        private const float SurfaceDepthOffset = 60f;
        private const string SmokeTexturePath = "Effect/smoke02.tga";

        private readonly SmokeParticle[] _particles = new SmokeParticle[MaxParticles];
        private Texture2D _texture;
        private int _activeCount;
        private float _emissionAccumulator;

        public bool PoisonVariant { get; set; }

        public ShadowMonsterVisualEffect()
        {
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = Blendings.NonPremultiplied;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();
            TextureData source = await TextureLoader.Instance.Prepare(SmokeTexturePath);
            _texture = CreateMaskedSmokeTexture(source);
        }

        public override void Update(GameTime gameTime)
        {
            ModelObject parentModel = Parent as ModelObject;
            Hidden = parentModel == null || parentModel.Hidden || parentModel.Model == null;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt > 0f)
            {
                UpdateParticles(dt);

                if (!Hidden && parentModel != null)
                    EmitParticles(parentModel, dt);
            }

            base.Update(gameTime);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (Hidden || _texture == null || _activeCount == 0 || Parent is not ModelObject parentModel)
                return;

            using (new SpriteBatchScope(
                GraphicsManager.Instance.Sprite,
                SpriteSortMode.Deferred,
                Blendings.NonPremultiplied,
                SamplerState.LinearClamp,
                DepthStencilState.DepthRead,
                RasterizerState.CullNone))
            {
                for (int i = 0; i < _activeCount; i++)
                {
                    ref readonly SmokeParticle particle = ref _particles[i];
                    Vector3 toCamera = Camera.Instance.Position - particle.Position;
                    float distance = toCamera.Length();
                    Vector3 depthPosition = particle.Position;
                    if (distance > SurfaceDepthOffset + 1f)
                        depthPosition += toCamera * (SurfaceDepthOffset / distance);

                    Vector3 projected = GraphicsDevice.Viewport.Project(
                        depthPosition,
                        Camera.Instance.Projection,
                        Camera.Instance.View,
                        Matrix.Identity);

                    if (projected.Z < 0f || projected.Z > 1f)
                        continue;

                    float lifeRatio = MathHelper.Clamp(
                        particle.Life / particle.MaxLife,
                        0f,
                        1f);
                    float fadeIn = MathHelper.SmoothStep(
                        0f,
                        1f,
                        MathHelper.Clamp((1f - lifeRatio) / 0.18f, 0f, 1f));
                    float fadeOut = MathHelper.SmoothStep(
                        0f,
                        1f,
                        MathHelper.Clamp(lifeRatio / 0.45f, 0f, 1f));
                    float intensity = fadeIn * fadeOut * 0.75f;
                    if (intensity <= 0.003f)
                        continue;

                    float ownerScale = parentModel.WorldPosition.Right.Length();
                    if (ownerScale <= 0.001f)
                        ownerScale = 1f;

                    float screenScale = particle.Scale * ownerScale /
                        (MathF.Max(distance, 0.1f) / Constants.TERRAIN_SIZE) *
                        Constants.RENDER_SCALE;
                    screenScale *= SmokeScaleMultiplier *
                        MathHelper.Lerp(0.8f, 1.25f, 1f - lifeRatio);

                    Vector3 smokeColor = PoisonVariant
                        ? new Vector3(0.18f, 0.85f, 0.22f)
                        : new Vector3(0.42f, 0.45f, 0.48f);
                    Color color = new Color(
                        smokeColor.X,
                        smokeColor.Y,
                        smokeColor.Z,
                        intensity);

                    GraphicsManager.Instance.Sprite.Draw(
                        _texture,
                        new Vector2(projected.X, projected.Y),
                        null,
                        color,
                        particle.Rotation,
                        new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f),
                        screenScale,
                        SpriteEffects.None,
                        MathHelper.Clamp(projected.Z, 0f, 1f));
                }
            }

            base.DrawAfter(gameTime);
        }

        private void EmitParticles(ModelObject parentModel, float dt)
        {
            _emissionAccumulator += dt * LegacyFramesPerSecond;
            int legacyTicks = (int)_emissionAccumulator;
            _emissionAccumulator -= legacyTicks;

            Matrix[] bones = parentModel.GetBoneTransforms();
            if (bones == null || bones.Length == 0)
                return;

            for (int tick = 0; tick < legacyTicks; tick++)
            {
                // Two overlapping puffs per legacy frame create a continuous cloud,
                // while each puff still fades independently.
                for (int puff = 0; puff < 2; puff++)
                {
                    if (_activeCount >= MaxParticles ||
                        !TryGetBoneWorldPosition(parentModel, bones, out Vector3 position))
                        return;

                    SpawnParticle(position);
                }
            }
        }

        private bool TryGetBoneWorldPosition(
            ModelObject parentModel,
            Matrix[] bones,
            out Vector3 worldPosition)
        {
            int eligibleCount = 0;
            for (int i = 0; i < bones.Length; i++)
            {
                if (IsEligibleBone(parentModel, i))
                    eligibleCount++;
            }

            if (eligibleCount == 0)
            {
                worldPosition = default;
                return false;
            }

            int selected = MuGame.Random.Next(eligibleCount);
            for (int i = 0; i < bones.Length; i++)
            {
                if (!IsEligibleBone(parentModel, i))
                    continue;

                if (selected-- != 0)
                    continue;

                Vector3 localOffset = new Vector3(
                    RandomRange(-10f, 10f),
                    RandomRange(-10f, 10f),
                    RandomRange(-10f, 10f));
                worldPosition = Vector3.Transform(
                    localOffset,
                    bones[i] * parentModel.WorldPosition);
                return true;
            }

            worldPosition = default;
            return false;
        }

        private bool IsEligibleBone(ModelObject parentModel, int index)
        {
            if (index < 0 || index >= parentModel.Model.Bones.Length ||
                ReferenceEquals(parentModel.Model.Bones[index], BMDTextureBone.Dummy))
                return false;

            return (index < 15 || index > 20) && (index < 27 || index > 32);
        }

        private void SpawnParticle(Vector3 position)
        {
            ref SmokeParticle particle = ref _particles[_activeCount++];
            particle = new SmokeParticle
            {
                Position = position,
                Velocity = new Vector3(
                    RandomRange(-12f, 12f),
                    RandomRange(-12f, 12f),
                    RandomRange(12f, 28f)),
                Life = RandomRange(0.85f, 1.25f),
                Scale = PoisonVariant
                    ? RandomRange(0.65f, 0.95f)
                    : RandomRange(0.75f, 1.10f),
                Rotation = RandomRange(0f, MathHelper.TwoPi),
                RotationSpeed = RandomRange(-0.8f, 0.8f)
            };
            particle.MaxLife = particle.Life;
        }

        private void UpdateParticles(float dt)
        {
            for (int i = _activeCount - 1; i >= 0; i--)
            {
                ref SmokeParticle particle = ref _particles[i];
                particle.Life -= dt;
                if (particle.Life <= 0f)
                {
                    _activeCount--;
                    if (i != _activeCount)
                        _particles[i] = _particles[_activeCount];
                    _particles[_activeCount] = default;
                    continue;
                }

                particle.Position += particle.Velocity * dt;
                particle.Velocity *= 1f - MathHelper.Clamp(dt * 0.6f, 0f, 0.2f);
                particle.Velocity.Z += 10f * dt;
                particle.Rotation += particle.RotationSpeed * dt;
            }
        }

        private float RandomRange(float min, float max)
        {
            return min + (float)MuGame.Random.NextDouble() * (max - min);
        }

        private Texture2D CreateMaskedSmokeTexture(TextureData source)
        {
            if (source?.Data == null || source.Width <= 0 || source.Height <= 0 ||
                (source.Components != 3 && source.Components != 4))
                return null;

            int pixelCount = source.Width * source.Height;
            int components = source.Components;
            if (source.Data.Length < pixelCount * components)
                return null;

            var pixels = new Color[pixelCount];
            float halfWidth = source.Width * 0.5f;
            float halfHeight = source.Height * 0.5f;

            for (int y = 0; y < source.Height; y++)
            {
                float normalizedY = (y + 0.5f - halfHeight) / halfHeight;
                for (int x = 0; x < source.Width; x++)
                {
                    int pixelIndex = y * source.Width + x;
                    int dataIndex = pixelIndex * components;
                    float luminance = MathF.Max(
                        source.Data[dataIndex],
                        MathF.Max(source.Data[dataIndex + 1], source.Data[dataIndex + 2])) / 255f;
                    float sourceAlpha = components == 4
                        ? source.Data[dataIndex + 3] / 255f
                        : 1f;

                    // Remove the dark texture background even when its stored alpha
                    // is opaque, then feather the whole circumference to zero.
                    float density = MathHelper.Clamp((luminance - 0.04f) / 0.96f, 0f, 1f);
                    float normalizedX = (x + 0.5f - halfWidth) / halfWidth;
                    float radius = MathF.Sqrt(
                        normalizedX * normalizedX + normalizedY * normalizedY);
                    float edgeFade = MathHelper.Clamp((1f - radius) / 0.22f, 0f, 1f);
                    edgeFade = edgeFade * edgeFade * (3f - 2f * edgeFade);
                    density = MathHelper.SmoothStep(0f, 1f, density * sourceAlpha * edgeFade);

                    pixels[pixelIndex] = new Color(1f, 1f, 1f, density);
                }
            }

            var texture = new Texture2D(
                GraphicsDevice,
                source.Width,
                source.Height,
                false,
                SurfaceFormat.Color);
            texture.SetData(pixels);
            return texture;
        }

        public override void Dispose()
        {
            _texture?.Dispose();
            _texture = null;
            _activeCount = 0;
            base.Dispose();
        }

        private struct SmokeParticle
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Life;
            public float MaxLife;
            public float Scale;
            public float Rotation;
            public float RotationSpeed;
        }
    }
}
