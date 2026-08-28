using System;
using System.Threading.Tasks;
using Client.Main;
using Client.Main.Controls.UI;
using Microsoft.Xna.Framework.Input;

namespace MuIos
{
    /// <summary>
    /// iOS 版的文字輸入欄位。
    ///
    /// 基底的 <see cref="TextFieldControl"/> 只有兩條輸入路徑：Android 的軟鍵盤，
    /// 以及 <c>#if !ANDROID</c> 的桌面實體鍵盤輪詢。iOS 兩者皆不適用 —— 它會落到
    /// 桌面分支去輪詢一個不存在的實體鍵盤，結果就是「點了輸入框但鍵盤never出現，
    /// 一個字也打不了」。
    ///
    /// 這裡比照 <c>AndroidTextFieldControl</c> 的做法，改用 MonoGame 的
    /// <see cref="KeyboardInput"/>（iOS 後端內部以 UITextField 實作，原生支援密碼模式）。
    /// 由 <c>Program.FinishedLaunching</c> 透過 <c>TextFieldControl.ControlType</c> 注入。
    /// </summary>
    public class IosTextFieldControl : TextFieldControl
    {
        private bool _dialogOpen;

        // 密碼欄位不把明碼寫進日誌，只記長度
        private string Describe(string value)
        {
            if (value is null) return "<null>";
            return MaskValue ? $"<{value.Length} chars>" : value;
        }

        public override void OnFocus()
        {
            base.OnFocus();

            // 避免連續點擊時疊出多個系統對話框
            if (_dialogOpen)
            {
                return;
            }

            _dialogOpen = true;
            Console.WriteLine($"[MuIos.Keyboard] open  label='{Label}' mask={MaskValue} current='{Describe(Value)}'");

            _ = ShowKeyboardAsync();
        }

        private async Task ShowKeyboardAsync()
        {
            string result = null;

            try
            {
                result = await KeyboardInput.Show(
                    title: string.IsNullOrEmpty(Label) ? "輸入" : Label,
                    description: Placeholder ?? string.Empty,
                    defaultText: Value ?? string.Empty,
                    usePasswordMode: MaskValue);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MuIos] KeyboardInput failed: " + ex);
            }
            finally
            {
                _dialogOpen = false;
            }

            // 對話框的 callback 不在遊戲執行緒上。UI 狀態必須回到主執行緒再改，
            // 否則會與 Update/Draw 迴圈競爭。（Android 版沒做這件事，是潛在的 race。）
            MuGame.ScheduleOnMainThread(() =>
            {
                if (result is null)
                {
                    Console.WriteLine($"[MuIos.Keyboard] cancel label='{Label}' keeping='{Describe(Value)}'");
                    Blur();
                    return;
                }

                Value = result;
                OnValueChanged();
                Console.WriteLine($"[MuIos.Keyboard] set   label='{Label}' value='{Describe(result)}'");
                Blur();
            });
        }
    }
}
