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

            if (Texture == null)
                return;

            // <b>這張貼圖是刻意保留的。</b>
            //
            // 進入地圖時的名稱橫幅（MapName_I2.ozd）有它自己的造型，使用者兩次
            // 指名要保留 —— 我先前兩次都自作主張換成程式繪製（第一次畫成方框、
            // 第二次畫成兩端淡出的橫幅），兩次都是錯的。
            //
            // 「一律程式繪製」是通則，不是可以覆蓋明確指示的理由。
            // 這裡是那個例外，不要再改。見 docs/待清理素材.md 的「刻意保留」。
            //
            // 手機用 LinearClamp：PointClamp 在放大時會出現硬邊鋸齒。
            using (new SpriteBatchScope(
                   sb,
                   SpriteSortMode.Deferred,
                   BlendState.NonPremultiplied,
                   MobileUi.IsMobile ? SamplerState.LinearClamp : SamplerState.PointClamp,
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
