using System;
using System.Collections.Generic;
using System.Linq;
using Client.Main;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Controls.UI;
using Client.Main.Models;
using Client.Main.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game
{
    public class NpcShopControl : UIControl
    {
        // ═══════════════════════════════════════════════════════════════
        // SHOP MODE
        // ═══════════════════════════════════════════════════════════════
        public enum ShopMode
        {
            BuyAndSell = 1,
            Repair = 2
        }

        // ═══════════════════════════════════════════════════════════════
        // WINDOW DIMENSIONS
        // ═══════════════════════════════════════════════════════════════
        private const int SHOP_COLUMNS = 8;
        private const int SHOP_ROWS = 15;
        /// <summary>
        /// 格子邊長。桌面 32，手機 64 —— 和背包同一個數字。
        ///
        /// 32 px 在 iPhone 上換算後約 15 pt，遠低於可以放心點的 44 pt，
        /// 而且 20x28 的道具圖示縮到那個尺寸根本認不出是什麼東西。
        /// 每個視窗自己訂一個格子大小的話，同一件道具在背包、商店、倉庫裡
        /// 會是三種尺寸 —— 玩家每換一個視窗就要重新認一次。
        /// 視窗寬度是從這個值推算的，改了會一起變寬。
        /// </summary>
        private static int SHOP_SQUARE_WIDTH => MobileUi.FitCellSize(SHOP_ROWS, HEADER_HEIGHT + SECTION_HEADER_HEIGHT + GRID_PADDING * 2 + FOOTER_HEIGHT + WINDOW_MARGIN + BUTTON_AREA_HEIGHT, 64);
        private static int SHOP_SQUARE_HEIGHT => SHOP_SQUARE_WIDTH;

        /// <summary>
        /// 標題列高度。手機至少要放得下 46 見方的關閉鈕再加上下各 6 的餘裕 ——
        /// 原本的 46 會讓關閉鈕的下緣穿出標題列、壓到內容區。
        /// </summary>
        private static int HEADER_HEIGHT => MobileUi.IsMobile ? MobileUi.CloseButtonSize + 12 : 46;
        private const int SECTION_HEADER_HEIGHT = 22;
        private const int GRID_PADDING = 10;
        private const int BUTTON_AREA_HEIGHT = 40;
        private const int FOOTER_HEIGHT = 46;
        private const int WINDOW_MARGIN = 12;

        private static readonly int GRID_WIDTH = SHOP_COLUMNS * SHOP_SQUARE_WIDTH;
        private static readonly int GRID_HEIGHT = SHOP_ROWS * SHOP_SQUARE_HEIGHT;

        // ── 手機：一行一件商品 ──
        //
        // 桌面是 8 x 15 的格線，換算成手機就是一個 364 寬、726 高的窄長條，
        // 幾乎佔滿整個畫面高度；而它靠左對齊，關閉鈕正好落在螢幕左上角的圓角上
        // ——「看得到但點不到」。格子本身也只有 40 px，圖示認不出是什麼東西。
        //
        // 改成清單：一行一件，左邊圖示、中間名稱與價格、右邊一顆固定尺寸的 BUY。
        // 名稱和價格是玩家真正要看的資訊，格線一格也放不下。
        private const int MobileRowHeight = 76;
        private const int MobileRowGap = 4;
        private const int MobileIconSize = 64;
        private const int MobileBuyWidth = 110;
        private const int MobileListWidth = 540;
        private static int MobileBuyHeight => MobileUi.CloseButtonSize;

        private static int WINDOW_WIDTH => MobileUi.IsMobile
            ? MobileListWidth
            : GRID_WIDTH + GRID_PADDING * 2 + WINDOW_MARGIN * 2;

        private int WindowHeight => MobileUi.IsMobile
            ? MobileWindowHeight
            : HEADER_HEIGHT + SECTION_HEADER_HEIGHT + GRID_PADDING * 2 + GRID_HEIGHT + (_isRepairShop ? BUTTON_AREA_HEIGHT : 0) + FOOTER_HEIGHT + WINDOW_MARGIN;

        /// <summary>手機的視窗高度：夾在畫面內，放不下的列用捲的。</summary>
        private int MobileWindowHeight
        {
            get
            {
                int chrome = HEADER_HEIGHT + 8 + (_isRepairShop ? BUTTON_AREA_HEIGHT : 0) + FOOTER_HEIGHT + WINDOW_MARGIN;
                int wanted = chrome + Math.Max(1, _items.Count) * (MobileRowHeight + MobileRowGap);
                return MobileUi.ClampWindowSize(MobileListWidth, wanted).Y;
            }
        }

        /// <summary>清單目前捲到第幾列。</summary>
        private int _mobileScrollRow;
        private int _mobileVisibleRows = 1;
        private Rectangle _mobileListRect;

        // ═══════════════════════════════════════════════════════════════
        // MODERN DARK THEME
        // ═══════════════════════════════════════════════════════════════
        /// <summary>
        /// 這個面板的顏色。每一個值都轉發到 <see cref="ModernHudTheme"/>：
        /// 桌面拿到的是一模一樣的數值，<b>手機拿到的是扁平化後的那一組</b>
        /// （金色點綴變中性灰、底色三階收斂成同一個半透明深藍灰）。
        ///
        /// 這裡原本是十份各自寫死的複本 —— 改一次配色要改十個檔案，
        /// 而手機的面板也就永遠跟登入畫面長得不一樣。
        /// 值和 ModernHudTheme 不同的欄位保留原本的字面值，並在該行說明原因。
        /// </summary>
        private static class Theme
        {
            public static readonly Color BgDarkest = ModernHudTheme.BgDarkest;
            public static readonly Color BgDark = ModernHudTheme.BgDark;
            public static readonly Color BgMid = ModernHudTheme.BgMid;
            public static readonly Color BgLight = ModernHudTheme.BgLight;

            public static readonly Color Accent = ModernHudTheme.Accent;
            public static readonly Color AccentBright = ModernHudTheme.AccentBright;
            public static readonly Color AccentDim = ModernHudTheme.AccentDim;
            public static readonly Color AccentGlow = ModernHudTheme.AccentGlow;

            public static readonly Color BorderOuter = ModernHudTheme.BorderOuter;
            public static readonly Color BorderInner = ModernHudTheme.BorderInner;
            public static readonly Color BorderHighlight = ModernHudTheme.BorderHighlight;

            public static readonly Color SlotBg = ModernHudTheme.SlotBg;
            public static readonly Color SlotBorder = ModernHudTheme.SlotBorder;
            public static readonly Color SlotHover = ModernHudTheme.SlotHover;
            public static readonly Color SlotSelected = ModernHudTheme.SlotSelected;

            public static readonly Color GlowNormal = ModernHudTheme.GlowNormal;
            public static readonly Color GlowMagic = ModernHudTheme.GlowMagic;
            public static readonly Color GlowExcellent = ModernHudTheme.GlowExcellent;
            public static readonly Color GlowAncient = ModernHudTheme.GlowAncient;
            public static readonly Color GlowLegendary = ModernHudTheme.GlowLegendary;

            public static readonly Color TextWhite = ModernHudTheme.TextWhite;
            public static readonly Color TextGold = ModernHudTheme.TextGold;
            public static readonly Color TextGray = ModernHudTheme.TextGray;
        }

        private static readonly ItemGlowPalette GlowPalette = new(
            Theme.GlowNormal,
            Theme.GlowMagic,
            Theme.GlowExcellent,
            Theme.GlowAncient,
            Theme.GlowLegendary);

        private static NpcShopControl _instance;

        private readonly List<InventoryItem> _items = new();
        private readonly List<(InventoryItem Item, Rectangle Rect)> _jewelEntries = new();
        private readonly Dictionary<string, Texture2D> _itemTextureCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(InventoryItem item, int width, int height, bool animated), Texture2D> _bmdPreviewCache = new();

        private Rectangle _headerRect;
        private Rectangle _gridRect;
        private Rectangle _gridFrameRect;
        private Rectangle _buttonAreaRect;
        private Rectangle _footerRect;
        private Rectangle _closeButtonRect;
        private Rectangle _repairButtonRect;
        private Rectangle _repairAllButtonRect;
        private bool _repairButtonHovered;
        private bool _repairAllButtonHovered;

        private RenderTarget2D _staticSurface;
        private bool _staticSurfaceDirty = true;

        private SpriteFont _font;
        private CharacterState _characterState;

        private InventoryItem _hoveredItem;

        /// <summary>目前被按著的 BUY 是第幾列（-1 = 沒有）。按下與放開要在同一顆上才算數。</summary>
        private int _mobilePressedBuyIndex = -1;

        /// <summary>清單拖曳捲動的起點。</summary>
        private int _mobileDragStartY = int.MinValue;
        private int _mobileDragStartScrollRow;
        private Point _hoveredSlot = new(-1, -1);
        private GameTime _currentGameTime;

        private bool _wasVisible;
        private bool _closeRequestSent;
        private bool _closeHovered;
        private bool _pendingShow;
        private bool _warmupComplete;

        // Drag support
        private bool _isDragging;
        private Point _dragOffset;
        private DateTime _lastClickTime = DateTime.MinValue;

        // Repair mode
        private ShopMode _shopMode = ShopMode.BuyAndSell;
        private bool _isRepairShop = false;

        private NpcShopControl()
        {
            BuildLayoutMetrics();

            ControlSize = new Point(WINDOW_WIDTH, WindowHeight);
            ViewSize = ControlSize;
            AutoViewSize = false;
            Interactive = true;
            Visible = false;
            Align = ControlAlign.VerticalCenter | ControlAlign.Left;

            EnsureCharacterState();
        }

        public override bool NonDisposable => true;
        public static NpcShopControl Instance => _instance ??= new NpcShopControl();
        public static bool IsOpen => _instance?.Visible == true;

        /// <summary>
        /// Forces immediate position calculation based on Align property.
        /// Call this before showing the control to prevent position flickering.
        /// </summary>
        private void ForceAlignNow()
        {
            if (MobileUi.IsMobile)
            {
                // 靠右放，把左半邊整片留給背包（見 InventoryControl.SetCompactLayout）。
                //
                // 原本是 VerticalCenter | Left，也就是貼著畫面最左邊 —— 關閉鈕
                // 因此落在螢幕左上角的圓角上，看得到卻點不到。
                X = Math.Max(MobileUi.LeftEdge, MobileUi.RightEdge - WINDOW_WIDTH);
                Y = Math.Max(MobileUi.CornerInset, (UiScaler.VirtualSize.Y - WindowHeight) / 2);
                return;
            }

            if (Parent == null || Align == ControlAlign.None)
                return;

            const int padding = 20;

            if (Align.HasFlag(ControlAlign.Top))
                Y = padding;
            else if (Align.HasFlag(ControlAlign.Bottom))
                Y = Parent.DisplaySize.Y - DisplaySize.Y - padding;
            else if (Align.HasFlag(ControlAlign.VerticalCenter))
                Y = (Parent.DisplaySize.Y / 2) - (DisplaySize.Y / 2);

            if (Align.HasFlag(ControlAlign.Left))
                X = padding;
            else if (Align.HasFlag(ControlAlign.Right))
                X = Parent.DisplaySize.X - DisplaySize.X - padding;
            else if (Align.HasFlag(ControlAlign.HorizontalCenter))
                X = (Parent.DisplaySize.X / 2) - (DisplaySize.X / 2);
        }

        private void BuildLayoutMetrics()
        {
            if (MobileUi.IsMobile)
            {
                BuildMobileLayoutMetrics();
                return;
            }

            int buttonAreaHeight = _isRepairShop ? BUTTON_AREA_HEIGHT : 0;

            _headerRect = new Rectangle(0, 0, WINDOW_WIDTH, HEADER_HEIGHT);

            int gridFrameX = WINDOW_MARGIN;
            int gridFrameY = HEADER_HEIGHT;
            int gridFrameWidth = GRID_WIDTH + GRID_PADDING * 2;
            int gridFrameHeight = SECTION_HEADER_HEIGHT + GRID_PADDING * 2 + GRID_HEIGHT;
            _gridFrameRect = new Rectangle(gridFrameX, gridFrameY, gridFrameWidth, gridFrameHeight);

            _gridRect = new Rectangle(
                gridFrameX + GRID_PADDING,
                gridFrameY + SECTION_HEADER_HEIGHT + GRID_PADDING,
                GRID_WIDTH,
                GRID_HEIGHT);

            _buttonAreaRect = new Rectangle(WINDOW_MARGIN, _gridFrameRect.Bottom + 2, _gridFrameRect.Width, buttonAreaHeight);
            _footerRect = new Rectangle(WINDOW_MARGIN, _buttonAreaRect.Bottom + 4, _gridFrameRect.Width, FOOTER_HEIGHT - 8);
            // 關閉鈕放<b>左上角</b>：螢幕右上角是六顆介面按鈕（MENU / CHAR / BAG …），
            // 視窗的關閉鈕再放右上角就會疊在同一塊區域，拇指分不開。
            // 遊戲內所有視窗一致，見 docs/手機遊戲界面規格.md。
            // 統一尺寸：26x22 只有 14 pt，得瞄準才按得到。見 MobileUi.CloseButtonSize。
            _closeButtonRect = MobileUi.IsMobile
                ? new Rectangle(12, (HEADER_HEIGHT - MobileUi.CloseButtonSize) / 2, MobileUi.CloseButtonSize, MobileUi.CloseButtonSize)
                : new Rectangle(12, 10, 26, 22);

        // Repair buttons in button area
            int buttonWidth = 100;
            int buttonHeight = 29;
            int buttonSpacing = 10;
            int buttonY = _buttonAreaRect.Y + (_buttonAreaRect.Height - buttonHeight) / 2;
            int startX = _buttonAreaRect.X + 10;

            _repairButtonRect = new Rectangle(startX, buttonY, buttonWidth, buttonHeight);
            _repairAllButtonRect = new Rectangle(startX + buttonWidth + buttonSpacing, buttonY, buttonWidth, buttonHeight);
        }

        /// <summary>
        /// 手機的版面：標題列 + 一行一件的清單 + 底列（金幣）。
        ///
        /// 視窗靠<b>右</b>放，把左半邊整片留給背包（見 InventoryControl.SetCompactLayout）——
        /// 兩個都是寬視窗，疊在一起就誰也點不到。
        /// </summary>
        private void BuildMobileLayoutMetrics()
        {
            int buttonAreaHeight = _isRepairShop ? BUTTON_AREA_HEIGHT : 0;
            int width = WINDOW_WIDTH;
            int height = WindowHeight;

            _headerRect = new Rectangle(0, 0, width, HEADER_HEIGHT);

            int listTop = HEADER_HEIGHT + 8;
            int listBottom = height - WINDOW_MARGIN - FOOTER_HEIGHT - buttonAreaHeight;
            int listHeight = Math.Max(MobileRowHeight, listBottom - listTop);

            _mobileListRect = new Rectangle(WINDOW_MARGIN, listTop, width - WINDOW_MARGIN * 2, listHeight);
            _mobileVisibleRows = Math.Max(1, (listHeight + MobileRowGap) / (MobileRowHeight + MobileRowGap));
            _mobileScrollRow = Math.Clamp(_mobileScrollRow, 0, MaxMobileScrollRow);

            // 桌面才有的格線區在手機上不存在，但仍有程式讀它們 —— 給空矩形。
            _gridFrameRect = Rectangle.Empty;
            _gridRect = Rectangle.Empty;

            _buttonAreaRect = new Rectangle(WINDOW_MARGIN, _mobileListRect.Bottom + 2, _mobileListRect.Width, buttonAreaHeight);
            _footerRect = new Rectangle(WINDOW_MARGIN, _buttonAreaRect.Bottom + 4, _mobileListRect.Width, FOOTER_HEIGHT - 8);
            _closeButtonRect = MobileUi.WindowCloseButtonRect(new Rectangle(0, 0, width, height));

            int buttonWidth = 100;
            int buttonHeight = 29;
            int buttonSpacing = 10;
            int buttonY = _buttonAreaRect.Y + (_buttonAreaRect.Height - buttonHeight) / 2;
            int startX = _buttonAreaRect.X + 10;
            _repairButtonRect = new Rectangle(startX, buttonY, buttonWidth, buttonHeight);
            _repairAllButtonRect = new Rectangle(startX + buttonWidth + buttonSpacing, buttonY, buttonWidth, buttonHeight);
        }

        private int MaxMobileScrollRow => Math.Max(0, _items.Count - _mobileVisibleRows);

        /// <summary>第 index 件商品（絕對索引）在畫面上的整列矩形；不在可見範圍內回傳空。</summary>
        private Rectangle GetMobileRowRect(int index)
        {
            int visibleIndex = index - _mobileScrollRow;
            if (visibleIndex < 0 || visibleIndex >= _mobileVisibleRows)
                return Rectangle.Empty;

            return new Rectangle(
                DisplayRectangle.X + _mobileListRect.X,
                DisplayRectangle.Y + _mobileListRect.Y + visibleIndex * (MobileRowHeight + MobileRowGap),
                _mobileListRect.Width,
                MobileRowHeight);
        }

        /// <summary>該列的 BUY 鈕。所有列共用同一個尺寸與同一條右對齊線。</summary>
        private static Rectangle GetMobileBuyRect(Rectangle row)
            => new(row.Right - 12 - MobileBuyWidth,
                   row.Y + (row.Height - MobileBuyHeight) / 2,
                   MobileBuyWidth,
                   MobileBuyHeight);


        public override async System.Threading.Tasks.Task Load()
        {
            await base.Load();
            _font = GraphicsManager.Instance.Font;
            InvalidateStaticSurface();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            EnsureCharacterState();

            // Handle deferred show - wait one frame after warmup to avoid black screen
            if (_pendingShow && !Visible)
            {
                if (_warmupComplete)
                {
                    // Warmup done in previous frame, now safe to show
                    Visible = true;
                    BringToFront();

                    // 背包切成窄版面並靠左，商店靠右 —— 兩個都是寬視窗，
                    // 疊在一起就誰也點不到（使用者回報「無法操作」）。
                    InventoryControl.Instance?.SetCompactLayout(true);

                    BuildLayoutMetrics();
                    ForceAlignNow();
                    InvalidateStaticSurface();

                    SoundController.Instance.PlayBuffer("Sound/iCreateWindow.wav");
                    _pendingShow = false;
                    _warmupComplete = false;
                }
                else
                {
                    // Do warmup this frame, show next frame
                    WarmupTexturesSync();
                    InvalidateStaticSurface();
                    EnsureStaticSurface();
                    _warmupComplete = true;
                }
            }

            if (Visible)
            {
                _currentGameTime = gameTime;

                if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Escape) &&
                    MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Escape))
                {
                    Visible = false;
                    HandleVisibilityLost();
                    _wasVisible = false;
                    return;
                }

                // Handle 'L' key for repair mode toggle (only if repair shop and no dragged item)
                if (_isRepairShop &&
                    MuGame.Instance.Keyboard.IsKeyDown(Keys.L) &&
                    MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.L))
                {
                    // Only toggle if not dragging an item
                    if (InventoryControl.Instance?.GetDraggedItem() == null)
                    {
                        ToggleRepairMode();
                        SoundController.Instance.PlayBuffer("Sound/iButton.wav");
                    }
                }

                Point mousePos = MuGame.Instance.UiMouseState.Position;
                bool leftPressed = MuGame.Instance.UiMouseState.LeftButton == ButtonState.Pressed;
                bool leftJustPressed = leftPressed && MuGame.Instance.PrevUiMouseState.LeftButton == ButtonState.Released;
                bool leftJustReleased = !leftPressed && MuGame.Instance.PrevUiMouseState.LeftButton == ButtonState.Pressed;

                UpdateChromeHover(mousePos);

                // Handle close button
                if (leftJustPressed && _closeHovered)
                {
                    Visible = false;
                    HandleVisibilityLost();
                    return;
                }

                // Handle repair buttons (only if repair shop)
                if (_isRepairShop && leftJustPressed)
                {
                    if (_repairButtonHovered)
                    {
                        // Toggle repair mode
                        ToggleRepairMode();
                        SoundController.Instance.PlayBuffer("Sound/iButton.wav");
                        return;
                    }
                    else if (_repairAllButtonHovered)
                    {
                        // Repair all items
                        var svc = MuGame.Network?.GetCharacterService();
                        if (svc != null)
                        {
                            _ = svc.SendRepairItemRequestAsync(0xFF, false); // 0xFF = repair all
                            SoundController.Instance.PlayBuffer("Sound/iButton.wav");
                        }
                        return;
                    }
                }

                // Handle window dragging
                if (leftJustPressed && IsMouseOverDragArea(mousePos) && !_isDragging)
                {
                    DateTime now = DateTime.Now;
                    if ((now - _lastClickTime).TotalMilliseconds < 500)
                    {
                        // Double-click to reset position
                        Align = ControlAlign.None;
                        _lastClickTime = DateTime.MinValue;
                    }
                    else
                    {
                        _isDragging = true;
                        _dragOffset = new Point(mousePos.X - X, mousePos.Y - Y);
                        Align = ControlAlign.None;
                        _lastClickTime = now;
                    }
                }
                else if (leftJustReleased && _isDragging)
                {
                    _isDragging = false;
                }
                else if (_isDragging && leftPressed)
                {
                    X = mousePos.X - _dragOffset.X;
                    Y = mousePos.Y - _dragOffset.Y;
                }

                if (!_isDragging)
                {
                    UpdateHoverState();
                    HandleMouseInput();
                }
            }
            else if (_wasVisible)
            {
                HandleVisibilityLost();
            }

            _wasVisible = Visible;
        }

        private bool IsMouseOverDragArea(Point mousePos)
        {
            Rectangle headerScreen = Translate(_headerRect);
            Rectangle closeScreen = Translate(_closeButtonRect);
            return headerScreen.Contains(mousePos) && !closeScreen.Contains(mousePos);
        }

        private void UpdateChromeHover(Point mousePos)
        {
            var closeRect = Translate(_closeButtonRect);
            _closeHovered = closeRect.Contains(mousePos);

            // Handle repair button hover (only show if repair shop)
            if (_isRepairShop)
            {
                var repairRect = Translate(_repairButtonRect);
                var repairAllRect = Translate(_repairAllButtonRect);
                _repairButtonHovered = repairRect.Contains(mousePos);
                _repairAllButtonHovered = repairAllRect.Contains(mousePos);
            }
            else
            {
                _repairButtonHovered = false;
                _repairAllButtonHovered = false;
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible) return;

            EnsureStaticSurface();

            var gm = GraphicsManager.Instance;
            var spriteBatch = gm?.Sprite;
            if (spriteBatch == null) return;

            SpriteBatchScope? scope = null;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                scope = new SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, transform: UiScaler.SpriteTransform);
            }

            try
            {
                if (_staticSurface != null && !_staticSurface.IsDisposed)
                {
                    spriteBatch.Draw(_staticSurface, DisplayRectangle, Color.White * Alpha);
                }

                var pixel = GraphicsManager.Instance.Pixel;

                // 手機沒有格線（清單版面），停留效果由每一列自己畫。
                if (!MobileUi.IsMobile)
                {
                    ItemGridRenderHelper.DrawGridOverlays(spriteBatch, pixel, DisplayRectangle, _gridRect, _hoveredItem, _hoveredSlot,
                                                          SHOP_SQUARE_WIDTH, SHOP_SQUARE_HEIGHT, Theme.SlotHover, Theme.Accent, Alpha);
                }
                DrawShopItems(spriteBatch);
                DrawCloseButton(spriteBatch);
                if (_isRepairShop)
                {
                    DrawRepairButtons(spriteBatch);
                }
            }
            finally
            {
                scope?.Dispose();
            }
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible || _hoveredItem == null) return;

            var gm = GraphicsManager.Instance;
            var spriteBatch = gm?.Sprite;
            if (spriteBatch == null) return;

            SpriteBatchScope? scope = null;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                scope = new SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, transform: UiScaler.SpriteTransform);
            }

            try
            {
                DrawTooltip(spriteBatch);
            }
            finally
            {
                scope?.Dispose();
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            if (_characterState != null)
            {
                _characterState.ShopItemsChanged -= RefreshShopContent;
                _characterState = null;
            }

            Client.Main.Graphics.UiRenderTargetPool.Return(_staticSurface);
            _staticSurface = null;
        }

        protected override void OnScreenSizeChanged()
        {
            base.OnScreenSizeChanged();
            InvalidateStaticSurface();
        }

        // ═══════════════════════════════════════════════════════════════
        // DRAWING PRIMITIVES
        // ═══════════════════════════════════════════════════════════════

        private void DrawWindowBackground(SpriteBatch spriteBatch, Rectangle rect)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            if (MobileUi.IsMobile)
            {
                // 和登入、選伺服器、背包同一個面板：半透明底 + 一條細框。
                // 桌面那套（外框 + 漸層 + 內框高光 + 四角托架）在手機上
                // 只是把一個面板拆成五條互相干擾的線。
                MobileUi.DrawPanel(spriteBatch, rect);
                return;
            }

            spriteBatch.Draw(pixel, rect, Theme.BorderOuter);

            var innerRect = new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4);
            UiDrawHelper.DrawVerticalGradient(spriteBatch, innerRect, Theme.BgDark, Theme.BgDarkest);

            spriteBatch.Draw(pixel, new Rectangle(innerRect.X, innerRect.Y, innerRect.Width, 1), Theme.BorderInner * 0.5f);
            spriteBatch.Draw(pixel, new Rectangle(innerRect.X, innerRect.Y, 1, innerRect.Height), Theme.BorderInner * 0.3f);

            UiDrawHelper.DrawCornerAccents(spriteBatch, rect, Theme.Accent * 0.4f);
        }

        private void DrawPanel(SpriteBatch spriteBatch, Rectangle rect, Color bgColor, bool withBorder = true)
        {
            UiDrawHelper.DrawPanel(spriteBatch, rect, bgColor,
                withBorder ? Theme.BorderInner * 0.8f : (Color?)null,
                withBorder ? Theme.BorderOuter : (Color?)null,
                withBorder ? Theme.BorderInner * 0.6f : null);
        }

        private void DrawSectionHeader(SpriteBatch spriteBatch, string title, int x, int y, int width)
        {
            if (_font == null) return;

            float scale = MobileUi.IsMobile ? MobileUi.ScaleFor(MobileUi.TextCaption) : 0.32f;
            Vector2 size = _font.MeasureString(title) * scale;
            float textX = x + (width - size.X) / 2;

            spriteBatch.DrawString(_font, title, new Vector2(textX + 1, y + 1), Color.Black * 0.6f,
                                   0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(_font, title, new Vector2(textX, y), Theme.TextGold,
                                   0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        // ═══════════════════════════════════════════════════════════════
        // STATIC SURFACE RENDERING
        // ═══════════════════════════════════════════════════════════════

        private void EnsureStaticSurface()
        {
            if (!_staticSurfaceDirty && _staticSurface != null && !_staticSurface.IsDisposed)
                return;

            var gd = GraphicsManager.Instance?.GraphicsDevice;
            if (gd == null) return;

            Client.Main.Graphics.UiRenderTargetPool.Return(_staticSurface);
            _staticSurface = Client.Main.Graphics.UiRenderTargetPool.Rent(gd, WINDOW_WIDTH, WindowHeight);

            // 切換 render target 之前必須先把外層批次送出去，否則畫面上排隊中的
            // 東西會被畫進這張表面裡（見 SpriteBatchScope.BeginRenderTarget）。
            using var __rtSection = SpriteBatchScope.BeginRenderTarget(gd, _staticSurface);
            gd.Clear(Color.Transparent);

            var spriteBatch = GraphicsManager.Instance.Sprite;
            using (new SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp))
            {
                DrawStaticElements(spriteBatch);
            }

            _staticSurfaceDirty = false;
        }

        private void InvalidateStaticSurface() => _staticSurfaceDirty = true;

        private void DrawStaticElements(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            var fullRect = new Rectangle(0, 0, WINDOW_WIDTH, WindowHeight);
            DrawWindowBackground(spriteBatch, fullRect);
            DrawModernHeader(spriteBatch);

            // 手機沒有格線區：清單的每一列自己畫底（見 DrawMobileShopList）。
            if (!MobileUi.IsMobile)
                DrawModernGridSection(spriteBatch);

            DrawModernButtonArea(spriteBatch);
            DrawModernFooter(spriteBatch);
        }

        private void DrawModernHeader(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            var headerBg = new Rectangle(8, 6, WINDOW_WIDTH - 16, HEADER_HEIGHT - 8);
            DrawPanel(spriteBatch, headerBg, Theme.BgMid);

            // 標題上緣那兩條裝飾橫槓：手機不畫，標題列本身已經有底色了。
            if (!MobileUi.IsMobile)
            {
                spriteBatch.Draw(pixel, new Rectangle(20, 8, WINDOW_WIDTH - 40, 2), Theme.Accent * 0.8f);
                spriteBatch.Draw(pixel, new Rectangle(30, 10, WINDOW_WIDTH - 60, 1), Theme.AccentDim * 0.4f);
            }

            if (_font != null)
            {
                string title = "NPC SHOP";
                float scale = MobileUi.IsMobile ? MobileUi.ScaleFor(MobileUi.TextTitle) : 0.50f;
                Vector2 size = _font.MeasureString(title) * scale;
                Vector2 pos = new((WINDOW_WIDTH - size.X) / 2, (HEADER_HEIGHT - size.Y) / 2 + 2);

                spriteBatch.Draw(pixel, new Rectangle((int)pos.X - 20, (int)pos.Y - 4, (int)size.X + 40, (int)size.Y + 8),
                                Theme.AccentGlow * 0.3f);

                spriteBatch.DrawString(_font, title, pos + new Vector2(2, 2), Color.Black * 0.5f,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                spriteBatch.DrawString(_font, title, pos, Theme.TextWhite,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }

            int sepY = HEADER_HEIGHT - 2;
            UiDrawHelper.DrawHorizontalGradient(spriteBatch, new Rectangle(20, sepY, (WINDOW_WIDTH - 40) / 2, 1),
                                  Color.Transparent, Theme.BorderInner);
            UiDrawHelper.DrawHorizontalGradient(spriteBatch, new Rectangle(WINDOW_WIDTH / 2, sepY, (WINDOW_WIDTH - 40) / 2, 1),
                                  Theme.BorderInner, Color.Transparent);
        }

        private void DrawModernGridSection(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            DrawSectionHeader(spriteBatch, "ITEMS FOR SALE", _gridFrameRect.X, _gridFrameRect.Y + 4, _gridFrameRect.Width);
            DrawPanel(spriteBatch, _gridFrameRect, Theme.BgMid);

            spriteBatch.Draw(pixel, _gridRect, Theme.SlotBg);

            spriteBatch.Draw(pixel, new Rectangle(_gridRect.X, _gridRect.Y, _gridRect.Width, 2), Color.Black * 0.4f);
            spriteBatch.Draw(pixel, new Rectangle(_gridRect.X, _gridRect.Y, 2, _gridRect.Height), Color.Black * 0.3f);

            Color gridLine = new(40, 48, 60, 100);
            Color gridLineMajor = new(55, 65, 80, 120);

            for (int x = 1; x < SHOP_COLUMNS; x++)
            {
                int lineX = _gridRect.X + x * SHOP_SQUARE_WIDTH;
                bool isMajor = x == SHOP_COLUMNS / 2;
                spriteBatch.Draw(pixel, new Rectangle(lineX, _gridRect.Y, 1, _gridRect.Height), isMajor ? gridLineMajor : gridLine);
            }

            for (int y = 1; y < SHOP_ROWS; y++)
            {
                int lineY = _gridRect.Y + y * SHOP_SQUARE_HEIGHT;
                bool isMajor = y == SHOP_ROWS / 2;
                spriteBatch.Draw(pixel, new Rectangle(_gridRect.X, lineY, _gridRect.Width, 1), isMajor ? gridLineMajor : gridLine);
            }

            spriteBatch.Draw(pixel, new Rectangle(_gridRect.X, _gridRect.Bottom - 1, _gridRect.Width, 1), Theme.BorderHighlight * 0.2f);
            spriteBatch.Draw(pixel, new Rectangle(_gridRect.Right - 1, _gridRect.Y, 1, _gridRect.Height), Theme.BorderHighlight * 0.15f);
        }

        private void DrawModernButtonArea(SpriteBatch spriteBatch)
        {
            if (_buttonAreaRect.Height == 0) return;

            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            DrawPanel(spriteBatch, _buttonAreaRect, Theme.BgMid);
        }

        private void DrawModernFooter(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            int sepY = _footerRect.Y - 4;
            UiDrawHelper.DrawHorizontalGradient(spriteBatch, new Rectangle(30, sepY, (WINDOW_WIDTH - 60) / 2, 1),
                                  Color.Transparent, Theme.Accent * 0.4f);
            UiDrawHelper.DrawHorizontalGradient(spriteBatch, new Rectangle(WINDOW_WIDTH / 2, sepY, (WINDOW_WIDTH - 60) / 2, 1),
                                  Theme.Accent * 0.4f, Color.Transparent);

            DrawPanel(spriteBatch, _footerRect, Theme.BgMid);

            if (_font != null)
            {
                string hint = _isRepairShop
                    ? (_shopMode == ShopMode.Repair ? "Repair mode - Click items" : "Buy/Sell - Press 'L' to repair")
                    : "Click item to buy";
                float scale = MobileUi.IsMobile ? MobileUi.ScaleFor(MobileUi.TextLabel) : 0.38f;
                Vector2 size = _font.MeasureString(hint) * scale;
                int hintX = _footerRect.X;
                Vector2 pos = new(hintX + ((_footerRect.Width - (hintX - _footerRect.X)) - size.X) / 2,
                                  _footerRect.Y + (_footerRect.Height - size.Y) / 2);

                spriteBatch.DrawString(_font, hint, pos + Vector2.One, Color.Black * 0.5f,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                spriteBatch.DrawString(_font, hint, pos, Theme.TextGold,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
        }

        private void DrawRepairButtons(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null || _font == null) return;

            // Draw Repair button
            var repairRect = Translate(_repairButtonRect);
            Color repairBg = _shopMode == ShopMode.Repair ? Theme.AccentDim : Theme.BgLight;
            Color repairBorder = _repairButtonHovered ? Theme.Accent : Theme.BorderInner;
            UiDrawHelper.DrawPanel(spriteBatch, repairRect, repairBg, repairBorder, Theme.BorderOuter);

            // Draw "Repair item" text for Repair
            string repairText = "Repair item";
            float scale = MobileUi.IsMobile ? MobileUi.ScaleFor(MobileUi.TextLabel) : 0.4f;
            Vector2 textSize = _font.MeasureString(repairText) * scale;
            Vector2 textPos = new(repairRect.X + (repairRect.Width - textSize.X) / 2,
                                  repairRect.Y + (repairRect.Height - textSize.Y) / 2);
            spriteBatch.DrawString(_font, repairText, textPos + Vector2.One, Color.Black * 0.6f,
                                   0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(_font, repairText, textPos, Theme.TextWhite,
                                   0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

            // Draw Repair All button
            var repairAllRect = Translate(_repairAllButtonRect);
            Color repairAllBorder = _repairAllButtonHovered ? Theme.Accent : Theme.BorderInner;
            UiDrawHelper.DrawPanel(spriteBatch, repairAllRect, Theme.BgLight, repairAllBorder, Theme.BorderOuter);

            // Draw "Repair all" text for Repair All
            string allText = "Repair all";
            scale = 0.4f;
            textSize = _font.MeasureString(allText) * scale;
            textPos = new(repairAllRect.X + (repairAllRect.Width - textSize.X) / 2,
                          repairAllRect.Y + (repairAllRect.Height - textSize.Y) / 2);
            spriteBatch.DrawString(_font, allText, textPos + Vector2.One, Color.Black * 0.6f,
                                   0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(_font, allText, textPos, Theme.TextWhite,
                                   0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        // ═══════════════════════════════════════════════════════════════
        // DYNAMIC DRAWING
        // ═══════════════════════════════════════════════════════════════

        private void DrawCloseButton(SpriteBatch spriteBatch)
        {
            if (MobileUi.IsMobile)
            {
                // 外觀也統一：一塊底 + 兩條線畫的叉。原本每個視窗各畫各的 X，
                // 粗細、顏色、有沒有底都不一樣。
                MobileUi.DrawCloseGlyph(spriteBatch, Translate(_closeButtonRect), _closeHovered);
                return;
            }

            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            var rect = Translate(_closeButtonRect);
            Color btnColor = _closeHovered ? Theme.Accent : Theme.TextGray;

            // Draw X symbol
            int cx = rect.X + rect.Width / 2;
            int cy = rect.Y + rect.Height / 2;
            int halfSize = 6;
            int thickness = 2;

            // Draw diagonal lines for X
            for (int i = -halfSize; i <= halfSize; i++)
            {
                spriteBatch.Draw(pixel, new Rectangle(cx + i - thickness / 2, cy + i - thickness / 2, thickness, thickness), btnColor);
                spriteBatch.Draw(pixel, new Rectangle(cx + i - thickness / 2, cy - i - thickness / 2, thickness, thickness), btnColor);
            }
        }

        private void DrawShopItems(SpriteBatch spriteBatch)
        {
            if (MobileUi.IsMobile)
            {
                DrawMobileShopList(spriteBatch);
                return;
            }

            var font = _font ?? GraphicsManager.Instance.Font;
            Point gridOrigin = new(DisplayRectangle.X + _gridRect.X, DisplayRectangle.Y + _gridRect.Y);
            var pixel = GraphicsManager.Instance.Pixel;
            _jewelEntries.Clear();

            foreach (var item in _items)
            {
                var rect = new Rectangle(
                    gridOrigin.X + item.GridPosition.X * SHOP_SQUARE_WIDTH,
                    gridOrigin.Y + item.GridPosition.Y * SHOP_SQUARE_HEIGHT,
                    item.Definition.Width * SHOP_SQUARE_WIDTH,
                    item.Definition.Height * SHOP_SQUARE_HEIGHT);

                bool isHovered = item == _hoveredItem;
                Texture2D texture = ResolveItemTexture(item, rect.Width, rect.Height, isHovered);

                // Glow similar to inventory/vault
                Color glowColor = ItemUiHelper.GetItemGlowColor(item, GlowPalette);
                if (glowColor.A > 0 || isHovered)
                {
                    Color finalGlow = isHovered ? Color.Lerp(glowColor, Theme.Accent, 0.4f) : glowColor;
                    finalGlow.A = (byte)Math.Min(255, finalGlow.A + (isHovered ? 40 : 0));
                    ItemUiHelper.DrawItemGlow(spriteBatch, pixel, rect, finalGlow);
                }

                // Cell background
                if (pixel != null)
                {
                    var bgRect = new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
                    spriteBatch.Draw(pixel, bgRect, isHovered ? Theme.SlotHover : Theme.SlotBg);
                }

                if (texture != null)
                {
                    spriteBatch.Draw(texture, rect, Color.White * Alpha);

                    if (JewelShineOverlay.ShouldShine(item))
                    {
                        _jewelEntries.Add((item, rect));
                    }
                }
                else if (pixel != null)
                {
                    ItemGridRenderHelper.DrawItemPlaceholder(spriteBatch, pixel, font, rect, item, Theme.BgLight, Theme.TextGray * 0.8f);
                }

                if (font != null && item.Definition.BaseDurability == 0 && item.Definition.MagicDurability == 0 && item.Durability > 1)
                {
                    ItemGridRenderHelper.DrawItemStackCount(spriteBatch, font, rect, item.Durability, Theme.TextGold, Alpha);
                }

                ItemGridRenderHelper.DrawItemLevelBadge(spriteBatch, GraphicsManager.Instance.Pixel, font, rect, item.Details.Level,
               lvl => lvl >= 9 ? Theme.AccentBright :
                      lvl >= 7 ? Theme.Accent :
                      lvl >= 4 ? Theme.AccentDim :
                      Theme.TextGray,
               new Color(0, 0, 0, 180));

                if (_jewelEntries.Count > 0)
                {
                    JewelShineOverlay.DrawBatch(spriteBatch, _jewelEntries, _currentGameTime, Alpha, UiScaler.SpriteTransform);
                }
            }
        }

        /// <summary>
        /// 手機的商店清單：一行一件，左圖示、中名稱與價格、右一顆 BUY。
        ///
        /// 格線放不下名稱與價格，而那正是買東西時唯一要看的兩件事 ——
        /// 玩家不該為了知道「這是什麼、多少錢」而先長按一個 40 px 的小方塊。
        /// </summary>
        private void DrawMobileShopList(SpriteBatch spriteBatch)
        {
            var font = _font ?? GraphicsManager.Instance.Font;
            var pixel = GraphicsManager.Instance.Pixel;
            if (font == null || pixel == null)
                return;

            _jewelEntries.Clear();

            for (int i = 0; i < _items.Count; i++)
            {
                var row = GetMobileRowRect(i);
                if (row.IsEmpty)
                    continue;

                var item = _items[i];
                bool isHovered = item == _hoveredItem;

                spriteBatch.Draw(pixel, row, (isHovered ? Theme.SlotHover : MobileUi.FieldFill * 0.55f) * Alpha);

                // 圖示
                var iconRect = new Rectangle(row.X + 6, row.Y + (row.Height - MobileIconSize) / 2,
                                             MobileIconSize, MobileIconSize);
                var texture = ResolveItemTexture(item, iconRect.Width, iconRect.Height, isHovered);
                if (texture != null)
                {
                    spriteBatch.Draw(texture, iconRect, Color.White * Alpha);
                    if (JewelShineOverlay.ShouldShine(item))
                        _jewelEntries.Add((item, iconRect));
                }
                else
                {
                    ItemGridRenderHelper.DrawItemPlaceholder(spriteBatch, pixel, font, iconRect, item,
                        Theme.BgLight, Theme.TextGray * 0.8f);
                }

                // 名稱與價格
                var buyRect = GetMobileBuyRect(row);
                int textX = iconRect.Right + 12;
                int textWidth = Math.Max(40, buyRect.X - 12 - textX);

                var tooltipLines = ItemUiHelper.BuildTooltipLines(item);
                string name = tooltipLines.Count > 0 ? tooltipLines[0].text : (item.Definition?.Name ?? "?");
                name = FitToWidth(font, name, textWidth, MobileUi.TextBody);
                MobileUi.DrawText(spriteBatch, font, name,
                    new Vector2(textX, row.Y + 12), MobileUi.TextBody, MobileUi.TextPrimary * Alpha);

                int price = ItemPriceCalculator.CalculateBuyPrice(item);
                MobileUi.DrawText(spriteBatch, font,
                    price.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " Zen",
                    new Vector2(textX, row.Y + 40), MobileUi.TextLabel, Theme.TextGold * Alpha);

                // BUY —— 每一列同一個尺寸、同一條右對齊線
                bool pressed = _mobilePressedBuyIndex == i;
                spriteBatch.Draw(pixel, buyRect,
                    (pressed ? MobileUi.TitleBarFill * 1.6f : MobileUi.TitleBarFill * MobileUi.PanelAlpha) * Alpha);
                MobileUi.DrawTextCentered(spriteBatch, font, "BUY", buyRect,
                    MobileUi.TextHeading, MobileUi.TextPrimary * Alpha);
            }

            if (_jewelEntries.Count > 0)
                JewelShineOverlay.DrawBatch(spriteBatch, _jewelEntries, _currentGameTime, Alpha, UiScaler.SpriteTransform);

            DrawMobileScrollbar(spriteBatch, pixel);
        }

        private void DrawMobileScrollbar(SpriteBatch spriteBatch, Texture2D pixel)
        {
            if (MaxMobileScrollRow <= 0)
                return;

            var track = new Rectangle(
                DisplayRectangle.X + _mobileListRect.Right - 4,
                DisplayRectangle.Y + _mobileListRect.Y,
                4, _mobileListRect.Height);

            float visibleRatio = _mobileVisibleRows / (float)Math.Max(1, _items.Count);
            int thumbHeight = Math.Max(24, (int)(track.Height * visibleRatio));
            int travel = Math.Max(0, track.Height - thumbHeight);
            int thumbY = track.Y + (int)(travel * (_mobileScrollRow / (float)MaxMobileScrollRow));

            MobileUi.DrawScrollbar(spriteBatch, track,
                new Rectangle(track.X, thumbY, track.Width, thumbHeight), false);
        }

        /// <summary>把名稱截到欄寬以內。字型沒有 U+2026，截斷一律用 ".."。</summary>
        private static string FitToWidth(SpriteFont font, string text, int maxWidth, float sizePx)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0)
                return text ?? string.Empty;

            float scale = MobileUi.ScaleFor(sizePx);
            float width = font.MeasureString(text).X * scale;
            if (width <= maxWidth)
                return text;

            int keep = Math.Max(1, (int)(text.Length * (maxWidth / width)) - 2);
            return text.Substring(0, keep).TrimEnd() + "..";
        }

        private void DrawTooltip(SpriteBatch spriteBatch)
        {
            // 手機的清單每一列已經寫著名稱與價格，工具提示只會把旁邊的東西蓋掉。
            if (MobileUi.IsMobile) return;

            if (_hoveredItem == null || _font == null) return;

            var lines = ItemUiHelper.BuildTooltipLines(_hoveredItem);
            int buyPrice = ItemPriceCalculator.CalculateBuyPrice(_hoveredItem);
            if (buyPrice > 0)
            {
                lines.Add(($"Buy Price: {buyPrice} Zen", Theme.TextGold));
            }
            float scale = MobileUi.IsMobile ? MobileUi.ScaleFor(MobileUi.TextBody) : 0.44f;
            const int lineSpacing = 4;
            const int paddingX = 14;
            const int paddingY = 12;

            int maxWidth = 0;
            int totalHeight = 0;
            foreach (var (text, _) in lines)
            {
                Vector2 sz = _font.MeasureString(text) * scale;
                maxWidth = Math.Max(maxWidth, (int)MathF.Ceiling(sz.X));
                totalHeight += (int)MathF.Ceiling(sz.Y) + lineSpacing;
            }
            totalHeight += 6;

            int tooltipWidth = maxWidth + paddingX * 2;
            int tooltipHeight = totalHeight + paddingY * 2;

            Point mouse = MuGame.Instance.UiMouseState.Position;
            var itemRect = new Rectangle(
                DisplayRectangle.X + _gridRect.X + _hoveredItem.GridPosition.X * SHOP_SQUARE_WIDTH,
                DisplayRectangle.Y + _gridRect.Y + _hoveredItem.GridPosition.Y * SHOP_SQUARE_HEIGHT,
                _hoveredItem.Definition.Width * SHOP_SQUARE_WIDTH,
                _hoveredItem.Definition.Height * SHOP_SQUARE_HEIGHT);

            Rectangle tooltipRect = new(mouse.X + 16, mouse.Y + 16, tooltipWidth, tooltipHeight);
            Rectangle screenBounds = new(0, 0, UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y);

            if (tooltipRect.Intersects(itemRect))
            {
                tooltipRect.X = itemRect.X - tooltipWidth - 8;
                tooltipRect.Y = itemRect.Y;

                if (tooltipRect.X < 10 || tooltipRect.Intersects(itemRect))
                {
                    tooltipRect.X = itemRect.X;
                    tooltipRect.Y = itemRect.Y - tooltipHeight - 8;

                    if (tooltipRect.Y < 10)
                    {
                        tooltipRect.X = itemRect.X;
                        tooltipRect.Y = itemRect.Bottom + 8;
                    }
                }
            }

            tooltipRect.X = Math.Clamp(tooltipRect.X, 10, screenBounds.Right - tooltipRect.Width - 10);
            tooltipRect.Y = Math.Clamp(tooltipRect.Y, 10, screenBounds.Bottom - tooltipRect.Height - 10);

            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            var shadowRect = new Rectangle(tooltipRect.X + 4, tooltipRect.Y + 4, tooltipRect.Width, tooltipRect.Height);
            spriteBatch.Draw(pixel, shadowRect, Color.Black * 0.5f);

            UiDrawHelper.DrawVerticalGradient(spriteBatch, tooltipRect, new Color(20, 24, 32, 252), new Color(12, 14, 18, 254));

            bool isExcellent = _hoveredItem.Details.IsExcellent;
            bool isAncient = _hoveredItem.Details.IsAncient;
            bool isHighLevel = _hoveredItem.Details.Level >= 7;

            Color borderColor = isExcellent ? Theme.GlowExcellent :
                                isAncient ? Theme.GlowAncient :
                                isHighLevel ? Theme.Accent :
                                Theme.TextWhite;

            const int borderThickness = 2;
            spriteBatch.Draw(pixel, new Rectangle(tooltipRect.X, tooltipRect.Y, tooltipRect.Width, borderThickness), borderColor);
            spriteBatch.Draw(pixel, new Rectangle(tooltipRect.X, tooltipRect.Bottom - borderThickness, tooltipRect.Width, borderThickness), borderColor);
            spriteBatch.Draw(pixel, new Rectangle(tooltipRect.X, tooltipRect.Y, borderThickness, tooltipRect.Height), borderColor);
            spriteBatch.Draw(pixel, new Rectangle(tooltipRect.Right - borderThickness, tooltipRect.Y, borderThickness, tooltipRect.Height), borderColor);

            int textY = tooltipRect.Y + paddingY;
            bool firstLine = true;
            foreach (var (text, color) in lines)
            {
                Vector2 textSize = _font.MeasureString(text) * scale;
                int textX = tooltipRect.X + (tooltipRect.Width - (int)textSize.X) / 2;

                spriteBatch.DrawString(_font, text, new Vector2(textX + 1, textY + 1), Color.Black * 0.7f,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                Color lineColor = firstLine ? borderColor : color;
                spriteBatch.DrawString(_font, text, new Vector2(textX, textY), lineColor,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

                textY += (int)textSize.Y + lineSpacing;

                if (firstLine)
                {
                    textY += 2;
                    spriteBatch.Draw(pixel, new Rectangle(tooltipRect.X + 8, textY, tooltipRect.Width - 16, 1), borderColor * 0.3f);
                    textY += 4;
                    firstLine = false;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // INPUT HANDLING
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 手機的商店輸入：BUY 要「按下再放開同一顆」，其餘拖曳就是捲動清單。
        ///
        /// 桌面是「點到商品就買」。那在滑鼠上還行，在觸控上是災難 ——
        /// 想捲清單或只是想看看是什麼東西，手指一碰就付錢了。
        /// </summary>
        private void HandleMobileInput()
        {
            if (IsModalDialogOpen() || Scene?.FocusControl != this)
                return;

            if (InventoryControl.Instance?.GetDraggedItem() != null || VaultControl.Instance?.GetDraggedItem() != null)
                return;

            var mouse = MuGame.Instance.UiMouseState;
            var prev = MuGame.Instance.PrevUiMouseState;
            bool pressed = mouse.LeftButton == ButtonState.Pressed;
            bool justPressed = pressed && prev.LeftButton == ButtonState.Released;
            bool justReleased = !pressed && prev.LeftButton == ButtonState.Pressed;
            Point mousePos = mouse.Position;

            if (justPressed)
            {
                if (!DisplayRectangle.Contains(mousePos))
                    return;

                Scene?.SetMouseInputConsumed();

                _mobilePressedBuyIndex = -1;
                for (int i = 0; i < _items.Count; i++)
                {
                    var row = GetMobileRowRect(i);
                    if (!row.IsEmpty && GetMobileBuyRect(row).Contains(mousePos))
                    {
                        _mobilePressedBuyIndex = i;
                        break;
                    }
                }

                if (_mobilePressedBuyIndex < 0)
                {
                    _mobileDragStartY = mousePos.Y;
                    _mobileDragStartScrollRow = _mobileScrollRow;
                }

                return;
            }

            if (pressed && _mobileDragStartY != int.MinValue)
            {
                int delta = _mobileDragStartY - mousePos.Y;
                int rows = delta / (MobileRowHeight + MobileRowGap);
                _mobileScrollRow = Math.Clamp(_mobileDragStartScrollRow + rows, 0, MaxMobileScrollRow);
                return;
            }

            if (!justReleased)
                return;

            _mobileDragStartY = int.MinValue;

            int buyIndex = _mobilePressedBuyIndex;
            _mobilePressedBuyIndex = -1;

            if (buyIndex < 0 || buyIndex >= _items.Count)
                return;

            var buyRow = GetMobileRowRect(buyIndex);
            if (buyRow.IsEmpty || !GetMobileBuyRect(buyRow).Contains(mousePos))
                return;

            var boughtItem = _items[buyIndex];
            byte mobileSlot = (byte)(boughtItem.GridPosition.Y * SHOP_COLUMNS + boughtItem.GridPosition.X);
            var mobileSvc = MuGame.Network?.GetCharacterService();
            if (mobileSvc != null)
            {
                SoundController.Instance.PlayBuffer("Sound/iButton.wav");
                _ = mobileSvc.SendBuyItemFromNpcRequestAsync(mobileSlot);
            }
        }

        private void HandleMouseInput()
        {
            if (MobileUi.IsMobile)
            {
                HandleMobileInput();
                return;
            }

            var mouse = MuGame.Instance.UiMouseState;
            var prev = MuGame.Instance.PrevUiMouseState;

            bool leftJustPressed = mouse.LeftButton == ButtonState.Pressed &&
                                   prev.LeftButton == ButtonState.Released;

            if (!leftJustPressed) return;

            // Prevent input when a modal dialog is open (e.g., sell confirmation)
            if (IsModalDialogOpen()) return;
            if (Scene?.FocusControl != this) return;

            // Ignore shop clicks while dragging an item from inventory/vault (so a sell drop doesn't auto-buy a shop item)
            if (InventoryControl.Instance?.GetDraggedItem() != null || VaultControl.Instance?.GetDraggedItem() != null) return;

            Point mousePos = mouse.Position;

            if (DisplayRectangle.Contains(mousePos))
            {
                Scene?.SetMouseInputConsumed();
            }

            if (_hoveredItem == null) return;

            byte slot = (byte)(_hoveredItem.GridPosition.Y * SHOP_COLUMNS + _hoveredItem.GridPosition.X);
            var svc = MuGame.Network?.GetCharacterService();
            if (svc != null)
            {
                _ = svc.SendBuyItemFromNpcRequestAsync(slot);
            }
        }

        private void UpdateHoverState()
        {
            var mousePos = MuGame.Instance.UiMouseState.Position;
            _hoveredSlot = ItemGridRenderHelper.GetSlotAtScreenPosition(DisplayRectangle, _gridRect, SHOP_COLUMNS, SHOP_ROWS, SHOP_SQUARE_WIDTH, SHOP_SQUARE_HEIGHT, mousePos);
            _hoveredItem = GetItemAt(mousePos);
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════

        private Rectangle Translate(Rectangle rect)
            => new(DisplayRectangle.X + rect.X, DisplayRectangle.Y + rect.Y, rect.Width, rect.Height);

        private InventoryItem GetItemAt(Point mousePos)
        {
            if (!DisplayRectangle.Contains(mousePos)) return null;

            if (MobileUi.IsMobile)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    var row = GetMobileRowRect(i);
                    if (!row.IsEmpty && row.Contains(mousePos))
                        return _items[i];
                }

                return null;
            }

            Point gridOrigin = new(DisplayRectangle.X + _gridRect.X, DisplayRectangle.Y + _gridRect.Y);

            foreach (var item in _items)
            {
                var rect = new Rectangle(
                    gridOrigin.X + item.GridPosition.X * SHOP_SQUARE_WIDTH,
                    gridOrigin.Y + item.GridPosition.Y * SHOP_SQUARE_HEIGHT,
                    item.Definition.Width * SHOP_SQUARE_WIDTH,
                    item.Definition.Height * SHOP_SQUARE_HEIGHT);

                if (rect.Contains(mousePos)) return item;
            }

            return null;
        }

        private void HandleVisibilityLost()
        {
            // 背包回到完整版面（含資訊欄）。
            InventoryControl.Instance?.SetCompactLayout(false);

            SendCloseNpcRequest();
            _characterState?.ClearShopItems();
            _items.Clear();
            _itemTextureCache.Clear();
            _bmdPreviewCache.Clear();
            _hoveredItem = null;
            _hoveredSlot = new Point(-1, -1);
            _isDragging = false;
            _pendingShow = false;

            // Reset repair mode when closing shop
            _shopMode = ShopMode.BuyAndSell;
            _warmupComplete = false;
        }

        private bool IsModalDialogOpen()
        {
            var scene = Scene;
            if (scene == null) return false;

            var controls = scene.Controls.GetSnapshotArray();
            for (int i = controls.Length - 1; i >= 0; i--)
            {
                if (controls[i] is DialogControl dialog && dialog.Visible)
                {
                    return true;
                }
            }

            return false;
        }

        private void SendCloseNpcRequest()
        {
            if (_closeRequestSent) return;
            _closeRequestSent = true;
            var svc = MuGame.Network?.GetCharacterService();
            if (svc != null)
            {
                _ = svc.SendCloseNpcRequestAsync();
            }
        }

        private void EnsureCharacterState()
        {
            if (_characterState != null) return;

            _characterState = MuGame.Network?.GetCharacterState();
            if (_characterState != null)
            {
                _characterState.ShopItemsChanged += RefreshShopContent;
            }
        }

        private void RefreshShopContent()
        {
            if (_characterState == null) return;

            _items.Clear();
            _itemTextureCache.Clear();
            _bmdPreviewCache.Clear();

            var shopItems = _characterState.GetShopItems();
            int maxSlots = SHOP_COLUMNS * SHOP_ROWS;
            foreach (var kv in shopItems)
            {
                byte slot = kv.Key;
                if (slot >= maxSlots)
                    continue;

                byte[] data = kv.Value;

                int gridX = slot % SHOP_COLUMNS;
                int gridY = slot / SHOP_COLUMNS;

                var def = ItemDatabase.GetItemDefinition(data)
                    ?? new ItemDefinition(0, ItemDatabase.GetItemName(data) ?? "Unknown Item", 1, 1, "Interface/newui_item_box.tga");

                var item = new InventoryItem(def, new Point(gridX, gridY), data);
                if (data.Length > 2)
                {
                    item.Durability = data[2];
                }

                _items.Add(item);
            }

            foreach (var item in _items)
            {
                if (!string.IsNullOrEmpty(item.Definition.TexturePath) &&
                    !item.Definition.TexturePath.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase))
                {
                    _ = TextureLoader.Instance.Prepare(item.Definition.TexturePath);
                }
            }

            if (_items.Count > 0)
            {
                // Align left with padding before showing, then freeze position to avoid auto realignment
                ForceAlignNow();
                Align = ControlAlign.None;
                // Use deferred show - warmup happens in Update(), window shows one frame later
                // to avoid black screen flicker from render target switches during Draw().
                _pendingShow = true;
                _warmupComplete = false;
                _closeRequestSent = false;
                _isDragging = false;
            }
        }

        private void WarmupTexturesSync()
        {
            if (GraphicsManager.Instance?.Sprite == null)
                return;

            foreach (var item in _items)
            {
                int w = item.Definition.Width * SHOP_SQUARE_WIDTH;
                int h = item.Definition.Height * SHOP_SQUARE_HEIGHT;
                _ = ResolveItemTexture(item, w, h, animated: false);
            }
        }

        private Texture2D ResolveItemTexture(InventoryItem item, int width, int height, bool animated)
        {
            if (item?.Definition == null) return null;

            string texturePath = item.Definition.TexturePath;
            if (string.IsNullOrEmpty(texturePath)) return null;

            bool isBmd = texturePath.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase);

            if (!isBmd)
            {
                if (_itemTextureCache.TryGetValue(texturePath, out var cached) && cached != null)
                    return cached;

                var tex = TextureLoader.Instance.GetTexture2D(texturePath);
                if (tex != null) _itemTextureCache[texturePath] = tex;
                return tex;
            }

            bool isHovered = animated;

            // Material animation for non-hovered items (if enabled)
            if (!isHovered && Constants.ENABLE_ITEM_MATERIAL_ANIMATION)
            {
                try
                {
                    var mat = BmdPreviewRenderer.GetMaterialAnimatedPreview(item, width, height, _currentGameTime);
                    if (mat != null)
                    {
                        return mat;
                    }
                }
                catch { }
            }

            if (isHovered)
            {
                try
                {
                    return BmdPreviewRenderer.GetSmoothAnimatedPreview(item, width, height, _currentGameTime);
                }
                catch { return null; }
            }

            var cacheKey = (item, width, height, false);
            if (_bmdPreviewCache.TryGetValue(cacheKey, out var cachedPreview) && cachedPreview != null)
                return cachedPreview;

            try
            {
                var preview = BmdPreviewRenderer.GetPreview(item, width, height);
                if (preview != null)
                {
                    _bmdPreviewCache[cacheKey] = preview;
                }
                return preview;
            }
            catch { return null; }
        }

        // ═══════════════════════════════════════════════════════════════
        // REPAIR MODE
        // ═══════════════════════════════════════════════════════════════

        public ShopMode GetShopMode() => _shopMode;
        public bool IsRepairShop => _isRepairShop;
        public bool IsRepairMode => _shopMode == ShopMode.Repair;

        public void SetRepairShop(bool canRepair)
        {
            _isRepairShop = canRepair;
            if (!canRepair && _shopMode == ShopMode.Repair)
            {
                // If NPC can't repair, reset to buy/sell mode
                _shopMode = ShopMode.BuyAndSell;
            }
            BuildLayoutMetrics();
            var newSize = new Point(WINDOW_WIDTH, WindowHeight);
            ControlSize = newSize;
            ViewSize = newSize;              // <-- KLUCZ: utrzymuj ViewSize = ControlSize gdy AutoViewSize=false
            InvalidateStaticSurface();
        }

        public void ToggleRepairMode()
        {
            if (!_isRepairShop) return;

            if (_shopMode == ShopMode.BuyAndSell)
            {
                _shopMode = ShopMode.Repair;
            }
            else
            {
                _shopMode = ShopMode.BuyAndSell;
            }

            InvalidateStaticSurface();

            // TODO: Notify inventory control of mode change
            // InventoryControl.Instance?.SetRepairMode(_shopMode == ShopMode.Repair);
        }

    }
}
