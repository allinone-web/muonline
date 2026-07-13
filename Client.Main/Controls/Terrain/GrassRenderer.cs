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
            public Vector3 Center;

            public void Dispose()
            {
                VertexBuffer?.Dispose();
                VertexBuffer = null;
            }
        }

        private const float GrassBladeBaseW = 130f;
        private const float GrassBladeBaseH = 45f;
        private const float GrassScaleMax = 3.0f;
        private const float GrassUWidth = 0.30f;
        private const float FootprintBoundaryEpsilon = 0.001f;
        private const int ChunkSize = 16; // 16x16 tiles
        private const int GrassPerTile = 6;
        private const int VerticesPerBlade = 6;
        private const float MaxRenderDistanceSq = 5000f * 5000f;

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
        private string _grassSpritePath;
        private short _worldIndex;

        private readonly List<GrassChunk> _chunks = new();

        public float GrassBrightness { get; set; } = 2f;
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

        private static readonly object _premulLock = new();
        private static readonly HashSet<string> _premultipliedOnce = new(StringComparer.OrdinalIgnoreCase);

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
                if (_grassSpriteTexture != null)
                {
                    bool doPremul = false;
                    lock (_premulLock)
                    {
                        if (!_premultipliedOnce.Contains(_grassSpritePath))
                        {
                            _premultipliedOnce.Add(_grassSpritePath);
                            doPremul = true;
                        }
                    }

                    if (doPremul)
                        PremultiplyAlpha(_grassSpriteTexture);

                    _texReady = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading grass texture '{_grassSpritePath}': {ex.Message}");
            }

            try
            {
                _grassWindEffect ??= MuGame.Instance?.Content?.Load<Effect>("Grass");
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
            int maxVertexCount = ChunkSize * ChunkSize * GrassPerTile * VerticesPerBlade;
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

                        var tileLight = new Color(
                            (byte)MathF.Min(staticLight.R * brightness, 255f),
                            (byte)MathF.Min(staticLight.G * brightness, 255f),
                            (byte)MathF.Min(staticLight.B * brightness, 255f));

                        for (int bladeIndex = 0; bladeIndex < GrassPerTile; bladeIndex++)
                        {
                            AddGrassBlade(
                                vertices,
                                ref vertexCount,
                                x,
                                y,
                                bladeIndex,
                                tileLight,
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
                    Center = (minBounds + maxBounds) * 0.5f,
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
            float u0 = PseudoRandom(tileX, tileY, 123 + bladeIndex) * (1f - GrassUWidth);
            float u1 = u0 + GrassUWidth;
            float maxOffset = 0.5f - (GrassUWidth * 0.5f);

            float rx = (PseudoRandom(tileX, tileY, 17 + bladeIndex) * 2f - 1f) * maxOffset;
            float ry = (PseudoRandom(tileX, tileY, 91 + bladeIndex) * 2f - 1f) * maxOffset;

            float worldX = (tileX + 0.5f + rx) * Constants.TERRAIN_SCALE;
            float worldY = (tileY + 0.5f + ry) * Constants.TERRAIN_SCALE;
            float scale = MathHelper.Lerp(1.0f, GrassScaleMax, PseudoRandom(tileX, tileY, 33 + bladeIndex));
            float jitter = MathHelper.ToRadians((PseudoRandom(tileX, tileY, 57 + bladeIndex) - 0.5f) * 180f);

            float width = GrassBladeBaseW * GrassUWidth * scale;
            float bladeHeight = GrassBladeBaseH * scale;
            float halfWidth = width * 0.5f;
            float baseAngle = MathHelper.ToRadians(45f) + jitter;
            float cosBase = MathF.Cos(baseAngle);
            float sinBase = MathF.Sin(baseAngle);
            float dirX = -sinBase;
            float dirY = cosBase;
            float swayAmplitude = MathF.Max(6f, bladeHeight * 0.22f);

            float endpoint1X = worldX - halfWidth * cosBase;
            float endpoint1Y = worldY - halfWidth * sinBase;
            float endpoint2X = worldX + halfWidth * cosBase;
            float endpoint2Y = worldY + halfWidth * sinBase;

            // Include the maximum shader displacement in the placement footprint. This prevents
            // a valid source tile from rendering a wide or wind-bent blade over a NoMove or NoGround tile.
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

            float height = _physics.RequestTerrainHeight(worldX, worldY);
            Vector3 basePos = new Vector3(worldX, worldY, height);

            Vector3 wp1 = new Vector3(endpoint1X, endpoint1Y, basePos.Z);
            Vector3 wp2 = new Vector3(endpoint2X, endpoint2Y, basePos.Z);
            Vector3 wp3 = new Vector3(endpoint1X, endpoint1Y, basePos.Z + bladeHeight);
            Vector3 wp4 = new Vector3(endpoint2X, endpoint2Y, basePos.Z + bladeHeight);

            // Bounds include the maximum GPU wind displacement to avoid edge-culling pops.
            minBounds = Vector3.Min(
                minBounds,
                new Vector3(footprintMinX, footprintMinY, basePos.Z));
            maxBounds = Vector3.Max(
                maxBounds,
                new Vector3(footprintMaxX, footprintMaxY, basePos.Z + bladeHeight));

            Vector2 t1 = new Vector2(u0, 1f);
            Vector2 t2 = new Vector2(u1, 1f);
            Vector2 t3 = new Vector2(u0, 0f);
            Vector2 t4 = new Vector2(u1, 0f);

            float phase = jitter * 2.7f + basePos.X * 0.0012f + basePos.Y * 0.0011f;
            Vector4 windBottom = new Vector4(dirX, dirY, phase, 0f);
            Vector4 windTop = new Vector4(dirX, dirY, phase, swayAmplitude);

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
                dev.BlendState = BlendState.AlphaBlend;
                dev.DepthStencilState = DepthStencilState.Default;
                dev.RasterizerState = RasterizerState.CullNone;
                dev.SamplerStates[0] = SamplerState.PointClamp;

                float timeSeconds = (float)(MuGame.Instance?.GameTime.TotalGameTime.TotalSeconds ?? 0.0);
                _grassWindEffect.Parameters["World"]?.SetValue(Matrix.Identity);
                _grassWindEffect.Parameters["View"]?.SetValue(Camera.Instance.View);
                _grassWindEffect.Parameters["Projection"]?.SetValue(Camera.Instance.Projection);
                _grassWindEffect.Parameters["GrassTexture"]?.SetValue(_grassSpriteTexture);
                _grassWindEffect.Parameters["Time"]?.SetValue(timeSeconds);
                _grassWindEffect.Parameters["WindSpeed"]?.SetValue(2.2f);
                _grassWindEffect.Parameters["WindStrength"]?.SetValue(1.0f);
                _grassWindEffect.Parameters["AlphaCutoff"]?.SetValue(64f / 255f);

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

                        if (Vector3.DistanceSquared(camPos, chunk.Center) > MaxRenderDistanceSq)
                            continue;

                        if (frustum.Contains(chunk.Bounds) == ContainmentType.Disjoint)
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
        private static float PseudoRandom(int x, int y, int salt = 0)
        {
            uint h = (uint)(x * 73856093 ^ y * 19349663 ^ salt * 83492791);
            h ^= h >> 13;
            h *= 0x165667B1u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }

        private static void PremultiplyAlpha(Texture2D tex)
        {
            if (tex.Format != SurfaceFormat.Color || tex.IsDisposed)
                return;

            int len = tex.Width * tex.Height;
            var px = new Color[len];
            tex.GetData(px);

            for (int i = 0; i < len; i++)
            {
                var c = px[i];
                if (c.A == 255)
                    continue;

                px[i] = new Color(
                    (byte)(c.R * c.A / 255),
                    (byte)(c.G * c.A / 255),
                    (byte)(c.B * c.A / 255),
                    c.A);
            }

            tex.SetData(px);
        }
    }
}
