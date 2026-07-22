using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Login
{
    public class WaterSplashObject : ModelObject
    {
        private LegacyWaterfallParticleSystem _particleSystem;

        public override WorldObjectRenderPolicy RenderPolicy => base.RenderPolicy.With(alwaysUpdate: true);
        protected override bool RequiresPerFrameAnimation => true;

        public override async Task Load()
        {
            // Type 54 is the invisible Object73 type 82 effect marker.
            HiddenMesh = -2;

            _particleSystem = new LegacyWaterfallParticleSystem
            {
                Position = Position,
                Angle = Angle,
                SourceScale = Scale,
                World = World
            };

            World.Objects.Add(_particleSystem);
            await _particleSystem.Load();
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (_particleSystem == null)
                return;

            _particleSystem.Position = Position;
            _particleSystem.Angle = Angle;
            _particleSystem.SourceScale = Scale;
        }

        private sealed class LegacyWaterfallParticleSystem : SourceParticleSystem
        {
            private const short Waterfall5Marker = 54;
            private const int MaxParticles = 64;
            private const float FramesPerSecond = 60f;

            private Texture2D _texture;
            private Vector2 _textureCenter;

            public float SourceScale { get; set; }

            protected override Texture2D ParticleTexture => _texture;
            protected override Vector2 ParticleTextureCenter => _textureCenter;

            public LegacyWaterfallParticleSystem()
                : base(MaxParticles)
            {
                MaxDistance = 50000f;
                ReferenceDistance = 800f;
                MinDistanceScale = 1f;
                ScaleGrowth = 0f;
            }

            public override async Task LoadContent()
            {
                _texture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/waterFall5.OZJ");
                if (_texture != null)
                    _textureCenter = new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);
            }

            protected override void OnBeforeParticlesUpdated(float dt)
            {
                if (!FPSCounter.Instance.RandFPSCheck(1))
                    return;

                CreateParticle(
                    type: Waterfall5Marker,
                    position: Position,
                    angle: Angle,
                    light: Vector3.One,
                    subType: 9,
                    scale: SourceScale);
            }

            protected override void OnParticleCreated(ref SourceParticle particle)
            {
                particle.LifeTime = 30f / FramesPerSecond;
                particle.MaxLifeTime = particle.LifeTime;
                particle.Rotation = MathHelper.ToRadians(MuGame.Random.Next(360));
                particle.Scale = 0.6f + SourceScale;
                particle.Velocity.Z = -(MuGame.Random.Next(5) + 7);
                particle.Light = new Vector3(0.2f);
            }

            protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
            {
                float animationFactor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;

                if (particle.EnableMove)
                {
                    Matrix rotation = Matrix.CreateFromYawPitchRoll(
                        particle.Angle.Y,
                        particle.Angle.X,
                        particle.Angle.Z);
                    particle.Position += Vector3.TransformNormal(particle.Velocity, rotation) * animationFactor;
                }

                particle.Scale -= 0.005f * animationFactor;
                particle.Velocity.Z += 0.1f * animationFactor;

                float lifeFrames = particle.LifeTime * FramesPerSecond;
                if (lifeFrames < 8f)
                    particle.Light *= MathF.Pow(1f / 1.2f, animationFactor);
                else if (lifeFrames > 20f)
                    particle.Light *= MathF.Pow(1.1f, animationFactor);
            }

            protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio) =>
                new Color(particle.Light.X, particle.Light.Y, particle.Light.Z, particle.Alpha);

            protected override float GetParticleScale(
                in SourceParticle particle,
                float lifeRatio,
                float distanceScale,
                float perspectiveScale) => particle.Scale * perspectiveScale;
        }
    }
}
