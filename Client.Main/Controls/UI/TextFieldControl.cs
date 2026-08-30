using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Client.Main.Controls.UI
{
    public enum TextFieldSkin
    {
        /// <summary>
        /// 純程式繪製，也是目前唯一的皮膚。
        ///
        /// 原本還有一種九宮格貼圖皮膚（Interface/GFx/textbg01-09.ozd），
        /// 那是為 1024x768 的桌面畫的，在 3x 的手機螢幕上放大必糊，
        /// 而且與其他已經改成程式繪製的面板風格不一致，已整組移除。
        /// 見 docs/待清理素材.md。
        /// </summary>
        Flat
    }

    public class TextFieldControl : UIControl, IUiTexturePreloadable
    {
        public static Type ControlType = typeof(TextFieldControl);

        public static TextFieldControl Create()
        {
            return (TextFieldControl)Activator.CreateInstance(ControlType, true);
        }

        protected readonly StringBuilder _inputText = new();
        private double _cursorBlinkTimer;
        private bool _showCursor;
        private float _scrollOffset;
        private string _cachedValue = string.Empty;
        private string _cachedMaskedValue = string.Empty;
        private bool _textCacheDirty;
        private bool _scrollMetricsDirty = true;
        private int _scrollMetricsWidth = -1;
        private float _scrollMetricsFontSize = float.NaN;
        private bool _scrollMetricsMaskValue;

        /// <summary>
        /// 文字與輸入框左右邊界的距離。
        ///
        /// 桌面的 5 px 是配著 1280 寬的版面畫的，在手機上換算不到 2 pt ——
        /// 字幾乎貼在框線上，看起來像是排版壞掉。手機用 18：字級大了三倍，
        /// 內距也要跟著放大，文字才「住在」框裡而不是「黏在」框上。
        /// </summary>
        private static int TextMargin => MobileUi.IsMobile ? 18 : 5;
        private const int CursorBlinkInterval = 500;

        private static readonly RasterizerState s_scissorRasterizerState = new()
        {
            ScissorTestEnable = true
        };

        private static readonly ILogger _logger = MuGame.AppLoggerFactory?.CreateLogger<TextFieldControl>();

        public TextFieldSkin Skin { get; set; } = TextFieldSkin.Flat;
        public Color TextColor { get; set; } = Color.White;
        public float FontSize { get; set; } = 12f;
        public bool IsFocused { get; private set; }
        public string Label { get; set; }
        public string Placeholder { get; set; }

        public string Value
        {
            get => GetCachedValue();
            set
            {
                _inputText.Clear();
                _inputText.Append(value ?? string.Empty);
                MarkTextChanged();
                MoveCursorToEnd();
            }
        }

        public bool MaskValue { get; set; }
        public event EventHandler ValueChanged;
        public event EventHandler EnterKeyPressed;

        protected TextFieldControl()
        {
            AutoViewSize = false;
            ViewSize = new Point(176, 14);
            Interactive = true;
            IsFocused = false;
        }

        public IEnumerable<string> GetPreloadTexturePaths()
        {
            yield break;   // 輸入框已無貼圖依賴
        }

        public override void OnFocus()
        {
            if (IsFocused) return;
            base.OnFocus();
            IsFocused = true;
            _showCursor = true;
            _cursorBlinkTimer = 0;
            if (Scene != null) Scene.FocusControl = this;

            _logger?.LogDebug("TextFieldControl: OnFocus called. Subscribing to TextInput.");
        }

        public override void OnBlur()
        {
            if (!IsFocused) return;
            base.OnBlur();
            IsFocused = false;
            _showCursor = false;
            _cursorBlinkTimer = 0;

            // 失焦後這個控制項不一定還會被 Update（視窗可能同時被關掉），
            // 位移必須在這裡就還原，否則會永久留在畫面上。
            if (ReferenceEquals(s_shiftOwner, this))
                ReleaseKeyboardShift();

            _logger?.LogDebug("TextFieldControl: OnBlur called. Unsubscribing from TextInput.");

#if ANDROID
            AndroidKeyboard.TextInput -= OnTextInput;
            AndroidKeyboard.Hide();
#endif
        }

        public new void Focus() => OnFocus();
        public new void Blur() => OnBlur();

        // ─────────────────── 鍵盤避讓 ───────────────────
        //
        // 橫置時 iOS 鍵盤佔掉將近一半的畫面高度。登入面板是置中的，密碼欄與
        // LOGIN 鈕正好落在鍵盤底下 —— 玩家看不到自己打了什麼，也按不到送出。
        //
        // 位移寫在 Offset 而不是 Y：Y 每一幀都會被 AlignControl() 依對齊方式重算，
        // 寫進去等於沒寫；Offset 是加在最後的顯示座標上的，不受對齊影響。
        //
        // 同一時間只有一個欄位是聚焦的，而同一個視窗裡的欄位共用一份位移，
        // 因此狀態是靜態的：換欄位時沿用同一份，換視窗時先把舊的還原。

        /// <summary>目前被挪動的視窗，null = 沒有任何視窗被挪動。</summary>
        private static GameControl s_shiftedWindow;

        /// <summary>
        /// 造成目前這份位移的欄位。只有它可以還原 —— 否則同一個視窗裡的另一個欄位
        /// 會在同一幀先還原、再由聚焦中的欄位重新套用，面板每一幀跳一次。
        /// </summary>
        private static TextFieldControl s_shiftOwner;

        /// <summary>挪動前的 Offset.Y，還原時用。</summary>
        private static int s_shiftBaseOffsetY;

        /// <summary>目前挪了多少（正值 = 往上）。</summary>
        private static int s_shiftAmount;

        /// <summary>欄位下緣與鍵盤上緣之間至少要留的距離。</summary>
        private const int KeyboardGap = 24;

        /// <summary>視窗根 —— 直接掛在場景底下的那一層，也就是要整個挪動的東西。</summary>
        private GameControl WindowRoot()
        {
            GameControl control = this;
            while (control.Parent != null && control.Parent is not Client.Main.Scenes.BaseScene)
                control = control.Parent;
            return control;
        }

        private void UpdateKeyboardAvoidance()
        {
            if (!IsFocused || !Visible || MobileUi.KeyboardHeight <= 0f)
            {
                if (ReferenceEquals(s_shiftOwner, this))
                    ReleaseKeyboardShift();
                return;
            }

            var window = WindowRoot();
            if (window == null)
                return;

            if (!ReferenceEquals(window, s_shiftedWindow))
            {
                ReleaseKeyboardShift();
                s_shiftedWindow = window;
                s_shiftBaseOffsetY = window.Offset.Y;
                s_shiftAmount = 0;
            }

            s_shiftOwner = this;

            int keyboardTop = UiScaler.VirtualSize.Y - (int)MathF.Ceiling(MobileUi.KeyboardHeight);

            // DisplayRectangle 已經含了目前的位移，先加回去換算成「沒挪動時」的位置，
            // 否則每一幀都會在上一幀的結果上再挪一次。
            int bottomIfUnshifted = DisplayRectangle.Bottom + s_shiftAmount;

            // 上限是把視窗頂到畫面最上緣為止 —— 再往上就整個看不到了。
            int maxShift = Math.Max(0, window.DisplayRectangle.Y + s_shiftAmount);
            int desired = Math.Clamp(bottomIfUnshifted + KeyboardGap - keyboardTop, 0, maxShift);

            if (desired == s_shiftAmount)
                return;

            s_shiftAmount = desired;
            window.Offset = new Point(window.Offset.X, s_shiftBaseOffsetY - desired);
        }

        private static void ReleaseKeyboardShift()
        {
            if (s_shiftedWindow == null)
                return;

            s_shiftedWindow.Offset = new Point(s_shiftedWindow.Offset.X, s_shiftBaseOffsetY);
            s_shiftedWindow = null;
            s_shiftOwner = null;
            s_shiftAmount = 0;
        }

        public void MoveCursorToEnd()
        {
            UpdateScrollOffset();
            if (IsFocused)
            {
                _showCursor = true;
                _cursorBlinkTimer = 0;
            }
        }

        protected void UpdateScrollOffset()
        {
            SpriteFont font = GraphicsManager.GetUiFont(FontSize, out float scaleFactor);
            if (font == null) return;

            string textToDisplay = GetDisplayText();
            var textWidth = font.MeasureString(textToDisplay).X * scaleFactor;
            float maxVisibleWidth = DisplayRectangle.Width - TextMargin * 2;

            _scrollOffset = textWidth > maxVisibleWidth ? textWidth - maxVisibleWidth : 0;
            _scrollMetricsDirty = false;
            _scrollMetricsWidth = DisplayRectangle.Width;
            _scrollMetricsFontSize = FontSize;
            _scrollMetricsMaskValue = MaskValue;
        }

        private void EnsureScrollOffsetCurrent()
        {
            if (_scrollMetricsDirty ||
                _scrollMetricsWidth != DisplayRectangle.Width ||
                _scrollMetricsFontSize != FontSize ||
                _scrollMetricsMaskValue != MaskValue)
            {
                UpdateScrollOffset();
            }
        }

        private void MarkTextChanged()
        {
            _textCacheDirty = true;
            _scrollMetricsDirty = true;
        }

        private string GetCachedValue()
        {
            RefreshTextCache();
            return _cachedValue;
        }

        private string GetDisplayText()
        {
            RefreshTextCache();
            return MaskValue ? _cachedMaskedValue : _cachedValue;
        }

        private void RefreshTextCache()
        {
            if (!_textCacheDirty)
                return;

            _cachedValue = _inputText.ToString();
            _cachedMaskedValue = _inputText.Length == 0
                ? string.Empty
                : new string('*', _inputText.Length);
            _textCacheDirty = false;
        }

        protected void OnEnterKeyPressed()
        {
            EnterKeyPressed?.Invoke(this, EventArgs.Empty);
        }

        protected void OnValueChanged()
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Handles text input on Android (from soft keyboard or scrcpy).
        /// </summary>
#if ANDROID
        private void OnTextInput(object sender, Platform.Android.TextInputEventArgs e)
        {
            bool textChanged = false;

            // Handle control keys by character or key code
            if (e.Character == '\r' || e.Key == Keys.Enter)
            {
                EnterKeyPressed?.Invoke(this, EventArgs.Empty);
                ValueChanged?.Invoke(this, EventArgs.Empty);
                return; // Enter usually consumes the event
            }
            else if (e.Character == '\b' || e.Key == Keys.Back)
            {
                // Backspace - delete last character
                if (_inputText.Length > 0)
                {
                    _inputText.Remove(_inputText.Length - 1, 1);
                    MarkTextChanged();
                    textChanged = true;
                }
            }
            else if (e.Character != '\0' && !char.IsControl(e.Character))
            {
                // Standard printable character input
                _inputText.Append(e.Character);
                MarkTextChanged();
                textChanged = true;
            }

            if (textChanged)
            {
                MoveCursorToEnd();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
#endif

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (MobileUi.IsMobile)
                UpdateKeyboardAvoidance();

            if (!IsFocused || !Visible) return;

#if !ANDROID
            // On non-Android platforms (Windows, Linux, Mac), use keyboard polling
            KeyboardState keyboard = MuGame.Instance.Keyboard;
            KeyboardState previousKeyboard = MuGame.Instance.PrevKeyboard;
            bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            bool capsLock = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? Console.CapsLock : false;

            Keys[] inputKeys = DesktopTextInputKeys.All;
            for (int i = 0; i < inputKeys.Length; i++)
            {
                Keys key = inputKeys[i];
                if (keyboard.IsKeyDown(key) && previousKeyboard.IsKeyUp(key))
                    ProcessKey(key, shift, capsLock);
            }

            EnsureScrollOffsetCurrent();
#endif

            _cursorBlinkTimer += gameTime.ElapsedGameTime.TotalMilliseconds;
            if (_cursorBlinkTimer >= CursorBlinkInterval)
            {
                _showCursor = !_showCursor;
                _cursorBlinkTimer = 0;
            }
        }

#if !ANDROID
        // Keyboard input processing for Windows/Desktop platforms
        private void ProcessKey(Keys key, bool shift, bool capsLock)
        {
            bool textChanged = false;
            if (key == Keys.Back && _inputText.Length > 0)
            {
                _inputText.Remove(_inputText.Length - 1, 1);
                MarkTextChanged();
                textChanged = true;
            }
            else if (key == Keys.Enter)
            {
                EnterKeyPressed?.Invoke(this, EventArgs.Empty);
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                char character = KeyToChar(key, shift, capsLock);
                if (character != '\0')
                {
                    _inputText.Append(character);
                    MarkTextChanged();
                    textChanged = true;
                }
            }

            if (textChanged)
            {
                MoveCursorToEnd();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private char KeyToChar(Keys key, bool shift, bool capsLock)
        {
            if (key >= Keys.A && key <= Keys.Z)
            {
                bool isUpper = capsLock ^ shift;
                char letter = (char)('A' + (key - Keys.A));
                return isUpper ? letter : char.ToLower(letter);
            }
            else if (key >= Keys.D0 && key <= Keys.D9)
            {
                char digit = (char)('0' + (key - Keys.D0));
                if (shift)
                {
                    return key switch
                    {
                        Keys.D1 => '!',
                        Keys.D2 => '@',
                        Keys.D3 => '#',
                        Keys.D4 => '$',
                        Keys.D5 => '%',
                        Keys.D6 => '^',
                        Keys.D7 => '&',
                        Keys.D8 => '*',
                        Keys.D9 => '(',
                        Keys.D0 => ')',
                        _ => digit,
                    };
                }
                return digit;
            }
            else if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
            {
                return (char)('0' + (key - Keys.NumPad0));
            }
            return key switch
            {
                Keys.Space => ' ',
                Keys.OemComma => ',',
                Keys.OemPeriod => '.',
                Keys.OemMinus => shift ? '_' : '-',
                Keys.OemPlus => shift ? '+' : '=',
                Keys.OemQuestion => shift ? '?' : '/',
                Keys.OemOpenBrackets => shift ? '{' : '[',
                Keys.OemCloseBrackets => shift ? '}' : ']',
                Keys.OemPipe => shift ? '|' : '\\',
                Keys.OemTilde => shift ? '~' : '`',
                Keys.OemQuotes => shift ? '"' : '\'',
                Keys.OemSemicolon => shift ? ':' : ';',
                _ => '\0'
            };
        }
#endif

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            using (new SpriteBatchScope(
                GraphicsManager.Instance.Sprite,
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.LinearClamp,
                transform: UiScaler.SpriteTransform))
            {
                var spriteBatch = GraphicsManager.Instance.Sprite;

                DrawFlatBackground(spriteBatch);

                DrawTextAndCursor(spriteBatch);
            }

            // 這裡刻意<b>不呼叫 base.Draw</b>。
            //
            // GameControl.Draw 會再畫一次 DrawBackground() + DrawBorder()，而它是在
            // 上面的 SpriteBatchScope 關閉<b>之後</b>執行的 —— 等於把背景蓋在剛畫好的
            // 文字上面。BackgroundColor 是透明時看不出來（舊的 NineSlice 皮膚就是這樣，
            // 所以一直沒發現），一旦指定了不透明的底色，文字就會被蓋掉 96%，
            // 看起來像是「白字變成了很暗的灰字」。
            //
            // 輸入框沒有子控制項，base.Draw 對它只有這兩件事，直接不呼叫最乾淨。
        }

        private void DrawFlatBackground(SpriteBatch spriteBatch)
        {
            DrawBackground();
            DrawBorder();
        }

        private void DrawTextAndCursor(SpriteBatch spriteBatch)
        {
            SpriteFont font = GraphicsManager.GetUiFont(FontSize, out float scale);
            if (font == null) return;

            EnsureScrollOffsetCurrent();

            var gd = GraphicsManager.Instance.GraphicsDevice;
            var originalScissorRect = gd.ScissorRectangle;
            var area = new Rectangle(
                DisplayRectangle.X + TextMargin,
                DisplayRectangle.Y,
                Math.Max(0, DisplayRectangle.Width - TextMargin * 2),
                DisplayRectangle.Height
            );
            gd.ScissorRectangle = Rectangle.Intersect(originalScissorRect, area);
            gd.RasterizerState = s_scissorRasterizerState;

            string text = GetDisplayText();
            Vector2 textPos = new Vector2(DisplayRectangle.X + TextMargin - _scrollOffset,
                                          DisplayRectangle.Y + (DisplayRectangle.Height - font.MeasureString("A").Y * scale) / 2f);

            spriteBatch.DrawString(font, text, textPos, TextColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

            if (IsFocused && _showCursor)
            {
                float w = font.MeasureString(text).X * scale;
                var cursorPos = textPos + new Vector2(w, 0);
                if (cursorPos.X >= area.Left && cursorPos.X <= area.Right)
                {
                    spriteBatch.DrawString(font, "|", cursorPos, TextColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                }
            }

            gd.ScissorRectangle = originalScissorRect;
            gd.RasterizerState = RasterizerState.CullNone;
        }
    }
}
