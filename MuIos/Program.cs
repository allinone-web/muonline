using System;
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
                RunGame();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MuIos] RunGame failed: " + ex);
                throw;
            }

            return true;
        }
    }
}
