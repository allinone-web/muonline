using Client.Main.Controls.UI.Common;
using Client.Main.Controllers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace Client.Main.Controls.UI.Login
{
    /// <summary>
    /// 登入對話框。
    ///
    /// 版面與 <see cref="MobileServerListControl"/>、選角清單統一：半透明深色面板、
    /// 一條細邊框、白灰兩色文字，全部用程式繪製。原本繼承 PopupFieldDialog 的九宮格
    /// 外框（Interface/GFx/popupfield*.ozd）與輸入框底圖在手機上放大後都會糊掉，
    /// 而且是很明顯的 Windows 風格 —— 見 docs/待清理素材.md。
    /// </summary>
    public class LoginDialog : DialogControl
    {
        // ── 版面（設計單位，實際尺寸 = 設計單位 x Scale）──
        private const int DesignWidth = 320;
        // 224 -> 262：底部多留一行給登入結果訊息（見 ShowMessage）。
        private const int DesignHeight = 262;
        private const int TitleHeight = 40;
        private const int SidePadding = 20;
        private const int FieldHeight = 30;

        private readonly float _scale;
        private int S(float value) => (int)MathF.Round(value * _scale);

        private readonly TextFieldControl _userInput;
        private readonly TextFieldControl _passwordInput;
        private readonly ButtonControl _submitButton;

        private string _serverName = string.Empty;

        // 登入結果訊息。原本是跳一個 MessageWindow ——
        // 在手機上為了一句「帳號或密碼錯誤」而彈出一個要再點一次才能關掉的視窗，
        // 只是把重試變成三個動作。訊息直接寫在按鈕下面，看完就可以繼續打字。
        private string _message = string.Empty;
        private Color _messageColor = MobileUi.TextDim;

        // 送出登入後到伺服器回應之間約 1-2 秒沒有任何回饋，實測使用者會以為沒反應
        // 而重複點擊 —— 第一次其實已經登入成功，第二次就收到 AccountAlreadyConnected。
        private bool _isSubmitting;
        private double _submitElapsedSeconds;

        /// <summary>逾時保護。封包遺失或伺服器無回應時，避免按鈕永久鎖死。</summary>
        private const double SubmitTimeoutSeconds = 15.0;

        public string ServerName
        {
            get => _serverName;
            set => _serverName = value ?? string.Empty;
        }

        public string Username => _userInput.Value;
        public string Password => _passwordInput.Value;

        public event EventHandler LoginAttempt;

        public LoginDialog()
        {
            _scale = MobileUi.IsMobile ? 2.0f : 1f;

            AutoViewSize = false;
            ControlSize = new Point(S(DesignWidth), S(DesignHeight));
            ViewSize = ControlSize;
            BackgroundColor = Color.Transparent;
            Interactive = true;

            int fieldX = S(SidePadding);
            int fieldWidth = S(DesignWidth - SidePadding * 2);

            _userInput = CreateField(fieldX, S(88), fieldWidth, masked: false);
            _passwordInput = CreateField(fieldX, S(144), fieldWidth, masked: true);

            _passwordInput.ValueChanged += PasswordInput_EnterPressed;
            Controls.Add(_userInput);
            Controls.Add(_passwordInput);

            _userInput.Click += (s, e) => { _userInput.OnFocus(); _passwordInput.OnBlur(); };
            _passwordInput.Click += (s, e) => { _passwordInput.OnFocus(); _userInput.OnBlur(); };

            _submitButton = new ButtonControl
            {
                Text = "LOGIN",
                // 字級<b>不</b>乘上面板的設計倍率。
                //
                // 虛擬像素在整個 app 裡是絕對的：登入畫面的 13*2=26 px 標題和
                // 遊戲內視窗的 21 px 標題並排比較時就是兩種大小，而它們是同一個
                // 層級的東西。版面可以放大兩倍（元素少、要聚焦），但文字級距
                // 必須跟全app一致，見 MobileUi 的文字級距。
                FontSize = MobileUi.TextHeading,
                AutoViewSize = false,
                ViewSize = new Point(fieldWidth, S(34)),
                X = fieldX,
                Y = S(188),
                BackgroundColor = new Color(52, 62, 78) * 0.95f,
                HoverBackgroundColor = new Color(72, 86, 106) * 0.95f,
                PressedBackgroundColor = new Color(34, 42, 54) * 0.95f,
                TextColor = MobileUi.TextPrimary,
                HoverTextColor = Color.White,
                DisabledTextColor = MobileUi.TextDim,
                Interactive = true,
                BorderThickness = 1,
                BorderColor = MobileUi.PanelBorder * 0.6f
            };
            _submitButton.Click += (s, e) => AttemptLogin();
            Controls.Add(_submitButton);

            // 帶入上次登入的帳號與密碼。手機上每次都要叫出系統鍵盤重打非常麻煩。
            // 密碼的保存方式與安全性取捨見 MuGame.PersistLoginCredentials。
            var lastUser = MuGame.LoadLastUsername();
            if (!string.IsNullOrEmpty(lastUser))
                _userInput.Value = lastUser;

            var lastPassword = MuGame.LoadLastPassword();
            if (!string.IsNullOrEmpty(lastPassword))
                _passwordInput.Value = lastPassword;
        }

        private TextFieldControl CreateField(int x, int y, int width, bool masked)
        {
            var field = TextFieldControl.Create();
            field.X = x;
            field.Y = y;
            field.AutoViewSize = false;
            field.ViewSize = new Point(width, S(FieldHeight));
            field.MaskValue = masked;

            // Flat 皮膚 = 純程式繪製，任何尺寸都銳利。
            field.Skin = TextFieldSkin.Flat;
            field.FontSize = MobileUi.TextBody;
            field.BackgroundColor = MobileUi.FieldFill * 0.96f;
            field.BorderColor = MobileUi.PanelBorder * 0.55f;
            field.BorderThickness = 2;

            // 預設是白字，但深色底上一定要明確指定 —— 先前沿用素材皮膚時是黑字，
            // 配上深色背景幾乎看不見。
            field.TextColor = MobileUi.TextPrimary;

            return field;
        }

        public void FocusUsername()
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                _userInput?.OnFocus();
                _passwordInput?.OnBlur();
            });
        }

        private void SetSubmitting(bool submitting)
        {
            _isSubmitting = submitting;
            _submitElapsedSeconds = 0;

            if (_submitButton != null)
            {
                _submitButton.Interactive = !submitting;
                _submitButton.Enabled = !submitting;
                _submitButton.Text = submitting ? "..." : "LOGIN";
            }
        }

        /// <summary>由 LoginScene 在登入失敗、連線錯誤或狀態回復時呼叫，解除送出鎖定。</summary>
        public void ResetSubmitState() => SetSubmitting(false);

        /// <summary>
        /// 在按鈕下方顯示一行訊息，並解除送出鎖定讓使用者可以直接重試。
        /// </summary>
        public void ShowMessage(string text, bool isError = true)
        {
            _message = text ?? string.Empty;
            _messageColor = isError ? new Color(226, 116, 108) : MobileUi.TextDim;
            SetSubmitting(false);
        }

        /// <summary>使用者重新輸入時清掉上一次的錯誤訊息。</summary>
        public void ClearMessage() => _message = string.Empty;

        public override void Update(GameTime gameTime)
        {
            // 逾時保護：伺服器沒有回應時解除鎖定，否則按鈕會永久按不下去
            if (_isSubmitting)
            {
                _submitElapsedSeconds += gameTime.ElapsedGameTime.TotalSeconds;
                if (_submitElapsedSeconds >= SubmitTimeoutSeconds)
                    SetSubmitting(false);
            }

            // 實體鍵盤的 Tab 切換欄位（桌面／外接鍵盤）
            if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Tab) && MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Tab))
            {
                if (_userInput.IsFocused)
                {
                    _userInput.OnBlur();
                    _passwordInput.OnFocus();
                }
                else if (_passwordInput.IsFocused)
                {
                    _passwordInput.OnBlur();
                    _userInput.OnFocus();
                }
            }

            base.Update(gameTime);
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            var sprite = GraphicsManager.Instance.Sprite;
            var font = GraphicsManager.Instance.Font;
            if (sprite == null || font == null)
                return;

            var rect = DisplayRectangle;
            MobileUi.DrawPanel(sprite, rect, S(TitleHeight));

            DrawCentered(sprite, font, "SIGN IN", rect, S(12), MobileUi.ScaleFor(MobileUi.TextTitle), MobileUi.TextPrimary);

            // 伺服器名稱放在標題列<b>同一行</b>的右側，而不是自己佔一行。
            // 它是「我要登入哪裡」的補充說明，和標題是同一件事。
            if (!string.IsNullOrEmpty(_serverName))
            {
                float serverScale = MobileUi.ScaleFor(MobileUi.TextLabel);
                var serverSize = font.MeasureString(_serverName) * serverScale;
                var serverPos = new Vector2(
                    rect.Right - S(SidePadding) - serverSize.X,
                    rect.Y + S(TitleHeight) * 0.5f - serverSize.Y * 0.5f);
                sprite.DrawString(font, _serverName, serverPos + Vector2.One, Color.Black * 0.7f,
                                  0f, Vector2.Zero, serverScale, SpriteEffects.None, 0f);
                sprite.DrawString(font, _serverName, serverPos, MobileUi.TextDim,
                                  0f, Vector2.Zero, serverScale, SpriteEffects.None, 0f);
            }

            DrawLabel(sprite, font, "ACCOUNT", rect, S(SidePadding), S(70));
            DrawLabel(sprite, font, "PASSWORD", rect, S(SidePadding), S(126));

            if (!string.IsNullOrEmpty(_message))
            {
                float messageScale = MobileUi.ScaleFor(MobileUi.TextLabel);
                var lines = WrapMessage(font, _message, rect.Width - S(SidePadding) * 2, messageScale);
                float y = rect.Y + S(230);
                foreach (var line in lines)
                {
                    var size = font.MeasureString(line) * messageScale;
                    var pos = new Vector2(rect.X + (rect.Width - size.X) * 0.5f, y);
                    sprite.DrawString(font, line, pos + Vector2.One, Color.Black * 0.7f,
                                      0f, Vector2.Zero, messageScale, SpriteEffects.None, 0f);
                    sprite.DrawString(font, line, pos, _messageColor,
                                      0f, Vector2.Zero, messageScale, SpriteEffects.None, 0f);
                    y += size.Y + S(2);
                }
            }

            base.Draw(gameTime);
        }

        /// <summary>訊息可能比面板寬（伺服器的錯誤字串沒有長度上限），按空白斷行。</summary>
        private static System.Collections.Generic.List<string> WrapMessage(SpriteFont font, string text, int maxWidth, float scale)
        {
            var lines = new System.Collections.Generic.List<string>();
            var current = string.Empty;

            foreach (var word in text.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (font.MeasureString(candidate).X * scale <= maxWidth || current.Length == 0)
                {
                    current = candidate;
                }
                else
                {
                    lines.Add(current);
                    current = word;
                }

                // 兩行就夠了；再長的訊息不會有人在登入畫面讀完
                if (lines.Count == 2)
                    break;
            }

            if (lines.Count < 2 && current.Length > 0)
                lines.Add(current);

            return lines;
        }

        private void DrawCentered(SpriteBatch sprite, SpriteFont font, string text, Rectangle rect, int offsetY, float scale, Color color)
        {
            var size = font.MeasureString(text) * scale;
            var position = new Vector2(rect.X + (rect.Width - size.X) * 0.5f, rect.Y + offsetY);
            sprite.DrawString(font, text, position + Vector2.One, Color.Black * 0.7f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sprite.DrawString(font, text, position, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private void DrawLabel(SpriteBatch sprite, SpriteFont font, string text, Rectangle rect, int offsetX, int offsetY)
        {
            float scale = MobileUi.ScaleFor(MobileUi.TextLabel);
            var position = new Vector2(rect.X + offsetX, rect.Y + offsetY);
            sprite.DrawString(font, text, position, MobileUi.TextDim, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private void PasswordInput_EnterPressed(object sender, EventArgs e)
        {
            // ValueChanged 在每次文字變動時都會觸發，這裡只認 Enter
            bool enterPressed = MuGame.Instance.Keyboard.IsKeyDown(Keys.Enter) &&
                                MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Enter);
            if (enterPressed)
                AttemptLogin();
        }

        private void AttemptLogin()
        {
#if IOS
            Console.WriteLine($"[MuIos.Login] AttemptLogin user='{_userInput.Value}' passwordLength={_passwordInput.Value?.Length ?? 0}");
#endif
            if (_isSubmitting)
                return;

            SetSubmitting(true);
            MuGame.PersistLoginCredentials(_userInput.Value, _passwordInput.Value);

            // 送出後收起軟鍵盤
            _userInput.OnBlur();
            _passwordInput.OnBlur();
            if (Scene != null && (Scene.FocusControl == _userInput || Scene.FocusControl == _passwordInput))
                Scene.FocusControl = null;

            LoginAttempt?.Invoke(this, EventArgs.Empty);
        }
    }
}
