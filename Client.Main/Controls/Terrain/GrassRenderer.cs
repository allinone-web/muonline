using Client.Data.ATT;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Client.Main.Controls.Terrain
{
    /// <summary>
    /// Renders the original MU terrain-grass pass.
    ///
    /// SourceMain renders one vertical textured quad per terrain face. The quad uses
    /// Layer1, the four terrain light values, and the two upper terrain vertices are
    /// displaced by TerrainGrassWind. It is intentionally not a field of random blades.
    /// </summary>
    public sealed class GrassRenderer : IDisposable
    {
        private const int GrassTextureCount = 3;
        private const int GrassTextureBaseIndex = 0;
        private const int ChunkSize = 32;
        /// <summary>
        /// 一株草在貼圖裡佔的寬度比例。這是**比例**不是像素，所以換更高解析度的
        /// 貼圖不必動它 —— 一張圖橫排四株，不論 256 寬還是 1024 寬。
        /// </summary>
        private const float GrassUvWidth = 64f / 256f;

        /// <summary>
        /// 一株草在世界裡的高度。
        /// </summary>
        /// <remarks>
        /// 這裡原本是 <c>貼圖像素高度 × 2</c>，也就是草的高度綁在貼圖解析度上。
        /// 原始貼圖是 64 px 高，所以草是 128 個世界單位（角色約 175）。
        ///
        /// 那個相依會讓「換更清晰的貼圖」變成一個陷阱：把貼圖重繪成 256 px 高，
        /// 草就會變成 512 單位、比角色還高三倍，而且**不會有任何錯誤訊息**。
        ///
        /// 改成固定值之後：現有的 64 px 貼圖畫出來完全一樣（128 = 64 × 2），
        /// 而任何解析度的新貼圖都能直接換上去。
        /// </remarks>
        private const float GrassWorldHeight = 128f;
        private const float GrassHorizontalOffset = -50f;

        /// <summary>草叢底部相對頂部的亮度。假 AO —— 讓草看起來長在地上而不是插在地上。</summary>
        private const float GrassRootShade = 0.55f;
        private const float SpecialHeight = 1200f;

        private readonly struct GrassQuad
        {
            public readonly Vector3 BottomLeft;
            public readonly Vector3 BottomRight;
            public readonly Color Light1;
            public readonly Color Light2;
            public readonly Color Light3;
            public readonly Color Light4;
            public readonly int WindIndex1;
            public readonly int WindIndex2;
            public readonly float U;
            public readonly float Height;

            public GrassQuad(
                Vector3 bottomLeft,
                Vector3 bottomRight,
                Color light1,
                Color light2,
                Color light3,
                Color light4,
                int windIndex1,
                int windIndex2,
                float u,
                float height)
            {
                BottomLeft = bottomLeft;
                BottomRight = bottomRight;
                Light1 = light1;
                Light2 = light2;
                Light3 = light3;
                Light4 = light4;
                WindIndex1 = windIndex1;
                WindIndex2 = windIndex2;
                U = u;
                Height = height;
            }
        }

        private sealed class GrassBatch : IDisposable
        {
            public readonly Texture2D Texture;
            public readonly GrassQuad[] Quads;
            public readonly VertexPositionColorTexture[] Vertices;
            public readonly BoundingBox Bounds;
            public DynamicVertexBuffer VertexBuffer;
            public int LastWindVersion = int.MinValue;

            public GrassBatch(
                GraphicsDevice graphicsDevice,
                Texture2D texture,
                List<GrassQuad> quads,
                BoundingBox bounds)
            {
                Texture = texture;
                Quads = quads.ToArray();
                Vertices = new VertexPositionColorTexture[Quads.Length * 6];
                Bounds = bounds;
                VertexBuffer = new DynamicVertexBuffer(
                    graphicsDevice,
                    VertexPositionColorTexture.VertexDeclaration,
                    Vertices.Length,
                    BufferUsage.WriteOnly);

                BuildStaticVertices();
                VertexBuffer.SetData(Vertices, 0, Vertices.Length, SetDataOptions.Discard);
            }

            private void BuildStaticVertices()
            {
                float halfTexelU = 0.5f / Texture.Width;
                float halfTexelV = 0.5f / Texture.Height;

                for (int i = 0; i < Quads.Length; i++)
                {
                    GrassQuad quad = Quads[i];
                    Vector3 topLeft = quad.BottomLeft;
                    topLeft.X += GrassHorizontalOffset;
                    topLeft.Z += quad.Height;

                    Vector3 topRight = quad.BottomRight;
                    topRight.X += GrassHorizontalOffset;
                    topRight.Z += quad.Height;

                    int vertex = i * 6;
                    Vector2 uvTopLeft = new(quad.U + halfTexelU, halfTexelV);
                    Vector2 uvTopRight = new(quad.U + GrassUvWidth - halfTexelU, halfTexelV);
                    Vector2 uvBottomRight = new(quad.U + GrassUvWidth - halfTexelU, 1f - halfTexelV);
                    Vector2 uvBottomLeft = new(quad.U + halfTexelU, 1f - halfTexelV);

                    Vertices[vertex + 0] = new VertexPositionColorTexture(topLeft, quad.Light1, uvTopLeft);
                    Vertices[vertex + 1] = new VertexPositionColorTexture(topRight, quad.Light2, uvTopRight);
                    Vertices[vertex + 2] = new VertexPositionColorTexture(quad.BottomRight, quad.Light3, uvBottomRight);
                    Vertices[vertex + 3] = new VertexPositionColorTexture(quad.BottomRight, quad.Light3, uvBottomRight);
                    Vertices[vertex + 4] = new VertexPositionColorTexture(quad.BottomLeft, quad.Light4, uvBottomLeft);
                    Vertices[vertex + 5] = new VertexPositionColorTexture(topLeft, quad.Light1, uvTopLeft);
                }
            }

            public void Dispose()
            {
                VertexBuffer?.Dispose();
                VertexBuffer = null;
            }
        }

        private readonly GraphicsDevice _graphicsDevice;
        private readonly TerrainData _data;
        private readonly TerrainPhysics _physics;
        private readonly WindSimulator _wind;
        private readonly Texture2D[] _grassTextures = new Texture2D[GrassTextureCount];
        private readonly List<GrassBatch> _batches = new();

        private readonly object _contentLoadLock = new();
        private Task _contentLoadTask;
        private BasicEffect _additiveEffect;
        private float[] _rowOffsets;
        private volatile bool _contentReady;
        private volatile bool _rebuildPending;
        private short _worldIndex;

        public float GrassBrightness { get; set; } = 1f;
        public float AmbientLight { get; set; } = 0.25f;
        public HashSet<byte> GrassTextureIndices { get; } = new() { 0, 1, 2 };
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
            _wind = wind;
            _ = lightManager;
        }

        public void LoadContent(short worldIndex)
        {
            if (_worldIndex != worldIndex)
            {
                _worldIndex = worldIndex;
                _contentReady = false;
                _rebuildPending = true;
                _rowOffsets = null;
                lock (_contentLoadLock)
                    _contentLoadTask = null;
            }

            _ = EnsureContentLoadTask(worldIndex);
        }

        public void EnsureContentLoaded(short worldIndex)
        {
            if (Constants.DRAW_GRASS && !_contentReady)
                _ = EnsureContentLoadTask(worldIndex);
        }

        private Task EnsureContentLoadTask(short worldIndex)
        {
            lock (_contentLoadLock)
            {
                if (_contentLoadTask == null)
                    _contentLoadTask = LoadContentCoreAsync(worldIndex);

                return _contentLoadTask;
            }
        }

        private async Task LoadContentCoreAsync(short worldIndex)
        {
            if (!Constants.DRAW_GRASS)
                return;

            _worldIndex = worldIndex;
            bool specialBlendMap = IsSpecialBlendMap(worldIndex);

            for (int i = 0; i < GrassTextureCount; i++)
            {
                string fileName = i == GrassTextureBaseIndex && specialBlendMap
                    ? "TileGrass01_R.jpg"
                    : $"TileGrass0{i + 1}.ozt";
                string path = Path.Combine($"World{worldIndex}", fileName);

                try
                {
                    _grassTextures[i] = await TextureLoader.Instance
                        .PrepareAndGetTexture(path)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading grass texture '{path}': {ex.Message}");
                    _grassTextures[i] = null;
                }
            }

            _contentReady = true;
            _rebuildPending = true;
        }

        public void ResetMetrics()
        {
            Flushes = 0;
            DrawnTriangles = 0;
        }

        public void BuildAllGrass()
        {
            _rebuildPending = false;
            DisposeBatches();

            if (!Constants.DRAW_GRASS || IsGrassDisabledWorld(_worldIndex) ||
                _graphicsDevice == null || !_contentReady)
            {
                _rebuildPending = !_contentReady && Constants.DRAW_GRASS;
                return;
            }

            if (_data?.HeightMap == null || _data.Mapping.Layer1 == null ||
                _data.Mapping.Alpha == null || GrassTextureIndices.Count == 0)
            {
                return;
            }

            int chunksPerSide = (Constants.TERRAIN_SIZE + ChunkSize - 1) / ChunkSize;
            var quadLists = new List<GrassQuad>[chunksPerSide * chunksPerSide * GrassTextureCount];
            var boundsMin = new Vector3[quadLists.Length];
            var boundsMax = new Vector3[quadLists.Length];
            for (int i = 0; i < quadLists.Length; i++)
            {
                boundsMin[i] = new Vector3(float.MaxValue);
                boundsMax[i] = new Vector3(float.MinValue);
            }

            // SourceMain initializes one random 0/.25/.5/.75 U offset per terrain row
            // when the map is loaded, so rebuilds keep the same pattern.
            if (_rowOffsets == null)
            {
                _rowOffsets = new float[Constants.TERRAIN_SIZE];
                var random = new Random();
                for (int y = 0; y < _rowOffsets.Length; y++)
                    _rowOffsets[y] = random.Next(4) * 0.25f;
            }
            float[] rowOffsets = _rowOffsets;

            int terrainSize = Constants.TERRAIN_SIZE;
            int terrainMask = terrainSize - 1;
            var walls = _data.Attributes?.TerrainWall;
            var mapping = _data.Mapping;

            for (int y = 0; y < terrainSize; y++)
            {
                for (int x = 0; x < terrainSize; x++)
                {
                    int index1 = (y & terrainMask) * terrainSize + (x & terrainMask);
                    if (walls != null && (uint)index1 < (uint)walls.Length &&
                        (walls[index1] & TWFlags.NoGround) != 0)
                    {
                        continue;
                    }

                    int index2 = (y & terrainMask) * terrainSize + ((x + 1) & terrainMask);
                    int index3 = ((y + 1) & terrainMask) * terrainSize + ((x + 1) & terrainMask);
                    int index4 = ((y + 1) & terrainMask) * terrainSize + (x & terrainMask);

                    if (HasTerrainAlpha(mapping.Alpha, index1) ||
                        HasTerrainAlpha(mapping.Alpha, index2) ||
                        HasTerrainAlpha(mapping.Alpha, index3) ||
                        HasTerrainAlpha(mapping.Alpha, index4))
                    {
                        continue;
                    }

                    int textureIndex = mapping.Layer1[index1];
                    if ((uint)textureIndex >= GrassTextureCount ||
                        !GrassTextureIndices.Contains((byte)textureIndex) ||
                        _grassTextures[textureIndex] == null)
                    {
                        continue;
                    }

                    int chunkX = x / ChunkSize;
                    int chunkY = y / ChunkSize;
                    int batchIndex = (chunkY * chunksPerSide + chunkX) * GrassTextureCount + textureIndex;
                    var list = quadLists[batchIndex] ??= new List<GrassQuad>(ChunkSize * ChunkSize / 2);

                    int tufts = Constants.GRASS_TUFTS_PER_TILE;
                    if (tufts <= 1)
                    {
                        float height = GrassWorldHeight;
                        Vector3 bottomLeft = CreateTerrainPosition(x, y, index1);
                        Vector3 bottomRight = CreateTerrainPosition(x + 1, y + 1, index3);
                        float u = x * GrassUvWidth + rowOffsets[y & terrainMask];
                        var quad = new GrassQuad(
                            bottomLeft,
                            bottomRight,
                            GetTerrainLight(index1),
                            GetTerrainLight(index2),
                            GetTerrainLight(index3),
                            GetTerrainLight(index4),
                            index1,
                            index2,
                            u,
                            height);

                        list.Add(quad);
                        ExpandBounds(ref boundsMin[batchIndex], ref boundsMax[batchIndex], quad);
                    }
                    else
                    {
                        AppendTufts(list, ref boundsMin[batchIndex], ref boundsMax[batchIndex],
                                    x, y, index1, index2, index3, index4, tufts);
                    }
                }
            }

            for (int i = 0; i < quadLists.Length; i++)
            {
                if (quadLists[i] == null || quadLists[i].Count == 0)
                    continue;

                int textureIndex = i % GrassTextureCount;
                _batches.Add(new GrassBatch(
                    _graphicsDevice,
                    _grassTextures[textureIndex],
                    quadLists[i],
                    new BoundingBox(boundsMin[i], boundsMax[i])));
            }
        }

        public void Draw()
        {
            if (_rebuildPending && _contentReady)
                BuildAllGrass();

            if (!Constants.DRAW_GRASS || IsGrassDisabledWorld(_worldIndex) ||
                _graphicsDevice == null || _batches.Count == 0 || Camera.Instance == null)
            {
                return;
            }

            AlphaTestEffect alphaEffect = GraphicsManager.Instance?.AlphaTestEffect3D;
            if (alphaEffect == null)
                return;

            bool additive = IsSpecialBlendMap(_worldIndex);
            BasicEffect additiveEffect = additive ? EnsureAdditiveEffect() : null;
            Effect effect = additive ? additiveEffect : alphaEffect;
            if (effect == null)
                return;

            BlendState previousBlend = _graphicsDevice.BlendState;
            DepthStencilState previousDepth = _graphicsDevice.DepthStencilState;
            RasterizerState previousRasterizer = _graphicsDevice.RasterizerState;
            SamplerState previousSampler = _graphicsDevice.SamplerStates[0];
            int previousReferenceAlpha = alphaEffect.ReferenceAlpha;
            CompareFunction previousAlphaFunction = alphaEffect.AlphaFunction;

            try
            {
                _graphicsDevice.RasterizerState = RasterizerState.CullNone;
                _graphicsDevice.SamplerStates[0] = GraphicsManager.GetQualityLinearWrapSamplerState();
                _graphicsDevice.BlendState = additive ? BlendState.Additive : BlendState.NonPremultiplied;
                _graphicsDevice.DepthStencilState = additive
                    ? DepthStencilState.DepthRead
                    : DepthStencilState.Default;

                ConfigureEffect(effect, additive ? additiveEffect : null);
                if (!additive)
                {
                    alphaEffect.AlphaFunction = CompareFunction.Greater;
                    alphaEffect.ReferenceAlpha = (int)(255f * Constants.GRASS_ALPHA_REFERENCE);
                }

                BoundingFrustum frustum = Camera.Instance.Frustum;
                int windVersion = _wind.Version;
                Vector3 cameraPosition = Camera.Instance.Position;
                float maxDistance = Constants.GRASS_DRAW_DISTANCE;
                float maxDistanceSquared = maxDistance > 0f ? maxDistance * maxDistance : 0f;
                for (int i = 0; i < _batches.Count; i++)
                {
                    GrassBatch batch = _batches[i];
                    if (frustum.Contains(batch.Bounds) == ContainmentType.Disjoint)
                        continue;

                    // 距離剔除。原版只有視錐剔除 —— 視野最遠處那些每片不到一個像素的草，
                    // 照樣要跑完整的頂點處理，還要在每次風力更新時被回寫一遍。
                    // 密度拉高之後這是主要成本，手機更明顯。
                    if (maxDistanceSquared > 0f
                        && Vector3.DistanceSquared(cameraPosition, GetCenter(batch.Bounds)) > maxDistanceSquared)
                    {
                        continue;
                    }

                    if (batch.LastWindVersion != windVersion)
                    {
                        UpdateBatchWind(batch);
                        batch.LastWindVersion = windVersion;
                    }

                    _graphicsDevice.SetVertexBuffer(batch.VertexBuffer);
                    if (additive)
                        additiveEffect.Texture = batch.Texture;
                    else
                        alphaEffect.Texture = batch.Texture;

                    foreach (EffectPass pass in effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        _graphicsDevice.DrawPrimitives(
                            PrimitiveType.TriangleList,
                            0,
                            batch.Vertices.Length / 3);
                    }

                    Flushes++;
                    DrawnTriangles += batch.Vertices.Length / 3;
                }
            }
            finally
            {
                _graphicsDevice.SetVertexBuffer(null);
                _graphicsDevice.BlendState = previousBlend;
                _graphicsDevice.DepthStencilState = previousDepth;
                _graphicsDevice.RasterizerState = previousRasterizer;
                _graphicsDevice.SamplerStates[0] = previousSampler;
                alphaEffect.ReferenceAlpha = previousReferenceAlpha;
                alphaEffect.AlphaFunction = previousAlphaFunction;
            }
        }

        private void ConfigureEffect(Effect effect, BasicEffect additiveEffect)
        {
            Matrix view = Camera.Instance.View;
            Matrix projection = Camera.Instance.Projection;

            if (additiveEffect != null)
            {
                additiveEffect.World = Matrix.Identity;
                additiveEffect.View = view;
                additiveEffect.Projection = projection;
                additiveEffect.TextureEnabled = true;
                additiveEffect.VertexColorEnabled = true;
                additiveEffect.LightingEnabled = false;
                additiveEffect.DiffuseColor = Vector3.One;
                additiveEffect.Alpha = 1f;
                return;
            }

            var alphaEffect = (AlphaTestEffect)effect;
            alphaEffect.World = Matrix.Identity;
            alphaEffect.View = view;
            alphaEffect.Projection = projection;
            alphaEffect.VertexColorEnabled = true;
            alphaEffect.DiffuseColor = Vector3.One;
            alphaEffect.Alpha = 1f;
        }

        private BasicEffect EnsureAdditiveEffect()
        {
            if (_additiveEffect != null && !_additiveEffect.IsDisposed)
                return _additiveEffect;

            _additiveEffect = new BasicEffect(_graphicsDevice)
            {
                TextureEnabled = true,
                VertexColorEnabled = true,
                LightingEnabled = false,
                World = Matrix.Identity
            };
            return _additiveEffect;
        }

        private void UpdateBatchWind(GrassBatch batch)
        {
            for (int i = 0; i < batch.Quads.Length; i++)
            {
                GrassQuad quad = batch.Quads[i];
                int vertex = i * 6;
                float leftY = quad.BottomLeft.Y + _wind.GetWindValue(quad.WindIndex1);
                float rightY = quad.BottomRight.Y + _wind.GetWindValue(quad.WindIndex2);

                Vector3 topLeft = batch.Vertices[vertex + 0].Position;
                topLeft.Y = leftY;
                batch.Vertices[vertex + 0].Position = topLeft;
                batch.Vertices[vertex + 5].Position = topLeft;

                Vector3 topRight = batch.Vertices[vertex + 1].Position;
                topRight.Y = rightY;
                batch.Vertices[vertex + 1].Position = topRight;
            }

            batch.VertexBuffer.SetData(batch.Vertices, 0, batch.Vertices.Length, SetDataOptions.Discard);
        }

        /// <summary>
        /// 一格長 <paramref name="tufts"/> 叢草，取代原版的「一格一張立牌」。
        /// </summary>
        /// <remarks>
        /// 三件事讓它不再像一排整齊的柵欄：
        /// 格內隨機位置、隨機朝向（原版全部沿對角線）、高度與寬度各自抖動。
        /// 再把底部兩個頂點壓暗當假 AO —— 沒有這個，草看起來是「插在地上」而不是「長在地上」。
        ///
        /// 隨機數用座標雜湊而不是 <see cref="Random"/>：重建（切換密度、切地圖回來）
        /// 必須長出一模一樣的草，否則畫面會閃。
        /// </remarks>
        private void AppendTufts(
            List<GrassQuad> list,
            ref Vector3 boundsMin,
            ref Vector3 boundsMax,
            int x,
            int y,
            int index1,
            int index2,
            int index3,
            int index4,
            int tufts)
        {
            float h1 = SampleHeight(index1);
            float h2 = SampleHeight(index2);
            float h3 = SampleHeight(index3);
            float h4 = SampleHeight(index4);

            Color light1 = GetTerrainLight(index1);
            Color light2 = GetTerrainLight(index2);
            Color light3 = GetTerrainLight(index3);
            Color light4 = GetTerrainLight(index4);

            float scale = Constants.TERRAIN_SCALE;
            float baseX = x * scale;
            float baseY = y * scale;

            // 幾片立牌共用一個圓心。planes 只改變分組方式，立牌總數不變 ——
            // 所以三角形數與 planes 無關。
            int planes = Math.Clamp(Constants.GRASS_CLUSTER_PLANES, 1, 4);
            if (planes > tufts)
                planes = tufts;

            // 每格的立牌數整叢地上下浮動 —— 三種情形機率相同，所以**平均等於 tufts**，
            // 效能估算不受影響，但草地不再是「每一格都一樣多」。
            //
            // 以整叢為單位加減（不是加減一片），交叉的分組才不會被打散。
            // 固定叢數是「一組一組」的來源之一：格子是 100 單位的規則網格，
            // 每格塞同樣多的草，眼睛就會把一格讀成一個單位。
            float countRoll = Hash01(x, y, 97);
            int total = tufts + (countRoll < 0.33f ? -planes : countRoll > 0.66f ? planes : 0);
            total = Math.Max(planes, total);

            for (int i = 0; i < total; i++)
            {
                // 同一叢的幾片共用位置與尺寸的亂數，只有角度不同。
                int cluster = i / planes;
                int plane = i % planes;

                float r0 = Hash01(x, y, cluster * 8 + 0);
                float r1 = Hash01(x, y, cluster * 8 + 1);
                float r2 = Hash01(x, y, cluster * 8 + 2);
                float r3 = Hash01(x, y, cluster * 8 + 3);
                float r4 = Hash01(x, y, cluster * 8 + 4);
                float r5 = Hash01(x, y, i * 8 + 5);

                // 格內位置用滿 [0, 1]，不留邊界。
                //
                // 先前是 0.1 + r * 0.8，也就是每格留 10% 的邊界。量過的後果：
                // 格內位置直方圖的頭尾兩段完全是 0 —— 每 100 世界單位就有一條
                // 20 單位寬、一株草都沒有的空帶，橫豎都有。那就是「一行一行」。
                //
                // 試過讓草叢溢出格子（-0.15 到 1.15）來打散網格，結果更糟：
                // 邊界帶會同時收到兩格的草，空帶變成**密帶**（實測邊緣 15-16% vs 中間 7%）。
                // 均勻取滿整格才是對的 —— 中心落在格線上本來就沒有問題。
                float fx = r0;
                float fy = r1;
                float sx = fx;
                float sy = fy;

                // 一叢之內平均分掉 180 度：兩片＝十字，三片＝三角。
                // 立牌沒有正反面（CullNone），所以分 180 度而不是 360 度。
                float angle = r2 * MathHelper.TwoPi + plane * (MathHelper.Pi / planes);
                float halfWidth = scale * (0.55f + r3 * 0.35f);
                float height = GrassWorldHeight * (0.75f + r4 * 0.5f);

                float dx = MathF.Cos(angle) * halfWidth;
                float dy = MathF.Sin(angle) * halfWidth;

                float cx = baseX + fx * scale;
                float cy = baseY + fy * scale;
                float cz = Bilinear(h1, h2, h3, h4, sx, sy);

                var bottomLeft = new Vector3(cx - dx, cy - dy, cz);
                var bottomRight = new Vector3(cx + dx, cy + dy, cz);

                // 一張圖橫排四株，隨機挑一株，不然整片草會是同一個剪影。
                float u = (int)(r5 * 4f) % 4 * GrassUvWidth;

                Color top = Bilinear(light1, light2, light3, light4, sx, sy);
                Color bottom = Darken(top, GrassRootShade);

                // 風的取樣點跟著草叢的位置走，不是整格共用 index1／index2。
                // 整格共用的話一格裡的草會**完全同步搖動**，
                // 於是一格 100 單位就成為一個看得出來的運動單位 ——
                // 這是「一組一組」在動態上的來源，靜態截圖看不出來。
                int windA = sx < 0.5f ? (sy < 0.5f ? index1 : index4) : (sy < 0.5f ? index2 : index3);
                int windB = sx < 0.5f ? (sy < 0.5f ? index2 : index3) : (sy < 0.5f ? index4 : index1);

                var quad = new GrassQuad(
                    bottomLeft,
                    bottomRight,
                    top,
                    top,
                    bottom,
                    bottom,
                    windA,
                    windB,
                    u,
                    height);

                list.Add(quad);
                ExpandBounds(ref boundsMin, ref boundsMax, quad);
            }
        }

        private static Vector3 GetCenter(in BoundingBox box) => (box.Min + box.Max) * 0.5f;

        private float SampleHeight(int sampleIndex)
        {
            float z = 0f;
            if (_data.HeightMap != null && (uint)sampleIndex < (uint)_data.HeightMap.Length)
                z = _data.HeightMap[sampleIndex].R * 1.5f;

            var walls = _data.Attributes?.TerrainWall;
            if (walls != null && (uint)sampleIndex < (uint)walls.Length &&
                (walls[sampleIndex] & TWFlags.Height) != 0)
            {
                z += SpecialHeight;
            }

            return z;
        }

        private static float Bilinear(float v1, float v2, float v3, float v4, float fx, float fy)
        {
            float bottom = v1 + (v2 - v1) * fx;   // (0,0) -> (1,0)
            float top = v4 + (v3 - v4) * fx;      // (0,1) -> (1,1)
            return bottom + (top - bottom) * fy;
        }

        private static Color Bilinear(Color c1, Color c2, Color c3, Color c4, float fx, float fy)
            => new(
                (int)Bilinear(c1.R, c2.R, c3.R, c4.R, fx, fy),
                (int)Bilinear(c1.G, c2.G, c3.G, c4.G, fx, fy),
                (int)Bilinear(c1.B, c2.B, c3.B, c4.B, fx, fy));

        private static Color Darken(Color color, float factor)
            => new((int)(color.R * factor), (int)(color.G * factor), (int)(color.B * factor));

        /// <summary>座標雜湊，回傳 [0, 1)。同樣的輸入永遠得到同樣的值。</summary>
        private static float Hash01(int x, int y, int salt)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + salt * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }

        private Vector3 CreateTerrainPosition(int tileX, int tileY, int sampleIndex)
        {
            float z = 0f;
            if (_data.HeightMap != null && (uint)sampleIndex < (uint)_data.HeightMap.Length)
                z = _data.HeightMap[sampleIndex].R * 1.5f;

            var walls = _data.Attributes?.TerrainWall;
            if (walls != null && (uint)sampleIndex < (uint)walls.Length &&
                (walls[sampleIndex] & TWFlags.Height) != 0)
            {
                z += SpecialHeight;
            }

            return new Vector3(
                tileX * Constants.TERRAIN_SCALE,
                tileY * Constants.TERRAIN_SCALE,
                z);
        }

        private Color GetTerrainLight(int index)
        {
            if (_data.FinalLightMap == null || (uint)index >= (uint)_data.FinalLightMap.Length)
                return Color.White;

            Color source = _data.FinalLightMap[index];
            float ambient = AmbientLight * 255f;
            float brightness = float.IsFinite(GrassBrightness) ? MathF.Max(0f, GrassBrightness) : 1f;
            return new Color(
                ClampColor((source.R + ambient) * brightness),
                ClampColor((source.G + ambient) * brightness),
                ClampColor((source.B + ambient) * brightness),
                (byte)255);
        }

        private static byte ClampColor(float value)
        {
            return (byte)Math.Clamp((int)value, 0, 255);
        }

        private static bool HasTerrainAlpha(byte[] alpha, int index)
        {
            return alpha != null && (uint)index < (uint)alpha.Length && alpha[index] > 0;
        }

        private static void ExpandBounds(ref Vector3 min, ref Vector3 max, GrassQuad quad)
        {
            Vector3 quadMin = Vector3.Min(quad.BottomLeft, quad.BottomRight);
            Vector3 quadMax = Vector3.Max(quad.BottomLeft, quad.BottomRight);
            quadMin.X -= 100f;
            quadMin.Y -= 80f;
            quadMin.Z -= 1f;
            quadMax.X += 100f;
            quadMax.Y += 80f;
            quadMax.Z += quad.Height;
            min = Vector3.Min(min, quadMin);
            max = Vector3.Max(max, quadMax);
        }

        private static bool IsSpecialBlendMap(short worldIndex)
        {
            return worldIndex == 63 || worldIndex == 66;
        }

        private static bool IsGrassDisabledWorld(short worldIndex)
        {
            return worldIndex == 7 || worldIndex == 67 ||
                   (worldIndex >= 11 && worldIndex <= 17) || worldIndex == 52;
        }

        private void DisposeBatches()
        {
            for (int i = 0; i < _batches.Count; i++)
                _batches[i].Dispose();
            _batches.Clear();
        }

        public void Dispose()
        {
            DisposeBatches();
            _additiveEffect?.Dispose();
            _additiveEffect = null;
        }
    }
}
