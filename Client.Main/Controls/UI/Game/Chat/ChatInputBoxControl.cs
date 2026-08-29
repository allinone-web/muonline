using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Client.Main.Controls.UI
{
    public class ChatInputBoxControl : UIControl, IUiTexturePreloadable
    {
        // Fields
        private const int CHATBOX_WIDTH = 281;

        // ── 手機的輸入列 ──
        //
        // 桌面是 281x47，輸入欄 176x14、字級 10。那是滑鼠加實體鍵盤的尺寸：
        // 在手機上輸入欄只有一根手指的三分之一高，字小到看不清自己打了什麼 ——
        // 使用者的說法是「根本沒法用」。
        //
        // 手機改成一整條橫跨畫面下緣的列：左邊一顆頻道鈕（點一下換頻道，
        // 取代原本四個 27x27 的分頁貼圖）、中間是輸入欄、右邊一顆送出鈕。
        // 高度 72，輸入欄 48 —— 都在拇指按得到的範圍。
        private const int MobileBarHeight = 72;

        /// <summary>
        /// 輸入列上方那一排開關的高度與尺寸。
        ///
        /// 桌面版有十顆 27x27 的貼圖鈕（四個頻道分頁 + 悄悄話/系統/紀錄/外框/
        /// 大小/透明度）。27x27 在 iPhone 上不到 15 pt，而且是為 1024x768 畫的
        /// 點陣圖。手機只保留真正用得到的三個開關，做成文字鈕：
        /// 頻道已經有自己的按鈕（點一下輪替），外框／大小／透明度是桌面視窗的
        /// chrome，手機的聊天視窗不能拖也不能縮放，留著沒有意義。
        /// </summary>
        private const int MobileToggleHeight = 44;
        private const int MobileToggleGap = 8;
        private const int MobileToggleWidth = 128;
        private const int MobileBarPadding = 10;
        private const int MobileChannelButtonWidth = 132;
        private const int MobileSendButtonWidth = 120;
        private const int MobileInnerHeight = MobileBarHeight - MobileBarPadding * 2;
        private const float MobileFontSize = 19f;

        /// <summary>
        /// 輸入列的寬度。
        ///
        /// 原本是整條畫面寬。橫置的手機很寬（虛擬座標常常超過 1600），
        /// 一個只打幾個字的輸入欄沒有理由那麼長 —— 而且它橫跨下緣就會壓在
        /// 藥水鈕與 ATK 上面。收成畫面的六成、上限 900。
        /// </summary>
        public static int MobileWidth
        {
            get
            {
                int canvas = UiScaler.VirtualSize.X;
                return Math.Clamp((int)(canvas * 0.6f), 320, 900);
            }
        }

        public static int ChatBoxHeight => MobileUi.IsMobile ? MobileBarHeight : 47;

        /// <summary>桌面沿用原本的 47；手機是一整條 72 高的輸入列。</summary>
        public static int CHATBOX_HEIGHT => ChatBoxHeight;
        private const int BUTTON_WIDTH = 27;
        private const int BUTTON_HEIGHT = 26;
        private const int GROUP_SEPARATING_WIDTH = 6;
        private const int INPUT_MESSAGE_TYPE_COUNT = 4;

        // Button X positions
        private const int INPUT_TYPE_START_X = 0;
        private const int BLOCK_WHISPER_START_X = INPUT_MESSAGE_TYPE_COUNT * BUTTON_WIDTH + GROUP_SEPARATING_WIDTH; // 4 * 27 + 6 = 114
        private const int SYSTEM_ON_START_X = BLOCK_WHISPER_START_X + BUTTON_WIDTH; // 114 + 27 = 141
        private const int CHATLOG_ON_START_X = SYSTEM_ON_START_X + BUTTON_WIDTH; // 141 + 27 = 168
        private const int FRAME_ON_START_X = CHATLOG_ON_START_X + BUTTON_WIDTH + GROUP_SEPARATING_WIDTH; // 168 + 27 + 6 = 201
        private const int FRAME_RESIZE_START_X = FRAME_ON_START_X + BUTTON_WIDTH; // 201 + 27 = 228
        private const int TRANSPARENCY_START_X = FRAME_RESIZE_START_X + BUTTON_WIDTH; // 228 + 27 = 255

        private const int MAX_CHAT_HISTORY = 12;
        private const int MAX_WHISPER_HISTORY = 5;

        private static readonly string[] s_typeButtonTextures =
        {
            "Interface/newui_chat_normal_on.jpg",
            "Interface/newui_chat_party_on.jpg",
            "Interface/newui_chat_guild_on.jpg",
            "Interface/newui_chat_gens_on.jpg"
        };

        private static readonly string[] s_toggleButtonTextures =
        {
            "Interface/newui_chat_whisper_on.jpg",
            "Interface/newui_chat_system_on.jpg",
            "Interface/newui_chat_chat_on.jpg",
            "Interface/newui_chat_frame_on.jpg",
            "Interface/newui_chat_btn_size.jpg",
            "Interface/newui_chat_btn_alpha.jpg"
        };

        /// <summary>
        /// 聊天框底圖。<b>手機不載</b> —— 改用 MobileUi 自己畫的半透明面板，
        /// 和登入、背包、角色面板同一套。清單見 docs/待清理素材.md。
        ///
        /// 上面那兩組（頻道分頁、開關按鈕）目前手機仍在用貼圖。它們是 27x27 的
        /// 桌面尺寸小圖示，換掉等於要重新設計十個圖示 —— 併到之後的手機聊天
        /// 介面改版一起做，不在這次的面板統一裡。
        /// </summary>
        private static readonly string[] s_baseChatTextures =
        {
            "Interface/newui_chat_back.jpg"
        };

        // Child Controls
        private TextureControl _background;
        private TextFieldControl _chatInput;
        private TextFieldControl _whisperIdInput;
        private SpriteControl[] _typeButtons = new SpriteControl[4]; // Normal, Party, Guild, Gens
        private SpriteControl _whisperToggleButton;
        private SpriteControl _systemToggleButton;
        private SpriteControl _chatLogToggleButton;
        private SpriteControl _frameToggleButton;
        private SpriteControl _sizeButton;
        private SpriteControl _transparencyButton;

        // State
        private InputMessageType _currentInputType = InputMessageType.Chat;
        private bool _isWhisperLocked = false; // Corresponds to m_bBlockWhisper
        private bool _isWhisperSendMode = true; // Corresponds to m_bWhisperSend (true = show ID box)
        private bool _suppressNextEnter;
        private readonly ChatLogWindow _chatLogWindowRef; // Reference to the chat log

        // History
        private List<string> _chatHistory = new List<string>();
        private List<string> _whisperIdHistory = new List<string>();
        private int _currentChatHistoryIndex = 0;
        private int _currentWhisperHistoryIndex = 0;
        private MessageType finalType;

        // Cooldown for chat messages
        private const long ChatCooldownMs = 1000; // 1 Second
        private long _lastChatTime = 0;

        private readonly ILogger<ChatInputBoxControl> _logger;

        // Properties
        public InputMessageType CurrentInputType => _currentInputType;
        public bool IsWhisperLocked => _isWhisperLocked;

        // Event for sending messages
        public event EventHandler<ChatMessageEventArgs> MessageSendRequested;

        // Constructors
        public ChatInputBoxControl(ChatLogWindow chatLogWindow, ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));
            _logger = loggerFactory.CreateLogger<ChatInputBoxControl>();

            _chatLogWindowRef = chatLogWindow ?? throw new ArgumentNullException(nameof(chatLogWindow));
            AutoViewSize = false;
            ViewSize = MobileUi.IsMobile
                ? new Point(MobileWidth, MobileBarHeight)
                : new Point(CHATBOX_WIDTH, CHATBOX_HEIGHT);
            ControlSize = ViewSize;
            Visible = false; // Start hidden.
            Interactive = true; // Needs mouse interaction.
        }

        // Methods
        public IEnumerable<string> GetPreloadTexturePaths()
        {
            if (!MobileUi.IsMobile)
            {
                foreach (var texture in s_baseChatTextures)
                {
                    yield return texture;
                }
            }

            foreach (var texture in s_typeButtonTextures)
            {
                yield return texture;
            }

            foreach (var texture in s_toggleButtonTextures)
            {
                yield return texture;
            }
        }

        public override async Task Load()
        {
            // 1. Background
            if (!MobileUi.IsMobile)
            {
                _background = new TextureControl
                {
                    TexturePath = "Interface/newui_chat_back.jpg",
                    BlendState = BlendState.AlphaBlend, // Assuming JPG might need alpha blend if it has a transparency layer, otherwise Opaque.
                    ViewSize = ViewSize,
                    AutoViewSize = false
                };
                Controls.Add(_background);
            }

            // 2. Text Input Fields
            _chatInput = TextFieldControl.Create();
            _whisperIdInput = TextFieldControl.Create();

            if (MobileUi.IsMobile)
            {
                LayoutMobileFields();
            }
            else
            {
                _chatInput.X = 72;
                _chatInput.Y = 30;
                _chatInput.ViewSize = new Point(176, 14);
                _chatInput.FontSize = 10f;
                _chatInput.BackgroundColor = Color.Black * 0.1f;
                _chatInput.TextColor = new Color(230, 210, 255);

                _whisperIdInput.X = 5;
                _whisperIdInput.Y = 30;
                _whisperIdInput.ViewSize = new Point(60, 14);
                _whisperIdInput.FontSize = 10f;
                _whisperIdInput.BackgroundColor = Color.Black * 0.1f;
                _whisperIdInput.TextColor = new Color(200, 200, 200, 255);
            }

            _whisperIdInput.Visible = false;

            Controls.Add(_chatInput);
            Controls.Add(_whisperIdInput);

            // --- Buttons --------------------------------------------------------------

            // Type Buttons (Normal, Party, Guild, Gens)
            for (int i = 0; i < _typeButtons.Length; i++)
            {
                _typeButtons[i] = CreateButton(INPUT_TYPE_START_X + i * BUTTON_WIDTH, 0,
                                               s_typeButtonTextures[i], $"TypeBtn_{i}");
                int typeIdx = i;
                _typeButtons[i].Click += (s, e) =>
                {
                    SetInputType((InputMessageType)typeIdx);
                    SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav"); // Play sound on click.
                };
                Controls.Add(_typeButtons[i]);
            }

            // Whisper-Lock
            _whisperToggleButton = CreateButton(BLOCK_WHISPER_START_X, 0,
                                                s_toggleButtonTextures[0], "WhisperToggle");
            _whisperToggleButton.Click += (s, e) =>
            {
                ToggleWhisperLock();
                SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav"); // Play sound on click.
            };
            Controls.Add(_whisperToggleButton);

            // System-Messages ON/OFF
            _systemToggleButton = CreateButton(SYSTEM_ON_START_X, 0,
                                               s_toggleButtonTextures[1], "SystemToggle");
            _systemToggleButton.Click += (s, e) =>
            {
                ToggleSystemMessages();
                SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav"); // Play sound on click.
            };
            Controls.Add(_systemToggleButton);

            // Chat-Log ON/OFF
            _chatLogToggleButton = CreateButton(CHATLOG_ON_START_X, 0,
                                                s_toggleButtonTextures[2], "ChatLogToggle");
            _chatLogToggleButton.Click += (s, e) =>
            {
                ToggleChatLogVisibility();
                SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav"); // Play sound on click.
            };
            Controls.Add(_chatLogToggleButton);

            // Show / hide frame (scrollbar, resize etc.)
            _frameToggleButton = CreateButton(FRAME_ON_START_X, 0,
                                              s_toggleButtonTextures[3], "FrameToggle");
            _frameToggleButton.Click += (s, e) =>
            {
                _chatLogWindowRef.ToggleFrame();
                SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav"); // Play sound on click.
            };
            Controls.Add(_frameToggleButton);

            // Size-cycle (F4)
            _sizeButton = CreateButton(FRAME_RESIZE_START_X, 0,
                                       s_toggleButtonTextures[4], "SizeButton");
            _sizeButton.Click += (s, e) =>
            {
                _chatLogWindowRef.CycleSize();
                SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav"); // Play sound on click.
            };
            Controls.Add(_sizeButton);

            // Transparency-cycle
            _transparencyButton = CreateButton(TRANSPARENCY_START_X, 0,
                                               s_toggleButtonTextures[5], "AlphaButton");
            _transparencyButton.Click += (s, e) =>
            {
                _chatLogWindowRef.CycleBackgroundAlpha();
                SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav"); // Play sound on click.
            };
            Controls.Add(_transparencyButton);

            // Subscribe to EnterKeyPressed event
            _chatInput.EnterKeyPressed += (s, e) =>
            {
                if (_suppressNextEnter)
                {
                    _suppressNextEnter = false; // consume the suppression flag first

                    // If after consuming suppression, the input field is truly empty (and whisper not active or empty)
                    // then this Enter press (which was originally to open the chat) should now close it.
                    if (string.IsNullOrEmpty(_chatInput.Value.Trim()) &&
                        (!_isWhisperSendMode || string.IsNullOrEmpty(_whisperIdInput.Value.Trim())))
                    {
                        Hide();
                        if (Scene != null) Scene.ConsumeKeyboardEnter(); // explicitly consume
                    }
                    // If text was entered after opening and before this Enter, do nothing; suppression worked.
                }
                else
                {
                    // Normal Enter press, not suppressed. Let ProcessEnterKey decide to send or hide.
                    ProcessEnterKey();
                }
            };

            // Load textures for all children.
            await base.Load(); // This initializes children, including loading their textures.

            // Initial visual state update.
            UpdateVisualStates();
        }

        private SpriteControl CreateButton(int x, int y, string texturePath, string name)
        {
            return new SpriteControl
            {
                X = x,
                Y = y,
                TexturePath = texturePath,
                TileWidth = BUTTON_WIDTH,
                TileHeight = BUTTON_HEIGHT,
                ViewSize = new Point(BUTTON_WIDTH, BUTTON_HEIGHT),
                BlendState = BlendState.AlphaBlend, // Use AlphaBlend for JPG/TGA with potential transparency.
                Interactive = true,
                Name = name,
                Visible = false // Start hidden, shown based on parent's state.
            };
        }

        public void Show()
        {
            Visible = true;

            // 手機的輸入列橫跨畫面下緣，會壓到藥水鈕與 ATK ——
            // 必須在最上層，否則點擊會被下面的東西先吃掉。
            BringToFront();

            _chatInput.Visible = true;
            _whisperIdInput.Visible = _isWhisperSendMode;

            _suppressNextEnter = true;
            // 手機的頻道與開關改由輸入列自己畫（見 DrawMobileBar），
            // 那 10 顆 27x27 的貼圖鈕一律不顯示。
            if (!MobileUi.IsMobile)
            {
                foreach (var btn in GetAllButtons()) btn.Visible = true;
            }

            _chatInput.Value = string.Empty; // Clear text on show.

            // 手機<b>不要</b>自動聚焦。
            //
            // 聚焦會立刻叫出 iOS 的系統鍵盤，鍵盤佔掉半個畫面 —— 但玩家按 CHAT
            // 常常只是想看一下聊天內容，並不是要打字。想打字的時候點一下輸入欄
            // 就好，那一下正是「我要開始打字了」的明確意思。
            if (!MobileUi.IsMobile)
            {
                _chatInput.Focus();
                Scene.FocusControl = _chatInput;
                _chatInput.MoveCursorToEnd();
            }

            // Reset history navigation.
            _currentChatHistoryIndex = _chatHistory.Count;
            _currentWhisperHistoryIndex = _whisperIdHistory.Count;

            UpdateVisualStates();

            // Play sound on opening.
            SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav");
        }

        public void StartWhisperTo(string targetName)
        {
            if (string.IsNullOrWhiteSpace(targetName))
            {
                return;
            }

            if (!Visible)
            {
                Show();
            }

            _isWhisperSendMode = true;
            _whisperIdInput.Visible = true;
            _whisperIdInput.Value = targetName;
            UpdateVisualStates();

            _whisperIdInput.Blur();
            _chatInput.Focus();
            if (Scene != null)
            {
                Scene.FocusControl = _chatInput;
            }
            _chatInput.MoveCursorToEnd();
        }

        public void Hide()
        {
            Visible = false;
            _chatInput.Visible = false;
            _whisperIdInput.Visible = false;
            foreach (var btn in GetAllButtons()) btn.Visible = false;

            if (Scene.FocusControl == _chatInput || Scene.FocusControl == _whisperIdInput)
            {
                Scene.FocusControl = null; // Remove focus.
            }

            // Play sound on closing.
            SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav");
        }

        // ── 手機輸入列 ──
        private Rectangle _mobileChannelRect;
        private Rectangle _mobileFieldRect;
        private Rectangle _mobileSendRect;

        /// <summary>悄悄話鎖定、系統訊息、聊天紀錄。</summary>
        private readonly Rectangle[] _mobileToggleRects = new Rectangle[3];
        private static readonly string[] MobileToggleLabels = { "WHISPER", "SYSTEM", "LOG" };

        private int _mobilePressedButton = -1;   // 0 = 頻道，1 = 送出，2.. = 開關
        private bool _mobileWasPressed;
        private Point _mobileLastCanvas = Point.Zero;

        private static readonly string[] MobileChannelLabels = { "ALL", "PARTY", "GUILD", "GENS" };

        /// <summary>
        /// 依畫布重新計算手機輸入列的三個區塊：頻道 ｜ 輸入欄 ｜ 送出。
        /// </summary>
        private void LayoutMobileFields()
        {
            var canvas = UiScaler.VirtualSize;
            _mobileLastCanvas = canvas;

            int width = MobileWidth;
            int totalHeight = MobileBarHeight + MobileToggleHeight + MobileToggleGap;
            ViewSize = new Point(width, totalHeight);
            ControlSize = ViewSize;
            AutoViewSize = false;

            // 水平置中；垂直放在畫面中下段而不是貼著下緣。
            //
            // 貼下緣有兩個問題：它正好蓋在藥水鈕與 ATK 上，而且軟鍵盤彈出來
            // 之後輸入欄會被鍵盤本身擋住。0.62 的高度大約在畫面下三分之一，
            // 拇指構得到，也還在鍵盤上緣之上。
            X = (canvas.X - width) / 2;
            Y = (int)(canvas.Y * 0.62f);

            // 開關排在最上面一列，輸入列在它下面
            for (int i = 0; i < _mobileToggleRects.Length; i++)
            {
                _mobileToggleRects[i] = new Rectangle(
                    MobileBarPadding + i * (MobileToggleWidth + MobileToggleGap),
                    0, MobileToggleWidth, MobileToggleHeight);
            }

            int barTop = MobileToggleHeight + MobileToggleGap;

            _mobileChannelRect = new Rectangle(
                MobileBarPadding, barTop + MobileBarPadding, MobileChannelButtonWidth, MobileInnerHeight);

            _mobileSendRect = new Rectangle(
                width - MobileBarPadding - MobileSendButtonWidth, barTop + MobileBarPadding,
                MobileSendButtonWidth, MobileInnerHeight);

            int fieldX = _mobileChannelRect.Right + MobileBarPadding;
            _mobileFieldRect = new Rectangle(
                fieldX, barTop + MobileBarPadding,
                Math.Max(80, _mobileSendRect.X - MobileBarPadding - fieldX), MobileInnerHeight);

            // 輸入欄的內距：文字不要貼著框線。
            _chatInput.X = _mobileFieldRect.X + 12;
            _chatInput.Y = _mobileFieldRect.Y + (MobileInnerHeight - 26) / 2;
            _chatInput.ViewSize = new Point(_mobileFieldRect.Width - 24, 26);
            _chatInput.FontSize = MobileFontSize;
            _chatInput.BackgroundColor = Color.Transparent;   // 底由 DrawMobileBar 畫
            _chatInput.TextColor = MobileUi.TextPrimary;

            // 悄悄話的收件人欄疊在輸入欄的左段，開啟時才顯示
            _whisperIdInput.X = _chatInput.X;
            _whisperIdInput.Y = _chatInput.Y;
            _whisperIdInput.ViewSize = new Point(Math.Min(200, _chatInput.ViewSize.X / 3), 26);
            _whisperIdInput.FontSize = MobileFontSize;
            _whisperIdInput.BackgroundColor = Color.Transparent;
            _whisperIdInput.TextColor = MobileUi.TextDim;
        }

        /// <summary>手機輸入列：頻道鈕、輸入欄的底、送出鈕。</summary>
        private void DrawMobileBar(SpriteBatch sb)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            var font = GraphicsManager.Instance.Font;
            if (pixel == null || font == null)
                return;

            var origin = DisplayRectangle.Location;

            // 三個開關：亮 = 開著。用文字，不用貼圖。
            for (int i = 0; i < _mobileToggleRects.Length; i++)
            {
                bool on = i switch
                {
                    0 => _isWhisperLocked,
                    1 => _chatLogWindowRef.IsSysMsgVisible,
                    _ => _chatLogWindowRef.IsChatLogVisible,
                };

                DrawMobileBarButton(sb, font, origin, _mobileToggleRects[i],
                    MobileToggleLabels[i], _mobilePressedButton == i + 2, emphasis: on);
            }

            // 輸入欄：比面板再深一階，才看得出「這裡可以打字」
            var field = new Rectangle(origin.X + _mobileFieldRect.X, origin.Y + _mobileFieldRect.Y,
                                      _mobileFieldRect.Width, _mobileFieldRect.Height);
            sb.Draw(pixel, field, MobileUi.FieldFill * 0.92f);
            sb.Draw(pixel, new Rectangle(field.X, field.Bottom - 1, field.Width, 1), MobileUi.PanelBorder * 0.35f);

            DrawMobileBarButton(sb, font, origin, _mobileChannelRect,
                MobileChannelLabels[Math.Clamp((int)_currentInputType, 0, MobileChannelLabels.Length - 1)],
                _mobilePressedButton == 0, emphasis: false);

            DrawMobileBarButton(sb, font, origin, _mobileSendRect, "SEND",
                _mobilePressedButton == 1, emphasis: true);
        }

        private static void DrawMobileBarButton(SpriteBatch sb, SpriteFont font, Point origin,
                                                Rectangle local, string label, bool pressed, bool emphasis)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null)
                return;

            var rect = new Rectangle(origin.X + local.X, origin.Y + local.Y, local.Width, local.Height);

            float fill = pressed ? 0.95f : (emphasis ? 0.75f : 0.5f);
            sb.Draw(pixel, rect, MobileUi.TitleBarFill * fill);
            sb.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), MobileUi.PanelBorder * 0.3f);
            sb.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), MobileUi.PanelBorder * 0.3f);

            const float scale = 0.56f;
            Vector2 size = font.MeasureString(label) * scale;
            var pos = new Vector2(rect.X + (rect.Width - size.X) * 0.5f, rect.Y + (rect.Height - size.Y) * 0.5f);
            sb.DrawString(font, label, pos + Vector2.One, Color.Black * 0.6f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.DrawString(font, label, pos, emphasis ? MobileUi.TextPrimary : MobileUi.TextDim,
                          0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// 手機輸入列自己處理觸控。
        ///
        /// 不走 SpriteControl 那套子控制項按鈕：那些是 27x27 的貼圖鈕，
        /// 在手機上既按不到也看不清（見 s_typeButtonTextures 的註解）。
        /// </summary>
        private void UpdateMobileTouch()
        {
            var mouse = MuGame.Instance.UiMouseState;
            bool pressed = mouse.LeftButton == ButtonState.Pressed;
            var origin = DisplayRectangle.Location;
            var position = mouse.Position;

            int Hit()
            {
                if (new Rectangle(origin.X + _mobileChannelRect.X, origin.Y + _mobileChannelRect.Y,
                                  _mobileChannelRect.Width, _mobileChannelRect.Height).Contains(position))
                    return 0;
                if (new Rectangle(origin.X + _mobileSendRect.X, origin.Y + _mobileSendRect.Y,
                                  _mobileSendRect.Width, _mobileSendRect.Height).Contains(position))
                    return 1;

                for (int i = 0; i < _mobileToggleRects.Length; i++)
                {
                    var r = _mobileToggleRects[i];
                    if (new Rectangle(origin.X + r.X, origin.Y + r.Y, r.Width, r.Height).Contains(position))
                        return i + 2;
                }

                return -1;
            }

            if (pressed && !_mobileWasPressed)
            {
                _mobilePressedButton = Hit();
            }
            else if (!pressed && _mobileWasPressed)
            {
                int button = _mobilePressedButton;
                _mobilePressedButton = -1;
                _mobileWasPressed = false;

                if (button >= 0 && button == Hit())
                {
                    SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav");

                    switch (button)
                    {
                        case 0:
                            // 一顆鈕輪流切換四個頻道，取代四個分頁。
                            SetInputType((InputMessageType)(((int)_currentInputType + 1) % MobileChannelLabels.Length));
                            break;
                        case 1:
                            ProcessEnterKey();
                            break;
                        case 2:
                            ToggleWhisperLock();
                            break;
                        case 3:
                            ToggleSystemMessages();
                            break;
                        default:
                            ToggleChatLogVisibility();
                            break;
                    }
                }

                return;
            }

            _mobileWasPressed = pressed;
        }

        public override void Update(GameTime gameTime)
        {
            if (!Visible) return;

            if (MobileUi.IsMobile && UiScaler.VirtualSize != _mobileLastCanvas)
            {
                LayoutMobileFields();
            }

            base.Update(gameTime); // Update children (buttons, text fields).

            if (MobileUi.IsMobile)
            {
                UpdateMobileTouch();
            }

            HandleKeyboardInput();
            UpdateVisualStates(); // Keep visual state consistent.
        }

        private void HandleKeyboardInput()
        {
            if (!Visible) return;

            var keyboard = MuGame.Instance.Keyboard;
            var prevKeyboard = MuGame.Instance.PrevKeyboard;

            // Handle Enter key FIRST (with suppression of the first Enter after Show) ---
            // This allows Enter to close an empty chat even if the container has focus.
            if (keyboard.IsKeyDown(Keys.Enter) && prevKeyboard.IsKeyUp(Keys.Enter))
            {
                if (_suppressNextEnter)
                {
                    _suppressNextEnter = false;
                }
                else
                {
                    ProcessEnterKey();
                    // After Enter, usually focus is lost or window closes, so further key processing might not be needed.
                    // However, if ProcessEnterKey decided not to close, other keys might still be relevant.
                    // For safety, we can return here if Enter was the action.
                    return;
                }
            }

            // --- Handle Escape key to hide the chat box ---
            if (keyboard.IsKeyDown(Keys.Escape) && prevKeyboard.IsKeyUp(Keys.Escape))
            {
                Hide();
                return;
            }

            // proceed with other inputs only if input fields have focus.
            bool chatFocus = Scene.FocusControl == _chatInput;
            bool whisperFocus = Scene.FocusControl == _whisperIdInput && _whisperIdInput.Visible;

            if (!chatFocus && !whisperFocus) // only proceed if one of the text fields has focus
                return; // neither text field has focus, don't process Tab, Up, Down, F-keys related to chat functionality

            // --- Tab key to switch focus between input fields ---
            if (keyboard.IsKeyDown(Keys.Tab) && prevKeyboard.IsKeyUp(Keys.Tab))
            {
                if (_isWhisperSendMode)
                {
                    if (chatFocus)
                    {
                        _chatInput.Blur();
                        _whisperIdInput.Focus();
                        Scene.FocusControl = _whisperIdInput;
                        _whisperIdInput.MoveCursorToEnd();
                    }
                    else if (whisperFocus)
                    {
                        _whisperIdInput.Blur();
                        _chatInput.Focus();
                        Scene.FocusControl = _chatInput;
                        _chatInput.MoveCursorToEnd();
                    }
                }
            }
            // --- Navigate message history ---
            else if (keyboard.IsKeyDown(Keys.Up) && prevKeyboard.IsKeyUp(Keys.Up))
            {
                NavigateHistory(-1);
            }
            else if (keyboard.IsKeyDown(Keys.Down) && prevKeyboard.IsKeyUp(Keys.Down))
            {
                NavigateHistory(1);
            }
            // --- Toggle whisper mode ---
            else if (keyboard.IsKeyDown(Keys.F3) && prevKeyboard.IsKeyUp(Keys.F3))
            {
                ToggleWhisperSendMode();
                SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav");
            }
            // --- Cycle chat size ---
            else if (keyboard.IsKeyDown(Keys.F4) && prevKeyboard.IsKeyUp(Keys.F4))
            {
                if (_chatLogWindowRef.IsFrameVisible)
                {
                    _chatLogWindowRef.CycleSize();
                    SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav");
                }
            }
            // --- Toggle chat frame ---
            else if (keyboard.IsKeyDown(Keys.F5) && prevKeyboard.IsKeyUp(Keys.F5))
            {
                _chatLogWindowRef.ToggleFrame();
                SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav");
            }
        }

        private void ProcessEnterKey()
        {
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (currentTime - _lastChatTime < ChatCooldownMs)
            {
                // Optional: Show a message "Please wait before sending another message."
                // For now, just prevent sending and potentially re-show the input if hidden.
                if (!Visible)
                {
                    Show();
                }
                _logger.LogDebug("Chat cooldown active, message blocked.");
                return;
            }

            string messageText = _chatInput.Value.Trim();
            string whisperTarget = _whisperIdInput.Value.Trim();

            // If both inputs are empty, just hide/show the box.
            if (string.IsNullOrEmpty(messageText) && (string.IsNullOrEmpty(whisperTarget) || !_isWhisperSendMode))
            {
                if (!Visible)
                {
                    Show();
                }
                else
                {
                    Hide();
                    if (Scene != null) Scene.ConsumeKeyboardEnter();
                }
                return;
            }

            // --- Remove direct NetworkManager usage, use event instead ---
            if (_isWhisperSendMode && !string.IsNullOrEmpty(whisperTarget))
            {
                if (string.IsNullOrEmpty(messageText))
                {
                    Hide();
                    if (Scene != null) Scene.ConsumeKeyboardEnter();
                    return;
                }
                finalType = MessageType.Whisper;
                AddWhisperIdHistory(whisperTarget);
                MessageSendRequested?.Invoke(this, new ChatMessageEventArgs(messageText, finalType, whisperTarget));
            }
            else
            {
                if (string.IsNullOrEmpty(messageText))
                {
                    Hide();
                    if (Scene != null) Scene.ConsumeKeyboardEnter();
                    return;
                }
                string messageToSend = messageText;
                // Check for explicit prefixes FIRST.
                if (messageText.StartsWith("~"))
                {
                    finalType = MessageType.Chat;
                    messageToSend = messageText.Substring(1);
                }
                else if (messageText.StartsWith("@"))
                {
                    finalType = MessageType.Guild;
                    messageToSend = messageText.Substring(1);
                }
                else if (messageText.StartsWith("$"))
                {
                    finalType = MessageType.Gens;
                    messageToSend = messageText.Substring(1);
                }
                else
                {
                    finalType = (MessageType)_currentInputType;
                    if (finalType == MessageType.Party) messageToSend = "~" + messageText;
                    else if (finalType == MessageType.Guild) messageToSend = "@" + messageText;
                    else if (finalType == MessageType.Gens) messageToSend = "$" + messageText;
                }
                MessageSendRequested?.Invoke(this, new ChatMessageEventArgs(messageToSend, finalType));
            }

            // Add to chat history (only the message text, not the prefix).
            AddChatHistory(messageText); // Add original text without prefix.

            // Clear input and hide AFTER ensuring the send task is initiated.
            _chatInput.Value = "";
            Hide();
            if (Scene != null) Scene.ConsumeKeyboardEnter();

            // Update cooldown timer AFTER successful send attempt initiation.
            _lastChatTime = currentTime;

            // Optional: Await the send task if you need confirmation, but usually UI shouldn't block.
        }

        private void NavigateHistory(int direction)
        {
            bool chatFocus = Scene.FocusControl == _chatInput;
            bool whisperFocus = Scene.FocusControl == _whisperIdInput && _whisperIdInput.Visible;

            if (chatFocus && _chatHistory.Count > 0)
            {
                _currentChatHistoryIndex = Math.Clamp(_currentChatHistoryIndex + direction, 0, _chatHistory.Count);
                _chatInput.Value = (_currentChatHistoryIndex < _chatHistory.Count) ? _chatHistory[_currentChatHistoryIndex] : "";
                _chatInput.MoveCursorToEnd();
            }
            else if (whisperFocus && _whisperIdHistory.Count > 0)
            {
                _currentWhisperHistoryIndex = Math.Clamp(_currentWhisperHistoryIndex + direction, 0, _whisperIdHistory.Count);
                _whisperIdInput.Value = (_currentWhisperHistoryIndex < _whisperIdHistory.Count) ? _whisperIdHistory[_currentWhisperHistoryIndex] : "";
                _whisperIdInput.MoveCursorToEnd();
            }
        }

        private void AddChatHistory(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _chatHistory.Remove(text); // Remove duplicates.
            _chatHistory.Add(text);
            if (_chatHistory.Count > MAX_CHAT_HISTORY)
            {
                _chatHistory.RemoveAt(0);
            }
            _currentChatHistoryIndex = _chatHistory.Count; // Reset index to bottom.
        }

        private void AddWhisperIdHistory(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            _whisperIdHistory.Remove(id); // Remove duplicates.
            _whisperIdHistory.Add(id);
            if (_whisperIdHistory.Count > MAX_WHISPER_HISTORY)
            {
                _whisperIdHistory.RemoveAt(0);
            }
            _currentWhisperHistoryIndex = _whisperIdHistory.Count; // Reset index to bottom.
        }

        private void SetInputType(InputMessageType type)
        {
            if (_currentInputType != type)
            {
                _currentInputType = type;
                UpdateVisualStates();
            }
        }

        private void ToggleWhisperLock()
        {
            _isWhisperLocked = !_isWhisperLocked;
            UpdateVisualStates();
        }

        private void ToggleWhisperSendMode()
        {
            _isWhisperSendMode = !_isWhisperSendMode;
            _whisperIdInput.Visible = _isWhisperSendMode;

            if (_isWhisperSendMode && Visible)
            {
                _chatInput.Blur();
                _whisperIdInput.Focus();
                Scene.FocusControl = _whisperIdInput;
                _whisperIdInput.MoveCursorToEnd();
            }
            else if (!_isWhisperSendMode && Visible)
            {
                _whisperIdInput.Blur();
                _chatInput.Focus();
                Scene.FocusControl = _chatInput;
                _chatInput.MoveCursorToEnd();
            }
        }

        private void ToggleSystemMessages()
        {
            // This state is managed by ChatLogWindow, but we keep the button visual update.
            bool newState = !_chatLogWindowRef.IsSysMsgVisible;
            _chatLogWindowRef.ShowSystemMessages(newState);
            UpdateVisualStates();
        }

        private void ToggleChatLogVisibility()
        {
            // This state is managed by ChatLogWindow.
            bool newState = !_chatLogWindowRef.IsChatLogVisible;
            _chatLogWindowRef.ShowChatLogMessages(newState);
            UpdateVisualStates();
        }

        private void UpdateVisualStates()
        {
            if (!Visible) return;

            for (int i = 0; i < _typeButtons.Length; i++)
                _typeButtons[i].Visible = true;

            _whisperToggleButton.Visible = true;
            _systemToggleButton.Visible = true;
            _chatLogToggleButton.Visible = true;
            _frameToggleButton.Visible = true;

            bool showFrameButtons = _chatLogWindowRef.IsFrameVisible;
            _sizeButton.Visible = showFrameButtons;
            _transparencyButton.Visible = showFrameButtons;

            _whisperIdInput.Visible = _isWhisperSendMode;
        }

        // Helper to get all buttons for visibility toggling.
        private IEnumerable<SpriteControl> GetAllButtons()
        {
            foreach (var btn in _typeButtons) yield return btn;
            yield return _whisperToggleButton;
            yield return _systemToggleButton;
            yield return _chatLogToggleButton;
            yield return _frameToggleButton;
            yield return _sizeButton;
            yield return _transparencyButton;
        }

        private static void DrawDisabledOverlay(SpriteControl btn)
        {
            var sb = GraphicsManager.Instance.Sprite;
            sb.Draw(GraphicsManager.Instance.Pixel, btn.DisplayRectangle, Color.Black * 0.55f);
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible)
                return;

            var sb = GraphicsManager.Instance.Sprite;

            // 手機沒有 _background 這個子控制項，底自己畫。
            // 必須畫在 base.Draw 之前 —— 子控制項（輸入框、按鈕）要蓋在底上面。
            if (MobileUi.IsMobile && sb != null)
            {
                // 面板只蓋輸入列那一段；上面那排開關各自有底，之間留空。
                var full = DisplayRectangle;
                int barTop = MobileToggleHeight + MobileToggleGap;
                MobileUi.DrawPanel(sb, new Rectangle(
                    full.X, full.Y + barTop, full.Width, full.Height - barTop));
            }

            if (MobileUi.IsMobile && sb != null)
            {
                // 先畫輸入欄與兩顆鈕的底，再讓 base.Draw 把文字欄畫在上面
                DrawMobileBar(sb);
                base.Draw(gameTime);
                return;
            }

            base.Draw(gameTime);

            for (int i = 0; i < _typeButtons.Length; i++)
            {
                if (i != (int)_currentInputType)
                    DrawDisabledOverlay(_typeButtons[i]);
            }

            if (!_isWhisperLocked) DrawDisabledOverlay(_whisperToggleButton);
            if (!_chatLogWindowRef.IsSysMsgVisible) DrawDisabledOverlay(_systemToggleButton);
            if (!_chatLogWindowRef.IsChatLogVisible) DrawDisabledOverlay(_chatLogToggleButton);
            if (!_chatLogWindowRef.IsFrameVisible) DrawDisabledOverlay(_frameToggleButton);

            if (!_isWhisperSendMode && _whisperIdInput != null)
            {
                sb.Draw(
                    GraphicsManager.Instance.Pixel,
                    _whisperIdInput.DisplayRectangle,
                    Color.Black * 0.5f);
            }
        }

        public override bool OnClick()
        {
            // If the main chat input box area is clicked (not a button within it),
            // and it's visible, set focus to the chat input field.
            if (Visible && _chatInput != null && _chatInput.Visible)
            {
                _chatInput.Focus(); // this will also set Scene.FocusControl
            }
            return base.OnClick(); // allow base to fire Click event if any subscribers
        }
    }
}
