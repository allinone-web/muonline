using Client.Main.Controllers;
using Client.Main.Content;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Objects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// SourceMain5.2 CreateShiny equivalent for a dropped item.
    /// It periodically creates BITMAP_SHINY and BITMAP_SHINY+1 at the same local offset.
    /// </summary>
    public sealed class DroppedItemShineEffect : SpriteObject
    {
        private const float SourceShinyIntervalFrames = 48f;
        private const float SourceShinyLifeFrames = 18f;
        private const float SourceShinyRotationSpeed = 12f;

        private readonly DroppedItemObject _owner;
        private readonly ShineParticle[] _particles = new ShineParticle[2];
        private Texture2D _subTypeTexture;
        private float _sourceIntervalFrames;

        private struct ShineParticle
        {
            public bool Active;
            public Vector3 Position;
            public float LifeFrames;
            public float RotationDegrees;
            public byte SubType;
        }

        public override string TexturePath => "Effect/Shiny01.jpg";

        public DroppedItemShineEffect(DroppedItemObject owner)
        {
            _owner = owner;

            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;
            IsTransparent = true;
            AffectedByTransparency = true;
            LightEnabled = false;
            Alpha = 1f;
            BoundingBoxLocal = new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

        public override async Task Load()
        {
            await base.Load();
            if (Status != GameControlStatus.Ready)
                return;

            _subTypeTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/Shiny02.jpg");
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready || _owner == null || !_owner.Visible)
                return;

            float factor = MathF.Max(0.01f, FPSCounter.Instance.FPS_ANIMATION_FACTOR);
            UpdateParticles(factor);

            _sourceIntervalFrames += factor;
            if (_sourceIntervalFrames >= SourceShinyIntervalFrames)
            {
                _sourceIntervalFrames -= SourceShinyIntervalFrames;
                SpawnSourceShiny();
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || SpriteTexture == null || !HasActiveParticle())
                return;

            if (!SpriteBatchScope.BatchIsBegun)
            {
                using (new SpriteBatchScope(
                    GraphicsManager.Instance.Sprite,
                    SpriteSortMode.Deferred,
                    BlendState.Additive,
                    SamplerState.LinearClamp,
                    DepthState))
                {
                    DrawParticles();
                }
            }
            else
            {
                DrawParticles();
            }
        }

        private void UpdateParticles(float factor)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                if (!_particles[i].Active)
                    continue;

                ShineParticle particle = _particles[i];
                particle.LifeFrames -= factor;
                particle.RotationDegrees -= particle.SubType == 1
                    ? SourceShinyRotationSpeed * factor
                    : 0f;

                if (particle.LifeFrames <= 0f)
                {
                    particle.Active = false;
                }

                _particles[i] = particle;
            }
        }

        private void SpawnSourceShiny()
        {
            int localX = MuGame.Random.Next(16, 48);
            int localZ = MuGame.Random.Next(16, 48);
            Vector3 localOffset = new Vector3(localX, 0f, localZ);
            Vector3 rotatedOffset = Vector3.Transform(
                localOffset,
                Matrix.CreateFromQuaternion(MathUtils.AngleQuaternion(_owner.ShineAngle)));
            Vector3 position = _owner.Position + rotatedOffset;

            _particles[0] = new ShineParticle
            {
                Active = true,
                Position = position,
                LifeFrames = SourceShinyLifeFrames,
                RotationDegrees = 0f,
                SubType = 0
            };
            _particles[1] = new ShineParticle
            {
                Active = true,
                Position = position,
                LifeFrames = SourceShinyLifeFrames,
                RotationDegrees = 0f,
                SubType = 1
            };
        }

        private bool HasActiveParticle()
        {
            return _particles[0].Active || _particles[1].Active;
        }

        private void DrawParticles()
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ShineParticle particle = _particles[i];
                if (!particle.Active)
                    continue;

                Vector3 projected = GraphicsDevice.Viewport.Project(
                    particle.Position,
                    Camera.Instance.Projection,
                    Camera.Instance.View,
                    Matrix.Identity);

                if (projected.Z < 0f || projected.Z > 1f)
                    continue;

                float worldScale = WorldPosition.Right.Length();
                if (worldScale <= 0.001f)
                    worldScale = 1f;

                float distance = Vector3.Distance(Camera.Instance.Position, particle.Position);
                float screenScale = worldScale /
                    (MathF.Max(distance, 0.1f) / Constants.TERRAIN_SIZE) *
                    Constants.RENDER_SCALE;

                // Source: Scale = sin(LifeTime * 10 degrees). SubType 1 additionally
                // multiplies the scale by pow(0.75, FPS_ANIMATION_FACTOR).
                float scale = MathF.Sin(MathHelper.ToRadians(particle.LifeFrames * 10f));
                if (particle.SubType == 1)
                    scale *= 0.75f;

                if (scale <= 0.001f)
                    continue;

                Texture2D texture = particle.SubType == 1
                    ? (_subTypeTexture ?? SpriteTexture)
                    : SpriteTexture;

                GraphicsManager.Instance.Sprite.Draw(
                    texture,
                    new Vector2(projected.X, projected.Y),
                    null,
                    Color.White * TotalAlpha,
                    MathHelper.ToRadians(particle.RotationDegrees),
                    new Vector2(SpriteTexture.Width * 0.5f, SpriteTexture.Height * 0.5f),
                    screenScale * scale,
                    SpriteEffects.None,
                    MathHelper.Clamp(projected.Z, 0f, 1f));
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            _subTypeTexture = null;
        }
    }
}
