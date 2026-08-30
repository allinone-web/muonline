using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Client.Main;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game;
using Client.Main.Models;
using Client.Main.Networking;
using Client.Main.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MUnique.OpenMU.Network.Packets;
using Client.Main.Controls.UI;

namespace Client.Main.Controls.UI.Game.Inventory
{
    public class InventoryControl : UIControl, IUiTexturePreloadable
    {
        /// <summary>
        /// 找不到道具自己的圖示時的替代圖。這是本面板<b>唯一</b>還在用的貼圖。
        ///
        /// 這裡原本還預載 9 張視窗外框貼圖（newui_item_table*）、一張 msgbox 底圖，
        /// 以及 NpcShop_I3.ozd 這張圖集 —— 全部只是載進來放著：面板改成程式繪製之後
        /// 一次都沒有被畫出來。同樣被移除的還有兩份內嵌的版面 JSON
        /// （InventoryLayout / InventoryRect），它們解析完就沒有任何人讀。
        /// 清單見 docs/待清理素材.md。
        /// </summary>
        private const string DefaultItemIconPath = "Interface/newui_item_box.tga";

        // ═══════════════════════════════════════════════════════════════
        // WINDOW DIMENSIONS - REDESIGNED
        // ═══════════════════════════════════════════════════════════════
        // 手機是橫向的長螢幕：水平空間很多、垂直空間很少。
        // 桌面版把「裝備欄」疊在「背包格」上面，總高 700 —— 在 720 高的畫布上
        // 幾乎頂滿，格子還只有 34 px。手機改成左右兩欄：左邊裝備、右邊背包，
        // 兩邊都拿得到完整高度，格子也放得大。
        private static readonly bool s_mobile = Client.Main.Controls.UI.MobileUi.IsMobile;

        // 桌面是固定尺寸；手機依畫布算出來（見 ResolveWindowSize），因此是實例欄位。
        private int WINDOW_WIDTH = 396;
        private int WINDOW_HEIGHT = 700;

        /// <summary>手機視窗的上緣。要讓開右上角的介面按鈕、經驗條與狀態列。</summary>
        private const int MobileWindowTop = 112;

        private const int MobileInfoColumnWidth = 280;
        private const int MobileColumnGap = 14;

        /// <summary>
        /// 裝備欄的寬度，取人偶實際需要的最小值。
        ///
        /// BuildEquipSlots 的擺放範圍是中心點左邊 4 格 + 24、右邊 4.5 格 + 16
        /// （右邊比較寬是因為翅膀那格有 3 格寬），合計 8.5 格 + 40。
        /// 先前寫成 9 格 + 40，多出來的 32 px 是純粹的留白。
        /// </summary>
        /// <summary>
        /// 裝備欄的寬度。
        ///
        /// 這一欄橫向是五段：左欄(2格) ｜ 項鍊戒指(1格) ｜ 中欄(2格) ｜ 戒指(1格) ｜ 右欄(2格)
        /// = 8 格，加上四道間隙。8x64 + 24 = 536。
        ///
        /// 原本是 8.5 格 + 40 = 584：多出來的半格是「翅膀」欄位比別人寬一格
        /// 又往右偏移造成的懸空，間隙也開得比需要的大。收窄的 48 px 直接讓給
        /// 最右邊的道具資訊欄 —— 那裡才是真的不夠用的地方。
        /// </summary>
        private static int MobileEquipColumnWidth => INVENTORY_SQUARE_WIDTH * 8 + 24;

        // 手機的四欄：立體圖 | 裝備 | 背包 | 資訊
        private Rectangle _previewPanelRect;
        private Rectangle _infoPanelRect;

        /// <summary>開啟時的滑入動畫（見 MobileUi.OpenAnimation）。</summary>
        private readonly MobileUi.OpenAnimation _openAnimation = new();

        // 資訊欄字級的快取（見 DrawMobileItemDetail）
        private InventoryItem _infoScaleItem;
        private int _infoScaleWidth = -1;
        private float _infoScale = MobileUi.ScaleFor(MobileUi.TextBody);

        /// <summary>標題列高度。手機一律用 MobileUi.WindowTitleHeight，
        /// 和技能、地圖、交易、設定同一個值 —— 每個視窗自己訂一個數字，
        /// 正是使用者說「每個面板都不一樣」的來源。</summary>
        private static int HEADER_HEIGHT => MobileUi.IsMobile ? MobileUi.WindowTitleHeight : 52;
        private const int SECTION_SPACING = 16;
        private const int PANEL_PADDING = 12;
        private static readonly int EQUIP_SECTION_HEIGHT = s_mobile ? 400 : 270;

        public static readonly int INVENTORY_SQUARE_WIDTH = s_mobile ? 64 : 34;
        public static readonly int INVENTORY_SQUARE_HEIGHT = s_mobile ? 64 : 34;

        /// <summary>裝備欄那一欄的水平中心。桌面是整個視窗的中心，手機是左半欄的中心。</summary>
        private int _equipCenterX;

        /// <summary>
        /// 背包格線的<b>欄數</b>。桌面 8，手機 <b>5</b>。
        ///
        /// 這個值純粹是排版：伺服器送來的是平面格號，客戶端用
        /// <c>x = index % Columns, y = index / Columns</c> 攤成格子，
        /// 送回去時再 <c>index = y * Columns + x</c> 折回來。兩者互為反函數，
        /// 所以欄數怎麼設都不影響協議 —— 前提是道具都是 1x1，
        /// 由 <see cref="Core.Utilities.ItemDatabase.SingleSlotItems"/> 保證。
        ///
        /// <b>手機一定是 5 欄</b>（使用者指定，且強調過三次）。8 欄要 512 px，
        /// 加上裝備欄與資訊欄之後整個視窗寬得離譜。5 欄只要 320 px，
        /// 代價是列數變多、要捲動 —— 對手機來說那是划算的交換，
        /// 而捲動本來就是手機最自然的動作。
        /// </summary>
        public static int Columns => s_mobile ? 5 : 8;

        /// <summary>
        /// 列數：把 <see cref="TotalSlots"/> 攤成 <see cref="Columns"/> 欄需要幾列。
        /// 5 欄 x 13 列 = 65，比實際的 64 多一格；多出來的那一格不對應任何
        /// 伺服器格號，由 <see cref="IsWithinGrid"/> 與 CanPlaceItem 兩處擋掉。
        /// </summary>
        public static int Rows => s_mobile
            ? (TotalSlots + Columns - 1) / Columns
            : 8;

        /// <summary>
        /// 背包實際的格數，<b>由伺服器決定</b>（目前 64）。
        /// 伺服器若擴充到 100，只要改這一個數字，列數與捲動範圍會自己跟著算。
        /// </summary>
        public const int TotalSlots = 64;
        internal const int InventorySlotOffsetConstant = 12;

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
            // Background layers
            public static readonly Color BgDarkest = ModernHudTheme.BgDarkest;
            public static readonly Color BgDark = ModernHudTheme.BgDark;
            public static readonly Color BgMid = ModernHudTheme.BgMid;
            public static readonly Color BgLight = ModernHudTheme.BgLight;
            public static readonly Color BgLighter = ModernHudTheme.BgLighter;

            // Accent - Warm Gold
            public static readonly Color Accent = ModernHudTheme.Accent;
            public static readonly Color AccentBright = ModernHudTheme.AccentBright;
            public static readonly Color AccentDim = ModernHudTheme.AccentDim;
            public static readonly Color AccentGlow = ModernHudTheme.AccentGlow;

            // Secondary accent - Cool Blue
            public static readonly Color Secondary = ModernHudTheme.Secondary;
            public static readonly Color SecondaryBright = ModernHudTheme.SecondaryBright;
            public static readonly Color SecondaryDim = ModernHudTheme.SecondaryDim;

            // Borders
            public static readonly Color BorderOuter = ModernHudTheme.BorderOuter;
            public static readonly Color BorderInner = ModernHudTheme.BorderInner;
            public static readonly Color BorderHighlight = ModernHudTheme.BorderHighlight;

            // Slots
            public static readonly Color SlotBg = ModernHudTheme.SlotBg;
            public static readonly Color SlotBorder = ModernHudTheme.SlotBorder;
            public static readonly Color SlotHover = ModernHudTheme.SlotHover;
            public static readonly Color SlotSelected = ModernHudTheme.SlotSelected;

            // Item rarity glow
            public static readonly Color GlowNormal = ModernHudTheme.GlowNormal;
            public static readonly Color GlowMagic = ModernHudTheme.GlowMagic;
            public static readonly Color GlowExcellent = ModernHudTheme.GlowExcellent;
            public static readonly Color GlowAncient = ModernHudTheme.GlowAncient;
            public static readonly Color GlowLegendary = ModernHudTheme.GlowLegendary;

            // Text
            public static readonly Color TextWhite = ModernHudTheme.TextWhite;
            public static readonly Color TextGold = ModernHudTheme.TextGold;
            public static readonly Color TextGray = ModernHudTheme.TextGray;
            public static readonly Color TextDark = ModernHudTheme.TextDark;

            // Status colors
            public static readonly Color Success = ModernHudTheme.Success;
            public static readonly Color Warning = ModernHudTheme.Warning;
            public static readonly Color Danger = ModernHudTheme.Danger;
        }

        private static readonly ItemGlowPalette GlowPalette = new(
            Theme.GlowNormal,
            Theme.GlowMagic,
            Theme.GlowExcellent,
            Theme.GlowAncient,
            Theme.GlowLegendary);

        private sealed class EquipSlotLayout
        {
            public EquipSlotLayout(byte slot, Rectangle rect, Point size, string label, bool accentRed = false)
            {
                Slot = slot;
                Rect = rect;
                Size = size;
                Label = label;
                AccentRed = accentRed;
            }

            public byte Slot { get; }
            public Rectangle Rect { get; }
            public Point Size { get; }
            public string Label { get; }
            public bool AccentRed { get; }
        }

        private enum TextAlignment
        {
            Left,
            Center,
            Right
        }

        private sealed class InventoryTextEntry
        {
            public InventoryTextEntry(Vector2 basePosition, float fontScale, Color color, TextAlignment alignment)
            {
                BasePosition = basePosition;
                FontScale = fontScale;
                Color = color;
                Alignment = alignment;
            }

            public Vector2 BasePosition { get; }
            public float FontScale { get; }
            public Color Color { get; set; }
            public TextAlignment Alignment { get; }
            public string Text { get; set; } = string.Empty;
            public bool Visible { get; set; } = true;
        }


        private RenderTarget2D _staticSurface;
        private bool _staticSurfaceDirty = true;

        private readonly List<InventoryTextEntry> _texts = new();
        private InventoryTextEntry _zenText;

        private readonly Dictionary<string, Texture2D> _itemTextureCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(InventoryItem item, int width, int height, bool animated), Texture2D> _bmdPreviewCache = new();

        private readonly List<InventoryItem> _items = new();
        private readonly List<(InventoryItem Item, Rectangle Rect)> _jewelEntries = new();
        private readonly Dictionary<byte, InventoryItem> _equippedItems = new();
        private InventoryItem[,] _itemGrid;

        private readonly NetworkManager _networkManager;
        private readonly ILogger<InventoryControl> _logger;

        private SpriteFont _font;

        private Rectangle _headerRect;
        private Rectangle _paperdollPanelRect;
        private Rectangle _beamRect;
        private Rectangle _gridRect;

        // ── 背包格線的捲動（只有手機會用到）──
        //
        // 5 欄需要 13 列，13 x 64 = 832 px，比可用高度還高，所以要捲。
        // _gridVisibleRows 是實際看得到的列數，_gridScrollRow 是最上面那一列的列號。
        private int _gridVisibleRows = Rows;
        private int _gridScrollRow;
        private bool _gridDragging;
        private int _gridDragStartY;
        private int _gridDragStartScrollPixels;
        private float _gridScrollPixels;
        private Rectangle _gridFrameRect;
        private Rectangle _footerRect;
        private Rectangle _zenFieldRect;
        private Rectangle _zenIconRect;
        private Rectangle _closeButtonRect;
        private Rectangle _footerLeftButtonRect;
        private Rectangle _footerRightButtonRect;

        private InventoryItem _hoveredItem;
        private Point _hoveredSlot = new(-1, -1);
        private int _hoveredEquipSlot = -1;
        private int _pickedFromEquipSlot = -1;
        private Point _pickedItemOriginalGrid = new(-1, -1);
        private Point _pickedAtMousePos;
        private bool _itemDragMoved;

        private bool _isDragging;
        private Point _dragOffset;
        private DateTime _lastClickTime = DateTime.MinValue;

        private long _zenAmount;
        private GameTime _currentGameTime;
        private bool _closeHovered;
        private bool _leftFooterHovered;
        private bool _rightFooterHovered;

        private bool _isRepairMode;
        private int _repairEnableLevel = 50;

        public bool IsSelfRepairMode => _isRepairMode;

        public readonly PickedItemRenderer _pickedItemRenderer;

        private readonly Dictionary<byte, EquipSlotLayout> _equipSlots = new();
        private Vector2 _layoutScale = Vector2.One;

        private static InventoryControl _instance;

        public InventoryControl(NetworkManager networkManager = null, ILoggerFactory loggerFactory = null)
        {
            // Ensure the singleton points to the active UI instance (needed by VaultControl drops).
            _instance = this;

            _networkManager = networkManager;
            var factory = loggerFactory ?? MuGame.AppLoggerFactory;
            _logger = factory?.CreateLogger<InventoryControl>();

            // 尺寸必須先決定 —— BuildLayoutMetrics 的每一欄都是從 WINDOW_WIDTH 推算的。
            // 順序反過來的話，版面會用預設的 396 去算，最右邊的資訊欄寬度剛好變成 0，
            // 於是只有那一欄不見（其他欄仍在視窗範圍內，所以看起來只是「文字沒出來」）。
            ResolveWindowSize();
            BuildLayoutMetrics();

            ControlSize = new Point(WINDOW_WIDTH, WINDOW_HEIGHT);
            ViewSize = ControlSize;
            AutoViewSize = false;
            Interactive = true;
            Visible = false;
            // 手機：靠右對齊會正好躲到右上角那六顆介面按鈕底下，改用明確座標（見 PositionForMobile）
            Align = s_mobile ? ControlAlign.None : (ControlAlign.VerticalCenter | ControlAlign.Right);
            Scale = 1f;

            _itemGrid = new InventoryItem[Columns, Rows];
            _pickedItemRenderer = new PickedItemRenderer();

            InitializeTextEntries();
        }

