using System;
using System.Linq;
using Client.Main;
using Client.Main.Controllers;
using Client.Main.Controls.UI;
using CoreGraphics;
using Foundation;
using UIKit;

namespace MuIos
{
    /// <summary>
    /// iOS 版的文字輸入欄位。
    ///
    /// 基底的 <see cref="TextFieldControl"/> 只有兩條輸入路徑：Android 的軟鍵盤，
    /// 以及 <c>#if !ANDROID</c> 的桌面實體鍵盤輪詢。iOS 兩者皆不適用 —— 它會落到
    /// 桌面分支去輪詢一個不存在的實體鍵盤，結果就是「點了輸入框但鍵盤不會出現」。
    ///
    /// <b>這裡不用 MonoGame 的 <c>KeyboardInput.Show</c>。</b>那個 API 會彈出一個
    /// 系統的 UIAlertController，玩家看到的是一個灰白色、標題寫著「輸入」的 iOS
    /// 對話框蓋在遊戲上 —— 手機遊戲不會這樣做，而且那個對話框的輸入框無法設定
    /// 自動大寫：帳號打 <c>test3</c> 會被改成 <c>Test3</c>，登入必然失敗。
    ///
    /// 改用手機遊戲的標準做法：放一個看不見的 <see cref="UITextField"/> 到畫面上
    /// 讓它成為 first responder，系統鍵盤照常升起，而文字仍然畫在遊戲自己的輸入框裡
    /// （由基底的 <c>Draw</c> 負責）。鍵盤高度回報給 <see cref="MobileUi.KeyboardHeight"/>，
    /// 基底再據此把被蓋住的視窗往上挪。
    ///
    /// 由 <c>Program.FinishedLaunching</c> 透過 <c>TextFieldControl.ControlType</c> 注入。
    /// </summary>
    public class IosTextFieldControl : TextFieldControl
    {
        private UITextField _native;
        private NSObject _keyboardFrameObserver;
        private NSObject _keyboardHideObserver;

        // 密碼欄位不把明碼寫進日誌，只記長度
        private string Describe(string value)
        {
            if (value is null) return "<null>";
            return MaskValue ? $"<{value.Length} chars>" : value;
        }

        public override void OnFocus()
        {
            bool alreadyFocused = IsFocused;
            base.OnFocus();

            if (alreadyFocused || _native != null)
                return;

            Console.WriteLine($"[MuIos.Keyboard] open  label='{Label}' mask={MaskValue} current='{Describe(Value)}'");
            AttachNativeField();
        }

        public override void OnBlur()
        {
            if (!IsFocused)
                return;

            base.OnBlur();
            DetachNativeField();
        }

        private static UIWindow KeyWindow =>
            UIApplication.SharedApplication?.Windows?.FirstOrDefault(w => w.IsKeyWindow)
            ?? UIApplication.SharedApplication?.Windows?.FirstOrDefault();

        private void AttachNativeField()
        {
            var window = KeyWindow;
            if (window == null)
            {
                Console.WriteLine("[MuIos.Keyboard] no key window — cannot show keyboard");
                return;
            }

            // 完全透明，但<b>不是</b> Hidden、也不是 alpha 0 ——
            // 那兩種狀態下的 view 無法成為 first responder，鍵盤不會升起。
            _native = new UITextField(new CGRect(0, 0, 1, 1))
            {
                // 帳號不是句子。預設的 Sentences 會把第一個字母改成大寫，
                // test3 變成 Test3，而 OpenMU 的帳號比對是區分大小寫的。
                AutocapitalizationType = UITextAutocapitalizationType.None,
                AutocorrectionType = UITextAutocorrectionType.No,
                SpellCheckingType = UITextSpellCheckingType.No,
                SmartQuotesType = UITextSmartQuotesType.No,
                SmartDashesType = UITextSmartDashesType.No,
                SmartInsertDeleteType = UITextSmartInsertDeleteType.No,
                KeyboardType = UIKeyboardType.ASCIICapable,
                ReturnKeyType = UIReturnKeyType.Done,
                SecureTextEntry = MaskValue,
                Text = Value ?? string.Empty,
                TextColor = UIColor.Clear,
                TintColor = UIColor.Clear,
                BackgroundColor = UIColor.Clear
            };

            // 空字串的 TextContentType 會關掉「自動填入密碼」那條黃色提示列。
            _native.TextContentType = new NSString(string.Empty);

            _native.EditingChanged += NativeEditingChanged;
            _native.ShouldReturn = NativeShouldReturn;

            window.AddSubview(_native);
            _native.BecomeFirstResponder();

            _keyboardFrameObserver = UIKeyboard.Notifications.ObserveWillChangeFrame(KeyboardFrameChanged);
            _keyboardHideObserver = UIKeyboard.Notifications.ObserveWillHide((s, e) => MobileUi.KeyboardHeight = 0f);
        }

        private void DetachNativeField()
        {
            _keyboardFrameObserver?.Dispose();
            _keyboardFrameObserver = null;
            _keyboardHideObserver?.Dispose();
            _keyboardHideObserver = null;
            MobileUi.KeyboardHeight = 0f;

            if (_native == null)
                return;

            Console.WriteLine($"[MuIos.Keyboard] close label='{Label}' value='{Describe(Value)}'");

            _native.EditingChanged -= NativeEditingChanged;
            _native.ShouldReturn = null;
            _native.ResignFirstResponder();
            _native.RemoveFromSuperview();
            _native.Dispose();
            _native = null;
        }

        private void NativeEditingChanged(object sender, EventArgs e)
        {
            string text = _native?.Text ?? string.Empty;

            // UIKit 的事件雖然也在主執行緒上，但排到下一幀才改狀態，
            // 才不會在 Update/Draw 跑到一半的時候把文字換掉。
            MuGame.ScheduleOnMainThread(() =>
            {
                if (Value == text)
                    return;

                Value = text;
                OnValueChanged();
            });
        }

        private bool NativeShouldReturn(UITextField field)
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                OnEnterKeyPressed();
                Blur();
            });
            return true;
        }

        /// <summary>
        /// 把鍵盤蓋住的高度換算成虛擬像素。
        ///
        /// iOS 8 之後鍵盤通知的座標已經是「目前介面方向」下的座標，因此橫置時
        /// 直接拿 window 的高度去減就是正確的遮蔽高度，不需要再自己轉一次。
        /// </summary>
        private static void KeyboardFrameChanged(object sender, UIKeyboardEventArgs e)
        {
            try
            {
                var window = KeyWindow;
                if (window == null)
                    return;

                nfloat covered = window.Bounds.Height - e.FrameEnd.Y;
                if (covered < 0)
                    covered = 0;

                nfloat scale = window.Screen?.NativeScale ?? UIScreen.MainScreen.NativeScale;
                MobileUi.KeyboardHeight = (float)(covered * scale) * UiScaler.InverseScaleY;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MuIos.Keyboard] frame read failed: {ex.Message}");
            }
        }
    }
}
