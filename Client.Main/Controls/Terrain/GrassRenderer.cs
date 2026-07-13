using Client.Main.Content;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Client.Main.Controls.Terrain
{
    /// <summary>
    /// Renders grass using static GPU chunks. Geometry is built once per world load,
    /// while wind deformation is applied in Grass.fx on GPU.
    /// </summary>
    public sealed class GrassRenderer : IDisposable
    {
        private sealed class GrassChunk : IDisposable
        {
            public VertexBuffer VertexBuffer;
            public int VertexCount;
            public BoundingBox Bounds;

            public void Dispose()
            {
                VertexBuffer?.Dispose();
                VertexBuffer = null;
            }
        }

        private const float GrassBladeBaseW = 105f;
        private const float GrassBladeBaseH = 50f;
        private const float GrassScaleMin = 0.82f;
        private const float GrassScaleMax = 1.75f;
        private const float GrassUWidth = 0.26f;
        private const float GrassPlacementRadius = 0.42f;
        private const float GrassBaseSink = 1.5f;
        private const float FootprintBoundaryEpsilon = 0.001f;
        private const int ChunkSize = 24;
        private const int GrassCandidatesPerTile = 30;
        private const int VerticesPerBlade = 6;
        private const float DensityFadeStart = 1450f;
        private const float DensityFadeEnd = 4300f;
        private const float ChunkCullDistanceSq = 4600f * 4600f;

        private volatile bool _texReady;
        private readonly object _contentLoadLock = new();
        private Task _contentLoadTask;

        private readonly GraphicsDevice _graphicsDevice;
        private readonly TerrainData _data;
        private readonly TerrainPhysics _physics;
        private readonly bool[] _terrainPlacementMask;
        private readonly bool[] _grassTileMask;
        private readonly bool[] _grassTextureLookup = new bool[256];

        private Texture2D _grassSpriteTexture;
        private Effect _grassWindEffect;
        private EffectParameter _worldParameter;
        private EffectParameter _viewParameter;
        private EffectParameter _projectionParameter;
        private EffectParameter _textureParameter;
        private EffectParameter _timeParameter;
        private EffectParameter _windSpeedParameter;
        private EffectParameter _windStrengthParameter;
        private EffectParameter _alphaCutoffParameter;
        private EffectParameter _cameraPositionParameter;
        private EffectParameter _densityFadeStartParameter;
        private EffectParameter _densityFadeEndParameter;
        private string _grassSpritePath;
        private short _worldIndex;

        private readonly List<GrassChunk> _chunks = new();

        public float GrassBrightness { get; set; } = 1.35f;
        public HashSet<byte> GrassTextureIndices { get; } = new() { 0 };
        public int Flushes { get; private set; }
        public int DrawnTriangles { get; private set; }

        public GrassRenderer(
            GraphicsDevice graphicsDevice,
            TerrainData data,
            TerrainPhysics physics,
            WindSimulator wind,
            TerrainLightManager lightManager)
        {
            _graphicsDevice = graphicsDevice;
            _data = data;
            _physics = physics;

            int tileCount = Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE;
            _terrainPlacementMask = new bool[tileCount];
            _grassTileMask = new bool[tileCount];

            // Wind animation is generated entirely in Grass.fx. The parameters remain in the
            // constructor to preserve the existing construction API.
            _ = wind;
            _ = lightManager;
        }

        public void LoadContent(short worldIndex)
        {
            _ = EnsureContentLoadTask(worldIndex);
        }

        private Task EnsureContentLoadTask(short worldIndex)
        {
            lock (_contentLoadLock)
            {
                if (_contentLoadTask == null || (_contentLoadTask.IsCompleted && !_texReady))
                    _contentLoadTask = LoadContentCoreAsync(worldIndex);

                return _contentLoadTask;
            }
        }

        private async Task LoadContentCoreAsync(short worldIndex)
        {
            if (!Constants.DRAW_GRASS)
                return;

            _worldIndex = worldIndex;
            string textureFile = worldIndex == 3 ? "TileGrass02.ozt" : "TileGrass01.ozt";
            _grassSpritePath = Path.Combine($"World{worldIndex}", textureFile);

            try
            {
                _grassSpriteTexture = await TextureLoader.Instance.PrepareAndGetTexture(_grassSpritePath);
                _texReady = _grassSpriteTexture != null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading grass texture '{_grassSpritePath}': {ex.Message}");
            }

            try
            {
                _grassWindEffect ??= MuGame.Instance?.Content?.Load<Effect>("Grass");
                CacheEffectParameters();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading grass shader effect: {ex.Message}");
                _grassWindEffect = null;
            }
        }

        public void EnsureContentLoaded(short worldIndex)
        {
            if (Constants.DRAW_GRASS && !_texReady)
                _ = EnsureContentLoadTask(worldIndex);
        }

        public void ResetMetrics()
        {
            Flushes = 0;
            DrawnTriangles = 0;
        }

        /// <summary>
        /// Builds all grass chunks once and uploads static buffers to GPU.
        /// </summary>
        public void BuildAllGrass()
        {
            DisposeChunks();

            if (!Constants.DRAW_GRASS ||
                _worldIndex == 11 ||
                _graphicsDevice == null ||
                !PreparePlacementMasks())
            {
                return;
            }

            int chunksX = (Constants.TERRAIN_SIZE + ChunkSize - 1) / ChunkSize;
            int chunksY = (Constants.TERRAIN_SIZE + ChunkSize - 1) / ChunkSize;

            for (int cy = 0; cy < chunksY; cy++)
            {
                for (int cx = 0; cx < chunksX; cx++)
                {
                    var chunk = BuildChunk(cx, cy);
                    if (chunk != null)
                        _chunks.Add(chunk);
                }
            }
        }

        /// <summary>
        /// Precomputes terrain and texture eligibility once per rebuild. Missing terrain data is
        /// treated as blocked so texture index 0 cannot accidentally generate grass everywhere.
        /// </summary>
        private bool PreparePlacementMasks()
        {
            Array.Clear(_terrainPlacementMask, 0, _terrainPlacementMask.Length);
            Array.Clear(_grassTileMask, 0, _grassTileMask.Length);
            Array.Clear(_grassTextureLookup, 0, _grassTextureLookup.Length);

            if (_data?.HeightMap == null ||
                _data.HeightMap.Length < _grassTileMask.Length ||
                GrassTextureIndices.Count == 0)
            {
                return false;
            }

            foreach (byte textureIndex in GrassTextureIndices)
                _grassTextureLookup[textureIndex] = true;

            bool hasAnyGrass = false;
            for (int y = 0; y < Constants.TERRAIN_SIZE; y++)
            {
                int rowStart = y * Constants.TERRAIN_SIZE;
                for (int x = 0; x < Constants.TERRAIN_SIZE; x++)
                {
                    int index = rowStart + x;
                    bool terrainAllowed = !_physics.IsTerrainBlocked(x, y);
                    _terrainPlacementMask[index] = terrainAllowed;

                    if (!terrainAllowed ||
                        !_physics.TryGetDominantTextureIndexAt(x, y, out byte textureIndex) ||
                        !_grassTextureLookup[textureIndex])
                    {
                        continue;
                    }

                    _grassTileMask[index] = true;
                    hasAnyGrass = true;
                }
            }

            return hasAnyGrass;
        }

        private GrassChunk BuildChunk(int chunkX, int chunkY)
        {
            int maxVertexCount = ChunkSize * ChunkSize * GrassCandidatesPerTile * VerticesPerBlade;
            var vertices = ArrayPool<GrassVertexPositionColorTextureWind>.Shared.Rent(maxVertexCount);

            try
            {
                int vertexCount = 0;
                Vector3 minBounds = new Vector3(float.MaxValue);
                Vector3 maxBounds = new Vector3(float.MinValue);

                int startX = chunkX * ChunkSize;
                int startY = chunkY * ChunkSize;
                int endX = Math.Min(startX + ChunkSize, Constants.TERRAIN_SIZE);
                int endY = Math.Min(startY + ChunkSize, Constants.TERRAIN_SIZE);
                float brightness = float.IsFinite(GrassBrightness)
                    ? MathF.Max(0f, GrassBrightness)
                    : 1f;

                for (int y = startY; y < endY; y++)
                {
                    int rowStart = y * Constants.TERRAIN_SIZE;
                    for (int x = startX; x < endX; x++)
                    {
                        int terrainIndex = rowStart + x;
                        if (!_grassTileMask[terrainIndex])
                            continue;

                        Color staticLight = Color.White;
                        if (_data.FinalLightMap != null &&
                            (uint)terrainIndex < (uint)_data.FinalLightMap.Length)
                        {
                            staticLight = _data.FinalLightMap[terrainIndex];
                        }

                        // Low-frequency value noise creates broad clumps instead of placing the
                        // same number of blades on every tile. The first candidate has a higher
                        // chance so sparse areas still blend naturally into denser patches.
                        float patchNoise = FractalPatchNoise(x, y);
                        float patchDensity = MathHelper.Lerp(0.65f, 1.0f, patchNoise);

                        for (int bladeIndex = 0; bladeIndex < GrassCandidatesPerTile; bladeIndex++)
                        {
                            float placementRoll = PseudoRandom(x, y, 701 + bladeIndex * 37);
                            float placementChance = bladeIndex == 0
                                ? MathHelper.Clamp(0.52f + patchDensity * 0.48f, 0f, 1f)
                                : patchDensity * MathHelper.Lerp(
                                    0.72f,
                                    0.94f,
                                    PseudoRandom(x, y, 809 + bladeIndex * 53));

                            if (placementRoll > placementChance)
                                continue;

                            float shadeVariation = MathHelper.Lerp(
                                0.88f,
                                1.08f,
                                PseudoRandom(x, y, 977 + bladeIndex * 29));
                            float lightScale = brightness * shadeVariation;
                            var bladeLight = new Color(
                                (byte)MathF.Min(staticLight.R * lightScale, 255f),
                                (byte)MathF.Min(staticLight.G * lightScale, 255f),
                                (byte)MathF.Min(staticLight.B * lightScale, 255f),
                                (byte)255);

                            AddGrassBlade(
                                vertices,
                                ref vertexCount,
                                x,
                                y,
                                bladeIndex,
                                bladeLight,
                                ref minBounds,
                                ref maxBounds);
                        }
                    }
                }

                if (vertexCount == 0)
                    return null;

                var chunk = new GrassChunk
                {
                    VertexCount = vertexCount,
                    Bounds = new BoundingBox(minBounds, maxBounds),
                    VertexBuffer = new VertexBuffer(
                        _graphicsDevice,
                        GrassVertexPositionColorTextureWind.VertexDeclaration,
                        vertexCount,
                        BufferUsage.WriteOnly)
                };

                chunk.VertexBuffer.SetData(vertices, 0, vertexCount);
                return chunk;
            }
            finally
            {
                ArrayPool<GrassVertexPositionColorTextureWind>.Shared.Return(vertices);
            }
        }

        private void AddGrassBlade(
            GrassVertexPositionColorTextureWind[] vertices,
            ref int vertexCount,
            int tileX,
            int tileY,
            int bladeIndex,
            Color lightColor,
            ref Vector3 minBounds,
            ref Vector3 maxBounds)
        {
            float u0 = PseudoRandom(tileX, tileY, 123 + bladeIndex * 17) * (1f - GrassUWidth);
            float u1 = u0 + GrassUWidth;

            float rx = (PseudoRandom(tileX, tileY, 17 + bladeIndex * 31) * 2f - 1f) * GrassPlacementRadius;
            float ry = (PseudoRandom(tileX, tileY, 91 + bladeIndex * 43) * 2f - 1f) * GrassPlacementRadius;

            float worldX = (tileX + 0.5f + rx) * Constants.TERRAIN_SCALE;
            float worldY = (tileY + 0.5f + ry) * Constants.TERRAIN_SCALE;

            float scaleNoise = PseudoRandom(tileX, tileY, 33 + bladeIndex * 47);
            float scale = MathHelper.Lerp(GrassScaleMin, GrassScaleMax, scaleNoise * scaleNoise);
            float widthVariation = MathHelper.Lerp(
                0.86f,
                1.14f,
                PseudoRandom(tileX, tileY, 211 + bladeIndex * 19));
            float heightVariation = MathHelper.Lerp(
                0.90f,
                1.12f,
                PseudoRandom(tileX, tileY, 263 + bladeIndex * 23));

            float width = GrassBladeBaseW * GrassUWidth * scale * widthVariation;
            float bladeHeight = GrassBladeBaseH * scale * heightVariation;
            float halfWidth = width * 0.5f;
            float angle = PseudoRandom(tileX, tileY, 57 + bladeIndex * 59) * MathHelper.Pi;
            float cosBase = MathF.Cos(angle);
            float sinBase = MathF.Sin(angle);
            float dirX = -sinBase;
            float dirY = cosBase;
            float swayAmplitude = MathF.Max(4f, bladeHeight * 0.16f);

            float endpoint1X = worldX - halfWidth * cosBase;
            float endpoint1Y = worldY - halfWidth * sinBase;
            float endpoint2X = worldX + halfWidth * cosBase;
            float endpoint2Y = worldY + halfWidth * sinBase;

            float swayExtentX = MathF.Abs(dirX) * swayAmplitude;
            float swayExtentY = MathF.Abs(dirY) * swayAmplitude;
            float footprintMinX = MathF.Min(endpoint1X, endpoint2X) - swayExtentX;
            float footprintMaxX = MathF.Max(endpoint1X, endpoint2X) + swayExtentX;
            float footprintMinY = MathF.Min(endpoint1Y, endpoint2Y) - swayExtentY;
            float footprintMaxY = MathF.Max(endpoint1Y, endpoint2Y) + swayExtentY;

            if (!IsPlacementFootprintAllowed(
                    footprintMinX,
                    footprintMinY,
                    footprintMaxX,
                    footprintMaxY))
            {
                return;
            }

            // Sample both endpoints so the quad follows the local terrain slope instead of
            // floating above one side or cutting deeply into the other.
            float baseHeight1 = _physics.RequestTerrainHeight(endpoint1X, endpoint1Y) - GrassBaseSink;
            float baseHeight2 = _physics.RequestTerrainHeight(endpoint2X, endpoint2Y) - GrassBaseSink;

            float lean = MathHelper.Lerp(
                -0.08f,
                0.08f,
                PseudoRandom(tileX, tileY, 331 + bladeIndex * 61)) * bladeHeight;
            float topOffsetX = dirX * lean;
            float topOffsetY = dirY * lean;

            Vector3 wp1 = new Vector3(endpoint1X, endpoint1Y, baseHeight1);
            Vector3 wp2 = new Vector3(endpoint2X, endpoint2Y, baseHeight2);
            Vector3 wp3 = new Vector3(endpoint1X + topOffsetX, endpoint1Y + topOffsetY, baseHeight1 + bladeHeight);
            Vector3 wp4 = new Vector3(endpoint2X + topOffsetX, endpoint2Y + topOffsetY, baseHeight2 + bladeHeight);

            minBounds = Vector3.Min(
                minBounds,
                new Vector3(footprintMinX, footprintMinY, MathF.Min(baseHeight1, baseHeight2)));
            maxBounds = Vector3.Max(
                maxBounds,
                new Vector3(
                    footprintMaxX,
                    footprintMaxY,
                    MathF.Max(baseHeight1, baseHeight2) + bladeHeight));

            Vector2 t1 = new Vector2(u0, 1f);
            Vector2 t2 = new Vector2(u1, 1f);
            Vector2 t3 = new Vector2(u0, 0f);
            Vector2 t4 = new Vector2(u1, 0f);

            float phase = angle * 2.7f + worldX * 0.0012f + worldY * 0.0011f;
            Vector4 windBottom = new Vector4(dirX, dirY, phase, 0f);
            Vector4 windTop = new Vector4(dirX, dirY, phase, swayAmplitude);

            // Alpha stores a stable per-blade density threshold used by Grass.fx. This makes
            // distant blades disappear individually instead of switching an entire chunk at once.
            byte densitySeed = (byte)Math.Clamp(
                (int)(PseudoRandom(tileX, tileY, 1201 + bladeIndex * 71) * 254f),
                0,
                254);
            lightColor.A = densitySeed;

            vertices[vertexCount++] = new GrassVertexPositionColorTextureWind(wp1, lightColor, t1, windBottom);
            vertices[vertexCount++] = new GrassVertexPositionColorTextureWind(wp2, lightColor, t2, windBottom);
            vertices[vertexCount++] = new GrassVertexPositionColorTextureWind(wp3, lightColor, t3, windTop);
            vertices[vertexCount++] = new GrassVertexPositionColorTextureWind(wp2, lightColor, t2, windBottom);
            vertices[vertexCount++] = new GrassVertexPositionColorTextureWind(wp4, lightColor, t4, windTop);
            vertices[vertexCount++] = new GrassVertexPositionColorTextureWind(wp3, lightColor, t3, windTop);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsPlacementFootprintAllowed(
            float minWorldX,
            float minWorldY,
            float maxWorldX,
            float maxWorldY)
        {
            int minTileX = (int)MathF.Floor(minWorldX / Constants.TERRAIN_SCALE);
            int minTileY = (int)MathF.Floor(minWorldY / Constants.TERRAIN_SCALE);
            int maxTileX = (int)MathF.Floor((maxWorldX - FootprintBoundaryEpsilon) / Constants.TERRAIN_SCALE);
            int maxTileY = (int)MathF.Floor((maxWorldY - FootprintBoundaryEpsilon) / Constants.TERRAIN_SCALE);

            if (minTileX < 0 || minTileY < 0 ||
                maxTileX >= Constants.TERRAIN_SIZE ||
                maxTileY >= Constants.TERRAIN_SIZE)
            {
                return false;
            }

            for (int y = minTileY; y <= maxTileY; y++)
            {
                int rowStart = y * Constants.TERRAIN_SIZE;
                for (int x = minTileX; x <= maxTileX; x++)
                {
                    if (!_terrainPlacementMask[rowStart + x])
                        return false;
                }
            }

            return true;
        }

        public void Draw()
        {
            if (!Constants.DRAW_GRASS ||
                _worldIndex == 11 ||
                !_texReady ||
                _grassWindEffect == null ||
                _grassSpriteTexture == null ||
                _chunks.Count == 0)
            {
                return;
            }

            var dev = _graphicsDevice;
            var prevBlend = dev.BlendState;
            var prevDepth = dev.DepthStencilState;
            var prevRaster = dev.RasterizerState;
            var prevSampler = dev.SamplerStates[0];

            try
            {
                // Grass.fx already rejects transparent pixels with clip(), so blending only
                // adds overdraw and disables the cheapest opaque path.
                dev.BlendState = BlendState.Opaque;
                dev.DepthStencilState = DepthStencilState.Default;
                dev.RasterizerState = RasterizerState.CullNone;
                dev.SamplerStates[0] = SamplerState.LinearClamp;

                float timeSeconds = (float)(MuGame.Instance?.GameTime.TotalGameTime.TotalSeconds ?? 0.0);
                _worldParameter?.SetValue(Matrix.Identity);
                _viewParameter?.SetValue(Camera.Instance.View);
                _projectionParameter?.SetValue(Camera.Instance.Projection);
                _textureParameter?.SetValue(_grassSpriteTexture);
                _timeParameter?.SetValue(timeSeconds);
                _windSpeedParameter?.SetValue(1.55f);
                _windStrengthParameter?.SetValue(0.68f);
                _alphaCutoffParameter?.SetValue(0.36f);
                _cameraPositionParameter?.SetValue(Camera.Instance.Position);
                _densityFadeStartParameter?.SetValue(DensityFadeStart);
                _densityFadeEndParameter?.SetValue(DensityFadeEnd);

                var frustum = Camera.Instance.Frustum;
                var camPos = Camera.Instance.Position;

                if (_grassWindEffect.CurrentTechnique == null)
                    return;

                foreach (var pass in _grassWindEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();

                    for (int i = 0; i < _chunks.Count; i++)
                    {
                        var chunk = _chunks[i];
                        if (chunk?.VertexBuffer == null || chunk.VertexBuffer.IsDisposed)
                            continue;

                        // The shader reaches zero density before this conservative AABB
                        // distance cull, so chunks can never visibly pop at the outer range.
                        if (DistanceSquaredToBoundsXY(camPos, chunk.Bounds) > ChunkCullDistanceSq)
                            continue;

                        if (frustum.Contains(chunk.Bounds) == ContainmentType.Disjoint)
                            continue;

                        if (chunk.VertexCount <= 0)
                            continue;

                        dev.SetVertexBuffer(chunk.VertexBuffer);
                        dev.DrawPrimitives(PrimitiveType.TriangleList, 0, chunk.VertexCount / 3);

                        DrawnTriangles += chunk.VertexCount / 3;
                        Flushes++;
                    }
                }
            }
            finally
            {
                dev.SetVertexBuffer(null);
                dev.BlendState = prevBlend;
                dev.DepthStencilState = prevDepth;
                dev.RasterizerState = prevRaster;
                dev.SamplerStates[0] = prevSampler;
            }
        }

        private void CacheEffectParameters()
        {
            if (_grassWindEffect == null)
                return;

            _worldParameter = _grassWindEffect.Parameters["World"];
            _viewParameter = _grassWindEffect.Parameters["View"];
            _projectionParameter = _grassWindEffect.Parameters["Projection"];
            _textureParameter = _grassWindEffect.Parameters["GrassTexture"];
            _timeParameter = _grassWindEffect.Parameters["Time"];
            _windSpeedParameter = _grassWindEffect.Parameters["WindSpeed"];
            _windStrengthParameter = _grassWindEffect.Parameters["WindStrength"];
            _alphaCutoffParameter = _grassWindEffect.Parameters["AlphaCutoff"];
            _cameraPositionParameter = _grassWindEffect.Parameters["CameraPosition"];
            _densityFadeStartParameter = _grassWindEffect.Parameters["DensityFadeStart"];
            _densityFadeEndParameter = _grassWindEffect.Parameters["DensityFadeEnd"];
        }

        public void Dispose()
        {
            DisposeChunks();
            _grassWindEffect = null;
        }

        private void DisposeChunks()
        {
            for (int i = 0; i < _chunks.Count; i++)
                _chunks[i].Dispose();

            _chunks.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DistanceSquaredToBoundsXY(Vector3 point, BoundingBox bounds)
        {
            float dx = point.X < bounds.Min.X
                ? bounds.Min.X - point.X
                : point.X > bounds.Max.X
                    ? point.X - bounds.Max.X
                    : 0f;
            float dy = point.Y < bounds.Min.Y
                ? bounds.Min.Y - point.Y
                : point.Y > bounds.Max.Y
                    ? point.Y - bounds.Max.Y
                    : 0f;
            return dx * dx + dy * dy;
        }

        private static float FractalPatchNoise(int tileX, int tileY)
        {
            float broad = ValueNoise(tileX / 8f, tileY / 8f, 1501);
            float medium = ValueNoise(tileX / 3.5f, tileY / 3.5f, 1601);
            float value = broad * 0.72f + medium * 0.28f;
            return SmoothStep(0.12f, 0.88f, value);
        }

        private static float ValueNoise(float x, float y, int salt)
        {
            int x0 = (int)MathF.Floor(x);
            int y0 = (int)MathF.Floor(y);
            float tx = x - x0;
            float ty = y - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);

            float n00 = PseudoRandom(x0, y0, salt);
            float n10 = PseudoRandom(x0 + 1, y0, salt);
            float n01 = PseudoRandom(x0, y0 + 1, salt);
            float n11 = PseudoRandom(x0 + 1, y0 + 1, salt);

            float nx0 = MathHelper.Lerp(n00, n10, tx);
            float nx1 = MathHelper.Lerp(n01, n11, tx);
            return MathHelper.Lerp(nx0, nx1, ty);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float SmoothStep(float edge0, float edge1, float value)
        {
            float t = MathHelper.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float PseudoRandom(int x, int y, int salt = 0)
        {
            uint h = (uint)(x * 73856093 ^ y * 19349663 ^ salt * 83492791);
            h ^= h >> 13;
            h *= 0x165667B1u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }


    }
}
