using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
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

        private enum ResizeCorner : byte
        {
            None,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        private const int WindowWidth = 272;
        private const int WindowHeight = 300;
        private const int HeaderHeight = 34;
        private const int MapSize = 240;
        private const int MapLeft = 16;
        private const int MapTop = 44;
        private const int MapPadding = 8;
        private const int MarkerRecordCount = 100;
        private const int MarkerRecordSize = 113;
        private const int LegacyHeaderSize = 45;
        private const float MapRotation = MathHelper.Pi / 4f - MathHelper.PiOver2;
        private const float MapCoverageScale = 1.41421356f;
        private const float InitialZoom = 800f;
        private const float MinZoom = 800f;
        private const float MaxZoom = 1800f;
        private const float ZoomStep = 200f;
        private const float MapWorldSize = 256f;
        private const float MarkerHoverRadius = 10f;
        private const float MinWindowScale = 0.65f;
        private const float MaxWindowScale = 1.75f;
        private const int ResizeHandleSize = 14;

        private static readonly byte[] BuxCode = { 0xFC, 0xCF, 0xAB };
        private static readonly Color PortalMarkerColor = new(190, 120, 255);
        private static readonly Matrix MapRotationMatrix = Matrix.CreateRotationZ(MapRotation);

        private readonly GameScene _gameScene;
        private readonly List<MiniMapMarker> _markers = new();
        private readonly List<(Vector2 Position, string Text)> _hoverTargets = new();

        private Texture2D _mapTexture;
        private RenderTarget2D _staticSurface;
        private RenderTarget2D _mapSurface;
        private bool _staticSurfaceDirty = true;
        private SpriteFont _font;
        private float _zoom = InitialZoom;
        private int _loadGeneration;
        private bool _closeHovered;
        private bool _closePressed;
        private bool _isDragging;
        private bool _isResizing;
        private Point _dragOffset;
        private ResizeCorner _resizeCorner;
        private ResizeCorner _hoveredResizeCorner;
        private Point _resizeAnchor;
        private string _tooltipText;
        private Vector2 _tooltipPosition;
        private string _worldName;

        public MiniMapControl(GameScene scene)
        {
            _gameScene = scene ?? throw new ArgumentNullException(nameof(scene));

            Align = ControlAlign.Top | ControlAlign.Right;
            Margin = new Margin { Top = 42, Right = 18 };
            AutoViewSize = false;
            ControlSize = new Point(WindowWidth, WindowHeight);
            ViewSize = ControlSize;
            Interactive = true;
            Visible = false;
        }

        public override async Task Load()
        {
            await base.Load();
            _font = GraphicsManager.Instance.Font;
            InvalidateStaticSurface();
        }

        public async Task LoadContentForWorld(short worldIndex)
        {
            int generation = ++_loadGeneration;
            string worldName = _gameScene.World?.Name;

            if (string.IsNullOrWhiteSpace(worldName))
            {
                worldName = MapDatabase.GetMapName((ushort)Math.Max(0, worldIndex - 1));
            }

            Texture2D mapTexture = await LoadMapTextureAsync(worldIndex);
            List<MiniMapMarker> markers = await LoadMarkersAsync(worldName);

            if (generation != _loadGeneration)
            {
                return;
            }

            _mapTexture = mapTexture;
            _worldName = worldName;
            _markers.Clear();
            _markers.AddRange(markers);
            _hoverTargets.Clear();
            _zoom = InitialZoom;
            InvalidateStaticSurface();
        }

        private static async Task<Texture2D> LoadMapTextureAsync(short worldIndex)
        {
            string basePath = $"World{worldIndex}/mini_map";
            Texture2D texture = await TextureLoader.Instance.PrepareAndGetTexture(basePath + ".ozt");
            return texture ?? await TextureLoader.Instance.PrepareAndGetTexture(basePath + ".tga");
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
            if (Align == ControlAlign.None)
            {
                SetManualScreenPosition(DisplayRectangle.X, DisplayRectangle.Y);
            }
            BringToFront();

            if (_mapTexture == null && _gameScene.World != null)
            {
                _ = LoadContentForWorld(_gameScene.World.WorldIndex);
            }
        }

        public void Hide()
        {
            Visible = false;
            _isDragging = false;
            _isResizing = false;
            _closePressed = false;
            _resizeCorner = ResizeCorner.None;
            _hoveredResizeCorner = ResizeCorner.None;
            _tooltipText = null;
            if (Scene?.FocusControl == this)
            {
                Scene.FocusControl = null;
            }

            // SourceMain5.2 sends the close-NPC notification when this window closes.
            _ = MuGame.Network?.GetCharacterService()?.SendCloseNpcRequestAsync();
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
            Rectangle closeRectangle = GetCloseButtonRectangle();
            _closeHovered = closeRectangle.Contains(mousePosition);
            _hoveredResizeCorner = GetResizeCorner(mousePosition);

            bool leftPressed = MuGame.Instance.UiMouseState.LeftButton == ButtonState.Pressed;
            bool leftJustPressed = leftPressed &&
                                   MuGame.Instance.PrevUiMouseState.LeftButton == ButtonState.Released;
            bool leftJustReleased = !leftPressed &&
                                    MuGame.Instance.PrevUiMouseState.LeftButton == ButtonState.Pressed;

            if (leftJustPressed)
            {
                _closePressed = _closeHovered;
                if (!_closePressed && _hoveredResizeCorner != ResizeCorner.None)
                {
                    BeginResize(_hoveredResizeCorner);
                }
                else if (!_closePressed && GetHeaderScreenRectangle().Contains(mousePosition))
                {
                    BeginDrag(mousePosition);
                }
            }

            if (_isResizing && leftPressed)
            {
                UpdateResize(mousePosition);
            }
            else if (_isDragging && leftPressed)
            {
                UpdateDrag(mousePosition);
            }

            if (leftJustReleased)
            {
                bool closeRequested = _closePressed && _closeHovered;
                _closePressed = false;
                _isDragging = false;
                _isResizing = false;
                _resizeCorner = ResizeCorner.None;

                if (closeRequested)
                {
                    Hide();
                    return;
                }
            }

            Rectangle mapRectangle = GetMapScreenRectangle();

            int scrollDelta = MuGame.Instance.UiMouseState.ScrollWheelValue -
                              MuGame.Instance.PrevUiMouseState.ScrollWheelValue;
            if (!_isDragging && !_isResizing && scrollDelta != 0 && mapRectangle.Contains(mousePosition))
            {
                _zoom = MathHelper.Clamp(_zoom + Math.Sign(scrollDelta) * ZoomStep, MinZoom, MaxZoom);
            }

            UpdateTooltip(mousePosition, mapRectangle);
        }

        private void BeginDrag(Point mousePosition)
        {
            Rectangle rectangle = DisplayRectangle;
            SwitchToManualPosition(rectangle.Location);
            _isDragging = true;
            _dragOffset = new Point(mousePosition.X - rectangle.X, mousePosition.Y - rectangle.Y);
            BringToFront();
        }

        private void UpdateDrag(Point mousePosition)
        {
            SetManualScreenPosition(
                mousePosition.X - _dragOffset.X,
                mousePosition.Y - _dragOffset.Y);
        }

        private void BeginResize(ResizeCorner corner)
        {
            Rectangle rectangle = DisplayRectangle;
            _resizeCorner = corner;
            _resizeAnchor = corner switch
            {
                ResizeCorner.TopLeft => new Point(rectangle.Right, rectangle.Bottom),
                ResizeCorner.TopRight => new Point(rectangle.Left, rectangle.Bottom),
                ResizeCorner.BottomLeft => new Point(rectangle.Right, rectangle.Top),
                _ => new Point(rectangle.Left, rectangle.Top)
            };

            SwitchToManualPosition(rectangle.Location);
            _isResizing = true;
            BringToFront();
        }

        private void UpdateResize(Point mousePosition)
        {
            float desiredWidth = _resizeCorner is ResizeCorner.TopLeft or ResizeCorner.BottomLeft
                ? _resizeAnchor.X - mousePosition.X
                : mousePosition.X - _resizeAnchor.X;
            float desiredHeight = _resizeCorner is ResizeCorner.TopLeft or ResizeCorner.TopRight
                ? _resizeAnchor.Y - mousePosition.Y
                : mousePosition.Y - _resizeAnchor.Y;

            float projectedScale =
                (desiredWidth * WindowWidth + desiredHeight * WindowHeight) /
                (WindowWidth * WindowWidth + WindowHeight * WindowHeight);
            float screenScaleLimit = MathF.Min(
                UiScaler.VirtualSize.X / (float)WindowWidth,
                UiScaler.VirtualSize.Y / (float)WindowHeight);
            float maximumScale = MathF.Max(MinWindowScale, MathF.Min(MaxWindowScale, screenScaleLimit));
            Scale = MathHelper.Clamp(projectedScale, MinWindowScale, maximumScale);

            Point size = DisplaySize;
            int x = _resizeCorner is ResizeCorner.TopLeft or ResizeCorner.BottomLeft
                ? _resizeAnchor.X - size.X
                : _resizeAnchor.X;
            int y = _resizeCorner is ResizeCorner.TopLeft or ResizeCorner.TopRight
                ? _resizeAnchor.Y - size.Y
                : _resizeAnchor.Y;
            SetManualScreenPosition(x, y);
        }

        private void SwitchToManualPosition(Point screenPosition)
        {
            Align = ControlAlign.None;
            Margin = default;
            Offset = Point.Zero;
            SetManualScreenPosition(screenPosition.X, screenPosition.Y);
        }

        private void SetManualScreenPosition(int screenX, int screenY)
        {
            Point parentPosition = Parent?.DisplayRectangle.Location ?? Point.Zero;
            int maxX = Math.Max(0, UiScaler.VirtualSize.X - DisplaySize.X);
            int maxY = Math.Max(0, UiScaler.VirtualSize.Y - DisplaySize.Y);
            X = Math.Clamp(screenX, 0, maxX) - parentPosition.X;
            Y = Math.Clamp(screenY, 0, maxY) - parentPosition.Y;
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

            EnsureStaticSurface();
            RenderMapSurface();

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
                if (_staticSurface != null && !_staticSurface.IsDisposed)
                {
                    spriteBatch.Draw(_staticSurface, DisplayRectangle, Color.White * Alpha);
                }

                DrawMapSurface(spriteBatch);
                DrawCloseButton(spriteBatch);
                DrawResizeHandles(spriteBatch);
                DrawTooltip(spriteBatch);
            }
            finally
            {
                scope?.Dispose();
            }
        }

        private void EnsureStaticSurface()
        {
            if (!_staticSurfaceDirty && _staticSurface != null && !_staticSurface.IsDisposed)
            {
                return;
            }

            var graphicsDevice = GraphicsManager.Instance.GraphicsDevice;
            if (graphicsDevice == null)
            {
                return;
            }

            Client.Main.Graphics.UiRenderTargetPool.Return(_staticSurface);
            _staticSurface = Client.Main.Graphics.UiRenderTargetPool.Rent(graphicsDevice, WindowWidth, WindowHeight);

            var previousTargets = graphicsDevice.GetRenderTargets();
            graphicsDevice.SetRenderTarget(_staticSurface);
            graphicsDevice.Clear(Color.Transparent);

            SpriteBatch spriteBatch = GraphicsManager.Instance.Sprite;
            using (new SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp))
            {
                DrawStaticElements(spriteBatch);
            }

            graphicsDevice.SetRenderTargets(previousTargets);
            _staticSurfaceDirty = false;
        }

        private void DrawStaticElements(SpriteBatch spriteBatch)
        {
            Texture2D pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null)
            {
                return;
            }

            Rectangle window = new(0, 0, WindowWidth, WindowHeight);
            spriteBatch.Draw(pixel, window, ModernHudTheme.BorderOuter);
            UiDrawHelper.DrawVerticalGradient(
                spriteBatch,
                new Rectangle(2, 2, WindowWidth - 4, WindowHeight - 4),
                ModernHudTheme.BgDark,
                ModernHudTheme.BgDarkest);

            Rectangle header = new(10, 8, WindowWidth - 20, HeaderHeight - 8);
            UiDrawHelper.DrawPanel(
                spriteBatch,
                header,
                ModernHudTheme.BgMid,
                ModernHudTheme.BorderInner,
                ModernHudTheme.BorderOuter,
                ModernHudTheme.BorderHighlight * 0.3f,
                true,
                ModernHudTheme.Accent * 0.12f);
            spriteBatch.Draw(pixel, new Rectangle(20, 10, WindowWidth - 40, 2), ModernHudTheme.Accent * 0.8f);
            spriteBatch.Draw(pixel, new Rectangle(28, HeaderHeight - 2, WindowWidth - 56, 1), ModernHudTheme.AccentDim * 0.7f);
            UiDrawHelper.DrawCornerAccents(spriteBatch, window, ModernHudTheme.Accent * 0.45f, 9, 1);

            if (_font != null)
            {
                DrawTextWithShadow(spriteBatch, "MINIMAP", new Vector2(22f, 14f), ModernHudTheme.TextWhite, 0.42f);

                if (!string.IsNullOrWhiteSpace(_worldName))
                {
                    Vector2 worldNameSize = _font.MeasureString(_worldName) * 0.32f;
                    DrawTextWithShadow(
                        spriteBatch,
                        _worldName,
                        new Vector2(WindowWidth - 22f - worldNameSize.X, 15f),
                        ModernHudTheme.TextGold,
                        0.32f);
                }
            }

            Rectangle mapPanel = new(MapLeft - MapPadding, MapTop - MapPadding, MapSize + MapPadding * 2, MapSize + MapPadding * 2);
            UiDrawHelper.DrawPanel(
                spriteBatch,
                mapPanel,
                ModernHudTheme.SlotBg,
                ModernHudTheme.BorderInner,
                ModernHudTheme.BorderOuter,
                ModernHudTheme.BorderHighlight * 0.25f);
        }

        private void RenderMapSurface()
        {
            _hoverTargets.Clear();
            if (_mapTexture == null)
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

            RenderTargetBinding[] previousTargets = graphicsDevice.GetRenderTargets();
            try
            {
                graphicsDevice.SetRenderTarget(_mapSurface);
                graphicsDevice.Clear(ModernHudTheme.BgDarkest);

                SpriteBatch spriteBatch = GraphicsManager.Instance.Sprite;
                using (new SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp))
                {
                    if (!TryGetPlayer(out PlayerObject player))
                    {
                        spriteBatch.Draw(_mapTexture, new Rectangle(0, 0, MapSize, MapSize), Color.White);
                        return;
                    }

                    Rectangle source = GetMapSourceRectangle(player);
                    Vector2 drawScale = new(
                        MapSize * MapCoverageScale / source.Width,
                        MapSize * MapCoverageScale / source.Height);
                    Vector2 center = new(MapSize / 2f, MapSize / 2f);

                    spriteBatch.Draw(
                        _mapTexture,
                        center,
                        source,
                        Color.White,
                        MapRotation,
                        new Vector2(source.Width / 2f, source.Height / 2f),
                        drawScale,
                        SpriteEffects.None,
                        0f);

                    DrawStaticMarkers(spriteBatch, source, drawScale, center);
                    DrawWorldMarkers(spriteBatch, player, source, drawScale, center);
                    TryGetLocalMapPosition(GetPlayerTilePosition(player), source, drawScale, center, out Vector2 playerMapPosition);
                    playerMapPosition.X = MathHelper.Clamp(playerMapPosition.X, 8f, MapSize - 8f);
                    playerMapPosition.Y = MathHelper.Clamp(playerMapPosition.Y, 8f, MapSize - 8f);
                    DrawLocalPlayerMarker(spriteBatch, playerMapPosition);
                }
            }
            finally
            {
                graphicsDevice.SetRenderTargets(previousTargets);
            }
        }

        private void DrawMapSurface(SpriteBatch spriteBatch)
        {
            Rectangle destination = GetMapScreenRectangle();
            if (_mapSurface != null && !_mapSurface.IsDisposed && _mapTexture != null)
            {
                spriteBatch.Draw(_mapSurface, destination, Color.White * Alpha);
                return;
            }

            Texture2D pixel = GraphicsManager.Instance.Pixel;
            if (pixel != null)
            {
                spriteBatch.Draw(pixel, destination, ModernHudTheme.BgDarkest * Alpha);
            }
            DrawCenteredText(spriteBatch, "Map unavailable", destination, ModernHudTheme.TextDark, 0.35f);
        }

        private Rectangle GetMapSourceRectangle(PlayerObject player)
        {
            Vector2 tilePosition = GetPlayerTilePosition(player);
            float centerX = tilePosition.Y / MapWorldSize * _mapTexture.Width;
            float centerY = tilePosition.X / MapWorldSize * _mapTexture.Height;
            float width = Math.Clamp(MapSize * MapCoverageScale / _zoom * _mapTexture.Width, 1f, _mapTexture.Width);
            float height = Math.Clamp(MapSize * MapCoverageScale / _zoom * _mapTexture.Height, 1f, _mapTexture.Height);

            centerX = MathHelper.Clamp(centerX, width / 2f, _mapTexture.Width - width / 2f);
            centerY = MathHelper.Clamp(centerY, height / 2f, _mapTexture.Height - height / 2f);

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
                tilePosition.Y / MapWorldSize * _mapTexture.Width,
                tilePosition.X / MapWorldSize * _mapTexture.Height);
            Vector2 sourceCenter = new(
                source.X + source.Width * 0.5f,
                source.Y + source.Height * 0.5f);
            Vector2 relative = (texturePosition - sourceCenter) * drawScale;
            localPosition = center + Vector2.Transform(relative, MapRotationMatrix);
            return new Rectangle(0, 0, MapSize, MapSize).Contains(localPosition.ToPoint());
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
                    DrawOutlinedSquare(spriteBatch, pixel, position, 7, ModernHudTheme.SecondaryBright);
                    spriteBatch.Draw(pixel, CenteredRectangle(position, 3, 3), ModernHudTheme.SecondaryBright);
                    break;

                case ProceduralMarkerKind.Monster:
                    DrawDiamond(spriteBatch, pixel, position, 6, Color.Black * 0.8f);
                    DrawDiamond(spriteBatch, pixel, position, 4, ModernHudTheme.Danger);
                    break;

                case ProceduralMarkerKind.Player:
                    DrawDiamond(spriteBatch, pixel, position, 6, Color.Black * 0.8f);
                    DrawDiamond(spriteBatch, pixel, position, 4, ModernHudTheme.Success);
                    spriteBatch.Draw(pixel, CenteredRectangle(position, 2, 2), ModernHudTheme.TextWhite);
                    break;

                case ProceduralMarkerKind.Portal:
                    DrawDiamond(spriteBatch, pixel, position, 7, Color.Black * 0.8f);
                    DrawDiamond(spriteBatch, pixel, position, 5, PortalMarkerColor);
                    DrawDiamond(spriteBatch, pixel, position, 2, ModernHudTheme.BgDarkest);
                    break;
            }
        }

        private static void DrawLocalPlayerMarker(SpriteBatch spriteBatch, Vector2 position)
        {
            Texture2D pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null)
            {
                return;
            }

            Vector2 tip = position + new Vector2(0f, -8f);
            Vector2 left = position + new Vector2(-6f, 6f);
            Vector2 right = position + new Vector2(6f, 6f);
            DrawLine(spriteBatch, pixel, tip, left, Color.Black * 0.9f, 4f);
            DrawLine(spriteBatch, pixel, tip, right, Color.Black * 0.9f, 4f);
            DrawLine(spriteBatch, pixel, left, right, Color.Black * 0.9f, 4f);
            DrawLine(spriteBatch, pixel, tip, left, ModernHudTheme.AccentBright, 2f);
            DrawLine(spriteBatch, pixel, tip, right, ModernHudTheme.AccentBright, 2f);
            DrawLine(spriteBatch, pixel, left, right, ModernHudTheme.AccentBright, 2f);
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

        private void DrawCloseButton(SpriteBatch spriteBatch)
        {
            Rectangle rectangle = GetCloseButtonRectangle();
            Texture2D pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null)
            {
                return;
            }

            spriteBatch.Draw(pixel, rectangle, _closeHovered ? ModernHudTheme.Danger : ModernHudTheme.BgLight);
            UiDrawHelper.DrawBorder(spriteBatch, rectangle, _closeHovered ? ModernHudTheme.Danger : ModernHudTheme.BorderInner);
            DrawCenteredText(spriteBatch, "X", rectangle, ModernHudTheme.TextWhite, 0.38f * Scale);
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
                (int)MathF.Ceiling(textSize.X) + 14,
                (int)MathF.Ceiling(textSize.Y) + 8);

            rectangle.X = Math.Clamp(rectangle.X, 4, UiScaler.VirtualSize.X - rectangle.Width - 4);
            rectangle.Y = Math.Clamp(rectangle.Y, 4, UiScaler.VirtualSize.Y - rectangle.Height - 4);

            Texture2D pixel = GraphicsManager.Instance.Pixel;
            if (pixel != null)
            {
                spriteBatch.Draw(pixel, new Rectangle(rectangle.X + 3, rectangle.Y + 3, rectangle.Width, rectangle.Height), Color.Black * 0.55f);
            }
            UiDrawHelper.DrawVerticalGradient(spriteBatch, rectangle, ModernHudTheme.BgLight, ModernHudTheme.BgDarkest);
            UiDrawHelper.DrawBorder(spriteBatch, rectangle, ModernHudTheme.AccentDim);
            DrawTextWithShadow(spriteBatch, _tooltipText, new Vector2(rectangle.X + 7, rectangle.Y + 4), ModernHudTheme.TextWhite, scale);
        }

        private Rectangle GetMapScreenRectangle()
        {
            Rectangle control = DisplayRectangle;
            return new Rectangle(
                control.X + (int)MathF.Round(MapLeft * Scale),
                control.Y + (int)MathF.Round(MapTop * Scale),
                Math.Max(1, (int)MathF.Round(MapSize * Scale)),
                Math.Max(1, (int)MathF.Round(MapSize * Scale)));
        }

        private Rectangle GetHeaderScreenRectangle()
        {
            Rectangle control = DisplayRectangle;
            return new Rectangle(
                control.X,
                control.Y,
                control.Width,
                Math.Max(1, (int)MathF.Round(HeaderHeight * Scale)));
        }

        private ResizeCorner GetResizeCorner(Point mousePosition)
        {
            Rectangle rectangle = DisplayRectangle;
            if (new Rectangle(rectangle.Left, rectangle.Top, ResizeHandleSize, ResizeHandleSize).Contains(mousePosition))
            {
                return ResizeCorner.TopLeft;
            }
            if (new Rectangle(rectangle.Right - ResizeHandleSize, rectangle.Top, ResizeHandleSize, ResizeHandleSize).Contains(mousePosition))
            {
                return ResizeCorner.TopRight;
            }
            if (new Rectangle(rectangle.Left, rectangle.Bottom - ResizeHandleSize, ResizeHandleSize, ResizeHandleSize).Contains(mousePosition))
            {
                return ResizeCorner.BottomLeft;
            }
            if (new Rectangle(rectangle.Right - ResizeHandleSize, rectangle.Bottom - ResizeHandleSize, ResizeHandleSize, ResizeHandleSize).Contains(mousePosition))
            {
                return ResizeCorner.BottomRight;
            }

            return ResizeCorner.None;
        }

        private void DrawResizeHandles(SpriteBatch spriteBatch)
        {
            Texture2D pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null)
            {
                return;
            }

            DrawResizeHandle(spriteBatch, pixel, ResizeCorner.TopLeft);
            DrawResizeHandle(spriteBatch, pixel, ResizeCorner.TopRight);
            DrawResizeHandle(spriteBatch, pixel, ResizeCorner.BottomLeft);
            DrawResizeHandle(spriteBatch, pixel, ResizeCorner.BottomRight);
        }

        private void DrawResizeHandle(SpriteBatch spriteBatch, Texture2D pixel, ResizeCorner corner)
        {
            Rectangle rectangle = DisplayRectangle;
            bool highlighted = (_isResizing && _resizeCorner == corner) || _hoveredResizeCorner == corner;
            Color color = (highlighted ? ModernHudTheme.AccentBright : ModernHudTheme.AccentDim * 0.75f) * Alpha;
            const int length = 10;
            const int thickness = 2;

            switch (corner)
            {
                case ResizeCorner.TopLeft:
                    spriteBatch.Draw(pixel, new Rectangle(rectangle.Left, rectangle.Top, length, thickness), color);
                    spriteBatch.Draw(pixel, new Rectangle(rectangle.Left, rectangle.Top, thickness, length), color);
                    break;
                case ResizeCorner.TopRight:
                    spriteBatch.Draw(pixel, new Rectangle(rectangle.Right - length, rectangle.Top, length, thickness), color);
                    spriteBatch.Draw(pixel, new Rectangle(rectangle.Right - thickness, rectangle.Top, thickness, length), color);
                    break;
                case ResizeCorner.BottomLeft:
                    spriteBatch.Draw(pixel, new Rectangle(rectangle.Left, rectangle.Bottom - thickness, length, thickness), color);
                    spriteBatch.Draw(pixel, new Rectangle(rectangle.Left, rectangle.Bottom - length, thickness, length), color);
                    break;
                case ResizeCorner.BottomRight:
                    spriteBatch.Draw(pixel, new Rectangle(rectangle.Right - length, rectangle.Bottom - thickness, length, thickness), color);
                    spriteBatch.Draw(pixel, new Rectangle(rectangle.Right - thickness, rectangle.Bottom - length, thickness, length), color);
                    break;
            }
        }

        private Rectangle GetCloseButtonRectangle()
        {
            Rectangle control = DisplayRectangle;
            int size = Math.Max(14, (int)MathF.Round(20f * Scale));
            int rightInset = Math.Max(8, (int)MathF.Round(14f * Scale));
            int topInset = Math.Max(6, (int)MathF.Round(9f * Scale));
            return new Rectangle(control.Right - size - rightInset, control.Y + topInset, size, size);
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

        private void InvalidateStaticSurface() => _staticSurfaceDirty = true;

        protected override void OnScreenSizeChanged()
        {
            base.OnScreenSizeChanged();
            if (Align == ControlAlign.None)
            {
                SetManualScreenPosition(DisplayRectangle.X, DisplayRectangle.Y);
            }
            InvalidateStaticSurface();
        }

        public override void Dispose()
        {
            base.Dispose();
            Client.Main.Graphics.UiRenderTargetPool.Return(_staticSurface);
            _staticSurface = null;
            Client.Main.Graphics.UiRenderTargetPool.Return(_mapSurface);
            _mapSurface = null;
        }
    }
}
