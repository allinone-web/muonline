using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Icarus
{
    /// <summary>
    /// Recreates the original Icarus storm controller: asset-driven static clouds,
    /// short global cloud flashes, one-frame local cloud lights, paired procedural
    /// lightning joints and the sparse lights travelling below the map.
    /// </summary>
    public sealed class IcarusStormSystem : EffectObject
    {
        private const string CloudLightTexturePath = "Effect/cloudLight.jpg";
        private const string JointThunderTexturePath = "Effect/JointThunder01.jpg";
        private const string UnderCloudLightTexturePath = "Effect/flare01.jpg";

        private const float LegacyStepSeconds = 1f / 25f;
        private const int MaxLegacyStepsPerFrame = 5;
        private const int MaxBolts = 24;
        private const int MaxBoltPoints = 25;
        private const int MaxCloudFlashes = 24;
        private const int MaxUnderCloudLights = 64;

        // SourceMain used a much deeper projection and its raw lightning offsets
        // do not map 1:1 to this client's camera. Preserve the four compositions
        // and directions, but fit them into the visible Icarus volume.
        private const float GlobalLightningPositionScale = 0.36f;
        private const float GlobalLightningWidthScale = 0.42f;
        private const float LocalLightningDirectionScale = 0.12f;
        private const float LocalLightningWidthScale = 0.42f;

        private readonly IcarusFlashCloudObject _flashCloud;
        private readonly LightningBolt[] _bolts = new LightningBolt[MaxBolts];
        private readonly CloudFlash[] _cloudFlashes = new CloudFlash[MaxCloudFlashes];
        private readonly UnderCloudLight[] _underCloudLights = new UnderCloudLight[MaxUnderCloudLights];

        private readonly VertexPositionColorTexture[] _boltVertices =
            new VertexPositionColorTexture[MaxBolts * MaxBoltPoints * 2];
        private readonly short[] _boltIndices =
            new short[MaxBolts * (MaxBoltPoints - 1) * 6];
        private readonly VertexPositionColorTexture[] _billboardVertices =
            new VertexPositionColorTexture[Math.Max(MaxCloudFlashes, MaxUnderCloudLights) * 4];
        private static readonly short[] BillboardIndices =
            QuadIndexCache.Get(Math.Max(MaxCloudFlashes, MaxUnderCloudLights));

        private readonly DynamicLight _globalFlashLight;
        private BasicEffect _effect;
        private Texture2D _cloudLightTexture;
        private Texture2D _jointThunderTexture;
        private Texture2D _underCloudLightTexture;
        private float _legacyAccumulator;
        private int _flashCloudTicks;
        private int _flashCloudSpawnFrame = -1;
        private bool _lightRegistered;
        private int _boltSpawnSequence;

        public IcarusStormSystem()
        {
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-30000f, -30000f, -15000f),
                new Vector3(30000f, 30000f, 15000f));
            IsTransparent = false;
            AffectedByTransparency = false;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;

            for (int i = 0; i < _bolts.Length; i++)
                _bolts[i] = new LightningBolt();

            _flashCloud = new IcarusFlashCloudObject();
            Children.Add(_flashCloud);

            _globalFlashLight = new DynamicLight
            {
                Owner = this,
                Position = Vector3.Zero,
                Color = Vector3.Zero,
                Radius = Constants.TERRAIN_SCALE * 2f,
                Intensity = 0f
            };
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            _cloudLightTexture = await PrepareTexture(CloudLightTexturePath);
            _jointThunderTexture = await PrepareTexture(JointThunderTexturePath);
            _underCloudLightTexture = await PrepareTexture(UnderCloudLightTexturePath);

            _effect = new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = true,
                TextureEnabled = true,
                LightingEnabled = false,
                World = Matrix.Identity
            };

            if (World?.Terrain != null && !_lightRegistered)
            {
                World.Terrain.AddDynamicLight(_globalFlashLight);
                _lightRegistered = true;
            }
        }

        public override void Update(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready)
                return;

            float elapsed = MathHelper.Clamp(
                (float)gameTime.ElapsedGameTime.TotalSeconds,
                0f,
                LegacyStepSeconds * MaxLegacyStepsPerFrame);
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

            // Update the preloaded MODEL_CLOUD child after storm state changes so its
            // transform and light buffers are ready for the same rendered frame.
            base.Update(gameTime);
        }

        private void TickLegacy()
        {
            UpdateTransientEffects();

            if (!TryGetHeroPosition(out Vector3 heroPosition))
                return;

            // Original MoveHeavenThunder: 1/50 at the 25 Hz reference rate.
            if (MuGame.Random.Next(50) == 0)
                SpawnGlobalFlash(heroPosition);

            SpawnLocalCloudEffects();

            // Additional BITMAP_LIGHT activity below the player: 1/10 per legacy tick.
            if (MuGame.Random.Next(10) == 0)
                SpawnUnderCloudLight(heroPosition);
        }

        private void UpdateTransientEffects()
        {
            if (_flashCloudTicks > 0 && _flashCloudSpawnFrame != MuGame.FrameIndex)
            {
                _flashCloudTicks--;
                if (_flashCloudTicks == 0)
                {
                    _flashCloud.Hide();
                    _globalFlashLight.Intensity = 0f;
                }
            }

            for (int i = 0; i < _cloudFlashes.Length; i++)
            {
                if (_cloudFlashes[i].LifeTicks > 0 &&
                    _cloudFlashes[i].SpawnFrame != MuGame.FrameIndex)
                {
                    _cloudFlashes[i].LifeTicks--;
                }
            }

            for (int i = 0; i < _bolts.Length; i++)
                _bolts[i].Tick();

            for (int i = 0; i < _underCloudLights.Length; i++)
            {
                ref UnderCloudLight light = ref _underCloudLights[i];
                if (light.LifeTicks <= 0)
                    continue;

                light.LifeTicks--;
                if (light.LifeTicks <= 0)
                    continue;

                light.Position += light.VelocityPerTick;
                light.Scale += 0.0025f;
            }
        }

        private void SpawnGlobalFlash(Vector3 heroPosition)
        {
            float luminosity = MuGame.Random.Next(4, 8) * 0.05f;
            // The original warm RGB ratio becomes visibly green with the current
            // cloud assets and additive pipeline. Keep the same low intensity while
            // biasing the flash toward neutral storm-blue light.
            Vector3 light = new(
                luminosity * 0.24f,
                luminosity * 0.27f,
                luminosity * 0.30f);

            _flashCloud.Show(heroPosition, light);
            _flashCloudTicks = 2;
            _flashCloudSpawnFrame = MuGame.FrameIndex;

            _globalFlashLight.Position = new Vector3(
                heroPosition.X + MuGame.Random.Next(-150, 150),
                heroPosition.Y,
                heroPosition.Z + MuGame.Random.Next(-150, 150));
            _globalFlashLight.Color = light;
            _globalFlashLight.Radius = Constants.TERRAIN_SCALE * 2f;
            _globalFlashLight.Intensity = 1f;

            // One long background lightning pair for every five global flashes.
            if (MuGame.Random.Next(5) == 0)
                SpawnGlobalLightningPair(heroPosition);
        }

        private void SpawnGlobalLightningPair(Vector3 heroPosition)
        {
            int variant = MuGame.Random.Next(4);
            Vector3 anchorOffset;
            Vector3 lengthVector;
            float secondRotationDegrees;

            switch (variant)
            {
                case 0:
                    anchorOffset = new Vector3(-400f, -1000f, 0f);
                    lengthVector = new Vector3(-200f, -1000f, 0f);
                    secondRotationDegrees = 240f;
                    break;
                case 1:
                    anchorOffset = new Vector3(-300f, -400f, 0f);
                    lengthVector = new Vector3(-500f, -1000f, 0f);
                    secondRotationDegrees = 210f;
                    break;
                case 2:
                    anchorOffset = new Vector3(-200f, -400f, 0f);
                    lengthVector = new Vector3(-1000f, -1500f, 0f);
                    secondRotationDegrees = 235f;
                    break;
                default:
                    anchorOffset = new Vector3(-200f, 400f, 0f);
                    lengthVector = new Vector3(-600f, -1200f, 0f);
                    secondRotationDegrees = 200f;
                    break;
            }

            anchorOffset *= GlobalLightningPositionScale;
            lengthVector *= GlobalLightningPositionScale;

            Vector3 anchor = heroPosition + RotateAroundZ(anchorOffset, -45f);
            Vector3 offset = RotateAroundZ(lengthVector, secondRotationDegrees);
            Vector3 start = anchor + offset;
            Vector3 end = anchor - offset;
            float verticalOffset = 300f * GlobalLightningPositionScale;
            start.Z -= verticalOffset;
            end.Z -= verticalOffset;

            float firstWidth = MuGame.Random.Next(40, 50) * GlobalLightningWidthScale;
            float secondWidth = MuGame.Random.Next(40, 50) * GlobalLightningWidthScale;
            SpawnBolt(start, end, firstWidth, 0, 20, Vector3.One, 18);
            SpawnBolt(start, end, secondWidth, 0, 20, Vector3.One, 18);
        }

        private void SpawnLocalCloudEffects()
        {
            if (World == null)
                return;

            var visibleObjects = World.VisibleObjects;
            for (int i = 0; i < visibleObjects.Count; i++)
            {
                WorldObject cloudObject = visibleObjects[i];
                if (cloudObject == null ||
                    !cloudObject.IsMapPlacementObject ||
                    cloudObject.Type < 0 ||
                    cloudObject.Type > 5)
                {
                    continue;
                }

                // Original local roll: 1/500 for each visible cloud/environment object.
                if (MuGame.Random.Next(500) != 0)
                    continue;

                Vector3 position = cloudObject.WorldPosition.Translation;
                float flashIntensity = MuGame.Random.Next(10) / 50f;
                Vector3 flashColor = new(
                    flashIntensity * 0.78f,
                    flashIntensity * 0.88f,
                    flashIntensity);
                SpawnCloudFlash(position, flashColor);

                SpawnLocalLightning(position);
                SpawnLocalLightning(position);
            }
        }

        private void SpawnLocalLightning(Vector3 start)
        {
            // Keep the original strongly diagonal direction, but cap it to the
            // current camera depth. The unscaled -10000 Z vector produced a bolt
            // several screens long and was mostly clipped by the far plane.
            Vector3 direction = new(
                2050f + MuGame.Random.Next(200),
                2050f + MuGame.Random.Next(200),
                -10000f);
            Vector3 end = start + direction * LocalLightningDirectionScale;

            float gray = MuGame.Random.Next(10) / 15f + 0.1f;
            int totalLifeTicks = MuGame.Random.Next(6, 26);
            int delayTicks = Math.Max(0, totalLifeTicks - 4);
            float width = MuGame.Random.Next(10, 30) * LocalLightningWidthScale;
            SpawnBolt(
                start,
                end,
                width,
                delayTicks,
                4,
                new Vector3(gray),
                14);
        }

        private void SpawnCloudFlash(Vector3 position, Vector3 color)
        {
            int slot = FindCloudFlashSlot();
            ref CloudFlash flash = ref _cloudFlashes[slot];
            flash.Position = position;
            flash.Color = color;
            flash.Scale = 0.5f;
            flash.LifeTicks = 1;
            flash.SpawnFrame = MuGame.FrameIndex;
        }

        private void SpawnUnderCloudLight(Vector3 heroPosition)
        {
            int slot = FindUnderCloudLightSlot();
            ref UnderCloudLight light = ref _underCloudLights[slot];
            light.Position = new Vector3(
                heroPosition.X + MuGame.Random.Next(-2500, 2500),
                heroPosition.Y + MuGame.Random.Next(-2500, 2500),
                heroPosition.Z - 1000f);
            light.VelocityPerTick = new Vector3(
                (float)(MuGame.Random.NextDouble() - 0.5) * 1.5f,
                (float)(MuGame.Random.NextDouble() - 0.5) * 1.5f,
                4f + (float)MuGame.Random.NextDouble() * 4f);
            light.Scale = 3f;
            light.MaxLifeTicks = 400;
            light.LifeTicks = light.MaxLifeTicks;
        }

        private void SpawnBolt(
            Vector3 start,
            Vector3 end,
            float width,
            int delayTicks,
            int visibleTicks,
            Vector3 color,
            int pointCount)
        {
            int slot = FindBoltSlot();
            uint seed = unchecked((uint)(MuGame.Random.Next() ^ ++_boltSpawnSequence * 397));
            _bolts[slot].Activate(
                start,
                end,
                width,
                delayTicks,
                visibleTicks,
                color,
                Math.Clamp(pointCount, 4, MaxBoltPoints),
                seed);
        }

        private int FindBoltSlot()
        {
            int oldestIndex = 0;
            int smallestRemaining = int.MaxValue;

            for (int i = 0; i < _bolts.Length; i++)
            {
                if (!_bolts[i].Active)
                    return i;

                int remaining = _bolts[i].DelayTicks + _bolts[i].LifeTicks;
                if (remaining < smallestRemaining)
                {
                    smallestRemaining = remaining;
                    oldestIndex = i;
                }
            }

            return oldestIndex;
        }

        private int FindCloudFlashSlot()
        {
            for (int i = 0; i < _cloudFlashes.Length; i++)
            {
                if (_cloudFlashes[i].LifeTicks <= 0)
                    return i;
            }

            return 0;
        }

        private int FindUnderCloudLightSlot()
        {
            int oldestIndex = 0;
            int smallestLife = int.MaxValue;

            for (int i = 0; i < _underCloudLights.Length; i++)
            {
                int life = _underCloudLights[i].LifeTicks;
                if (life <= 0)
                    return i;

                if (life < smallestLife)
                {
                    smallestLife = life;
                    oldestIndex = i;
                }
            }

            return oldestIndex;
        }

        private bool TryGetHeroPosition(out Vector3 position)
        {
            if (World is WalkableWorldControl walkable && walkable.Walker != null)
            {
                position = walkable.Walker.WorldPosition.Translation;
                return true;
            }

            position = Vector3.Zero;
            return false;
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible || _effect == null)
                return;

            // Draw the MODEL_CLOUD child first, then billboard flashes and lightning ribbons.
            base.DrawAfter(gameTime);

            BlendState oldBlend = GraphicsDevice.BlendState;
            DepthStencilState oldDepth = GraphicsDevice.DepthStencilState;
            RasterizerState oldRasterizer = GraphicsDevice.RasterizerState;
            SamplerState oldSampler = GraphicsDevice.SamplerStates[0];

            try
            {
                GraphicsDevice.BlendState = Blendings.OneOneAdditive;
                GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                GraphicsDevice.RasterizerState = RasterizerState.CullNone;
                GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

                _effect.World = Matrix.Identity;
                _effect.View = Camera.Instance.View;
                _effect.Projection = Camera.Instance.Projection;

                DrawCloudFlashes();
                DrawUnderCloudLights();
                DrawLightningBolts();
            }
            finally
            {
                GraphicsDevice.BlendState = oldBlend;
                GraphicsDevice.DepthStencilState = oldDepth;
                GraphicsDevice.RasterizerState = oldRasterizer;
                GraphicsDevice.SamplerStates[0] = oldSampler;
            }
        }

        private void DrawCloudFlashes()
        {
            if (_cloudLightTexture == null || _cloudLightTexture.IsDisposed)
                return;

            int quadCount = 0;
            Vector3 cameraRight = Camera.Instance.Right;
            Vector3 cameraUp = Camera.Instance.Up;

            for (int i = 0; i < _cloudFlashes.Length; i++)
            {
                ref CloudFlash flash = ref _cloudFlashes[i];
                if (flash.LifeTicks <= 0)
                    continue;

                float halfWidth = _cloudLightTexture.Width * flash.Scale * 0.5f;
                float halfHeight = _cloudLightTexture.Height * flash.Scale * 0.5f;
                WriteBillboardQuad(
                    quadCount++,
                    flash.Position,
                    cameraRight * halfWidth,
                    cameraUp * halfHeight,
                    ToColor(flash.Color));
            }

            DrawBillboardBatch(_cloudLightTexture, quadCount);
        }

        private void DrawUnderCloudLights()
        {
            if (_underCloudLightTexture == null || _underCloudLightTexture.IsDisposed)
                return;

            int quadCount = 0;
            Vector3 cameraRight = Camera.Instance.Right;
            Vector3 cameraUp = Camera.Instance.Up;

            for (int i = 0; i < _underCloudLights.Length; i++)
            {
                ref UnderCloudLight light = ref _underCloudLights[i];
                if (light.LifeTicks <= 0)
                    continue;

                float lifeRatio = light.MaxLifeTicks > 0
                    ? light.LifeTicks / (float)light.MaxLifeTicks
                    : 0f;
                float intensity = lifeRatio < 0.1f ? lifeRatio * 10f : 1f;
                float halfWidth = _underCloudLightTexture.Width * light.Scale * 0.5f;
                float halfHeight = _underCloudLightTexture.Height * light.Scale * 0.5f;

                WriteBillboardQuad(
                    quadCount++,
                    light.Position,
                    cameraRight * halfWidth,
                    cameraUp * halfHeight,
                    new Color(intensity, intensity, intensity, 1f));
            }

            DrawBillboardBatch(_underCloudLightTexture, quadCount);
        }

        private void DrawBillboardBatch(Texture2D texture, int quadCount)
        {
            if (quadCount <= 0)
                return;

            _effect.Texture = texture;
            _effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                _billboardVertices,
                0,
                quadCount * 4,
                BillboardIndices,
                0,
                quadCount * 2);
        }

        private void DrawLightningBolts()
        {
            if (_jointThunderTexture == null || _jointThunderTexture.IsDisposed)
                return;

            int vertexCount = 0;
            int indexCount = 0;

            for (int i = 0; i < _bolts.Length; i++)
            {
                LightningBolt bolt = _bolts[i];
                if (!bolt.IsVisible)
                    continue;

                bolt.RebuildVisiblePath();
                AppendBoltGeometry(bolt, ref vertexCount, ref indexCount);
            }

            if (vertexCount == 0 || indexCount == 0)
                return;

            _effect.Texture = _jointThunderTexture;
            _effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                _boltVertices,
                0,
                vertexCount,
                _boltIndices,
                0,
                indexCount / 3);
        }

        private void AppendBoltGeometry(LightningBolt bolt, ref int vertexCount, ref int indexCount)
        {
            int baseVertex = vertexCount;
            Vector3 cameraPosition = Camera.Instance.Position;
            Color color = ToColor(bolt.Color * bolt.GetIntensity());

            for (int point = 0; point < bolt.PointCount; point++)
            {
                Vector3 current = bolt.Points[point];
                Vector3 tangent;
                if (point == 0)
                    tangent = bolt.Points[1] - current;
                else if (point == bolt.PointCount - 1)
                    tangent = current - bolt.Points[point - 1];
                else
                    tangent = bolt.Points[point + 1] - bolt.Points[point - 1];

                Vector3 viewDirection = cameraPosition - current;
                Vector3 side = Vector3.Cross(tangent, viewDirection);
                if (side.LengthSquared() < 0.0001f)
                    side = Camera.Instance.Right;
                else
                    side.Normalize();

                float progress = point / (float)(bolt.PointCount - 1);
                float width = bolt.Width * MathF.Pow(1f - progress, 0.7f);
                width = MathF.Max(width, bolt.Width * 0.08f);
                Vector3 offset = side * (width * 0.5f);

                _boltVertices[vertexCount++] = new VertexPositionColorTexture(
                    current - offset,
                    color,
                    new Vector2(0f, progress));
                _boltVertices[vertexCount++] = new VertexPositionColorTexture(
                    current + offset,
                    color,
                    new Vector2(1f, progress));
            }

            for (int segment = 0; segment < bolt.PointCount - 1; segment++)
            {
                short left0 = checked((short)(baseVertex + segment * 2));
                short right0 = checked((short)(left0 + 1));
                short left1 = checked((short)(left0 + 2));
                short right1 = checked((short)(left0 + 3));

                _boltIndices[indexCount++] = left0;
                _boltIndices[indexCount++] = right0;
                _boltIndices[indexCount++] = left1;
                _boltIndices[indexCount++] = right0;
                _boltIndices[indexCount++] = right1;
                _boltIndices[indexCount++] = left1;
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

        private static Vector3 RotateAroundZ(Vector3 value, float degrees)
        {
            float radians = MathHelper.ToRadians(degrees);
            float cosine = MathF.Cos(radians);
            float sine = MathF.Sin(radians);
            return new Vector3(
                value.X * cosine - value.Y * sine,
                value.X * sine + value.Y * cosine,
                value.Z);
        }

        private static Color ToColor(Vector3 value)
        {
            return new Color(
                MathHelper.Clamp(value.X, 0f, 1f),
                MathHelper.Clamp(value.Y, 0f, 1f),
                MathHelper.Clamp(value.Z, 0f, 1f),
                1f);
        }

        private static async Task<Texture2D> PrepareTexture(string path)
        {
            try
            {
                Texture2D texture = await TextureLoader.Instance.PrepareAndGetTexture(path);
                return texture ?? GraphicsManager.Instance.Pixel;
            }
            catch
            {
                return GraphicsManager.Instance.Pixel;
            }
        }

        public override void Dispose()
        {
            _globalFlashLight.Intensity = 0f;
            _effect?.Dispose();
            _effect = null;
            _cloudLightTexture = null;
            _jointThunderTexture = null;
            _underCloudLightTexture = null;
            base.Dispose();
        }

        private struct CloudFlash
        {
            public Vector3 Position;
            public Vector3 Color;
            public float Scale;
            public int LifeTicks;
            public int SpawnFrame;
        }

        private struct UnderCloudLight
        {
            public Vector3 Position;
            public Vector3 VelocityPerTick;
            public float Scale;
            public int LifeTicks;
            public int MaxLifeTicks;
        }

        private sealed class LightningBolt
        {
            public readonly Vector3[] Points = new Vector3[MaxBoltPoints];

            public bool Active { get; private set; }
            public int DelayTicks { get; private set; }
            public int LifeTicks { get; private set; }
            public int MaxLifeTicks { get; private set; }
            public int PointCount { get; private set; }
            public float Width { get; private set; }
            public Vector3 Color { get; private set; }

            private Vector3 _start;
            private Vector3 _end;
            private uint _seed;
            private int _visibleAge;
            private int _spawnFrame;
            private int _visibleStartFrame;

            public bool IsVisible => Active && DelayTicks <= 0 && LifeTicks > 0;

            public void Activate(
                Vector3 start,
                Vector3 end,
                float width,
                int delayTicks,
                int visibleTicks,
                Vector3 color,
                int pointCount,
                uint seed)
            {
                _start = start;
                _end = end;
                Width = width;
                DelayTicks = Math.Max(0, delayTicks);
                LifeTicks = Math.Max(1, visibleTicks);
                MaxLifeTicks = LifeTicks;
                Color = color;
                PointCount = Math.Clamp(pointCount, 4, MaxBoltPoints);
                _seed = seed == 0 ? 1u : seed;
                _visibleAge = 0;
                _spawnFrame = MuGame.FrameIndex;
                _visibleStartFrame = DelayTicks == 0 ? _spawnFrame : -1;
                Active = true;
                BuildPath(_seed);
            }

            public void Tick()
            {
                if (!Active)
                    return;

                if (DelayTicks > 0)
                {
                    DelayTicks--;
                    if (DelayTicks == 0)
                    {
                        _visibleStartFrame = MuGame.FrameIndex;
                        BuildPath(_seed ^ 0x9E3779B9u);
                    }
                    return;
                }

                if (_spawnFrame == MuGame.FrameIndex || _visibleStartFrame == MuGame.FrameIndex)
                    return;

                LifeTicks--;
                _visibleAge++;
                if (LifeTicks <= 0)
                {
                    Active = false;
                    return;
                }
            }

            public void RebuildVisiblePath()
            {
                if (!IsVisible)
                    return;

                // Evolving tails are rebuilt from a deterministic tick-dependent seed.
                BuildPath(_seed + unchecked((uint)(_visibleAge * 747796405)));
            }

            public float GetIntensity()
            {
                if (!IsVisible)
                    return 0f;

                if (MaxLifeTicks <= 4)
                {
                    // Local subtype-6 bolts are deliberately abrupt and flicker during
                    // their final four visible legacy frames.
                    return LifeTicks switch
                    {
                        4 => 0.75f,
                        3 => 1f,
                        2 => 0.85f,
                        _ => 0.55f
                    };
                }

                float fade = MathHelper.Clamp(LifeTicks / 4f, 0f, 1f);
                float flicker = (_visibleAge & 1) == 0 ? 1f : 0.82f;
                return fade * flicker;
            }

            private void BuildPath(uint seed)
            {
                Points[0] = _start;
                Points[PointCount - 1] = _end;

                Vector3 direction = _end - _start;
                float length = direction.Length();
                if (length < 0.001f)
                {
                    for (int i = 1; i < PointCount - 1; i++)
                        Points[i] = _start;
                    return;
                }

                direction /= length;
                Vector3 reference = MathF.Abs(Vector3.Dot(direction, Vector3.UnitZ)) > 0.9f
                    ? Vector3.UnitY
                    : Vector3.UnitZ;
                Vector3 perpendicularA = Vector3.Cross(direction, reference);
                perpendicularA.Normalize();
                Vector3 perpendicularB = Vector3.Cross(direction, perpendicularA);
                perpendicularB.Normalize();

                float amplitude = MathF.Min(length * 0.028f, Width * 3.5f + 70f);
                uint state = seed;

                for (int i = 1; i < PointCount - 1; i++)
                {
                    float progress = i / (float)(PointCount - 1);
                    float envelope = MathF.Sin(progress * MathHelper.Pi);
                    float offsetA = NextSigned(ref state) * amplitude * envelope;
                    float offsetB = NextSigned(ref state) * amplitude * 0.65f * envelope;
                    Points[i] = Vector3.Lerp(_start, _end, progress) +
                                perpendicularA * offsetA +
                                perpendicularB * offsetB;
                }
            }

            private static float NextSigned(ref uint state)
            {
                state = state * 1664525u + 1013904223u;
                float normalized = (state & 0x00FFFFFFu) / 16777215f;
                return normalized * 2f - 1f;
            }
        }
    }
}
