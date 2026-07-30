#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Fixed-pool forge effect used by Hanzo. It combines a persistent smoke plume,
    /// impact smoke, four joint sparks, four flying sparks and a short dynamic-light pulse.
    /// </summary>
    public sealed class BlacksmithForgeEffect : EffectObject
    {
        private const string SmokeTexturePath = "Effect/smoke01.jpg";
        private const string JointSparkTexturePath = "Effect/Spark01.jpg";
        private const string SparkTexturePath = "Effect/Spark02.jpg";

        private const int MaxSmokePuffs = 32;
        private const int MaxJointSparks = 16;
        private const int MaxSparks = 16;
        private const int AmbientSmokeIntervalTicks = 3;
        private const int ImpactSmokeCount = 6;
        private const double LegacyStepSeconds = 1.0 / 25.0;
        private const int MaxCatchUpTicks = 8;

        private static readonly BlendState MuRgbAdditiveBlend = new()
        {
            ColorBlendFunction = BlendFunction.Add,
            ColorSourceBlend = Blend.One,
            ColorDestinationBlend = Blend.One,
            AlphaBlendFunction = BlendFunction.Add,
            AlphaSourceBlend = Blend.One,
            AlphaDestinationBlend = Blend.One
        };

        private struct SmokePuff
        {
            public bool Active;
            public Vector3 Position;
            public Vector3 Velocity;
            public float RiseSpeed;
            public float LifeTicks;
            public float MaxLifeTicks;
            public float Rotation;
            public float RotationSpeed;
            public float Scale;
            public float ScaleGrowth;
            public float Brightness;
        }

        private struct JointSpark
        {
            public bool Active;
            public Vector3 Position;
            public Vector3 Velocity;
            public float LifeTicks;
            public float MaxLifeTicks;
            public float Rotation;
            public Vector2 Scale;
        }

        private struct Spark
        {
            public bool Active;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Gravity;
            public float LifeTicks;
            public float MaxLifeTicks;
            public float Rotation;
            public float Scale;
        }

        private readonly SmokePuff[] _smokePuffs = new SmokePuff[MaxSmokePuffs];
        private readonly JointSpark[] _jointSparks = new JointSpark[MaxJointSparks];
        private readonly Spark[] _sparks = new Spark[MaxSparks];
        private readonly DynamicLight _forgeLight;

        private Texture2D? _smokeTexture;
        private Texture2D? _jointSparkTexture;
        private Texture2D? _sparkTexture;
        private SpriteBatch? _spriteBatch;
        private double _legacyAccumulator;
        private float _pulseTicks;
        private int _ambientSmokeTicks;
        private bool _lightRegistered;
        private bool _hasForgeOrigin;
        private Vector3 _forgeOrigin;

        public float CurrentLuminosity { get; private set; }

        public BlacksmithForgeEffect()
        {
            IsTransparent = true;
            AffectedByTransparency = false;
            BlendState = MuRgbAdditiveBlend;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-300f, -300f, -150f),
                new Vector3(300f, 300f, 360f));

            _forgeLight = new DynamicLight
            {
                Owner = this,
                Color = new Vector3(1f, 0.4f, 0f),
                Radius = Constants.TERRAIN_SCALE * 3f,
                Intensity = 0f
            };
        }

        public override async Task LoadContent()
        {
            _smokeTexture = await TextureLoader.Instance.PrepareAndGetTexture(SmokeTexturePath);
            _jointSparkTexture = await TextureLoader.Instance.PrepareAndGetTexture(JointSparkTexturePath);
            _sparkTexture = await TextureLoader.Instance.PrepareAndGetTexture(SparkTexturePath);
            _spriteBatch = GraphicsManager.Instance.Sprite;

            EnsureLightRegistered();
        }

        public void SetForgeOrigin(Vector3 worldPosition)
        {
            if (!IsFinite(worldPosition))
                return;

            _forgeOrigin = worldPosition;
            if (!_hasForgeOrigin)
                _ambientSmokeTicks = AmbientSmokeIntervalTicks;
            _hasForgeOrigin = true;
        }

        public void EmitBurst(Vector3 worldPosition, Vector3 ownerAngle)
        {
            if (!IsFinite(worldPosition))
                return;

            _forgeLight.Position = worldPosition;
            _pulseTicks = 10f;
            CurrentLuminosity = 1f;

            Vector3 smokeOrigin = _hasForgeOrigin ? _forgeOrigin : worldPosition;
            for (int i = 0; i < ImpactSmokeCount; i++)
                SpawnSmoke(smokeOrigin, impact: true);

            for (int i = 0; i < 4; i++)
            {
                Vector3 angle = new(
                    MuGame.Random.Next(150, 210),
                    0f,
                    MathHelper.ToDegrees(ownerAngle.Z) + MuGame.Random.Next(0, 30));

                Vector3 direction = BuildDirection(angle);
                Vector3 jitteredPosition = worldPosition + new Vector3(
                    RandomRange(-4f, 4f),
                    RandomRange(-4f, 4f),
                    RandomRange(-3f, 5f));

                SpawnJointSpark(jitteredPosition, direction, angle.Z);
                SpawnSpark(jitteredPosition, direction, angle.Z);
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready)
                return;

            EnsureLightRegistered();

            double elapsed = Math.Min(
                gameTime.ElapsedGameTime.TotalSeconds,
                LegacyStepSeconds * MaxCatchUpTicks);
            _legacyAccumulator += elapsed;

            while (_legacyAccumulator >= LegacyStepSeconds)
            {
                UpdateOneLegacyTick();
                _legacyAccumulator -= LegacyStepSeconds;
            }

            _forgeLight.Intensity = CurrentLuminosity * 1.75f;
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);

            if (_spriteBatch == null || Camera.Instance == null)
                return;

            if (_smokeTexture != null)
            {
                using (new SpriteBatchScope(
                    _spriteBatch,
                    SpriteSortMode.Deferred,
                    MuRgbAdditiveBlend,
                    SamplerState.LinearClamp,
                    DepthStencilState.DepthRead,
                    RasterizerState.CullNone))
                {
                    DrawSmokePuffs();
                }
            }

            if (_jointSparkTexture == null && _sparkTexture == null)
                return;

            using (new SpriteBatchScope(
                _spriteBatch,
                SpriteSortMode.Deferred,
                MuRgbAdditiveBlend,
                SamplerState.LinearClamp,
                DepthStencilState.DepthRead,
                RasterizerState.CullNone))
            {
                DrawJointSparks();
                DrawSparks();
            }
        }

        public override void Dispose()
        {
            if (_lightRegistered && World?.Terrain != null)
            {
                World.Terrain.RemoveDynamicLight(_forgeLight);
                _lightRegistered = false;
            }

            base.Dispose();
        }

        private void EnsureLightRegistered()
        {
            if (_lightRegistered || World?.Terrain == null)
                return;

            World.Terrain.AddDynamicLight(_forgeLight);
            _lightRegistered = true;
        }

        private void UpdateOneLegacyTick()
        {
            if (_pulseTicks > 0f)
            {
                _pulseTicks -= 1f;
                float pulseRatio = MathHelper.Clamp(_pulseTicks / 10f, 0f, 1f);
                CurrentLuminosity = pulseRatio * pulseRatio;
            }
            else
            {
                CurrentLuminosity = 0f;
            }

            UpdateSmokePuffs();
            UpdateJointSparks();
            UpdateSparks();

            if (_hasForgeOrigin && Parent?.Visible == true)
            {
                _ambientSmokeTicks++;
                if (_ambientSmokeTicks >= AmbientSmokeIntervalTicks)
                {
                    _ambientSmokeTicks = 0;
                    SpawnSmoke(_forgeOrigin, impact: false);
                }
            }
        }

        private void UpdateSmokePuffs()
        {
            for (int i = 0; i < _smokePuffs.Length; i++)
            {
                ref SmokePuff puff = ref _smokePuffs[i];
                if (!puff.Active)
                    continue;

                puff.LifeTicks -= 1f;
                if (puff.LifeTicks <= 0f)
                {
                    puff.Active = false;
                    continue;
                }

                puff.Position += puff.Velocity;
                puff.Position.Z += puff.RiseSpeed;
                puff.RiseSpeed += 0.07f;
                puff.Velocity *= 0.94f;
                puff.Scale += puff.ScaleGrowth;
                puff.Rotation += puff.RotationSpeed;
            }
        }

        private void UpdateJointSparks()
        {
            for (int i = 0; i < _jointSparks.Length; i++)
            {
                ref JointSpark spark = ref _jointSparks[i];
                if (!spark.Active)
                    continue;

                spark.LifeTicks -= 1f;
                if (spark.LifeTicks <= 0f)
                {
                    spark.Active = false;
                    continue;
                }

                spark.Position += spark.Velocity;
                spark.Velocity *= 0.86f;
                spark.Scale.X *= 0.96f;
                spark.Scale.Y *= 0.93f;
            }
        }

        private void UpdateSparks()
        {
            for (int i = 0; i < _sparks.Length; i++)
            {
                ref Spark spark = ref _sparks[i];
                if (!spark.Active)
                    continue;

                spark.LifeTicks -= 1f;
                if (spark.LifeTicks <= 0f)
                {
                    spark.Active = false;
                    continue;
                }

                spark.Position += spark.Velocity;
                spark.Position.Z += spark.Gravity;
                spark.Gravity -= 1.15f;
                spark.Velocity *= 0.97f;
                spark.Scale *= 0.975f;
            }
        }

        private void SpawnSmoke(Vector3 origin, bool impact)
        {
            int index = FindFreeSmokePuff();
            if (index < 0)
                return;

            float life = impact
                ? MuGame.Random.Next(18, 27)
                : MuGame.Random.Next(24, 35);

            _smokePuffs[index] = new SmokePuff
            {
                Active = true,
                Position = origin + new Vector3(
                    RandomRange(impact ? -12f : -7f, impact ? 12f : 7f),
                    RandomRange(impact ? -12f : -7f, impact ? 12f : 7f),
                    RandomRange(impact ? -3f : 0f, impact ? 8f : 5f)),
                Velocity = new Vector3(
                    RandomRange(impact ? -1.8f : -0.45f, impact ? 1.8f : 0.45f),
                    RandomRange(impact ? -1.8f : -0.45f, impact ? 1.8f : 0.45f),
                    0f),
                RiseSpeed = RandomRange(impact ? 1.3f : 0.65f, impact ? 2.8f : 1.25f),
                LifeTicks = life,
                MaxLifeTicks = life,
                Rotation = RandomRange(0f, MathHelper.TwoPi),
                RotationSpeed = RandomRange(-0.035f, 0.035f),
                Scale = RandomRange(impact ? 0.78f : 0.62f, impact ? 1.15f : 0.88f),
                ScaleGrowth = RandomRange(impact ? 0.04f : 0.03f, impact ? 0.07f : 0.052f),
                Brightness = RandomRange(impact ? 0.9f : 0.68f, impact ? 1.0f : 0.88f)
            };
        }

        private void SpawnJointSpark(Vector3 position, Vector3 direction, float rotationDegrees)
        {
            int index = FindFreeJointSpark();
            if (index < 0)
                return;

            float life = MuGame.Random.Next(11, 17);
            _jointSparks[index] = new JointSpark
            {
                Active = true,
                Position = position,
                Velocity = direction * MuGame.Random.Next(10, 18),
                LifeTicks = life,
                MaxLifeTicks = life,
                Rotation = MathHelper.ToRadians(rotationDegrees),
                Scale = new Vector2(2.35f, 0.58f)
            };
        }

        private void SpawnSpark(Vector3 position, Vector3 direction, float rotationDegrees)
        {
            int index = FindFreeSpark();
            if (index < 0)
                return;

            float life = MuGame.Random.Next(20, 31);
            _sparks[index] = new Spark
            {
                Active = true,
                Position = position,
                Velocity = direction * MuGame.Random.Next(5, 12),
                Gravity = MuGame.Random.Next(8, 17),
                LifeTicks = life,
                MaxLifeTicks = life,
                Rotation = MathHelper.ToRadians(rotationDegrees),
                Scale = MuGame.Random.Next(72, 116) * 0.01f
            };
        }

        private void DrawSmokePuffs()
        {
            for (int i = 0; i < _smokePuffs.Length; i++)
            {
                ref readonly SmokePuff puff = ref _smokePuffs[i];
                if (!puff.Active)
                    continue;

                float lifeRatio = MathHelper.Clamp(
                    puff.LifeTicks / MathF.Max(1f, puff.MaxLifeTicks),
                    0f,
                    1f);
                float fade = lifeRatio * lifeRatio;
                float intensity = puff.Brightness * fade;
                float smokeIntensity = MathHelper.Clamp(intensity, 0f, 1f);
                Color color = new(
                    smokeIntensity,
                    smokeIntensity,
                    smokeIntensity,
                    1f);

                DrawWorldSprite(
                    _smokeTexture!,
                    puff.Position,
                    color,
                    puff.Rotation,
                    new Vector2(puff.Scale));
            }
        }

        private void DrawJointSparks()
        {
            if (_jointSparkTexture == null)
                return;

            for (int i = 0; i < _jointSparks.Length; i++)
            {
                ref readonly JointSpark spark = ref _jointSparks[i];
                if (!spark.Active)
                    continue;

                float lifeRatio = MathHelper.Clamp(
                    spark.LifeTicks / MathF.Max(1f, spark.MaxLifeTicks),
                    0f,
                    1f);
                float intensity = MathHelper.Clamp(lifeRatio * 1.8f, 0f, 1f);
                Color color = new(intensity, 0.78f * intensity, 0.32f * intensity, 1f);

                DrawWorldSprite(
                    _jointSparkTexture,
                    spark.Position,
                    color,
                    spark.Rotation,
                    spark.Scale);
                DrawWorldSprite(
                    _jointSparkTexture,
                    spark.Position,
                    Color.White,
                    spark.Rotation,
                    spark.Scale * 0.48f);
            }
        }

        private void DrawSparks()
        {
            if (_sparkTexture == null)
                return;

            for (int i = 0; i < _sparks.Length; i++)
            {
                ref readonly Spark spark = ref _sparks[i];
                if (!spark.Active)
                    continue;

                float lifeRatio = MathHelper.Clamp(
                    spark.LifeTicks / MathF.Max(1f, spark.MaxLifeTicks),
                    0f,
                    1f);
                float intensity = MathHelper.Clamp(lifeRatio * 1.65f, 0f, 1f);
                Color color = new(intensity, 0.86f * intensity, 0.42f * intensity, 1f);

                Vector2 outerScale = new(spark.Scale);
                DrawWorldSprite(
                    _sparkTexture,
                    spark.Position,
                    color,
                    spark.Rotation,
                    outerScale);
                DrawWorldSprite(
                    _sparkTexture,
                    spark.Position,
                    Color.White,
                    spark.Rotation,
                    outerScale * 0.55f);
            }
        }

        private void DrawWorldSprite(
            Texture2D texture,
            Vector3 worldPosition,
            Color color,
            float rotation,
            Vector2 worldScale)
        {
            var camera = Camera.Instance;
            var viewport = GraphicsDevice.Viewport;
            Matrix viewProjection = camera.View * camera.Projection;
            Vector4 clip = Vector4.Transform(worldPosition, viewProjection);
            if (clip.W <= 0.001f)
                return;

            float invW = 1f / clip.W;
            float depth = clip.Z * invW;
            if (depth < 0f || depth > 1f)
                return;

            Vector2 screenPosition = new(
                (clip.X * invW * 0.5f + 0.5f) * viewport.Width,
                (0.5f - clip.Y * invW * 0.5f) * viewport.Height);

            // The off-screen render target already accounts for RENDER_SCALE. Applying
            // it again made sparks and smoke shrink a second time at reduced render scale.
            float projectionScale = viewport.Height * camera.Projection.M22 * 0.5f * invW;

            _spriteBatch!.Draw(
                texture,
                screenPosition,
                null,
                color,
                rotation,
                new Vector2(texture.Width * 0.5f, texture.Height * 0.5f),
                worldScale * projectionScale,
                SpriteEffects.None,
                depth);
        }

        private static Vector3 BuildDirection(Vector3 angleDegrees)
        {
            float pitch = MathHelper.ToRadians(angleDegrees.X - 180f);
            float yaw = MathHelper.ToRadians(angleDegrees.Z);
            float horizontal = MathF.Cos(pitch);

            Vector3 direction = new(
                MathF.Cos(yaw) * horizontal,
                MathF.Sin(yaw) * horizontal,
                MathF.Sin(pitch));

            if (direction.LengthSquared() < 0.0001f)
                return Vector3.UnitZ;

            direction.Normalize();
            return direction;
        }

        private static float RandomRange(float min, float max) =>
            min + (float)MuGame.Random.NextDouble() * (max - min);

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

        private int FindFreeSmokePuff()
        {
            for (int i = 0; i < _smokePuffs.Length; i++)
            {
                if (!_smokePuffs[i].Active)
                    return i;
            }

            return -1;
        }

        private int FindFreeJointSpark()
        {
            for (int i = 0; i < _jointSparks.Length; i++)
            {
                if (!_jointSparks[i].Active)
                    return i;
            }

            return -1;
        }

        private int FindFreeSpark()
        {
            for (int i = 0; i < _sparks.Length; i++)
            {
                if (!_sparks[i].Active)
                    return i;
            }

            return -1;
        }
    }
}
