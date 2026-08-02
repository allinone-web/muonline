#nullable enable
using System;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Particles
{
    public abstract class SourceParticleSystem : EffectObject
    {
        private Matrix _viewProjection;
        private Vector3 _cameraPosition;

        protected SourceParticleSystem(int capacity)
        {
            Particles = new SourceParticle[Math.Max(1, capacity)];
            IsTransparent = false;
            AffectedByTransparency = false;
            BlendState = BlendState.Additive;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-5000f, -5000f, -5000f),
                new Vector3(5000f, 5000f, 5000f));
        }

        protected SourceParticle[] Particles { get; }
        protected int ActiveCount { get; private set; }

        public float MaxDistance { get; set; } = 1500f;
        public float ReferenceDistance { get; set; } = 800f;
        public float MinDistanceScale { get; set; } = 0.5f;
        public float ScaleGrowth { get; set; } = 0.4f;

        protected abstract Texture2D? ParticleTexture { get; }
        protected abstract Vector2 ParticleTextureCenter { get; }

        public int CreateParticle(
            int type,
            Vector3 position,
            Vector3 angle,
            Vector3 light,
            int subType = 0,
            float scale = 0f,
            WorldObject? owner = null)
        {
            if (ActiveCount >= Particles.Length)
                return -1;

            int index = ActiveCount++;
            ref SourceParticle particle = ref Particles[index];
            particle = new SourceParticle
            {
                Live = true,
                Type = type,
                TexType = type,
                SubType = subType,
                Scale = scale,
                Position = position,
                Angle = angle,
                Light = light,
                Alpha = 1f,
                LifeTime = 1f,
                MaxLifeTime = 1f,
                EnableMove = true,
                StartPosition = position,
                Owner = owner,
            };

            OnParticleCreated(ref particle);
            return index;
        }

        public override void Update(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || Camera.Instance == null)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            OnBeforeParticlesUpdated(dt);
            UpdateParticles(dt);
        }

        public override void Draw(GameTime gameTime)
        {
            if (ActiveCount == 0 || ParticleTexture == null || Status != GameControlStatus.Ready)
                return;

            var camera = Camera.Instance;
            if (camera == null)
                return;

            var device = GraphicsManager.Instance.GraphicsDevice;
            var spriteBatch = GraphicsManager.Instance.Sprite;
            if (device == null || spriteBatch == null)
                return;

            _viewProjection = camera.View * camera.Projection;
            _cameraPosition = camera.Position;

            if (!SpriteBatchScope.BatchIsBegun)
            {
                using var scope = new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    this.BlendState ?? Microsoft.Xna.Framework.Graphics.BlendState.Additive,
                    SamplerState.LinearClamp,
                    DepthStencilState.DepthRead,
                    RasterizerState.CullNone);
                DrawParticles(device.Viewport, camera);
            }
            else
            {
                DrawParticles(device.Viewport, camera);
            }
        }

        public override float Depth => Position.Y + Position.Z;

        protected virtual void OnBeforeParticlesUpdated(float dt)
        {
        }

        protected virtual void OnParticleCreated(ref SourceParticle particle)
        {
        }

        protected virtual void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            if (particle.EnableMove)
            {
                particle.Position += particle.Velocity * dt;
                particle.Velocity.Z += particle.Gravity * dt;
            }
        }

        protected virtual Color GetParticleColor(in SourceParticle particle, float lifeRatio) =>
            new(particle.Light.X, particle.Light.Y, particle.Light.Z, particle.Alpha * lifeRatio * lifeRatio);

        protected virtual float GetParticleScale(in SourceParticle particle, float lifeRatio, float distanceScale, float perspectiveScale)
        {
            float growth = 1f + ScaleGrowth * (1f - lifeRatio);
            return particle.Scale * growth * distanceScale * perspectiveScale;
        }

        protected virtual float GetParticleRotation(in SourceParticle particle) => particle.Rotation;

        protected virtual Rectangle? GetParticleSourceRectangle(Texture2D texture, in SourceParticle particle) => null;

        protected float RandomRange(float min, float max) =>
            min + (float)MuGame.Random.NextDouble() * (max - min);

        protected void UpdateParticles(float dt)
        {
            int i = 0;
            while (i < ActiveCount)
            {
                ref SourceParticle particle = ref Particles[i];
                particle.LifeTime -= dt;

                if (!particle.Live || particle.LifeTime <= 0f)
                {
                    RemoveAt(i);
                    continue;
                }

                UpdateLiveParticle(ref particle, dt);
                i++;
            }
        }

        private void DrawParticles(Viewport viewport, Camera camera)
        {
            Texture2D texture = ParticleTexture!;
            Vector3 forward = Vector3.Normalize(camera.Target - _cameraPosition);
            float maxDistanceSq = MaxDistance * MaxDistance;

            for (int i = 0; i < ActiveCount; i++)
            {
                ref readonly SourceParticle particle = ref Particles[i];
                Vector3 toParticle = particle.Position - _cameraPosition;
                if (Vector3.Dot(toParticle, forward) < 0f)
                    continue;

                float distanceSq = toParticle.LengthSquared();
                if (distanceSq > maxDistanceSq)
                    continue;

                Vector4 clipPosition = Vector4.Transform(particle.Position, _viewProjection);
                if (clipPosition.W <= 0.001f)
                    continue;

                float invW = 1f / clipPosition.W;
                float screenX = (clipPosition.X * invW * 0.5f + 0.5f) * viewport.Width;
                float screenY = (0.5f - clipPosition.Y * invW * 0.5f) * viewport.Height;
                float depth = clipPosition.Z * invW;

                if (depth < 0f || depth > 1f ||
                    screenX < -100f || screenX > viewport.Width + 100f ||
                    screenY < -100f || screenY > viewport.Height + 100f)
                    continue;

                float lifeRatio = particle.MaxLifeTime > 0f
                    ? MathHelper.Clamp(particle.LifeTime / particle.MaxLifeTime, 0f, 1f)
                    : 1f;
                float distanceScale = MathHelper.Lerp(1f, MinDistanceScale, distanceSq / maxDistanceSq);
                float perspectiveScale = clipPosition.W > 0.001f
                    ? MathF.Max(0.1f, ReferenceDistance / clipPosition.W) * Constants.RENDER_SCALE
                    : 1f;

                GraphicsManager.Instance.Sprite.Draw(
                    texture,
                    new Vector2(screenX, screenY),
                    GetParticleSourceRectangle(texture, particle),
                    GetParticleColor(particle, lifeRatio),
                    GetParticleRotation(particle),
                    ParticleTextureCenter,
                    GetParticleScale(particle, lifeRatio, distanceScale, perspectiveScale),
                    SpriteEffects.None,
                    depth);
            }
        }

        private void RemoveAt(int index)
        {
            int last = --ActiveCount;
            if (index != last)
                Particles[index] = Particles[last];

            Particles[last] = default;
        }

    }
}
