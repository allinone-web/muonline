using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MUnique.OpenMU.Network.Packets;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Recreates the legacy EquipmentLevelSet aura independently from the armor material shader.
    /// A valid full set receives six bone-linked lights from +9 and the denser +11/+13 particle
    /// and orbit layers described by the original client.
    /// </summary>
    public sealed class FullSetAuraEffect : EffectObject
    {
        private const string LightTexturePath = "Effect/flare01.jpg";
        private const string FlareTexturePath = "Effect/flare.jpg";

        private const float LegacyStepSeconds = 1f / 25f;
        private const int MaxLegacyStepsPerFrame = 5;

        private const int MaxTorsoParticles = 96;
        private const int MaxOrbitParticles = 20;
        private const int MaxTrails = 10;
        private const int MaxTailPoints = 20;
        private const int MaxTrailRenderPoints = MaxTailPoints + 1;
        private const int MaxBillboardQuads = 6 + MaxTorsoParticles + MaxOrbitParticles + 12;

        private static readonly Vector3[] TorsoOffsets11 =
        {
            new(0f, -18f, 50f),
            new(0f,   0f, 70f),
            new(0f,  18f, 50f)
        };

        private static readonly Vector3[] TorsoOffsets13 =
        {
            new(0f, -20f, 50f),
            new(0f,   0f, 70f),
            new(0f,  20f, 50f)
        };

        private static readonly float[] TorsoScales = { 0.54f, 0.72f, 0.90f };
        private readonly TorsoParticle[] _torsoParticles = new TorsoParticle[MaxTorsoParticles];
        private readonly OrbitParticle[] _orbitParticles = new OrbitParticle[MaxOrbitParticles];
        private readonly OrbitTrail[] _trails = new OrbitTrail[MaxTrails];

        private readonly VertexPositionColorTexture[] _billboardVertices =
            new VertexPositionColorTexture[MaxBillboardQuads * 4];
        private readonly short[] _billboardIndices = new short[MaxBillboardQuads * 6];
        private readonly VertexPositionColorTexture[] _trailVertices =
            new VertexPositionColorTexture[MaxTrails * MaxTrailRenderPoints * 2];
        private readonly short[] _trailIndices =
            new short[MaxTrails * (MaxTrailRenderPoints - 1) * 6];

        private BasicEffect _effect;
        private Texture2D _lightTexture;
        private Texture2D _flareTexture;
        private bool _ownsLightTexture;
        private bool _ownsFlareTexture;
        private float _legacyAccumulator;
        private int _activeSetIndex = -1;
        private int _auraTier;
        private Vector3 _auraColor = new(0.72f, 0.78f, 0.90f);
        private Vector3 _torsoColor;
        private bool _hasTorsoParticles;
        private bool _primeAuraPending;
        private uint _spawnSequence;
        private float _renderInterpolation;
        private float _auraTime;

        public FullSetAuraEffect()
        {
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-260f, -260f, -80f),
                new Vector3(260f, 260f, 360f));
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;

            for (int i = 0; i < _trails.Length; i++)
                _trails[i] = new OrbitTrail();

            BuildStaticIndices(_billboardIndices, MaxBillboardQuads);
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            _lightTexture = await PrepareTexture(LightTexturePath);
            if (_lightTexture == null)
            {
                _lightTexture = CreateRadialTexture(GraphicsDevice, 64, 2.4f);
                _ownsLightTexture = true;
            }

            _flareTexture = await PrepareTexture(FlareTexturePath);
            if (_flareTexture == null)
            {
                _flareTexture = CreateRadialTexture(GraphicsDevice, 64, 1.45f);
                _ownsFlareTexture = true;
            }

            _effect = new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = true,
                TextureEnabled = true,
                LightingEnabled = false,
                World = Matrix.Identity
            };
        }

        public override void Update(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready)
                return;

            if (!RefreshSetState())
            {
                base.Update(gameTime);
                return;
            }

            float elapsed = MathHelper.Clamp(
                (float)gameTime.ElapsedGameTime.TotalSeconds,
                0f,
                LegacyStepSeconds * MaxLegacyStepsPerFrame);
            _auraTime += elapsed;
            _legacyAccumulator += elapsed;

            int steps = 0;
            while (_legacyAccumulator >= LegacyStepSeconds && steps < MaxLegacyStepsPerFrame)
            {
                TickLegacy();
                _legacyAccumulator -= LegacyStepSeconds;
                steps++;
            }

            if (steps == MaxLegacyStepsPerFrame)
                _legacyAccumulator = MathF.Min(_legacyAccumulator, LegacyStepSeconds);

            _renderInterpolation = MathHelper.Clamp(
                _legacyAccumulator / LegacyStepSeconds,
                0f,
                1f);

            base.Update(gameTime);
        }

        private bool RefreshSetState()
        {
            if (Parent is not PlayerObject player ||
                player.Hidden ||
                player.Model == null ||
                GraphicsQualityManager.ActivePreset == GraphicsQualityPreset.Low ||
                player.World == null ||
                IsChaosCastleMap(player.World.WorldIndex))
            {
                ResetSetState();
                return false;
            }

            int setIndex = -1;
            int minimumLevel = int.MaxValue;

            bool requireHelm = !IsMagicGladiatorClass(player.CharacterClass);
            bool requireGloves = !IsRageFighterClass(player.CharacterClass);

            if (requireHelm && !AcceptPart(player.Helm, ref setIndex, ref minimumLevel))
            {
                ResetSetState();
                return false;
            }

            if (!AcceptPart(player.Armor, ref setIndex, ref minimumLevel) ||
                !AcceptPart(player.Pants, ref setIndex, ref minimumLevel) ||
                (requireGloves && !AcceptPart(player.Gloves, ref setIndex, ref minimumLevel)) ||
                !AcceptPart(player.Boots, ref setIndex, ref minimumLevel))
            {
                ResetSetState();
                return false;
            }

            if (setIndex < 0 || minimumLevel < 9)
            {
                ResetSetState();
                return false;
            }

            int tier = minimumLevel >= 13 ? 13 : minimumLevel >= 11 ? 11 : 9;
            if (_activeSetIndex != setIndex || _auraTier != tier)
            {
                ClearTransientEffects();
                _activeSetIndex = setIndex;
                _auraTier = tier;
                _auraColor = GetSetAuraColor(setIndex);
                _hasTorsoParticles = TryGetTorsoParticleColor(setIndex, out _torsoColor);
                _primeAuraPending = tier >= 11;
            }

            Hidden = false;
            return true;
        }

        private static bool AcceptPart(ModelObject part, ref int setIndex, ref int minimumLevel)
        {
            if (part == null ||
                part.Hidden ||
                part.Model == null ||
                part.MaterialItemIndex < 0 ||
                part.ItemLevel < 9)
            {
                return false;
            }

            if (setIndex < 0)
                setIndex = part.MaterialItemIndex;
            else if (part.MaterialItemIndex != setIndex)
                return false;

            minimumLevel = Math.Min(minimumLevel, part.ItemLevel);
            return true;
        }

        private void TickLegacy()
        {
            if (Parent is not PlayerObject player)
                return;

            UpdateTorsoParticles();
            UpdateOrbitParticles(player.IsMoving);
            UpdateTrails(player);

            // Distant/throttled players retain their six skeletal lights but do not emit
            // the expensive transient layers until they return to normal quality.
            if (player.LowQuality)
                return;

            if (_primeAuraPending)
            {
                PrimeAura(player);
                _primeAuraPending = false;
            }

            if (_hasTorsoParticles && _auraTier >= 11)
            {
                bool emit = _auraTier >= 13 || MuGame.Random.Next(3) == 0;
                if (emit)
                    SpawnTorsoBurst(player, _auraTier >= 13 ? TorsoOffsets13 : TorsoOffsets11);
            }

            // Keep the original stochastic character, but make the aura easier to read in
            // the wider/high-resolution MonoGame camera.
            if (_auraTier <= 9 || MuGame.Random.Next(14) != 0)
                return;

            if (_auraTier == 11)
            {
                if (MuGame.Random.Next(6) == 0)
                    SpawnClassicTrail(player);
                else
                    SpawnOrbitParticle();
            }
            else
            {
                // +13 layer A: two counter-rotating expanding spirals.
                if (MuGame.Random.Next(5) == 0)
                {
                    SpawnExpandingTrail(player, direction: 1);
                    SpawnExpandingTrail(player, direction: -1);
                }

                // +13 layer B: keep the small orbiting flare. The large blue
                // spatial orb was intentionally removed because it dominated the aura.
                if (MuGame.Random.Next(3) == 0)
                    SpawnOrbitParticle();
            }
        }


        private void PrimeAura(PlayerObject player)
        {
            // Avoid an empty-looking aura immediately after equipment/scene changes. This is
            // a one-shot visual seed; steady-state emission still follows the legacy 25 Hz rolls.
            SpawnOrbitParticle();
            if (_hasTorsoParticles)
                SpawnTorsoBurst(player, _auraTier >= 13 ? TorsoOffsets13 : TorsoOffsets11);

            if (_auraTier >= 13)
            {
                SpawnExpandingTrail(player, direction: 1);
                SpawnExpandingTrail(player, direction: -1);
            }
        }

        private void SpawnTorsoBurst(PlayerObject player, Vector3[] offsets)
        {
            Matrix[] bones = player.GetBoneTransforms();
            if (bones == null || bones.Length == 0)
                return;

            Matrix rootBone = bones[0];
            Matrix playerWorld = player.WorldPosition;

            for (int i = 0; i < offsets.Length; i++)
            {
                int slot = FindTorsoParticleSlot();
                ref TorsoParticle particle = ref _torsoParticles[slot];

                Vector3 local = offsets[i] + new Vector3(
                    RandomRange(-10f, 10f),
                    RandomRange(-10f, 10f),
                    RandomRange(-20f, 20f));
                Vector3 modelPosition = Vector3.Transform(local, rootBone);

                particle.Position = Vector3.Transform(modelPosition, playerWorld);
                particle.VelocityPerTick = Vector3.TransformNormal(
                    new Vector3(
                        RandomRange(-0.55f, 0.55f),
                        RandomRange(-0.55f, 0.55f),
                        2f + RandomRange(-0.25f, 0.45f)),
                    playerWorld);
                particle.DriftXPerTick = RandomRange(-0.055f, 0.055f);
                particle.DriftYPerTick = RandomRange(-0.055f, 0.055f);
                particle.Color = _torsoColor;
                particle.Scale = TorsoScales[i] * RandomRange(0.9f, 1.08f);
                particle.Rotation = MathHelper.ToRadians(MuGame.Random.Next(360));
                particle.LifeTicks = 12;
                particle.MaxLifeTicks = 12;
            }
        }

        private void UpdateTorsoParticles()
        {
            for (int i = 0; i < _torsoParticles.Length; i++)
            {
                ref TorsoParticle particle = ref _torsoParticles[i];
                if (particle.LifeTicks <= 0)
                    continue;

                particle.LifeTicks--;
                if (particle.LifeTicks <= 0)
                    continue;

                particle.Position += particle.VelocityPerTick;
                particle.VelocityPerTick.X += particle.DriftXPerTick;
                particle.VelocityPerTick.Y += particle.DriftYPerTick;
                particle.Scale *= 0.965f;
                particle.Rotation += 0.045f;
            }
        }

        private void SpawnOrbitParticle()
        {
            int slot = FindOrbitParticleSlot();
            ref OrbitParticle particle = ref _orbitParticles[slot];
            particle.Phase = RandomRange(0f, MathHelper.TwoPi);
            particle.Direction = MuGame.Random.Next(2) == 0 ? -1f : 1f;
            particle.Radius = 40f;
            particle.Height = RandomRange(24f, 62f);
            particle.VerticalSpeedPerTick = RandomRange(1f, 4.96f);
            particle.Scale = RandomRange(0.19f, 0.24f);
            particle.Color = Vector3.Lerp(_auraColor, Vector3.One, 0.28f);
            particle.LifeTicks = 60;
            particle.MaxLifeTicks = 60;
        }

        private void UpdateOrbitParticles(bool ownerMoving)
        {
            for (int i = 0; i < _orbitParticles.Length; i++)
            {
                ref OrbitParticle particle = ref _orbitParticles[i];
                if (particle.LifeTicks <= 0)
                    continue;

                if (ownerMoving && particle.LifeTicks > 20)
                    particle.LifeTicks = 20;

                particle.LifeTicks--;
                if (particle.LifeTicks <= 0)
                    continue;

                particle.Phase += particle.Direction * 0.1f;
                particle.Height += particle.VerticalSpeedPerTick;
                particle.Scale = MathF.Max(0.04f, particle.Scale - 0.002f);
            }
        }

        private void SpawnClassicTrail(PlayerObject player)
        {
            OrbitTrail trail = GetTrailSlot();
            trail.Activate(
                TrailKind.Classic,
                player,
                _auraColor,
                direction: MuGame.Random.Next(2) == 0 ? -1 : 1,
                lifeTicks: 100,
                tailLimit: 20,
                phase: RandomRange(0f, MathHelper.TwoPi),
                radius: 40f,
                width: 5.5f,
                verticalSpeed: RandomRange(0.45f, 1.4f),
                seed: NextSeed());
        }

        private void SpawnExpandingTrail(PlayerObject player, int direction)
        {
            OrbitTrail trail = GetTrailSlot();
            trail.Activate(
                TrailKind.Expanding,
                player,
                Vector3.Lerp(_auraColor, Vector3.One, 0.2f),
                direction,
                lifeTicks: 50,
                tailLimit: 20,
                phase: RandomRange(0f, MathHelper.TwoPi),
                radius: 40f,
                width: 7.5f,
                verticalSpeed: RandomRange(0.8f, 1.7f),
                seed: NextSeed());
        }

        private void UpdateTrails(PlayerObject player)
        {
            for (int i = 0; i < _trails.Length; i++)
            {
                OrbitTrail trail = _trails[i];
                if (!trail.Active)
                    continue;

                trail.Tick(player);
            }
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (!Visible || _effect == null || _activeSetIndex < 0 || Parent is not PlayerObject player)
                return;

            float parentAlpha = MathHelper.Clamp(player.TotalAlpha, 0f, 1f);
            if (parentAlpha <= 0.05f)
                return;

            BlendState previousBlend = GraphicsDevice.BlendState;
            DepthStencilState previousDepth = GraphicsDevice.DepthStencilState;
            RasterizerState previousRasterizer = GraphicsDevice.RasterizerState;
            SamplerState previousSampler = GraphicsDevice.SamplerStates[0];

            try
            {
                GraphicsDevice.BlendState = Blendings.OneOneAdditive;
                GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                GraphicsDevice.RasterizerState = RasterizerState.CullNone;
                GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

                _effect.World = Matrix.Identity;
                _effect.View = Camera.Instance.View;
                _effect.Projection = Camera.Instance.Projection;

                DrawSoftLights(player, parentAlpha, _renderInterpolation);
                DrawOrbitFlares(player, parentAlpha, _renderInterpolation);
                DrawTrails(parentAlpha, player, _renderInterpolation);
            }
            finally
            {
                GraphicsDevice.BlendState = previousBlend;
                GraphicsDevice.DepthStencilState = previousDepth;
                GraphicsDevice.RasterizerState = previousRasterizer;
                GraphicsDevice.SamplerStates[0] = previousSampler;
            }
        }

        private void DrawSoftLights(PlayerObject player, float parentAlpha, float interpolation)
        {
            if (_lightTexture == null || _lightTexture.IsDisposed)
                return;

            Matrix inverseView = Matrix.Invert(Camera.Instance.View);
            Vector3 cameraRight = inverseView.Right;
            Vector3 cameraUp = inverseView.Up;
            int quadCount = 0;

            // The original suppresses these six lights while cloaked. Alpha is the reliable
            // representation available in this client for the same visual state.
            if (parentAlpha >= 0.8f)
            {
                Matrix[] bones = player.GetBoneTransforms();
                if (bones != null)
                {
                    float bonePulse = 0.88f + MathF.Sin(_auraTime * 2.15f) * 0.12f;
                    Vector3 boneLightColor = _auraColor * (0.72f * bonePulse * parentAlpha);
                    int leftLinkBone = player.Weapon1 != null && player.Weapon1.ParentBoneLink >= 0
                        ? player.Weapon1.ParentBoneLink
                        : PlayerObject.LeftHandBoneIndex;
                    int rightLinkBone = player.Weapon2 != null && player.Weapon2.ParentBoneLink >= 0
                        ? player.Weapon2.ParentBoneLink
                        : PlayerObject.RightHandBoneIndex;

                    AppendBoneLightGroup(
                        player,
                        bones,
                        leftLinkBone,
                        boneLightColor,
                        cameraRight,
                        cameraUp,
                        ref quadCount);
                    AppendBoneLightGroup(
                        player,
                        bones,
                        rightLinkBone,
                        boneLightColor,
                        cameraRight,
                        cameraUp,
                        ref quadCount);
                }
            }

            // A subtle persistent core makes the full-set state readable between random
            // orbit emissions without turning it into a detached full-body billboard aura.
            if (_auraTier >= 11 && quadCount + 1 < MaxBillboardQuads)
            {
                Vector3 center = player.WorldPosition.Translation + new Vector3(0f, 0f, 62f);
                float corePulse = 0.82f + MathF.Sin(_auraTime * 1.7f) * 0.18f;
                Vector3 coreColor = Vector3.Lerp(_auraColor, Vector3.One, 0.18f);
                float tierScale = _auraTier >= 13 ? 1.18f : 1f;
                WriteBillboardQuad(
                    quadCount++, center,
                    cameraRight * (20f * tierScale),
                    cameraUp * (27f * tierScale),
                    ToColor(coreColor * (0.18f * corePulse * parentAlpha)));
                WriteBillboardQuad(
                    quadCount++, center + new Vector3(0f, 0f, 5f),
                    cameraRight * (34f * tierScale),
                    cameraUp * (43f * tierScale),
                    ToColor(coreColor * (0.07f * corePulse * parentAlpha)));
            }

            for (int i = 0; i < _torsoParticles.Length && quadCount < MaxBillboardQuads; i++)
            {
                ref TorsoParticle particle = ref _torsoParticles[i];
                if (particle.LifeTicks <= 0)
                    continue;

                float remaining = MathF.Max(0f, particle.LifeTicks - interpolation);
                float age = particle.MaxLifeTicks - remaining;
                float fadeIn = MathHelper.SmoothStep(0f, 1f, MathHelper.Clamp(age / 1.5f, 0f, 1f));
                float fadeOut = MathHelper.SmoothStep(0f, 1f, MathHelper.Clamp(remaining / 4f, 0f, 1f));
                float shimmer = 0.90f + MathF.Sin(_auraTime * 7f + i * 1.73f) * 0.10f;
                float intensity = fadeIn * fadeOut * shimmer * 1.28f * parentAlpha;
                Vector3 predictedPosition = particle.Position +
                    particle.VelocityPerTick * interpolation +
                    new Vector3(
                        particle.DriftXPerTick * interpolation * interpolation * 0.5f,
                        particle.DriftYPerTick * interpolation * interpolation * 0.5f,
                        0f);
                float predictedScale = particle.Scale * MathF.Pow(0.965f, interpolation);
                float predictedRotation = particle.Rotation + 0.045f * interpolation;
                float halfSize = 11.25f * predictedScale;
                float cosine = MathF.Cos(predictedRotation);
                float sine = MathF.Sin(predictedRotation);
                Vector3 rotatedRight = cameraRight * cosine + cameraUp * sine;
                Vector3 rotatedUp = cameraUp * cosine - cameraRight * sine;
                WriteBillboardQuad(
                    quadCount++,
                    predictedPosition,
                    rotatedRight * halfSize,
                    rotatedUp * halfSize,
                    ToColor(particle.Color * intensity));
            }

            DrawBillboardBatch(_lightTexture, quadCount);
        }

        private void AppendBoneLightGroup(
            PlayerObject player,
            Matrix[] bones,
            int linkBone,
            Vector3 color,
            Vector3 cameraRight,
            Vector3 cameraUp,
            ref int quadCount)
        {
            for (int offset = 0; offset < 3 && quadCount < MaxBillboardQuads; offset++)
            {
                int boneIndex = offset switch
                {
                    0 => linkBone,
                    1 => linkBone - 6,
                    _ => linkBone - 7
                };

                if (boneIndex < 0 || boneIndex >= bones.Length)
                    continue;

                Vector3 worldPosition = Vector3.Transform(
                    bones[boneIndex].Translation,
                    player.WorldPosition);
                float halfSize = 13.0f;
                WriteBillboardQuad(
                    quadCount++,
                    worldPosition,
                    cameraRight * halfSize,
                    cameraUp * halfSize,
                    ToColor(color));
            }
        }

        private void DrawOrbitFlares(PlayerObject player, float parentAlpha, float interpolation)
        {
            if (_flareTexture == null || _flareTexture.IsDisposed)
                return;

            Matrix inverseView = Matrix.Invert(Camera.Instance.View);
            Vector3 cameraRight = inverseView.Right;
            Vector3 cameraUp = inverseView.Up;
            Vector3 center = player.WorldPosition.Translation;
            int quadCount = 0;

            for (int i = 0; i < _orbitParticles.Length && quadCount < MaxBillboardQuads; i++)
            {
                ref OrbitParticle particle = ref _orbitParticles[i];
                if (particle.LifeTicks <= 0)
                    continue;

                float predictedPhase = particle.Phase + particle.Direction * 0.1f * interpolation;
                float predictedHeight = particle.Height + particle.VerticalSpeedPerTick * interpolation;
                Vector3 position = center + new Vector3(
                    MathF.Sin(predictedPhase) * particle.Radius,
                    -MathF.Cos(predictedPhase) * particle.Radius,
                    predictedHeight);
                float remaining = MathF.Max(0f, particle.LifeTicks - interpolation);
                float age = particle.MaxLifeTicks - remaining;
                float fadeIn = MathHelper.SmoothStep(0f, 1f, MathHelper.Clamp(age / 2f, 0f, 1f));
                float fadeOut = MathHelper.SmoothStep(0f, 1f, MathHelper.Clamp(remaining / 8f, 0f, 1f));
                float intensity = fadeIn * fadeOut * 1.22f * parentAlpha;
                float predictedScale = MathF.Max(0.04f, particle.Scale - 0.002f * interpolation);
                float halfSize = 34f * predictedScale;
                WriteBillboardQuad(
                    quadCount++,
                    position,
                    cameraRight * halfSize,
                    cameraUp * halfSize,
                    ToColor(particle.Color * intensity));
            }

            DrawBillboardBatch(_flareTexture, quadCount);
        }

        private void DrawTrails(float parentAlpha, PlayerObject player, float interpolation)
        {
            if (_flareTexture == null || _flareTexture.IsDisposed)
                return;

            int vertexCount = 0;
            int indexCount = 0;
            Vector3 cameraPosition = Camera.Instance.Position;
            Matrix inverseView = Matrix.Invert(Camera.Instance.View);
            Vector3 cameraRight = inverseView.Right;

            for (int trailIndex = 0; trailIndex < _trails.Length; trailIndex++)
            {
                OrbitTrail trail = _trails[trailIndex];
                if (!trail.Active || trail.TailCount < 2)
                    continue;

                int renderPointCount = Math.Min(
                    MaxTrailRenderPoints,
                    trail.TailCount + (interpolation > 0.001f ? 1 : 0));
                if (renderPointCount < 2)
                    continue;

                int baseVertex = vertexCount;
                for (int pointIndex = 0; pointIndex < renderPointCount; pointIndex++)
                {
                    Vector3 current = trail.GetRenderPoint(player, pointIndex, interpolation);
                    Vector3 tangent;
                    if (pointIndex == 0)
                        tangent = trail.GetRenderPoint(player, 1, interpolation) - current;
                    else if (pointIndex == renderPointCount - 1)
                        tangent = current - trail.GetRenderPoint(player, pointIndex - 1, interpolation);
                    else
                        tangent = trail.GetRenderPoint(player, pointIndex + 1, interpolation) -
                                  trail.GetRenderPoint(player, pointIndex - 1, interpolation);

                    Vector3 view = cameraPosition - current;
                    Vector3 side = Vector3.Cross(tangent, view);
                    if (side.LengthSquared() < 0.0001f)
                        side = cameraRight;
                    else
                        side.Normalize();

                    float progress = pointIndex / (float)(renderPointCount - 1);
                    float tailFade = MathF.Pow(1f - progress, 0.72f);
                    float widthPulse = 0.94f + MathF.Sin(_auraTime * 5f + trailIndex) * 0.06f;
                    float width = trail.Width * widthPulse * MathHelper.Lerp(1.12f, 0.12f, progress);
                    Vector3 offset = side * (width * 0.5f);
                    float lifeRatio = trail.GetRenderLifeRatio(interpolation);
                    Color color = ToColor(trail.Color * (lifeRatio * tailFade * 1.32f * parentAlpha));

                    _trailVertices[vertexCount++] = new VertexPositionColorTexture(
                        current - offset,
                        color,
                        new Vector2(0f, progress));
                    _trailVertices[vertexCount++] = new VertexPositionColorTexture(
                        current + offset,
                        color,
                        new Vector2(1f, progress));
                }

                for (int segment = 0; segment < renderPointCount - 1; segment++)
                {
                    short left0 = checked((short)(baseVertex + segment * 2));
                    short right0 = checked((short)(left0 + 1));
                    short left1 = checked((short)(left0 + 2));
                    short right1 = checked((short)(left0 + 3));

                    _trailIndices[indexCount++] = left0;
                    _trailIndices[indexCount++] = right0;
                    _trailIndices[indexCount++] = left1;
                    _trailIndices[indexCount++] = right0;
                    _trailIndices[indexCount++] = right1;
                    _trailIndices[indexCount++] = left1;
                }
            }

            if (vertexCount == 0 || indexCount == 0)
                return;

            _effect.Texture = _flareTexture;
            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _trailVertices,
                    0,
                    vertexCount,
                    _trailIndices,
                    0,
                    indexCount / 3);
            }
        }

        private void DrawBillboardBatch(Texture2D texture, int quadCount)
        {
            if (quadCount <= 0)
                return;

            _effect.Texture = texture;
            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _billboardVertices,
                    0,
                    quadCount * 4,
                    _billboardIndices,
                    0,
                    quadCount * 2);
            }
        }

        private void WriteBillboardQuad(
            int quadIndex,
            Vector3 center,
            Vector3 right,
            Vector3 up,
            Color color)
        {
            int vertex = quadIndex * 4;
            _billboardVertices[vertex] = new VertexPositionColorTexture(
                center - right - up, color, new Vector2(0f, 1f));
            _billboardVertices[vertex + 1] = new VertexPositionColorTexture(
                center + right - up, color, new Vector2(1f, 1f));
            _billboardVertices[vertex + 2] = new VertexPositionColorTexture(
                center + right + up, color, new Vector2(1f, 0f));
            _billboardVertices[vertex + 3] = new VertexPositionColorTexture(
                center - right + up, color, new Vector2(0f, 0f));
        }

        private static void BuildStaticIndices(short[] indices, int quadCapacity)
        {
            for (int i = 0; i < quadCapacity; i++)
            {
                int vertex = i * 4;
                int index = i * 6;
                indices[index] = checked((short)vertex);
                indices[index + 1] = checked((short)(vertex + 1));
                indices[index + 2] = checked((short)(vertex + 2));
                indices[index + 3] = checked((short)vertex);
                indices[index + 4] = checked((short)(vertex + 2));
                indices[index + 5] = checked((short)(vertex + 3));
            }
        }

        private OrbitTrail GetTrailSlot()
        {
            OrbitTrail oldest = _trails[0];
            int smallestLife = int.MaxValue;

            for (int i = 0; i < _trails.Length; i++)
            {
                if (!_trails[i].Active)
                    return _trails[i];

                if (_trails[i].LifeTicks < smallestLife)
                {
                    smallestLife = _trails[i].LifeTicks;
                    oldest = _trails[i];
                }
            }

            return oldest;
        }

        private int FindTorsoParticleSlot()
        {
            int oldest = 0;
            int smallestLife = int.MaxValue;
            for (int i = 0; i < _torsoParticles.Length; i++)
            {
                if (_torsoParticles[i].LifeTicks <= 0)
                    return i;

                if (_torsoParticles[i].LifeTicks < smallestLife)
                {
                    smallestLife = _torsoParticles[i].LifeTicks;
                    oldest = i;
                }
            }

            return oldest;
        }

        private int FindOrbitParticleSlot()
        {
            int oldest = 0;
            int smallestLife = int.MaxValue;
            for (int i = 0; i < _orbitParticles.Length; i++)
            {
                if (_orbitParticles[i].LifeTicks <= 0)
                    return i;

                if (_orbitParticles[i].LifeTicks < smallestLife)
                {
                    smallestLife = _orbitParticles[i].LifeTicks;
                    oldest = i;
                }
            }

            return oldest;
        }

        private void ResetSetState()
        {
            if (_activeSetIndex >= 0)
                ClearTransientEffects();

            _activeSetIndex = -1;
            _auraTier = 0;
            _hasTorsoParticles = false;
            _primeAuraPending = false;
            _renderInterpolation = 0f;
            Hidden = true;
        }

        private void ClearTransientEffects()
        {
            Array.Clear(_torsoParticles, 0, _torsoParticles.Length);
            Array.Clear(_orbitParticles, 0, _orbitParticles.Length);
            for (int i = 0; i < _trails.Length; i++)
                _trails[i].Reset();
            _legacyAccumulator = 0f;
            _renderInterpolation = 0f;
        }

        private static Vector3 GetSetAuraColor(int setIndex)
        {
            if (setIndex == 4 || setIndex == 14 || setIndex == 15 || setIndex == 17 ||
                (setIndex >= 39 && setIndex <= 42))
            {
                return new Vector3(1f, 0.5f, 0f);
            }

            if (setIndex == 18 || setIndex == 43)
                return new Vector3(0f, 0.5f, 1f);

            if (setIndex == 21 || setIndex == 44)
                return Vector3.One;

            if (TryGetTorsoParticleColor(setIndex, out Vector3 specialColor))
                return Vector3.Lerp(specialColor, Vector3.One, 0.2f);

            return new Vector3(0.72f, 0.78f, 0.90f);
        }

        private static bool TryGetTorsoParticleColor(int bootsIndex, out Vector3 color)
        {
            string name = null;
            try
            {
                name = ItemDatabase.GetItemDefinition(11, (short)bootsIndex)?.Name;
            }
            catch
            {
                // The fallback indices below cover the standard Season 5.2 item table.
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                string normalized = name.ToLowerInvariant();
                if (normalized.Contains("dragon knight"))
                {
                    color = new Vector3(0.65f, 0.30f, 0.10f);
                    return true;
                }
                if (normalized.Contains("venom mist"))
                {
                    color = new Vector3(0.10f, 0.10f, 0.90f);
                    return true;
                }
                if (normalized.Contains("sylphid ray") || normalized.Contains("sylpid ray"))
                {
                    color = new Vector3(0.00f, 0.32f, 0.24f);
                    return true;
                }
                if (normalized.Contains("volcano"))
                {
                    color = new Vector3(0.50f, 0.24f, 0.80f);
                    return true;
                }
                if (normalized.Contains("sunlight"))
                {
                    color = new Vector3(0.60f, 0.40f, 0.00f);
                    return true;
                }
                if (normalized.Contains("aura"))
                {
                    color = new Vector3(0.60f, 0.30f, 0.40f);
                    return true;
                }
            }

            color = bootsIndex switch
            {
                29 => new Vector3(0.65f, 0.30f, 0.10f),
                30 => new Vector3(0.10f, 0.10f, 0.90f),
                31 => new Vector3(0.00f, 0.32f, 0.24f),
                32 => new Vector3(0.50f, 0.24f, 0.80f),
                33 => new Vector3(0.60f, 0.40f, 0.00f),
                _ => Vector3.Zero
            };
            return color != Vector3.Zero;
        }

        private static bool IsMagicGladiatorClass(CharacterClassNumber cls) =>
            cls == CharacterClassNumber.MagicGladiator ||
            cls == CharacterClassNumber.DuelMaster;

        private static bool IsRageFighterClass(CharacterClassNumber cls) =>
            cls == CharacterClassNumber.RageFighter ||
            cls == CharacterClassNumber.FistMaster;

        private static bool IsChaosCastleMap(short worldIndex) =>
            (worldIndex >= 18 && worldIndex <= 23) || worldIndex == 53 || worldIndex == 97;

        private uint NextSeed()
        {
            _spawnSequence++;
            return unchecked((uint)MuGame.Random.Next() ^ (_spawnSequence * 747796405u));
        }

        private static float RandomRange(float min, float max) =>
            min + (float)MuGame.Random.NextDouble() * (max - min);

        private static Color ToColor(Vector3 value) => new(
            MathHelper.Clamp(value.X, 0f, 1f),
            MathHelper.Clamp(value.Y, 0f, 1f),
            MathHelper.Clamp(value.Z, 0f, 1f),
            1f);

        private static async Task<Texture2D> PrepareTexture(string path)
        {
            try
            {
                return await TextureLoader.Instance.PrepareAndGetTexture(path);
            }
            catch
            {
                return null;
            }
        }

        private static Texture2D CreateRadialTexture(GraphicsDevice graphicsDevice, int size, float exponent)
        {
            var texture = new Texture2D(graphicsDevice, size, size);
            var pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / radius;
                    float dy = (y - center) / radius;
                    float distance = MathF.Sqrt(dx * dx + dy * dy);
                    float intensity = MathF.Pow(MathHelper.Clamp(1f - distance, 0f, 1f), exponent);
                    pixels[y * size + x] = new Color(intensity, intensity, intensity, intensity);
                }
            }

            texture.SetData(pixels);
            return texture;
        }

        public override void Dispose()
        {
            _effect?.Dispose();
            _effect = null;
            if (_ownsLightTexture)
                _lightTexture?.Dispose();
            if (_ownsFlareTexture)
                _flareTexture?.Dispose();
            _lightTexture = null;
            _flareTexture = null;
            base.Dispose();
        }

        private struct TorsoParticle
        {
            public Vector3 Position;
            public Vector3 VelocityPerTick;
            public Vector3 Color;
            public float DriftXPerTick;
            public float DriftYPerTick;
            public float Scale;
            public float Rotation;
            public int LifeTicks;
            public int MaxLifeTicks;
        }

        private struct OrbitParticle
        {
            public Vector3 Color;
            public float Phase;
            public float Direction;
            public float Radius;
            public float Height;
            public float VerticalSpeedPerTick;
            public float Scale;
            public int LifeTicks;
            public int MaxLifeTicks;
        }

        private enum TrailKind : byte
        {
            Classic,
            Expanding
        }

        private sealed class OrbitTrail
        {
            private readonly Vector3[] _tails = new Vector3[MaxTailPoints];
            private int _tailStart;
            private int _tailLimit;
            private float _phase;
            private float _radius;
            private float _height;
            private float _verticalSpeed;
            private int _direction;
            private uint _seed;

            public bool Active { get; private set; }
            public TrailKind Kind { get; private set; }
            public Vector3 Color { get; private set; }
            public float Width { get; private set; }
            public int LifeTicks { get; private set; }
            public int MaxLifeTicks { get; private set; }
            public int TailCount { get; private set; }
            public void Activate(
                TrailKind kind,
                PlayerObject player,
                Vector3 color,
                int direction,
                int lifeTicks,
                int tailLimit,
                float phase,
                float radius,
                float width,
                float verticalSpeed,
                uint seed)
            {
                Kind = kind;
                Color = color;
                _direction = direction >= 0 ? 1 : -1;
                LifeTicks = Math.Max(1, lifeTicks);
                MaxLifeTicks = LifeTicks;
                _tailLimit = Math.Clamp(tailLimit, 2, MaxTailPoints);
                _phase = phase;
                _radius = radius;
                Width = width;
                _verticalSpeed = verticalSpeed;
                _height = RandomHeight(seed);
                _seed = seed == 0 ? 1u : seed;
                _tailStart = 0;
                TailCount = 0;
                Active = true;

                Vector3 first = CalculatePosition(player);
                PushTail(first);
            }

            public bool Tick(PlayerObject player)
            {
                if (!Active)
                    return false;

                if (Kind == TrailKind.Classic && player.IsMoving && LifeTicks > 20)
                    LifeTicks = 20;

                LifeTicks--;
                if (LifeTicks <= 0)
                {
                    Active = false;
                    return true;
                }

                switch (Kind)
                {
                    case TrailKind.Classic:
                        _phase += _direction * 0.1f;
                        _height += _verticalSpeed;
                        Width = MathF.Max(1.6f, Width - 0.025f);
                        break;

                    case TrailKind.Expanding:
                        _phase += _direction * 0.2f;
                        _radius += 0.1f;
                        Width += 0.08f;
                        _height += _verticalSpeed * 1.1f;
                        break;
                }

                PushTail(CalculatePosition(player));
                return false;
            }

            private Vector3 CalculatePosition(PlayerObject player, float tickFraction = 0f)
            {
                Vector3 center = player.WorldPosition.Translation;
                float phaseSpeed = Kind == TrailKind.Expanding ? 0.2f : 0.1f;
                float phase = _phase + _direction * phaseSpeed * tickFraction;
                float radius = _radius + (Kind == TrailKind.Expanding ? 0.1f * tickFraction : 0f);
                float verticalMultiplier = Kind == TrailKind.Expanding ? 1.1f : 1f;
                float height = _height + _verticalSpeed * verticalMultiplier * tickFraction;
                return center + new Vector3(
                    MathF.Cos(phase) * radius,
                    -MathF.Sin(phase) * radius,
                    height);
            }

            public Vector3 GetRenderPoint(PlayerObject player, int index, float interpolation)
            {
                if (interpolation > 0.001f)
                {
                    if (index == 0)
                        return CalculatePosition(player, interpolation);
                    index--;
                }

                return GetTailPoint(Math.Clamp(index, 0, TailCount - 1));
            }

            public float GetRenderLifeRatio(float interpolation) =>
                MaxLifeTicks > 0
                    ? MathHelper.Clamp((LifeTicks - interpolation) / MaxLifeTicks, 0f, 1f)
                    : 0f;

            private void PushTail(Vector3 position)
            {
                if (TailCount < _tailLimit)
                {
                    int index = (_tailStart + TailCount) % MaxTailPoints;
                    _tails[index] = position;
                    TailCount++;
                    return;
                }

                _tails[_tailStart] = position;
                _tailStart = (_tailStart + 1) % MaxTailPoints;
            }

            public Vector3 GetTailPoint(int index)
            {
                int newestToOldest = TailCount - 1 - index;
                int actual = (_tailStart + newestToOldest) % MaxTailPoints;
                if (actual < 0)
                    actual += MaxTailPoints;
                return _tails[actual];
            }

            public void Reset()
            {
                Active = false;
                LifeTicks = 0;
                MaxLifeTicks = 0;
                TailCount = 0;
                _tailStart = 0;
            }

            private static float RandomHeight(uint seed)
            {
                seed = seed * 1664525u + 1013904223u;
                return 24f + (seed & 0xFFFFu) / 65535f * 36f;
            }
        }
    }
}
