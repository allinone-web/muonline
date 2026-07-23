using Client.Data.ATT;
using Client.Data.MAP;
using Client.Main.Controls.Terrain;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Client.Main.Controls
{
    /// <summary>
    /// Manages and coordinates all terrain-related systems, acting as a facade
    /// for loading, rendering, and querying terrain information.
    /// </summary>
    public class TerrainControl : GameControl
    {
        // --- Sub-systems ---
        private TerrainData _data;
        private TerrainLoader _loader;
        private TerrainPhysics _physics;
        private TerrainLightManager _lightManager;
        private WindSimulator _wind;
        private TerrainVisibilityManager _visibility;
        private TerrainRenderer _renderer;
        private GrassRenderer _grassRenderer;

        // --- Public Properties (Facades) ---
        public short WorldIndex { get; set; }
        // Use the opposite of SUN_DIRECTION because the lightmap expects a vector pointing toward the sun.
        public Vector3 LightDirection { get; set; } = Vector3.Normalize(-SunCycleManager.BaseSunDirection);
        public IReadOnlyList<DynamicLight> DynamicLights => _lightManager.DynamicLights;
        public IReadOnlyList<DynamicLightSnapshot> ActiveLights => _lightManager.ActiveLights;
        public IReadOnlyList<DynamicLightSnapshot> VisibleLights => _lightManager.VisibleLights;
        public int ActiveLightsVersion => _lightManager.ActiveLightsVersion;
        public int VisibleLightsVersion => _lightManager.VisibleLightsVersion;
        public int DynamicLightsOrphansPruned => _lightManager?.OrphanLightsPrunedCount ?? 0;
        public int DynamicLightsDuplicateAddsRejected => _lightManager?.DuplicateAddsRejectedCount ?? 0;
        public int LastFrameRegisteredDynamicLights => _lightManager?.LastFrameRegisteredCount ?? 0;
        public int LastFrameActiveDynamicLights => _lightManager?.LastFrameActiveCount ?? 0;
        public int LastFrameVisibleDynamicLights => _lightManager?.LastFrameVisibleCount ?? 0;
        public int LastUploadedDynamicLights => _renderer?.LastUploadedDynamicLights ?? 0;
        public Texture2D HeightMapTexture => _data?.HeightMapTexture;
        private Dictionary<int, string> _pendingTextureMap = new();
        private bool _replaceTextureMapping;
        private bool _preferIndexBatching;

        public Dictionary<int, string> TextureMappingFiles
        {
            get => _data != null ? _data.TextureMappingFiles : _pendingTextureMap;
            set
            {
                _replaceTextureMapping = true;
                if (_data != null)
                    _data.TextureMappingFiles = value ?? new Dictionary<int, string>();
                else
                    _pendingTextureMap = value ?? new Dictionary<int, string>();
            }
        }

        public float WaterSpeed { get => _renderer.WaterSpeed; set => _renderer.WaterSpeed = value; }
        public float DistortionAmplitude { get => _renderer.DistortionAmplitude; set => _renderer.DistortionAmplitude = value; }
        public float DistortionFrequency { get => _renderer.DistortionFrequency; set => _renderer.DistortionFrequency = value; }
        public float AmbientLight { get => _renderer.AmbientLight; set => _renderer.AmbientLight = value; }
        public float GrassBrightness { get => _grassRenderer.GrassBrightness; set => _grassRenderer.GrassBrightness = value; }
        public Vector2 WaterFlowDirection { get => _renderer.WaterFlowDirection; set => _renderer.WaterFlowDirection = value; }
        public HashSet<byte> GrassTextureIndices => _grassRenderer.GrassTextureIndices;
        public bool IsGpuTerrainLighting => _renderer?.IsGpuLightingActive == true;
        public bool IsDynamicLightingShaderAvailable => _renderer?.IsDynamicLightingShaderAvailable == true;
        public bool PreferIndexBatching
        {
            get => _renderer?.PreferIndexBatching ?? _preferIndexBatching;
            set
            {
                _preferIndexBatching = value;
                if (_renderer != null)
                    _renderer.PreferIndexBatching = value;
            }
        }

        /// <summary>
        /// Configures grass settings for specific world requirements.
        /// This should be called in world's AfterLoad() method.
        /// </summary>
        /// <param name="brightness">Grass brightness multiplier (default: 1.0f)</param>
        /// <param name="textureIndices">Valid texture indices for grass rendering (default: {0})</param>
        public void ConfigureGrass(float brightness = 1f, params byte[] textureIndices)
        {
            var oldBrightness = _grassRenderer.GrassBrightness;
            _grassRenderer.GrassBrightness = brightness;

            if (textureIndices != null && textureIndices.Length > 0)
            {
                _grassRenderer.GrassTextureIndices.Clear();
                foreach (var index in textureIndices)
                {
                    _grassRenderer.GrassTextureIndices.Add(index);
                }
            }

            _grassRenderer.BuildAllGrass();

            Console.WriteLine($"[TerrainControl] ConfigureGrass for World{WorldIndex} - Brightness: {oldBrightness:F2} → {brightness:F2}, TextureIndices: [{string.Join(", ", textureIndices ?? new byte[] { 0 })}]");
        }

        /// <summary>
        /// Ensures grass textures are loaded when DRAW_GRASS setting is toggled on.
        /// Call this after changing Constants.DRAW_GRASS from false to true.
        /// </summary>
        public void ReloadGrassIfNeeded()
        {
            _grassRenderer?.EnsureContentLoaded(WorldIndex);
            _grassRenderer?.BuildAllGrass();
        }
        public TerrainFrameMetrics FrameMetrics { get; } = new TerrainFrameMetrics();

        public TerrainControl()
        {
            AutoViewSize = false;
            ViewSize = new Point(MuGame.Instance.Width, MuGame.Instance.Height);
        }

        public override async Task Load()
        {
            _loader = new TerrainLoader(WorldIndex);

            if (_pendingTextureMap.Count > 0 || _replaceTextureMapping)
            {
                _loader.SetTextureMapping(_pendingTextureMap, _replaceTextureMapping);
            }

            _data = await _loader.LoadAsync();
            _pendingTextureMap.Clear();
            _replaceTextureMapping = false;

            if (_data == null)
            {
                Status = Models.GameControlStatus.Error;
                return;
            }

            // Initialize sub-systems in order of dependency
            _lightManager = new TerrainLightManager(_data, this);
            _physics = new TerrainPhysics(_data, _lightManager);
            _wind = new WindSimulator(_data);
            _visibility = new TerrainVisibilityManager(_data);
            _grassRenderer = new GrassRenderer(GraphicsDevice, _data, _physics, _wind, _lightManager);
            _renderer = new TerrainRenderer(GraphicsDevice, _data, _visibility, _lightManager, _grassRenderer)
            {
                WorldIndex = this.WorldIndex,
                PreferIndexBatching = _preferIndexBatching
            };

            // Post-load processing
            _lightManager.CreateTerrainNormals();
            _lightManager.CreateFinalLightmap(LightDirection);
            _renderer.CreateHeightMapTexture();

            // Reset grass to defaults before loading world-specific content
            _grassRenderer.LoadContent(WorldIndex);
            _grassRenderer.BuildAllGrass();

            Camera.Instance.AspectRatio = GraphicsDevice.Viewport.AspectRatio;

            await base.Load();
        }

        public override void Update(GameTime time)
        {
            base.Update(time);
            if (Status != Models.GameControlStatus.Ready || _visibility == null)
                return;

            var camPos2D = new Vector2(Camera.Instance.Position.X, Camera.Instance.Position.Y);

            _visibility.Update(camPos2D);
            _lightManager.UpdateActiveLights((float)time.ElapsedGameTime.TotalSeconds);

            // Grass vertices are static and their motion is generated by Grass.fx from
            // the global time value. Updating the legacy per-tile CPU wind field here
            // performed thousands of writes every ~32 ms without affecting rendering.
            _renderer.Update(time);
        }

        internal void PrepareInitialRenderResources()
        {
            if (Status != Models.GameControlStatus.Ready || _visibility == null || _renderer == null)
                return;

            var camera = Camera.Instance;
            _visibility.Update(new Vector2(camera.Position.X, camera.Position.Y));
            _renderer.PrepareFirstDrawResources();
        }

        internal async Task PrepareInitialRenderResourcesAsync(string phaseName)
        {
            if (Status != Models.GameControlStatus.Ready || _visibility == null || _renderer == null)
                return;

            var camera = Camera.Instance;
            _visibility.Update(new Vector2(camera.Position.X, camera.Position.Y));
            await _renderer.PrepareFirstDrawResourcesAsync(phaseName);
        }

        public override void Draw(GameTime time)
        {
            if (!Visible || Status != Models.GameControlStatus.Ready || _renderer == null || _grassRenderer == null)
                return;

            FrameMetrics.Reset();
            var terrainStarted = RenderPassProfiler.Start();
            _renderer.Draw(after: false);
            RenderPassProfiler.AddTerrainOpaque(terrainStarted);

            // Aggregate metrics from renderers
            FrameMetrics.DrawCalls = _renderer.DrawCalls + _grassRenderer.Flushes; // Grass flush is a draw call
            FrameMetrics.DrawnTriangles = _renderer.DrawnTriangles + _grassRenderer.DrawnTriangles;
            FrameMetrics.DrawnBlocks = _renderer.DrawnBlocks;
            FrameMetrics.DrawnCells = _renderer.DrawnCells;
            FrameMetrics.GrassFlushes = _grassRenderer.Flushes;
            FrameMetrics.IndexedCells = _renderer.IndexedCells;
            FrameMetrics.StreamedCells = _renderer.StreamedCells;
            FrameMetrics.IndexUploads = _renderer.IndexUploads;
            FrameMetrics.VertexUploads = _renderer.VertexUploads;
            FrameMetrics.UploadedIndices = _renderer.UploadedIndices;
            FrameMetrics.UploadedVertices = _renderer.UploadedVertices;
            FrameMetrics.UsedIndexBatching = _renderer.UsedIndexBatching;

            base.Draw(time);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible || Status != Models.GameControlStatus.Ready || _renderer == null || _grassRenderer == null)
                return;

            var terrainAfterStarted = RenderPassProfiler.Start();
            _renderer.Draw(after: true);
            RenderPassProfiler.AddTerrainAfter(terrainAfterStarted);
            base.DrawAfter(gameTime);
        }


        // --- Public Query Methods (Facade) ---
        public int GetHeroTile(float xf, float yf) => _data.Mapping.Layer1[TerrainPhysics.GetTerrainIndex(xf, yf)];
        public TWFlags RequestTerrainFlag(int x, int y) => _physics?.RequestTerrainFlag(x, y) ?? 0f;
        public float RequestTerrainHeight(float xf, float yf) => _physics?.RequestTerrainHeight(xf, yf) ?? 0f;
        public Vector3 EvaluateTerrainLight(float xf, float yf) => _physics?.RequestTerrainLight(xf, yf, AmbientLight) ?? Vector3.Zero;
        public Vector3 EvaluateDynamicLight(Vector2 position) => _lightManager.EvaluateDynamicLight(position);
        public byte GetBaseTextureIndexAt(int x, int y) => _physics.GetBaseTextureIndexAt(x, y);
        public float GetWindValue(int x, int y) => _wind.GetWindValue(x, y);

        /// <summary>
        /// Renders the terrain into the shared shadow map.
        /// </summary>
        public void RenderShadowMap(Effect shadowEffect, Matrix lightViewProjection)
        {
            _renderer?.DrawShadowMap(shadowEffect, lightViewProjection);
        }

        // --- Light Management (Facade) ---
        public void AddDynamicLight(DynamicLight light) => _lightManager?.AddDynamicLight(light);
        public void RemoveDynamicLight(DynamicLight light) => _lightManager?.RemoveDynamicLight(light);
        public void RemoveDynamicLightsByOwner(WorldObject owner) => _lightManager?.RemoveDynamicLightsByOwner(owner);

        public override void Dispose()
        {
            _renderer?.Dispose();
            _grassRenderer?.Dispose();
            base.Dispose();
            _data = null; // Allow GC to collect all data
            GC.SuppressFinalize(this);
        }
    }
}
