using System;
using System.IO;
using System.Linq;
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
