using Client.Main.Controllers;
using Client.Main.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Controls.UI.Game
{
    public class LoadingScreenControl : GameControl
    {
        private SpriteFont _font;
        private string _pendingMessage = "Loading...";
        private float _progress = 0f; // Value from 0 to 1


        public string Message
        {
            get => _pendingMessage;
            set => _pendingMessage = value ?? "Loading...";
        }

        public float Progress
        {
            get => _progress;
            set => _progress = MathHelper.Clamp(value, 0f, 1f);
        }

        public override async Task Load()
        {
            _font = GraphicsManager.Instance.Font;
            await base.Load();
        }



        public override void Draw(GameTime gameTime)
        {
            if (!Visible) return;

            var gd = GraphicsManager.Instance.GraphicsDevice;
            var spriteBatch = GraphicsManager.Instance.Sprite;

            using (new SpriteBatchScope(
                spriteBatch,
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.LinearClamp,
                DepthStencilState.None,
                RasterizerState.CullNone,
                transform: UiScaler.SpriteTransform))
            {
                // Background Dim
                spriteBatch.Draw(
                    GraphicsManager.Instance.Pixel,
                    new Rectangle(0, 0, UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y),
                    Color.Black * 0.75f); // Semi-transparent black background

                // 訊息與進度條<b>不在這裡畫</b>。
                //
                // 這個控制項和 ProgressBarControl 幾乎總是成對出現
                // （SelectCharacterScene 兩個都加，GameScene 也是），而兩邊各自
                // 畫一條進度條加一份文字 —— 畫面下緣因此有兩條相差 10 px、
                // 寬度還不一樣的進度條，文字也印兩次。使用者看到的就是「兩套載入百分比」。
                //
                // 分工改成：這個控制項只負責<b>壓暗背景</b>，
                // 進度條、百分比與狀態文字一律由 ProgressBarControl 畫。
                // Message / Progress 兩個屬性保留 —— 它們仍然是資料來源，
                // GameScene 與 SelectCharacterScene 從這裡讀值餵給 ProgressBarControl。
            }

            // We call base.Draw(gameTime) if LoadingScreenControl itself might have child controls
            // For now, it's simple, so it might not be strictly necessary.
            // base.Draw(gameTime); 
        }
    }
}
