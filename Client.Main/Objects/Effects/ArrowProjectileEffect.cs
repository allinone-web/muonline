#nullable enable
using System;
using System.Threading.Tasks;
using Client.Data.ATT;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    public enum ArrowVolleyKind : byte
    {
        Normal,
        TripleShot,
        IceArrow,
        Penetration,
        DeepImpact,
        MultiShot
    }

    /// <summary>
    /// A complete client-side bow/crossbow projectile volley. Every visible arrow is a real BMD
    /// model which moves independently from the shooter. Simulation and emission use the original
    /// 25 Hz reference step, while render positions are interpolated between ticks.
    /// </summary>
    public sealed class ArrowProjectileEffect : EffectObject
    {
        private const float LegacyStepSeconds = 1f / 25f;
        private const int MaxLegacyStepsPerFrame = 5;
        private const int MaxProjectiles = 4;
        private const int MaxParticles = 384;
        private const int MaxBillboardQuads = MaxParticles;
        private const int DefaultLifeTicks = 30;
        private const float TargetCollisionRadius = 100f;
        private const float TargetImpactHeightRatio = 0.55f;

        private static readonly float[] SingleSpread = { 0f };
        private static readonly float[] TripleSpread = { 0f, 15f, -15f };
        private static readonly float[] QuadSpread = { 5f, 15f, -5f, -15f };

        private readonly PlayerObject _shooter;
        private readonly WalkableWorldControl _walkableWorld;
        private readonly ushort _targetId;
        private readonly Vector3? _fallbackTargetPosition;
        private readonly ArrowVolleyKind _volleyKind;
        private readonly ProjectileProfile _profile;
        private readonly float _visualPower;
        private readonly int _trailSampleCount;
        private readonly bool _enhancedProjectile;
        private readonly float _modelScaleMultiplier;
        private readonly float _lightIntensity;
        private readonly ProjectileState[] _projectiles = new ProjectileState[MaxProjectiles];
        private readonly TrailParticle[] _particles = new TrailParticle[MaxParticles];
        private readonly DynamicLight[] _lights = new DynamicLight[MaxProjectiles];
        private readonly VertexPositionColorTexture[] _vertices =
            new VertexPositionColorTexture[MaxBillboardQuads * 4];
        private readonly short[] _indices = new short[MaxBillboardQuads * 6];

        private ProjectileModelRenderer? _modelRenderer;
        private Texture2D? _fireTexture;
        private Texture2D? _energyTexture;
        private Texture2D? _flareTexture;
        private Texture2D? _smokeTexture;
        private BasicEffect? _billboardEffect;
        private float _legacyAccumulator;
        private float _renderInterpolation;
        private int _projectileCount;
        private int _particleCount;
        private int _particleWriteCursor;
        private bool _initialized;
        private bool _lightsAdded;
        private bool _removing;
        private bool _disposed;

        private enum ProjectileStyle : byte
        {
            Basic,
            Steel,
            Saw,
            Laser,
            Thunder,
            V,
            Nature,
            Holy,
            Lace,
            Wing,
            Bomb,
            Double,
            BestCrossbow,
            Drill,
            Spark,
            Ring,
            DarkStinger,
            Gamble,
            Impact
        }

        private enum ParticleKind : byte
        {
            Fire,
            Energy,
            Flare,
            Smoke
        }

        private readonly struct ProjectileProfile
        {
            public ProjectileProfile(
                ProjectileStyle style,
                string[] modelCandidates,
                float scale,
                float speedPerTick,
                float rollPerTickDegrees,
                int blendMesh,
                Vector3 trailColor,
                Vector3 lightColor,
                float lightRadius,
                float trailSize,
                bool useJumpTrajectory = false,
                bool homeToTarget = false)
            {
                Style = style;
                ModelCandidates = modelCandidates;
                Scale = scale;
                SpeedPerTick = speedPerTick;
                RollPerTickDegrees = rollPerTickDegrees;
                BlendMesh = blendMesh;
                TrailColor = trailColor;
                LightColor = lightColor;
                LightRadius = lightRadius;
                TrailSize = trailSize;
                UseJumpTrajectory = useJumpTrajectory;
                HomeToTarget = homeToTarget;
            }

            public ProjectileStyle Style { get; }
            public string[] ModelCandidates { get; }
            public float Scale { get; }
            public float SpeedPerTick { get; }
            public float RollPerTickDegrees { get; }
            public int BlendMesh { get; }
            public Vector3 TrailColor { get; }
            public Vector3 LightColor { get; }
            public float LightRadius { get; }
            public float TrailSize { get; }
            public bool UseJumpTrajectory { get; }
            public bool HomeToTarget { get; }
        }

        private struct ProjectileState
        {
            public bool Active;
            public bool HitTarget;
            public bool Stopped;
            public Vector3 PreviousPosition;
            public Vector3 Position;
            public Vector3 Direction;
            public Vector3 AngleRadians;
            public float RollDegrees;
            public float VerticalVelocity;
            public int LifeTicks;
        }

        private struct TrailParticle
        {
            public bool Active;
            public ParticleKind Kind;
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 Color;
            public float Age;
            public float Life;
            public float StartSize;
            public float EndSize;
            public float Rotation;
            public float RotationSpeed;
        }

        public ArrowProjectileEffect(
            PlayerObject shooter,
            WalkableWorldControl world,
            ushort targetId,
            Vector3? targetPosition,
            ArrowVolleyKind volleyKind)
        {
            _shooter = shooter ?? throw new ArgumentNullException(nameof(shooter));
            _walkableWorld = world ?? throw new ArgumentNullException(nameof(world));
            _targetId = (ushort)(targetId & 0x7FFF);
            _fallbackTargetPosition = targetPosition;
            _volleyKind = volleyKind;
            _profile = SelectProfile(shooter, volleyKind);
            _visualPower = ResolveVisualPower(_profile.Style, volleyKind);
            _trailSampleCount = ResolveTrailSampleCount(_profile.Style, volleyKind);
            _enhancedProjectile = _visualPower > 1.05f;
            _modelScaleMultiplier = MathF.Min(1.22f, 1f + (_visualPower - 1f) * 0.18f);
            _lightIntensity = MathF.Min(1.25f, 0.75f + (_visualPower - 1f) * 0.34f);

            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-2600f, -2600f, -600f),
                new Vector3(2600f, 2600f, 1600f));

            BuildStaticIndices();
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            string modelPath = await ResolveModelPath(_profile.ModelCandidates);
            _modelRenderer = new ProjectileModelRenderer(modelPath, _profile.BlendMesh)
            {
                World = World
            };
            await _modelRenderer.Load();

            _fireTexture = await PrepareFirstTexture(
                "Effect/fire01.jpg",
                "Effect/Fire01.jpg",
                "Effect/fire02.jpg",
                "Effect/Fire02.jpg");
            _energyTexture = await PrepareFirstTexture(
                "Effect/energy.jpg",
                "Effect/Energy.jpg",
                "Effect/fire03.jpg");
            _flareTexture = await PrepareFirstTexture(
                "Effect/flare01.jpg",
                "Effect/Flare01.jpg",
                "Effect/flare.jpg");
            _smokeTexture = await PrepareFirstTexture(
                "Effect/smoke01.jpg",
                "Effect/Smoke01.jpg");

            _billboardEffect = new BasicEffect(GraphicsDevice)
            {
                TextureEnabled = true,
                VertexColorEnabled = true,
                LightingEnabled = false,
                FogEnabled = false,
                World = Matrix.Identity
            };
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status == GameControlStatus.NonInitialized)
                _ = Load();

            if (Status != GameControlStatus.Ready)
                return;

            if (!_initialized)
                InitializeVolley();

            _modelRenderer?.Update(gameTime);

            float elapsedSeconds = MathHelper.Clamp(
                (float)gameTime.ElapsedGameTime.TotalSeconds,
                0f,
                LegacyStepSeconds * MaxLegacyStepsPerFrame);

            _legacyAccumulator += elapsedSeconds;
            int steps = 0;
            while (_legacyAccumulator >= LegacyStepSeconds && steps < MaxLegacyStepsPerFrame)
            {
                TickLegacy();
                _legacyAccumulator -= LegacyStepSeconds;
                steps++;
            }

            if (steps == MaxLegacyStepsPerFrame)
                _legacyAccumulator = MathF.Min(_legacyAccumulator, LegacyStepSeconds);

            _renderInterpolation = MathHelper.Clamp(_legacyAccumulator / LegacyStepSeconds, 0f, 1f);
            UpdateParticles(elapsedSeconds);
            UpdateEffectAnchor();
            UpdateLights();

            if (!HasActiveProjectiles() && _particleCount == 0)
                RemoveSelf();
        }

        private void InitializeVolley()
        {
            _initialized = true;
            Vector3 start = ResolveStartPosition();
            Vector3 target = ResolveTargetPosition(start);
            float baseYaw = ResolveBaseYaw(start, target);
            ReadOnlySpan<float> spreads = ResolveSpreadAngles();

            _projectileCount = spreads.Length;
            for (int i = 0; i < _projectileCount; i++)
            {
                float yaw = baseYaw + MathHelper.ToRadians(spreads[i]);
                Vector3 direction = _profile.HomeToTarget
                    ? ResolveDirection(start, target, yaw)
                    : new Vector3(MathF.Sin(yaw), -MathF.Cos(yaw), 0f);
                Vector3 angles = DirectionToModelAngles(direction, yaw);

                _projectiles[i] = new ProjectileState
                {
                    Active = true,
                    PreviousPosition = start,
                    Position = start,
                    Direction = direction,
                    AngleRadians = angles,
                    RollDegrees = MuGame.Random.Next(0, 360),
                    VerticalVelocity = _profile.UseJumpTrajectory ? 18f : 0f,
                    LifeTicks = DefaultLifeTicks
                };

                _lights[i] = new DynamicLight
                {
                    Owner = this,
                    Position = start,
                    Color = ResolveLightColor(),
                    Radius = _profile.LightRadius * (1f + (_visualPower - 1f) * 0.24f),
                    Intensity = _lightIntensity
                };
            }

            if (World?.Terrain != null)
            {
                for (int i = 0; i < _projectileCount; i++)
                    World.Terrain.AddDynamicLight(_lights[i]);
                _lightsAdded = true;
            }

            Position = start;
            EmitLaunchFlash(start, baseYaw);
        }

        private ReadOnlySpan<float> ResolveSpreadAngles()
        {
            return _volleyKind switch
            {
                ArrowVolleyKind.TripleShot => TripleSpread,
                ArrowVolleyKind.MultiShot when UsesFourProjectileMultiShot(_profile.Style)
                    => QuadSpread,
                ArrowVolleyKind.MultiShot => TripleSpread,
                _ => SingleSpread
            };
        }

        private void TickLegacy()
        {
            for (int i = 0; i < _projectileCount; i++)
            {
                ref ProjectileState projectile = ref _projectiles[i];
                if (!projectile.Active)
                    continue;

                projectile.PreviousPosition = projectile.Position;

                if (projectile.Stopped)
                {
                    // Original no-attack-zone behavior: the arrow stops and its remaining
                    // lifetime is halved on every 25 Hz reference tick.
                    projectile.LifeTicks /= 2;
                    if (projectile.LifeTicks <= 0)
                        projectile.Active = false;
                    continue;
                }

                if (_profile.HomeToTarget && TryResolveTargetPosition(out Vector3 liveTarget))
                {
                    Vector3 desired = liveTarget - projectile.Position;
                    if (desired.LengthSquared() > 0.0001f)
                    {
                        desired.Normalize();
                        projectile.Direction = Vector3.Normalize(Vector3.Lerp(projectile.Direction, desired, 0.28f));
                    }
                }

                Vector3 movement = projectile.Direction * _profile.SpeedPerTick;
                if (_profile.UseJumpTrajectory)
                {
                    movement.Z += projectile.VerticalVelocity;
                    projectile.VerticalVelocity -= 3.2f;
                }

                projectile.Position += movement;
                projectile.RollDegrees = NormalizeDegrees(
                    projectile.RollDegrees + _profile.RollPerTickDegrees);
                float yaw = MathF.Atan2(projectile.Direction.X, -projectile.Direction.Y);
                projectile.AngleRadians = DirectionToModelAngles(projectile.Direction, yaw);
                projectile.AngleRadians.Y = MathHelper.ToRadians(projectile.RollDegrees);
                projectile.LifeTicks--;

                EmitFlightTrail(projectile.PreviousPosition, projectile.Position, projectile.Direction);

                if (IsNoAttackZone(projectile.Position))
                {
                    projectile.Stopped = true;
                    projectile.Direction = Vector3.Zero;
                    projectile.VerticalVelocity = 0f;
                    projectile.LifeTicks = Math.Max(1, projectile.LifeTicks / 2);
                    continue;
                }

                bool hitTarget = CheckTargetCollision(ref projectile);
                bool hitTerrain = CheckTerrainCollision(projectile.Position, out Vector3 terrainImpact);

                if (hitTerrain)
                {
                    projectile.Position = terrainImpact;
                    projectile.Active = false;
                    EmitImpact(terrainImpact, subdued: false);
                    continue;
                }

                if (hitTarget && _volleyKind != ArrowVolleyKind.Penetration)
                {
                    projectile.Active = false;
                    EmitImpact(projectile.Position, subdued: false);
                    continue;
                }

                if (projectile.LifeTicks <= 0)
                {
                    projectile.Active = false;
                    if (_profile.Style is ProjectileStyle.Bomb or ProjectileStyle.Drill)
                        EmitImpact(projectile.Position, subdued: false);
                }
            }
        }

        private bool CheckTargetCollision(ref ProjectileState projectile)
        {
            if (projectile.HitTarget && _volleyKind == ArrowVolleyKind.Penetration)
                return false;

            if (!TryResolveTargetPosition(out Vector3 target))
                return false;

            if (!SegmentIntersectsSphere(
                    projectile.PreviousPosition,
                    projectile.Position,
                    target,
                    TargetCollisionRadius))
            {
                return false;
            }

            projectile.HitTarget = true;
            if (_volleyKind == ArrowVolleyKind.Penetration)
                EmitImpact(projectile.Position, subdued: true);
            return true;
        }

        private bool CheckTerrainCollision(Vector3 position, out Vector3 impact)
        {
            impact = position;
            if (World?.Terrain == null)
                return false;

            float height = World.Terrain.RequestTerrainHeight(position.X, position.Y);
            if (position.Z > height + 8f)
                return false;

            impact.Z = height + 10f;
            return true;
        }

        private bool IsNoAttackZone(Vector3 position)
        {
            if (World?.Terrain == null)
                return false;

            int tileX = (int)(position.X / Constants.TERRAIN_SCALE);
            int tileY = (int)(position.Y / Constants.TERRAIN_SCALE);
            if (tileX < 0 || tileY < 0 || tileX >= Constants.TERRAIN_SIZE || tileY >= Constants.TERRAIN_SIZE)
                return false;

            return World.Terrain.RequestTerrainFlag(tileX, tileY).HasFlag(TWFlags.NoAttackZone);
        }

        private void EmitFlightTrail(Vector3 previousPosition, Vector3 position, Vector3 direction)
        {
            Vector3 color = ResolveTrailColor();
            ParticleKind primaryKind = ResolvePrimaryParticleKind();
            float sizePower = 1f + (_visualPower - 1f) * 0.42f;
            float lifePower = 1f + (_visualPower - 1f) * 0.22f;

            for (int sample = 0; sample < _trailSampleCount; sample++)
            {
                float amount = _trailSampleCount == 1 ? 1f : (sample + 0.5f) / _trailSampleCount;
                Vector3 samplePosition = Vector3.Lerp(previousPosition, position, amount);
                Vector3 backward = -direction * RandomRange(5f, 18f);

                SpawnParticle(new TrailParticle
                {
                    Active = true,
                    Kind = primaryKind,
                    Position = samplePosition + RandomVector(_enhancedProjectile ? 5f : 4f),
                    Velocity = backward * 0.15f + RandomVector(_enhancedProjectile ? 20f : 18f),
                    Color = color,
                    Life = (primaryKind == ParticleKind.Smoke ? 0.26f : 0.16f) * lifePower,
                    StartSize = _profile.TrailSize * RandomRange(0.85f, 1.2f) * sizePower,
                    EndSize = _profile.TrailSize *
                              (primaryKind == ParticleKind.Smoke ? 1.8f : 0.25f) * sizePower,
                    Rotation = RandomRange(0f, MathHelper.TwoPi),
                    RotationSpeed = RandomRange(-4f, 4f)
                });

                if (_enhancedProjectile)
                {
                    ParticleKind secondaryKind = primaryKind == ParticleKind.Flare
                        ? ParticleKind.Energy
                        : ParticleKind.Flare;
                    Vector3 hotColor = Vector3.Lerp(color, Vector3.One, 0.42f);

                    SpawnParticle(new TrailParticle
                    {
                        Active = true,
                        Kind = secondaryKind,
                        Position = samplePosition - direction * RandomRange(2f, 10f),
                        Velocity = backward * 0.07f + RandomVector(8f),
                        Color = hotColor * 0.86f,
                        Life = (0.12f + (_visualPower - 1f) * 0.045f),
                        StartSize = _profile.TrailSize * (0.48f + _visualPower * 0.22f),
                        EndSize = RandomRange(2f, 5f),
                        Rotation = RandomRange(0f, MathHelper.TwoPi),
                        RotationSpeed = RandomRange(-3f, 3f)
                    });
                }

                if (_profile.Style == ProjectileStyle.Drill)
                {
                    SpawnParticle(new TrailParticle
                    {
                        Active = true,
                        Kind = ParticleKind.Smoke,
                        Position = samplePosition + RandomVector(8f),
                        Velocity = backward * 0.1f + RandomVector(12f),
                        Color = new Vector3(0.55f, 0.34f, 0.18f),
                        Life = 0.32f * lifePower,
                        StartSize = _profile.TrailSize * 0.8f * sizePower,
                        EndSize = _profile.TrailSize * 2.1f * sizePower,
                        Rotation = RandomRange(0f, MathHelper.TwoPi),
                        RotationSpeed = RandomRange(-2f, 2f)
                    });
                }
            }
        }

        private ParticleKind ResolvePrimaryParticleKind() => _profile.Style switch
        {
            ProjectileStyle.Basic => ParticleKind.Fire,
            ProjectileStyle.Drill => ParticleKind.Smoke,
            ProjectileStyle.Wing or ProjectileStyle.Bomb => ParticleKind.Flare,
            ProjectileStyle.Nature or ProjectileStyle.Lace or ProjectileStyle.Ring
                => ParticleKind.Energy,
            _ => ParticleKind.Energy
        };

        private void EmitLaunchFlash(Vector3 position, float yaw)
        {
            if (!_enhancedProjectile)
                return;

            Vector3 forward = new(MathF.Sin(yaw), -MathF.Cos(yaw), 0f);
            Vector3 flashPosition = position + forward * 8f;
            Vector3 color = ResolveTrailColor();
            float size = _profile.TrailSize * (1.45f + _visualPower * 0.42f);

            SpawnParticle(new TrailParticle
            {
                Active = true,
                Kind = ParticleKind.Energy,
                Position = flashPosition,
                Velocity = -forward * 24f,
                Color = color * 0.68f,
                Life = 0.22f + (_visualPower - 1f) * 0.05f,
                StartSize = size * 1.45f,
                EndSize = size * 0.25f,
                Rotation = RandomRange(0f, MathHelper.TwoPi),
                RotationSpeed = 3f
            });

            SpawnParticle(new TrailParticle
            {
                Active = true,
                Kind = ParticleKind.Flare,
                Position = flashPosition,
                Velocity = Vector3.Zero,
                Color = Vector3.Lerp(color, Vector3.One, 0.58f),
                Life = 0.15f,
                StartSize = size,
                EndSize = 4f,
                Rotation = RandomRange(0f, MathHelper.TwoPi),
                RotationSpeed = -4f
            });
        }

        private void EmitImpact(Vector3 position, bool subdued)
        {
            bool enhancedImpact = _enhancedProjectile || subdued;
            float impactPower = subdued ? MathF.Min(_visualPower, 1.35f) : _visualPower;
            int baseCount = _profile.Style is ProjectileStyle.Bomb or ProjectileStyle.Drill ? 14 : 8;
            int count = subdued
                ? 5
                : enhancedImpact
                    ? Math.Min(28, (int)MathF.Round(baseCount * (0.75f + impactPower * 0.48f)))
                    : baseCount;
            Vector3 baseColor = ResolveLightColor();
            float impactSize = _profile.TrailSize * (1.25f + impactPower * 0.55f);

            if (enhancedImpact)
            {
                // A stationary two-layer flash makes skill and late-game hits readable even
                // when the moving BMD itself is very small.
                SpawnParticle(new TrailParticle
                {
                    Active = true,
                    Kind = ParticleKind.Energy,
                    Position = position,
                    Velocity = Vector3.Zero,
                    Color = baseColor * (subdued ? 0.48f : 0.78f),
                    Life = subdued ? 0.16f : 0.28f,
                    StartSize = impactSize * (subdued ? 1.05f : 1.65f),
                    EndSize = impactSize * 0.2f,
                    Rotation = RandomRange(0f, MathHelper.TwoPi),
                    RotationSpeed = 2.5f
                });

                SpawnParticle(new TrailParticle
                {
                    Active = true,
                    Kind = ParticleKind.Flare,
                    Position = position + Vector3.UnitZ * 4f,
                    Velocity = Vector3.Zero,
                    Color = Vector3.Lerp(baseColor, Vector3.One, 0.62f),
                    Life = subdued ? 0.12f : 0.2f,
                    StartSize = impactSize * (subdued ? 0.7f : 1.05f),
                    EndSize = 3f,
                    Rotation = RandomRange(0f, MathHelper.TwoPi),
                    RotationSpeed = -3.5f
                });
            }

            for (int i = 0; i < count; i++)
            {
                float maximumVelocity = subdued ? 145f : enhancedImpact ? 285f : 260f;
                Vector3 velocity = RandomUnitVector() * RandomRange(60f, maximumVelocity);
                velocity.Z = MathF.Abs(velocity.Z) + RandomRange(20f, enhancedImpact ? 120f : 110f);
                SpawnParticle(new TrailParticle
                {
                    Active = true,
                    Kind = i % 3 == 0 ? ParticleKind.Flare : ParticleKind.Energy,
                    Position = position + RandomVector(enhancedImpact ? 9f : 8f),
                    Velocity = velocity,
                    Color = baseColor * RandomRange(enhancedImpact ? 0.72f : 0.7f, enhancedImpact ? 1.18f : 1.15f),
                    Life = RandomRange(0.15f, subdued ? 0.25f : enhancedImpact ? 0.42f : 0.38f),
                    StartSize = _profile.TrailSize * RandomRange(enhancedImpact ? 0.85f : 0.8f, enhancedImpact ? 1.55f : 1.5f) *
                                (enhancedImpact ? 0.9f + impactPower * 0.28f : 1f),
                    EndSize = RandomRange(3f, enhancedImpact ? 10f : 9f),
                    Rotation = RandomRange(0f, MathHelper.TwoPi),
                    RotationSpeed = RandomRange(-6f, 6f)
                });
            }
        }

        private void SpawnParticle(in TrailParticle particle)
        {
            for (int n = 0; n < _particles.Length; n++)
            {
                int index = (_particleWriteCursor + n) % _particles.Length;
                if (_particles[index].Active)
                    continue;

                _particles[index] = particle;
                _particleWriteCursor = (index + 1) % _particles.Length;
                _particleCount++;
                return;
            }

            int overwrite = _particleWriteCursor;
            _particles[overwrite] = particle;
            _particleWriteCursor = (overwrite + 1) % _particles.Length;
        }

        private void UpdateParticles(float elapsedSeconds)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ref TrailParticle particle = ref _particles[i];
                if (!particle.Active)
                    continue;

                particle.Age += elapsedSeconds;
                if (particle.Age >= particle.Life)
                {
                    particle.Active = false;
                    _particleCount--;
                    continue;
                }

                particle.Position += particle.Velocity * elapsedSeconds;
                particle.Velocity *= MathF.Pow(0.15f, elapsedSeconds);
                if (particle.Kind != ParticleKind.Smoke)
                    particle.Velocity.Z -= 120f * elapsedSeconds;
                else
                    particle.Velocity.Z += 20f * elapsedSeconds;
                particle.Rotation += particle.RotationSpeed * elapsedSeconds;
            }
        }

        private void UpdateLights()
        {
            for (int i = 0; i < _projectileCount; i++)
            {
                bool active = _projectiles[i].Active;
                _lights[i].Position = Vector3.Lerp(
                    _projectiles[i].PreviousPosition,
                    _projectiles[i].Position,
                    _renderInterpolation);
                _lights[i].Intensity = active
                    ? (_projectiles[i].Stopped
                        ? _lightIntensity * MathHelper.Clamp(_projectiles[i].LifeTicks / 15f, 0f, 1f)
                        : _lightIntensity)
                    : 0f;
            }
        }

        private void UpdateEffectAnchor()
        {
            Vector3 sum = Vector3.Zero;
            int count = 0;
            for (int i = 0; i < _projectileCount; i++)
            {
                if (!_projectiles[i].Active)
                    continue;
                sum += Vector3.Lerp(
                    _projectiles[i].PreviousPosition,
                    _projectiles[i].Position,
                    _renderInterpolation);
                count++;
            }

            if (count > 0)
                Position = sum / count;
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready || _modelRenderer == null)
                return;

            for (int i = 0; i < _projectileCount; i++)
            {
                ref ProjectileState projectile = ref _projectiles[i];
                if (!projectile.Active)
                    continue;

                Vector3 position = Vector3.Lerp(
                    projectile.PreviousPosition,
                    projectile.Position,
                    _renderInterpolation);
                _modelRenderer.DrawInstance(
                    position,
                    projectile.AngleRadians,
                    _profile.Scale * _modelScaleMultiplier,
                    Vector3.One,
                    1f + (_visualPower - 1f) * 0.45f);
            }
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (!Visible || Status != GameControlStatus.Ready || _billboardEffect == null)
                return;

            GraphicsDevice device = GraphicsDevice;
            BlendState previousBlend = device.BlendState;
            DepthStencilState previousDepth = device.DepthStencilState;
            RasterizerState previousRasterizer = device.RasterizerState;
            SamplerState previousSampler = device.SamplerStates[0];

            try
            {
                device.DepthStencilState = DepthStencilState.DepthRead;
                device.RasterizerState = RasterizerState.CullNone;
                device.SamplerStates[0] = SamplerState.LinearClamp;

                _billboardEffect.World = Matrix.Identity;
                _billboardEffect.View = Camera.Instance.View;
                _billboardEffect.Projection = Camera.Instance.Projection;
                _billboardEffect.TextureEnabled = true;
                _billboardEffect.VertexColorEnabled = true;
                _billboardEffect.LightingEnabled = false;
                _billboardEffect.FogEnabled = false;
                _billboardEffect.DiffuseColor = Vector3.One;
                _billboardEffect.Alpha = 1f;

                DrawProjectileCoreLayers();

                if (_fireTexture != null)
                    DrawParticleKind(ParticleKind.Fire, _fireTexture);
                if (_energyTexture != null)
                    DrawParticleKind(ParticleKind.Energy, _energyTexture);
                if (_flareTexture != null)
                    DrawParticleKind(ParticleKind.Flare, _flareTexture);
                if (_smokeTexture != null)
                    DrawParticleKind(ParticleKind.Smoke, _smokeTexture);
            }
            finally
            {
                device.BlendState = previousBlend;
                device.DepthStencilState = previousDepth;
                device.RasterizerState = previousRasterizer;
                device.SamplerStates[0] = previousSampler;
            }
        }

        private void DrawProjectileCoreLayers()
        {
            Texture2D? coreTexture = _flareTexture ?? _energyTexture;
            if (!_enhancedProjectile || coreTexture == null)
                return;

            Vector3 color = ResolveTrailColor();
            float baseSize = _profile.TrailSize * (0.62f + _visualPower * 0.24f);

            if (_energyTexture != null)
            {
                DrawProjectileCoreLayer(
                    _energyTexture,
                    baseSize * 1.7f,
                    color * 0.38f,
                    trailingCopies: _visualPower >= 1.55f ? 2 : 1,
                    spacing: baseSize * 0.52f,
                    rotationOffset: 0.45f);
            }

            DrawProjectileCoreLayer(
                coreTexture,
                baseSize,
                Vector3.Lerp(color, Vector3.One, 0.52f) * 0.82f,
                trailingCopies: 1,
                spacing: 0f,
                rotationOffset: 0f);

            if (_visualPower >= 1.72f)
            {
                DrawProjectileCoreLayer(
                    coreTexture,
                    baseSize * 0.48f,
                    Vector3.One * 0.8f,
                    trailingCopies: 1,
                    spacing: 0f,
                    rotationOffset: -0.35f);
            }
        }

        private void DrawProjectileCoreLayer(
            Texture2D texture,
            float size,
            Vector3 color,
            int trailingCopies,
            float spacing,
            float rotationOffset)
        {
            Vector3 cameraRight = new(Camera.Instance.View.M11, Camera.Instance.View.M21, Camera.Instance.View.M31);
            Vector3 cameraUp = new(Camera.Instance.View.M12, Camera.Instance.View.M22, Camera.Instance.View.M32);
            int quadCount = 0;

            for (int i = 0; i < _projectileCount && quadCount < MaxBillboardQuads; i++)
            {
                ref ProjectileState projectile = ref _projectiles[i];
                if (!projectile.Active)
                    continue;

                Vector3 head = Vector3.Lerp(
                    projectile.PreviousPosition,
                    projectile.Position,
                    _renderInterpolation);
                float pulse = 0.92f + 0.08f * MathF.Sin((DefaultLifeTicks - projectile.LifeTicks) * 0.82f + i);
                float rotation = MathHelper.ToRadians(projectile.RollDegrees) + rotationOffset;
                float cos = MathF.Cos(rotation);
                float sin = MathF.Sin(rotation);
                Vector3 right = cameraRight * cos + cameraUp * sin;
                Vector3 up = cameraUp * cos - cameraRight * sin;

                for (int copy = 0; copy < trailingCopies && quadCount < MaxBillboardQuads; copy++)
                {
                    float copyFade = 1f - copy * 0.34f;
                    float copyScale = 1f - copy * 0.18f;
                    Vector3 position = head - projectile.Direction * (spacing * copy);
                    WriteBillboardQuad(
                        quadCount++,
                        position,
                        right * (size * pulse * copyScale * 0.5f),
                        up * (size * pulse * copyScale * 0.5f),
                        ToAdditiveColor(color * copyFade));
                }
            }

            if (quadCount == 0)
                return;

            GraphicsDevice.BlendState = Blendings.OneOneAdditive;
            _billboardEffect!.Texture = texture;
            foreach (EffectPass pass in _billboardEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _vertices,
                    0,
                    quadCount * 4,
                    _indices,
                    0,
                    quadCount * 2);
            }
        }

        private void DrawParticleKind(ParticleKind kind, Texture2D texture)
        {
            Vector3 cameraRight = new(Camera.Instance.View.M11, Camera.Instance.View.M21, Camera.Instance.View.M31);
            Vector3 cameraUp = new(Camera.Instance.View.M12, Camera.Instance.View.M22, Camera.Instance.View.M32);
            int quadCount = 0;

            for (int i = 0; i < _particles.Length && quadCount < MaxBillboardQuads; i++)
            {
                ref TrailParticle particle = ref _particles[i];
                if (!particle.Active || particle.Kind != kind)
                    continue;

                float t = MathHelper.Clamp(particle.Age / MathF.Max(0.001f, particle.Life), 0f, 1f);
                float fade = SmoothStep(0f, 0.12f, t) * (1f - SmoothStep(0.55f, 1f, t));
                float size = MathHelper.Lerp(particle.StartSize, particle.EndSize, t);
                float cos = MathF.Cos(particle.Rotation);
                float sin = MathF.Sin(particle.Rotation);
                Vector3 right = cameraRight * cos + cameraUp * sin;
                Vector3 up = cameraUp * cos - cameraRight * sin;
                Color color = ToAdditiveColor(particle.Color * fade);

                WriteBillboardQuad(
                    quadCount++,
                    particle.Position,
                    right * (size * 0.5f),
                    up * (size * 0.5f),
                    color);
            }

            if (quadCount == 0)
                return;

            GraphicsDevice.BlendState = Blendings.OneOneAdditive;
            _billboardEffect!.Texture = texture;
            foreach (EffectPass pass in _billboardEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _vertices,
                    0,
                    quadCount * 4,
                    _indices,
                    0,
                    quadCount * 2);
            }
        }

        private void WriteBillboardQuad(int quad, Vector3 position, Vector3 right, Vector3 up, Color color)
        {
            int v = quad * 4;
            _vertices[v] = new VertexPositionColorTexture(position - right - up, color, new Vector2(0f, 1f));
            _vertices[v + 1] = new VertexPositionColorTexture(position + right - up, color, new Vector2(1f, 1f));
            _vertices[v + 2] = new VertexPositionColorTexture(position + right + up, color, new Vector2(1f, 0f));
            _vertices[v + 3] = new VertexPositionColorTexture(position - right + up, color, new Vector2(0f, 0f));
        }

        private void BuildStaticIndices()
        {
            for (int quad = 0; quad < MaxBillboardQuads; quad++)
            {
                int vertex = quad * 4;
                int index = quad * 6;
                _indices[index] = (short)vertex;
                _indices[index + 1] = (short)(vertex + 1);
                _indices[index + 2] = (short)(vertex + 2);
                _indices[index + 3] = (short)vertex;
                _indices[index + 4] = (short)(vertex + 2);
                _indices[index + 5] = (short)(vertex + 3);
            }
        }

        private Vector3 ResolveStartPosition()
        {
            Vector3 localOffset = new(-10f, -60f, 135f);
            Matrix rotation = Matrix.CreateRotationZ(_shooter.Angle.Z);
            Vector3 start = _shooter.WorldPosition.Translation + Vector3.TransformNormal(localOffset, rotation);
            if (IsFenrirArrowAction(_shooter.CurrentAction))
                start.Z += 30f;
            return start;
        }

        private Vector3 ResolveTargetPosition(Vector3 start)
        {
            if (TryResolveTargetPosition(out Vector3 target))
                return target;

            Vector3 forward = new(MathF.Sin(_shooter.Angle.Z), -MathF.Cos(_shooter.Angle.Z), 0f);
            return start + forward * 2100f;
        }

        private bool TryResolveTargetPosition(out Vector3 target)
        {
            if (_targetId != 0 &&
                _walkableWorld.TryGetWalkerById(_targetId, out WalkerObject walker) &&
                walker.Status != GameControlStatus.Disposed)
            {
                BoundingBox bounds = walker.BoundingBoxWorld;
                float height = MathF.Max(60f, bounds.Max.Z - bounds.Min.Z);
                target = new Vector3(
                    (bounds.Min.X + bounds.Max.X) * 0.5f,
                    (bounds.Min.Y + bounds.Max.Y) * 0.5f,
                    bounds.Min.Z + height * TargetImpactHeightRatio);
                return true;
            }

            if (_fallbackTargetPosition.HasValue)
            {
                target = _fallbackTargetPosition.Value + Vector3.UnitZ * 80f;
                return true;
            }

            target = default;
            return false;
        }

        private float ResolveBaseYaw(Vector3 start, Vector3 target)
        {
            // A normal attack inherits the shooter's yaw exactly. Skill animation packets do
            // not always carry a direction, so for skill volleys reconstruct the launch yaw
            // from the authoritative target point while still keeping the flight non-homing.
            if (_volleyKind == ArrowVolleyKind.Normal)
                return _shooter.Angle.Z;

            Vector3 delta = target - start;
            if (delta.X * delta.X + delta.Y * delta.Y < 0.001f)
                return _shooter.Angle.Z;
            return MathF.Atan2(delta.X, -delta.Y);
        }

        private static Vector3 ResolveDirection(Vector3 start, Vector3 target, float yaw)
        {
            Vector3 delta = target - start;
            float horizontal = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
            float pitch = horizontal > 0.001f ? MathF.Atan2(delta.Z, horizontal) : 0f;
            float cosPitch = MathF.Cos(pitch);
            return Vector3.Normalize(new Vector3(
                MathF.Sin(yaw) * cosPitch,
                -MathF.Cos(yaw) * cosPitch,
                MathF.Sin(pitch)));
        }

        private static Vector3 DirectionToModelAngles(Vector3 direction, float yaw)
        {
            float horizontal = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            float pitch = horizontal > 0.001f ? -MathF.Atan2(direction.Z, horizontal) : 0f;
            return new Vector3(pitch, 0f, yaw);
        }

        private static bool IsFenrirArrowAction(PlayerAction action) => action is
            PlayerAction.PlayerFenrirAttackBow or
            PlayerAction.PlayerFenrirAttackCrossbow;

        private bool HasActiveProjectiles()
        {
            for (int i = 0; i < _projectileCount; i++)
            {
                if (_projectiles[i].Active)
                    return true;
            }
            return false;
        }

        private static bool SegmentIntersectsSphere(Vector3 start, Vector3 end, Vector3 center, float radius)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.LengthSquared();
            if (lengthSquared <= 0.0001f)
                return Vector3.DistanceSquared(start, center) <= radius * radius;

            float t = MathHelper.Clamp(Vector3.Dot(center - start, segment) / lengthSquared, 0f, 1f);
            Vector3 closest = start + segment * t;
            return Vector3.DistanceSquared(closest, center) <= radius * radius;
        }

        private static ProjectileProfile SelectProfile(PlayerObject shooter, ArrowVolleyKind volleyKind)
        {
            if (volleyKind == ArrowVolleyKind.DeepImpact)
                return CreateProfile(ProjectileStyle.Impact);

            ProjectileStyle style = ResolveEquippedProjectileStyle(shooter);
            ProjectileProfile profile = CreateProfile(style);

            if (volleyKind == ArrowVolleyKind.IceArrow)
            {
                return new ProjectileProfile(
                    profile.Style,
                    profile.ModelCandidates,
                    profile.Scale,
                    profile.SpeedPerTick,
                    profile.RollPerTickDegrees,
                    profile.BlendMesh,
                    new Vector3(0.45f, 0.75f, 1f),
                    new Vector3(0.3f, 0.55f, 1f),
                    MathF.Max(profile.LightRadius, 180f),
                    MathF.Max(profile.TrailSize, 22f),
                    profile.UseJumpTrajectory,
                    profile.HomeToTarget);
            }

            return profile;
        }

        private Vector3 ResolveTrailColor()
        {
            return _volleyKind switch
            {
                ArrowVolleyKind.IceArrow => new Vector3(0.45f, 0.78f, 1f),
                ArrowVolleyKind.Penetration => Vector3.Lerp(_profile.TrailColor, Vector3.One, 0.28f),
                ArrowVolleyKind.DeepImpact => new Vector3(1f, 0.56f, 0.16f),
                ArrowVolleyKind.TripleShot or ArrowVolleyKind.MultiShot
                    => Vector3.Lerp(_profile.TrailColor, Vector3.One, 0.1f),
                _ => _profile.TrailColor
            };
        }

        private Vector3 ResolveLightColor()
        {
            return _volleyKind switch
            {
                ArrowVolleyKind.IceArrow => new Vector3(0.3f, 0.6f, 1f),
                ArrowVolleyKind.Penetration => Vector3.Lerp(_profile.LightColor, Vector3.One, 0.22f),
                ArrowVolleyKind.DeepImpact => new Vector3(1f, 0.42f, 0.1f),
                _ => _profile.LightColor
            };
        }

        private static float ResolveVisualPower(ProjectileStyle style, ArrowVolleyKind volleyKind)
        {
            float stylePower = style switch
            {
                ProjectileStyle.Basic or ProjectileStyle.Steel => 1f,
                ProjectileStyle.Saw or ProjectileStyle.V => 1.12f,
                ProjectileStyle.Laser or ProjectileStyle.Thunder => 1.28f,
                ProjectileStyle.Nature or ProjectileStyle.Holy or ProjectileStyle.Lace or
                ProjectileStyle.Double or ProjectileStyle.Spark => 1.45f,
                ProjectileStyle.Wing or ProjectileStyle.Bomb or ProjectileStyle.BestCrossbow or
                ProjectileStyle.Drill or ProjectileStyle.Ring or ProjectileStyle.DarkStinger or
                ProjectileStyle.Gamble => 1.62f,
                ProjectileStyle.Impact => 1.82f,
                _ => 1f
            };

            float skillBoost = volleyKind switch
            {
                ArrowVolleyKind.Normal => 0f,
                ArrowVolleyKind.TripleShot => 0.28f,
                ArrowVolleyKind.MultiShot => 0.36f,
                ArrowVolleyKind.IceArrow => 0.46f,
                ArrowVolleyKind.Penetration => 0.42f,
                ArrowVolleyKind.DeepImpact => 0.18f,
                _ => 0f
            };

            return MathF.Min(2.05f, stylePower + skillBoost);
        }

        private static int ResolveTrailSampleCount(ProjectileStyle style, ArrowVolleyKind volleyKind)
        {
            bool powerfulWeapon = style is
                ProjectileStyle.Nature or ProjectileStyle.Holy or ProjectileStyle.Lace or
                ProjectileStyle.Wing or ProjectileStyle.Bomb or ProjectileStyle.Double or
                ProjectileStyle.BestCrossbow or ProjectileStyle.Drill or ProjectileStyle.Spark or
                ProjectileStyle.Ring or ProjectileStyle.DarkStinger or ProjectileStyle.Gamble or
                ProjectileStyle.Impact;

            int samples = powerfulWeapon ? 2 : 1;
            if (volleyKind != ArrowVolleyKind.Normal)
                samples++;

            // Four simultaneous late-game arrows already create eight dense trail layers per tick.
            if (volleyKind == ArrowVolleyKind.MultiShot)
                samples = Math.Min(samples, 2);

            return Math.Clamp(samples, 1, 3);
        }

        private static ProjectileStyle ResolveEquippedProjectileStyle(PlayerObject shooter)
        {
            ItemDefinition? right = TryGetWeaponDefinition(shooter.Weapon2);
            ItemDefinition? left = TryGetWeaponDefinition(shooter.Weapon1);

            // Item group 4 indices are stable protocol data and therefore take precedence
            // over localized item names. Ammunition entries 7 (bolt) and 15 (arrows) are
            // deliberately excluded.
            if (TryResolveStyleByItem(right, out ProjectileStyle rightStyle, out bool rightCrossbow) && rightCrossbow)
                return rightStyle;
            if (TryResolveStyleByItem(left, out ProjectileStyle leftStyle, out bool leftCrossbow) && !leftCrossbow)
                return leftStyle;
            if (TryResolveStyleByItem(right, out rightStyle, out _))
                return rightStyle;
            if (TryResolveStyleByItem(left, out leftStyle, out _))
                return leftStyle;

            string weaponName = ResolveWeaponName(shooter);
            return ResolveStyleByName(weaponName, shooter.CurrentAction);
        }

        private static bool TryResolveStyleByItem(
            ItemDefinition? item,
            out ProjectileStyle style,
            out bool isCrossbow)
        {
            style = ProjectileStyle.Basic;
            isCrossbow = false;
            if (item == null || item.Group != 4)
                return false;

            switch (item.Id)
            {
                // Bows
                case 0: // Short Bow
                case 1: // Bow
                case 3: // Battle Bow
                case 4: // Tiger Bow
                case 5: // Silver Bow
                    style = ProjectileStyle.Basic;
                    return true;
                case 2:
                    style = ProjectileStyle.V;       // Elven Bow
                    return true;
                case 6:
                    style = ProjectileStyle.Nature;  // Chaos Nature Bow
                    return true;
                case 17:
                    style = ProjectileStyle.Holy;    // Celestial Bow
                    return true;
                case 20:
                    style = ProjectileStyle.Lace;    // Arrow Viper Bow
                    return true;
                case 21:
                    style = ProjectileStyle.Spark;   // Sylph Wind Bow
                    return true;
                case 22:
                    style = ProjectileStyle.Ring;    // Albatross Bow
                    return true;
                case 23:
                    style = ProjectileStyle.DarkStinger; // Stinger Bow
                    return true;
                case 24:
                    style = ProjectileStyle.Gamble;  // Air Lyn Bow
                    return true;

                // Crossbows
                case 8:  // Crossbow
                case 9:  // Golden Crossbow
                    style = ProjectileStyle.Steel;
                    isCrossbow = true;
                    return true;
                case 10:
                    style = ProjectileStyle.Saw;     // Arquebus
                    isCrossbow = true;
                    return true;
                case 11:
                    style = ProjectileStyle.Laser;   // Light Crossbow
                    isCrossbow = true;
                    return true;
                case 12:
                    style = ProjectileStyle.Thunder; // Serpent Crossbow
                    isCrossbow = true;
                    return true;
                case 13:
                    style = ProjectileStyle.Wing;    // Bluewing Crossbow
                    isCrossbow = true;
                    return true;
                case 14:
                    style = ProjectileStyle.Bomb;    // Aquagold Crossbow
                    isCrossbow = true;
                    return true;
                case 16:
                    style = ProjectileStyle.Double;  // Saint Crossbow
                    isCrossbow = true;
                    return true;
                case 18:
                    style = ProjectileStyle.BestCrossbow; // Divine Crossbow of Archangel
                    isCrossbow = true;
                    return true;
                case 19:
                    style = ProjectileStyle.Drill;   // Great Reign Crossbow
                    isCrossbow = true;
                    return true;
                default:
                    return false;
            }
        }

        private static string ResolveWeaponName(PlayerObject shooter)
        {
            ItemDefinition? right = TryGetWeaponDefinition(shooter.Weapon2);
            ItemDefinition? left = TryGetWeaponDefinition(shooter.Weapon1);

            if (right?.Name?.Contains("Crossbow", StringComparison.OrdinalIgnoreCase) == true ||
                right?.Name?.Contains("Kusza", StringComparison.OrdinalIgnoreCase) == true)
            {
                return right.Name;
            }

            if (left?.Name != null && left.Group == 4)
                return left.Name;
            if (right?.Name != null && right.Group == 4)
                return right.Name;

            return shooter.CurrentAction.ToString();
        }

        private static ItemDefinition? TryGetWeaponDefinition(WeaponObject weapon)
        {
            if (weapon.MaterialItemGroup >= 0 && weapon.MaterialItemIndex >= 0)
            {
                return ItemDatabase.GetItemDefinition(
                    (byte)weapon.MaterialItemGroup,
                    (short)weapon.MaterialItemIndex);
            }
            return null;
        }

        private static ProjectileStyle ResolveStyleByName(string weaponName, PlayerAction action)
        {
            string name = weaponName ?? string.Empty;

            if (Contains(name, "Great Reign")) return ProjectileStyle.Drill;
            if (Contains(name, "Divine Crossbow")) return ProjectileStyle.BestCrossbow;
            if (Contains(name, "Saint Crossbow")) return ProjectileStyle.Double;
            if (Contains(name, "Aquagold Crossbow")) return ProjectileStyle.Bomb;
            if (Contains(name, "Bluewing Crossbow")) return ProjectileStyle.Wing;
            if (Contains(name, "Serpent Crossbow")) return ProjectileStyle.Thunder;
            if (Contains(name, "Light Crossbow")) return ProjectileStyle.Laser;
            if (Contains(name, "Arquebus")) return ProjectileStyle.Saw;
            if (Contains(name, "Golden Crossbow") || Contains(name, "Crossbow") || Contains(name, "Kusza"))
                return ProjectileStyle.Steel;

            if (Contains(name, "Air Lyn")) return ProjectileStyle.Gamble;
            if (Contains(name, "Stinger")) return ProjectileStyle.DarkStinger;
            if (Contains(name, "Albatross")) return ProjectileStyle.Ring;
            if (Contains(name, "Sylph Wind")) return ProjectileStyle.Spark;
            if (Contains(name, "Arrow Viper")) return ProjectileStyle.Lace;
            if (Contains(name, "Celestial")) return ProjectileStyle.Holy;
            if (Contains(name, "Chaos Nature")) return ProjectileStyle.Nature;
            if (Contains(name, "Elven Bow")) return ProjectileStyle.V;

            return IsCrossbowAction(action) ? ProjectileStyle.Steel : ProjectileStyle.Basic;
        }

        private static ProjectileProfile CreateProfile(ProjectileStyle style)
        {
            return style switch
            {
                ProjectileStyle.Basic => new ProjectileProfile(
                    style,
                    new[] { "Skill/Arrow01.bmd", "Skill/arrow01.bmd" },
                    0.8f, 70f, 0f, 1,
                    new Vector3(1f, 0.48f, 0.16f),
                    new Vector3(0.8f, 0.5f, 0.2f),
                    170f, 20f),
                ProjectileStyle.Steel => new ProjectileProfile(
                    style,
                    new[] { "Skill/ArrowSteel01.bmd", "Skill/arrowsteel01.bmd" },
                    0.8f, 70f, 0f, -1,
                    new Vector3(0.9f, 0.8f, 0.55f),
                    new Vector3(0.75f, 0.55f, 0.25f),
                    160f, 18f),
                ProjectileStyle.Saw => new ProjectileProfile(
                    style,
                    new[] { "Skill/ArrowSaw01.bmd", "Skill/arrowsaw01.bmd" },
                    0.9f, 70f, 30f, -1,
                    new Vector3(1f, 0.56f, 0.2f),
                    new Vector3(0.9f, 0.5f, 0.2f),
                    180f, 20f),
                ProjectileStyle.Laser => new ProjectileProfile(
                    style,
                    new[] { "Skill/ArrowLaser01.bmd", "Skill/arrowlaser01.bmd" },
                    0.9f, 70f, 30f, -1,
                    new Vector3(0.7f, 0.85f, 1f),
                    new Vector3(0.35f, 0.65f, 1f),
                    190f, 22f),
                ProjectileStyle.Thunder => new ProjectileProfile(
                    style,
                    new[] { "Skill/ArrowThunder01.bmd", "Skill/arrowthunder01.bmd" },
                    0.9f, 70f, 35f, -1,
                    new Vector3(0.52f, 0.7f, 1f),
                    new Vector3(0.3f, 0.48f, 1f),
                    200f, 24f),
                ProjectileStyle.V => new ProjectileProfile(
                    style,
                    new[] { "Skill/ArrowV01.bmd", "Skill/arrowv01.bmd" },
                    0.9f, 70f, 25f, -1,
                    new Vector3(0.8f, 1f, 0.55f),
                    new Vector3(0.55f, 0.85f, 0.35f),
                    180f, 20f),
                ProjectileStyle.Nature => new ProjectileProfile(
                    style,
                    new[] { "Skill/ArrowNature01.bmd", "Skill/ArrowNature.bmd", "Skill/arrownature01.bmd" },
                    1f, 70f, 30f, -1,
                    new Vector3(0.3f, 1f, 0.38f),
                    new Vector3(0.25f, 0.85f, 0.3f),
                    220f, 27f),
                ProjectileStyle.Holy => new ProjectileProfile(
                    style,
                    new[] { "Skill/CW_Bow_Skill.bmd", "Skill/ArrowHoly01.bmd", "Skill/ArrowHoly.bmd" },
                    1f, 70f, 30f, -1,
                    new Vector3(1f, 0.9f, 0.45f),
                    new Vector3(1f, 0.72f, 0.25f),
                    220f, 27f),
                ProjectileStyle.Lace => new ProjectileProfile(
                    style,
                    new[] { "Skill/LaceArrow.bmd", "Skill/LaceArrow01.bmd", "Skill/ArrowLaser01.bmd" },
                    1f, 70f, 60f, -1,
                    new Vector3(0.85f, 0.35f, 1f),
                    new Vector3(0.65f, 0.25f, 0.95f),
                    230f, 28f),
                ProjectileStyle.Wing => new ProjectileProfile(
                    style,
                    new[] { "Skill/ArrowWing01.bmd", "Skill/arrowwing01.bmd" },
                    1.8f, 50f, 20f, -1,
                    new Vector3(0.45f, 0.7f, 1f),
                    new Vector3(0.35f, 0.55f, 1f),
                    250f, 30f),
                ProjectileStyle.Bomb => new ProjectileProfile(
                    style,
                    new[] { "Skill/ArrowBomb01.bmd", "Skill/arrowbomb01.bmd" },
                    1f, 58f, 30f, -1,
                    new Vector3(1f, 0.42f, 0.12f),
                    new Vector3(1f, 0.35f, 0.1f),
                    260f, 29f,
                    useJumpTrajectory: true),
                ProjectileStyle.Double => new ProjectileProfile(
                    style,
                    new[] { "Skill/ArrowDouble01.bmd", "Skill/arrowdouble01.bmd" },
                    1f, 70f, 30f, -1,
                    new Vector3(0.3f, 0.55f, 1f),
                    new Vector3(0.25f, 0.45f, 1f),
                    220f, 26f),
                ProjectileStyle.BestCrossbow => new ProjectileProfile(
                    style,
                    new[] { "Skill/KCross.bmd", "Skill/kcross.bmd", "Skill/ArrowBestCrossbow.bmd" },
                    1f, 70f, 50f, -2,
                    new Vector3(1f, 0.55f, 0.18f),
                    new Vector3(1f, 0.45f, 0.15f),
                    250f, 30f),
                ProjectileStyle.Drill => new ProjectileProfile(
                    style,
                    new[] { "Skill/Carow.bmd", "Skill/carow.bmd", "Skill/ArrowDrill.bmd" },
                    1f, 70f, 30f, -1,
                    new Vector3(1f, 0.45f, 0.15f),
                    new Vector3(0.95f, 0.35f, 0.1f),
                    250f, 28f),
                ProjectileStyle.Spark => new ProjectileProfile(
                    style,
                    new[] { "Skill/Arrow_Spark.bmd", "Skill/ArrowSpark.bmd", "Skill/arrow_spark.bmd" },
                    1f, 70f, 35f, -1,
                    new Vector3(0.55f, 0.82f, 1f),
                    new Vector3(0.35f, 0.65f, 1f),
                    220f, 26f),
                ProjectileStyle.Ring => new ProjectileProfile(
                    style,
                    new[] { "Skill/sketbows_arrows.bmd", "Skill/ArrowRing.bmd", "Skill/ArrowRing01.bmd" },
                    1f, 70f, 38f, -1,
                    new Vector3(0.75f, 0.35f, 1f),
                    new Vector3(0.55f, 0.25f, 0.95f),
                    230f, 29f),
                ProjectileStyle.DarkStinger => new ProjectileProfile(
                    style,
                    new[] { "Skill/sketbows_arrows.bmd", "Skill/ArrowDarkStinger.bmd", "Skill/ArrowDarkStinger01.bmd" },
                    1f, 70f, 32f, -1,
                    new Vector3(0.55f, 0.25f, 0.9f),
                    new Vector3(0.4f, 0.18f, 0.75f),
                    235f, 28f),
                ProjectileStyle.Gamble => new ProjectileProfile(
                    style,
                    new[] { "Skill/gamble_arrows01.bmd", "Skill/Gamble_Arrows01.bmd", "Skill/ArrowGamble.bmd" },
                    1f, 70f, 35f, -1,
                    new Vector3(0.35f, 0.9f, 1f),
                    new Vector3(0.2f, 0.7f, 1f),
                    235f, 28f),
                ProjectileStyle.Impact => new ProjectileProfile(
                    style,
                    new[] { "Skill/ArrowImpact.bmd", "Skill/arrowimpact.bmd" },
                    1f, 72f, 35f, -1,
                    new Vector3(1f, 0.5f, 0.15f),
                    new Vector3(1f, 0.4f, 0.12f),
                    260f, 32f,
                    homeToTarget: true),
                _ => throw new ArgumentOutOfRangeException(nameof(style), style, null)
            };
        }

        private static bool UsesFourProjectileMultiShot(ProjectileStyle style) => style is
            ProjectileStyle.BestCrossbow or
            ProjectileStyle.Drill or
            ProjectileStyle.Ring or
            ProjectileStyle.DarkStinger or
            ProjectileStyle.Gamble;

        public static bool UsesCrossbow(PlayerObject shooter)
        {
            ItemDefinition? right = TryGetWeaponDefinition(shooter.Weapon2);
            ItemDefinition? left = TryGetWeaponDefinition(shooter.Weapon1);
            if (TryResolveStyleByItem(right, out _, out bool rightCrossbow))
                return rightCrossbow;
            if (TryResolveStyleByItem(left, out _, out bool leftCrossbow))
                return leftCrossbow;

            string weaponName = ResolveWeaponName(shooter);
            return weaponName.Contains("Crossbow", StringComparison.OrdinalIgnoreCase) ||
                   weaponName.Contains("Kusza", StringComparison.OrdinalIgnoreCase) ||
                   IsCrossbowAction(shooter.CurrentAction);
        }

        public static bool IsBowAttackAction(PlayerAction action) => action is
            PlayerAction.PlayerAttackBow or
            PlayerAction.PlayerAttackCrossbow or
            PlayerAction.PlayerAttackFlyBow or
            PlayerAction.PlayerAttackFlyCrossbow or
            PlayerAction.PlayerAttackRideBow or
            PlayerAction.PlayerAttackRideCrossbow or
            PlayerAction.PlayerFenrirAttackBow or
            PlayerAction.PlayerFenrirAttackCrossbow or
            PlayerAction.PlayerAttackBowUp or
            PlayerAction.PlayerAttackCrossbowUp or
            PlayerAction.PlayerAttackFlyBowUp or
            PlayerAction.PlayerAttackFlyCrossbowUp or
            PlayerAction.PlayerAttackRideBowUp or
            PlayerAction.PlayerAttackRideCrossbowUp;

        private static bool IsCrossbowAction(PlayerAction action) => action is
            PlayerAction.PlayerAttackCrossbow or
            PlayerAction.PlayerAttackFlyCrossbow or
            PlayerAction.PlayerAttackRideCrossbow or
            PlayerAction.PlayerFenrirAttackCrossbow or
            PlayerAction.PlayerAttackCrossbowUp or
            PlayerAction.PlayerAttackFlyCrossbowUp or
            PlayerAction.PlayerAttackRideCrossbowUp or
            PlayerAction.PlayerSkillMultishotCrossbowStand or
            PlayerAction.PlayerSkillMultishotCrossbowFlying;

        private static bool Contains(string value, string expected) =>
            value.Contains(expected, StringComparison.OrdinalIgnoreCase);

        private static async Task<string> ResolveModelPath(string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (await BMDLoader.Instance.AssestExist(candidates[i]))
                    return candidates[i];
            }

            // Data packs from different seasons do not always contain every late-game arrow.
            // Keep the projectile functional by falling back to a standard skill projectile,
            // never to the inventory ammunition models Item/Arrows01 or Item/Arrows02.
            string[] commonFallbacks =
            {
                "Skill/Arrow01.bmd",
                "Skill/arrow01.bmd",
                "Skill/ArrowSteel01.bmd",
                "Skill/arrowsteel01.bmd"
            };

            for (int i = 0; i < commonFallbacks.Length; i++)
            {
                if (await BMDLoader.Instance.AssestExist(commonFallbacks[i]))
                    return commonFallbacks[i];
            }

            return candidates[0];
        }

        private static async Task<Texture2D?> PrepareFirstTexture(params string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    Texture2D? texture = await TextureLoader.Instance.PrepareAndGetTexture(candidates[i]);
                    if (texture != null)
                        return texture;
                }
                catch
                {
                    // Optional layer: skip it instead of drawing a stretched fallback pixel.
                }
            }
            return null;
        }

        private static Color ToAdditiveColor(Vector3 color) => new(
            MathHelper.Clamp(color.X, 0f, 1f),
            MathHelper.Clamp(color.Y, 0f, 1f),
            MathHelper.Clamp(color.Z, 0f, 1f),
            1f);

        private static float SmoothStep(float minimum, float maximum, float value)
        {
            float t = MathHelper.Clamp((value - minimum) / MathF.Max(0.0001f, maximum - minimum), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        private static float NormalizeDegrees(float value)
        {
            value %= 360f;
            return value < 0f ? value + 360f : value;
        }

        private static float RandomRange(float minimum, float maximum) =>
            minimum + (float)MuGame.Random.NextDouble() * (maximum - minimum);

        private static Vector3 RandomVector(float extent) => new(
            RandomRange(-extent, extent),
            RandomRange(-extent, extent),
            RandomRange(-extent, extent));

        private static Vector3 RandomUnitVector()
        {
            Vector3 value = RandomVector(1f);
            if (value.LengthSquared() < 0.001f)
                return Vector3.UnitZ;
            value.Normalize();
            return value;
        }

        private void RemoveSelf()
        {
            if (_removing)
                return;
            _removing = true;
            World?.RemoveObject(this);
            Dispose();
        }

        public override void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_lightsAdded && World?.Terrain != null)
            {
                for (int i = 0; i < _projectileCount; i++)
                    World.Terrain.RemoveDynamicLight(_lights[i]);
                _lightsAdded = false;
            }

            _modelRenderer?.Dispose();
            _billboardEffect?.Dispose();
            _modelRenderer = null;
            _billboardEffect = null;
            _fireTexture = null;
            _energyTexture = null;
            _flareTexture = null;
            _smokeTexture = null;
            base.Dispose();
        }

        private sealed class ProjectileModelRenderer : ModelObject
        {
            private readonly string _modelPath;

            public ProjectileModelRenderer(string modelPath, int blendMesh)
            {
                _modelPath = modelPath;
                BlendMesh = blendMesh;
                BlendMeshLight = 1f;
                ContinuousAnimation = true;
                AnimationSpeed = 8f;
                LightEnabled = false;
                RenderShadow = false;
                IsTransparent = true;
                AffectedByTransparency = true;
                DepthState = DepthStencilState.DepthRead;
                BlendState = BlendState.AlphaBlend;
                BlendMeshState = Blendings.OneOneAdditive;
            }

            protected override bool AllowGpuSkinning => false;
            protected override bool AllowDynamicLightingShader => false;
            protected override bool ForceTwoSidedMeshes => true;

            public override async Task Load()
            {
                Model = await BMDLoader.Instance.Prepare(_modelPath);
                await base.Load();
            }

            public void DrawInstance(
                Vector3 position,
                Vector3 angleRadians,
                float scale,
                Vector3 light,
                float blendMeshLight)
            {
                if (Status != GameControlStatus.Ready || Model == null)
                    return;

                Position = position;
                Angle = angleRadians;
                Scale = scale;
                Light = light;
                BlendMeshLight = blendMeshLight;
                Alpha = 1f;

                GraphicsDevice device = GraphicsDevice;
                RasterizerState previousRasterizer = device.RasterizerState;
                try
                {
                    device.RasterizerState = RasterizerState.CullNone;
                    GraphicsManager.Instance.AlphaTestEffect3D.View = Camera.Instance.View;
                    GraphicsManager.Instance.AlphaTestEffect3D.Projection = Camera.Instance.Projection;
                    GraphicsManager.Instance.AlphaTestEffect3D.World = WorldPosition;
                    DrawModel(false);
                    DrawModel(true);
                }
                finally
                {
                    device.RasterizerState = previousRasterizer;
                }
            }
        }
    }

    /// <summary>Shared spawn helpers for normal attacks and network-driven remote attacks.</summary>
    public static class ArrowProjectileSpawner
    {
        public static bool SpawnNormal(PlayerObject shooter, ushort targetId)
        {
            if (shooter?.World is not WalkableWorldControl world)
                return false;
            if (!ArrowProjectileEffect.IsBowAttackAction(shooter.CurrentAction))
                return false;

            var effect = new ArrowProjectileEffect(
                shooter,
                world,
                targetId,
                targetPosition: null,
                ArrowVolleyKind.Normal);

            world.Objects.Add(effect);
            QueueLoad(effect);
            return true;
        }

        public static bool IsArrowSkill(ushort skillId) => skillId is 24 or 25 or 46 or 51 or 52 or 235;

        public static ArrowVolleyKind GetVolleyKind(ushort skillId) => skillId switch
        {
            24 => ArrowVolleyKind.TripleShot,
            46 => ArrowVolleyKind.DeepImpact,
            51 => ArrowVolleyKind.IceArrow,
            52 => ArrowVolleyKind.Penetration,
            235 => ArrowVolleyKind.MultiShot,
            _ => ArrowVolleyKind.Normal
        };

        private static void QueueLoad(WorldObject effect)
        {
            if (effect.Status != GameControlStatus.NonInitialized)
                return;

            MuGame.TaskScheduler?.QueueTask(
                async () => await effect.Load(),
                Client.Main.Controllers.TaskScheduler.Priority.High,
                $"ArrowProjectile.Load.{effect.GetType().Name}");
        }
    }
}