        public static InventoryControl Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new InventoryControl();
                }

                return _instance;
            }
        }

        public IEnumerable<string> GetPreloadTexturePaths()
            => new[] { DefaultItemIconPath };

        public long ZenAmount
        {
            get => _zenAmount;
            set
            {
                if (_zenAmount != value)
                {
                    _zenAmount = value;
                    UpdateZenText();
                }
            }
        }

        public override async Task Load()
        {
            await base.Load();

            await TextureLoader.Instance.PrepareAndGetTexture(DefaultItemIconPath);

            _font = GraphicsManager.Instance.Font;

            UpdateZenFromNetwork();
            UpdateZenText();
            InvalidateStaticSurface();
        }

        public void Preload()
        {
            RefreshInventoryContent();
        }

        public InventoryItem GetDraggedItem() => _pickedItemRenderer.Item;

        public void Show()
        {
            // Force correct position BEFORE first draw to prevent flash on wrong side
            PositionForMobile();
            _openAnimation.Restart();
            ForceAlignNow();
            Align = ControlAlign.None; // Prevent auto-realignment
            _networkManager?.GetCharacterState()?.ClearPendingInventoryMove(); // ensure no stale hides
            UpdateZenFromNetwork();
            RefreshInventoryContent();

            Visible = true;
            BringToFront();
            Scene.FocusControl = this;

            _zenText.Visible = true;
            UpdateZenText();

            _pickedItemRenderer.Visible = false;

            // Reset repair mode when reopening inventory
            _isRepairMode = false;

            InvalidateStaticSurface();
        }

        /// <summary>
        /// 手機：把視窗擺在「介面按鈕區塊的左邊」與「左上角資源資訊的下面」，
        /// 否則視窗會被 HUD 蓋住 —— HUD 是最上層繪製的，重疊處的文字會透出來，
        /// 也點不到底下的格子。
        /// </summary>
        private void PositionForMobile()
        {
            if (!s_mobile)
                return;

            var canvas = Controllers.UiScaler.VirtualSize;

            // 水平與垂直都置中。少掉底列之後高度剛好落在右上角按鈕區塊的下方，
            // 置中比硬釘在某個 Y 好看，也不會壓到那六顆按鈕。
            X = Math.Max(PANEL_PADDING, (canvas.X - WINDOW_WIDTH) / 2);
            _mobileBaseY = 0;   // 由下一行決定
            // 垂直置中。
            //
            // 先前為了「讓開頂部 HUD」把上緣釘在固定位置，結果視窗長高之後就再也
            // 置不了中 —— 那個顧慮其實不成立：Show() 會 BringToFront()，視窗本來
            // 就畫在 HUD 之上，不會有文字透出來的問題。置中優先。
            _mobileBaseY = Math.Max(12, (canvas.Y - WINDOW_HEIGHT) / 2);
            Y = _mobileBaseY;
        }

        /// <summary>視窗定位之後的 Y（滑入動畫會在它之上加偏移）。</summary>
        private int _mobileBaseY;

        /// <summary>左上角的頭像與數值文字底下的第一個安全 Y 座標。</summary>
        internal const int MobileTopSafeY = 112;

        /// <summary>
        /// Forces immediate position calculation based on Align property.
        /// Call this before showing the control to prevent position flickering.
        /// </summary>
        private void ForceAlignNow()
        {
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

        public void Hide()
        {
            // 手上還拿著東西就關視窗 —— 一定要放回<b>原來的位置</b>。
            //
            // 原本是 AddItem()，那是「放進背包格線裡的任何空位」。從裝備欄拿起來的
            // 東西因此會被塞進格線（畫面上等於脫掉了），格線滿了的話連塞都塞不進去，
            // 直接從客戶端消失 —— 而伺服器那邊它還好好地穿在身上。
            // 使用者回報的「背包偶爾會丟失裝備，重登又回來」就是這個。
            //
            // RestorePickedItemToOriginalLocation 認得來源是格子還是裝備欄。
            RestorePickedItemToOriginalLocation();

            Visible = false;
            if (Scene?.FocusControl == this)
            {
                Scene.FocusControl = null;
            }

            _zenText.Visible = false;
        }

        public void HookEvents()
        {
            if (_networkManager == null)
            {
                return;
            }

            var state = _networkManager.GetCharacterState();
            state.InventoryChanged += () => MuGame.ScheduleOnMainThread(RefreshInventoryContent);
            state.MoneyChanged += () => MuGame.ScheduleOnMainThread(() => ZenAmount = state.InventoryZen);
        }

        public bool AddItem(InventoryItem item)
        {
            if (CanPlaceItem(item, item.GridPosition))
            {
                _items.Add(item);
                PlaceItemOnGrid(item);
                return true;
            }

            return false;
        }

        public Point GetSlotAtScreenPositionPublic(Point screenPos) => GetSlotAtScreenPosition(screenPos);

        public bool CanPlaceAt(Point gridSlot, InventoryItem item) => CanPlaceItem(item, gridSlot);

        public override void Update(GameTime gameTime)
        {
            _currentGameTime = gameTime;

            if (s_mobile && _mobileBaseY > 0)
            {
                _openAnimation.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
                Y = _mobileBaseY + _openAnimation.OffsetPixels;
            }

            if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Escape) &&
                MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Escape))
            {
                Hide();
                return;
            }

            if (MuGame.Instance.Keyboard.IsKeyDown(Keys.L) &&
                MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.L))
            {
                ToggleRepairMode();
            }

            if (!Visible)
            {
                _pickedItemRenderer.Visible = false;
                return;
            }

            base.Update(gameTime);

            Point mousePos = MuGame.Instance.UiMouseState.Position;
            _hoveredItem = null;
            _hoveredSlot = new Point(-1, -1);
            _hoveredEquipSlot = GetEquipSlotAtScreenPosition(mousePos);
            UpdateChromeHover(mousePos);

            bool leftPressed = MuGame.Instance.UiMouseState.LeftButton == ButtonState.Pressed;
            bool leftJustPressed = leftPressed && MuGame.Instance.PrevUiMouseState.LeftButton == ButtonState.Released;
            bool leftJustReleased = !leftPressed && MuGame.Instance.PrevUiMouseState.LeftButton == ButtonState.Pressed;

            if (leftJustPressed && HandleChromeClick())
            {
                return;
            }

            if (leftJustPressed && IsMouseOverDragArea() && !_isDragging)
            {
                DateTime now = DateTime.Now;
                if ((now - _lastClickTime).TotalMilliseconds < 500)
                {
                    Align = ControlAlign.VerticalCenter | ControlAlign.Right;
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

            if (IsMouseOver && !_isDragging)
            {
                // 拖曳格線＝捲動。被捲動吃掉的話就不能同時算成點選道具，
                // 否則手指一滑，最後停在哪一格就選中哪一格。
                bool scrolled = UpdateGridScroll(mousePos, leftPressed, leftJustPressed, leftJustReleased);
                if (!scrolled)
                    HandleInventoryInteraction(mousePos, leftJustPressed, leftJustReleased);
            }

            if (_hoveredItem == null && _hoveredEquipSlot >= 0 && _equippedItems.TryGetValue((byte)_hoveredEquipSlot, out var hoveredEquip))
            {
                _hoveredItem = hoveredEquip;
            }

            if (leftJustReleased && _pickedItemRenderer.Item != null && !_isDragging)
            {
                // Simple click without moving should keep the item picked up; only place after a drag.
                if (!_itemDragMoved)
                {
                    return;
                }

                if (_hoveredEquipSlot >= 0 && TryPlacePickedItemIntoEquipSlot((byte)_hoveredEquipSlot))
                {
                    return;
                }

                // 放開的位置在<b>視窗外面</b>才算「丟出去」，不能只是「不在背包格子上」。
                //
                // 原本的條件是 !IsMouseOverGrid()，等於視窗裡除了格子以外的任何地方
                // ——標題列、裝備欄的空白、立體圖與資訊欄——都會把手上的道具丟到地上。
                //
                // 這裡刻意直接測 DisplayRectangle，而不是用 IsMouseOver 這個旗標：
                // 旗標是 GameControl.Update 算出來的，只要哪天 Interactive 被關掉
                // （手機上好幾個控制項都是這樣做的，為了不搶場景焦點），它就會恆為
                // false —— 而 false 在這裡的意思是「把道具丟掉」。用一個會預設成
                // 「銷毀」的旗標來守著不可逆的動作，是不能接受的。
                if (_itemDragMoved && !DisplayRectangle.Contains(MuGame.Instance.UiMouseState.Position))
                {
                    HandleDropOutsideInventory();
                }
            }

            if (_pickedItemRenderer.Item != null && !_itemDragMoved)
            {
                var current = MuGame.Instance.UiMouseState.Position;
                if (Vector2.Distance(current.ToVector2(), _pickedAtMousePos.ToVector2()) > 2f)
                {
                    _itemDragMoved = true;
                }
            }

            _pickedItemRenderer.Update(gameTime);
        }

        private void ToggleRepairMode()
        {
            var state = _networkManager?.GetCharacterState();
            if (state == null || state.Level < _repairEnableLevel)
            {
                return;
            }
            _isRepairMode = !_isRepairMode;
            SoundController.Instance.PlayBuffer("Sound/iButton.wav");
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible)
            {
                return;
            }

            var graphicsManager = GraphicsManager.Instance;
            if (graphicsManager?.Sprite == null)
            {
                return;
            }

            EnsureStaticSurface();

            var spriteBatch = graphicsManager.Sprite;
            SpriteBatchScope? scope = null;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                scope = new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    GraphicsManager.GetQualityLinearSamplerState(),
                    transform: UiScaler.SpriteTransform);
            }

            try
            {
                if (_staticSurface != null && !_staticSurface.IsDisposed)
                {
                    spriteBatch.Draw(_staticSurface, DisplayRectangle, Color.White * Alpha);
                }

                // Draw overlays beneath items (consistent with vault/NPC shop)
                DrawGridOverlays(spriteBatch);
                DrawEquipHighlights(spriteBatch);
                DrawInventoryItems(spriteBatch);
                DrawGridScrollbar(spriteBatch);
                DrawEquippedItems(spriteBatch);
                DrawChrome(spriteBatch);
                DrawTexts(spriteBatch);
                DrawMobilePickedHighlight(spriteBatch);
                if (s_mobile)
                    DrawMobileDetailColumns(spriteBatch);
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
            Client.Main.Graphics.UiRenderTargetPool.Return(_staticSurface);
            _staticSurface = null;
        }

        protected override void OnScreenSizeChanged()
        {
            base.OnScreenSizeChanged();

            if (s_mobile && ResolveWindowSize())
            {
                ControlSize = new Point(WINDOW_WIDTH, WINDOW_HEIGHT);
                ViewSize = ControlSize;
                BuildLayoutMetrics();
            }

            InvalidateStaticSurface();
        }

        /// <summary>
        /// 決定視窗尺寸。手機用整個畫布的寬度（扣掉邊距），因為四欄版面需要橫向空間；
        /// 上緣讓開右上角的介面按鈕、經驗條與狀態列。
        /// </summary>
        /// <returns>尺寸是否改變。</returns>
        private bool ResolveWindowSize()
        {
            int width, height;

            if (s_mobile)
            {
                // 依內容決定寬度，不要鋪滿畫布 —— 鋪滿的話沒有選道具時，
                // 右邊會出現一大片空白，看起來像壞掉。
                var canvas = Controllers.UiScaler.VirtualSize;

                // 三欄：裝備 ｜ 背包 ｜ 資訊（立體圖放在資訊欄上方）。
                // 格子放大到 64 之後四欄放不下 —— 與其縮小格子，不如少一欄：
                // 格子看得清楚比多一欄重要，而立體圖疊在資訊上方反而更大。
                int content = PANEL_PADDING * 2
                    + MobileEquipColumnWidth
                    + (Columns * INVENTORY_SQUARE_WIDTH + 20)    // 背包欄（含外框）
                    + MobileInfoColumnWidth
                    + MobileColumnGap * 2;

                width = content;

                // 高度只需要容納標題 + 背包格（裝備欄比它矮），不再有底列。
                // 13 列裝不進畫面是正常的 —— ClampWindowSize 會夾住，
                // BuildMobileLayoutMetrics 再算出放得下幾列，其餘用捲的。
                height = HEADER_HEIGHT + 8 + Rows * INVENTORY_SQUARE_HEIGHT + 16;

                // 一定要夾進畫面。「高度由內容決定」在 720p 的桌面視窗裡沒問題，
                // 在 756 高的畫布上會直接長到畫面外，而且是無聲的 ——
                // 玩家只會看到背包底下少了幾列，沒有任何提示。
                // 夾完之後 BuildMobileLayoutMetrics 會自己算出放得下幾列並開始捲動。
                var clamped = Client.Main.Controls.UI.MobileUi.ClampWindowSize(width, height);
                width = clamped.X;
                height = clamped.Y;
            }
            else
            {
                width = 396;
                height = 700;
            }

            if (width == WINDOW_WIDTH && height == WINDOW_HEIGHT)
                return false;

            WINDOW_WIDTH = width;
            WINDOW_HEIGHT = height;
            return true;
        }

        private void InitializeTextEntries()
        {
            _texts.Clear();

            // Title is now drawn in DrawModernHeader, so we skip the title text

            // Zen text - positioned inside zen field
            _zenText = CreateText(new Vector2(_zenFieldRect.X + 8, _zenFieldRect.Y + _zenFieldRect.Height * 0.5f - 6f),
                                  12f, Theme.TextGold);
            _zenText.Visible = false;
        }

        private InventoryTextEntry CreateText(Vector2 basePosition, float fontSize, Color color, TextAlignment alignment = TextAlignment.Left)
        {
            float fontScale = fontSize / Constants.BASE_FONT_SIZE;
            var entry = new InventoryTextEntry(basePosition, fontScale, color, alignment);
            _texts.Add(entry);
            return entry;
        }

        private void UpdateZenFromNetwork()
        {
            if (_networkManager == null)
            {
                return;
            }

            var state = _networkManager.GetCharacterState();
            ZenAmount = state?.InventoryZen ?? 0;
        }

        private void UpdateZenText()
        {
            if (_zenText != null)
            {
                // 千分位。HUD 的金幣是 9,509,902，背包卻寫成 9509902 ——
                // 同一個數字在同一個畫面上有兩種寫法。
                _zenText.Text = ZenAmount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            }
        }


        private void RecalculateLayoutScale()
        {
            // For inventory we want the JSON sizes to be used directly (no auto-scaling).
            _layoutScale = Vector2.One;
        }

        private void BuildLayoutMetrics()
        {
            if (s_mobile)
            {
                BuildMobileLayoutMetrics();
                return;
            }

            _equipCenterX = WINDOW_WIDTH / 2;
            BuildEquipSlots();

            // Header
            _headerRect = new Rectangle(0, 0, WINDOW_WIDTH, HEADER_HEIGHT);

            // Equipment panel - centered, fixed height
            int equipPanelWidth = WINDOW_WIDTH - PANEL_PADDING * 2;
            int equipPanelTop = HEADER_HEIGHT + 8;
            _paperdollPanelRect = new Rectangle(PANEL_PADDING, equipPanelTop, equipPanelWidth, EQUIP_SECTION_HEIGHT);

            // Grid section - positioned BELOW equipment with proper spacing
            int gridTotalWidth = Columns * INVENTORY_SQUARE_WIDTH;
            int gridTotalHeight = Rows * INVENTORY_SQUARE_HEIGHT;
            int gridX = (WINDOW_WIDTH - gridTotalWidth) / 2;
            int minGridY = _paperdollPanelRect.Bottom + Math.Max(SECTION_SPACING / 2, 4);
            int gridY = minGridY;

            int footerHeight = 50;
            int footerTop = WINDOW_HEIGHT - footerHeight - 10;

            // Ensure grid section does not overlap footer
            int availableBottom = footerTop - SECTION_SPACING;
            int maxGridY = Math.Max(minGridY, availableBottom - gridTotalHeight - 8);
            gridY = Math.Min(gridY, maxGridY);
            if (gridY < minGridY)
            {
                gridY = minGridY;
            }

            _gridRect = new Rectangle(gridX, gridY, gridTotalWidth, gridTotalHeight);
            _gridFrameRect = new Rectangle(gridX - 8, gridY - 8, gridTotalWidth + 16, gridTotalHeight + 16);

            // Footer - at bottom
            _footerRect = new Rectangle(PANEL_PADDING, footerTop,
                                         WINDOW_WIDTH - PANEL_PADDING * 2, footerHeight);

            // Zen display
            _zenIconRect = new Rectangle(_footerRect.X + 12, _footerRect.Y + 14, 22, 22);
            _zenFieldRect = new Rectangle(_zenIconRect.Right + 10, _footerRect.Y + 10, 160, 30);

            // Buttons
            int btnSize = 38;
            _closeButtonRect = new Rectangle(WINDOW_WIDTH - btnSize - 12, 10, btnSize, btnSize);
            _footerLeftButtonRect = new Rectangle(_footerRect.Right - btnSize * 2 - 20, _footerRect.Y + 6, btnSize, btnSize);
            _footerRightButtonRect = new Rectangle(_footerRect.Right - btnSize - 8, _footerRect.Y + 6, btnSize, btnSize);

            // Beam rect not used in new design
            _beamRect = Rectangle.Empty;
        }

        /// <summary>
        /// 手機版的兩欄配置：
        ///
        ///   ┌─────────── 標題 ───────────────────────────────┐
        ///   │  裝備（人物剪影 + 12 個裝備格）  │  背包 8x8    │
        ///   │                                 │              │
        ///   ├───────────────── 金幣 / 按鈕 ────────────────────┤
        ///   └────────────────────────────────────────────────┘
        /// </summary>
        private void BuildMobileLayoutMetrics()
        {
            // 手機沒有底列。金幣與修理鈕都併進標題列 ——
            // 少一整列（約 64 px）視窗才矮得下來，才能在畫面上垂直置中。
            int contentTop = HEADER_HEIGHT + 8;
            int contentBottom = WINDOW_HEIGHT - 14;
            int contentHeight = contentBottom - contentTop;

            int gridTotalWidth = Columns * INVENTORY_SQUARE_WIDTH;

            // 放得下幾列就顯示幾列，其餘用捲的。
            _gridVisibleRows = Math.Clamp(contentHeight / INVENTORY_SQUARE_HEIGHT, 1, Rows);
            int gridTotalHeight = _gridVisibleRows * INVENTORY_SQUARE_HEIGHT;

            // 四欄由左到右：立體圖 | 裝備 | 背包 | 資訊
            const int ColumnGap = MobileColumnGap;
            int equipWidth = MobileEquipColumnWidth;
            int gridColumnWidth = gridTotalWidth + 20;          // 含外框

            int x = PANEL_PADDING;

            _paperdollPanelRect = new Rectangle(x, contentTop, equipWidth, EQUIP_SECTION_HEIGHT);
            _equipCenterX = _paperdollPanelRect.Center.X;
            x += equipWidth + ColumnGap;

            int gridX = x + 10;
            int gridY = contentTop + Math.Max(0, (contentHeight - gridTotalHeight) / 2);
            _gridRect = new Rectangle(gridX, gridY, gridTotalWidth, gridTotalHeight);
            _gridFrameRect = new Rectangle(gridX - 10, gridY - 10, gridTotalWidth + 20, gridTotalHeight + 20);
            x += gridColumnWidth + ColumnGap;

            // 最右欄：立體圖在上、文字在下，共用一欄。
            int infoWidth = Math.Max(0, WINDOW_WIDTH - PANEL_PADDING - x);
            int previewHeight = Math.Min(infoWidth, contentHeight / 2);
            _previewPanelRect = new Rectangle(x, contentTop, infoWidth, previewHeight);
            _infoPanelRect = new Rectangle(x, contentTop + previewHeight + 8,
                                           infoWidth, contentHeight - previewHeight - 8);

            BuildEquipSlots();

            _headerRect = new Rectangle(0, 0, WINDOW_WIDTH, HEADER_HEIGHT);
            _footerRect = Rectangle.Empty;

            // 關閉鈕放<b>左上角</b>，不是右上角。
            //
            // 螢幕右上角是六顆介面按鈕（MENU / CHAR / BAG …）。視窗的關閉鈕如果也在
            // 右上角，兩者就疊在同一塊區域 —— 想關視窗結果開了另一個，或反過來。
            // 這在滑鼠上不成問題（指標很精準），在拇指上每天都會發生。
            // 遊戲內所有視窗一律左上角關閉，見 docs/手機遊戲界面規格.md。
            // 位置由 MobileUi 決定，和其他視窗一模一樣（左上角、垂直置中）。
            _closeButtonRect = MobileUi.WindowCloseButtonRect(new Rectangle(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT));
            _footerRightButtonRect = new Rectangle(
                _closeButtonRect.Right + 10, _closeButtonRect.Y,
                MobileUi.CloseButtonSize, MobileUi.CloseButtonSize);

            // 金幣改到標題列右側，補上左邊讓出來的位置
            const int zenFieldWidth = 200;
            _zenFieldRect = new Rectangle(WINDOW_WIDTH - PANEL_PADDING - zenFieldWidth, 10, zenFieldWidth, 32);
            _zenIconRect = new Rectangle(_zenFieldRect.X - 34, 14, 24, 24);

            // 底列原本還有一顆重複的關閉鈕，標題列已經有了，手機不再重複
            _footerLeftButtonRect = Rectangle.Empty;

            _beamRect = Rectangle.Empty;
        }

        private void BuildEquipSlots()
        {
            _equipSlots.Clear();

            int cell = INVENTORY_SQUARE_WIDTH;
            int panelCenterX = _equipCenterX;
            int baseY = HEADER_HEIGHT + 20;

            // Left column (pet, left-hand weapon, gloves)
            // 間隙由 24 收成 8：這一欄的寬度是被最外側的兩個欄位撐出來的，
            // 每收 1 px 兩邊就各省 1 px。
            int leftColX = panelCenterX - cell * 4 - 8;
            AddEquipSlot(8, new Point(leftColX, baseY), new Point(2, 2), "PET");
            AddEquipSlot(0, new Point(leftColX, baseY + cell * 2 + 8), new Point(2, 3), "L.HAND");
            AddEquipSlot(5, new Point(leftColX, baseY + cell * 5 + 16), new Point(2, 2), "GLOVES");

            // Center column (helm, armor, pants + rings/pendant)
            int centerColX = panelCenterX - cell;
            AddEquipSlot(2, new Point(centerColX, baseY), new Point(2, 2), "HELM");
            AddEquipSlot(3, new Point(centerColX, baseY + cell * 2 + 8), new Point(2, 3), "ARMOR");
            AddEquipSlot(4, new Point(centerColX, baseY + cell * 5 + 16), new Point(2, 2), "PANTS");

            // Rings and pendant next to the center column
            int accessoryOffset = 6;
            AddEquipSlot(9, new Point(centerColX - cell - accessoryOffset, baseY + cell * 2 + 20), new Point(1, 1), "PEND");
            AddEquipSlot(10, new Point(centerColX - cell - accessoryOffset, baseY + cell * 5 + 28), new Point(1, 1), "RING");
            AddEquipSlot(11, new Point(centerColX + cell * 2 + accessoryOffset, baseY + cell * 5 + 28), new Point(1, 1), "RING");

            // Right column (wings, right-hand weapon, boots)
            int rightColX = panelCenterX + cell * 2 + 8;

            // 翅膀原本是 3 格寬、而且還往左偏半格，右緣因此比同欄的武器多出 32 px ——
            // 整個裝備欄的寬度就是被它一個人撐出來的。改成和其他欄位一樣 2 格寬。
            AddEquipSlot(7, new Point(rightColX, baseY - 4), new Point(2, 2), "WINGS");
            AddEquipSlot(1, new Point(rightColX, baseY + cell * 2 + 8), new Point(2, 3), "R.HAND");
            AddEquipSlot(6, new Point(rightColX, baseY + cell * 5 + 16), new Point(2, 2), "BOOTS");
        }

        private void AddEquipSlot(byte slot, Point origin, Point size, string ghostLabel, bool accentRed = false)
        {
            var rect = new Rectangle(origin.X, origin.Y, size.X * INVENTORY_SQUARE_WIDTH, size.Y * INVENTORY_SQUARE_HEIGHT);
            _equipSlots[slot] = new EquipSlotLayout(slot, rect, size, ghostLabel, accentRed);
        }

        private void InvalidateStaticSurface()
        {
            _staticSurfaceDirty = true;
        }

        private void EnsureStaticSurface()
        {
            if (!_staticSurfaceDirty && _staticSurface != null && !_staticSurface.IsDisposed)
            {
                return;
            }

            var graphicsDevice = GraphicsManager.Instance?.GraphicsDevice;
            if (graphicsDevice == null)
            {
                return;
            }

            Client.Main.Graphics.UiRenderTargetPool.Return(_staticSurface);
            _staticSurface = Client.Main.Graphics.UiRenderTargetPool.Rent(graphicsDevice, WINDOW_WIDTH, WINDOW_HEIGHT);

            // 切換 render target 之前必須先把外層批次送出去，否則畫面上排隊中的
            // 東西會被畫進這張表面裡（見 SpriteBatchScope.BeginRenderTarget）。
            using var __rtSection = SpriteBatchScope.BeginRenderTarget(graphicsDevice, _staticSurface);
            graphicsDevice.Clear(Color.Transparent);

            var spriteBatch = GraphicsManager.Instance.Sprite;
            using (new SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp))
            {
                DrawStaticElements(spriteBatch);
            }

            _staticSurfaceDirty = false;
        }

        // ═══════════════════════════════════════════════════════════════
        // CORE DRAWING PRIMITIVES
        // ═══════════════════════════════════════════════════════════════

        private void DrawWindowBackground(SpriteBatch spriteBatch, Rectangle rect)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            if (s_mobile)
            {
                // 和登入、選伺服器、登入表單同一個面板：半透明底 + 一條細框 + 標題列。
                // 桌面那套（外框 + 漸層 + 內框高光 + 四角托架）在手機上只是把
                // 一個面板拆成五條互相干擾的線。
                MobileUi.DrawPanel(spriteBatch, rect, HEADER_HEIGHT);
                return;
            }

            // Outer border
            spriteBatch.Draw(pixel, rect, Theme.BorderOuter);

            // Main background with vertical gradient
            var innerRect = new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4);
            UiDrawHelper.DrawVerticalGradient(spriteBatch, innerRect, Theme.BgDark, Theme.BgDarkest);

            // Subtle inner border highlight
            spriteBatch.Draw(pixel, new Rectangle(innerRect.X, innerRect.Y, innerRect.Width, 1), Theme.BorderInner * 0.5f);
            spriteBatch.Draw(pixel, new Rectangle(innerRect.X, innerRect.Y, 1, innerRect.Height), Theme.BorderInner * 0.3f);

            // Corner accents
            UiDrawHelper.DrawCornerAccents(spriteBatch, rect, Theme.Accent * 0.4f);
        }

        private void DrawPanel(SpriteBatch spriteBatch, Rectangle rect, Color bgColor, bool withBorder = true, bool withGlow = false)
        {
            UiDrawHelper.DrawPanel(spriteBatch, rect, bgColor,
                withBorder ? Theme.BorderInner : (Color?)null,
                withBorder ? Theme.BorderOuter : (Color?)null,
                withBorder ? Theme.BorderHighlight * 0.3f : null,
                withGlow, withGlow ? Theme.Accent * 0.15f : null);
        }

        private void DrawSectionHeader(SpriteBatch spriteBatch, string title, int x, int y, int width)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null || _font == null) return;

            // 區塊標題 —— 統一級距，見 MobileUi 的文字級距。
            // 手機用 TextLabel（13）而不是 TextHeading（17）：它畫在欄位框的<b>上方</b>，
            // 而框是之後才畫的；17 px 的字高會被框的上緣切掉半行。
            float scale = s_mobile ? MobileUi.ScaleFor(MobileUi.TextLabel) : 0.36f;
            Vector2 textSize = _font.MeasureString(title) * scale;

            if (s_mobile)
            {
                // 只留字，靠左。原本左右各一條漸層線加一個 3px 方點 ——
                // 那是在替一個兩個字的標籤加四樣裝飾。
                var flatPos = new Vector2(x, y);
                spriteBatch.DrawString(_font, title, flatPos + Vector2.One, Color.Black * 0.55f,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                spriteBatch.DrawString(_font, title, flatPos, MobileUi.TextDim,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                return;
            }

            // Decorative lines on sides
            int lineY = y + (int)(textSize.Y / 2);
            int textPadding = 12;
            int textX = x + (width - (int)textSize.X) / 2;

            // Left line with fade
            int leftLineWidth = textX - x - textPadding;
            if (leftLineWidth > 20)
            {
                UiDrawHelper.DrawHorizontalGradient(spriteBatch,
                    new Rectangle(x, lineY, leftLineWidth, 1),
                    Theme.Accent * 0.1f, Theme.Accent * 0.5f);
                spriteBatch.Draw(pixel, new Rectangle(textX - textPadding - 3, lineY - 1, 3, 3), Theme.Accent * 0.6f);
            }

            // Right line with fade
            int rightLineStart = textX + (int)textSize.X + textPadding;
            int rightLineWidth = x + width - rightLineStart;
            if (rightLineWidth > 20)
            {
                UiDrawHelper.DrawHorizontalGradient(spriteBatch,
                    new Rectangle(rightLineStart, lineY, rightLineWidth, 1),
                    Theme.Accent * 0.5f, Theme.Accent * 0.1f);
                spriteBatch.Draw(pixel, new Rectangle(rightLineStart, lineY - 1, 3, 3), Theme.Accent * 0.6f);
            }

            // Text shadow
            spriteBatch.DrawString(_font, title, new Vector2(textX + 1, y + 1), Color.Black * 0.6f,
                                   0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            // Text
            spriteBatch.DrawString(_font, title, new Vector2(textX, y), Theme.TextGold,
                                   0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private void DrawStaticElements(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            var fullRect = new Rectangle(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT);

            // ═══════════════════════════════════════════════════════════
            // 1. MAIN WINDOW BACKGROUND
            // ═══════════════════════════════════════════════════════════
            DrawWindowBackground(spriteBatch, fullRect);

            // ═══════════════════════════════════════════════════════════
            // 2. HEADER
            // ═══════════════════════════════════════════════════════════
            DrawModernHeader(spriteBatch);

            // ═══════════════════════════════════════════════════════════
            // 3. EQUIPMENT SECTION
            // ═══════════════════════════════════════════════════════════
            DrawModernEquipSection(spriteBatch);

            // ═══════════════════════════════════════════════════════════
            // 4. INVENTORY GRID SECTION
            // ═══════════════════════════════════════════════════════════
            DrawModernGridSection(spriteBatch);

            // ═══════════════════════════════════════════════════════════
            // 5. FOOTER
            // ═══════════════════════════════════════════════════════════
            DrawModernFooter(spriteBatch);
        }

        private void DrawModernHeader(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            if (s_mobile)
            {
                // 標題列本身已經由 DrawWindowBackground 畫好了（MobileUi.DrawPanel 的 titleHeight），
                // 這裡只放字。沒有金線、沒有文字後面的光暈、沒有兩段漸層分隔線 ——
                // 那些在手機上加起來就是使用者說的「很繁瑣」。
                if (_font != null)
                {
                    const string title = "INVENTORY";
                    float scale = MobileUi.ScaleFor(MobileUi.TextTitle);
                    Vector2 size = _font.MeasureString(title) * scale;
                    var pos = new Vector2((WINDOW_WIDTH - size.X) / 2f, (HEADER_HEIGHT - size.Y) / 2f);
                    spriteBatch.DrawString(_font, title, pos + Vector2.One, Color.Black * 0.6f,
                                           0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                    spriteBatch.DrawString(_font, title, pos, MobileUi.TextPrimary,
                                           0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                }

                return;
            }

            // Header background
            var headerBg = new Rectangle(8, 6, WINDOW_WIDTH - 16, HEADER_HEIGHT - 8);
            DrawPanel(spriteBatch, headerBg, Theme.BgMid);

            // Gold accent line at very top
            spriteBatch.Draw(pixel, new Rectangle(20, 8, WINDOW_WIDTH - 40, 2), Theme.Accent * 0.8f);
            spriteBatch.Draw(pixel, new Rectangle(30, 10, WINDOW_WIDTH - 60, 1), Theme.AccentDim * 0.4f);

            // Title
            if (_font != null)
            {
                string title = "INVENTORY";
                float scale = 0.55f;
                Vector2 size = _font.MeasureString(title) * scale;
                Vector2 pos = new((WINDOW_WIDTH - size.X) / 2, (HEADER_HEIGHT - size.Y) / 2 + 2);

                // Glow behind text
                spriteBatch.Draw(pixel, new Rectangle((int)pos.X - 20, (int)pos.Y - 4, (int)size.X + 40, (int)size.Y + 8),
                                Theme.AccentGlow * 0.3f);

                // Shadow
                spriteBatch.DrawString(_font, title, pos + new Vector2(2, 2), Color.Black * 0.5f,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                // Main text
                spriteBatch.DrawString(_font, title, pos, Theme.TextWhite,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }

            // Bottom separator
            int separatorY = HEADER_HEIGHT - 2;
            UiDrawHelper.DrawHorizontalGradient(spriteBatch, new Rectangle(20, separatorY, (WINDOW_WIDTH - 40) / 2, 1),
                                  Color.Transparent, Theme.BorderInner);
            UiDrawHelper.DrawHorizontalGradient(spriteBatch, new Rectangle(WINDOW_WIDTH / 2, separatorY, (WINDOW_WIDTH - 40) / 2, 1),
                                  Theme.BorderInner, Color.Transparent);
        }

        private void DrawModernEquipSection(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            // Section title
            // 手機不畫「EQUIPMENT」這行字：底下就是一整排裝備格，不需要標籤說明，
            // 而且它畫在欄位框上方，正好被右上角的介面按鈕壓住。
            if (!s_mobile)
                DrawSectionHeader(spriteBatch, "EQUIPMENT", _paperdollPanelRect.X, _paperdollPanelRect.Y - 18, _paperdollPanelRect.Width);

            // Main panel background
            DrawPanel(spriteBatch, _paperdollPanelRect, Theme.BgMid);

            // Character silhouette area (center darker region)
            int silhouetteWidth = INVENTORY_SQUARE_WIDTH * 2 + 20;
            int silhouetteX = _equipCenterX - silhouetteWidth / 2;
            var silhouetteRect = new Rectangle(silhouetteX, _paperdollPanelRect.Y + 10,
                                                silhouetteWidth, _paperdollPanelRect.Height - 20);
            spriteBatch.Draw(pixel, silhouetteRect, Theme.BgDarkest * 0.5f);

            // Draw vertical divider lines
            // 手機不畫：paperdoll 的分組已經靠 silhouetteRect 的深底表達了，
            // 再加兩條半透明豎線只是多兩條線。
            if (s_mobile)
            {
                foreach (var mobileLayout in _equipSlots.Values)
                {
                    DrawModernEquipSlot(spriteBatch, mobileLayout);
                }

                return;
            }

            int dividerX1 = silhouetteX - 30;
            int dividerX2 = silhouetteX + silhouetteWidth + 30;
            UiDrawHelper.DrawVerticalGradient(spriteBatch,
                new Rectangle(dividerX1, _paperdollPanelRect.Y + 20, 1, _paperdollPanelRect.Height - 40),
                Theme.BorderInner * 0.3f, Theme.BorderInner * 0.1f);
            UiDrawHelper.DrawVerticalGradient(spriteBatch,
                new Rectangle(dividerX2, _paperdollPanelRect.Y + 20, 1, _paperdollPanelRect.Height - 40),
                Theme.BorderInner * 0.3f, Theme.BorderInner * 0.1f);

            // Draw each equipment slot
            foreach (var layout in _equipSlots.Values)
            {
                DrawModernEquipSlot(spriteBatch, layout);
            }
        }

        private void DrawModernEquipSlot(SpriteBatch spriteBatch, EquipSlotLayout layout)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            Rectangle rect = layout.Rect;
            bool hasItem = _equippedItems.ContainsKey(layout.Slot);

            // Slot background
            Color bgColor = hasItem ? Theme.BgLight : Theme.SlotBg;
            UiDrawHelper.DrawVerticalGradient(spriteBatch, rect, bgColor, Theme.BgDarkest);

            // Border
            Color borderColor = layout.AccentRed ? Theme.Danger : Theme.SlotBorder;
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), borderColor * 0.8f);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), Theme.BorderOuter);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), borderColor * 0.6f);
            spriteBatch.Draw(pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), Theme.BorderOuter);

            // Inner cell divisions
            for (int y = 1; y < layout.Size.Y; y++)
            {
                int lineY = rect.Y + y * INVENTORY_SQUARE_HEIGHT;
                spriteBatch.Draw(pixel, new Rectangle(rect.X + 2, lineY, rect.Width - 4, 1), Theme.BorderOuter * 0.5f);
            }
            for (int x = 1; x < layout.Size.X; x++)
            {
                int lineX = rect.X + x * INVENTORY_SQUARE_WIDTH;
                spriteBatch.Draw(pixel, new Rectangle(lineX, rect.Y + 2, 1, rect.Height - 4), Theme.BorderOuter * 0.5f);
            }

            // Ghost label (only if no item)
            if (!hasItem)
            {
                DrawSlotGhostLabel(spriteBatch, layout);
            }
        }

        private void DrawSlotGhostLabel(SpriteBatch spriteBatch, EquipSlotLayout layout)
        {
            if (_font == null || string.IsNullOrEmpty(layout.Label)) return;

            // 空槽的提示字。刻意用最小的一級 —— 它只是在說「這裡放什麼」，
            // 一旦裝備上去就被圖示蓋掉，不該和真正的內容搶注意力。
            float scale = s_mobile ? MobileUi.ScaleFor(MobileUi.TextCaption) : 0.26f;
            Vector2 size = _font.MeasureString(layout.Label) * scale;
            Vector2 center = new(layout.Rect.X + layout.Rect.Width / 2f, layout.Rect.Y + layout.Rect.Height / 2f);
            Vector2 pos = center - size / 2f;

            Color textColor = layout.AccentRed ? new Color(100, 60, 60, 120) : Theme.TextDark * 0.6f;

            spriteBatch.DrawString(_font, layout.Label, pos + Vector2.One, Color.Black * 0.3f,
                                   0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(_font, layout.Label, pos, textColor,
                                   0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private void DrawModernGridSection(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            // Section title
            DrawSectionHeader(spriteBatch, "BACKPACK", _gridFrameRect.X, _gridFrameRect.Y + 4, _gridFrameRect.Width);

            // Outer frame
            DrawPanel(spriteBatch, _gridFrameRect, Theme.BgMid, withGlow: false);

            // Grid background
            spriteBatch.Draw(pixel, _gridRect, Theme.SlotBg);

            // Inner shadow
            spriteBatch.Draw(pixel, new Rectangle(_gridRect.X, _gridRect.Y, _gridRect.Width, 2), Color.Black * 0.4f);
            spriteBatch.Draw(pixel, new Rectangle(_gridRect.X, _gridRect.Y, 2, _gridRect.Height), Color.Black * 0.3f);

            // Grid lines
            Color gridLine = new(40, 48, 60, 100);
            Color gridLineMajor = new(55, 65, 80, 120);

            for (int x = 1; x < Columns; x++)
            {
                int lineX = _gridRect.X + x * INVENTORY_SQUARE_WIDTH;
                bool isMajor = x == Columns / 2;
                spriteBatch.Draw(pixel, new Rectangle(lineX, _gridRect.Y, 1, _gridRect.Height), isMajor ? gridLineMajor : gridLine);
            }

            for (int y = 1; y < _gridVisibleRows; y++)
            {
                int lineY = _gridRect.Y + y * INVENTORY_SQUARE_HEIGHT;
                bool isMajor = y == _gridVisibleRows / 2;
                spriteBatch.Draw(pixel, new Rectangle(_gridRect.X, lineY, _gridRect.Width, 1), isMajor ? gridLineMajor : gridLine);
            }

            // Border highlight
            spriteBatch.Draw(pixel, new Rectangle(_gridRect.X, _gridRect.Bottom - 1, _gridRect.Width, 1), Theme.BorderHighlight * 0.2f);
            spriteBatch.Draw(pixel, new Rectangle(_gridRect.Right - 1, _gridRect.Y, 1, _gridRect.Height), Theme.BorderHighlight * 0.15f);
        }

        private void DrawModernFooter(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            // Top separator line
            int sepY = _footerRect.Y - 6;
            UiDrawHelper.DrawHorizontalGradient(spriteBatch, new Rectangle(30, sepY, (WINDOW_WIDTH - 60) / 2, 1),
                                  Color.Transparent, Theme.Accent * 0.4f);
            UiDrawHelper.DrawHorizontalGradient(spriteBatch, new Rectangle(WINDOW_WIDTH / 2, sepY, (WINDOW_WIDTH - 60) / 2, 1),
                                  Theme.Accent * 0.4f, Color.Transparent);

            // Footer panel
            DrawPanel(spriteBatch, _footerRect, Theme.BgMid);

            // Zen display area
            DrawZenDisplay(spriteBatch);
        }

        private void DrawZenDisplay(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            // Coin icon
            Rectangle iconRect = _zenIconRect;

            // Coin outer
            DrawFilledCircle(spriteBatch, iconRect.X + iconRect.Width / 2, iconRect.Y + iconRect.Height / 2,
                             iconRect.Width / 2, Theme.AccentDim);
            // Coin inner
            DrawFilledCircle(spriteBatch, iconRect.X + iconRect.Width / 2, iconRect.Y + iconRect.Height / 2,
                             iconRect.Width / 2 - 3, Theme.Accent);
            // Coin highlight
            DrawFilledCircle(spriteBatch, iconRect.X + iconRect.Width / 2 - 2, iconRect.Y + iconRect.Height / 2 - 2,
                             iconRect.Width / 4, Theme.AccentBright * 0.6f);

            // Zen field background
            if (MobileUi.IsMobile)
            {
                // 一塊底色就夠了。三層（外框 + 內框 + 內部再一塊更深的底）在標題列裡
                // 只會讓金幣欄看起來像另一個視窗。
                spriteBatch.Draw(pixel, _zenFieldRect, MobileUi.FieldFill * 0.9f);
                return;
            }

            DrawPanel(spriteBatch, _zenFieldRect, Theme.SlotBg);

            // Inner darker area
            var innerField = new Rectangle(_zenFieldRect.X + 2, _zenFieldRect.Y + 2,
                                           _zenFieldRect.Width - 4, _zenFieldRect.Height - 4);
            spriteBatch.Draw(pixel, innerField, Theme.BgDarkest * 0.7f);
        }

        private void DrawFilledCircle(SpriteBatch spriteBatch, int centerX, int centerY, int radius, Color color)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null || radius <= 0) return;

            // Simple filled circle using rectangles
            for (int y = -radius; y <= radius; y++)
            {
                int halfWidth = (int)MathF.Sqrt(radius * radius - y * y);
                if (halfWidth > 0)
                {
                    spriteBatch.Draw(pixel, new Rectangle(centerX - halfWidth, centerY + y, halfWidth * 2, 1), color);
                }
            }
        }

        private void RefreshInventoryContent()
        {
            if (_networkManager == null)
            {
                return;
            }

            _items.Clear();
            _itemGrid = new InventoryItem[Columns, Rows];
            _equippedItems.Clear();
            _bmdPreviewCache.Clear();

            var characterItems = _network_manager_getitems();
            const string defaultItemIconTexturePath = DefaultItemIconPath;

            foreach (var entry in characterItems.Where(e => e.Key <= 11))
            {
                byte slotIndex = entry.Key;
                byte[] itemData = entry.Value;

                ItemDefinition itemDef = ItemDatabase.GetItemDefinition(itemData)
                    ?? new ItemDefinition(0, ItemDatabase.GetItemName(itemData) ?? "Unknown Item", 1, 1, defaultItemIconTexturePath);

                var invItem = new InventoryItem(itemDef, Point.Zero, itemData);
                invItem.Durability = ItemDatabase.GetItemDurability(itemData);

                _equippedItems[slotIndex] = invItem;
            }

            foreach (var entry in characterItems.Where(e => e.Key >= InventorySlotOffsetConstant))
            {
                byte slotIndex = entry.Key;
                byte[] itemData = entry.Value;

                int adjustedIndex = slotIndex - InventorySlotOffsetConstant;
                if (adjustedIndex < 0)
                {
                    _logger?.LogWarning("SlotIndex {SlotIndex} is below inventory offset. Skipping.", slotIndex);
                    continue;
                }

                int gridX = adjustedIndex % Columns;
                int gridY = adjustedIndex / Columns;

                if (gridX >= Columns || gridY >= Rows)
                {
                    string itemName = ItemDatabase.GetItemName(itemData) ?? "Unknown Item";
                    _logger?.LogWarning("Item at slot {SlotIndex} ({ItemName}) has invalid grid position ({GridX},{GridY}). Skipping.", slotIndex, itemName, gridX, gridY);
                    continue;
                }

                string itemNameFinal = ItemDatabase.GetItemName(itemData) ?? "Unknown Item";
                ItemDefinition itemDef = ItemDatabase.GetItemDefinition(itemData);
                if (itemDef == null)
                {
                    itemDef = new ItemDefinition(0, itemNameFinal, 1, 1, defaultItemIconTexturePath);
                }

                InventoryItem newItem = new(itemDef, new Point(gridX, gridY), itemData);

                newItem.Durability = ItemDatabase.GetItemDurability(itemData);

                if (!AddItem(newItem))
                {
                    _logger?.LogWarning("Failed to add item '{ItemName}' to inventory UI at slot {SlotIndex}. Slot might be occupied unexpectedly.", itemNameFinal, slotIndex);
                }
            }

            var preloadTasks = new List<Task>();
            foreach (var item in _items)
            {
                if (!string.IsNullOrEmpty(item.Definition.TexturePath) &&
                    !item.Definition.TexturePath.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase))
                {
                    preloadTasks.Add(TextureLoader.Instance.Prepare(item.Definition.TexturePath));
                }
            }

            if (preloadTasks.Count > 0)
            {
                _ = Task.WhenAll(preloadTasks);
            }

            // 這裡<b>不要</b>作廢靜態表面。
            //
            // 靜態表面畫的是面板的框、格線、裝備欄的空槽 —— 全部只跟版面有關，
            // 跟背包裡有什麼東西無關。每次收到背包更新（撿到、移動、賣掉都會發）
            // 就整張重畫一次，等於把 render target 還回池子再租一張新的：
            // 那一幀面板會閃一下。使用者回報的「點擊道具，UI 會閃爍一次」就是這個。
            //
            // 真正需要重畫的是版面改變時（Load、Show、OnScreenSizeChanged、
            // 切換修理模式），那幾處各自有呼叫。
        }

        private Dictionary<byte, byte[]> _network_manager_getitems()
        {
            return new Dictionary<byte, byte[]>(_networkManager.GetCharacterState().GetInventoryItems());
        }

        private void UpdateChromeHover(Point mousePos)
        {
            var closeRect = Translate(_closeButtonRect);
            var leftRect = Translate(_footerLeftButtonRect);
            var rightRect = Translate(_footerRightButtonRect);

            _closeHovered = closeRect.Contains(mousePos);
            _leftFooterHovered = leftRect.Contains(mousePos);
            _rightFooterHovered = rightRect.Contains(mousePos);
        }

        private bool HandleChromeClick()
        {
            if (_closeHovered)
            {
                Hide();
                return true;
            }

            if (_leftFooterHovered)
            {
                Hide();
                return true;
            }

            if (_rightFooterHovered)
            {
                ToggleRepairMode();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 選取背包格子裡的某個道具（內部就是「拿起」）。
        /// 若手上已經拿著別的道具，先把它放回原本的格子。
        /// </summary>
        private void SelectGridItem(InventoryItem item, Point mousePos)
        {
            if (item == null)
                return;

            if (_pickedItemRenderer.Item is InventoryItem carried && !ReferenceEquals(carried, item))
            {
                // 放回原本的格子，不送封包。
                // 一定要確保放得回去 —— AddItem 失敗的話道具會從畫面上消失
                // （它已經被移出 _items），要等下一次伺服器刷新才會回來。
                if (_pickedItemOriginalGrid.X >= 0 && _pickedItemOriginalGrid.Y >= 0)
                    carried.GridPosition = _pickedItemOriginalGrid;

                if (!AddItem(carried))
                {
                    _logger?.LogWarning(
                        "Could not return the carried item to slot {Slot}; requesting an inventory refresh.",
                        carried.GridPosition);
                    _networkManager?.GetCharacterState()?.RaiseInventoryChanged();
                }

                ReleasePickedItem();
            }

            _pickedItemRenderer.PickUpItem(item);
            _pickedItemOriginalGrid = item.GridPosition;
            _pickedAtMousePos = mousePos;
            _itemDragMoved = false;
            RemoveItemFromGrid(item);
            _items.Remove(item);
            _hoveredItem = null;
            _pickedFromEquipSlot = -1;
        }

        private void HandleInventoryInteraction(Point mousePos, bool leftJustPressed, bool leftJustReleased)
        {
            Point gridSlot = GetSlotAtScreenPosition(mousePos);
            _hoveredSlot = gridSlot;

            if (gridSlot.X != -1)
            {
                _hoveredItem = _itemGrid[gridSlot.X, gridSlot.Y];

                if (leftJustPressed)
                {
                    // Check if there's an item from VaultControl being dragged
                    var vaultDraggedItem = VaultControl.Instance?.GetDraggedItem();
                    if (vaultDraggedItem != null && _pickedItemRenderer.Item == null)
                    {
                        // VaultControl will handle the drop via its own AttemptDrop logic
                        // We just need to consume the mouse input to prevent picking up items underneath
                        Scene?.SetMouseInputConsumed();
                        return;
                    }

                    // Check if there's an item from TradeControl being dragged
                    var tradeDraggedItem = Client.Main.Controls.UI.Game.Trade.TradeControl.Instance?.GetDraggedItem();
                    if (tradeDraggedItem != null && _pickedItemRenderer.Item == null)
                    {
                        // TradeControl will handle the drop via its own AttemptDrop logic
                        Scene?.SetMouseInputConsumed();
                        return;
                    }

                    if (_pickedItemRenderer.Item != null)
                    {
                        if (CanPlaceItem(_pickedItemRenderer.Item, gridSlot))
                        {
                            InventoryItem itemToPlace = _pickedItemRenderer.Item;
                            if (_pickedItemOriginalGrid.X >= 0 && gridSlot == _pickedItemOriginalGrid)
                            {
                                itemToPlace.GridPosition = gridSlot;
                                AddItem(itemToPlace);
                                ReleasePickedItem();
                                return;
                            }

                            byte fromSlot = 0;
                            if (_pickedItemOriginalGrid.X >= 0)
                            {
                                fromSlot = (byte)(InventorySlotOffsetConstant + (_pickedItemOriginalGrid.Y * Columns) + _pickedItemOriginalGrid.X);
                            }
                            else if (_pickedFromEquipSlot >= 0)
                            {
                                fromSlot = (byte)_pickedFromEquipSlot;
                            }

                            byte toSlot = (byte)(InventorySlotOffsetConstant + (gridSlot.Y * Columns) + gridSlot.X);

                            itemToPlace.GridPosition = gridSlot;
                            AddItem(itemToPlace);

                            if (_networkManager != null)
                            {
                                var svc = _networkManager.GetCharacterService();
                                var version = _networkManager.TargetVersion;
                                var raw = itemToPlace.RawData ?? Array.Empty<byte>();
                                var state = _networkManager.GetCharacterState();
                                state.StashPendingInventoryMove(fromSlot, toSlot);
                                _ = Task.Run(async () =>
                                {
                                    await svc.SendItemMoveRequestAsync(fromSlot, toSlot, version, raw);
                                    await Task.Delay(1200);
                                    if (_networkManager != null && state.IsInventoryMovePending(fromSlot, toSlot))
                                    {
                                        MuGame.ScheduleOnMainThread(() => state.RaiseInventoryChanged());
                                    }
                                });
                            }

                            ReleasePickedItem();
                        }
                        else if (TryUsePickedJewelOnInventory(gridSlot))
                        {
                            return;
                        }
                        else if (s_mobile
                                 && _hoveredItem != null
                                 && _pickedFromEquipSlot < 0
                                 && !ReferenceEquals(_hoveredItem, _pickedItemRenderer.Item))
                        {
                            // 手機：點另一個道具 = 改選它。
                            //
                            // 原版是「點一下拿起、再點一下放下」，所以拿著東西時點別的道具
                            // 會被當成「放到它身上」—— 放不下就什麼事都不發生，玩家會覺得
                            // 「點了沒反應、左邊的圖也不換」。
                            // 移動仍然靠點空格子完成，不受影響。
                            SelectGridItem(_hoveredItem, mousePos);
                            return;
                        }
                    }
                    else if (_hoveredItem != null)
                    {
                        // Check if NPC shop is in repair mode
                        var npcShop = NpcShopControl.Instance;
                        if (npcShop != null && npcShop.Visible && npcShop.IsRepairMode)
                        {
                            // Repair mode - send repair request instead of picking up item
                            if (Core.Utilities.ItemPriceCalculator.IsRepairable(_hoveredItem))
                            {
                                byte itemSlot = (byte)(InventorySlotOffsetConstant + (_hoveredItem.GridPosition.Y * Columns) + _hoveredItem.GridPosition.X);
                                var svc = _networkManager?.GetCharacterService();
                                if (svc != null)
                                {
                                    _ = svc.SendRepairItemRequestAsync(itemSlot, false);
                                    SoundController.Instance.PlayBuffer("Sound/iButton.wav");
                                }
                            }
                            return;
                        }

                        // Check if in self repair mode
                        else if (_isRepairMode)
                        {
                            // Self repair mode - send repair request instead of picking up item
                            if (Core.Utilities.ItemPriceCalculator.IsRepairable(_hoveredItem))
                            {
                                byte itemSlot = (byte)(InventorySlotOffsetConstant + (_hoveredItem.GridPosition.Y * Columns) + _hoveredItem.GridPosition.X);
                                var svc = _networkManager?.GetCharacterService();
                                if (svc != null)
                                {
                                    _ = svc.SendRepairItemRequestAsync(itemSlot, true);
                                    SoundController.Instance.PlayBuffer("Sound/iButton.wav");
                                }
                            }
                            return;
                        }

                        // Normal mode - pick up item
                        SelectGridItem(_hoveredItem, mousePos);
                    }
                }

                bool rightJustPressed = MuGame.Instance.UiMouseState.RightButton == ButtonState.Pressed &&
                                        MuGame.Instance.PrevUiMouseState.RightButton == ButtonState.Released;

                if (rightJustPressed && _hoveredItem != null && _pickedItemRenderer.Item == null)
                {
                    if (_hoveredItem.Definition?.IsConsumable() == true)
                    {
                        if (_hoveredItem.Definition.IsUpgradeJewel())
                        {
                            return;
                        }

                        string itemName = _hoveredItem.Definition?.Name?.ToLowerInvariant() ?? string.Empty;
                        if (itemName.Contains("apple"))
                        {
                            SoundController.Instance.PlayBuffer("Sound/pEatApple.wav");
                        }
                        else
                        {
                            SoundController.Instance.PlayBuffer("Sound/pDrink.wav");
                        }

                        byte itemSlot = (byte)(InventorySlotOffsetConstant + (_hoveredItem.GridPosition.Y * Columns) + _hoveredItem.GridPosition.X);

                        if (_networkManager != null)
                        {
                            var svc = _networkManager.GetCharacterService();
                            _ = Task.Run(async () =>
                            {
                                await svc.SendConsumeItemRequestAsync(itemSlot);
                                await Task.Delay(300);

                                var state = _networkManager.GetCharacterState();
                                MuGame.ScheduleOnMainThread(() => state.RaiseInventoryChanged());
                            });
                        }
                    }
                }
            }
            else if (_hoveredEquipSlot >= 0)
            {
                if (leftJustPressed)
                {
                    if (_pickedItemRenderer.Item != null)
                    {
                        if (TryPlacePickedItemIntoEquipSlot((byte)_hoveredEquipSlot))
                        {
                            return;
                        }
                    }
                    else
                    {
                        if (_equippedItems.TryGetValue((byte)_hoveredEquipSlot, out var eqItem))
                        {
                            // Check if NPC shop is in repair mode
                            var npcShop = NpcShopControl.Instance;
                            if (npcShop != null && npcShop.Visible && npcShop.IsRepairMode)
                            {
                                // Repair mode - send repair request for equipped item
                                if (Core.Utilities.ItemPriceCalculator.IsRepairable(eqItem))
                                {
                                    byte equipSlot = (byte)_hoveredEquipSlot;
                                    var svc = _networkManager?.GetCharacterService();
                                    if (svc != null)
                                    {
                                        _ = svc.SendRepairItemRequestAsync(equipSlot, false);
                                        SoundController.Instance.PlayBuffer("Sound/iButton.wav");
                                    }
                                }
                                return;
                            }

                            // Check if in self repair mode
                            else if (_isRepairMode)
                            {
                                // Self repair mode - send repair request for equipped item
                                if (Core.Utilities.ItemPriceCalculator.IsRepairable(eqItem))
                                {
                                    byte equipSlot = (byte)_hoveredEquipSlot;
                                    var svc = _networkManager?.GetCharacterService();
                                    if (svc != null)
                                    {
                                        _ = svc.SendRepairItemRequestAsync(equipSlot, true);
                                        SoundController.Instance.PlayBuffer("Sound/iButton.wav");
                                    }
                                }
                                return;
                            }

                            // Normal mode - pick up equipped item
                            _pickedItemRenderer.PickUpItem(eqItem);
                            _equippedItems.Remove((byte)_hoveredEquipSlot);
                            _pickedFromEquipSlot = _hoveredEquipSlot;
                            _pickedItemOriginalGrid = new Point(-1, -1);
                            _pickedAtMousePos = mousePos;
                            _itemDragMoved = false;
                            _hoveredItem = eqItem;
                        }
                    }
                }
            }
        }

        private void HandleDropOutsideInventory()
        {
            var item = _pickedItemRenderer.Item;
            if (item == null)
            {
                return;
            }

            byte slotIndex = _pickedFromEquipSlot >= 0
                ? (byte)_pickedFromEquipSlot
                : (byte)(InventorySlotOffsetConstant + (item.GridPosition.Y * Columns) + item.GridPosition.X);

            var shop = NpcShopControl.Instance;
            if (shop != null && shop.Visible && shop.DisplayRectangle.Contains(MuGame.Instance.UiMouseState.Position))
            {
                var itemToSell = _pickedItem_renderer_item();
                var originalGrid = _pickedItemOriginalGrid;
                int fromEquipSlot = _pickedFromEquipSlot;

                ReleasePickedItem();

                ShowSellConfirmation(itemToSell, slotIndex, originalGrid, fromEquipSlot);
            }
            else if (VaultControl.Instance is { } vault &&
                     vault.Visible &&
                     vault.DisplayRectangle.Contains(MuGame.Instance.UiMouseState.Position) &&
                     _network_manager_exists())
            {
                var drop = vault.GetSlotAtScreenPosition(MuGame.Instance.UiMouseState.Position);
                if (drop.X >= 0 && vault.CanPlaceAt(drop, item))
                {
                    byte toSlot = (byte)(drop.Y * 8 + drop.X);
                    var svc = _networkManager.GetCharacterService();
                    var raw = item.RawData ?? Array.Empty<byte>();
                    var state = _networkManager.GetCharacterState();
                    state.StashPendingInventoryMove(slotIndex, slotIndex);

                    _ = Task.Run(async () =>
                    {
                        await svc.SendStorageItemMoveAsync(ItemStorageKind.Inventory, slotIndex, ItemStorageKind.Vault, toSlot, _networkManager.TargetVersion, raw);
                        await Task.Delay(1200);
                        if (_networkManager != null && state.IsInventoryMovePending(slotIndex, slotIndex))
                        {
                            MuGame.ScheduleOnMainThread(() =>
                            {
                                state.RaiseInventoryChanged();
                                state.RaiseVaultItemsChanged();
                            });
                        }
                    });

                    ReleasePickedItem();
                }
                else
                {
                    AddItem(item);
                    _networkManager?.GetCharacterState()?.RaiseInventoryChanged();
                    ReleasePickedItem();
                }
            }
            else if (Client.Main.Controls.UI.Game.ChaosMixControl.Instance is { } chaos &&
                     chaos.Visible &&
                     chaos.DisplayRectangle.Contains(MuGame.Instance.UiMouseState.Position) &&
                     _network_manager_exists())
            {
                var drop = chaos.GetSlotAtScreenPosition(MuGame.Instance.UiMouseState.Position);
                if (drop.X >= 0 && chaos.CanPlaceAt(drop, item))
                {
                    byte toSlot = (byte)(drop.Y * Client.Main.Controls.UI.Game.ChaosMixControl.Columns + drop.X);
                    var svc = _networkManager.GetCharacterService();
                    var raw = item.RawData ?? Array.Empty<byte>();
                    var state = _networkManager.GetCharacterState();
                    state.StashPendingInventoryMove(slotIndex, slotIndex);

                    _ = Task.Run(async () =>
                    {
                        await svc.SendStorageItemMoveAsync(ItemStorageKind.Inventory, slotIndex, ItemStorageKind.ChaosMachine, toSlot, _networkManager.TargetVersion, raw);
                        await Task.Delay(1200);
                        if (_networkManager != null && state.IsInventoryMovePending(slotIndex, slotIndex))
                        {
                            MuGame.ScheduleOnMainThread(() =>
                            {
                                state.RaiseInventoryChanged();
                                state.RaiseChaosMachineItemsChanged();
                            });
                        }
                    });

                    ReleasePickedItem();
                }
                else
                {
                    AddItem(item);
                    _networkManager?.GetCharacterState()?.RaiseInventoryChanged();
                    ReleasePickedItem();
                }
            }
            else if (Client.Main.Controls.UI.Game.Trade.TradeControl.Instance is { } trade &&
                     trade.Visible &&
                     trade.DisplayRectangle.Contains(MuGame.Instance.UiMouseState.Position) &&
                     _network_manager_exists())
            {
                var drop = trade.GetSlotAtScreenPosition(MuGame.Instance.UiMouseState.Position);
                if (drop.X >= 0 && trade.CanPlaceAt(drop, item))
                {
                    trade.AcceptItemFromInventory(item, drop, slotIndex);
                    ReleasePickedItem();
                }
                else
                {
                    AddItem(item);
                    _networkManager?.GetCharacterState()?.RaiseInventoryChanged();
                    ReleasePickedItem();
                }
            }
            else if (s_mobile)
            {
                // 手機不從這裡丟東西到地上。
                //
                // 桌面是「按住拖到視窗外再放開」，那是一個有意識的動作。觸控沒有
                // 「按住」這個狀態：點一下裝備就會把它拿在手上（見 HandleInventoryInteraction），
                // 手指離開螢幕並不會放回去，於是道具就一直懸在手上 —— 接下來只要
                // 在視窗外點任何一下（ATK、搖桿、空白處），就會被當成「拖到視窗外放開」，
                // 一件穿在身上的裝備就這樣沒了，而且沒有任何確認。
                //
                // 使用者已經遇到兩次。丟棄是不可逆的，它不該是「沒點中任何東西」的預設結果。
                // 手機要丟東西的話，之後在道具資訊欄放一個明確的按鈕，不走這條路。
                RestorePickedItemToOriginalLocation();
            }
            else if (Scene?.World is Controls.WalkableWorldControl world && _network_manager_exists())
            {
                byte tileX = world.MouseTileX;
                byte tileY = world.MouseTileY;

                _ = Task.Run(async () =>
                {
                    var svc = _networkManager.GetCharacterService();
                    await svc.SendDropItemRequestAsync(tileX, tileY, slotIndex);
                    await Task.Delay(1200);
                    var state = _networkManager.GetCharacterState();
                    if (state.HasInventoryItem(slotIndex))
                    {
                        MuGame.ScheduleOnMainThread(() => state.RaiseInventoryChanged());
                    }
                });

                ReleasePickedItem();
            }
            else
            {
                AddItem(item);
                ReleasePickedItem();
            }
        }

        private InventoryItem _pickedItem_renderer_item() => _pickedItemRenderer.Item;

        private bool _network_manager_exists() => _networkManager != null;

        private bool TryUsePickedJewelOnInventory(Point gridSlot)
        {
            if (!IsUpgradeJewel(_pickedItemRenderer.Item) || !IsWithinGrid(gridSlot))
            {
                return false;
            }

            var targetItem = _itemGrid[gridSlot.X, gridSlot.Y];
            if (targetItem == null)
            {
                return false;
            }

            byte targetSlot = (byte)(InventorySlotOffsetConstant + (targetItem.GridPosition.Y * Columns) + targetItem.GridPosition.X);
            return TryConsumePickedUpgradeJewel(targetSlot);
        }

        private bool TryUsePickedJewelOnEquipment(byte equipSlot)
        {
            if (!IsUpgradeJewel(_pickedItemRenderer.Item))
            {
                return false;
            }

            if (!_equippedItems.ContainsKey(equipSlot))
            {
                return false;
            }

            return TryConsumePickedUpgradeJewel(equipSlot);
        }

        private bool TryConsumePickedUpgradeJewel(byte targetSlot)
        {
            if (!IsUpgradeJewel(_pickedItemRenderer.Item))
            {
                return false;
            }

            byte? jewelSlot = GetPickedItemSlotIndex();
            if (jewelSlot == null)
            {
                _logger?.LogWarning("Cannot apply jewel: source slot is unknown.");
                return false;
            }

            if (_networkManager == null)
            {
                _logger?.LogWarning("Cannot apply jewel: not connected to the server.");
                RestorePickedItemToOriginalLocation();
                return true;
            }

            QueueConsumeItemRequest(jewelSlot.Value, targetSlot);
            ReleasePickedItem();
            return true;
        }

        private bool TryPlacePickedItemIntoEquipSlot(byte equipSlot)
        {
            var itemToPlace = _pickedItemRenderer.Item;
            if (itemToPlace == null)
            {
                return false;
            }

            if (TryUsePickedJewelOnEquipment(equipSlot))
            {
                return true;
            }

            byte fromSlot = 0;
            if (_pickedItemOriginalGrid.X >= 0)
            {
                fromSlot = (byte)(InventorySlotOffsetConstant + (_pickedItemOriginalGrid.Y * Columns) + _pickedItemOriginalGrid.X);
            }
            else if (_pickedFromEquipSlot >= 0)
            {
                fromSlot = (byte)_pickedFromEquipSlot;
            }

            byte toSlot = equipSlot;

            // If moving to the same slot, just put it back without sending request
            if (fromSlot == toSlot)
            {
                _equippedItems[toSlot] = itemToPlace;
                ReleasePickedItem();
                return true;
            }

            _equippedItems[toSlot] = itemToPlace;

            if (_networkManager != null)
            {
                var svc = _networkManager.GetCharacterService();
                var version = _networkManager.TargetVersion;
                var raw = itemToPlace.RawData ?? Array.Empty<byte>();
                var state = _networkManager.GetCharacterState();
                state.StashPendingInventoryMove(fromSlot, toSlot);
                _ = Task.Run(async () =>
                {
                    await svc.SendItemMoveRequestAsync(fromSlot, toSlot, version, raw);
                    await Task.Delay(1200);
                    if (_networkManager != null && state.IsInventoryMovePending(fromSlot, toSlot))
                    {
                        MuGame.ScheduleOnMainThread(() => state.RaiseInventoryChanged());
                    }
                });
            }

            ReleasePickedItem();
            return true;
        }

        private void QueueConsumeItemRequest(byte itemSlot, byte targetSlot)
        {
            if (_networkManager == null)
            {
                return;
            }

            var svc = _networkManager.GetCharacterService();
            _ = Task.Run(async () =>
            {
                await svc.SendConsumeItemRequestAsync(itemSlot, targetSlot);
                await Task.Delay(300);

                var state = _networkManager?.GetCharacterState();
                if (state != null)
                {
                    MuGame.ScheduleOnMainThread(() => state.RaiseInventoryChanged());
                }
            });
        }

        private byte? GetPickedItemSlotIndex()
        {
            if (_pickedItemOriginalGrid.X >= 0)
            {
                return (byte)(InventorySlotOffsetConstant + (_pickedItemOriginalGrid.Y * Columns) + _pickedItemOriginalGrid.X);
            }

            if (_pickedFromEquipSlot >= 0)
            {
                return (byte)_pickedFromEquipSlot;
            }

            return null;
        }

        private void RestorePickedItemToOriginalLocation()
        {
            var item = _pickedItemRenderer.Item;
            if (item == null)
            {
                return;
            }

            if (_pickedItemOriginalGrid.X >= 0)
            {
                item.GridPosition = _pickedItemOriginalGrid;
                AddItem(item);
            }
            else if (_pickedFromEquipSlot >= 0)
            {
                _equippedItems[(byte)_pickedFromEquipSlot] = item;
            }

            ReleasePickedItem();
        }

        private static bool IsUpgradeJewel(InventoryItem item)
        {
            return item?.Definition?.IsUpgradeJewel() == true;
        }

        private static bool IsWithinGrid(Point slot)
        {
            if (slot.X < 0 || slot.X >= Columns || slot.Y < 0 || slot.Y >= Rows)
                return false;

            // 5 欄 x 13 列 = 65，比實際的 64 格多一格。那一格不對應任何
            // 伺服器格號 —— 讓它通過的話會送出格號 64，伺服器會直接拒絕。
            return slot.Y * Columns + slot.X < TotalSlots;
        }

        private void PlaceItemOnGrid(InventoryItem item)
        {
            if (item?.Definition == null)
            {
                return;
            }

            for (int y = 0; y < item.Definition.Height; y++)
            {
                for (int x = 0; x < item.Definition.Width; x++)
                {
                    int gridX = item.GridPosition.X + x;
                    int gridY = item.GridPosition.Y + y;

                    if (gridX < Columns && gridY < Rows)
                    {
                        _itemGrid[gridX, gridY] = item;
                    }
                }
            }
        }

        private void RemoveItemFromGrid(InventoryItem item)
        {
            if (item?.Definition == null)
            {
                return;
            }

            for (int y = 0; y < item.Definition.Height; y++)
            {
                for (int x = 0; x < item.Definition.Width; x++)
                {
                    int gridX = item.GridPosition.X + x;
                    int gridY = item.GridPosition.Y + y;

                    if (gridX < Columns && gridY < Rows)
                    {
                        _itemGrid[gridX, gridY] = null;
                    }
                }
            }
        }

        private bool CanPlaceItem(InventoryItem itemToPlace, Point targetSlot)
        {
            if (itemToPlace == null || itemToPlace.Definition == null)
            {
                return false;
            }

            if (targetSlot.X < 0 || targetSlot.Y < 0 ||
                targetSlot.X + itemToPlace.Definition.Width > Columns ||
                targetSlot.Y + itemToPlace.Definition.Height > Rows)
            {
                return false;
            }

            // 5 欄 x 13 列 = 65，比實際的 64 格多出一格。這裡必須用 IsWithinGrid
            // 而不是只比對 Columns / Rows —— 只比對邊界的話，最後那一格會被當成
            // 可以放，於是送出格號 64，伺服器直接拒絕，道具就卡在半途。
            // FindFreeSlot 是走這個函式找空位的，所以補在這裡兩邊都涵蓋到。
            if (!IsWithinGrid(targetSlot) ||
                !IsWithinGrid(new Point(
                    targetSlot.X + itemToPlace.Definition.Width - 1,
                    targetSlot.Y + itemToPlace.Definition.Height - 1)))
            {
                return false;
            }

            for (int y = 0; y < itemToPlace.Definition.Height; y++)
            {
                for (int x = 0; x < itemToPlace.Definition.Width; x++)
                {
                    int checkX = targetSlot.X + x;
                    int checkY = targetSlot.Y + y;

                    if (checkX >= Columns || checkY >= Rows)
                    {
                        return false;
                    }

                    if (_itemGrid[checkX, checkY] != null)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool TryFindFirstFreeSlot(InventoryItem item, out Point slot)
        {
            slot = new Point(-1, -1);
            if (item?.Definition == null)
            {
                return false;
            }

            for (int y = 0; y <= Rows - item.Definition.Height; y++)
            {
                for (int x = 0; x <= Columns - item.Definition.Width; x++)
                {
                    var candidate = new Point(x, y);
                    if (CanPlaceItem(item, candidate))
                    {
                        slot = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private static string BuildItemDisplayName(InventoryItem item)
        {
            if (item == null)
            {
                return "item";
            }

            string name = item.Definition?.Name ?? ItemDatabase.GetItemName(item.RawData) ?? "item";
            if (item.Details.Level > 0)
            {
                name += $" +{item.Details.Level}";
            }

            if (item.Definition?.BaseDurability == 0 && item.Definition.MagicDurability == 0 && item.Durability > 1)
            {
                name += $" x{item.Durability}";
            }

            return name;
        }

        private void ShowSellConfirmation(InventoryItem item, byte slotIndex, Point originalGrid, int fromEquipSlot)
        {
            if (item == null)
            {
                return;
            }

            if (_networkManager == null)
            {
                MessageWindow.Show("No connection to server. Sale is not possible.");
                RestoreItemAfterCancelledSell(item, originalGrid, fromEquipSlot);
                return;
            }

            var definition = item.Definition;
            if (definition == null)
            {
                MessageWindow.Show("Cannot identify the selected item.");
                RestoreItemAfterCancelledSell(item, originalGrid, fromEquipSlot);
                return;
            }

            string displayName = BuildItemDisplayName(item);

            if (!definition.CanSellToNpc)
            {
                MessageWindow.Show($"Item '{displayName}' cannot be sold to NPC shop.");
                RestoreItemAfterCancelledSell(item, originalGrid, fromEquipSlot);
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine($"Sell {displayName}?");
            if (definition.IsExpensive)
            {
                builder.AppendLine();
                builder.AppendLine("WARNING: This item is marked as expensive.");
            }

            RequestDialog.Show(
                builder.ToString(),
                onAccept: () => ExecuteSellToNpc(slotIndex),
                onReject: () => RestoreItemAfterCancelledSell(item, originalGrid, fromEquipSlot),
                acceptText: "Sell",
                rejectText: "Cancel");
        }

        private void ExecuteSellToNpc(byte slotIndex)
        {
            if (_networkManager == null)
            {
                MessageWindow.Show("No connection to server. Sale is not possible.");
                return;
            }

            var svc = _networkManager.GetCharacterService();
            if (svc == null)
            {
                MessageWindow.Show("Failed to connect to NPC shop server.");
                return;
            }

            var state = _networkManager.GetCharacterState();
            state.StashPendingSellSlot(slotIndex);

            _ = Task.Run(async () =>
            {
                try
                {
                    await svc.SendSellItemToNpcRequestAsync(slotIndex);
                    await Task.Delay(1200);

                    var refreshedState = _networkManager?.GetCharacterState();
                    if (refreshedState != null && refreshedState.HasInventoryItem(slotIndex))
                    {
                        MuGame.ScheduleOnMainThread(refreshedState.RaiseInventoryChanged);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error while sending item sale request from slot {Slot}.", slotIndex);
                    var refreshedState = _networkManager?.GetCharacterState();
                    if (refreshedState != null)
                    {
                        MuGame.ScheduleOnMainThread(refreshedState.RaiseInventoryChanged);
                    }

                    MuGame.ScheduleOnMainThread(() => MessageWindow.Show("Failed to sell item. Please try again."));
                }
            });
        }

        private void RestoreItemAfterCancelledSell(InventoryItem item, Point originalGrid, int fromEquipSlot)
        {
            if (item == null)
            {
                return;
            }

            if (fromEquipSlot >= 0)
            {
                _equippedItems[(byte)fromEquipSlot] = item;
                _networkManager?.GetCharacterState()?.RaiseEquipmentChanged();
                return;
            }

            Point targetSlot = originalGrid;
            if (targetSlot.X < 0 || targetSlot.Y < 0 || !CanPlaceItem(item, targetSlot))
            {
                if (!TryFindFirstFreeSlot(item, out targetSlot))
                {
                    _logger?.LogWarning("No free space to restore item '{Name}' in inventory.", item.Definition?.Name ?? "Unknown");
                    MessageWindow.Show("No space in inventory to restore item.");
                    return;
                }
            }

            item.GridPosition = targetSlot;
            if (!AddItem(item))
            {
                _logger?.LogWarning("Failed to restore item '{Name}' to inventory.", item.Definition?.Name ?? "Unknown");
                MessageWindow.Show("Restoring item to inventory failed.");
            }
        }

        /// <summary>
        /// 格線右側的捲軸。沒有它的話，玩家不會知道下面還有 8 列 ——
        /// 一個看起來剛好放滿的格線和一個「還有更多」的格線長得一模一樣。
        /// </summary>
        private void DrawGridScrollbar(SpriteBatch spriteBatch)
        {
            if (!s_mobile || MaxGridScrollRow <= 0)
                return;

            var grid = Translate(_gridRect);
            var track = new Rectangle(grid.Right + 4, grid.Y, 6, grid.Height);

            float visibleRatio = _gridVisibleRows / (float)Rows;
            int thumbHeight = Math.Max(28, (int)(track.Height * visibleRatio));
            int travel = track.Height - thumbHeight;
            int thumbY = track.Y + (int)(travel * (_gridScrollRow / (float)MaxGridScrollRow));

            MobileUi.DrawScrollbar(
                spriteBatch, track,
                new Rectangle(track.X, thumbY, track.Width, thumbHeight),
                _gridDragging);
        }

        private void DrawInventoryItems(SpriteBatch spriteBatch)
        {
            if (GraphicsManager.Instance.Pixel == null || GraphicsManager.Instance.Font == null)
                return;

            _jewelEntries.Clear();

            Point gridTopLeft = Translate(_gridRect).Location;
            gridTopLeft.Y -= _gridScrollRow * INVENTORY_SQUARE_HEIGHT;
            var font = GraphicsManager.Instance.Font;
            var pixel = GraphicsManager.Instance.Pixel;

            // Cache items count and iterate without creating a copy
            int itemCount = _items.Count;
            for (int i = 0; i < itemCount; i++)
            {
                var item = _items[i];
                if (item == _pickedItem_renderer_item())
                    continue;

                // Skip items that are in pending move (being transferred to vault/trade)
                var state = _networkManager?.GetCharacterState();
                byte itemSlotIndex = (byte)(InventorySlotOffsetConstant + (item.GridPosition.Y * Columns) + item.GridPosition.X);
                if (state?.PendingMoveFromSlot == itemSlotIndex)
                    continue;

                Rectangle itemRect = new(
                    gridTopLeft.X + item.GridPosition.X * INVENTORY_SQUARE_WIDTH,
                    gridTopLeft.Y + item.GridPosition.Y * INVENTORY_SQUARE_HEIGHT,
                    item.Definition.Width * INVENTORY_SQUARE_WIDTH,
                    item.Definition.Height * INVENTORY_SQUARE_HEIGHT);

                // Item glow effect
                Color glowColor = ItemUiHelper.GetItemGlowColor(item, GlowPalette);
                if (glowColor.A > 0)
                {
                    ItemUiHelper.DrawItemGlow(spriteBatch, pixel, itemRect, glowColor);
                }

                // Item texture
                Texture2D itemTexture = ResolveItemTexture(item, itemRect.Width, itemRect.Height);

                if (itemTexture != null)
                {
                    spriteBatch.Draw(itemTexture, itemRect, Color.White);

                    if (JewelShineOverlay.ShouldShine(item))
                    {
                        _jewelEntries.Add((item, itemRect));
                    }
                }
                else
                {
                    // Placeholder
                    ItemGridRenderHelper.DrawItemPlaceholder(spriteBatch, pixel, font, itemRect, item, Theme.BgLighter, Theme.TextGray * 0.8f);
                }

                // Stack count
                if (item.Definition.BaseDurability == 0 && item.Definition.MagicDurability == 0 && item.Durability > 1)
                {
                    ItemGridRenderHelper.DrawItemStackCount(spriteBatch, font, itemRect, item.Durability, Theme.TextGold, 1f);
                }

                // Level indicator
                if (item.Details.Level > 0)
                {
                    ItemGridRenderHelper.DrawItemLevelBadge(spriteBatch, pixel, font, itemRect, item.Details.Level,
                                       lvl => lvl >= 9 ? Theme.Danger :
                                              lvl >= 7 ? Theme.Accent :
                                              lvl >= 4 ? Theme.Secondary :
                                              Theme.TextGray,
                                       new Color(0, 0, 0, 180));
                }

                if (_jewelEntries.Count > 0)
                {
                    JewelShineOverlay.DrawBatch(spriteBatch, _jewelEntries, _currentGameTime, Alpha, UiScaler.SpriteTransform);
                }
            }
        }

        private void DrawEquippedItems(SpriteBatch spriteBatch)
        {
            foreach (var kv in _equippedItems)
            {
                if (!_equipSlots.TryGetValue(kv.Key, out var slot))
                {
                    continue;
                }

                var item = kv.Value;
                Rectangle itemRect = Translate(slot.Rect);

                Texture2D itemTexture = ResolveItemTexture(item, itemRect.Width, itemRect.Height);

                if (itemTexture != null)
                {
                    spriteBatch.Draw(itemTexture, itemRect, Color.White);
                }
                else if (GraphicsManager.Instance?.Pixel != null)
                {
                    spriteBatch.Draw(GraphicsManager.Instance.Pixel, itemRect, new Color(40, 40, 40, 200));
                }
            }
        }

        private Texture2D ResolveItemTexture(InventoryItem item, int width, int height)
        {
            if (item == null)
            {
                return null;
            }

            string texturePath = item.Definition?.TexturePath;
            if (string.IsNullOrEmpty(texturePath))
            {
                return null;
            }

            bool isBmd = texturePath.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase);

            if (!isBmd)
            {
                if (_itemTextureCache.TryGetValue(texturePath, out var cachedTexture) && cachedTexture != null)
                {
                    return cachedTexture;
                }

                var texture = TextureLoader.Instance.GetTexture2D(texturePath);
                if (texture != null)
                {
                    _itemTextureCache[texturePath] = texture;
                }
                return texture;
            }

            bool isHovered = item == _hoveredItem;

            // Material animation for non-hovered items (if enabled)
            if (!isHovered && Constants.ENABLE_ITEM_MATERIAL_ANIMATION)
            {
                try
                {
                    var animatedMaterial = BmdPreviewRenderer.GetMaterialAnimatedPreview(item, width, height, _currentGameTime);
                    if (animatedMaterial != null)
                    {
                        return animatedMaterial;
                    }
                }
                catch
                {
                    // ignore and fall back below
                }
            }

            if (isHovered)
            {
                try
                {
                    return BmdPreviewRenderer.GetSmoothAnimatedPreview(item, width, height, _currentGameTime);
                }
                catch
                {
                    return null;
                }
            }

            // Use cached static preview
            var cacheKey = (item, width, height, false);
            if (_bmdPreviewCache.TryGetValue(cacheKey, out var previewTexture) && previewTexture != null)
            {
                return previewTexture;
            }

            try
            {
                previewTexture = BmdPreviewRenderer.GetPreview(item, width, height);
                if (previewTexture != null)
                {
                    _bmdPreviewCache[cacheKey] = previewTexture;
                }
                return previewTexture;
            }
            catch
            {
                return null;
            }
        }

        private void DrawGridOverlays(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance?.Pixel;
            if (pixel == null)
            {
                return;
            }

            bool isOverGrid = IsMouseOverGrid();

            // Early exit if nothing to draw
            if (!isOverGrid)
            {
                return;
            }

            // 手機：游標會停在最後一次觸控的位置，高亮於是永遠留在那一格 ——
            // 空格子看起來像「被選中了」，玩家會以為那裡有東西。
            // 只在手指按住時顯示高亮。
            if (s_mobile && MuGame.Instance.UiMouseState.LeftButton != ButtonState.Pressed)
            {
                return;
            }

            Rectangle gridRect = Translate(_gridRect);
            var dragged = _pickedItem_renderer_item() ?? VaultControl.Instance?.GetDraggedItem();

            if (dragged != null)
            {
                if (_hoveredSlot.X >= 0 && _hoveredSlot.Y >= 0)
                {
                    bool canPlace = CanPlaceItem(dragged, _hoveredSlot);
                    Color overlay = canPlace ? Color.GreenYellow * 0.5f : Color.Red * 0.6f;

                    Rectangle dropRect = new(
                        gridRect.X + _hoveredSlot.X * INVENTORY_SQUARE_WIDTH,
                        gridRect.Y + _hoveredSlot.Y * INVENTORY_SQUARE_HEIGHT,
                        dragged.Definition.Width * INVENTORY_SQUARE_WIDTH,
                        dragged.Definition.Height * INVENTORY_SQUARE_HEIGHT);

                    spriteBatch.Draw(pixel, dropRect, overlay);
                }
            }
            else
            {
                // 手機同上：沒有按著就不畫停留效果。
                if (s_mobile && MuGame.Instance.UiMouseState.LeftButton != ButtonState.Pressed)
                {
                    return;
                }

                // Match vault/NPC shop hover overlays: highlight hovered slot and occupied slots only
                ItemGridRenderHelper.DrawGridOverlays(
                    spriteBatch,
                    pixel,
                    DisplayRectangle,
                    _gridRect,
                    _hoveredItem,
                    _hoveredSlot,
                    INVENTORY_SQUARE_WIDTH,
                    INVENTORY_SQUARE_HEIGHT,
                    Theme.SlotHover,
                    Theme.Secondary,
                    Alpha);
            }
        }

        private void DrawEquipHighlights(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance?.Pixel;
            if (pixel == null || _hoveredEquipSlot < 0)
            {
                return;
            }

            // 手機只在手指按著的時候才畫。放開之後游標停在最後一次觸控的位置，
            // 那一格就會永遠亮著 —— 使用者回報那看起來像「等待刪除」的狀態。
            if (s_mobile && MuGame.Instance.UiMouseState.LeftButton != ButtonState.Pressed)
            {
                return;
            }

            if (!_equipSlots.TryGetValue((byte)_hoveredEquipSlot, out var layout))
            {
                return;
            }

            Rectangle rect = Translate(layout.Rect);
            Color overlay = layout.AccentRed ? Theme.Danger * 0.45f : Theme.Secondary * 0.45f;
            spriteBatch.Draw(pixel, rect, overlay);

            // Border highlight
            Color light = layout.AccentRed ? Theme.Danger : Theme.Accent;
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), light * 0.8f);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), Theme.BorderOuter);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), light * 0.6f);
            spriteBatch.Draw(pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), Theme.BorderOuter);
        }

        private void DrawTexts(SpriteBatch spriteBatch)
        {
            if (_font == null)
            {
                return;
            }

            Vector2 basePosition = DisplayRectangle.Location.ToVector2();
            foreach (var entry in _texts)
            {
                if (entry == null || !entry.Visible || string.IsNullOrEmpty(entry.Text))
                {
                    continue;
                }

                float textScale = entry.FontScale * Scale;
                Vector2 pos = basePosition + entry.BasePosition * Scale;
                Vector2 size = _font.MeasureString(entry.Text) * textScale;

                switch (entry.Alignment)
                {
                    case TextAlignment.Center:
                        pos.X -= size.X * 0.5f;
                        break;
                    case TextAlignment.Right:
                        pos.X -= size.X;
                        break;
                }

                spriteBatch.DrawString(_font, entry.Text, pos, entry.Color * Alpha, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
            }
        }

        private void DrawChrome(SpriteBatch spriteBatch)
        {
            if (GraphicsManager.Instance?.Pixel == null)
            {
                return;
            }

            DrawCloseButton(spriteBatch);

            if (_footerLeftButtonRect.Width > 0)
                DrawFooterButton(spriteBatch, _footerLeftButtonRect, "X", _leftFooterHovered);
            string buttonText = (_networkManager?.GetCharacterState()?.Level >= _repairEnableLevel) ? "R" : "+";
            DrawFooterButton(spriteBatch, _footerRightButtonRect, buttonText, _rightFooterHovered);
        }

        private void DrawCloseButton(SpriteBatch spriteBatch)
        {
            var rect = Translate(_closeButtonRect);
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            bool hovered = _closeHovered;

            if (s_mobile)
            {
                // 一塊底 + 一個叉，沒有紅色、沒有高光、沒有外框。
                // 整個面板只有這一顆按鈕是飽和色的話，眼睛會一直被它拉過去 ——
                // 而關閉鈕不是玩家開背包時要找的東西。
                MobileUi.DrawCloseGlyph(spriteBatch, rect, hovered);
                return;
            }

            // Hover glow
            if (hovered)
            {
                var glowRect = new Rectangle(rect.X - 3, rect.Y - 3, rect.Width + 6, rect.Height + 6);
                spriteBatch.Draw(pixel, glowRect, Theme.Danger * 0.3f);
            }

            // Button background - circular feel with rounded corners simulated
            Color bgColor = hovered ? new Color(180, 60, 50) : new Color(140, 50, 45);
            spriteBatch.Draw(pixel, rect, bgColor);

            // Highlight
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2),
                            hovered ? new Color(255, 120, 100) : new Color(200, 90, 80));

            // Border
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), new Color(100, 30, 25));
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), new Color(60, 20, 15));

            // X icon
            if (_font != null)
            {
                string text = "X";
                float scale = MobileUi.ScaleFor(MobileUi.TextHeading);
                Vector2 size = _font.MeasureString(text) * scale;
                Vector2 pos = new(rect.X + (rect.Width - size.X) / 2, rect.Y + (rect.Height - size.Y) / 2);

                spriteBatch.DrawString(_font, text, pos + Vector2.One, Color.Black * 0.5f,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                spriteBatch.DrawString(_font, text, pos, Color.White,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
        }

        private void DrawFooterButton(SpriteBatch spriteBatch, Rectangle rectLocal, string text, bool hovered)
        {
            var rect = Translate(rectLocal);
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null) return;

            // Hover glow
            if (hovered)
            {
                var glowRect = new Rectangle(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4);
                spriteBatch.Draw(pixel, glowRect, Theme.Accent * 0.3f);
            }

            // Button background
            Color bgTop = hovered ? Theme.BgLighter : Theme.BgLight;
            Color bgBottom = hovered ? Theme.BgMid : Theme.BgDark;
            UiDrawHelper.DrawVerticalGradient(spriteBatch, rect, bgTop, bgBottom);

            // Border
            Color borderTop = hovered ? Theme.Accent : Theme.BorderInner;
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), borderTop);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), Theme.BorderOuter);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), borderTop * 0.7f);
            spriteBatch.Draw(pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), Theme.BorderOuter);

            // Inner highlight
            if (hovered)
            {
                spriteBatch.Draw(pixel, new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, 1), Theme.AccentBright * 0.3f);
            }

            // Text
            if (_font != null)
            {
                float scale = MobileUi.ScaleFor(MobileUi.TextHeading);
                Vector2 size = _font.MeasureString(text) * scale;
                Vector2 pos = new(rect.X + (rect.Width - size.X) / 2, rect.Y + (rect.Height - size.Y) / 2);

                spriteBatch.DrawString(_font, text, pos + new Vector2(1, 1), Color.Black * 0.6f,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                spriteBatch.DrawString(_font, text, pos, hovered ? Theme.AccentBright : Theme.Accent,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
        }

        private void DrawTooltip(SpriteBatch spriteBatch)
        {
            if (_font == null)
                return;

            // 手機：點一下道具就是「選取」，資訊要立刻出來。
            // 桌面沿用原本的規則 —— 手上拿著東西時不顯示提示。
            InventoryItem infoItem;
            if (s_mobile)
            {
                infoItem = GetMobileInfoItem();
            }
            else if (_pickedItem_renderer_item() != null)
            {
                return;   // 桌面：手上拿著東西時不顯示提示
            }
            else
            {
                infoItem = _hoveredItem;
            }

            if (infoItem == null)
                return;

            var lines = ItemUiHelper.BuildTooltipLines(infoItem);
            if (NpcShopControl.IsOpen)
            {
                int sellPrice = ItemPriceCalculator.CalculateSellPrice(infoItem);
                if (sellPrice > 0)
                {
                    lines.Add(($"Sell Price: {sellPrice} Zen", Theme.TextGold));
                }
            }
            else if (_isRepairMode)
            {
                if (Core.Utilities.ItemPriceCalculator.IsRepairable(infoItem))
                {
                    int repairCost = (int)(Core.Utilities.ItemPriceCalculator.CalculateRepairPrice(infoItem, false) * 2.5);
                    if (repairCost > 0)
                    {
                        lines.Add(($"Self Repair Cost: {repairCost} Zen", Theme.TextGold));
                    }
                }
                else
                {
                    lines.Add(("Cannot be repaired", new Color(255, 100, 100)));
                }
            }
            float scale = MobileUi.ScaleFor(MobileUi.TextBody);
            const int lineSpacing = 4;
            const int paddingX = 14;
            const int paddingY = 12;

            // Calculate tooltip size
            int maxWidth = 0;
            int totalHeight = 0;

            foreach (var (text, _) in lines)
            {
                Vector2 sz = _font.MeasureString(text) * scale;
                maxWidth = Math.Max(maxWidth, (int)MathF.Ceiling(sz.X));
                totalHeight += (int)MathF.Ceiling(sz.Y) + lineSpacing;
            }

            // Add separator after the first line
            totalHeight += 6;

            int tooltipWidth = maxWidth + paddingX * 2;
            int tooltipHeight = totalHeight + paddingY * 2;

            Point mousePosition = MuGame.Instance.UiMouseState.Position;
            Rectangle screenBounds = new(0, 0, UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y);

            int previewSize = 0;

            if (s_mobile)
            {
                // 手機不用浮動提示框。跟著手指跑的提示一定會擋住旁邊的格子，
                // 而且手指本身就壓在上面 —— 那是滑鼠時代的做法。
                // 改成視窗裡的固定兩欄：最左邊放立體圖，最右邊放文字。
                DrawMobileItemDetail(spriteBatch, infoItem, lines, scale, lineSpacing);
                return;
            }
            // Hovered item position
            Rectangle hoveredItemRect;
            if (_hoveredEquipSlot >= 0 && _equipSlots.TryGetValue((byte)_hoveredEquipSlot, out var layout))
            {
                hoveredItemRect = Translate(layout.Rect);
            }
            else
            {
                Point gridTopLeft = Translate(_gridRect).Location;
                gridTopLeft.Y -= _gridScrollRow * INVENTORY_SQUARE_HEIGHT;
                hoveredItemRect = new Rectangle(
                    gridTopLeft.X + infoItem.GridPosition.X * INVENTORY_SQUARE_WIDTH,
                    gridTopLeft.Y + infoItem.GridPosition.Y * INVENTORY_SQUARE_HEIGHT,
                    infoItem.Definition.Width * INVENTORY_SQUARE_WIDTH,
                    infoItem.Definition.Height * INVENTORY_SQUARE_HEIGHT);
            }

            // Tooltip positioning
            Rectangle tooltipRect = new(mousePosition.X + 16, mousePosition.Y + 16, tooltipWidth, tooltipHeight);

            // Avoid overlapping the item
            if (tooltipRect.Intersects(hoveredItemRect))
            {
                // Try left side
                tooltipRect.X = hoveredItemRect.X - tooltipWidth - 8;
                tooltipRect.Y = hoveredItemRect.Y;

                if (tooltipRect.X < 10 || tooltipRect.Intersects(hoveredItemRect))
                {
                    // Try above
                    tooltipRect.X = hoveredItemRect.X;
                    tooltipRect.Y = hoveredItemRect.Y - tooltipHeight - 8;

                    if (tooltipRect.Y < 10)
                    {
                        // Under the item
                        tooltipRect.X = hoveredItemRect.X;
                        tooltipRect.Y = hoveredItemRect.Bottom + 8;
                    }
                }
            }

            // Clamp to screen bounds
            tooltipRect.X = Math.Clamp(tooltipRect.X, 10, screenBounds.Right - tooltipRect.Width - 10);
            tooltipRect.Y = Math.Clamp(tooltipRect.Y, 10, screenBounds.Bottom - tooltipRect.Height - 10);

            DrawTooltipBody(spriteBatch, tooltipRect, lines, infoItem, scale, lineSpacing, paddingX, paddingY, previewSize);
        }

        /// <summary>目前要顯示資訊的道具：手上拿著的優先，其次是游標所在的。</summary>
        private InventoryItem GetMobileInfoItem()
            => _pickedItem_renderer_item() as InventoryItem ?? _hoveredItem;

        /// <summary>
        /// 兩個詳細資訊欄的底。沒有選道具時要畫出提示，
        /// 否則整片空白看起來像介面壞了。
        /// </summary>
        private void DrawMobileDetailColumns(SpriteBatch spriteBatch)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null || _font == null)
                return;

            var previewRect = Translate(_previewPanelRect);
            var infoRect = Translate(_infoPanelRect);

            spriteBatch.Draw(pixel, previewRect, new Color(10, 13, 19) * 0.35f);
            spriteBatch.Draw(pixel, infoRect, new Color(10, 13, 19) * 0.35f);

            if (GetMobileInfoItem() != null)
                return;

            // 遊戲的字型沒有中文字符，中文會整串變成 ??????（實機截圖確認）。
            // 這一層的字串一律用英文，與其他介面文字也一致。
            const string hint = "SELECT AN ITEM";
            float scale = MobileUi.ScaleFor(MobileUi.TextBody);
            var size = _font.MeasureString(hint) * scale;
            var position = new Vector2(
                infoRect.X + (infoRect.Width - size.X) * 0.5f,
                infoRect.Y + (infoRect.Height - size.Y) * 0.5f);

            spriteBatch.DrawString(_font, hint, position, Theme.TextDark, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// 手機的道具詳細資訊：最左欄一張大立體圖，最右欄文字。
        ///
        /// MU 的道具圖示<b>不是圖片，是即時算出來的 3D 模型</b>（<see cref="BmdPreviewRenderer"/>），
        /// 所以想放多大就放多大。背包格子只佔一格看不清細節，就靠這一欄補回來。
        /// </summary>
        private void DrawMobileItemDetail(
            SpriteBatch spriteBatch,
            InventoryItem infoItem,
            System.Collections.Generic.List<(string Text, Color Color)> lines,
            float scale,
            int lineSpacing)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null || _font == null)
                return;

            var previewRect = Translate(_previewPanelRect);
            var infoRect = Translate(_infoPanelRect);

            // ── 立體圖 ──
            if (previewRect.Width > 40 && previewRect.Height > 40)
            {
                int side = Math.Min(previewRect.Width - 16, previewRect.Height - 16);
                var imageRect = new Rectangle(
                    previewRect.X + (previewRect.Width - side) / 2,
                    previewRect.Y + 12,
                    side, side);

                try
                {
                    // 用會轉動的版本 —— MU 的道具本來就是即時算的 3D 模型，
                    // 靜態的那一張會讓人以為只是圖片。
                    var preview = _currentGameTime != null
                        ? BmdPreviewRenderer.GetSmoothAnimatedPreview(infoItem, side, side, _currentGameTime)
                        : BmdPreviewRenderer.GetPreview(infoItem, side, side);

                    if (preview != null)
                        spriteBatch.Draw(preview, imageRect, Color.White);
                }
                catch
                {
                    // 模型還沒載好就先留空，下一幀會補上
                }
            }

            // ── 文字 ──
            if (infoRect.Width < 80)
                return;

            // 字級要縮到最長的一行剛好塞得進欄寬。
            // 「Can be equipped by Magic Gladiator」這種句子比欄位寬，
            // 不縮的話會整片溢出到視窗外面（實機截圖確認過）。
            // 量測整份清單不便宜，而它只在「選了不同的道具」或欄寬改變時才會變。
            int available = infoRect.Width - 20;
            if (!ReferenceEquals(_infoScaleItem, infoItem) || _infoScaleWidth != available)
            {
                float widest = 0f;
                foreach (var (text, _) in lines)
                    widest = Math.Max(widest, _font.MeasureString(text).X);

                _infoScaleItem = infoItem;
                _infoScaleWidth = available;
                _infoScale = Math.Max(widest > 0f ? Math.Min(scale, available / widest) : scale, 0.26f);
            }

            float fitScale = _infoScale;

            int textY = infoRect.Y + 12;
            bool isFirstLine = true;

            foreach (var (text, color) in lines)
            {
                Vector2 textSize = _font.MeasureString(text) * fitScale;
                if (textY + textSize.Y > infoRect.Bottom - 8)
                    break;   // 塞不下就停，不要溢出到背包格子上

                var position = new Vector2(infoRect.X + 10, textY);
                spriteBatch.DrawString(_font, text, position + Vector2.One, Color.Black * 0.7f,
                                       0f, Vector2.Zero, fitScale, SpriteEffects.None, 0f);
                spriteBatch.DrawString(_font, text, position, isFirstLine ? Theme.TextGold : color,
                                       0f, Vector2.Zero, fitScale, SpriteEffects.None, 0f);

                textY += (int)textSize.Y + lineSpacing;

                if (isFirstLine)
                {
                    textY += 4;
                    spriteBatch.Draw(pixel, new Rectangle(infoRect.X + 10, textY, infoRect.Width - 20, 1),
                                     Theme.Accent * 0.3f);
                    textY += 6;
                    isFirstLine = false;
                }
            }
        }

        /// <summary>
        /// 畫出道具資訊框本體。位置由呼叫端決定 —— 桌面跟著游標，手機固定在視窗旁邊。
        /// </summary>
        private void DrawTooltipBody(
            SpriteBatch spriteBatch,
            Rectangle tooltipRect,
            System.Collections.Generic.List<(string Text, Color Color)> lines,
            InventoryItem infoItem,
            float scale,
            int lineSpacing,
            int paddingX,
            int paddingY,
            int previewSize)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null || _font == null) return;

            // ═══════════════════════════════════════════════════════════
            // TOOLTIP BACKGROUND
            // ═══════════════════════════════════════════════════════════

            // Drop shadow
            var shadowRect = new Rectangle(tooltipRect.X + 4, tooltipRect.Y + 4, tooltipRect.Width, tooltipRect.Height);
            spriteBatch.Draw(pixel, shadowRect, Color.Black * 0.5f);

            // Main background
            UiDrawHelper.DrawVerticalGradient(spriteBatch, tooltipRect, new Color(20, 24, 32, 252), new Color(12, 14, 18, 254));

            // Border color based on item rarity
            bool isExcellent = infoItem.Details.IsExcellent;
            bool isAncient = infoItem.Details.IsAncient;
            bool isHighLevel = infoItem.Details.Level >= 7;

            Color borderColor = isExcellent ? Theme.GlowExcellent :
                                isAncient ? Theme.GlowAncient :
                                isHighLevel ? Theme.Accent :
                                Theme.TextWhite;

            // Uniform border all around
            const int borderThickness = 2;
            spriteBatch.Draw(pixel, new Rectangle(tooltipRect.X, tooltipRect.Y, tooltipRect.Width, borderThickness), borderColor);
            spriteBatch.Draw(pixel, new Rectangle(tooltipRect.X, tooltipRect.Bottom - borderThickness, tooltipRect.Width, borderThickness), borderColor);
            spriteBatch.Draw(pixel, new Rectangle(tooltipRect.X, tooltipRect.Y, borderThickness, tooltipRect.Height), borderColor);
            spriteBatch.Draw(pixel, new Rectangle(tooltipRect.Right - borderThickness, tooltipRect.Y, borderThickness, tooltipRect.Height), borderColor);

            // ═══════════════════════════════════════════════════════════
            // TOOLTIP TEXT
            // ═══════════════════════════════════════════════════════════

            int textY = tooltipRect.Y + paddingY;

            if (previewSize > 0)
            {
                var previewRect = new Rectangle(
                    tooltipRect.X + (tooltipRect.Width - previewSize) / 2,
                    textY,
                    previewSize,
                    previewSize);

                spriteBatch.Draw(pixel, previewRect, new Color(6, 8, 12) * 0.55f);

                try
                {
                    var preview = BmdPreviewRenderer.GetPreview(infoItem, previewSize, previewSize);
                    if (preview != null)
                        spriteBatch.Draw(preview, previewRect, Color.White);
                }
                catch
                {
                    // 模型還沒載好就先留空，下一幀會補上
                }

                textY += previewSize + 10;
            }

            bool isFirstLine = true;

            foreach (var (text, color) in lines)
            {
                Vector2 textSize = _font.MeasureString(text) * scale;
                int textX = tooltipRect.X + (tooltipRect.Width - (int)textSize.X) / 2;

                // Shadow
                spriteBatch.DrawString(_font, text, new Vector2(textX + 1, textY + 1), Color.Black * 0.7f,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                // Text
                Color lineColor = isFirstLine ? borderColor : color;
                spriteBatch.DrawString(_font, text, new Vector2(textX, textY), lineColor,
                                       0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

                textY += (int)textSize.Y + lineSpacing;

                // Separator after item name
                if (isFirstLine)
                {
                    textY += 2;
                    spriteBatch.Draw(pixel, new Rectangle(tooltipRect.X + 8, textY, tooltipRect.Width - 16, 1), borderColor * 0.3f);
                    textY += 4;
                    isFirstLine = false;
                }
            }
        }

        /// <summary>
        /// 手機：拿起的道具不跟著手指跑，改在來源格子畫一圈外框表示「已選取」。
        /// 再點另一個格子就是移動過去。
        /// </summary>
        private void DrawMobilePickedHighlight(SpriteBatch spriteBatch)
        {
            if (!s_mobile)
                return;

            var item = _pickedItemRenderer.Item;
            if (item == null)
                return;

            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null)
                return;

            Rectangle rect;
            if (_pickedFromEquipSlot >= 0 && _equipSlots.TryGetValue((byte)_pickedFromEquipSlot, out var layout))
            {
                rect = Translate(layout.Rect);
            }
            else if (_pickedItemOriginalGrid.X >= 0 && _pickedItemOriginalGrid.Y >= 0)
            {
                Point gridTopLeft = Translate(_gridRect).Location;
                gridTopLeft.Y -= _gridScrollRow * INVENTORY_SQUARE_HEIGHT;
                rect = new Rectangle(
                    gridTopLeft.X + _pickedItemOriginalGrid.X * INVENTORY_SQUARE_WIDTH,
                    gridTopLeft.Y + _pickedItemOriginalGrid.Y * INVENTORY_SQUARE_HEIGHT,
                    Math.Max(1, item.Definition.Width) * INVENTORY_SQUARE_WIDTH,
                    Math.Max(1, item.Definition.Height) * INVENTORY_SQUARE_HEIGHT);
            }
            else
            {
                return;
            }

            // 道具的圖示要留在原格。
            // 「拿起」在內部是把道具從格子陣列移除，因此原本會整個消失 ——
            // 玩家看到的是一個空格子，完全不知道自己手上還拿著東西。
            var itemTexture = ResolveItemTexture(item, rect.Width, rect.Height);
            if (itemTexture != null)
            {
                int pad = 3;
                var iconRect = new Rectangle(rect.X + pad, rect.Y + pad,
                    Math.Max(1, rect.Width - pad * 2), Math.Max(1, rect.Height - pad * 2));
                spriteBatch.Draw(itemTexture, iconRect, Color.White);
            }

            const int thickness = 3;
            Color accent = Theme.Accent;
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, thickness), accent);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), accent);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, thickness, rect.Height), accent);
            spriteBatch.Draw(pixel, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), accent);
            spriteBatch.Draw(pixel, rect, accent * 0.12f);
        }

        private Rectangle Translate(Rectangle rect)
        {
            return new Rectangle(DisplayRectangle.X + rect.X, DisplayRectangle.Y + rect.Y, rect.Width, rect.Height);
        }

        private void ReleasePickedItem()
        {
            _pickedItemRenderer.ReleaseItem();
            ResetPickedState();
        }

        private void ResetPickedState()
        {
            _pickedItemOriginalGrid = new Point(-1, -1);
            _pickedFromEquipSlot = -1;
            _itemDragMoved = false;
        }

        private bool IsMouseOverGrid()
        {
            Point mousePos = MuGame.Instance.UiMouseState.Position;
            Rectangle gridScreenRect = Translate(_gridRect);

            return gridScreenRect.Contains(mousePos);
        }

        private bool IsMouseOverDragArea()
        {
            Point mousePos = MuGame.Instance.UiMouseState.Position;
            return Translate(_headerRect).Contains(mousePos);
        }

        private Point GetSlotAtScreenPosition(Point screenPos)
        {
            var slot = ItemGridRenderHelper.GetSlotAtScreenPosition(
                DisplayRectangle, _gridRect, Columns, _gridVisibleRows,
                INVENTORY_SQUARE_WIDTH, INVENTORY_SQUARE_HEIGHT, screenPos);

            if (slot.X < 0)
                return slot;

            // 命中的是「看得到的第幾列」，要加回捲掉的列數才是真正的格號
            slot.Y += _gridScrollRow;

            // 5x13 = 65 格，最後一格不對應任何伺服器格號
            if (!IsWithinGrid(slot))
                return new Point(-1, -1);

            return slot;
        }

        /// <summary>格線可以捲動的最大列號。</summary>
        private int MaxGridScrollRow => Math.Max(0, Rows - _gridVisibleRows);

        /// <summary>
        /// 背包格線的觸控捲動。回傳這一次觸控是否被捲動吃掉
        /// （被吃掉的話就不能同時算成「點選道具」）。
        /// </summary>
        private bool UpdateGridScroll(Point mousePos, bool leftPressed, bool leftJustPressed, bool leftJustReleased)
        {
            if (!s_mobile || MaxGridScrollRow <= 0)
                return false;

            if (leftJustPressed && Translate(_gridRect).Contains(mousePos))
            {
                _gridDragging = true;
                _gridDragStartY = mousePos.Y;
                _gridDragStartScrollPixels = (int)_gridScrollPixels;
                return false;   // 還不知道是拖曳還是點選，先不吃掉
            }

            if (!_gridDragging)
                return false;

            if (leftJustReleased || !leftPressed)
            {
                bool wasDrag = Math.Abs(mousePos.Y - _gridDragStartY) > DragThresholdPixels;
                _gridDragging = false;
                SnapGridScroll();
                return wasDrag;
            }

            int delta = _gridDragStartY - mousePos.Y;
            if (Math.Abs(delta) <= DragThresholdPixels)
                return false;

            float max = MaxGridScrollRow * INVENTORY_SQUARE_HEIGHT;
            _gridScrollPixels = MathHelper.Clamp(_gridDragStartScrollPixels + delta, 0f, max);
            _gridScrollRow = (int)MathF.Round(_gridScrollPixels / INVENTORY_SQUARE_HEIGHT);
            return true;
        }

        /// <summary>放開手指後對齊到整列，避免半列被切掉。</summary>
        private void SnapGridScroll()
        {
            _gridScrollRow = Math.Clamp(
                (int)MathF.Round(_gridScrollPixels / INVENTORY_SQUARE_HEIGHT), 0, MaxGridScrollRow);
            _gridScrollPixels = _gridScrollRow * INVENTORY_SQUARE_HEIGHT;
        }

        /// <summary>超過這個位移才算拖曳，否則算點選。手指按下時本來就會晃幾個像素。</summary>
        private const int DragThresholdPixels = 8;

        private int GetEquipSlotAtScreenPosition(Point screenPos)
        {
            foreach (var layout in _equipSlots.Values)
            {
                var slotRect = Translate(layout.Rect);
                if (slotRect.Contains(screenPos))
                {
                    return layout.Slot;
                }
            }
            return -1;
        }
    }
}
