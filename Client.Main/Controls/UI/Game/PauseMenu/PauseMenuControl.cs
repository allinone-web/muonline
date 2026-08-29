using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Client.Main.Controls.UI.Common;
using System.Threading.Tasks;
using Client.Main.Scenes;
using Client.Main.Networking;
using Client.Main.Networking.Services;
using Microsoft.Extensions.Logging;
using Client.Main.Core.Client; // ClientConnectionState
using Client.Main;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using MUnique.OpenMU.Network.Packets; // LogOutType
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.PauseMenu
{
    public class PauseMenuControl : UIControl
    {
        private readonly ILogger _logger = MuGame.AppLoggerFactory?.CreateLogger<PauseMenuControl>();
        private EventHandler<System.Collections.Generic.List<(string Name, MUnique.OpenMU.Network.Packets.CharacterClassNumber Class, ushort Level, byte[] Appearance)>> _characterListHandler;
        private EventHandler<LogOutType> _logoutResponseHandler;
        private class PausePanelControl : UIControl
        {
            public int HeaderHeight { get; set; } = 96;
            public int ContentTop { get; set; } = 0;
            public bool DrawContentSurface { get; set; }

            public PausePanelControl()
            {
                BackgroundColor = Color.Transparent;
                BorderColor = Color.Transparent;
                BorderThickness = 0;
            }

            public override void Draw(GameTime gameTime)
            {
                if (Status != GameControlStatus.Ready || !Visible)
                    return;

                var sprite = GraphicsManager.Instance.Sprite;
                var pixel = GraphicsManager.Instance.Pixel;
                var rect = DisplayRectangle;
                if (pixel == null)
                {
                    base.Draw(gameTime);
                    return;
                }

                sprite.Draw(pixel, new Rectangle(rect.X + 9, rect.Y + 12, rect.Width, rect.Height), new Color(0, 0, 0, 105));
                sprite.Draw(pixel, new Rectangle(rect.X + 4, rect.Y + 6, rect.Width, rect.Height), new Color(0, 0, 0, 70));

                UiDrawHelper.DrawVerticalGradient(
                    sprite,
                    rect,
                    new Color(29, 35, 46, 250),
                    new Color(8, 11, 17, 252),
                    20);

                var headerRect = new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, Math.Min(HeaderHeight, rect.Height - 2));
                UiDrawHelper.DrawVerticalGradient(
                    sprite,
                    headerRect,
                    new Color(50, 59, 73, 238),
                    new Color(24, 30, 40, 222),
                    12);

                UiDrawHelper.DrawHorizontalGradient(
                    sprite,
                    new Rectangle(rect.X + 24, rect.Y + HeaderHeight - 2, rect.Width - 48, 2),
                    Color.Transparent,
                    ModernHudTheme.AccentBright,
                    16);
                UiDrawHelper.DrawHorizontalGradient(
                    sprite,
                    new Rectangle(rect.Center.X, rect.Y + HeaderHeight - 2, Math.Max(1, rect.Right - 24 - rect.Center.X), 2),
                    ModernHudTheme.AccentBright,
                    Color.Transparent,
                    16);

                if (DrawContentSurface && ContentTop > 0 && ContentTop < rect.Height - 30)
                {
                    var contentRect = new Rectangle(
                        rect.X + 18,
                        rect.Y + ContentTop,
                        rect.Width - 36,
                        rect.Height - ContentTop - 18);
                    sprite.Draw(pixel, contentRect, new Color(5, 8, 13, 118));
                    UiDrawHelper.DrawBorder(sprite, contentRect, new Color(91, 104, 124, 72));
                }

                UiDrawHelper.DrawBorder(sprite, rect, ModernHudTheme.BorderOuter, 2);
                UiDrawHelper.DrawBorder(sprite, new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4), ModernHudTheme.BorderInner);
                UiDrawHelper.DrawCornerAccents(sprite, rect, ModernHudTheme.Accent, 18, 2);

                base.Draw(gameTime);
            }
        }

        private sealed class PauseMenuButtonControl : ButtonControl
        {
            public string Subtitle { get; set; }
            public Color AccentColor { get; set; } = ModernHudTheme.Accent;
            public bool IsDanger { get; set; }
            public bool Compact { get; set; }

            public PauseMenuButtonControl()
            {
                BackgroundColor = Color.Transparent;
                HoverBackgroundColor = Color.Transparent;
                PressedBackgroundColor = Color.Transparent;
                TextColor = ModernHudTheme.TextWhite;
                HoverTextColor = ModernHudTheme.TextWhite;
            }

            /// <summary>
            /// 手機版：一塊底 + 標題 + 副標。沒有投影、沒有漸層、沒有左側色條、沒有外框。
            ///
            /// 桌面那一套（陰影 + 12 段漸層 + 懸停色塊 + 5px 強調條）疊在六顆直排的
            /// 按鈕上，就是六層互相干擾的裝飾。每顆按鈕自己的顏色也一併取消 ——
            /// 六種顏色沒有傳達任何資訊，只是六種顏色。危險項（離開遊戲）例外，
            /// 那個紅色是真的在講「這一顆不一樣」。
            /// </summary>
            private void DrawMobile(SpriteBatch sprite, Texture2D pixel, SpriteFont font, Rectangle rect)
            {
                float fill = !Enabled ? 0.3f : (IsMousePressed ? 1.0f : (IsMouseOver ? 0.8f : 0.55f));
                sprite.Draw(pixel, rect, MobileUi.TitleBarFill * fill);

                if (IsDanger && Enabled)
                    sprite.Draw(pixel, new Rectangle(rect.X, rect.Y, 3, rect.Height), ModernHudTheme.Danger * 0.85f);

                string title = Text ?? string.Empty;
                float titleScale = 15f / Constants.BASE_FONT_SIZE;
                var titleSize = font.MeasureString(title) * titleScale;

                bool hasSubtitle = !string.IsNullOrEmpty(Subtitle) && rect.Height >= 52;
                float titleY = hasSubtitle
                    ? rect.Y + rect.Height * 0.30f - titleSize.Y * 0.5f
                    : rect.Y + (rect.Height - titleSize.Y) * 0.5f;

                var titlePos = new Vector2(rect.X + 18, titleY);
                sprite.DrawString(font, title, titlePos + Vector2.One, Color.Black * 0.6f,
                                  0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);
                sprite.DrawString(font, title, titlePos,
                                  (Enabled ? MobileUi.TextPrimary : MobileUi.TextDim) * Alpha,
                                  0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);

                if (!hasSubtitle)
                    return;

                float subScale = 10.5f / Constants.BASE_FONT_SIZE;
                var subPos = new Vector2(rect.X + 18, rect.Y + rect.Height * 0.62f);
                sprite.DrawString(font, Subtitle, subPos, MobileUi.TextDim * (0.85f * Alpha),
                                  0f, Vector2.Zero, subScale, SpriteEffects.None, 0f);
            }
            public override void Draw(GameTime gameTime)
            {
                if (Status != GameControlStatus.Ready || !Visible)
                    return;

                var sprite = GraphicsManager.Instance.Sprite;
                var pixel = GraphicsManager.Instance.Pixel;
                var font = GraphicsManager.Instance.Font;
                if (pixel == null || font == null)
                    return;

                if (MobileUi.IsMobile)
                {
                    DrawMobile(sprite, pixel, font, DisplayRectangle);
                    return;
                }
                var rect = DisplayRectangle;
                Color accent = IsDanger ? ModernHudTheme.Danger : AccentColor;
                Color top;
                Color bottom;

                if (!Enabled)
                {
                    top = new Color(25, 29, 36, 205);
                    bottom = new Color(13, 16, 21, 215);
                    accent = ModernHudTheme.TextDark;
                }
                else if (IsMousePressed)
                {
                    top = new Color(20, 25, 33, 252);
                    bottom = new Color(8, 11, 16, 252);
                }
                else if (IsMouseOver)
                {
                    top = IsDanger ? new Color(83, 38, 41, 248) : new Color(52, 61, 76, 248);
                    bottom = IsDanger ? new Color(42, 20, 24, 250) : new Color(20, 27, 37, 250);
                }
                else
                {
                    top = new Color(37, 44, 56, 238);
                    bottom = new Color(16, 21, 29, 244);
                }

                sprite.Draw(pixel, new Rectangle(rect.X + 3, rect.Y + 4, rect.Width, rect.Height), new Color(0, 0, 0, 76));
                UiDrawHelper.DrawVerticalGradient(sprite, rect, top, bottom, 12);

                if (IsMouseOver && Enabled)
                {
                    sprite.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, rect.Height), new Color(accent.R, accent.G, accent.B, (byte)22));
                    sprite.Draw(pixel, new Rectangle(rect.X, rect.Y, 5, rect.Height), accent);
                }
                else
                {
                    sprite.Draw(pixel, new Rectangle(rect.X, rect.Y, 3, rect.Height), new Color(accent.R, accent.G, accent.B, (byte)(Enabled ? 185 : 80)));
                }

                UiDrawHelper.DrawBorder(sprite, rect, IsMouseOver && Enabled ? new Color(accent.R, accent.G, accent.B, (byte)180) : new Color(91, 104, 124, 115));
                sprite.Draw(pixel, new Rectangle(rect.X + 10, rect.Bottom - 1, rect.Width - 20, 1), new Color(255, 255, 255, 18));

                float titleScale = (Compact ? 11.5f : 14f) / Constants.BASE_FONT_SIZE;
                float subtitleScale = 9.5f / Constants.BASE_FONT_SIZE;
                Color titleColor = Enabled ? ModernHudTheme.TextWhite : ModernHudTheme.TextDark;
                Color subtitleColor = Enabled ? ModernHudTheme.TextGray : ModernHudTheme.TextDark;

                if (Compact)
                {
                    Vector2 titleSize = font.MeasureString(Text ?? string.Empty) * titleScale;
                    var titlePos = new Vector2(
                        rect.X + (rect.Width - titleSize.X) * 0.5f,
                        rect.Y + (rect.Height - titleSize.Y) * 0.5f);
                    sprite.DrawString(font, Text ?? string.Empty, titlePos + Vector2.One, Color.Black * 0.7f, 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);
                    sprite.DrawString(font, Text ?? string.Empty, titlePos, titleColor * Alpha, 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);
                    return;
                }

                var titlePosition = new Vector2(rect.X + 18, rect.Y + 9);
                sprite.DrawString(font, Text ?? string.Empty, titlePosition + Vector2.One, Color.Black * 0.75f, 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);
                sprite.DrawString(font, Text ?? string.Empty, titlePosition, titleColor * Alpha, 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);

                if (!string.IsNullOrWhiteSpace(Subtitle))
                {
                    sprite.DrawString(font, Subtitle, new Vector2(rect.X + 18, rect.Y + 31), subtitleColor * Alpha, 0f, Vector2.Zero, subtitleScale, SpriteEffects.None, 0f);
                }

                string arrow = ">";
                float arrowScale = 13f / Constants.BASE_FONT_SIZE;
                Vector2 arrowSize = font.MeasureString(arrow) * arrowScale;
                Vector2 arrowPosition = new(rect.Right - 20 - arrowSize.X, rect.Y + (rect.Height - arrowSize.Y) * 0.5f);
                sprite.DrawString(font, arrow, arrowPosition, new Color(accent.R, accent.G, accent.B, (byte)(IsMouseOver ? 255 : 150)) * Alpha, 0f, Vector2.Zero, arrowScale, SpriteEffects.None, 0f);
            }
        }

        private sealed class MenuTabButtonControl : ButtonControl
        {
            public bool Active { get; set; }

            /// <summary>動作列（Continue / Exit …），不是設定分類。不顯示選中狀態。</summary>
            public bool IsAction { get; set; }

            /// <summary>不可逆的動作（離開遊戲）。左側一條紅槓，只有這裡用飽和色。</summary>
            public bool IsDanger { get; set; }

            public MenuTabButtonControl()
            {
                BackgroundColor = Color.Transparent;
                HoverBackgroundColor = Color.Transparent;
                PressedBackgroundColor = Color.Transparent;
            }

            public override void Draw(GameTime gameTime)
            {
                if (Status != GameControlStatus.Ready || !Visible)
                    return;

                var sprite = GraphicsManager.Instance.Sprite;
                var pixel = GraphicsManager.Instance.Pixel;
                var font = GraphicsManager.Instance.Font;
                if (pixel == null || font == null)
                    return;

                var rect = DisplayRectangle;

                if (MobileUi.IsMobile)
                {
                    // 分類清單是一列九個項目。每一個都畫外框的話，畫面左側就是九個
                    // 疊在一起的方框 —— 框線的數量比文字還多。
                    //
                    // 清單本來就是靠位置和間距讀的，不需要框；只有<b>目前選中的</b>
                    // 那一項需要被指出來，用一塊底色加左側一條短槓就夠了。
                    if (Active && !IsAction)
                    {
                        sprite.Draw(pixel, rect, MobileUi.TitleBarFill * MobileUi.PanelAlpha);
                        sprite.Draw(pixel, new Rectangle(rect.X, rect.Y + 6, 3, rect.Height - 12), MobileUi.TextPrimary * 0.75f);
                    }
                    else if (IsMouseOver)
                    {
                        sprite.Draw(pixel, rect, MobileUi.TitleBarFill * 0.45f);
                    }
                    else if (IsAction)
                    {
                        // 動作列給一層很淡的底，和下面的設定分類分開
                        sprite.Draw(pixel, rect, MobileUi.TitleBarFill * 0.28f);
                    }

                    if (IsDanger)
                        sprite.Draw(pixel, new Rectangle(rect.X, rect.Y + 6, 3, rect.Height - 12), ModernHudTheme.Danger * 0.85f);

                    float mobileScale = 12.5f / Constants.BASE_FONT_SIZE;
                    string mobileLabel = Text ?? string.Empty;
                    Vector2 mobileSize = font.MeasureString(mobileLabel) * mobileScale;

                    // 靠左對齊：清單項目置中的話，每一行的起點都不一樣，
                    // 眼睛要重新找一次左緣才讀得下去。
                    var mobilePosition = new Vector2(rect.X + 16, rect.Y + (rect.Height - mobileSize.Y) * 0.5f);
                    sprite.DrawString(font, mobileLabel, mobilePosition,
                        ((Active || IsAction) ? MobileUi.TextPrimary : MobileUi.TextDim) * Alpha,
                        0f, Vector2.Zero, mobileScale, SpriteEffects.None, 0f);
                    return;
                }

                Color fill = Active
                    ? new Color(64, 55, 34, 225)
                    : IsMouseOver
                        ? new Color(46, 55, 69, 225)
                        : new Color(20, 26, 35, 210);
                sprite.Draw(pixel, rect, fill);
                UiDrawHelper.DrawBorder(sprite, rect, Active ? new Color(ModernHudTheme.Accent.R, ModernHudTheme.Accent.G, ModernHudTheme.Accent.B, (byte)190) : new Color(91, 104, 124, 95));

                if (Active)
                    sprite.Draw(pixel, new Rectangle(rect.X + 8, rect.Bottom - 2, rect.Width - 16, 2), ModernHudTheme.AccentBright);

                float scale = 10.5f / Constants.BASE_FONT_SIZE;
                string label = Text ?? string.Empty;
                Vector2 size = font.MeasureString(label) * scale;
                Vector2 position = new(rect.X + (rect.Width - size.X) * 0.5f, rect.Y + (rect.Height - size.Y) * 0.5f);
                Color color = Active ? ModernHudTheme.TextGold : ModernHudTheme.TextGray;
                sprite.DrawString(font, label, position, color * Alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
        }

        private PausePanelControl _panel;
        private LabelControl _titleLabel;
        private LabelControl _subtitleLabel;
        private LabelControl _footerLabel;
        private ButtonControl _btnParty;
        private ButtonControl _btnCharacterSelect;
        private ButtonControl _btnServerSelect;
        private ButtonControl _btnOptions;
        private ButtonControl _btnExit;
        private ButtonControl _btnResume;
        private bool _returnInProgress;
        private bool _exitInProgress;
        private OptionsPanelControl _optionsPanel;

        public event EventHandler ResumeClicked;
        public event EventHandler CharacterSelectClicked;
        public event EventHandler ServerSelectClicked;
        public event EventHandler OptionsClicked;
        public event EventHandler ExitClicked;

        public PauseMenuControl()
        {
            Visible = false;
            Interactive = true;
            AutoViewSize = false;
            ViewSize = new Point(UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y);
            ControlSize = ViewSize;
            BackgroundColor = Color.Transparent;

            // 手機的底部 HUD 只有六個位置，PARTY 讓給了 SKILL（技能面板原本沒有入口），
            // 組隊改掛在這裡。桌面的底部面板還有 PARTY 鈕，不必重複。
            bool showParty = MobileUi.IsMobile;
            int buttonCount = showParty ? 6 : 5;

            // 版面：第一顆按鈕在 y=111，每顆 56 高、間距 10，底部留 69。
            const int MenuButtonHeight = 56;
            const int MenuButtonSpacing = 10;
            const int MenuFirstButtonY = 111;
            const int MenuBottomPadding = 69;
            int panelHeight = MenuFirstButtonY
                + buttonCount * MenuButtonHeight
                + (buttonCount - 1) * MenuButtonSpacing
                + MenuBottomPadding;

            _panel = new PausePanelControl
            {
                AutoViewSize = false,
                ControlSize = new Point(430, panelHeight),
                ViewSize = new Point(430, panelHeight),
                Align = Models.ControlAlign.HorizontalCenter | Models.ControlAlign.VerticalCenter,
                HeaderHeight = 98,
                Interactive = true
            };
            Controls.Add(_panel);

            _titleLabel = new LabelControl
            {
                Text = "PAUSE MENU",
                FontSize = 23f,
                TextColor = ModernHudTheme.TextGold,
                IsBold = true,
                X = 0,
                Y = 22,
                Align = Models.ControlAlign.HorizontalCenter
            };
            _panel.Controls.Add(_titleLabel);

            _subtitleLabel = new LabelControl
            {
                Text = "Take a breath. Your adventure is waiting.",
                FontSize = 10.5f,
                TextColor = ModernHudTheme.TextGray,
                HasShadow = false,
                X = 0,
                Y = 58,
                Align = Models.ControlAlign.HorizontalCenter
            };
            _panel.Controls.Add(_subtitleLabel);

            int btnWidth = 342;
            int btnHeight = MenuButtonHeight;
            int x = (_panel.ViewSize.X - btnWidth) / 2;
            int y = MenuFirstButtonY;
            int spacing = MenuButtonSpacing;

            _btnResume = CreateButton("Continue", "Return to the game", x, y, btnWidth, btnHeight, ModernHudTheme.AccentBright);
            _btnResume.Click += (s, e) =>
            {
                ResumeClicked?.Invoke(this, EventArgs.Empty);
                Visible = false;
                _panel.Visible = true;
                if (_optionsPanel != null)
                    _optionsPanel.Visible = false;
            };
            _panel.Controls.Add(_btnResume);
            y += btnHeight + spacing;

            if (showParty)
            {
                _btnParty = CreateButton("Party", "Invite, kick and view your party", x, y, btnWidth, btnHeight, ModernHudTheme.Accent);
                _btnParty.Click += (s, e) =>
                {
                    TogglePartyPanel();

                    // 開了組隊面板就把暫停選單收起來 —— 兩個都開著會互相遮擋
                    ResumeClicked?.Invoke(this, EventArgs.Empty);
                    Visible = false;
                    _panel.Visible = true;
                    if (_optionsPanel != null)
                        _optionsPanel.Visible = false;
                };
                _panel.Controls.Add(_btnParty);
                y += btnHeight + spacing;
            }

            _btnCharacterSelect = CreateButton("Character Select", "Leave the world and choose another hero", x, y, btnWidth, btnHeight, ModernHudTheme.SecondaryBright);
            _btnCharacterSelect.Click += async (s, e) =>
            {
                if (_returnInProgress) return;
                _returnInProgress = true;
                try
                {
                    CharacterSelectClicked?.Invoke(this, EventArgs.Empty);
                    await HandleReturnToCharacterSelectAsync();
                }
                finally
                {
                    _returnInProgress = false;
                }
            };
            _panel.Controls.Add(_btnCharacterSelect);
            y += btnHeight + spacing;

            _btnServerSelect = CreateButton("Server Select", "Disconnect and return to the server list", x, y, btnWidth, btnHeight, ModernHudTheme.Secondary);
            _btnServerSelect.Click += async (s, e) =>
            {
                ServerSelectClicked?.Invoke(this, EventArgs.Empty);
                await HandleReturnToServerSelectAsync();
            };
            _panel.Controls.Add(_btnServerSelect);
            y += btnHeight + spacing;

            _btnOptions = CreateButton("Settings", "Graphics, audio and performance options", x, y, btnWidth, btnHeight, new Color(150, 118, 210));
            _btnOptions.Click += (s, e) =>
            {
                OptionsClicked?.Invoke(this, EventArgs.Empty);
                ToggleOptionsPanel();
            };
            _panel.Controls.Add(_btnOptions);
            y += btnHeight + spacing;

            _btnExit = CreateButton("Exit Game", "Close the client", x, y, btnWidth, btnHeight, ModernHudTheme.Danger, isDanger: true);
            _btnExit.Click += async (s, e) =>
            {
                if (_exitInProgress) return;
                _exitInProgress = true;
                try
                {
                    ExitClicked?.Invoke(this, EventArgs.Empty);
                    await HandleExitAsync();
                }
                finally
                {
                    _exitInProgress = false;
                }
            };
            _panel.Controls.Add(_btnExit);

            _footerLabel = new LabelControl
            {
                Text = "ESC  ·  close menu",
                FontSize = 9.5f,
                TextColor = ModernHudTheme.TextDark,
                HasShadow = false,
                X = 0,
                Y = 468,
                Align = Models.ControlAlign.HorizontalCenter
            };
            _panel.Controls.Add(_footerLabel);
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            // 手機不畫全螢幕遮罩。近乎全黑的一層把遊戲整個蓋掉，
            // 在手機上顯得笨重 —— 面板本身已經是不透明的，看得出焦點在哪。
            if (!MobileUi.IsMobile)
            {
                var sprite = GraphicsManager.Instance.Sprite;
                var rect = DisplayRectangle;
                UiDrawHelper.DrawVerticalGradient(sprite, rect, new Color(6, 8, 13, 205), new Color(0, 0, 0, 238), 20);
            }

            base.Draw(gameTime);
        }

        private static ButtonControl CreateButton(string text, string subtitle, int x, int y, int width, int height, Color accent, bool isDanger = false)
        {
            return new PauseMenuButtonControl
            {
                Text = text,
                Subtitle = subtitle,
                AccentColor = accent,
                IsDanger = isDanger,
                X = x,
                Y = y,
                ControlSize = new Point(width, height),
                ViewSize = new Point(width, height),
                AutoViewSize = false,
                FontSize = 14f,
                TextColor = ModernHudTheme.TextWhite
            };
        }

        /// <summary>
        /// 切換組隊面板。與 ModernBottomHud 的 PARTY 鈕走同一條路（在場景的控制項裡找它）。
        /// </summary>
        // ── 手機的合併選單直接呼叫這幾個 ──
        //
        // 桌面的六顆按鈕各自帶著一段 Click 內容（防重入旗標、事件通知、等待非同步）。
        // 那些邏輯不該複製一份到左欄的動作列去，所以抽成方法，兩邊共用。

        internal void TogglePartyPanelFromMenu()
        {
            TogglePartyPanel();
            Visible = false;
        }

        internal async Task LeaveToCharacterSelectAsync()
        {
            if (_returnInProgress) return;
            _returnInProgress = true;
            try
            {
                CharacterSelectClicked?.Invoke(this, EventArgs.Empty);
                await HandleReturnToCharacterSelectAsync();
            }
            finally
            {
                _returnInProgress = false;
            }
        }

        internal async Task LeaveToServerSelectAsync()
        {
            ServerSelectClicked?.Invoke(this, EventArgs.Empty);
            await HandleReturnToServerSelectAsync();
        }

        internal async Task ExitGameAsync()
        {
            if (_exitInProgress) return;
            _exitInProgress = true;
            try
            {
                ExitClicked?.Invoke(this, EventArgs.Empty);
                await HandleExitAsync();
            }
            finally
            {
                _exitInProgress = false;
            }
        }

        private void TogglePartyPanel()
        {
            if (MuGame.Instance?.ActiveScene is not GameScene gs)
                return;

            var controls = gs.Controls.GetSnapshotArray();
            for (int i = 0; i < controls.Length; i++)
            {
                if (controls[i] is Party.PartyPanelControl party)
                {
                    party.Visible = !party.Visible;
                    if (party.Visible)
                        party.BringToFront();
                    return;
                }
            }
        }

        private void EnsureOptionsPanel()
        {
            if (_optionsPanel != null)
                return;

            _optionsPanel = new OptionsPanelControl(this)
            {
                Visible = false
            };
            Controls.Add(_optionsPanel);
            _optionsPanel.BringToFront();
        }

        private void ToggleOptionsPanel()
        {
            EnsureOptionsPanel();

            bool show = !_optionsPanel.Visible;
            _optionsPanel.Visible = show;
            _panel.Visible = !show;

            if (show)
            {
                _optionsPanel.Refresh();
                _optionsPanel.PlayOpenAnimation();
                _optionsPanel.BringToFront();
            }
        }

        // --- Internal handlers (network-aware) ---
        private async Task HandleReturnToCharacterSelectAsync()
        {
            try
            {
                Visible = false;
                if (_optionsPanel != null)
                {
                    _optionsPanel.Visible = false;
                }
                _panel.Visible = true;

                // Close NPC/Vault before switching
                try
                {
                    NpcShopControl.Instance.Visible = false;
                    VaultControl.Instance.Visible = false;
                    var svc = MuGame.Network?.GetCharacterService();
                    if (svc != null)
                        _ = svc.SendCloseNpcRequestAsync();
                    MuGame.Network?.GetCharacterState()?.ClearShopItems();
                }
                catch { }

                var net = MuGame.Network;
                if (net == null || !net.IsConnected)
                {
                    MuGame.Instance.ChangeScene(new LoginScene());
                    return;
                }

                UnsubscribeCharacterListHandler(net);
                UnsubscribeLogoutHandler(net);

                var characterListTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void CharacterListHandler(object sender, System.Collections.Generic.List<(string Name, MUnique.OpenMU.Network.Packets.CharacterClassNumber Class, ushort Level, byte[] Appearance)> list)
                {
                    try
                    {
                        var next = new SelectCharacterScene(list, net);
                        MuGame.Instance.ChangeScene(next);
                    }
                    finally
                    {
                        try { net.CharacterListReceived -= CharacterListHandler; } catch { }
                        _characterListHandler = null;
                        characterListTcs.TrySetResult(true);
                    }
                }
                _characterListHandler = CharacterListHandler;
                net.CharacterListReceived += _characterListHandler;

                var logoutTcs = new TaskCompletionSource<LogOutType>(TaskCreationOptions.RunContinuationsAsynchronously);
                void LogoutHandler(object sender, LogOutType type)
                {
                    logoutTcs.TrySetResult(type);
                }
                _logoutResponseHandler = LogoutHandler;
                net.LogoutResponseReceived += _logoutResponseHandler;

                _logger?.LogInformation("PauseMenu: Sending logout request (BackToCharacterSelection). Current state: {State}", net.CurrentState);
                await net.GetCharacterService().SendLogoutRequestAsync(LogOutType.BackToCharacterSelection);

                var logoutCompleted = await Task.WhenAny(logoutTcs.Task, Task.Delay(6000));
                if (logoutCompleted != logoutTcs.Task)
                {
                    _logger?.LogWarning("Logout response timed out. Staying in game.");
                    UnsubscribeLogoutHandler(net);
                    UnsubscribeCharacterListHandler(net);
                    Visible = true;
                    return;
                }

                var logoutResult = await logoutTcs.Task;
                UnsubscribeLogoutHandler(net);

                if (logoutResult != LogOutType.BackToCharacterSelection)
                {
                    _logger?.LogInformation("Logout returned type {Type}; aborting character selection flow.", logoutResult);
                    UnsubscribeCharacterListHandler(net);

                    if (logoutResult == LogOutType.BackToServerSelection)
                    {
                        MuGame.Instance.ChangeScene(new LoginScene());
                    }
                    else
                    {
                        Visible = true;
                    }
                    return;
                }

                // Wait for the refreshed character list which is requested after logout response.
                var listCompleted = await Task.WhenAny(characterListTcs.Task, Task.Delay(6000));
                if (listCompleted != characterListTcs.Task)
                {
                    UnsubscribeCharacterListHandler(net);
                    _logger?.LogWarning("Character list response timed out after logout. Staying in game.");

                    var cached = net.GetCachedCharacterList();
                    if (cached != null && cached.Count > 0)
                    {
                        try
                        {
                            _logger?.LogInformation("Using cached character list as fallback after timeout.");
                            MuGame.Instance.ChangeScene(new SelectCharacterScene(cached.ToList(), net));
                            return;
                        }
                        catch { /* if anything fails, reopen menu below */ }
                    }

                    Visible = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error while returning to character select");
                // Keep the current scene; allow user to retry instead of forcing LoginScene
                Visible = true;

            }
        }

        private async Task HandleReturnToServerSelectAsync()
        {
            try
            {
                Visible = false;
                if (_optionsPanel != null)
                {
                    _optionsPanel.Visible = false;
                }
                _panel.Visible = true;

                // Close NPC/Vault before switching
                try
                {
                    NpcShopControl.Instance.Visible = false;
                    VaultControl.Instance.Visible = false;
                    var svc = MuGame.Network?.GetCharacterService();
                    if (svc != null)
                        _ = svc.SendCloseNpcRequestAsync();
                    MuGame.Network?.GetCharacterState()?.ClearShopItems();
                }
                catch { }

                var net = MuGame.Network;
                if (net == null || !net.IsConnected)
                {
                    MuGame.Instance.ChangeScene(new LoginScene());
                    return;
                }

                UnsubscribeCharacterListHandler(net);
                UnsubscribeLogoutHandler(net);

                var logoutTcs = new TaskCompletionSource<LogOutType>(TaskCreationOptions.RunContinuationsAsynchronously);
                void LogoutHandler(object sender, LogOutType type)
                {
                    logoutTcs.TrySetResult(type);
                }
                _logoutResponseHandler = LogoutHandler;
                net.LogoutResponseReceived += _logoutResponseHandler;

                _logger?.LogInformation("PauseMenu: Sending logout request (BackToServerSelection). Current state: {State}", net.CurrentState);
                await net.GetCharacterService().SendLogoutRequestAsync(LogOutType.BackToServerSelection);

                var completed = await Task.WhenAny(logoutTcs.Task, Task.Delay(6000));
                if (completed != logoutTcs.Task)
                {
                    _logger?.LogWarning("Logout response timed out. Staying in game.");
                    UnsubscribeLogoutHandler(net);
                    Visible = true;
                    return;
                }

                var logoutResult = await logoutTcs.Task;
                UnsubscribeLogoutHandler(net);

                if (logoutResult != LogOutType.BackToServerSelection)
                {
                    _logger?.LogInformation("Logout returned type {Type}; keeping player in current scene.", logoutResult);
                    Visible = true;
                    return;
                }

                try
                {
                    _ = net.ConnectToConnectServerAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "PauseMenu: Failed to initiate connect server reconnect after logout.");
                }

                MuGame.Instance.ChangeScene(new LoginScene());
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error while returning to server select");
                MuGame.Instance.ChangeScene(new LoginScene());
            }
        }

        private async Task HandleExitAsync()
        {
            try
            {
                Visible = false;
                if (_optionsPanel != null)
                {
                    _optionsPanel.Visible = false;
                }
                _panel.Visible = true;

                var net = MuGame.Network;
                if (net != null && net.IsConnected)
                {
                    UnsubscribeLogoutHandler(net);

                    var logoutTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    void LogoutHandler(object sender, LogOutType type)
                    {
                        if (type == LogOutType.CloseGame)
                        {
                            logoutTcs.TrySetResult(true);
                        }
                    }

                    _logoutResponseHandler = LogoutHandler;
                    net.LogoutResponseReceived += _logoutResponseHandler;

                    _logger?.LogInformation("PauseMenu: Sending logout request (CloseGame). Current state: {State}", net.CurrentState);
                    try
                    {
                        await net.GetCharacterService().SendLogoutRequestAsync(LogOutType.CloseGame);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "PauseMenu: Logout request (CloseGame) failed, proceeding with local shutdown.");
                        logoutTcs.TrySetResult(false);
                    }

                    await Task.WhenAny(logoutTcs.Task, Task.Delay(3000));

                    UnsubscribeLogoutHandler(net);
                }

                MuGame.ScheduleOnMainThread(() =>
                {
#if !IOS
                    MuGame.Instance.Exit();
#endif
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "PauseMenu: Error while exiting the game. Forcing shutdown.");
#if !IOS
                MuGame.ScheduleOnMainThread(() => MuGame.Instance.Exit());
#endif
            }
        }

        private void ApplyBackgroundMusicSetting(bool enabled)
        {
            if (!enabled)
            {
                SoundController.Instance.StopBackgroundMusic();
                return;
            }

            var scene = MuGame.Instance?.ActiveScene as BaseScene;
            var music = scene?.World?.BackgroundMusicPath;
            if (!string.IsNullOrEmpty(music))
            {
                SoundController.Instance.PlayBackgroundMusic(music);
                SoundController.Instance.ApplyBackgroundMusicVolume();
            }
        }

        private void ApplyGraphicsSettings()
        {
            MuGame.ScheduleOnMainThread(() => MuGame.Instance?.ApplyGraphicsOptions());
        }

        private void ApplyQualityPreset(GraphicsQualityPreset preset, Action onComplete = null)
        {
            // Radio-button refreshes can invoke the selected option again. Reapplying the
            // same preset needlessly resets the graphics device and can present a black frame.
            if (GraphicsQualityManager.UserPreset == preset)
            {
                onComplete?.Invoke();
                return;
            }

            MuGame.ScheduleOnMainThread(() =>
            {
                var adapter = GraphicsManager.Instance?.GraphicsDevice?.Adapter ?? GraphicsAdapter.DefaultAdapter;
                GraphicsQualityManager.ApplyPreset(preset, adapter, _logger);

                // 畫質預設會直接寫 RENDER_SCALE（Low 0.75 / Medium 0.9 / High 1.0）。
                // 玩家若已經在 Render Scale 分頁明確選過值，那個選擇要蓋回來 ——
                // 否則兩組設定互相打架：選單上還顯示著玩家選的倍率，實際已被改掉。
                // 這與 MuGame 啟動時的順序一致（預設先套，玩家的個別設定後蓋）。
                if (MuGame.AppSettings?.Graphics?.RenderScale is float userScale && userScale > 0.05f)
                {
                    Constants.RENDER_SCALE = MathHelper.Clamp(userScale, 0.3f, 3.0f);
                }

                MuGame.Instance?.ApplyGraphicsOptions();
                GraphicsManager.Instance?.UpdateRenderScale();
                onComplete?.Invoke();
            });

            if (MuGame.AppSettings?.Graphics != null)
            {
                MuGame.AppSettings.Graphics.QualityPreset = preset.ToString();
            }
            MuGame.PersistGraphicsPreset(preset);
        }

        private void SetVSync(bool enabled)
        {
            Constants.DISABLE_VSYNC = !enabled;
            if (enabled)
                Constants.UNLIMITED_FPS = false;
            ApplyGraphicsSettings();
        }

        private void SetUnlimitedFps(bool enabled)
        {
            Constants.UNLIMITED_FPS = enabled;
            if (enabled)
                Constants.DISABLE_VSYNC = true;
            ApplyGraphicsSettings();
        }

        private void ApplyBackgroundMusicVolume()
        {
            if (!Constants.BACKGROUND_MUSIC)
            {
                return;
            }
            SoundController.Instance.ApplyBackgroundMusicVolume();
        }

        private void ApplySoundEffectsVolume()
        {
            SoundController.Instance.ApplySoundEffectsVolume();
        }

        private void ApplyDebugPanelSetting()
        {
            if (MuGame.Instance?.ActiveScene is BaseScene scene && scene.DebugPanel != null)
            {
                scene.DebugPanel.Visible = Constants.SHOW_DEBUG_PANEL;
                if (Constants.SHOW_DEBUG_PANEL)
                {
                    scene.DebugPanel.BringToFront();
                }
            }
        }

        public override void Update(GameTime time)
        {
            base.Update(time);

            if (!Visible)
            {
                _wasVisible = false;
                return;
            }

            if (MobileUi.IsMobile)
            {
                // 手機只有一層選單。MENU 直接開左右分欄的那個面板 ——
                // 動作（繼續／組隊／換角色／換伺服器／離開）已經排在它的左欄上半部，
                // 見 OptionsPanelControl 的 AddActionRow。
                //
                // 第一層那六顆按鈕的面板不再出現：多一層等於多一次點擊，
                // 而且多一個要關的視窗。
                if (!_wasVisible)
                {
                    _wasVisible = true;
                    EnsureOptionsPanel();
                    _panel.Visible = false;
                    _optionsPanel.Visible = true;
                    _optionsPanel.Refresh();
                    _optionsPanel.PlayOpenAnimation();
                    _optionsPanel.BringToFront();
                }

                return;
            }

            _wasVisible = true;

            if (_optionsPanel == null || !_optionsPanel.Visible)
            {
                if (_panel != null)
                {
                    _panel.Visible = true;
                }
            }
        }

        /// <summary>從隱藏變成顯示的那一幀要做初始化，記錄上一幀的狀態。</summary>
        private bool _wasVisible;

        public override void Dispose()
        {
            try
            {
                var net = MuGame.Network;
                UnsubscribeCharacterListHandler(net);
                UnsubscribeLogoutHandler(net);
            }
            finally
            {
                base.Dispose();
            }
        }

        private void UnsubscribeCharacterListHandler(NetworkManager net)
        {
            if (net != null && _characterListHandler != null)
            {
                try { net.CharacterListReceived -= _characterListHandler; } catch { }
            }
            _characterListHandler = null;
        }

        private void UnsubscribeLogoutHandler(NetworkManager net)
        {
            if (net != null && _logoutResponseHandler != null)
            {
                try { net.LogoutResponseReceived -= _logoutResponseHandler; } catch { }
            }
            _logoutResponseHandler = null;
        }

        private sealed class OptionsPanelControl : PausePanelControl
        {
            private readonly PauseMenuControl _owner;
            private readonly List<IOptionRow> _options = new();
            private readonly List<GameControl> _dynamicControls = new();
            // 桌面：直式，分類按鈕橫排在標題下方，選項一欄。
            // 手機：橫式兩欄 —— 左邊分類、右邊選項（選項再分兩個子欄）。
            //
            // 桌面版的面板是 560x700，在 720 高的畫布上等於整片蓋滿，
            // 而分類鈕只有 28 高、開關 26 高，都貼著可點擊尺寸的下限。
            // 手機把橫向空間用起來，列高才拉得開。
            private static bool IsMobile => MobileUi.IsMobile;

            private const int ContentStartY = 202;
            private const int ContentPaddingX = 30;
            private const int OptionRowHeight = 30;

            // ── 手機版面 ──
            private const int MobilePanelWidth = 1040;
            // 560 -> 660：左欄現在是「5 個動作 + 7 個設定分類」共 12 列。
            // 12 x 46 = 552，加上標題列 70 與群組間隔 14 就是 636，560 裝不下。
            // 畫布高度約 756（滿版之後），660 還留得下上下的餘裕。
            private const int MobilePanelHeight = 660;
            private const int MobileHeaderHeight = 64;
            private const int MobilePadding = 16;
            private const int MobileCategoryWidth = 250;
            private const int MobileCategoryHeight = 42;
            private const int MobileCategoryGap = 4;
            private const int MobileOptionRowHeight = 54;
            private const int MobileOptionPadding = 12;
            private const int MobileOptionColumns = 2;

            /// <summary>選項字級。13 在實機上偏小（實測截圖），提到 15。</summary>
            private const float MobileOptionFontSize = 15f;

            private int MobileOptionAreaX => MobilePadding + MobileCategoryWidth + MobilePadding + MobileOptionPadding;
            private int MobileOptionAreaWidth => MobilePanelWidth - MobileOptionAreaX - MobilePadding - MobileOptionPadding;
            private int MobileOptionColumnWidth => (MobileOptionAreaWidth - MobilePadding) / MobileOptionColumns;

            /// <summary>手機的選項是兩欄由上而下填，這裡記錄已經放了幾個。</summary>
            private int _mobileOptionIndex;
            private readonly ButtonControl _closeButton;
            private readonly int _panelWidth;
            private MenuTabButtonControl _activeCategoryButton;

            /// <summary>開啟時的滑入動畫（見 MobileUi.OpenAnimation）。</summary>
            private readonly MobileUi.OpenAnimation _openAnimation = new();
            private int _baseY = int.MinValue;

            public OptionsPanelControl(PauseMenuControl owner)
            {
                _owner = owner;
                AutoViewSize = false;
                ControlSize = IsMobile
                    ? new Point(MobilePanelWidth, MobilePanelHeight)
                    : new Point(560, 700);
                ViewSize = ControlSize;
                Align = Models.ControlAlign.HorizontalCenter | Models.ControlAlign.VerticalCenter;
                Interactive = true;
                HeaderHeight = IsMobile ? MobileHeaderHeight : 184;
                ContentTop = IsMobile ? MobileHeaderHeight + 6 : 190;
                DrawContentSurface = true;
                _panelWidth = ControlSize.X;

                var title = new LabelControl
                {
                    Text = "SETTINGS",
                    FontSize = IsMobile ? 18f : 22f,
                    TextColor = ModernHudTheme.TextGold,
                    IsBold = true,
                    Align = Models.ControlAlign.HorizontalCenter,
                    X = 0,
                    Y = IsMobile ? 20 : 18
                };
                Controls.Add(title);

                // 手機不放副標：一句沒有資訊的句子換走 30 px 的垂直空間不划算
                if (!IsMobile)
                {
                    var subtitle = new LabelControl
                    {
                        Text = "Tune the client without leaving the game",
                        FontSize = 10f,
                        TextColor = ModernHudTheme.TextGray,
                        HasShadow = false,
                        Align = Models.ControlAlign.HorizontalCenter,
                        X = 0,
                        Y = 50
                    };
                    Controls.Add(subtitle);
                }

                // ── 手機：左欄最上面是動作，下面才是設定分類 ──
                //
                // 桌面是兩層選單：MENU 開一個有六顆按鈕的面板，其中「設定」再開
                // 這個左右分欄的面板。手機不需要那一層 —— 左欄本來就是一份清單，
                // 把動作直接排進去，MENU 一按就到位，少一次點擊也少一個要關的視窗。
                if (IsMobile)
                {
                    AddActionRow("Continue", () => _owner.Visible = false);
                    AddActionRow("Party", () => _owner.TogglePartyPanelFromMenu());
                    AddActionRow("Character Select", () => _ = _owner.LeaveToCharacterSelectAsync());
                    AddActionRow("Server Select", () => _ = _owner.LeaveToServerSelectAsync());
                    AddActionRow("Exit Game", () => _ = _owner.ExitGameAsync(), isDanger: true);
                }

                int categoryStartY = 78;
                int categoryX = 20;
                int categoryWidth = 166;
                int categoryHeight = 28;
                int categorySpacing = 7;
                int categoriesPerRow = 3;
                int categoryIndex = 0;

                AddCategoryButton("Audio", () => BuildAudioCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                // Display 在手機上整組無效：解析度與全螢幕都由系統決定，MuGame 一律
                // 強制使用螢幕原生尺寸（iPhone Air 上是 2736x1260），選單裡的
                // 1280x720 之類選項按了不會有任何作用。
                if (!IsMobile)
                {
                    AddCategoryButton("Display", () => BuildDisplayCategory(), categoryStartY,
                        ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                }
                AddCategoryButton("Quality Preset", () => BuildQualityPresetCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                AddCategoryButton("World & Visibility", () => BuildWorldCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                AddCategoryButton("Render Scale", () => BuildRenderScaleCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                AddCategoryButton("Graphics", () => BuildGraphicsCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                AddCategoryButton("Lighting", () => BuildLightingCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                // Shadow Quality 在 iOS 上是壞的：開啟 Shadow Mapping 後角色陰影變成一個
                // 黑色方塊，Medium 以上整片地面變黑。陰影貼圖在 OpenGL ES 上需要可取樣的
                // 深度材質，這條路徑從未在手機上驗證過。角色腳下那圈陰影來自另一套
                // 機制，不受影響，所以整組隱藏不會失去任何目前可用的效果。
                if (!IsMobile)
                {
                    AddCategoryButton("Shadow Quality", () => BuildShadowQualityCategory(), categoryStartY,
                        ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);
                }
                AddCategoryButton("Performance", () => BuildPerformanceCategory(), categoryStartY,
                    ref categoryX, categoryWidth, categoryHeight, categorySpacing, categoriesPerRow, ref categoryIndex);

                _closeButton = new PauseMenuButtonControl
                {
                    Text = "Back to Pause Menu",
                    Subtitle = string.Empty,
                    Compact = true,
                    AccentColor = ModernHudTheme.Accent,
                    ControlSize = new Point(IsMobile ? MobileCategoryWidth : 190, IsMobile ? 44 : 38),
                    ViewSize = new Point(IsMobile ? MobileCategoryWidth : 190, IsMobile ? 44 : 38),
                    X = IsMobile ? MobilePadding : (ControlSize.X - 190) / 2,
                    // 手機放在左欄分類清單的下方，不要疊在選項欄上
                    Y = IsMobile ? MobilePanelHeight - 44 - MobilePadding : ContentStartY,
                    AutoViewSize = false,
                    FontSize = 12f,
                    TextColor = ModernHudTheme.TextWhite
                };
                // 手機沒有第一層選單可以返回 —— 左欄第一列的 Continue 就是離開。
                // 這顆保留成第二個出口（面板左下角），一樣是直接關掉。
                _closeButton.Text = IsMobile ? "Close" : "Back to Pause Menu";
                _closeButton.Click += (s, e) =>
                {
                    if (IsMobile)
                        _owner.Visible = false;
                    else
                        _owner.ToggleOptionsPanel();
                };

                // 手機不顯示這一顆：左欄第一列的 Continue 已經是出口，
                // 面板底部再放一顆「關閉」只是同一件事的第二個按鈕，
                // 而且會和最後一個設定分類擠在一起。
                _closeButton.Visible = !IsMobile;
                Controls.Add(_closeButton);

                BuildAudioCategory(); // default category
            }

            private delegate void CategoryBuilder(ref int currentY);

            private void ClearDynamicControls()
            {
                _mobileOptionIndex = 0;   // 換分類時兩欄的填入位置要從頭算

                foreach (var ctrl in _dynamicControls)
                {
                    Controls.Remove(ctrl);
                }
                _dynamicControls.Clear();
                _options.Clear();
            }

            private void BuildCategory(string categoryName, CategoryBuilder builder)
            {
                ClearDynamicControls();

                int currentY = ContentStartY;
                AddHeading(categoryName, ref currentY);
                builder(ref currentY);

                if (IsMobile)
                {
                    LayoutMobileOptions();
                }
                else
                {
                    _closeButton.Y = currentY + 10;
                }

                _closeButton.BringToFront();
            }

            private void BuildAudioCategory()
            {
                BuildCategory("Audio", (ref int currentY) =>
                {
                    AddOption("Background Music", () => Constants.BACKGROUND_MUSIC, value =>
                    {
                        Constants.BACKGROUND_MUSIC = value;
                        _owner.ApplyBackgroundMusicSetting(value);
                    }, ref currentY, OptionRowHeight);

                    AddOption("Sound Effects", () => Constants.SOUND_EFFECTS, value =>
                    {
                        Constants.SOUND_EFFECTS = value;
                        _owner.ApplySoundEffectsVolume();
                    }, ref currentY, OptionRowHeight);
                    AddVolumeControl("Music Volume", () => Constants.BACKGROUND_MUSIC_VOLUME, value =>
                    {
                        Constants.BACKGROUND_MUSIC_VOLUME = value;
                        _owner.ApplyBackgroundMusicVolume();
                    }, ref currentY, OptionRowHeight);
                    AddVolumeControl("Effects Volume", () => Constants.SOUND_EFFECTS_VOLUME, value =>
                    {
                        Constants.SOUND_EFFECTS_VOLUME = value;
                        _owner.ApplySoundEffectsVolume();
                    }, ref currentY, OptionRowHeight);
                });
            }

            private void BuildWorldCategory()
            {
                BuildCategory("World & Visibility", (ref int currentY) =>
                {
                    AddOption("Draw Bounding Boxes", () => Constants.DRAW_BOUNDING_BOXES, value => Constants.DRAW_BOUNDING_BOXES = value, ref currentY, OptionRowHeight);
                    AddOption("Draw Bounding Boxes (Interactives)", () => Constants.DRAW_BOUNDING_BOXES_INTERACTIVES, value => Constants.DRAW_BOUNDING_BOXES_INTERACTIVES = value, ref currentY, OptionRowHeight);
                    AddOption("Draw Grass", () => Constants.DRAW_GRASS, value =>
                    {
                        Constants.DRAW_GRASS = value;
                        MuGame.PersistRenderToggle("DRAW_GRASS", value);
                        if (value)
                        {
                            // When enabling grass, ensure textures are loaded
                            var scene = MuGame.Instance?.ActiveScene as BaseScene;
                            scene?.World?.Terrain?.ReloadGrassIfNeeded();
                        }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Low Quality Switch", () => Constants.ENABLE_LOW_QUALITY_SWITCH, value => { Constants.ENABLE_LOW_QUALITY_SWITCH = value; MuGame.PersistRenderToggle("ENABLE_LOW_QUALITY_SWITCH", value); }, ref currentY, OptionRowHeight);
                    AddOption("Low Quality in Login", () => Constants.ENABLE_LOW_QUALITY_IN_LOGIN_SCENE, value => { Constants.ENABLE_LOW_QUALITY_IN_LOGIN_SCENE = value; MuGame.PersistRenderToggle("ENABLE_LOW_QUALITY_IN_LOGIN_SCENE", value); }, ref currentY, OptionRowHeight);
                });
            }

            private void BuildQualityPresetCategory()
            {
                BuildCategory("Quality Preset", (ref int currentY) =>
                {
                    AddOption("Auto (Detect)", () => GraphicsQualityManager.UserPreset == GraphicsQualityPreset.Auto, value =>
                    {
                        if (value) _owner.ApplyQualityPreset(GraphicsQualityPreset.Auto, RefreshOptions);
                    }, ref currentY, OptionRowHeight);
                    AddOption("Low (0.75x)", () => GraphicsQualityManager.UserPreset == GraphicsQualityPreset.Low, value =>
                    {
                        if (value) _owner.ApplyQualityPreset(GraphicsQualityPreset.Low, RefreshOptions);
                    }, ref currentY, OptionRowHeight);
                    AddOption("Medium (1.0x)", () => GraphicsQualityManager.UserPreset == GraphicsQualityPreset.Medium, value =>
                    {
                        if (value) _owner.ApplyQualityPreset(GraphicsQualityPreset.Medium, RefreshOptions);
                    }, ref currentY, OptionRowHeight);
                    AddOption("High (2.0x)", () => GraphicsQualityManager.UserPreset == GraphicsQualityPreset.High, value =>
                    {
                        if (value) _owner.ApplyQualityPreset(GraphicsQualityPreset.High, RefreshOptions);
                    }, ref currentY, OptionRowHeight);
                });
            }

            private void BuildRenderScaleCategory()
            {
                BuildCategory("Render Scale", (ref int currentY) =>
                {
                    AddOption("Render Scale: 300%", () => Math.Abs(Constants.RENDER_SCALE - 3.0f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(3.0f); }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Render Scale: 200%", () => Math.Abs(Constants.RENDER_SCALE - 2.0f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(2.0f); }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Render Scale: 150%", () => Math.Abs(Constants.RENDER_SCALE - 1.5f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(1.5f); }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Render Scale: 125%", () => Math.Abs(Constants.RENDER_SCALE - 1.25f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(1.25f); }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Render Scale: 100%", () => Math.Abs(Constants.RENDER_SCALE - 1.0f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(1.0f); }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Render Scale: 75%", () => Math.Abs(Constants.RENDER_SCALE - 0.75f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(0.75f); }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Render Scale: 60%", () => Math.Abs(Constants.RENDER_SCALE - 0.6f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(0.6f); }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Render Scale: 50%", () => Math.Abs(Constants.RENDER_SCALE - 0.5f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(0.5f); }
                    }, ref currentY, OptionRowHeight);
                    AddOption("Render Scale: 37.5%", () => Math.Abs(Constants.RENDER_SCALE - 0.375f) < 0.01f, value =>
                    {
                        if (value) { SetRenderScale(0.375f); }
                    }, ref currentY, OptionRowHeight);
                });
            }

            private void BuildGraphicsCategory()
            {
                BuildCategory("Graphics", (ref int currentY) =>
                {
                    AddOption("High Quality Textures", () => Constants.HIGH_QUALITY_TEXTURES, value => { Constants.HIGH_QUALITY_TEXTURES = value; MuGame.PersistRenderToggle("HIGH_QUALITY_TEXTURES", value); }, ref currentY, OptionRowHeight);

                    // FXAA 先前沒有選單入口，只能靠鍵盤快捷鍵切換 —— 手機上等於無法使用。
                    // 這個 shader 的頂點著色器原本缺少變換矩陣，開啟後畫面只剩左上角一小塊，
                    // 修好後需要實機確認，因此保留為預設關閉、由玩家自行開啟。
                    if (GraphicsManager.Instance?.FXAAEffect != null)
                    {
                        AddOption("FXAA (Anti-aliasing)", () => GraphicsManager.Instance.IsFXAAEnabled, value =>
                        {
                            GraphicsManager.Instance.IsFXAAEnabled = value;
                            MuGame.PersistRenderToggle("FXAA", value);
                        }, ref currentY, OptionRowHeight);
                    }
                    AddOption("V-Sync", () => !Constants.DISABLE_VSYNC, value =>
                    {
                        _owner.SetVSync(value);
                    }, ref currentY, OptionRowHeight, RefreshOptions);
                });
            }

            private void BuildLightingCategory()
            {
                BuildCategory("Lighting & Materials", (ref int currentY) =>
                {
                    AddOption("Sun Light", () => Constants.SUN_ENABLED, value => Constants.SUN_ENABLED = value, ref currentY, OptionRowHeight);
                    AddOption("Day-Night Cycle (Real Time)", () => Constants.ENABLE_DAY_NIGHT_CYCLE, value =>
                    {
                        Constants.ENABLE_DAY_NIGHT_CYCLE = value;
                        if (!value)
                            SunCycleManager.ResetToDefault();
                    }, ref currentY, OptionRowHeight);
                    AddOption("Sun From +X", () => Constants.SUN_DIRECTION.X >= 0f, value =>
                    {
                        var dir = Constants.SUN_DIRECTION;
                        if (dir.LengthSquared() < 0.0001f)
                            dir = new Vector3(1f, 0f, -0.6f);
                        dir.X = Math.Abs(dir.X) * (value ? 1f : -1f);
                        Constants.SUN_DIRECTION = dir;
                    }, ref currentY, OptionRowHeight);
                    AddVolumeControl("Sun Strength (%)", () => Constants.SUN_STRENGTH * 100f, value =>
                    {
                        Constants.SUN_STRENGTH = MathHelper.Clamp(value, 0f, 200f) / 100f;
                    }, ref currentY, OptionRowHeight, 0f, 200f, 5f);
                    AddVolumeControl("Sun Shadow (%)", () => Constants.SUN_SHADOW_STRENGTH * 100f, value =>
                    {
                        Constants.SUN_SHADOW_STRENGTH = MathHelper.Clamp(value, 0f, 100f) / 100f;
                    }, ref currentY, OptionRowHeight, 0f, 100f, 5f);
                    AddOption("Terrain GPU Lighting", () => Constants.ENABLE_TERRAIN_GPU_LIGHTING, value => Constants.ENABLE_TERRAIN_GPU_LIGHTING = value, ref currentY, OptionRowHeight);
                    AddOption("Dynamic Lights", () => Constants.ENABLE_DYNAMIC_LIGHTS, value =>
                    {
                        Constants.ENABLE_DYNAMIC_LIGHTS = value;
                        MuGame.PersistRenderToggle("ENABLE_DYNAMIC_LIGHTS", value);
                    }, ref currentY, OptionRowHeight, RefreshOptions);
                    AddOption("Dynamic Lighting Shader (GPU)", () => Constants.ENABLE_DYNAMIC_LIGHTING_SHADER, value =>
                    {
                        Constants.ENABLE_DYNAMIC_LIGHTING_SHADER = value;
                        MuGame.PersistRenderToggle("ENABLE_DYNAMIC_LIGHTING_SHADER", value);
                        if (!value)
                            Constants.ENABLE_TERRAIN_GPU_LIGHTING = false;
                    }, ref currentY, OptionRowHeight, RefreshOptions);
                    AddOption("Optimize for Integrated GPU", () => Constants.OPTIMIZE_FOR_INTEGRATED_GPU, value => Constants.OPTIMIZE_FOR_INTEGRATED_GPU = value, ref currentY, OptionRowHeight);
                    AddOption("Debug Lighting Areas", () => Constants.DEBUG_LIGHTING_AREAS, value => Constants.DEBUG_LIGHTING_AREAS = value, ref currentY, OptionRowHeight);
                    AddOption("Item Material Shader", () => Constants.ENABLE_ITEM_MATERIAL_SHADER, value => { Constants.ENABLE_ITEM_MATERIAL_SHADER = value; MuGame.PersistRenderToggle("ENABLE_ITEM_MATERIAL_SHADER", value); }, ref currentY, OptionRowHeight);
                    AddOption("Monster Material Shader", () => Constants.ENABLE_MONSTER_MATERIAL_SHADER, value => { Constants.ENABLE_MONSTER_MATERIAL_SHADER = value; MuGame.PersistRenderToggle("ENABLE_MONSTER_MATERIAL_SHADER", value); }, ref currentY, OptionRowHeight);
                });
            }

            private void BuildShadowQualityCategory()
            {
                BuildCategory("Shadow Quality", (ref int currentY) =>
                {
                    AddOption("Shadow Mapping", () => Constants.ENABLE_SHADOW_MAPPING, value =>
                    {
                        Constants.ENABLE_SHADOW_MAPPING = value;
                        if (value && Constants.GetCurrentShadowQuality() == Constants.ShadowQuality.Off)
                        {
                            Constants.ApplyShadowQualityPreset(Constants.ShadowQuality.Medium);
                        }
                        OnShadowSettingChanged();
                    }, ref currentY, OptionRowHeight);

                    AddOption("Force Monster Mesh Shadows", () => MuGame.AppSettings?.Graphics?.ForceMonsterMeshShadows == true, value =>
                    {
                        var graphicsSettings = MuGame.AppSettings?.Graphics;
                        if (graphicsSettings == null)
                            return;

                        graphicsSettings.ForceMonsterMeshShadows = value;
                        MuGame.PersistMonsterShadowMode(value);
                        OnShadowSettingChanged();
                    }, ref currentY, OptionRowHeight);

                    currentY += 8;
                    AddHeading("Quality Presets", ref currentY);

                    AddOption("Off (Disabled)", () => Constants.GetCurrentShadowQuality() == Constants.ShadowQuality.Off, value =>
                    {
                        if (value) { Constants.ApplyShadowQualityPreset(Constants.ShadowQuality.Off); OnShadowSettingChanged(); }
                    }, ref currentY, OptionRowHeight);

                    AddOption("Low (512px, 800 units)", () => Constants.GetCurrentShadowQuality() == Constants.ShadowQuality.Low, value =>
                    {
                        if (value) { Constants.ApplyShadowQualityPreset(Constants.ShadowQuality.Low); OnShadowSettingChanged(); }
                    }, ref currentY, OptionRowHeight);

                    AddOption("Medium (1024px, 1200 units)", () => Constants.GetCurrentShadowQuality() == Constants.ShadowQuality.Medium, value =>
                    {
                        if (value) { Constants.ApplyShadowQualityPreset(Constants.ShadowQuality.Medium); OnShadowSettingChanged(); }
                    }, ref currentY, OptionRowHeight);

                    AddOption("High (1024px, 1500 units)", () => Constants.GetCurrentShadowQuality() == Constants.ShadowQuality.High, value =>
                    {
                        if (value) { Constants.ApplyShadowQualityPreset(Constants.ShadowQuality.High); OnShadowSettingChanged(); }
                    }, ref currentY, OptionRowHeight);

                    AddOption("Ultra (2048px, 2000 units)", () => Constants.GetCurrentShadowQuality() == Constants.ShadowQuality.Ultra, value =>
                    {
                        if (value) { Constants.ApplyShadowQualityPreset(Constants.ShadowQuality.Ultra); OnShadowSettingChanged(); }
                    }, ref currentY, OptionRowHeight);
                });
            }

            private void OnShadowSettingChanged()
            {
                // Force shadow map renderer to recreate render targets with new settings
                var shadowRenderer = GraphicsManager.Instance?.ShadowMapRenderer;
                if (shadowRenderer != null)
                {
                    shadowRenderer.EnsureRenderTarget();
                }
                RefreshOptions();
            }

            private void BuildPerformanceCategory()
            {
                BuildCategory("Performance & Debug", (ref int currentY) =>
                {
                    AddVolumeControl("Dynamic Light Update FPS", () => Constants.DYNAMIC_LIGHT_UPDATE_FPS, value =>
                    {
                        int fps = Constants.ClampPerformanceFps((int)value);
                        Constants.DYNAMIC_LIGHT_UPDATE_FPS = fps;

                        var graphicsSettings = MuGame.AppSettings?.Graphics;
                        if (graphicsSettings != null)
                        {
                            graphicsSettings.DynamicLightUpdateFps = fps;
                            MuGame.PersistGraphicsPerformanceCaps(graphicsSettings.DynamicLightUpdateFps, graphicsSettings.AnimationUpdateFps);
                        }
                    }, ref currentY, OptionRowHeight,
                    Constants.MIN_PERFORMANCE_FPS_CAP, Constants.MAX_PERFORMANCE_FPS_CAP, 1f, " FPS");

                    AddVolumeControl("Animation Update FPS", () => Constants.ANIMATION_UPDATE_FPS, value =>
                    {
                        int fps = Constants.ClampPerformanceFps((int)value);
                        Constants.ANIMATION_UPDATE_FPS = fps;

                        var graphicsSettings = MuGame.AppSettings?.Graphics;
                        if (graphicsSettings != null)
                        {
                            graphicsSettings.AnimationUpdateFps = fps;
                            MuGame.PersistGraphicsPerformanceCaps(graphicsSettings.DynamicLightUpdateFps, graphicsSettings.AnimationUpdateFps);
                        }
                    }, ref currentY, OptionRowHeight,
                    Constants.MIN_PERFORMANCE_FPS_CAP, Constants.MAX_PERFORMANCE_FPS_CAP, 1f, " FPS");

                    AddOption("Unlimited FPS", () => Constants.UNLIMITED_FPS, value => _owner.SetUnlimitedFps(value), ref currentY, OptionRowHeight, RefreshOptions);
                    AddOption("Dynamic Buffer Pool", () => Constants.ENABLE_DYNAMIC_BUFFER_POOL, value =>
                    {
                        DynamicBufferPool.SetEnabled(value);
                    }, ref currentY, OptionRowHeight);
                    AddOption("Item Material Animation", () => Constants.ENABLE_ITEM_MATERIAL_ANIMATION, value => Constants.ENABLE_ITEM_MATERIAL_ANIMATION = value, ref currentY, OptionRowHeight);
                    AddOption("Debug Panel", () => Constants.SHOW_DEBUG_PANEL, value =>
                    {
                        Constants.SHOW_DEBUG_PANEL = value;
                        _owner.ApplyDebugPanelSetting();
                    }, ref currentY, OptionRowHeight);
                });
            }

            private void BuildDisplayCategory()
            {
                BuildCategory("Display", (ref int currentY) =>
                {
                    var settings = MuGame.AppSettings?.Graphics;
                    if (settings == null) return;

                    // Get supported display modes from adapter
                    var adapter = GraphicsManager.Instance?.GraphicsDevice?.Adapter ?? GraphicsAdapter.DefaultAdapter;
                    var maxDisplayMode = adapter.CurrentDisplayMode;
                    int maxWidth = maxDisplayMode.Width;
                    int maxHeight = maxDisplayMode.Height;

                    // Helper to check if resolution is supported by adapter for fullscreen
                    bool IsResolutionSupported(int w, int h)
                    {
                        // Always allow resolutions up to max for windowed mode
                        if (!settings.IsFullScreen) return w <= maxWidth && h <= maxHeight;

                        // For fullscreen, check if adapter supports this mode
                        foreach (var mode in adapter.SupportedDisplayModes)
                        {
                            if (mode.Width == w && mode.Height == h)
                                return true;
                        }
                        return false;
                    }

                    AddHeading("Resolution", ref currentY);

                    // Standard 16:9 resolutions only - to maintain UI aspect ratio
                    if (IsResolutionSupported(1280, 720))
                    {
                        AddOption("1280x720", () => settings.Width == 1280 && settings.Height == 720, value =>
                        {
                            if (value) SetResolution(1280, 720);
                        }, ref currentY, OptionRowHeight);
                    }

                    if (IsResolutionSupported(1920, 1080))
                    {
                        AddOption("1920x1080", () => settings.Width == 1920 && settings.Height == 1080, value =>
                        {
                            if (value) SetResolution(1920, 1080);
                        }, ref currentY, OptionRowHeight);
                    }

                    if (IsResolutionSupported(2560, 1440))
                    {
                        AddOption("2560x1440", () => settings.Width == 2560 && settings.Height == 1440, value =>
                        {
                            if (value) SetResolution(2560, 1440);
                        }, ref currentY, OptionRowHeight);
                    }

                    if (IsResolutionSupported(3840, 2160))
                    {
                        AddOption("3840x2160", () => settings.Width == 3840 && settings.Height == 2160, value =>
                        {
                            if (value) SetResolution(3840, 2160);
                        }, ref currentY, OptionRowHeight);
                    }

                    currentY += 8;
                    AddHeading("Window Mode", ref currentY);

                    AddOption("Fullscreen", () => settings.IsFullScreen, value =>
                    {
                        SetFullscreen(value);
                    }, ref currentY, OptionRowHeight);
                });
            }

            private void SetResolution(int width, int height)
            {
                var settings = MuGame.AppSettings?.Graphics;
                if (settings == null) return;

                settings.Width = width;
                settings.Height = height;

                MuGame.ScheduleOnMainThread(() =>
                {
                    MuGame.Instance.ApplyGraphicsConfiguration(settings);
                    GraphicsManager.Instance.UpdateRenderScale();
                });

                MuGame.PersistDisplaySettings(width, height, settings.IsFullScreen);
                RefreshOptions();
            }

            private void SetFullscreen(bool enabled)
            {
                var settings = MuGame.AppSettings?.Graphics;
                if (settings == null) return;

                settings.IsFullScreen = enabled;

                MuGame.ScheduleOnMainThread(() =>
                {
                    MuGame.Instance.ApplyGraphicsConfiguration(settings);
                    GraphicsManager.Instance.UpdateRenderScale();
                });

                MuGame.PersistDisplaySettings(settings.Width, settings.Height, enabled);
                RefreshOptions();
            }

            /// <summary>由外部在顯示面板時呼叫，重新播放滑入。</summary>
            public void PlayOpenAnimation()
            {
                _baseY = int.MinValue;
                _openAnimation.Restart();
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);

                if (!IsMobile || !Visible)
                    return;

                // Align 會在 base.Update 之後把 Y 算成置中；記下它當作基準，
                // 動畫的偏移疊在上面。
                if (_baseY == int.MinValue || _openAnimation.OffsetPixels == 0)
                    _baseY = Y - _openAnimation.OffsetPixels;

                _openAnimation.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
                Y = _baseY + _openAnimation.OffsetPixels;
            }

            /// <summary>動作群組佔用的列數（Continue / Party / Character / Server / Exit）。</summary>
            private const int MobileActionRowCount = 5;

            /// <summary>動作群組與設定分類之間的分隔。</summary>
            private const int MobileActionGroupGap = 14;

            /// <summary>設定分類清單的上緣：動作群組之下。</summary>
            private int MobileCategoryListTop
                => ContentTop
                 + MobileActionRowCount * (MobileCategoryHeight + MobileCategoryGap)
                 + MobileActionGroupGap;

            private int _mobileActionIndex;

            /// <summary>
            /// 左欄上半部的動作列。外觀和設定分類一樣（同一份清單），
            /// 但不參與分類的選中狀態 —— 它們是「做一件事」，不是「看一組設定」。
            /// </summary>
            private void AddActionRow(string label, Action onClick, bool isDanger = false)
            {
                int y = ContentTop + _mobileActionIndex * (MobileCategoryHeight + MobileCategoryGap);
                _mobileActionIndex++;

                var button = new MenuTabButtonControl
                {
                    Text = label,
                    IsAction = true,
                    IsDanger = isDanger,
                    X = MobilePadding,
                    Y = y,
                    ControlSize = new Point(MobileCategoryWidth, MobileCategoryHeight),
                    ViewSize = new Point(MobileCategoryWidth, MobileCategoryHeight),
                    AutoViewSize = false,
                    FontSize = 14f,
                    TextColor = ModernHudTheme.TextGray
                };
                button.Click += (s, e) => onClick();
                Controls.Add(button);
            }

            private void AddCategoryButton(string label, Action onClick, int startY,
                ref int currentX, int width, int height, int spacing, int perRow, ref int index)
            {
                int x, y;
                if (IsMobile)
                {
                    // 左側一整欄，由上而下。橫排的窄條在觸控上很難按準。
                    x = MobilePadding;
                    y = MobileCategoryListTop + index * (MobileCategoryHeight + MobileCategoryGap);
                    width = MobileCategoryWidth;
                    height = MobileCategoryHeight;
                }
                else
                {
                    int row = index / perRow;
                    int col = index % perRow;
                    x = 20 + col * (width + spacing);
                    y = startY + row * (height + spacing);
                }

                var button = new MenuTabButtonControl
                {
                    Text = label,
                    X = x,
                    Y = y,
                    ControlSize = new Point(width, height),
                    ViewSize = new Point(width, height),
                    AutoViewSize = false,
                    FontSize = IsMobile ? 14f : 10.5f,
                    TextColor = ModernHudTheme.TextGray
                };
                button.Click += (s, e) =>
                {
                    if (_activeCategoryButton != null)
                        _activeCategoryButton.Active = false;
                    _activeCategoryButton = button;
                    _activeCategoryButton.Active = true;
                    onClick();
                };
                Controls.Add(button);
                if (_activeCategoryButton == null)
                {
                    _activeCategoryButton = button;
                    _activeCategoryButton.Active = true;
                }

                currentX += width + spacing;
                index++;
            }

            private void SetRenderScale(float scale)
            {
            MuGame.PersistRenderScale(scale);
                float clampedScale = MathHelper.Clamp(scale, 0.3f, 3.0f);

                if (Math.Abs(Constants.RENDER_SCALE - clampedScale) < 0.0001f)
                {
                    RefreshOptions();
                    return;
                }

                Constants.RENDER_SCALE = clampedScale;
                GraphicsManager.Instance.UpdateRenderScale();
                RefreshOptions();
            }

            private void RefreshOptions()
            {
                foreach (var option in _options)
                {
                    option.Refresh();
                }
            }

            private void AddOption(string label, Func<bool> getter, Action<bool> setter, ref int currentY, int rowHeight, Action onChanged = null)
            {
                Action<bool> apply = value =>
                {
                    setter(value);
                    onChanged?.Invoke();
                };

                OptionToggle option;
                if (IsMobile)
                {
                    // 兩欄由上而下填：先填滿左欄再換右欄。
                    int perColumn = Math.Max(1, (MobilePanelHeight - ContentTop - MobilePadding) / MobileOptionRowHeight);
                    int column = Math.Min(_mobileOptionIndex / perColumn, MobileOptionColumns - 1);
                    int row = _mobileOptionIndex - column * perColumn;

                    int x = MobileOptionAreaX + column * (MobileOptionColumnWidth + MobilePadding);
                    int y = ContentTop + row * MobileOptionRowHeight;

                    option = new OptionToggle(label, getter, apply,
                        x, y, MobileOptionColumnWidth, 104, 38, MobileOptionFontSize);
                    _mobileOptionIndex++;
                }
                else
                {
                    option = new OptionToggle(label, getter, apply, currentY, _panelWidth);
                    currentY += rowHeight;
                }

                option.AddTo(Controls);
                option.CollectControls(_dynamicControls);
                _options.Add(option);
            }

            /// <summary>
            /// 手機：建完整個分類之後才排版，這樣才知道總共有幾個選項。
            ///
            /// 邊建邊排的話「先填滿左欄再換右欄」會讓 10 個選項變成 9 + 1，
            /// 右欄只有一列、看起來像沒排好。知道總數之後可以平均分配：
            /// 2 個選項用一欄，10 個選項用兩欄各 5 個。
            /// </summary>
            private void LayoutMobileOptions()
            {
                int count = _options.Count;
                if (count == 0)
                    return;

                int available = MobilePanelHeight - ContentTop - MobilePadding;
                int perColumnMax = Math.Max(1, available / MobileOptionRowHeight);

                int columns = Math.Min(MobileOptionColumns, (count + perColumnMax - 1) / perColumnMax);
                columns = Math.Max(1, columns);
                int rowsPerColumn = (count + columns - 1) / columns;

                for (int i = 0; i < count; i++)
                {
                    int column = Math.Min(i / rowsPerColumn, columns - 1);
                    int row = i - column * rowsPerColumn;

                    _options[i].SetPosition(
                        MobileOptionAreaX + column * (MobileOptionColumnWidth + MobilePadding),
                        ContentTop + row * MobileOptionRowHeight,
                        MobileOptionColumnWidth);
                }
            }

            private void AddHeading(string label, ref int currentY)
            {
                if (IsMobile)
                    return;   // 手機的分類已經在左欄，右欄再放小標只是雜訊

                var heading = new LabelControl
                {
                    Text = label,
                    X = ContentPaddingX,
                    Y = currentY,
                    FontSize = 13f,
                    TextColor = ModernHudTheme.TextGold,
                    IsBold = true,
                    HasShadow = false
                };
                Controls.Add(heading);
                _dynamicControls.Add(heading);
                currentY += 18;
            }

            public void Refresh()
            {
                foreach (var option in _options)
                {
                    option.Refresh();
                }
            }

            private void AddVolumeControl(string label, Func<float> getter, Action<float> setter, ref int currentY, int rowHeight, float minValue = 0f, float maxValue = 100f, float step = 5f, string valueSuffix = "%")
            {
                var option = new OptionVolume(label, getter, setter, currentY, _panelWidth, minValue, maxValue, step, valueSuffix);
                option.AddTo(Controls);
                option.CollectControls(_dynamicControls);
                _options.Add(option);
                currentY += rowHeight;
            }

            private interface IOptionRow
            {
                void AddTo(ChildrenCollection<GameControl> controls);
                void Refresh();
                void CollectControls(List<GameControl> controls);

                /// <summary>建完之後重新定位，供手機的欄位平衡使用。</summary>
                void SetPosition(int x, int y, int width);
            }

            private sealed class OptionToggle : IOptionRow
            {
                private readonly LabelControl _label;
                private readonly ButtonControl _button;
                private readonly Func<bool> _getter;
                private readonly Action<bool> _setter;

                public OptionToggle(string label, Func<bool> getter, Action<bool> setter, int y, int panelWidth)
                    : this(label, getter, setter, ContentPaddingX, y, panelWidth - ContentPaddingX - 40, 110, 26, 11.5f)
                {
                }

                /// <summary>
                /// 明確指定位置與尺寸的版本。手機把選項排成兩欄、列高加大，
                /// 需要自己決定 x 與欄寬，不能沿用「靠面板右緣」的假設。
                /// </summary>
                public OptionToggle(string label, Func<bool> getter, Action<bool> setter,
                                    int x, int y, int width, int buttonWidth, int buttonHeight, float fontSize)
                {
                    _getter = getter;
                    _setter = setter;

                    // 標籤和開關在同一列，長標籤會直接壓在開關上（實機截圖：
                    // 「Dynamic Lighting Shader」和 ENABLED 疊在一起）。
                    // 先量過再決定要不要截斷，並且左右都留邊距。
                    string display = FitLabel(label, width - buttonWidth - 20, fontSize);

                    _label = new LabelControl
                    {
                        Text = display,
                        X = x,
                        Y = y,
                        FontSize = fontSize,
                        TextColor = ModernHudTheme.TextWhite,
                        HasShadow = false
                    };

                    _button = new ButtonControl
                    {
                        ControlSize = new Point(buttonWidth, buttonHeight),
                        ViewSize = new Point(buttonWidth, buttonHeight),
                        AutoViewSize = false,
                        X = x + width - buttonWidth,
                        Y = y - 4,
                        BackgroundColor = new Color(28, 35, 46, 230),
                        HoverBackgroundColor = new Color(48, 58, 73, 240),
                        PressedBackgroundColor = new Color(18, 23, 31, 245),
                        FontSize = fontSize * 0.95f,
                        TextColor = ModernHudTheme.TextWhite,
                        HoverTextColor = ModernHudTheme.TextGold
                    };
                    _button.Click += (s, e) =>
                    {
                        bool newValue = !_getter();
                        _setter(newValue);
                        Refresh();
                    };

                    Refresh();
                }

                public void AddTo(ChildrenCollection<GameControl> controls)
                {
                    controls.Add(_label);
                    controls.Add(_button);
                }

                public void Refresh()
                {
                    bool value = _getter();
                    _button.Text = value ? "ENABLED" : "DISABLED";
                    _button.BackgroundColor = value ? new Color(34, 74, 55, 225) : new Color(55, 37, 43, 220);
                    _button.HoverBackgroundColor = value ? new Color(45, 96, 70, 240) : new Color(78, 47, 55, 238);
                    _button.TextColor = value ? new Color(150, 235, 180) : new Color(210, 145, 150);
                    _button.HoverTextColor = Color.White;
                }

                public void CollectControls(List<GameControl> controls)
                {
                    controls.Add(_label);
                    controls.Add(_button);
                }

                public void SetPosition(int x, int y, int width)
                {
                    _label.X = x;
                    _label.Y = y;
                    _button.X = x + width - _button.ViewSize.X;
                    _button.Y = y - 4;
                }

                /// <summary>把標籤截到指定寬度以內，超出的部分以刪節號結尾。</summary>
                private static string FitLabel(string text, int maxWidth, float fontSize)
                {
                    var font = GraphicsManager.Instance?.Font;
                    if (font == null || maxWidth <= 0 || string.IsNullOrEmpty(text))
                        return text;

                    float scale = fontSize / Constants.BASE_FONT_SIZE;
                    float width = font.MeasureString(text).X * scale;
                    if (width <= maxWidth)
                        return text;

                    int keep = Math.Max(1, (int)(text.Length * (maxWidth / width)) - 1);
                    return text.Substring(0, keep).TrimEnd() + "…";
                }
            }

            private sealed class OptionVolume : IOptionRow
            {
                private readonly LabelControl _label;
                private readonly LabelControl _valueLabel;
                private readonly ButtonControl _minusButton;
                private readonly ButtonControl _plusButton;
                private readonly Func<float> _getter;
                private readonly Action<float> _setter;
                private readonly float _minValue;
                private readonly float _maxValue;
                private readonly float _step;
                private readonly string _valueSuffix;

                public OptionVolume(string label, Func<float> getter, Action<float> setter, int y, int panelWidth, float minValue = 0f, float maxValue = 100f, float step = 5f, string valueSuffix = "%")
                {
                    _getter = getter;
                    _setter = setter;
                    _minValue = minValue;
                    _maxValue = maxValue;
                    _step = step;
                    _valueSuffix = string.IsNullOrWhiteSpace(valueSuffix) ? string.Empty : valueSuffix;

                    _label = new LabelControl
                    {
                        Text = label,
                        X = ContentPaddingX,
                        Y = y,
                        FontSize = 11.5f,
                        TextColor = ModernHudTheme.TextWhite,
                        HasShadow = false
                    };

                    _valueLabel = new LabelControl
                    {
                        X = panelWidth - 210,
                        Y = y,
                        FontSize = 11f,
                        TextColor = ModernHudTheme.TextGold,
                        BackgroundColor = new Color(8, 12, 18, 180),
                        UseControlSizeBackground = true,
                        Padding = new Margin { Left = 6, Right = 6, Top = 2, Bottom = 2 },
                        HasShadow = false,
                        ControlSize = new Point(70, 24),
                        ViewSize = new Point(70, 24)
                    };

                    _minusButton = new ButtonControl
                    {
                        Text = "-",
                        ControlSize = new Point(28, 24),
                        ViewSize = new Point(28, 24),
                        AutoViewSize = false,
                        X = panelWidth - 130,
                        Y = y - 2,
                        BackgroundColor = new Color(28, 35, 46, 230),
                        HoverBackgroundColor = new Color(48, 58, 73, 240),
                        PressedBackgroundColor = new Color(18, 23, 31, 245),
                        FontSize = 11f,
                        TextColor = ModernHudTheme.TextWhite,
                        HoverTextColor = ModernHudTheme.TextGold
                    };

                    _plusButton = new ButtonControl
                    {
                        Text = "+",
                        ControlSize = new Point(28, 24),
                        ViewSize = new Point(28, 24),
                        AutoViewSize = false,
                        X = panelWidth - 96,
                        Y = y - 2,
                        BackgroundColor = new Color(28, 35, 46, 230),
                        HoverBackgroundColor = new Color(48, 58, 73, 240),
                        PressedBackgroundColor = new Color(18, 23, 31, 245),
                        FontSize = 11f,
                        TextColor = ModernHudTheme.TextWhite,
                        HoverTextColor = ModernHudTheme.TextGold
                    };

                    _minusButton.Click += (s, e) => AdjustVolume(-_step);
                    _plusButton.Click += (s, e) => AdjustVolume(_step);

                    Refresh();
                }

                private void AdjustVolume(float delta)
                {
                    float value = MathHelper.Clamp(_getter() + delta, _minValue, _maxValue);
                    value = (float)Math.Round(value);
                    _setter(value);
                    Refresh();
                }

                public void AddTo(ChildrenCollection<GameControl> controls)
                {
                    controls.Add(_label);
                    controls.Add(_valueLabel);
                    controls.Add(_minusButton);
                    controls.Add(_plusButton);
                }

                public void Refresh()
                {
                    float value = MathHelper.Clamp(_getter(), _minValue, _maxValue);
                    _valueLabel.Text = $"{Math.Round(value)}{_valueSuffix}";
                    _minusButton.Enabled = value > _minValue;
                    _plusButton.Enabled = value < _maxValue;
                }

                public void CollectControls(List<GameControl> controls)
                {
                    controls.Add(_label);
                    controls.Add(_valueLabel);
                    controls.Add(_minusButton);
                    controls.Add(_plusButton);
                }

                public void SetPosition(int x, int y, int width)
                {
                    int dy = y - _label.Y;
                    int dx = x - _label.X;
                    _label.X = x; _label.Y = y;
                    _valueLabel.X += dx; _valueLabel.Y += dy;
                    _minusButton.X += dx; _minusButton.Y += dy;
                    _plusButton.X += dx; _plusButton.Y += dy;
                }
            }
        }

    }
}
