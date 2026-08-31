using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game;
using Client.Main.Controls.UI.SelectCharacter;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Networking;
using Client.Main.Objects.Player;
using Client.Main.Worlds;
using Client.Main.Scenes.SelectCharacter;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MUnique.OpenMU.Network.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Main.Scenes
{
    public class SelectCharacterScene : BaseScene
    {
        private static class Theme
        {
            // Background layers
            public static readonly Color BgDarkest = new(8, 10, 14, 252);
            public static readonly Color BgDark = new(16, 20, 26, 250);
            public static readonly Color BgMid = new(24, 30, 38, 248);
            public static readonly Color BgLight = new(35, 42, 52, 245);
            public static readonly Color BgLighter = new(48, 56, 68, 240);

            // Accent - Warm Gold
            public static readonly Color Accent = new(212, 175, 85);
            public static readonly Color AccentBright = new(255, 215, 120);
            public static readonly Color AccentDim = new(140, 115, 55);
            public static readonly Color AccentGlow = new(255, 200, 80, 40);

            // Secondary accent - Cool Blue
            public static readonly Color Secondary = new(90, 140, 200);
            public static readonly Color SecondaryBright = new(130, 180, 240);
            public static readonly Color SecondaryDim = new(50, 80, 120);

            // Borders
            public static readonly Color BorderOuter = new(5, 6, 8, 255);
            public static readonly Color BorderInner = new(60, 70, 85, 200);
            public static readonly Color BorderHighlight = new(100, 110, 130, 120);

            // Text
            public static readonly Color TextWhite = new(240, 240, 245);
            public static readonly Color TextGold = new(255, 220, 130);
            public static readonly Color TextGray = new(160, 165, 175);
            public static readonly Color TextDark = new(100, 105, 115);

            // Status colors
            public static readonly Color Success = new(80, 200, 120);
            public static readonly Color Warning = new(240, 180, 60);
            public static readonly Color Danger = new(220, 80, 80);
        }

        // 手機的版面要放大。原本的尺寸換算到實機大約只有 20 pt 高的按鈕，
        // 遠低於可以穩定點中的大小（實機截圖確認過偏小）。
        private static readonly bool s_mobile = Client.Main.Controls.UI.MobileUi.IsMobile;

        private static readonly int PANEL_WIDTH = s_mobile ? 470 : 340;
        private static readonly int PANEL_MARGIN = s_mobile ? 40 : 30;   // 螢幕圓角，右側再讓開一點
        private static readonly int HEADER_HEIGHT = s_mobile ? 58 : 45;
        private static readonly int BUTTON_HEIGHT = s_mobile ? 56 : 36;
        private static readonly int BUTTON_SPACING = s_mobile ? 10 : 8;
        private static readonly int INNER_PADDING = s_mobile ? 14 : 12;
        private static readonly int CHAR_CARD_HEIGHT = s_mobile ? 88 : 65;
        private static readonly int CHAR_CARD_SPACING = s_mobile ? 8 : 6;

        private static readonly float BUTTON_FONT_SIZE = s_mobile ? 17f : 13f;

        // Fields
        private readonly List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)> _characters;
        private SelectWorld _selectWorld;
        private CharacterSelectionController _characterController;
        private readonly NetworkManager _networkManager;
        private ILogger<SelectCharacterScene> _logger;
        private (string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)? _selectedCharacterInfo = null;
        private LoadingScreenControl _loadingScreen;
        private bool _initialLoadComplete = false;
        private ButtonControl _previousCharacterButton;
        private ButtonControl _nextCharacterButton;
        private int _currentCharacterIndex = -1;
        private bool _isSelectionInProgress = false;
        private Texture2D _backgroundTexture;
        private ProgressBarControl _progressBar;

        /// <summary>載入超過這個時間才顯示進度條，避免快速載入時閃一下。</summary>
        private static readonly TimeSpan LoadingIndicatorDelay = TimeSpan.FromMilliseconds(600);
        private TimeSpan? _firstLoadingDrawAt;
        private bool _previousDayNightEnabled;
        private bool _previousShowNamesOnHover;
        private Vector3 _previousSunDirection;
        private bool _dayNightPatched;
        private ButtonControl _createCharacterButton;
        private ButtonControl _deleteCharacterButton;
        private ButtonControl _enterGameButton;
        private ButtonControl _exitButton;
        private CharacterCreationDialog _characterCreationDialog;
        private string _currentlySelectedCharacterName = null;
        private bool _isIntentionalLogout = false;
        private bool _returnToLoginRequested;
        private readonly SemaphoreSlim _characterRefreshLock = new(1, 1);
        private CancellationTokenSource _characterRefreshCancellation;

        // UI Panel rendering
        private Rectangle _characterPanelRect;
        private Rectangle _buttonSectionRect;
        private Rectangle _characterListRect;
        private List<Rectangle> _characterCardRects = new List<Rectangle>();
        private int _hoveredCardIndex = -1;
        private bool _previousMousePressed = false;

        // Constructors
        /// <summary>
        /// 允許在初始化完成之前就被呈現。
        ///
        /// 沒有這個的話，MuGame.ChangeSceneAsync 會等整個場景初始化完才切換
        /// （見 activateBeforeInitialization），而選角場景要載地圖與五個角色 ——
        /// 實測那段有將近二十秒畫面還停在登入頁、沒有任何回饋，使用者會以為
        /// 登入失敗而再按一次，於是拿到 AccountAlreadyConnected。
        /// 改成先呈現載入畫面與進度條，載完再露出頁面。
        /// </summary>
        public override bool CanRenderWhileInitializing => true;

        private Task _firstFramePreparationTask;

        public override Task PrepareForFirstPresentedFrameAsync()
        {
            _firstFramePreparationTask ??= PrepareFirstPresentedFrameCoreAsync();
            return _firstFramePreparationTask;
        }

        /// <summary>
        /// 只準備「第一幀畫得出來」所需的東西：載入畫面與進度條。
        /// 兩者都在建構子建好了，這裡只是把它們初始化到可以繪製。
        /// </summary>
        private async Task PrepareFirstPresentedFrameCoreAsync()
        {
            if (_loadingScreen != null && _loadingScreen.Status == GameControlStatus.NonInitialized)
                await _loadingScreen.Initialize();

            if (_progressBar != null && _progressBar.Status == GameControlStatus.NonInitialized)
                await _progressBar.Initialize();
        }

        public SelectCharacterScene(List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)> characters, NetworkManager networkManager)
        {
            _characters = characters ?? new List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)>();
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _logger = MuGame.AppLoggerFactory.CreateLogger<SelectCharacterScene>();

            _loadingScreen = new LoadingScreenControl { Visible = true, Message = "Loading Characters..." };
            Controls.Add(_loadingScreen);
            _loadingScreen.BringToFront();

            InitializeModernUI();

            try
            {
                _backgroundTexture = MuGame.Instance.Content.Load<Texture2D>("Background");
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[SelectCharacterScene] Background load failed: {ex.Message}");
            }

            _progressBar = new ProgressBarControl();
            Controls.Add(_progressBar);

            SubscribeToNetworkEvents();
        }

        private void DisableDayNightCycleForScene()
        {
            if (_dayNightPatched) return;

            _dayNightPatched = true;
            _previousDayNightEnabled = Constants.ENABLE_DAY_NIGHT_CYCLE;
            _previousSunDirection = Constants.SUN_DIRECTION;
            // 角色底下已經有名字標籤了。五個角色都是可點擊的，手指一碰就算 hover，
            // 頭頂會再冒出一個名字 —— 重複，而且會擋到旁邊的角色。
            _previousShowNamesOnHover = Constants.SHOW_NAMES_ON_HOVER;
            Constants.SHOW_NAMES_ON_HOVER = false;
            Constants.ENABLE_DAY_NIGHT_CYCLE = false;
            SunCycleManager.ResetToDefault();
        }

        private void RestoreDayNightCycle()
        {
            if (!_dayNightPatched) return;

            Constants.ENABLE_DAY_NIGHT_CYCLE = _previousDayNightEnabled;
            Constants.SUN_DIRECTION = _previousSunDirection;
            Constants.SHOW_NAMES_ON_HOVER = _previousShowNamesOnHover;
            _dayNightPatched = false;
        }

        private void UpdateLoadProgress(string message, float progress)
        {
            // 選角場景載入到一半卡住時，這是唯一看得到進度的地方 ——
            // 載入畫面本身在 LoginScene 底下，玩家與開發者都看不見。
            Console.WriteLine($"[Scene] select-character {progress:P0}: {message}");

            MuGame.ScheduleOnMainThread(() =>
            {
                if (_loadingScreen != null && _loadingScreen.Visible)
                {
                    _loadingScreen.Message = message;
                    _loadingScreen.Progress = progress;
                }
            });
        }

        private void InitializeModernUI()
        {
            // Previous/Next character arrows (disabled)
            _previousCharacterButton = CreateModernNavigationButton("<");
            _previousCharacterButton.Click += (s, e) => MoveSelection(-1);
            _previousCharacterButton.Enabled = false;
            _previousCharacterButton.Visible = false;
            Controls.Add(_previousCharacterButton);

            _nextCharacterButton = CreateModernNavigationButton(">");
            _nextCharacterButton.Click += (s, e) => MoveSelection(1);
            _nextCharacterButton.Enabled = false;
            _nextCharacterButton.Visible = false;
            Controls.Add(_nextCharacterButton);

            // Action buttons
            _enterGameButton = CreateModernButton("ENTER GAME", Theme.Success);
            _enterGameButton.Click += OnEnterGameButtonClick;
            Controls.Add(_enterGameButton);

            _createCharacterButton = CreateModernButton("CREATE CHARACTER", Theme.Secondary);
            _createCharacterButton.Click += OnCreateCharacterButtonClick;
            Controls.Add(_createCharacterButton);

            _deleteCharacterButton = CreateModernButton("DELETE CHARACTER", Theme.Danger);
            _deleteCharacterButton.Click += OnDeleteCharacterButtonClick;
            Controls.Add(_deleteCharacterButton);

            _exitButton = CreateModernButton("EXIT", Theme.BgLight);
            _exitButton.Click += OnExitButtonClick;
            Controls.Add(_exitButton);

            CalculatePanelLayout();
        }

        private ButtonControl CreateModernNavigationButton(string arrow)
        {
            var mobileFill = Client.Main.Controls.UI.MobileUi.PanelFill * 0.72f;

            return new ButtonControl
            {
                Text = arrow,
                // 箭頭是圖形不是文字，不走文字級距。72 px 讓 "<" 在 88 見方的
                // 按鈕裡看起來像一個箭頭，而不是一個標點符號。
                FontSize = s_mobile ? 72f : 48f,
                AutoViewSize = false,
                ViewSize = s_mobile ? new Point(88, 88) : new Point(70, 70),
                BackgroundColor = s_mobile ? mobileFill : Theme.BgMid,
                HoverBackgroundColor = s_mobile ? mobileFill : Theme.BgLight,
                PressedBackgroundColor = s_mobile
                    ? Client.Main.Controls.UI.MobileUi.TitleBarFill * 1.3f
                    : Theme.BgDark,
                TextColor = s_mobile ? Client.Main.Controls.UI.MobileUi.TextPrimary : Theme.Accent,
                HoverTextColor = s_mobile ? Client.Main.Controls.UI.MobileUi.TextPrimary : Theme.AccentBright,
                DisabledTextColor = Theme.TextDark,
                Interactive = true,
                Visible = false,
                Enabled = false,
                BorderThickness = s_mobile ? 0 : 2,
                BorderColor = s_mobile ? Color.Transparent : Theme.BorderInner
            };
        }

        /// <summary>
        /// 動作鈕。
        ///
        /// 手機上<b>只有刪除保留顏色</b>。原本進入是綠、刪除是紅、建立是藍，
        /// 三顆飽和色排在一起，眼睛第一個看到的是最鮮豔的那一顆，而不是最常用的
        /// 那一顆。顏色只留給帶資訊的東西 —— 這裡唯一帶資訊的是「這個動作會
        /// 把角色刪掉」。主要動作（進入遊戲）靠底色比其他鈕亮一階來表達。
        /// </summary>
        private ButtonControl CreateModernButton(string text, Color baseColor)
        {
            var fill = baseColor;
            if (s_mobile)
            {
                // 白色半透明：草地舞台的背景很花，實心深色面板會顯得笨重。
                // 主要動作（ENTER）稍微不透明一點，讓它在四顆裡站出來。
                fill = baseColor == Theme.Success
                    ? Color.White * 0.28f
                    : Color.White * 0.18f;
            }

            int width = s_mobile ? MobileButtonWidth : PANEL_WIDTH - INNER_PADDING * 2;

            return new ButtonControl
            {
                Text = text,
                FontSize = BUTTON_FONT_SIZE,
                AutoViewSize = false,
                ViewSize = new Point(width, BUTTON_HEIGHT),
                BackgroundColor = fill,
                HoverBackgroundColor = Color.Lerp(fill, Color.White, 0.2f),
                PressedBackgroundColor = Color.Lerp(fill, Color.Black, 0.2f),
                TextColor = Theme.TextWhite,
                HoverTextColor = Theme.TextWhite,
                DisabledTextColor = Theme.TextDark,
                Interactive = true,
                Visible = false,
                Enabled = false,
                BorderThickness = s_mobile ? 0 : 1,
                BorderColor = s_mobile ? Color.Transparent : Theme.BorderInner
            };
        }

        /// <summary>畫面放得下幾張角色卡（見 CalculatePanelLayout）。</summary>
        private int _visibleCharacterCards = 5;

        /// <summary>面板與畫面上下緣的距離。和其他手機介面同一條邊距。</summary>
        private static int EdgeMargin => Client.Main.Controls.UI.MobileUi.IsMobile ? Client.Main.Controls.UI.MobileUi.CornerInset : 12;

        private void CalculatePanelLayout()
        {
            int screenWidth = ViewSize.X;
            int screenHeight = ViewSize.Y;

            if (s_mobile)
            {
                CalculateMobileLayout(screenWidth, screenHeight);
                return;
            }

            // Calculate panel height based on content
            int buttonSectionHeight = (BUTTON_HEIGHT + BUTTON_SPACING) * 4 + INNER_PADDING * 2; // Buttons only, no header

            // 面板高度是「角色張數」決定的，但畫面高度是固定的。
            //
            // 原本只是把面板置中：張數一多，總高度就超過畫面，panelY 變成負值，
            // 底部的 EXIT 直接被切掉一半 —— 實機上四個角色就會這樣（使用者截圖）。
            //
            // 按鈕區不能犧牲（那是唯一的出口），所以要讓步的是角色清單：
            // 先算出扣掉標題列與按鈕區之後還剩多少高度，再決定放得下幾張卡片。
            int availableHeight = screenHeight - EdgeMargin * 2;
            int listBudget = availableHeight - HEADER_HEIGHT - buttonSectionHeight - INNER_PADDING * 2;
            int cardSlot = CHAR_CARD_HEIGHT + CHAR_CARD_SPACING;

            int maxCharCards = Math.Min(_characters.Count, 5);
            if (cardSlot > 0)
                maxCharCards = Math.Clamp(listBudget / cardSlot, 1, maxCharCards);

            int characterListHeight = maxCharCards * cardSlot + INNER_PADDING * 2;
            int totalPanelHeight = HEADER_HEIGHT + characterListHeight + buttonSectionHeight;

            _visibleCharacterCards = maxCharCards;

            // Character panel (right side)
            int panelX = screenWidth - PANEL_WIDTH - PANEL_MARGIN;
            int panelY = (screenHeight - totalPanelHeight) / 2;

            // 就算上面算過了也要夾一次：面板永遠不能超出上下邊緣。
            panelY = Math.Clamp(panelY, EdgeMargin, Math.Max(EdgeMargin, screenHeight - EdgeMargin - totalPanelHeight));
            _characterPanelRect = new Rectangle(panelX, panelY, PANEL_WIDTH, totalPanelHeight);

            // Character list section (top, below header)
            int listY = panelY + HEADER_HEIGHT;
            _characterListRect = new Rectangle(panelX, listY, PANEL_WIDTH, characterListHeight);

            // Button section (bottom of panel, below character list)
            int buttonY = listY + characterListHeight;
            _buttonSectionRect = new Rectangle(panelX, buttonY, PANEL_WIDTH, buttonSectionHeight);

            // Calculate character card rectangles
            _characterCardRects.Clear();
            int cardY = listY + INNER_PADDING;
            for (int i = 0; i < _characters.Count && i < _visibleCharacterCards; i++)
            {
                _characterCardRects.Add(new Rectangle(
                    panelX + INNER_PADDING,
                    cardY,
                    PANEL_WIDTH - INNER_PADDING * 2,
                    CHAR_CARD_HEIGHT
                ));
                cardY += CHAR_CARD_HEIGHT + CHAR_CARD_SPACING;
            }
        }

        /// <summary>
        /// 手機的選角版面：<b>沒有角色清單</b>。
        ///
        /// 清單的問題不是樣式而是容量 —— 面板高度由「放得下幾張卡」決定，
        /// 而畫面只有 756 高，扣掉四顆動作鈕之後只剩三張卡的空間。
        /// 帳號有四個角色時第四個直接看不到，而且沒有任何捲動的提示，
        /// 玩家會以為角色不見了。
        ///
        /// 改成手機遊戲的標準做法（使用者指定）：畫面中央就是角色本人，
        /// 左右兩顆箭頭切換，名字與等級寫在角色底下，下方一排圓點表示
        /// 總共有幾個、目前在第幾個。幾個角色都放得下，而且角色是用「看的」
        /// 不是用「讀清單的」—— 這正是 3D 選角畫面存在的意義。
        /// </summary>
        private void CalculateMobileLayout(int screenWidth, int screenHeight)
        {
            int inset = Client.Main.Controls.UI.MobileUi.CornerInset;

            // 動作鈕排成畫面底部的一整條橫列。
            // 原本是右下角的直排，會擋掉右邊三分之一的舞台 ——
            // 換成諾利亞草地當背景之後，那塊景色不該被按鈕蓋住。
            int left = Client.Main.Controls.UI.MobileUi.LeftEdge;
            int available = screenWidth - left - inset;

            // 只佔可用寬度的八成並置中：螢幕左右下角是圓角，貼著邊緣的按鈕會有
            // 一部分落在看不到的區域裡。
            int rowWidth = (int)(available * MobileButtonRowWidthRatio);
            int rowX = left + ((available - rowWidth) / 2);

            // 再往上抬一點 —— 貼著螢幕最底部容易在滑動或握持時誤觸。
            int rowY = screenHeight - inset - BUTTON_HEIGHT - MobileButtonRowBottomLift;

            _characterPanelRect = new Rectangle(rowX, rowY, rowWidth, BUTTON_HEIGHT);
            _buttonSectionRect = _characterPanelRect;
            _characterListRect = Rectangle.Empty;
            _characterCardRects.Clear();
            _visibleCharacterCards = 0;
        }

        /// <summary>手機動作鈕的寬度。比桌面的面板窄 —— 沒有清單就不需要那麼寬。</summary>
        private const int MobileButtonWidth = 320;

        /// <summary>底部按鈕列佔可用寬度的比例。圓角螢幕的兩側看不到內容，不要貼邊。</summary>
        private const float MobileButtonRowWidthRatio = 0.8f;

        /// <summary>底部按鈕列離螢幕底緣再抬高多少，避免誤觸。</summary>
        private const int MobileButtonRowBottomLift = 28;

        /// <summary>
        /// 角色模型在畫面上的位置（虛擬座標）。左右箭頭要貼著它擺，
        /// 不能寫死 —— 鏡頭的垂直視角在手機上會依長寬比補償（見 SelectWorld）。
        /// 投影不出來時回傳 false，呼叫端退回畫面中央。
        /// </summary>
        private bool TryGetCharacterScreenPosition(out Vector2 position)
        {
            position = Vector2.Zero;

            if (World is not Client.Main.Worlds.SelectWorld selectWorld)
                return false;

            var camera = Client.Main.Graphics.Camera.Instance;
            var device = GraphicsManager.Instance?.GraphicsDevice;
            if (camera == null || device == null)
                return false;

            // 角色站的位置是腳底，往上抬一點才是身體的中心。
            var world = selectWorld.CharacterDisplayPosition + new Vector3(0f, 0f, 180f);
            var projected = device.Viewport.Project(world, camera.Projection, camera.View, Matrix.Identity);

            if (projected.Z < 0f || projected.Z > 1f)
                return false;

            // Viewport 是實際像素，UI 走的是虛擬座標。
            position = new Vector2(projected.X * UiScaler.InverseScaleX, projected.Y * UiScaler.InverseScaleY);
            return true;
        }

        private void PositionNavigationButtons()
        {
            // Early exit if buttons not created yet (called during construction)
            if (_previousCharacterButton == null && _nextCharacterButton == null && 
                _enterGameButton == null && _createCharacterButton == null && 
                _deleteCharacterButton == null && _exitButton == null)
            {
                return;
            }

            CalculatePanelLayout();
            
            bool ready = _initialLoadComplete && (_loadingScreen == null || !_loadingScreen.Visible) && !_isSelectionInProgress;
            bool hasCharacters = _characters.Count > 0;
            bool hasSelection = !string.IsNullOrEmpty(_currentlySelectedCharacterName);
            bool canCreate = _characters.Count < 5;

            // Position navigation arrows
            if (s_mobile)
            {
                // 手機直接點角色就能選，左右箭頭是多餘的，而且會擋住舞台。
                PositionMobileArrows(false);
            }
            else if (_previousCharacterButton != null && _nextCharacterButton != null)
            {
                _previousCharacterButton.X = (ViewSize.X / 2) - 250;
                _previousCharacterButton.Y = (ViewSize.Y - _previousCharacterButton.ViewSize.Y) / 2;

                _nextCharacterButton.X = (ViewSize.X / 2) + 180;
                _nextCharacterButton.Y = (ViewSize.Y - _nextCharacterButton.ViewSize.Y) / 2;
            }

            if (s_mobile)
            {
                LayoutMobileButtonRow(ready, hasCharacters, hasSelection, canCreate);
                return;
            }

            // Position action buttons in button section (bottom of panel)
            int panelX = _characterPanelRect.X;
            // 手機的動作鈕自己就是一欄，沒有外框面板，所以不需要內距。
            int buttonX = s_mobile ? panelX : panelX + INNER_PADDING;
            int buttonY = _buttonSectionRect.Y + (s_mobile ? 0 : INNER_PADDING);

            // ENTER GAME button (top of button section)
            if (_enterGameButton != null)
            {
                _enterGameButton.X = buttonX;
                _enterGameButton.Y = buttonY;
                _enterGameButton.Enabled = ready && hasCharacters && hasSelection;
                _enterGameButton.Visible = ready && hasCharacters && hasSelection;
            }

            buttonY += (BUTTON_HEIGHT + BUTTON_SPACING);

            // DELETE CHARACTER button (shows when character selected)
            if (_deleteCharacterButton != null)
            {
                _deleteCharacterButton.X = buttonX;
                _deleteCharacterButton.Y = buttonY;
                _deleteCharacterButton.Enabled = ready && hasSelection;
                _deleteCharacterButton.Visible = ready && hasSelection;
                
                _logger?.LogDebug("Delete button - Ready: {Ready}, HasSelection: {HasSel}, CharName: '{Name}', Visible: {Vis}", 
                    ready, hasSelection, _currentlySelectedCharacterName, _deleteCharacterButton.Visible);
            }

            buttonY += (BUTTON_HEIGHT + BUTTON_SPACING);

            // CREATE CHARACTER button
            if (_createCharacterButton != null)
            {
                _createCharacterButton.X = buttonX;
                _createCharacterButton.Y = buttonY;
                _createCharacterButton.Enabled = ready && canCreate;
                _createCharacterButton.Visible = ready;
            }

            buttonY += (BUTTON_HEIGHT + BUTTON_SPACING);

            // EXIT button (very bottom)
            if (_exitButton != null)
            {
                _exitButton.X = buttonX;
                _exitButton.Y = buttonY;
                _exitButton.Enabled = ready && !_isSelectionInProgress;
                _exitButton.Visible = ready;
            }

        }

        /// <summary>
        /// 手機：ENTER / DELETE / CREATE / EXIT 排成畫面底部的一整條橫列。
        ///
        /// 四欄的位置固定，不隨顯示與否遞補 —— 按鈕會依選中狀態隱藏，
        /// 若讓後面的補上來，位置就會在選中／取消之間跳來跳去。
        /// </summary>
        private void LayoutMobileButtonRow(bool ready, bool hasCharacters, bool hasSelection, bool canCreate)
        {
            var row = _characterPanelRect;
            const int columns = 4;
            int slotWidth = (row.Width - BUTTON_SPACING * (columns - 1)) / columns;

            void Place(ButtonControl button, int column, bool visible, bool enabled)
            {
                if (button == null)
                    return;

                button.ViewSize = new Point(slotWidth, BUTTON_HEIGHT);
                button.X = row.X + column * (slotWidth + BUTTON_SPACING);
                button.Y = row.Y;
                button.Visible = visible;
                button.Enabled = enabled;
            }

            bool canEnter = ready && hasCharacters && hasSelection;
            Place(_enterGameButton, 0, canEnter, canEnter);
            Place(_deleteCharacterButton, 1, ready && hasSelection, ready && hasSelection);
            Place(_createCharacterButton, 2, ready, ready && canCreate);
            Place(_exitButton, 3, ready, ready && !_isSelectionInProgress);
        }

        private void UpdateNavigationButtonState()
        {
            // 手機的箭頭是唯一的切換方式，狀態由 PositionMobileArrows 決定。
            if (s_mobile)
                return;

            // Navigation buttons are permanently disabled
            if (_previousCharacterButton != null)
            {
                _previousCharacterButton.Enabled = false;
                _previousCharacterButton.Visible = false;
            }

            if (_nextCharacterButton != null)
            {
                _nextCharacterButton.Enabled = false;
                _nextCharacterButton.Visible = false;
            }
        }

        /// <summary>
        /// 左右箭頭貼著角色模型擺。只有一個角色時整個藏起來 ——
        /// 按了不會有事的按鈕比沒有按鈕更糟。
        /// </summary>
        private void PositionMobileArrows(bool show)
        {
            if (_previousCharacterButton == null || _nextCharacterButton == null)
                return;

            if (!TryGetCharacterScreenPosition(out var center))
                center = new Vector2(ViewSize.X * 0.5f, ViewSize.Y * 0.5f);

            _mobileCharacterAnchor = center;

            const int gap = 150;   // 半個角色的寬度 + 一點餘裕
            int size = _previousCharacterButton.ViewSize.Y;
            int y = (int)MathF.Round(center.Y - size / 2f);
            y = Math.Clamp(y, Client.Main.Controls.UI.MobileUi.CornerInset,
                           ViewSize.Y - Client.Main.Controls.UI.MobileUi.CornerInset - size);

            _previousCharacterButton.X = Math.Max(Client.Main.Controls.UI.MobileUi.LeftEdge,
                (int)MathF.Round(center.X) - gap - size);
            _previousCharacterButton.Y = y;

            _nextCharacterButton.X = (int)MathF.Round(center.X) + gap;
            _nextCharacterButton.Y = y;

            _previousCharacterButton.Visible = show;
            _previousCharacterButton.Enabled = show;
            _nextCharacterButton.Visible = show;
            _nextCharacterButton.Enabled = show;
        }

        /// <summary>角色模型在畫面上的位置，名字與圓點都排在它下面。</summary>
        private Vector2 _mobileCharacterAnchor;

        private void MoveSelection(int direction)
        {
            if (_characters.Count == 0 || _characterController == null)
            {
                return;
            }

            if (!_initialLoadComplete || (_loadingScreen != null && _loadingScreen.Visible) || _isSelectionInProgress)
            {
                return;
            }

            int currentIndex = _currentCharacterIndex;
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            if (_characters.Count == 1)
            {
                return;
            }

            int nextIndex = (currentIndex + direction) % _characters.Count;
            if (nextIndex < 0)
            {
                nextIndex += _characters.Count;
            }

            if (nextIndex == _currentCharacterIndex)
            {
                return;
            }

            _currentCharacterIndex = nextIndex;
            _characterController.SetActiveCharacter(_currentCharacterIndex);

            if (_currentCharacterIndex >= 0 && _currentCharacterIndex < _characters.Count)
            {
                _currentlySelectedCharacterName = _characters[_currentCharacterIndex].Name;
                PositionNavigationButtons();
                UpdateNavigationButtonState();
            }
            else
            {
                _currentlySelectedCharacterName = null;
                UpdateNavigationButtonState();
            }

        }

        protected override async Task LoadSceneContentWithProgress(Action<string, float> progressCallback)
        {
            DisableDayNightCycleForScene();
            UpdateLoadProgress("Initializing Character Selection...", 0.0f);
            _logger.LogInformation(">>> SelectCharacterScene LoadSceneContentWithProgress starting...");

            try
            {
                UpdateLoadProgress("Creating Select World...", 0.05f);
                _selectWorld = new SelectWorld { Visible = false };
                Controls.Add(_selectWorld);

                UpdateLoadProgress("Initializing Select World (Graphics)...", 0.1f);
                await _selectWorld.Initialize();
                await MuGame.YieldToNextFrameAsync(
                    "CharacterSelection.AttachWorld",
                    MainThreadDispatcher.WorkPriority.Critical);
                World = _selectWorld;
                UpdateLoadProgress("Select World Initialized.", 0.35f);
                _logger.LogInformation("--- SelectCharacterScene: SelectWorld initialized and set.");

                if (_selectWorld.Terrain != null)
                {
                    _selectWorld.Terrain.AmbientLight = 0.6f;
                }

                // Create controller
                _characterController = new CharacterSelectionController(
                    MuGame.AppLoggerFactory.CreateLogger<CharacterSelectionController>());

                // Subscribe to events
                _characterController.CharacterClicked += OnControllerCharacterClicked;
                _characterController.CharacterDoubleClicked += OnControllerCharacterDoubleClicked;

                // Connect to world
                _selectWorld.SetController(_characterController);

                // Attaching the world invalidates control-tree state. Let that frame finish
                // before constructing and publishing character slots.
                await MuGame.YieldToNextFrameAsync(
                    "CharacterSelection.CreateSlots",
                    MainThreadDispatcher.WorkPriority.High);

                if (_characters.Any())
                {
                    UpdateLoadProgress("Preparing Character Data...", 0.40f);
                    await _characterController.CreateCharactersAsync(
                        _characters,
                        _selectWorld,
                        this,
                        _selectWorld.CharacterDisplayPosition,
                        _selectWorld.CharacterDisplayAngle);

                    if (_characters.Count > 0)
                    {
                        _currentCharacterIndex = 0;
                        _currentlySelectedCharacterName = _characters[0].Name;
                    }
                    else
                    {
                        _currentCharacterIndex = -1;
                    }

                    PositionNavigationButtons();
                    UpdateNavigationButtonState();

                    float characterCreationStartProgress = 0.45f;
                    float characterCreationEndProgress = 0.85f;
                    float totalCharacterProgressSpan = characterCreationEndProgress - characterCreationStartProgress;

                    if (_characters.Count > 0)
                    {
                        float progressPerCharacter = totalCharacterProgressSpan / _characters.Count;
                        for (int i = 0; i < _characters.Count; i++)
                        {
                            UpdateLoadProgress($"Configuring character {i + 1}/{_characters.Count}...", characterCreationStartProgress + (i + 1) * progressPerCharacter);
                        }
                    }
                    else
                    {
                        UpdateLoadProgress("No characters to configure.", characterCreationEndProgress);
                    }

                    UpdateLoadProgress("Preparing first character-selection frame...", 0.90f);
                    await _selectWorld.PrepareInitialRenderResourcesAsync(
                        "CharacterSelection.PrewarmWorld");
                    await MuGame.YieldToNextFrameAsync(
                        "CharacterSelection.ActivateWorld",
                        MainThreadDispatcher.WorkPriority.Critical);
                    _selectWorld.Visible = true;
                    _characterController.EnsureActiveCharacterVisible(_selectWorld);
                    _selectWorld.PrepareInitialVisibilitySnapshot();
                    UpdateLoadProgress("Character Objects Ready.", 0.94f);
                    _logger.LogInformation("--- SelectCharacterScene: Character creation finished.");
                }
                else
                {
                    _currentCharacterIndex = -1;
                    string message = "No characters found on this account.";
                    _logger.LogWarning("--- SelectCharacterScene: {Message}", message);
                    UpdateLoadProgress(message, 0.90f);
                    UpdateNavigationButtonState();
                }

                if (!_selectWorld.Visible)
                {
                    await _selectWorld.PrepareInitialRenderResourcesAsync(
                        "CharacterSelection.PrewarmEmptyWorld");
                    await MuGame.YieldToNextFrameAsync(
                        "CharacterSelection.ActivateEmptyWorld",
                        MainThreadDispatcher.WorkPriority.Critical);
                    _selectWorld.Visible = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "!!! SelectCharacterScene: Error during world initialization or character creation.");
                UpdateLoadProgress("Error loading character selection.", 1.0f);
                UpdateNavigationButtonState();
            }
            finally
            {
                _initialLoadComplete = true;
                UpdateNavigationButtonState();
                UpdateLoadProgress("Character Selection Ready.", 1.0f);
                _logger.LogInformation("<<< SelectCharacterScene LoadSceneContentWithProgress finished.");
            }

            // Do not complete the parent scene-initialization chain inside the activation action.
            await Task.Yield();
        }

        public override void AfterLoad()
        {
            base.AfterLoad();
            _logger.LogInformation("SelectCharacterScene.AfterLoad() called.");
            if (_loadingScreen != null)
            {
                MuGame.ScheduleOnMainThread(() =>
                {
                    if (_loadingScreen != null)
                    {
                        Controls.Remove(_loadingScreen);
                        _loadingScreen.Dispose();
                        _loadingScreen = null;
                        if (_progressBar != null)
                        {
                            _progressBar.Visible = false;
                        }
                        PositionNavigationButtons();
                        UpdateNavigationButtonState();
                        _previousCharacterButton?.BringToFront();
                        _nextCharacterButton?.BringToFront();
                        _deleteCharacterButton?.BringToFront();
                        _createCharacterButton?.BringToFront();
                        _enterGameButton?.BringToFront();
                        _exitButton?.BringToFront();
                        Cursor?.BringToFront();
                        DebugPanel?.BringToFront();
                    }
                });
            }
        }

        protected override void OnScreenSizeChanged()
        {
            base.OnScreenSizeChanged();
            PositionNavigationButtons();
        }

        public override async Task Load()
        {
            if (Status == GameControlStatus.Initializing)
            {
                await LoadSceneContentWithProgress(UpdateLoadProgress);
            }
            else
            {
                _logger.LogDebug("SelectCharacterScene.Load() called outside of InitializeWithProgressReporting flow. Re-routing to progressive load.");
                await LoadSceneContentWithProgress(UpdateLoadProgress);
            }
        }


        public void CharacterSelected(string characterName)
        {
            if (_loadingScreen != null && _loadingScreen.Visible)
            {
                _logger.LogInformation("Character selection attempted while loading screen is visible. Ignoring.");
                return;
            }

            int matchedIndex = -1;
            for (int i = 0; i < _characters.Count; i++)
            {
                if (string.Equals(_characters[i].Name, characterName, StringComparison.Ordinal))
                {
                    matchedIndex = i;
                    break;
                }
            }

            if (matchedIndex < 0)
            {
                _logger.LogError("Character '{CharacterName}' selected, but not found in the character list.", characterName);
                MessageWindow.Show($"Error selecting character '{characterName}'.");
                return;
            }

            _selectedCharacterInfo = _characters[matchedIndex];
            _currentCharacterIndex = matchedIndex;
            _characterController?.SetActiveCharacter(_currentCharacterIndex);

            ClientConnectionState currentState = _networkManager.CurrentState;
            bool canSelect = currentState == ClientConnectionState.ConnectedToGameServer ||
                             currentState == ClientConnectionState.SelectingCharacter;

            if (!canSelect)
            {
                _logger.LogWarning("Character selection attempted but NetworkManager state is not ConnectedToGameServer or SelectingCharacter. State: {State}", currentState);
                MessageWindow.Show($"Cannot select character. Invalid network state: {currentState}");
                _selectedCharacterInfo = null;
                return;
            }

            _logger.LogInformation("Character '{CharacterName}' (Class: {Class}) selected in scene. Sending request...",
                                   _selectedCharacterInfo.Value.Name, _selectedCharacterInfo.Value.Class);

            DisableInteractionDuringSelection(characterName);
            _ = _networkManager.SendSelectCharacterRequestAsync(characterName);
        }

        public override void Dispose()
        {
            _logger.LogDebug("Disposing SelectCharacterScene.");
            UnsubscribeFromNetworkEvents();

            var refreshCancellation = Interlocked.Exchange(ref _characterRefreshCancellation, null);
            refreshCancellation?.Cancel();
            refreshCancellation?.Dispose();

            if (_characterController != null)
            {
                _characterController.CharacterClicked -= OnControllerCharacterClicked;
                _characterController.CharacterDoubleClicked -= OnControllerCharacterDoubleClicked;
                _characterController.Dispose();
                _characterController = null;
            }

            CloseCharacterCreationDialog();
            if (_loadingScreen != null)
            {
                Controls.Remove(_loadingScreen);
                _loadingScreen.Dispose();
                _loadingScreen = null;
            }
            RestoreDayNightCycle();
            base.Dispose();
        }

        private void SubscribeToNetworkEvents()
        {
            if (_networkManager != null)
            {
                _networkManager.EnteredGame += HandleEnteredGame;
                _networkManager.ErrorOccurred += HandleNetworkError;
                _networkManager.ConnectionStateChanged += HandleConnectionStateChange;
                _networkManager.CharacterListReceived += HandleCharacterListReceived;
                _networkManager.LogoutResponseReceived += HandleLogoutResponseReceived;
                _logger.LogDebug("SelectCharacterScene subscribed to NetworkManager events (including LogoutResponseReceived).");
            }
        }

        private void UnsubscribeFromNetworkEvents()
        {
            if (_networkManager != null)
            {
                _networkManager.EnteredGame -= HandleEnteredGame;
                _networkManager.ErrorOccurred -= HandleNetworkError;
                _networkManager.ConnectionStateChanged -= HandleConnectionStateChange;
                _networkManager.CharacterListReceived -= HandleCharacterListReceived;
                _networkManager.LogoutResponseReceived -= HandleLogoutResponseReceived;
                _logger.LogDebug("SelectCharacterScene unsubscribed from NetworkManager events.");
            }
        }

        private void HandleLogoutResponseReceived(object sender, LogOutType logoutType)
        {
            _logger.LogInformation("SelectCharacterScene.HandleLogoutResponseReceived: Type={Type}", logoutType);
            // Intentional logout handling is now done in HandleConnectionStateChange
            // which reacts to the Disconnected state after logout
        }

        private void HandleCharacterListReceived(object sender,
            List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)> characters)
        {
            var snapshot = characters?.ToList()
                ?? new List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)>();
            _logger.LogInformation(
                "SelectCharacterScene.HandleCharacterListReceived: Received {Count} characters",
                snapshot.Count);

            var refreshCancellation = new CancellationTokenSource();
            var previousCancellation = Interlocked.Exchange(
                ref _characterRefreshCancellation,
                refreshCancellation);
            previousCancellation?.Cancel();
            previousCancellation?.Dispose();
            CancellationToken token = refreshCancellation.Token;

            MuGame.ScheduleOnMainThread(
                () => RefreshCharacterListAsync(snapshot, token),
                MainThreadDispatcher.WorkPriority.High,
                "HandleCharacterListReceived.RefreshInPlace");
        }

        private async Task RefreshCharacterListAsync(
            List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)> characters,
            CancellationToken cancellationToken)
        {
            await _characterRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await MuGame.YieldToNextFrameAsync(
                    "CharacterSelection.Refresh.Begin",
                    MainThreadDispatcher.WorkPriority.High);

                if (MuGame.Instance.ActiveScene != this || _characterController == null || _selectWorld == null)
                    return;

                string selectedName = _currentlySelectedCharacterName;
                _isSelectionInProgress = true;
                UpdateNavigationButtonState();

                _characters.Clear();
                _characters.AddRange(characters);

                await _characterController.CreateCharactersAsync(
                    _characters,
                    _selectWorld,
                    this,
                    _selectWorld.CharacterDisplayPosition,
                    _selectWorld.CharacterDisplayAngle,
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                if (MuGame.Instance.ActiveScene != this)
                    return;

                int selectedIndex = -1;
                if (!string.IsNullOrEmpty(selectedName))
                {
                    selectedIndex = _characters.FindIndex(character =>
                        string.Equals(character.Name, selectedName, StringComparison.Ordinal));
                }

                if (selectedIndex < 0 && _characters.Count > 0)
                    selectedIndex = 0;

                _currentCharacterIndex = selectedIndex;
                _currentlySelectedCharacterName = selectedIndex >= 0
                    ? _characters[selectedIndex].Name
                    : null;
                if (selectedIndex >= 0)
                    _characterController.SetActiveCharacter(selectedIndex);

                _selectedCharacterInfo = null;
                PositionNavigationButtons();
                _logger.LogInformation(
                    "Character selection refreshed in place with {Count} slots.",
                    _characters.Count);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Character selection refresh superseded by a newer list.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing SelectCharacterScene in place.");
            }
            finally
            {
                _isSelectionInProgress = false;
                if (MuGame.Instance.ActiveScene == this)
                    UpdateNavigationButtonState();
                _characterRefreshLock.Release();
            }
        }

        private void HandleEnteredGame(object sender, EventArgs e)
        {
            _logger.LogInformation(">>> SelectCharacterScene.HandleEnteredGame: Event received.");

            if (!_selectedCharacterInfo.HasValue)
            {
                _logger.LogError("!!! SelectCharacterScene.HandleEnteredGame: EnteredGame event received, but _selectedCharacterInfo is null. Cannot change to GameScene.");
                if (_loadingScreen != null)
                {
                    MuGame.ScheduleOnMainThread(() =>
                    {
                        Controls.Remove(_loadingScreen);
                        _loadingScreen.Dispose();
                        _loadingScreen = null;
                        EnableInteractionAfterSelection();
                    });
                }
                return;
            }

            var characterInfo = _selectedCharacterInfo.Value;
            _logger.LogInformation("--- SelectCharacterScene.HandleEnteredGame: Scheduling scene change to GameScene for character: {Name} ({Class})",
                characterInfo.Name, characterInfo.Class);

            MuGame.ScheduleOnMainThread(() =>
            {
                _logger.LogInformation("--- SelectCharacterScene.HandleEnteredGame (UI Thread): Executing scheduled scene change...");
                if (MuGame.Instance.ActiveScene == this)
                {
                    try
                    {
                        MuGame.Instance.ChangeScene(new GameScene(characterInfo, _networkManager));
                        _logger.LogInformation("<<< SelectCharacterScene.HandleEnteredGame (UI Thread): ChangeScene to GameScene call completed.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "!!! SelectCharacterScene.HandleEnteredGame (UI Thread): Exception during ChangeScene to GameScene.");
                        EnableInteractionAfterSelection();
                    }
                }
                else
                {
                    _logger.LogWarning("<<< SelectCharacterScene.HandleEnteredGame (UI Thread): Scene changed before execution. Aborting change to GameScene.");
                }
            }, MainThreadDispatcher.WorkPriority.Critical, "HandleEnteredGame");
        }

        private void HandleNetworkError(object sender, string errorMessage)
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                _logger.LogError("SelectCharacterScene received NetworkError: {Error}", errorMessage);
                MessageWindow.Show($"Network Error: {errorMessage}");
                EnableInteractionAfterSelection();
                RequestReturnToLogin();
            });
        }

        private void HandleConnectionStateChange(object sender, ClientConnectionState newState)
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                _logger.LogDebug("SelectCharacterScene received ConnectionStateChanged: {NewState}", newState);
                if (newState == ClientConnectionState.Disconnected)
                {
                    if (_isIntentionalLogout)
                    {
                        _logger.LogInformation("Intentional logout - returning to LoginScene.");
                    }
                    else
                    {
                        _logger.LogWarning("Disconnected while in character selection. Returning to LoginScene.");
                        MessageWindow.Show("Connection lost.");
                    }

                    RequestReturnToLogin();
                }
            });
        }

        private void RequestReturnToLogin()
        {
            if (_returnToLoginRequested || MuGame.Instance.ActiveScene != this)
                return;

            _returnToLoginRequested = true;
            MuGame.Instance.ChangeScene<LoginScene>();
        }

        private void DisableInteractionDuringSelection(string characterName, bool showLoadingScreen = true)
        {
            _isSelectionInProgress = true;
            if (_selectWorld != null)
            {
                _selectWorld.Interactive = false;
            }
            if (_characterController != null)
            {
                foreach (var player in _characterController.Characters)
                {
                    player.Interactive = false;
                }
                foreach (var label in _characterController.Labels.Values)
                {
                    label.Visible = false;
                }
            }
            if (showLoadingScreen)
                ShowEnteringLoadingScreen(characterName);

            UpdateNavigationButtonState();
        }

        /// <summary>
        /// 蓋上「進入遊戲中」的載入畫面。
        ///
        /// 與停用互動分開，是因為載入畫面一顯示，Draw 就會 return 到純黑頁 ——
        /// 離場動畫會完全看不到。要先讓角色走完再蓋。
        /// </summary>
        private void ShowEnteringLoadingScreen(string characterName)
        {
            if (_loadingScreen == null)
            {
                _loadingScreen = new LoadingScreenControl { Visible = true };
                Controls.Add(_loadingScreen);
            }

            _loadingScreen.Message = $"Entering game as {characterName}...";
            _loadingScreen.Progress = 0f;
            _loadingScreen.Visible = true;
            _loadingScreen.BringToFront();
            Cursor?.BringToFront();
        }

        /// <summary>ENTER 之後讓其他角色走開多久，再真正進入遊戲。</summary>
        private static readonly TimeSpan DepartureDuration = TimeSpan.FromSeconds(3);

        /// <summary>
        /// 先讓沒被選中的角色走出畫面，再送出選角請求。
        /// 這三秒是刻意留的表演時間。
        /// </summary>
        private async Task EnterGameAfterDepartureAsync(string characterName)
        {
            _characterController?.BeginDeparture();

            await Task.Delay(DepartureDuration);

            ShowEnteringLoadingScreen(characterName);
            await _networkManager.SendSelectCharacterRequestAsync(characterName);
        }

        private void EnableInteractionAfterSelection()
        {
            _isSelectionInProgress = false;
            if (_selectWorld != null)
            {
                _selectWorld.Interactive = true;
            }
            if (_characterController != null)
            {
                foreach (var player in _characterController.Characters)
                {
                    player.Interactive = true;
                }
                // Labels visibility will be restored by controller's active character logic
                if (_characterController.ActiveCharacter != null)
                {
                    var activePlayer = _characterController.ActiveCharacter;
                    if (_characterController.Labels.TryGetValue(activePlayer, out var label))
                    {
                        label.Visible = true;
                    }
                }
            }
            _selectedCharacterInfo = null;

            if (_loadingScreen != null)
            {
                Controls.Remove(_loadingScreen);
                _loadingScreen.Dispose();
                _loadingScreen = null;
            }

            UpdateNavigationButtonState();
        }

        private void OnCreateCharacterButtonClick(object sender, EventArgs e)
        {
            if (_characterCreationDialog != null)
            {
                // Dialog already open
                return;
            }

            _logger.LogInformation("Opening character creation dialog...");

            // Create and show dialog
            _characterCreationDialog = new CharacterCreationDialog();
            _characterCreationDialog.CharacterCreateRequested += OnCharacterCreateRequested;
            _characterCreationDialog.CancelRequested += OnCharacterCreationCancelled;
            
            Controls.Add(_characterCreationDialog);
            _characterCreationDialog.BringToFront();
            Cursor?.BringToFront();

            // Disable interactions with world
            if (_selectWorld != null)
            {
                _selectWorld.Interactive = false;
            }
            if (_createCharacterButton != null)
            {
                _createCharacterButton.Enabled = false;
            }
        }

        private void OnCharacterCreateRequested(object sender, (string Name, CharacterClassNumber Class) data)
        {
            _logger.LogInformation("Character creation requested: Name={Name}, Class={Class}", data.Name, data.Class);

            // Close dialog
            CloseCharacterCreationDialog();

            // Send create character request
            var characterService = _networkManager?.GetCharacterService();
            if (characterService != null)
            {
                _ = characterService.SendCreateCharacterRequestAsync(data.Name, data.Class);
                MessageWindow.Show($"Creating character '{data.Name}'...\nPlease wait for server response.");
                
                // Request updated character list after a short delay
                _ = RefreshCharacterListAfterDelay();
            }
            else
            {
                _logger.LogError("CharacterService not available - cannot create character.");
                MessageWindow.Show("Error: Cannot create character at this time.");
            }
        }

        private async Task RefreshCharacterListAfterDelay()
        {
            // Wait for server to process creation
            await Task.Delay(2000);
            
            _logger.LogInformation("Requesting updated character list after creation...");
            var characterService = _networkManager?.GetCharacterService();
            if (characterService != null)
            {
                await characterService.RequestCharacterListAsync();
                // Note: The character list handler will update the scene
            }
        }

        private void OnCharacterCreationCancelled(object sender, EventArgs e)
        {
            _logger.LogInformation("Character creation cancelled.");
            CloseCharacterCreationDialog();
        }
        
        private void OnControllerCharacterClicked(object sender, string characterName)
        {
            _logger.LogInformation("Controller: Character '{Name}' clicked.", characterName);

            _currentlySelectedCharacterName = characterName;

            // Find index
            for (int i = 0; i < _characters.Count; i++)
            {
                if (_characters[i].Name == characterName)
                {
                    _currentCharacterIndex = i;
                    break;
                }
            }

            PositionNavigationButtons();
            UpdateNavigationButtonState();
        }

        private void OnControllerCharacterDoubleClicked(object sender, string characterName)
        {
            // 手機只有一條進入遊戲的路徑：選取 → ENTER GAME。
            // 觸控的「雙擊」既不直覺又容易誤判，直接不接受。
            if (s_mobile)
            {
                _logger.LogDebug("Controller: double click ignored on touch platforms.");
                return;
            }

            _logger.LogInformation("Controller: Character '{Name}' double-clicked.", characterName);
            CharacterSelected(characterName);
        }
        
        private void OnDeleteCharacterButtonClick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentlySelectedCharacterName))
            {
                _logger.LogWarning("Delete button clicked but no character selected.");
                return;
            }
            
            string characterToDelete = _currentlySelectedCharacterName;
            _logger.LogInformation("Delete button clicked for character '{Name}'.", characterToDelete);
            
            // Create security code input dialog
            var securityCodeDialog = new CharacterDeletionDialog(characterToDelete);
            securityCodeDialog.DeleteConfirmed += (s, securityCode) =>
            {
                _logger.LogInformation("User confirmed deletion of '{Name}' with security code.", characterToDelete);
                var characterService = _networkManager?.GetCharacterService();
                if (characterService != null)
                {
                    _ = characterService.SendDeleteCharacterRequestAsync(characterToDelete, securityCode);
                    MessageWindow.Show($"Deleting character '{characterToDelete}'...\nPlease wait for server response.");
                    
                    // Clear selection
                    _currentlySelectedCharacterName = null;
                    UpdateNavigationButtonState();
                        }
                else
                {
                    _logger.LogError("CharacterService not available - cannot delete character.");
                    MessageWindow.Show("Error: Cannot delete character at this time.");
                }
                
                // Clean up dialog
                Controls.Remove(securityCodeDialog);
                securityCodeDialog.Dispose();
                
                // Re-enable world interaction
                if (_selectWorld != null)
                {
                    _selectWorld.Interactive = true;
                }
            };
            
            securityCodeDialog.CancelRequested += (s, args) =>
            {
                _logger.LogInformation("User cancelled deletion of '{Name}'.", characterToDelete);
                
                // Clean up dialog
                Controls.Remove(securityCodeDialog);
                securityCodeDialog.Dispose();
                
                // Re-enable world interaction
                if (_selectWorld != null)
                {
                    _selectWorld.Interactive = true;
                }
            };
            
            // Show dialog
            Controls.Add(securityCodeDialog);
            securityCodeDialog.BringToFront();
            Cursor?.BringToFront();
            
            // Disable world interaction while dialog is open
            if (_selectWorld != null)
            {
                _selectWorld.Interactive = false;
            }
        }

        private void CloseCharacterCreationDialog()
        {
            if (_characterCreationDialog != null)
            {
                _characterCreationDialog.CharacterCreateRequested -= OnCharacterCreateRequested;
                _characterCreationDialog.CancelRequested -= OnCharacterCreationCancelled;
                Controls.Remove(_characterCreationDialog);
                _characterCreationDialog.Dispose();
                _characterCreationDialog = null;
            }

            // Re-enable interactions
            if (_selectWorld != null)
            {
                _selectWorld.Interactive = true;
            }
            UpdateNavigationButtonState();
        }

        public override void Update(GameTime gameTime)
        {
            if (_loadingScreen != null && _loadingScreen.Visible)
            {
                _loadingScreen.Update(gameTime);
                Cursor?.Update(gameTime);
                DebugPanel?.Update(gameTime);
                return;
            }
            if (!_initialLoadComplete && Status == GameControlStatus.Initializing)
            {
                Cursor?.Update(gameTime);
                DebugPanel?.Update(gameTime);
                return;
            }

            // Handle character card mouse interaction
            UpdateCharacterCardInteraction();

            base.Update(gameTime);
        }

        private void UpdateCharacterCardInteraction()
        {
            if (_characterCardRects.Count == 0 || !_initialLoadComplete || Cursor == null)
                return;

            var mouseState = MuGame.Instance.UiMouseState;

            // 直接讀輸入狀態，不要用 Cursor.X/Y。
            //
            // 這個方法是在 base.Update() 之前呼叫的，而 Cursor 是 base.Update() 裡的子控制項
            // —— 也就是說 Cursor.X/Y 永遠慢一幀。滑鼠上看不出來（游標早就移到定位了），
            // 但觸控上「按下」和「移到該位置」是同一幀發生的：
            // 第一次點擊時座標還停在上一次觸控的位置，於是只設到 hover，沒有真正選取；
            // 要再點第二次才會選中。這就是「要點兩次、而且有深淺兩種選中狀態」的原因。
            Point mousePos = new Point(mouseState.X, mouseState.Y);

            int previousHovered = _hoveredCardIndex;
            _hoveredCardIndex = -1;

            bool mousePressed = mouseState.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed;
            bool mouseClicked = mousePressed && !_previousMousePressed;
            _previousMousePressed = mousePressed;

            // 手機沒有「游標懸停」這個狀態，同一個介面只該有一種選中樣式。
            if (s_mobile && !mousePressed)
            {
                return;
            }

            // Only check cards if mouse is in the character list area
            if (!_characterListRect.Contains(mousePos))
                return;

            // Check if mouse is over any character card
            for (int i = 0; i < _characterCardRects.Count; i++)
            {
                if (_characterCardRects[i].Contains(mousePos))
                {
                    _hoveredCardIndex = i;
                    
                    if (previousHovered != _hoveredCardIndex)
                    {
                                }

                    // 點一下 = 選取。進入遊戲一律走 ENTER GAME 按鈕 ——
                    // 「再點一次同一張卡就進去」看似方便，實際上是誤觸來源：
                    // 玩家只是想確認選對了人，結果就被送進遊戲了。
                    if (mouseClicked)
                    {
                        SelectCharacterByIndex(i);
                        _logger.LogInformation("Character card {Index} clicked: {Name}", i, _characters[i].Name);
                    }
                    break;
                }
            }

            if (previousHovered != _hoveredCardIndex && previousHovered != -1)
            {
                }
        }

        private void SelectCharacterByIndex(int index)
        {
            if (index < 0 || index >= _characters.Count || _characterController == null)
                return;

            _currentCharacterIndex = index;
            var character = _characters[index];
            _currentlySelectedCharacterName = character.Name;
            _characterController.SetActiveCharacter(_currentCharacterIndex);
            PositionNavigationButtons();
            UpdateNavigationButtonState();

            _logger.LogInformation("Character '{Name}' selected via card click.", character.Name);
        }

        public override void Draw(GameTime gameTime)
        {
            if (_loadingScreen != null && _loadingScreen.Visible)
            {
                // 純黑、不畫背景圖。
                //
                // 原本鋪的是 MGContent/Background.jpg（MU 的宣傳圖），它的
                // MUMono 標誌在原圖是上方置中，但整張圖被拉伸進 UiScaler 的
                // 虛擬座標再映射到 2.26 的寬螢幕，標誌因此被擠壓變形，
                // 看起來就是「左上角有一段看不清楚的文字」。
                //
                // 之後要換成自己的載入背景，就在這裡畫上去。
                GraphicsDevice.Clear(Color.Black);

                // 只有載入真的拖久了才顯示進度條。
                // 只載舞台附近的物件之後，這段通常不到半秒 —— 進度條閃一下反而
                // 像「又出現一個載入畫面」，比什麼都不顯示更干擾。
                _firstLoadingDrawAt ??= gameTime.TotalGameTime;
                if (gameTime.TotalGameTime - _firstLoadingDrawAt.Value
                    > LoadingIndicatorDelay)
                {
                    _progressBar.Progress = _loadingScreen.Progress;
                    _progressBar.StatusText = _loadingScreen.Message;
                    _progressBar.Visible = true;
                    _progressBar.Draw(gameTime);
                }

                return;
            }

            // 載入畫面收掉之後，進度條也必須跟著收。
            // 它是手動繪製的，不會因為載入結束就自己消失，而原本唯一會關掉它的
            // 那行寫在「移除載入畫面」的分支裡 —— 載入畫面若已被別處收掉就永遠
            // 不會執行，於是進度條一直留在畫面底部顯示 0%。
            // 動作鈕改排成底部橫列之後，它就正面撞上按鈕了。
            if (_progressBar is { Visible: true })
                _progressBar.Visible = false;

            // Draw 3D world first
            base.Draw(gameTime);

            // Draw modern UI overlay
            DrawModernUI(gameTime);
        }

        private void DrawModernUI(GameTime gameTime)
        {
            // Draw character info panel
            using (var scope = new SpriteBatchScope(
                GraphicsManager.Instance.Sprite, SpriteSortMode.Deferred,
                BlendState.AlphaBlend, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone,
                null, UiScaler.SpriteTransform))
            {
                var sb = GraphicsManager.Instance.Sprite;
                DrawCharacterPanel(sb);
            }

            // Draw cursor and debug panel on top of everything
            using (var scope = new SpriteBatchScope(
                GraphicsManager.Instance.Sprite, SpriteSortMode.Deferred,
                BlendState.AlphaBlend, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone,
                null, UiScaler.SpriteTransform))
            {
                Cursor?.Draw(gameTime);
                DebugPanel?.Draw(gameTime);
            }
        }

        private void DrawCharacterPanel(SpriteBatch sb)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            var font = GraphicsManager.Instance.Font;
            if (pixel == null || font == null) return;

            if (s_mobile)
            {
                // 名字、職業與圓點原本畫在畫面中央 —— 那是「一次只顯示一個角色」
                // 時代的產物。五個角色並排、各自帶標籤之後，它只是重複，
                // 而且蓋在中間那個角色身上。
                return;
            }

            // Panel background excluding button section (so buttons are visible on top)
            var panelWithoutButtons = new Rectangle(
                _characterPanelRect.X,
                _characterPanelRect.Y,
                _characterPanelRect.Width,
                _characterPanelRect.Height - _buttonSectionRect.Height
            );
            var headerRect = new Rectangle(_characterPanelRect.X, _characterPanelRect.Y, _characterPanelRect.Width, HEADER_HEIGHT);
            string headerText = "CHARACTERS";
            float headerScale = s_mobile ? 0.68f : 0.75f;

            if (s_mobile)
            {
                // 與登入、選伺服器同一套：半透明深色 + 一條細邊框 + 白灰兩色文字
                Client.Main.Controls.UI.MobileUi.DrawPanel(sb, panelWithoutButtons, HEADER_HEIGHT);
            }
            else
            {
                UiDrawHelper.DrawVerticalGradient(sb, panelWithoutButtons, Theme.BgMid, Theme.BgDark);

                // Outer border (excluding button section - no bottom border, side borders stop at character list)
                int borderEndY = _characterListRect.Bottom;
                sb.Draw(pixel, new Rectangle(_characterPanelRect.X - 1, _characterPanelRect.Y - 1, _characterPanelRect.Width + 2, 1), Theme.BorderOuter); // Top border
                sb.Draw(pixel, new Rectangle(_characterPanelRect.X - 1, _characterPanelRect.Y, 1, borderEndY - _characterPanelRect.Y), Theme.BorderOuter); // Left border (stops at character list)
                sb.Draw(pixel, new Rectangle(_characterPanelRect.Right, _characterPanelRect.Y, 1, borderEndY - _characterPanelRect.Y), Theme.BorderOuter); // Right border (stops at character list)

                UiDrawHelper.DrawHorizontalGradient(sb, headerRect, Theme.BgLighter, Theme.BgMid);
                UiDrawHelper.DrawCornerAccents(sb, headerRect, Theme.Accent, 12, 2);

                sb.Draw(pixel, new Rectangle(headerRect.X, headerRect.Bottom - 1, headerRect.Width, 1), Theme.BorderInner);
                sb.Draw(pixel, new Rectangle(headerRect.X, headerRect.Bottom - 2, headerRect.Width, 1), Theme.Accent * 0.3f);
            }

            // Header text
            Vector2 headerTextSize = font.MeasureString(headerText) * headerScale;
            Vector2 headerTextPos = new Vector2(
                headerRect.X + (headerRect.Width - headerTextSize.X) / 2,
                headerRect.Y + (headerRect.Height - headerTextSize.Y) / 2
            );
            sb.DrawString(font, headerText, headerTextPos + new Vector2(1, 1), Color.Black * 0.7f, 0f, Vector2.Zero, headerScale, SpriteEffects.None, 0f);
            sb.DrawString(font, headerText, headerTextPos,
                s_mobile ? Client.Main.Controls.UI.MobileUi.TextPrimary : Theme.TextGold,
                0f, Vector2.Zero, headerScale, SpriteEffects.None, 0f);

            // Draw character list separator (top)
            sb.Draw(pixel, new Rectangle(_characterListRect.X, _characterListRect.Y, _characterListRect.Width, 1), Theme.BorderInner);
            
            // Draw separator between character list and buttons (bottom)
            sb.Draw(pixel, new Rectangle(_characterListRect.X, _characterListRect.Bottom, _characterListRect.Width, 1), Theme.BorderInner);

            // Draw character cards
            for (int i = 0; i < _characters.Count && i < _characterCardRects.Count; i++)
            {
                DrawCharacterCard(sb, pixel, font, i, _characterCardRects[i], _characters[i]);
            }
        }

        private void DrawCharacterCard(SpriteBatch sb, Texture2D pixel, SpriteFont font, int index, Rectangle cardRect, (string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance) character)
        {
            bool isSelected = _currentCharacterIndex == index;

            // 手機不畫 hover。桌面的「淺色 hover + 深色選中」在觸控上會變成
            // 同一個畫面同時有兩種選中樣式，玩家分不清哪一個才算數。
            bool isHovered = !s_mobile && _hoveredCardIndex == index;

            // Card background
            Color bgColor = isSelected ? Theme.BgLighter : (isHovered ? Theme.BgMid : Theme.BgDark);

            // 手機沒有框線，所以選中必須完全靠底色 —— 差距要拉得夠開才看得出來
            if (s_mobile)
                sb.Draw(pixel, cardRect, isSelected
                    ? Client.Main.Controls.UI.MobileUi.TitleBarFill * 1.35f
                    : Client.Main.Controls.UI.MobileUi.PanelFill * 0.85f);
            else
                sb.Draw(pixel, cardRect, bgColor);

            // Card border
            // 手機：<b>不畫框線</b>。
            //
            // 選中與否用底色深淺表達就夠了。一張卡片如果框一個顏色、底一個顏色、
            // 名字一個顏色、等級再一個顏色，四種顏色卻只傳達一件事（有沒有選中）——
            // 使用者的要求是「一個按鈕不要有太多顏色」，這是最典型的例子。
            // 見 docs/手機遊戲界面規格.md。
            Color borderColor = isSelected ? Theme.Accent : Theme.BorderInner;
            int borderWidth = s_mobile ? 0 : (isSelected ? 2 : 1);
            sb.Draw(pixel, new Rectangle(cardRect.X, cardRect.Y, cardRect.Width, borderWidth), borderColor);
            sb.Draw(pixel, new Rectangle(cardRect.X, cardRect.Bottom - borderWidth, cardRect.Width, borderWidth), borderColor);
            sb.Draw(pixel, new Rectangle(cardRect.X, cardRect.Y, borderWidth, cardRect.Height), borderColor);
            sb.Draw(pixel, new Rectangle(cardRect.Right - borderWidth, cardRect.Y, borderWidth, cardRect.Height), borderColor);

            // Character info —— 卡片在手機上放大了，字距與字級要跟著放大
            int textX = cardRect.X + (s_mobile ? 16 : 10);
            int textY = cardRect.Y + (s_mobile ? 14 : 10);
            float nameScale = s_mobile ? 0.86f : 0.7f;
            float infoScale = s_mobile ? 0.72f : 0.6f;

            // Name
            // 選中的卡片名字不換顏色 —— 底色已經說了它被選中。換色只是多一種顏色。
            Color nameColor = s_mobile
                ? (isSelected ? Client.Main.Controls.UI.MobileUi.TextPrimary : Client.Main.Controls.UI.MobileUi.TextDim)
                : (isSelected ? Theme.TextGold : Theme.TextWhite);
            sb.DrawString(font, character.Name, new Vector2(textX, textY) + new Vector2(1, 1), Color.Black * 0.7f, 0f, Vector2.Zero, nameScale, SpriteEffects.None, 0f);
            sb.DrawString(font, character.Name, new Vector2(textX, textY), nameColor, 0f, Vector2.Zero, nameScale, SpriteEffects.None, 0f);
            textY += s_mobile ? 30 : 22;

            // Class and Level
            string classLevelText = $"{character.Class}  •  Lv.{character.Level}";
            Color infoColor = s_mobile
                ? Client.Main.Controls.UI.MobileUi.TextDim
                : (isSelected ? Theme.AccentBright : Theme.TextGray);
            sb.DrawString(font, classLevelText, new Vector2(textX, textY) + new Vector2(1, 1), Color.Black * 0.7f, 0f, Vector2.Zero, infoScale, SpriteEffects.None, 0f);
            sb.DrawString(font, classLevelText, new Vector2(textX, textY), infoColor, 0f, Vector2.Zero, infoScale, SpriteEffects.None, 0f);
        }

        private new void DrawBackground()
        {
            if (_backgroundTexture == null) return;

            using var scope = new SpriteBatchScope(
                GraphicsManager.Instance.Sprite, SpriteSortMode.Deferred,
                BlendState.AlphaBlend, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone,
                null, UiScaler.SpriteTransform);

            GraphicsManager.Instance.Sprite.Draw(_backgroundTexture,
                new Rectangle(0, 0, UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y), Color.White);
        }

        private void OnEnterGameButtonClick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentlySelectedCharacterName))
            {
                MessageWindow.Show("Please select a character first.");
                return;
            }

            // Enter game with selected character
            var matchedIndex = _characters.FindIndex(c => c.Name.Equals(_currentlySelectedCharacterName, StringComparison.OrdinalIgnoreCase));
            if (matchedIndex < 0)
            {
                _logger.LogWarning("Character '{Name}' not found in character list.", _currentlySelectedCharacterName);
                MessageWindow.Show($"Error: Character '{_currentlySelectedCharacterName}' not found.");
                return;
            }

            _selectedCharacterInfo = _characters[matchedIndex];
            _currentCharacterIndex = matchedIndex;
            _characterController?.SetActiveCharacter(_currentCharacterIndex);

            ClientConnectionState currentState = _networkManager.CurrentState;
            bool canSelect = currentState == ClientConnectionState.ConnectedToGameServer ||
                             currentState == ClientConnectionState.SelectingCharacter;

            if (!canSelect)
            {
                _logger.LogWarning("Character selection attempted but NetworkManager state is not ConnectedToGameServer or SelectingCharacter. State: {State}", currentState);
                MessageWindow.Show($"Cannot select character. Invalid network state: {currentState}");
                _selectedCharacterInfo = null;
                return;
            }

            _logger.LogInformation("Character '{CharacterName}' (Class: {Class}) selected in scene. Sending request...",
                                   _selectedCharacterInfo.Value.Name, _selectedCharacterInfo.Value.Class);

            // 停用互動，但先不蓋載入畫面 —— 要讓玩家看到其他角色走開。
            DisableInteractionDuringSelection(_currentlySelectedCharacterName, showLoadingScreen: false);
            _ = EnterGameAfterDepartureAsync(_currentlySelectedCharacterName);
        }

        /// <summary>
        /// 送出登出再關閉程式。等一小段時間是為了讓封包真的送出去 ——
        /// 直接 Exit() 會讓連線被硬斷，伺服器要等逾時才釋放帳號。
        /// </summary>
        private async Task ExitGameAsync()
        {
            try
            {
                await _networkManager.GetCharacterService()
                    .SendLogoutRequestAsync(LogOutType.CloseGame);
                await Task.Delay(400);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send logout before exit; closing anyway.");
            }

            MuGame.Instance.Exit();
        }

        private void OnExitButtonClick(object sender, EventArgs e)
        {
            if (_isIntentionalLogout)
                return;

            _logger.LogInformation("Exit button clicked - closing the game.");
            _isIntentionalLogout = true;
            _isSelectionInProgress = true;

            if (_selectWorld != null)
                _selectWorld.Interactive = false;

            if (_loadingScreen == null)
            {
                _loadingScreen = new LoadingScreenControl { Visible = true };
                Controls.Add(_loadingScreen);
            }

            _loadingScreen.Message = "Exiting...";
            _loadingScreen.Progress = 0f;
            _loadingScreen.Visible = true;
            _loadingScreen.BringToFront();
            Cursor?.BringToFront();
            UpdateNavigationButtonState();

            // EXIT 是「離開遊戲」，不是「回伺服器選擇」。
            // 先送 CloseGame 讓伺服器釋放帳號佔用 —— 不送就直接關的話，
            // 伺服器會有一段時間仍視為在線，下次登入拿到 AccountAlreadyConnected。
            _ = ExitGameAsync();
        }
    }
}
