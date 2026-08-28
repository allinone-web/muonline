using System;
using System.IO;
using System.Linq;
using Client.Data.Texture;
using Client.Main.Content;
using Foundation;
using UIKit;

namespace MuIos
{
    [Register("AppDelegate")]
    class Program : UIApplicationDelegate
    {
        private static Client.Main.MuGame game;

        internal static void RunGame()
        {
            game = new Client.Main.MuGame();
            game.Run();
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main(string[] args)
        {
            UIApplication.Main(args, null, typeof(Program));
        }

        // 本機修改：原本只覆寫了 FinishedLaunching(UIApplication)，那是 iOS 6 就棄用的
        // application:didFinishLaunching:。Xamarin 的 UIApplicationDelegate 基底型別已經
        // 註冊了 application:didFinishLaunchingWithOptions:，所以 UIKit 只會呼叫後者，
        // 導致 RunGame() 從未執行 —— 表現為「進程活著、CPU 0%、停在白色啟動畫面」。
        public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
        {
            try
            {
                ConfigureDataPath();
                ConfigureDxtDecompression();
                ConfigureSafeArea();
                ConfigureBatteryReadout();
                // iOS 需要自己的文字輸入實作，否則點了輸入框不會有鍵盤（見 IosTextFieldControl）
                Client.Main.Controls.UI.TextFieldControl.ControlType = typeof(IosTextFieldControl);
                RunGame();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MuIos] RunGame failed: " + ex);
                throw;
            }

            return true;
        }

        /// <summary>
        /// 把 UIKit 的安全區域傳給 UiScaler。
        ///
        /// iPhone 的圓角與動態島會裁掉畫面邊緣，UI 若鋪滿整個 back buffer，
        /// 四角的元素會被切掉而且點不到 —— 實測 iPhone Air 上就是如此。
        /// SafeAreaInsets 的單位是 point，back buffer 是 pixel，需乘上 scale。
        /// </summary>
        private static void ConfigureSafeArea()
        {
            try
            {
                var window = UIApplication.SharedApplication?.Windows?.FirstOrDefault(w => w.IsKeyWindow)
                             ?? UIApplication.SharedApplication?.Windows?.FirstOrDefault();
                if (window == null)
                    return;

                var insets = window.SafeAreaInsets;
                nfloat scale = window.Screen?.NativeScale ?? UIScreen.MainScreen.NativeScale;

                Client.Main.Controllers.UiScaler.SafeAreaInsets = new Microsoft.Xna.Framework.Vector4(
                    (float)(insets.Left * scale),
                    (float)(insets.Top * scale),
                    (float)(insets.Right * scale),
                    (float)(insets.Bottom * scale));

                Console.WriteLine(
                    $"[MuIos] SafeAreaInsets (px) L={insets.Left * scale} T={insets.Top * scale} " +
                    $"R={insets.Right * scale} B={insets.Bottom * scale}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MuIos] Failed to read safe area insets: {ex.Message}");
            }
        }

        /// <summary>
        /// 讓 HUD 的狀態列讀得到電量。讀電量需要 UIKit，Client.Main 不能直接引用，
        /// 因此以委派的方式由平台端提供（見 MobileUi.BatteryLevelProvider）。
        /// </summary>
        private static void ConfigureBatteryReadout()
        {
            try
            {
                UIDevice.CurrentDevice.BatteryMonitoringEnabled = true;
                // BatteryLevel 在讀不到時回傳 -1，正好符合「負數 = 未知」的約定
                Client.Main.Controls.UI.MobileUi.BatteryLevelProvider =
                    () => UIDevice.CurrentDevice.BatteryLevel;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MuIos] Battery monitoring unavailable: {ex.Message}");
            }
        }

        /// <summary>
        /// MU 的 .OZD 貼圖是 DXT（S3TC）壓縮的 DDS。iOS 的 GPU 只支援
        /// PVRTC / ETC / ASTC，<b>不支援 DXT</b>，因此以 SurfaceFormat.Dxt1/3/5
        /// 建立 Texture2D 會失敗，貼圖變成 null，而 ModelObject 對沒有貼圖的網格
        /// 是直接跳過不繪製的。
        ///
        /// 實際表現：戰士看不到腿、NPC 只剩一顆頭、地形貼圖 38 張只成功 14 張、
        /// 草地貼圖索引為空 —— 全部同一個原因。
        ///
        /// Android 早就以 CustomDecompressFunction 在軟體端解壓 DXT
        /// （MuAndroid/MainActivity.cs），iOS 卻沒有對應處理。這裡補上，
        /// 共用 Client.Main 既有的 DxtDecoder。
        /// </summary>
        private static void ConfigureDxtDecompression()
        {
            TextureLoader.Instance.CustomDecompressFunction = textureInfo => textureInfo.Format switch
            {
                TextureSurfaceFormat.Dxt1 => DxtDecoder.DecompressDXT1(textureInfo.Data, textureInfo.Width, textureInfo.Height),
                TextureSurfaceFormat.Dxt3 => DxtDecoder.DecompressDXT3(textureInfo.Data, textureInfo.Width, textureInfo.Height),
                TextureSurfaceFormat.Dxt5 => DxtDecoder.DecompressDXT5(textureInfo.Data, textureInfo.Width, textureInfo.Height),
                _ => throw new NotSupportedException($"Unsupported DXT format for decompression: {textureInfo.Format}"),
            };

            Console.WriteLine("[MuIos] DXT software decompression enabled (iOS GPUs cannot sample S3TC).");
        }

        // Constants 預設把 DataPath 指到 AppDomain.BaseDirectory/Data，也就是 .app bundle。
        // 那在模擬器上可行（可以把 Data 軟連結進已安裝的 bundle），但真機的 bundle 唯讀且
        // 已簽名，2.5 GB 的遊戲資源既放不進去也無法在安裝後寫入。
        // 因此：bundle 內有資源就用它，否則改用容器中可寫入的 Documents/Data。
        private static void ConfigureDataPath()
        {
            var bundleData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            if (Directory.Exists(bundleData) && Directory.EnumerateFileSystemEntries(bundleData).Any())
            {
                Client.Main.Constants.DataPath = bundleData;
                Console.WriteLine($"[MuIos] DataPath (bundle) = {bundleData}");
                return;
            }

            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var containerData = Path.Combine(documents, "Data");
            Directory.CreateDirectory(containerData);
            Client.Main.Constants.DataPath = containerData;
            Console.WriteLine($"[MuIos] DataPath (container) = {containerData}");
        }
    }
}
