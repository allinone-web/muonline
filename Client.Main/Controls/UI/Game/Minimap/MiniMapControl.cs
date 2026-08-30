using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Client.Data.ATT;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Objects.Player;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game
{
    public sealed class MiniMapControl : UIControl
    {
        private enum ProceduralMarkerKind : byte
        {
            Npc,
            Monster,
            Player,
            Portal
        }

        // 手機的地圖視窗和其他視窗一樣：面板 + 標題列 + 左上角關閉鈕。
        //
        // 原本它是一張「浮在畫面上的地圖」，沒有面板、沒有標題、也沒有關閉鈕 ——
        // 只能再按一次右上角的 MAP 才關得掉。其他六個視窗都有關閉鈕，
        // 只有這一個沒有，玩家會先在左上角找，找不到才想到要按 MAP。
        private static bool Mobile => MobileUi.IsMobile;

        private const int MapSize = 520;
        private const int MapDisplayHeight = 455;
        private const int MobileMapPadding = 16;

        private static int MapLeft => Mobile ? MobileMapPadding : 20;
        private static int MapTop => Mobile ? MobileUi.WindowTitleHeight + 8 : 10;
        private static int WindowWidth => Mobile ? MapSize + MobileMapPadding * 2 : 560;
        private static int WindowHeight => Mobile ? MapTop + MapDisplayHeight + MobileMapPadding : 475;
        private const int EdgeMaskSize = 128;
        private const int TerrainSize = 256;
        private const int TerrainTexturePadding = 128;
        private const int TerrainTextureSize = TerrainSize + TerrainTexturePadding * 2;
        private const float ContourHeightStep = 60f;
        private const float ReliefHeightRange = 450f;
        private const int ReliefLayerCount = 4;
        private const float ReliefLayerOffset = 3f;
        private const int MarkerRecordCount = 100;
        private const int MarkerRecordSize = 113;
        private const int LegacyHeaderSize = 45;
        private const float MapRotation = MathHelper.Pi / 4f - MathHelper.PiOver2;
        private const float MapCoverageScale = 1.41421356f;
        private const float InitialZoom = 800f;
        private const float MinZoom = 800f;
        private const float MaxZoom = 1800f;
        private const float ZoomStep = 200f;
        private const float MarkerHoverRadius = 10f;

        private static readonly byte[] BuxCode = { 0xFC, 0xCF, 0xAB };
        private static readonly Color PortalMarkerColor = new(190, 120, 255);
        private static readonly Matrix MapRotationMatrix = Matrix.CreateRotationZ(MapRotation);
        private static readonly Vector3 ReliefLightDirection = Vector3.Normalize(new Vector3(-0.55f, -0.45f, 0.75f));

        private static class OverlayTheme
        {
            public static readonly Color MapTint = new(184, 218, 230);
            public static readonly Color Npc = new(102, 210, 235);
            public static readonly Color Monster = new(236, 78, 78);
            public static readonly Color Player = new(92, 224, 142);
            public static readonly Color Hero = new(255, 211, 112);
            public static readonly Color Caption = new(220, 226, 230);
            public static readonly Color Text = new(240, 244, 246);
            public static readonly Color Dark = new(12, 18, 24);
        }

        private readonly GameScene _gameScene;
        private readonly List<MiniMapMarker> _markers = new();
        private readonly List<(Vector2 Position, string Text)> _hoverTargets = new();
        private readonly BlendState _edgeMaskBlendState = new()
        {
            ColorSourceBlend = Blend.Zero,
            ColorDestinationBlend = Blend.SourceAlpha,
            ColorBlendFunction = BlendFunction.Add,
            AlphaSourceBlend = Blend.Zero,
            AlphaDestinationBlend = Blend.SourceAlpha,
            AlphaBlendFunction = BlendFunction.Add
        };

        private Texture2D _terrainMapTexture;
        private Texture2D _terrainReliefTexture;
        private Texture2D _edgeMaskTexture;
        private RenderTarget2D _mapSurface;
        private Color[] _terrainMapPixels;
        private Color[] _terrainReliefPixels;
        private bool _terrainTextureDirty;
        private SpriteFont _font;
        private float _zoom = InitialZoom;
        private int _loadGeneration;
        private string _tooltipText;
        private Vector2 _tooltipPosition;
        private string _worldName;

        public MiniMapControl(GameScene scene)
        {
            _gameScene = scene ?? throw new ArgumentNullException(nameof(scene));

            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
            Offset = new Point(0, -55);
            AutoViewSize = false;
            ControlSize = new Point(WindowWidth, WindowHeight);
            ViewSize = ControlSize;
            Interactive = false;
            Visible = false;
        }

        public override async Task Load()
        {
            await base.Load();
            _font = GraphicsManager.Instance.Font;
        }

        public async Task LoadContentForWorld(short worldIndex)
        {
            int generation = ++_loadGeneration;
            string worldName = _gameScene.World?.Name;

            if (string.IsNullOrWhiteSpace(worldName))
            {
                worldName = MapDatabase.GetMapName((ushort)Math.Max(0, worldIndex - 1));
            }

            Color[] terrainMapPixels = BuildTerrainMapPixels(
                _gameScene.World?.Terrain,
                out Color[] terrainReliefPixels);
            List<MiniMapMarker> markers = await LoadMarkersAsync(worldName);

            if (generation != _loadGeneration)
            {
                return;
            }

            _terrainMapPixels = terrainMapPixels;
            _terrainReliefPixels = terrainReliefPixels;
            _terrainTextureDirty = true;
            _worldName = worldName;
            _markers.Clear();
            _markers.AddRange(markers);
            _hoverTargets.Clear();
            _zoom = InitialZoom;
        }

        private static Color[] BuildTerrainMapPixels(
            TerrainControl terrain,
            out Color[] reliefPixels)
        {
            reliefPixels = null;
            if (terrain == null)
            {
                return null;
            }

            var flags = new TWFlags[TerrainSize * TerrainSize];
            var heights = new float[flags.Length];
            float minimumHeight = float.MaxValue;
            float maximumHeight = float.MinValue;

            for (int y = 0; y < TerrainSize; y++)
            {
                for (int x = 0; x < TerrainSize; x++)
                {
                    int index = y * TerrainSize + x;
                    TWFlags tileFlags = terrain.RequestTerrainFlag(x, y);
                    float height = terrain.RequestTerrainHeight(
                        x * Constants.TERRAIN_SCALE,
                        y * Constants.TERRAIN_SCALE);

                    flags[index] = tileFlags;
                    heights[index] = height;

                    if (!tileFlags.HasFlag(TWFlags.NoGround))
                    {
                        minimumHeight = MathF.Min(minimumHeight, height);
                        maximumHeight = MathF.Max(maximumHeight, height);
                    }
                }
            }

            if (minimumHeight == float.MaxValue || maximumHeight == float.MinValue)
            {
                minimumHeight = 0f;
                maximumHeight = 1f;
            }

            float heightRange = MathHelper.Clamp(
                maximumHeight - minimumHeight,
                1f,
                ReliefHeightRange);
            var pixels = new Color[TerrainTextureSize * TerrainTextureSize];
            reliefPixels = new Color[pixels.Length];
            for (int y = 0; y < TerrainSize; y++)
            {
                for (int x = 0; x < TerrainSize; x++)
                {
                    int index = y * TerrainSize + x;
                    TWFlags tileFlags = flags[index];
                    int terrainClass = GetTerrainClass(tileFlags);
                    bool hardEdge = HasDifferentNeighbour(flags, x, y, terrainClass, 1);
                    bool softEdge = !hardEdge && HasDifferentNeighbour(flags, x, y, terrainClass, 2);
                    bool contour = terrainClass != 0 && IsHeightContour(heights, x, y);
                    float normalizedHeight = MathHelper.Clamp(
                        (heights[index] - minimumHeight) / heightRange,
                        0f,
                        1f);
                    float reliefLight = GetReliefLight(heights, x, y);
                    int noise = ((x * 37) ^ (y * 19)) & 7;

                    Color color = GetTerrainColor(
                        tileFlags,
                        terrainClass,
                        hardEdge,
                        softEdge,
                        contour,
                        normalizedHeight,
                        reliefLight,
                        noise);
                    int textureX = y + TerrainTexturePadding;
                    int textureY = x + TerrainTexturePadding;
                    int textureIndex = textureY * TerrainTextureSize + textureX;
                    pixels[textureIndex] = color;

                    if (terrainClass != 0)
                    {
                        int reliefAlpha = (int)MathF.Round(MathHelper.Lerp(
                            8f,
                            190f,
                            MathF.Pow(normalizedHeight, 0.7f)));
                        reliefPixels[textureIndex] = Color.FromNonPremultiplied(
                            18,
                            34,
                            48,
                            reliefAlpha);
                    }
                }
            }

            return pixels;
        }

        private static float GetReliefLight(float[] heights, int tileX, int tileY)
        {
            float left = GetTerrainHeight(heights, tileX - 1, tileY);
            float right = GetTerrainHeight(heights, tileX + 1, tileY);
            float top = GetTerrainHeight(heights, tileX, tileY - 1);
            float bottom = GetTerrainHeight(heights, tileX, tileY + 1);
            const float reliefSlopeScale = 35f;
            var normal = Vector3.Normalize(new Vector3(
                -(right - left) / reliefSlopeScale,
                -(bottom - top) / reliefSlopeScale,
                1f));
            float diffuse = MathF.Max(0f, Vector3.Dot(normal, ReliefLightDirection));
            float lighting = 0.12f + diffuse * 1.05f;
            return MathHelper.Clamp(lighting, 0f, 1f);
        }

        private static bool IsHeightContour(float[] heights, int tileX, int tileY)
        {
            int currentBand = (int)MathF.Floor(GetTerrainHeight(heights, tileX, tileY) / ContourHeightStep);
            int rightBand = (int)MathF.Floor(GetTerrainHeight(heights, tileX + 1, tileY) / ContourHeightStep);
            int bottomBand = (int)MathF.Floor(GetTerrainHeight(heights, tileX, tileY + 1) / ContourHeightStep);
            return currentBand != rightBand || currentBand != bottomBand;
        }

        private static float GetTerrainHeight(float[] heights, int tileX, int tileY)
        {
            int x = Math.Clamp(tileX, 0, TerrainSize - 1);
            int y = Math.Clamp(tileY, 0, TerrainSize - 1);
            return heights[y * TerrainSize + x];
        }

        private static int GetTerrainClass(TWFlags flags)
        {
            if (flags.HasFlag(TWFlags.NoGround)) return 0;
            if (flags.HasFlag(TWFlags.Height)) return 1;
            if (flags.HasFlag(TWFlags.NoMove)) return 2;
            if (flags.HasFlag(TWFlags.SafeZone)) return 3;
            if (flags.HasFlag(TWFlags.Water)) return 4;
            return 5;
        }

        private static bool HasDifferentNeighbour(
            TWFlags[] flags,
            int tileX,
            int tileY,
            int terrainClass,
            int radius)
        {
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (Math.Abs(offsetX) != radius && Math.Abs(offsetY) != radius)
                    {
                        continue;
                    }

                    int x = tileX + offsetX;
                    int y = tileY + offsetY;
                    if ((uint)x >= TerrainSize || (uint)y >= TerrainSize)
                    {
                        if (terrainClass != 0)
                        {
                            return true;
                        }
                        continue;
                    }

                    if (GetTerrainClass(flags[y * TerrainSize + x]) != terrainClass)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static Color GetTerrainColor(
            TWFlags flags,
            int terrainClass,
            bool hardEdge,
            bool softEdge,
            bool contour,
            float normalizedHeight,
            float reliefLight,
            int noise)
        {
            int red;
            int green;
            int blue;
            int alpha;

            switch (terrainClass)
            {
                case 0:
                    red = 80; green = 110; blue = 124; alpha = 0;
                    break;
                case 1:
                    red = 226; green = 176; blue = 96; alpha = 128 + noise;
                    break;
                case 2:
                    red = 118; green = 150; blue = 164; alpha = 72 + noise;
                    break;
                case 3:
                    red = 86; green = 220; blue = 174; alpha = 112 + noise;
                    break;
                case 4:
                    red = 74; green = 150; blue = 220; alpha = 96 + noise;
                    break;
                default:
                    red = 126; green = 174; blue = 190; alpha = 88 + noise;
                    break;
            }

            if (hardEdge)
            {
                alpha = terrainClass == 0 ? 112 : 198;
                if (!flags.HasFlag(TWFlags.Height))
                {
                    red = Math.Max(red, 174);
                    green = Math.Max(green, 208);
                    blue = Math.Max(blue, 218);
                }
            }
            else if (softEdge)
            {
                alpha = Math.Max(alpha, terrainClass == 0 ? 34 : 78);
            }

            float illumination = MathHelper.Lerp(0.46f, 1.42f, reliefLight);
            red = Math.Clamp((int)MathF.Round(
                red * MathHelper.Lerp(0.72f, 1.34f, normalizedHeight) * illumination), 0, 255);
            green = Math.Clamp((int)MathF.Round(
                green * MathHelper.Lerp(0.78f, 1.22f, normalizedHeight) * illumination), 0, 255);
            blue = Math.Clamp((int)MathF.Round(
                blue * MathHelper.Lerp(0.86f, 1.06f, normalizedHeight) * illumination), 0, 255);

            if (contour)
            {
                red = Math.Max(red, 204);
                green = Math.Max(green, 214);
                blue = Math.Max(blue, 202);
                alpha = Math.Max(alpha, 158);
            }

            return Color.FromNonPremultiplied(red, green, blue, alpha);
        }

        private async Task<List<MiniMapMarker>> LoadMarkersAsync(string worldName)
        {
            string markerPath = FindMarkerDataPath(worldName);
            if (markerPath == null)
            {
                return new List<MiniMapMarker>();
            }

            try
            {
                byte[] fileData = await File.ReadAllBytesAsync(markerPath);
                return ParseMarkers(fileData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MiniMap] Could not load marker data '{markerPath}': {ex.Message}");
                return new List<MiniMapMarker>();
            }
        }

        private static string FindMarkerDataPath(string worldName)
        {
            string localRoot = Path.Combine(Constants.DataPath, "Local");
            if (!Directory.Exists(localRoot) || string.IsNullOrWhiteSpace(worldName))
            {
                return null;
            }

            string normalizedWorldName = NormalizeFileName(worldName);
            foreach (string languageDirectory in Directory.EnumerateDirectories(localRoot))
            {
                string minimapDirectory = Path.Combine(languageDirectory, "Minimap");
                if (!Directory.Exists(minimapDirectory))
                {
                    continue;
                }

                foreach (string path in Directory.EnumerateFiles(minimapDirectory, "Minimap_*.bmd"))
                {
                    string fileName = Path.GetFileNameWithoutExtension(path);
                    string normalizedFileName = NormalizeFileName(fileName);
                    if (normalizedFileName.Contains(normalizedWorldName, StringComparison.OrdinalIgnoreCase))
                    {
                        return path;
                    }
                }
            }

            return null;
        }

        private static string NormalizeFileName(string value)
        {
            return new string(value.Where(char.IsLetterOrDigit).ToArray());
        }

        private static List<MiniMapMarker> ParseMarkers(byte[] fileData)
        {
            int encryptedDataLength = MarkerRecordCount * MarkerRecordSize;
            if (fileData.Length < encryptedDataLength)
            {
                return new List<MiniMapMarker>();
            }

            var offsets = fileData.Length >= LegacyHeaderSize + encryptedDataLength + sizeof(uint)
                ? new[] { 0, LegacyHeaderSize }
                : new[] { 0 };
            List<MiniMapMarker> bestMarkers = new();

            foreach (int dataOffset in offsets)
            {
                List<MiniMapMarker> markers = ParseMarkerRecords(fileData, dataOffset, encryptedDataLength);
                if (markers.Count > bestMarkers.Count)
                {
                    bestMarkers = markers;
                }
            }

            return bestMarkers;
        }

        private static List<MiniMapMarker> ParseMarkerRecords(byte[] fileData, int dataOffset, int dataLength)
        {
            if (fileData.Length < dataOffset + dataLength)
            {
                return new List<MiniMapMarker>();
            }

            var markers = new List<MiniMapMarker>();
            for (int i = 0; i < MarkerRecordCount; i++)
            {
                int offset = dataOffset + i * MarkerRecordSize;
                byte[] record = new byte[MarkerRecordSize];
                Buffer.BlockCopy(fileData, offset, record, 0, record.Length);
                BuxConvert(record);

                byte kind = record[0];
                if (kind == 0)
                {
                    break;
                }

                if (kind is not 1 and not 2)
                {
                    continue;
                }

                int x = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(1, sizeof(int)));
                int y = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(5, sizeof(int)));
                int rotation = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(9, sizeof(int)));
                string name = DecodeMarkerName(record.AsSpan(13, 100));

                if ((uint)x >= 256 || (uint)y >= 256)
                {
                    continue;
                }

                markers.Add(new MiniMapMarker
                {
                    ID = i,
                    Kind = (MiniMapMarkerKind)kind,
                    Location = new Vector2(x, y),
                    Rotation = rotation,
                    Name = name
                });
            }

            return markers;
        }

        private static string DecodeMarkerName(ReadOnlySpan<byte> bytes)
        {
            int length = bytes.IndexOf((byte)0);
            if (length < 0) length = bytes.Length;
            return Constants.DATA_TEXT_ENCODING.GetString(bytes[..length]).Trim();
        }

        private static void BuxConvert(Span<byte> bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] ^= BuxCode[i % BuxCode.Length];
            }
        }

        public void Show()
        {
            if (Visible)
            {
                return;
            }

            Visible = true;

            if (_terrainMapPixels == null && _gameScene.World != null)
            {
                _ = LoadContentForWorld(_gameScene.World.WorldIndex);
            }
        }

        public void Hide()
        {
            Visible = false;
            _tooltipText = null;
            if (Scene?.FocusControl == this)
            {
                Scene.FocusControl = null;
            }
        }

        public override void Update(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready)
            {
                return;
            }

            base.Update(gameTime);

            if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Escape) &&
                MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Escape))
            {
                Hide();
                return;
            }

            Point mousePosition = MuGame.Instance.UiMouseState.Position;
            Rectangle mapRectangle = GetMapScreenRectangle();

            int scrollDelta = MuGame.Instance.UiMouseState.ScrollWheelValue -
                              MuGame.Instance.PrevUiMouseState.ScrollWheelValue;
            if (scrollDelta != 0 && mapRectangle.Contains(mousePosition))
            {
                _zoom = MathHelper.Clamp(_zoom + Math.Sign(scrollDelta) * ZoomStep, MinZoom, MaxZoom);
            }

            UpdateTooltip(mousePosition, mapRectangle);

            if (Mobile)
                UpdateCloseButton(mousePosition);
        }

        /// <summary>關閉鈕的按下狀態。位置與繪製都交給 MobileUi，兩邊用同一個算法。</summary>
        private bool _closePressed;
        private bool _closeWasPressed;

        private void UpdateCloseButton(Point mousePosition)
        {
            var closeRect = MobileUi.WindowCloseButtonRect(DisplayRectangle);
            bool pressed = MuGame.Instance.UiMouseState.LeftButton == ButtonState.Pressed;

            if (pressed && !_closeWasPressed)
            {
                _closePressed = closeRect.Contains(mousePosition);
            }
            else if (!pressed && _closeWasPressed)
            {
                // 「按下」與「放開」都要落在按鈕上才算數 —— 手指按下後滑開就是取消。
                bool activate = _closePressed && closeRect.Contains(mousePosition);
                _closePressed = false;
                _closeWasPressed = false;

                if (activate)
                {
                    Controllers.SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav");
                    Hide();
                }
                return;
            }

            _closeWasPressed = pressed;
        }

        private void UpdateTooltip(Point mousePosition, Rectangle mapRectangle)
        {
            _tooltipText = null;
            if (!mapRectangle.Contains(mousePosition))
            {
                return;
            }

            float hoverRadius = MarkerHoverRadius * Scale;
            float closestDistanceSquared = hoverRadius * hoverRadius;
            foreach (var target in _hoverTargets)
            {
                float distanceSquared = Vector2.DistanceSquared(mousePosition.ToVector2(), target.Position);
                if (distanceSquared < closestDistanceSquared)
                {
                    closestDistanceSquared = distanceSquared;
                    _tooltipText = target.Text;
                }
            }

            if (!string.IsNullOrWhiteSpace(_tooltipText))
            {
                _tooltipPosition = mousePosition.ToVector2() + new Vector2(12f, 12f);
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready)
            {
                return;
            }

            EnsureTerrainTexture();
            RenderMapSurface(gameTime);

            SpriteBatch spriteBatch = GraphicsManager.Instance.Sprite;
            SpriteBatchScope? scope = null;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                scope = new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.LinearClamp,
                    transform: UiScaler.SpriteTransform);
            }

            try
            {
                DrawMapSurface(spriteBatch);

                // 手機<b>不畫面板底</b>。使用者的原話：地圖原本就是半透明浮在遊戲上，
                // 那個設計是對的，加一塊黑底只是把景色遮掉。
                // 但關閉鈕要留 —— 其他視窗都有，只有地圖沒有會讓人先在左上角白找一次。
                if (Mobile)
                {
                    MobileUi.DrawCloseGlyph(spriteBatch,
                        MobileUi.WindowCloseButtonRect(DisplayRectangle), _closePressed);
                }
                DrawOverlayCaption(spriteBatch);
                DrawTooltip(spriteBatch);
            }
            finally
            {
                scope?.Dispose();
            }
        }

        private void EnsureTerrainTexture()
        {
            GraphicsDevice graphicsDevice = GraphicsManager.Instance.GraphicsDevice;
            if (graphicsDevice == null)
            {
                return;
            }

            if (_edgeMaskTexture == null || _edgeMaskTexture.IsDisposed)
            {
                _edgeMaskTexture = CreateEdgeMaskTexture(graphicsDevice);
            }

            if (!_terrainTextureDirty)
            {
                return;
            }

            _terrainTextureDirty = false;
            _terrainMapTexture?.Dispose();
            _terrainMapTexture = null;
            _terrainReliefTexture?.Dispose();
            _terrainReliefTexture = null;

            if (_terrainMapPixels == null)
            {
                return;
            }

            _terrainMapTexture = new Texture2D(
                graphicsDevice,
                TerrainTextureSize,
                TerrainTextureSize,
                false,
                SurfaceFormat.Color);
            _terrainMapTexture.SetData(_terrainMapPixels);

            if (_terrainReliefPixels != null)
            {
                _terrainReliefTexture = new Texture2D(
                    graphicsDevice,
                    TerrainTextureSize,
                    TerrainTextureSize,
                    false,
                    SurfaceFormat.Color);
                _terrainReliefTexture.SetData(_terrainReliefPixels);
            }
        }

        private static Texture2D CreateEdgeMaskTexture(GraphicsDevice graphicsDevice)
        {
            var texture = new Texture2D(
                graphicsDevice,
                EdgeMaskSize,
                EdgeMaskSize,
                false,
                SurfaceFormat.Color);
            var pixels = new Color[EdgeMaskSize * EdgeMaskSize];
            float center = EdgeMaskSize * 0.5f;

            for (int y = 0; y < EdgeMaskSize; y++)
            {
                for (int x = 0; x < EdgeMaskSize; x++)
                {
                    float normalizedX = (x + 0.5f - center) / center;
                    float normalizedY = (y + 0.5f - center) / center;
                    float angle = MathF.Atan2(normalizedY, normalizedX);
                    float organicOffset = MathF.Sin(angle * 5f) * 0.012f + MathF.Cos(angle * 3f) * 0.008f;
                    float radius = MathF.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY) + organicOffset;
                    float fade = MathHelper.Clamp((radius - 0.64f) / 0.34f, 0f, 1f);
                    fade = fade * fade * (3f - 2f * fade);
                    int alpha = (int)MathF.Round((1f - fade) * 255f);
                    pixels[y * EdgeMaskSize + x] = Color.FromNonPremultiplied(255, 255, 255, alpha);
                }
            }

            texture.SetData(pixels);
            return texture;
        }

        private void RenderMapSurface(GameTime gameTime)
        {
            _hoverTargets.Clear();
            if (_terrainMapTexture == null)
            {
                return;
            }

            GraphicsDevice graphicsDevice = GraphicsManager.Instance.GraphicsDevice;
            if (graphicsDevice == null)
            {
                return;
            }

            if (_mapSurface == null || _mapSurface.IsDisposed)
            {
                Client.Main.Graphics.UiRenderTargetPool.Return(_mapSurface);
                _mapSurface = Client.Main.Graphics.UiRenderTargetPool.Rent(graphicsDevice, MapSize, MapSize);
            }

            // 切換 render target 之前必須先把外層批次送出去。
            //
            // 少了這一步，畫面上排隊中的東西（聊天訊息、公告）會在切換之後才被
            // 送出，於是全部畫進這張地圖表面裡，跟著地圖一起顯示在畫面中央 ——
            // 使用者回報了很多次的「map 和 note 的文字合併重疊」就是這個。
            // 詳見 SpriteBatchScope.BeginRenderTarget 的說明。
            using var __rtSection = SpriteBatchScope.BeginRenderTarget(graphicsDevice, _mapSurface);
            try
            {
                graphicsDevice.Clear(Color.Transparent);

                SpriteBatch spriteBatch = GraphicsManager.Instance.Sprite;
                using (new SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp))
                {
                    if (!TryGetPlayer(out PlayerObject player))
                    {
                        spriteBatch.Draw(
                            _terrainMapTexture,
                            new Rectangle(0, 0, MapSize, MapSize),
                            OverlayTheme.MapTint * 0.55f);
                    }
                    else
                    {
                        Rectangle source = GetMapSourceRectangle(player);
                        Vector2 drawScale = new(
                            MapSize * MapCoverageScale / source.Width,
                            MapSize * MapCoverageScale / source.Height);
                        Vector2 center = new(MapSize / 2f, MapSize / 2f);

                        DrawIsometricMapLayer(spriteBatch, source, drawScale, center);

                        DrawStaticMarkers(spriteBatch, source, drawScale, center);
                        DrawWorldMarkers(spriteBatch, player, source, drawScale, center);
                        TryGetLocalMapPosition(GetPlayerTilePosition(player), source, drawScale, center, out Vector2 playerMapPosition);
                        playerMapPosition.X = MathHelper.Clamp(playerMapPosition.X, 8f, MapSize - 8f);
                        playerMapPosition.Y = MathHelper.Clamp(playerMapPosition.Y, 8f, MapSize - 8f);
                        float pulse = 0.5f + MathF.Sin((float)gameTime.TotalGameTime.TotalSeconds * 4f) * 0.5f;
                        DrawLocalPlayerMarker(spriteBatch, playerMapPosition, pulse);
                    }
                }

                if (_edgeMaskTexture != null && !_edgeMaskTexture.IsDisposed)
                {
                    using var maskScope = new SpriteBatchScope(
                        spriteBatch,
                        SpriteSortMode.Deferred,
                        _edgeMaskBlendState,
                        SamplerState.LinearClamp);
                    spriteBatch.Draw(
                        _edgeMaskTexture,
                        new Rectangle(0, 0, MapSize, MapSize),
                        Color.White);
                }
            }
            finally
            {
                // render target 的還原交給 __rtSection（using），
                // 它同時會把外層批次重新開起來。
            }
        }

        private void DrawMapSurface(SpriteBatch spriteBatch)
        {
            Rectangle destination = GetMapScreenRectangle();
            if (_mapSurface != null && !_mapSurface.IsDisposed && _terrainMapTexture != null)
            {
                spriteBatch.Draw(_mapSurface, destination, Color.White * (Alpha * 0.86f));
                return;
            }

            DrawCenteredText(spriteBatch, "Map unavailable", destination, OverlayTheme.Caption * 0.55f, 0.35f);
        }

        private void DrawIsometricMapLayer(
            SpriteBatch spriteBatch,
            Rectangle source,
            Vector2 drawScale,
            Vector2 center)
        {
            Vector2 origin = new(source.Width / 2f, source.Height / 2f);

            if (_terrainReliefTexture != null && !_terrainReliefTexture.IsDisposed)
            {
                for (int layer = ReliefLayerCount; layer >= 1; layer--)
                {
                    spriteBatch.Draw(
                        _terrainReliefTexture,
                        center + new Vector2(0f, layer * ReliefLayerOffset),
                        source,
                        Color.White * 0.3f,
                        MapRotation,
                        origin,
                        drawScale,
                        SpriteEffects.None,
                        0f);
                }
            }

            spriteBatch.Draw(
                _terrainMapTexture,
                center,
                source,
                OverlayTheme.MapTint * 0.98f,
                MapRotation,
                origin,
                drawScale,
                SpriteEffects.None,
                0f);
        }

        private void DrawOverlayCaption(SpriteBatch spriteBatch)
        {
            if (_font == null || string.IsNullOrWhiteSpace(_worldName))
            {
                return;
            }


            const float scale = 0.36f;
            string caption = _worldName.ToUpperInvariant();
            Vector2 size = _font.MeasureString(caption) * scale;
            Rectangle mapRectangle = GetMapScreenRectangle();
            Vector2 position = new(
                mapRectangle.Center.X - size.X * 0.5f,
                mapRectangle.Y + 8f * Scale);
            DrawTextWithShadow(spriteBatch, caption, position, OverlayTheme.Caption * 0.78f, scale);
        }

        private Rectangle GetMapSourceRectangle(PlayerObject player)
        {
            Vector2 tilePosition = GetPlayerTilePosition(player);
            float centerX = TerrainTexturePadding + tilePosition.Y;
            float centerY = TerrainTexturePadding + tilePosition.X;
            float width = Math.Clamp(MapSize * MapCoverageScale / _zoom * TerrainSize, 1f, TerrainSize);
            float height = Math.Clamp(MapSize * MapCoverageScale / _zoom * TerrainSize, 1f, TerrainSize);

            centerX = MathHelper.Clamp(centerX, width / 2f, _terrainMapTexture.Width - width / 2f);
            centerY = MathHelper.Clamp(centerY, height / 2f, _terrainMapTexture.Height - height / 2f);

            return new Rectangle(
                (int)MathF.Round(centerX - width / 2f),
                (int)MathF.Round(centerY - height / 2f),
                Math.Max(1, (int)MathF.Round(width)),
                Math.Max(1, (int)MathF.Round(height)));
        }

        private void DrawStaticMarkers(SpriteBatch spriteBatch, Rectangle source, Vector2 drawScale, Vector2 center)
        {
            foreach (MiniMapMarker marker in _markers)
            {
                if (!TryGetLocalMapPosition(marker.Location, source, drawScale, center, out Vector2 localPosition))
                {
                    continue;
                }

                ProceduralMarkerKind kind = marker.Kind == MiniMapMarkerKind.NPC
                    ? ProceduralMarkerKind.Npc
                    : ProceduralMarkerKind.Portal;
                DrawProceduralMarker(spriteBatch, localPosition, kind);
                AddHoverTarget(localPosition, string.IsNullOrWhiteSpace(marker.Name) ? kind.ToString() : marker.Name);
            }
        }

        private void DrawWorldMarkers(
            SpriteBatch spriteBatch,
            PlayerObject localPlayer,
            Rectangle source,
            Vector2 drawScale,
            Vector2 center)
        {
            WorldControl world = _gameScene.World;
            if (world == null)
            {
                return;
            }

            foreach (WalkerObject walker in world.Walkers)
            {
                if (ReferenceEquals(walker, localPlayer) || walker.IsMainWalker || !walker.Visible)
                {
                    continue;
                }

                ProceduralMarkerKind kind;
                string category;
                if (walker is NPCObject)
                {
                    kind = ProceduralMarkerKind.Npc;
                    category = "NPC";
                }
                else if (walker is MonsterObject)
                {
                    kind = ProceduralMarkerKind.Monster;
                    category = "Monster";
                }
                else if (walker is PlayerObject)
                {
                    kind = ProceduralMarkerKind.Player;
                    category = "Player";
                }
                else
                {
                    continue;
                }

                if (!TryGetLocalMapPosition(walker.Location, source, drawScale, center, out Vector2 localPosition))
                {
                    continue;
                }

                DrawProceduralMarker(spriteBatch, localPosition, kind);
                string name = string.IsNullOrWhiteSpace(walker.DisplayName) ? category : walker.DisplayName;
                AddHoverTarget(localPosition, $"{category}: {name}");
            }
        }

        private bool TryGetLocalMapPosition(
            Vector2 tilePosition,
            Rectangle source,
            Vector2 drawScale,
            Vector2 center,
            out Vector2 localPosition)
        {
            Vector2 texturePosition = new(
                TerrainTexturePadding + tilePosition.Y,
                TerrainTexturePadding + tilePosition.X);
            Vector2 sourceCenter = new(
                source.X + source.Width * 0.5f,
                source.Y + source.Height * 0.5f);
            Vector2 relative = (texturePosition - sourceCenter) * drawScale;
            localPosition = center + Vector2.Transform(relative, MapRotationMatrix);
            float markerRadius = MapSize * 0.48f;
            return new Rectangle(0, 0, MapSize, MapSize).Contains(localPosition.ToPoint()) &&
                   Vector2.DistanceSquared(localPosition, center) <= markerRadius * markerRadius;
        }

        private void AddHoverTarget(Vector2 localPosition, string text)
        {
            Rectangle destination = GetMapScreenRectangle();
            _hoverTargets.Add((
                new Vector2(
                    destination.X + localPosition.X / MapSize * destination.Width,
                    destination.Y + localPosition.Y / MapSize * destination.Height),
                text));
        }

        private static void DrawProceduralMarker(SpriteBatch spriteBatch, Vector2 position, ProceduralMarkerKind kind)
        {
            Texture2D pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null)
            {
                return;
            }

            switch (kind)
            {
                case ProceduralMarkerKind.Npc:
                    DrawOutlinedSquare(spriteBatch, pixel, position, 9, Color.Black * 0.8f);
                    DrawOutlinedSquare(spriteBatch, pixel, position, 7, OverlayTheme.Npc);
                    spriteBatch.Draw(pixel, CenteredRectangle(position, 3, 3), OverlayTheme.Npc);
                    break;

                case ProceduralMarkerKind.Monster:
                    DrawDiamond(spriteBatch, pixel, position, 6, Color.Black * 0.8f);
                    DrawDiamond(spriteBatch, pixel, position, 4, OverlayTheme.Monster);
                    break;

                case ProceduralMarkerKind.Player:
                    DrawDiamond(spriteBatch, pixel, position, 6, Color.Black * 0.8f);
                    DrawDiamond(spriteBatch, pixel, position, 4, OverlayTheme.Player);
                    spriteBatch.Draw(pixel, CenteredRectangle(position, 2, 2), OverlayTheme.Text);
                    break;

                case ProceduralMarkerKind.Portal:
                    DrawDiamond(spriteBatch, pixel, position, 7, Color.Black * 0.8f);
                    DrawDiamond(spriteBatch, pixel, position, 5, PortalMarkerColor);
                    DrawDiamond(spriteBatch, pixel, position, 2, OverlayTheme.Dark);
                    break;
            }
        }

        private static void DrawLocalPlayerMarker(SpriteBatch spriteBatch, Vector2 position, float pulse)
        {
            Texture2D pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null)
            {
                return;
            }

            Vector2 tip = position + new Vector2(0f, -8f);
            Vector2 left = position + new Vector2(-6f, 6f);
            Vector2 right = position + new Vector2(6f, 6f);
            DrawDiamond(spriteBatch, pixel, position, 11, OverlayTheme.Hero * (0.06f + pulse * 0.09f));
            DrawLine(spriteBatch, pixel, tip, left, Color.Black * 0.9f, 4f);
            DrawLine(spriteBatch, pixel, tip, right, Color.Black * 0.9f, 4f);
            DrawLine(spriteBatch, pixel, left, right, Color.Black * 0.9f, 4f);
            DrawLine(spriteBatch, pixel, tip, left, OverlayTheme.Hero, 2f);
            DrawLine(spriteBatch, pixel, tip, right, OverlayTheme.Hero, 2f);
            DrawLine(spriteBatch, pixel, left, right, OverlayTheme.Hero, 2f);
        }

        private static void DrawDiamond(SpriteBatch spriteBatch, Texture2D pixel, Vector2 center, int radius, Color color)
        {
            int centerX = (int)MathF.Round(center.X);
            int centerY = (int)MathF.Round(center.Y);
            for (int y = -radius; y <= radius; y++)
            {
                int halfWidth = radius - Math.Abs(y);
                spriteBatch.Draw(pixel, new Rectangle(centerX - halfWidth, centerY + y, halfWidth * 2 + 1, 1), color);
            }
        }

        private static void DrawOutlinedSquare(SpriteBatch spriteBatch, Texture2D pixel, Vector2 center, int size, Color color)
        {
            Rectangle rectangle = CenteredRectangle(center, size, size);
            spriteBatch.Draw(pixel, new Rectangle(rectangle.X, rectangle.Y, rectangle.Width, 1), color);
            spriteBatch.Draw(pixel, new Rectangle(rectangle.X, rectangle.Bottom - 1, rectangle.Width, 1), color);
            spriteBatch.Draw(pixel, new Rectangle(rectangle.X, rectangle.Y, 1, rectangle.Height), color);
            spriteBatch.Draw(pixel, new Rectangle(rectangle.Right - 1, rectangle.Y, 1, rectangle.Height), color);
        }

        private static Rectangle CenteredRectangle(Vector2 center, int width, int height)
        {
            return new Rectangle(
                (int)MathF.Round(center.X - width / 2f),
                (int)MathF.Round(center.Y - height / 2f),
                width,
                height);
        }

        private static void DrawLine(
            SpriteBatch spriteBatch,
            Texture2D pixel,
            Vector2 start,
            Vector2 end,
            Color color,
            float thickness)
        {
            Vector2 direction = end - start;
            float length = direction.Length();
            if (length <= 0f)
            {
                return;
            }

            spriteBatch.Draw(
                pixel,
                start,
                null,
                color,
                MathF.Atan2(direction.Y, direction.X),
                new Vector2(0f, 0.5f),
                new Vector2(length, thickness),
                SpriteEffects.None,
                0f);
        }

        private void DrawTooltip(SpriteBatch spriteBatch)
        {
            if (string.IsNullOrWhiteSpace(_tooltipText) || _font == null)
            {
                return;
            }

            const float scale = 0.38f;
            Vector2 textSize = _font.MeasureString(_tooltipText) * scale;
            Rectangle rectangle = new(
                (int)_tooltipPosition.X,
                (int)_tooltipPosition.Y,
                (int)MathF.Ceiling(textSize.X),
                (int)MathF.Ceiling(textSize.Y));

            // 畫布是滿版的，夾到 4 就等於貼著實體螢幕邊緣（可能在鏡頭挖孔底下）。
            // 夾到對齊線才是可見又可點的範圍。
            rectangle.X = Math.Clamp(rectangle.X,
                MobileUi.LeftEdge,
                Math.Max(MobileUi.LeftEdge, MobileUi.RightEdge - rectangle.Width));
            rectangle.Y = Math.Clamp(rectangle.Y, 4, UiScaler.VirtualSize.Y - rectangle.Height - 4);
            DrawTextWithShadow(
                spriteBatch,
                _tooltipText,
                new Vector2(rectangle.X, rectangle.Y),
                OverlayTheme.Text,
                scale);
        }

        private Rectangle GetMapScreenRectangle()
        {
            Rectangle control = DisplayRectangle;
            return new Rectangle(
                control.X + (int)MathF.Round(MapLeft * Scale),
                control.Y + (int)MathF.Round(MapTop * Scale),
                Math.Max(1, (int)MathF.Round(MapSize * Scale)),
                Math.Max(1, (int)MathF.Round(MapDisplayHeight * Scale)));
        }

        private bool TryGetPlayer(out PlayerObject player)
        {
            player = null;
            if (_gameScene.World is not WalkableWorldControl walkableWorld || walkableWorld.Walker is not PlayerObject worldPlayer)
            {
                return false;
            }

            player = worldPlayer;
            return true;
        }

        private static Vector2 GetPlayerTilePosition(PlayerObject player)
        {
            return new Vector2(
                player.Position.X / Constants.TERRAIN_SCALE,
                player.Position.Y / Constants.TERRAIN_SCALE);
        }

        private void DrawCenteredText(SpriteBatch spriteBatch, string text, Rectangle rectangle, Color color, float scale)
        {
            if (_font == null)
            {
                return;
            }

            Vector2 size = _font.MeasureString(text) * scale;
            Vector2 position = new(
                rectangle.X + (rectangle.Width - size.X) / 2f,
                rectangle.Y + (rectangle.Height - size.Y) / 2f);
            DrawTextWithShadow(spriteBatch, text, position, color, scale);
        }

        private void DrawTextWithShadow(SpriteBatch spriteBatch, string text, Vector2 position, Color color, float scale)
        {
            spriteBatch.DrawString(_font, text, position + Vector2.One, Color.Black * 0.65f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(_font, text, position, color * Alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        public override void Dispose()
        {
            base.Dispose();
            _terrainMapTexture?.Dispose();
            _terrainMapTexture = null;
            _terrainReliefTexture?.Dispose();
            _terrainReliefTexture = null;
            _edgeMaskTexture?.Dispose();
            _edgeMaskTexture = null;
            _edgeMaskBlendState.Dispose();
            Client.Main.Graphics.UiRenderTargetPool.Return(_mapSurface);
            _mapSurface = null;
        }
    }
}
