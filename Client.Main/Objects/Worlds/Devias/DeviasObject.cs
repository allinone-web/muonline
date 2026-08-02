#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Objects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.Devias
{
    /// <summary>
    /// Devias map objects which have object-specific effects in SourceMain5.2.
    /// Types 30 and 66 emit the original fire/smoke particles; type 100 hides
    /// its carrier mesh and renders the two lightning sprites from its bone 0.
    /// </summary>
    public sealed class DeviasObject : MapTileObject
    {
        private readonly DeviasObjectEffect _effect;

        protected override bool AllowMapObjectInstancing => false;

        public DeviasObject()
        {
            LightEnabled = true;
            _effect = new DeviasObjectEffect(this);
            Children.Add(_effect);
        }

        public override async Task Load()
        {
            // SourceMain5.2 initializes these blend meshes in CreateObject
            // before the first render pass.
            if (Type == 54 || Type == 56)
                BlendMesh = 1;
            else if (Type == 92 || Type == 93)
                BlendMesh = 0;

            await base.Load();

            // SourceMain5.2: type 100 has HiddenMesh = -2 and is represented
            // only by the lightning sprites emitted from the model bone.
            if (Type == 100)
                HiddenMesh = -2;
        }
    }

    internal sealed class DeviasObjectEffect : EffectObject
    {
        private const float ReferenceFps = 25f;
        private const int MaxParticles = 48;

        private readonly DeviasObject _owner;
        private readonly Particle[] _particles = new Particle[MaxParticles];

        private Texture2D?[] _fireTextures = Array.Empty<Texture2D?>();
        private Texture2D? _trueFireTexture;
        private Texture2D? _smokeTexture;
        private Texture2D? _lightningTexture;
        private SpriteBatch? _spriteBatch;
        private float _tickAccumulator;
        private int _particleCount;

        private enum ParticleKind
        {
            Fire,
            TrueFire,
            Smoke
        }

        private struct Particle
        {
            public ParticleKind Kind;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Gravity;
            public float Life;
            public float Scale;
            public float Rotation;
            public Vector3 Light;
        }

        public DeviasObjectEffect(DeviasObject owner)
        {
            _owner = owner;
            IsTransparent = true;
            AffectedByTransparency = false;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-500f, -500f, -100f),
                new Vector3(500f, 500f, 700f));
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            _fireTextures = new Texture2D?[]
            {
                await TextureLoader.Instance.PrepareAndGetTexture("Effect/Fire01.jpg"),
                await TextureLoader.Instance.PrepareAndGetTexture("Effect/Fire02.jpg"),
                await TextureLoader.Instance.PrepareAndGetTexture("Effect/Fire03.jpg"),
                await TextureLoader.Instance.PrepareAndGetTexture("Effect/Fire05.jpg")
            };
            _trueFireTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/fantaF.jpg");
            _smokeTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/smoke01.jpg");
            _lightningTexture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/lightning2.jpg");
            _spriteBatch = GraphicsManager.Instance.Sprite;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!_owner.Visible || _owner.Status != GameControlStatus.Ready)
            {
                _particleCount = 0;
                return;
            }

            float frameFactor = (float)gameTime.ElapsedGameTime.TotalSeconds * ReferenceFps;
            _tickAccumulator += MathF.Max(0f, frameFactor);
            while (_tickAccumulator >= 1f)
            {
                _tickAccumulator -= 1f;
                UpdateParticles();
                EmitSourceParticles();
            }
        }

        public override void Draw(GameTime gameTime)
        {
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (_spriteBatch == null || Camera.Instance == null || !_owner.Visible ||
                _owner.Status != GameControlStatus.Ready)
                return;

            if (!SpriteBatchScope.BatchIsBegun)
            {
                if (_owner.Type == 100)
                {
                    using var scope = new SpriteBatchScope(
                        _spriteBatch,
                        SpriteSortMode.Deferred,
                        Blendings.OneOneAdditive,
                        SamplerState.LinearClamp,
                        DepthState,
                        RasterizerState.CullNone);
                    DrawType100Lightning(gameTime);
                }
                else
                {
                    using (var scope = new SpriteBatchScope(
                        _spriteBatch,
                        SpriteSortMode.Deferred,
                        Blendings.OneOneAdditive,
                        SamplerState.LinearClamp,
                        DepthState,
                        RasterizerState.CullNone))
                    {
                        DrawParticleKind(ParticleKind.Fire);
                        DrawParticleKind(ParticleKind.TrueFire);
                    }

                    using (var smokeScope = new SpriteBatchScope(
                        _spriteBatch,
                        SpriteSortMode.Deferred,
                        Blendings.Negative,
                        SamplerState.LinearClamp,
                        DepthState,
                        RasterizerState.CullNone))
                    {
                        DrawParticleKind(ParticleKind.Smoke);
                    }
                }
            }
            else
            {
                if (_owner.Type == 100)
                {
                    DrawType100Lightning(gameTime);
                }
                else
                {
                    DrawParticleKind(ParticleKind.Fire);
                    DrawParticleKind(ParticleKind.TrueFire);
                    DrawParticleKind(ParticleKind.Smoke);
                }
            }
        }

        private void DrawParticleKind(ParticleKind kind)
        {
            for (int i = 0; i < _particleCount; i++)
            {
                ref readonly Particle particle = ref _particles[i];
                if (particle.Kind != kind)
                    continue;

                Texture2D? texture = particle.Kind switch
                {
                    ParticleKind.TrueFire => _trueFireTexture,
                    ParticleKind.Smoke => _smokeTexture,
                    _ => GetFireTexture(particle.Rotation)
                };
                if (texture == null)
                    continue;

                DrawWorldSprite(
                    texture,
                    particle.Position,
                    particle.Light,
                    particle.Rotation,
                    particle.Scale);
            }
        }

        private void DrawType100Lightning(GameTime gameTime)
        {
            if (_lightningTexture == null)
                return;

            Matrix[] bones = _owner.GetBoneTransforms();
            Vector3 position = _owner.Position + new Vector3(0f, 0f, 150f);
            if (bones.Length > 0)
            {
                Vector3 localPosition = Vector3.Transform(
                    new Vector3(0f, 0f, 150f),
                    bones[0]);
                position = Vector3.Transform(localPosition, _owner.WorldPosition);
            }

            float rotation = (int)(gameTime.TotalGameTime.TotalMilliseconds * 0.1f) % 360;
            DrawWorldSprite(_lightningTexture, position, Vector3.One, rotation, 2.5f);
            DrawWorldSprite(_lightningTexture, position, Vector3.One, -rotation, 2.5f);
        }

        private void EmitSourceParticles()
        {
            if (_owner.Type == 30)
            {
                Vector3 position = _owner.Position + new Vector3(0f, 0f, 160f);
                SpawnTrueFire(position, _owner.Scale);
                SpawnSmoke(position, 0.5f + MuGame.Random.Next(9) * 0.1f);
            }
            else if (_owner.Type == 66 && MuGame.Random.Next(2) == 0)
            {
                Vector3 position = _owner.Position + new Vector3(
                    MuGame.Random.Next(-8, 9),
                    MuGame.Random.Next(-8, 9),
                    50f + MuGame.Random.Next(-8, 9));
                SpawnFire(position);
            }
        }

        private void UpdateParticles()
        {
            int writeIndex = 0;
            for (int i = 0; i < _particleCount; i++)
            {
                Particle particle = _particles[i];
                particle.Life -= 1f;
                if (particle.Life <= 0f || particle.Scale <= 0f)
                    continue;

                switch (particle.Kind)
                {
                    case ParticleKind.Fire:
                        particle.Position += particle.Velocity;
                        particle.Gravity += 0.004f;
                        particle.Position.Z += particle.Gravity * 10f;
                        particle.Scale -= 0.04f;
                        particle.Light = new Vector3(particle.Life / 24f);
                        break;
                    case ParticleKind.TrueFire:
                        particle.Position += particle.Velocity;
                        particle.Velocity.X *= 0.95f;
                        particle.Velocity.Y *= 0.95f;
                        particle.Position.Z += 1f;
                        particle.Scale -= 0.02f;
                        particle.Light = new Vector3(particle.Life / 25f);
                        break;
                    case ParticleKind.Smoke:
                        particle.Gravity -= 0.1f;
                        particle.Position.X -= particle.Gravity * 0.2f;
                        particle.Position.Z += particle.Gravity;
                        particle.Scale -= 0.01f;
                        particle.Light = new Vector3(particle.Life / 50f);
                        break;
                }

                if (particle.Life > 0f && particle.Scale > 0f)
                    _particles[writeIndex++] = particle;
            }

            _particleCount = writeIndex;
        }

        private void SpawnFire(Vector3 position)
        {
            if (!TryAddParticle(out Particle particle))
                return;

            particle.Kind = ParticleKind.Fire;
            particle.Position = position;
            particle.Velocity = new Vector3(0f, -((MuGame.Random.Next(16) + 32) * 0.1f), 0f);
            particle.Life = 24f;
            particle.Scale = (MuGame.Random.Next(64) + 128) * 0.01f;
            particle.Rotation = MuGame.Random.Next(360);
            particle.Light = Vector3.One;
            AddParticle(particle);
        }

        private void SpawnTrueFire(Vector3 position, float scale)
        {
            if (!TryAddParticle(out Particle particle))
                return;

            particle.Kind = ParticleKind.TrueFire;
            particle.Position = position;
            particle.Velocity = new Vector3(
                (MuGame.Random.Next(10) - 5) * 0.4f,
                0f,
                (MuGame.Random.Next(10) + 5) * 0.2f);
            particle.Life = 24f;
            particle.Scale = scale;
            particle.Light = Vector3.One;
            AddParticle(particle);
        }

        private void SpawnSmoke(Vector3 position, float scale)
        {
            if (!TryAddParticle(out Particle particle))
                return;

            particle.Kind = ParticleKind.Smoke;
            particle.Position = position;
            particle.Life = 80f;
            particle.Scale = scale * (MuGame.Random.Next(64) + 64) * 0.005f;
            particle.Gravity = (MuGame.Random.Next(32) + 60) * 0.1f;
            particle.Rotation = MuGame.Random.Next(360);
            particle.Light = Vector3.One;
            AddParticle(particle);
        }

        private bool TryAddParticle(out Particle particle)
        {
            particle = default;
            return _particleCount < _particles.Length;
        }

        private void AddParticle(Particle particle)
        {
            _particles[_particleCount++] = particle;
        }

        private Texture2D? GetFireTexture(float rotation)
        {
            if (_fireTextures.Length == 0)
                return null;

            int index = Math.Abs((int)rotation) % _fireTextures.Length;
            return _fireTextures[index];
        }

        private void DrawWorldSprite(
            Texture2D texture,
            Vector3 position,
            Vector3 light,
            float rotationDegrees,
            float scale)
        {
            Vector3 projected = GraphicsDevice.Viewport.Project(
                position,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);
            if (projected.Z < 0f || projected.Z > 1f)
                return;

            Matrix inverseView = Matrix.Invert(Camera.Instance.View);
            Vector3 viewPosition = Vector3.Transform(position, Camera.Instance.View);
            Vector3 projectedWidth = GraphicsDevice.Viewport.Project(
                Vector3.Transform(
                    viewPosition + new Vector3(texture.Width * scale, 0f, 0f),
                    inverseView),
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);
            Vector3 projectedHeight = GraphicsDevice.Viewport.Project(
                Vector3.Transform(
                    viewPosition + new Vector3(0f, texture.Height * scale, 0f),
                    inverseView),
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);

            Vector2 spriteScale = new(
                MathF.Abs(projectedWidth.X - projected.X) / texture.Width,
                MathF.Abs(projectedHeight.Y - projected.Y) / texture.Height);
            if (!float.IsFinite(spriteScale.X) || !float.IsFinite(spriteScale.Y) ||
                spriteScale.X <= 0f || spriteScale.Y <= 0f)
                return;

            _spriteBatch!.Draw(
                texture,
                new Vector2(projected.X, projected.Y),
                null,
                new Color(light),
                -MathHelper.ToRadians(rotationDegrees),
                new Vector2(texture.Width * 0.5f, texture.Height * 0.5f),
                spriteScale,
                SpriteEffects.None,
                MathHelper.Clamp(projected.Z, 0f, 1f));
        }
    }
}
