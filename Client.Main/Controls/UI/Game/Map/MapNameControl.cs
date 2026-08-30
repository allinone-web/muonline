using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Client.Main.Controllers;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Map
{
    public class MapNameControl : TextureControl
    {
        private float _displayTimer = 0f;
        private LabelControl _label;

        public MapNameControl()
        {
            var layoutInfo = LoadLayoutInfo();
            var texRectData = LoadTextureRectData();

            if (layoutInfo != null)
            {
                X = (int)layoutInfo.ScreenX;
                Y = (int)layoutInfo.ScreenY;
                ViewSize = new Point(layoutInfo.Width, layoutInfo.Height);
            }

            if (texRectData != null)
            {
                TextureRectangle = new Rectangle(texRectData.X, texRectData.Y, texRectData.Width, texRectData.Height);
            }

            TexturePath = "Interface/GFx/MapName_I2.ozd";
            AutoViewSize = false;
            Visible = true;
            Alpha = 1f;

            _label = new LabelControl
            {
                FontSize = 24,
                TextColor = Color.WhiteSmoke,
                UseManualPosition = true,
                IsBold = false,
                IsItalic = true,
                HasUnderline = false
            };

            LabelText = "Map Name"; // Default

            UpdateLabelPosition();
        }

        public void ShowMapName(string mapName)
        {
            _displayTimer = 0f;
            Alpha = 1f;
            Visible = true;
            _label.Visible = true;
            LabelText = mapName ?? string.Empty;
        }

        public string LabelText
        {
            get => _label.Text;
            set
            {
                _label.Text = value;
                UpdateLabelPosition();
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            _displayTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (_displayTimer <= 5f)
            {
                Alpha = 1f;
            }
            else if (_displayTimer > 5f && _displayTimer <= 7f)
            {
                float fadeProgress = (_displayTimer - 5f) / 2f;
                Alpha = MathHelper.SmoothStep(1f, 0f, fadeProgress);
            }
            else
            {
                Alpha = 0f;
            }

            if (Alpha < 0.4f)
                _label.Visible = false;
            else
            {
                _label.Visible = true;
                _label.Alpha = Alpha / 2f;
            }

            UpdateLabelPosition();
        }


        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            var sb = GraphicsManager.Instance.Sprite;

            if (MobileUi.IsMobile)
            {
                // 進入地圖時的名稱橫幅。
                //
                // 上一版只畫了一塊方panel，把原本的樣式弄丟了。原版是一條中間亮、
                // 兩端淡出的橫幅 —— 那個造型是它好看的地方，不能省。
                // 這裡用程式重畫同一個造型（貼圖是為 1280 寬畫的，放大會糊）：
                //   1) 中間一條漸亮的底
                //   2) 上下各一條同樣兩端淡出的細線
                using (new SpriteBatchScope(
                       sb,
                       SpriteSortMode.Deferred,
                       BlendState.AlphaBlend,
                       SamplerState.LinearClamp,
                       transform: UiScaler.SpriteTransform))
                {
                    DrawMobileBanner(sb, DisplayRectangle, Alpha);
                }

                _label.Draw(gameTime);
                return;
            }

            if (Texture == null)
                return;

            using (new SpriteBatchScope(
                   sb,
                   SpriteSortMode.Deferred,
                   BlendState.NonPremultiplied,
                   SamplerState.PointClamp,
                   transform: UiScaler.SpriteTransform))
            {
                sb.Draw(Texture,
                        DisplayRectangle,
                        TextureRectangle,
                        Color.White * Alpha);
            }

            _label.Draw(gameTime);
        }

        /// <summary>
        /// 兩端淡出的橫幅。中央最亮，往左右各自收成透明 —— 沒有硬邊，
        /// 所以它不像一個「視窗」，而像一條浮在畫面上的標題。
        /// </summary>
        private static void DrawMobileBanner(SpriteBatch sb, Rectangle rect, float alpha)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null || rect.Width <= 0)
                return;

            const int steps = 48;
            int sliceWidth = Math.Max(1, rect.Width / steps);

            for (int i = 0; i < steps; i++)
            {
                int x = rect.X + i * sliceWidth;
                int w = (i == steps - 1) ? rect.Right - x : sliceWidth;
                if (w <= 0)
                    continue;

                // 距離中心 0..1，用 1 - d^2 收邊，中央保持亮、兩端收得快
                float d = MathF.Abs((i + 0.5f) / steps * 2f - 1f);
                float falloff = MathHelper.Clamp(1f - d * d, 0f, 1f);

                sb.Draw(pixel, new Rectangle(x, rect.Y, w, rect.Height),
                        MobileUi.PanelFill * (0.82f * falloff * alpha));

                sb.Draw(pixel, new Rectangle(x, rect.Y, w, 1),
                        MobileUi.PanelBorder * (0.75f * falloff * alpha));
                sb.Draw(pixel, new Rectangle(x, rect.Bottom - 1, w, 1),
                        MobileUi.PanelBorder * (0.75f * falloff * alpha));
            }
        }

        /// <summary>
        /// 手機：水平置中。
        ///
        /// X 來自 MapNameLayout.json，那是為 1280 寬的桌面版面畫的固定座標。
        /// 手機的畫布依螢幕比例推算（滿版之後常常超過 1600 寬），固定座標因此
        /// 偏在中央的左邊 —— 使用者回報的「地圖名稱不居中」就是這個。
        /// </summary>
        private void ApplyMobileCentering()
        {
            if (!MobileUi.IsMobile)
                return;

            int centered = (UiScaler.VirtualSize.X - ViewSize.X) / 2;
            if (X != centered)
                X = centered;
        }

        private void UpdateLabelPosition()
        {
            ApplyMobileCentering();

            _label.X = X + (ViewSize.X - _label.ControlSize.X) / 2 + 10;
            _label.Y = Y + (ViewSize.Y - _label.ControlSize.Y) / 2;
        }

        private LayoutInfo LoadLayoutInfo()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream("Client.Main.Controls.UI.Game.Layouts.MapNameLayout.json"))
            {
                if (stream == null)
                    return null;
                using (StreamReader reader = new StreamReader(stream))
                {
                    string json = reader.ReadToEnd();
                    var list = JsonSerializer.Deserialize<List<LayoutInfo>>(json);
                    return list.FirstOrDefault(item => item.Name == "MapName");
                }
            }
        }

        private TextureRectData LoadTextureRectData()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream("Client.Main.Controls.UI.Game.Layouts.MapNameRect.json"))
            {
                if (stream == null)
                    return null;
                using (StreamReader reader = new StreamReader(stream))
                {
                    string json = reader.ReadToEnd();
                    var list = JsonSerializer.Deserialize<List<TextureRectData>>(json);
                    return list.FirstOrDefault(item => item.Name == "MapName");
                }
            }
        }
    }
}
