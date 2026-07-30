using Client.Main.Content;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Noria
{
    public class LightBeamObject : ModelObject
    {
        public LightBeamObject()
        {
            Children.Add(new NoriaLightBeamParticleSystem());
        }

        public override async Task Load()
        {
            BlendState = BlendState.NonPremultiplied;
            BlendMesh = 0;
            BlendMeshState = BlendState.Additive;
            LightEnabled = true;
            IsTransparent = true;
            Model = await BMDLoader.Instance.Prepare($"Object4/Object38.bmd");

            await base.Load();
        }

        private sealed class NoriaLightBeamParticleSystem : SourceParticleSystem
        {
            private const int MaxParticles = 20;
            private const float EmissionRate = 6f;
            private Texture2D _texture;
            private Vector2 _textureCenter;
            private float _emissionAccumulator;

            protected override Texture2D ParticleTexture => _texture;
            protected override Vector2 ParticleTextureCenter => _textureCenter;

            public NoriaLightBeamParticleSystem()
                : base(MaxParticles)
            {
                BlendState = BlendState.Additive;
                MaxDistance = 1800f;
                ReferenceDistance = 800f;
                ScaleGrowth = 0.7f;
            }

            public override async Task LoadContent()
            {
                _texture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/fi01.jpg");
                if (_texture != null)
                    _textureCenter = new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);
            }

            protected override void OnBeforeParticlesUpdated(float dt)
            {
                if (_texture == null || Camera.Instance == null)
                    return;

                Vector3 emitterPosition = WorldPosition.Translation;
                if (Vector3.DistanceSquared(Camera.Instance.Position, emitterPosition) > MaxDistance * MaxDistance)
                    return;

                _emissionAccumulator += EmissionRate * dt;
                int emitCount = (int)_emissionAccumulator;
                _emissionAccumulator -= emitCount;

                for (int i = 0; i < emitCount; i++)
                {
                    CreateParticle(
                        type: 0,
                        position: emitterPosition,
                        angle: Vector3.Zero,
                        light: Vector3.One);
                }
            }

            protected override void OnParticleCreated(ref SourceParticle particle)
            {
                float lifetime = RandomRange(0.7f, 1.15f);
                particle.LifeTime = lifetime;
                particle.MaxLifeTime = lifetime;
                particle.Scale = RandomRange(0.55f, 0.9f);
                particle.Rotation = RandomRange(0f, MathHelper.TwoPi);
                particle.Velocity = new Vector3(
                    RandomRange(-8f, 8f),
                    RandomRange(-8f, 8f),
                    RandomRange(22f, 38f));
            }

            protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
            {
                particle.Position += particle.Velocity * dt;
                particle.Rotation += dt * 0.35f;
            }

            protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio)
            {
                float alpha = lifeRatio * lifeRatio * 0.65f;
                return Color.White * alpha;
            }
        }
    }
}
