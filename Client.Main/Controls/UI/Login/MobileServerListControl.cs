#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Core.Models;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Login
{
    public class ServerSelectEventArgs : EventArgs
    {
        public byte Index { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// 手機用的伺服器選擇清單。
    ///
    /// 桌面版是「先點 Servers／Events 分組牌子，再點 192x26 的伺服器條」——
    /// 在手機上那兩塊牌子只有指頭的一半大，而且 Events 分組在 OpenMU 上根本沒有內容，
    /// 點下去只會看到同一批伺服器換個名字，令人困惑。
    ///
    /// 這裡直接列出伺服器，一張卡片一個，卡片高度依手指尺寸設定，點一下就進登入畫面。
    /// </summary>
    public class MobileServerListControl : UIControl
    {
        // 樣式與登入對話框、選角清單共用（見 MobileUi）
        private static readonly Color CardFill = new(30, 36, 46);
        private static readonly Color CardFillPressed = new(56, 66, 82);
        private static readonly Color TextWhite = MobileUi.TextPrimary;
        private static readonly Color TextGray = MobileUi.TextDim;
        private static readonly Color LoadFull = new(200, 70, 70);

        private const int PanelWidth = 520;
        private const int TitleHeight = 58;
        private const int CardHeight = 76;
        private const int CardGap = 10;
        private const int Padding = 14;

        private readonly List<ServerInfo> _servers = new();
        private readonly List<Rectangle> _cardRects = new();

        private Rectangle _panelRect;
        private Point _lastVirtualSize = Point.Zero;
        private int _pressedCard = -1;
        private bool _wasPressed;

        /// <summary>
        /// 已經選過伺服器了。選完之後這個清單就不該再回應任何觸控 ——
        /// 但它不會立刻消失：Visible 要等連線狀態變更的事件回來才會被關掉，
        /// 中間有幾十毫秒到一兩秒的空窗。使用者回報「聽到連續快速的點擊音效」
        /// 就是這段空窗裡的重複觸發。
        ///
        /// 由 SetServers 重置（重新拿到清單 = 真的回到選伺服器這一步）。
        /// </summary>
        private bool _selectionSent;

        /// <summary>
        /// 兩次啟用之間的最短間隔。觸控狀態偶爾會在單次按壓中閃回 Released
        /// 再變回 Pressed，那在邊緣判定上就是完整的一次「放開」——
        /// 沒有這個下限的話一次點擊會送出好幾次。
        /// </summary>
        private const double MinActivationIntervalSeconds = 0.4;
        private double _lastActivationAt = double.NegativeInfinity;
        private double _elapsedSeconds;
        private SpriteFont? _font;

        public event EventHandler<ServerSelectEventArgs>? ServerClick;

        public MobileServerListControl()
        {
            AutoViewSize = false;
            // 自行處理觸控 —— 卡片是直接繪製的，沒有子控制項可以路由點擊
            Interactive = false;
            Visible = false;
        }

        public void SetServers(IReadOnlyList<ServerInfo>? servers)
        {
            _servers.Clear();
            if (servers != null)
                _servers.AddRange(servers);

            _lastVirtualSize = Point.Zero;   // 強制重算版面

            // 重新拿到清單 = 真的回到「選伺服器」這一步，解除鎖定
            _selectionSent = false;
        }

        public int ServerCount => _servers.Count;

        protected override void OnScreenSizeChanged()
        {
            base.OnScreenSizeChanged();
            _lastVirtualSize = Point.Zero;
        }

        private void RefreshLayout()
        {
            var size = UiScaler.VirtualSize;
            if (size == _lastVirtualSize)
                return;

            _lastVirtualSize = size;

            int listHeight = _servers.Count > 0
                ? _servers.Count * CardHeight + (_servers.Count - 1) * CardGap
                : CardHeight;
            int panelHeight = TitleHeight + listHeight + Padding * 2;

            int panelX = (size.X - PanelWidth) / 2;
            int panelY = Math.Max(20, (size.Y - panelHeight) / 2);
            _panelRect = new Rectangle(panelX, panelY, PanelWidth, panelHeight);

            _cardRects.Clear();
            int y = panelY + TitleHeight + Padding;
            for (int i = 0; i < _servers.Count; i++)
            {
                _cardRects.Add(new Rectangle(panelX + Padding, y, PanelWidth - Padding * 2, CardHeight));
                y += CardHeight + CardGap;
            }

            X = _panelRect.X;
            Y = _panelRect.Y;
            ControlSize = new Point(_panelRect.Width, _panelRect.Height);
            ViewSize = ControlSize;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!Visible)
            {
                _pressedCard = -1;
                _wasPressed = false;
                return;
            }

            RefreshLayout();

            _elapsedSeconds += gameTime.ElapsedGameTime.TotalSeconds;

            var mouse = MuGame.Instance.UiMouseState;
            bool pressed = mouse.LeftButton == ButtonState.Pressed;
            var position = new Point(mouse.X, mouse.Y);

            if (pressed && !_wasPressed)
            {
                _pressedCard = HitTest(position);
            }
            else if (!pressed && _wasPressed)
            {
                int card = _pressedCard;
                _pressedCard = -1;
                _wasPressed = false;

                bool tooSoon = _elapsedSeconds - _lastActivationAt < MinActivationIntervalSeconds;

                if (card >= 0 && card == HitTest(position) && card < _servers.Count
                    && !_selectionSent && !tooSoon)
                {
                    _selectionSent = true;
                    _lastActivationAt = _elapsedSeconds;

                    var server = _servers[card];
                    SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav");
                    ServerClick?.Invoke(this, new ServerSelectEventArgs
                    {
                        Index = (byte)server.ServerId,
                        Name = server.ServerName ?? $"Server {server.ServerId}"
                    });
                }

                return;
            }

            _wasPressed = pressed;
        }

        private int HitTest(Point position)
        {
            for (int i = 0; i < _cardRects.Count; i++)
            {
                if (_cardRects[i].Contains(position))
                    return i;
            }

            return -1;
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            RefreshLayout();

            var sb = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            _font ??= GraphicsManager.Instance.Font;
            if (sb == null || pixel == null || _font == null)
                return;

            // 場景可能已經開好批次；重複 Begin 會失敗，畫面上就什麼都看不到。
            SpriteBatchScope? scope = null;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                scope = new SpriteBatchScope(
                    sb, SpriteSortMode.Deferred, BlendState.AlphaBlend,
                    SamplerState.LinearClamp, transform: UiScaler.SpriteTransform);
            }

            try
            {
                DrawPanel(sb, pixel);

                for (int i = 0; i < _cardRects.Count && i < _servers.Count; i++)
                    DrawCard(sb, pixel, i);

                if (_servers.Count == 0)
                    DrawEmptyState(sb);
            }
            finally
            {
                scope?.Dispose();
            }

            base.Draw(gameTime);
        }

        private void DrawPanel(SpriteBatch sb, Texture2D pixel)
        {
            MobileUi.DrawPanel(sb, _panelRect, TitleHeight);

            const string title = "SELECT SERVER";
            float scale = 0.68f;
            var textSize = _font!.MeasureString(title) * scale;
            var position = new Vector2(
                _panelRect.X + (_panelRect.Width - textSize.X) * 0.5f,
                _panelRect.Y + (TitleHeight - textSize.Y) * 0.5f);

            sb.DrawString(_font, title, position + Vector2.One, Color.Black * 0.7f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.DrawString(_font, title, position, TextWhite, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private void DrawCard(SpriteBatch sb, Texture2D pixel, int index)
        {
            var rect = _cardRects[index];
            var server = _servers[index];
            bool pressed = index == _pressedCard;
            bool full = server.LoadPercentage >= 100;

            sb.Draw(pixel, rect, (pressed ? CardFillPressed : CardFill) * (full ? 0.55f : 0.95f));

            if (pressed)
                sb.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), Color.White * 0.35f);

            string name = server.ServerName ?? $"Server {server.ServerId}";
            float nameScale = 0.66f;
            var nameSize = _font!.MeasureString(name) * nameScale;
            var namePos = new Vector2(rect.X + 18, rect.Y + 14);

            sb.DrawString(_font, name, namePos + Vector2.One, Color.Black * 0.8f, 0f, Vector2.Zero, nameScale, SpriteEffects.None, 0f);
            sb.DrawString(_font, name, namePos, full ? TextGray : TextWhite, 0f, Vector2.Zero, nameScale, SpriteEffects.None, 0f);

            // 負載條
            int gaugeWidth = rect.Width - 36;
            var gaugeRect = new Rectangle(rect.X + 18, (int)(namePos.Y + nameSize.Y + 8), gaugeWidth, 8);
            sb.Draw(pixel, gaugeRect, new Color(10, 12, 16) * 0.9f);

            float load = MathHelper.Clamp(server.LoadPercentage / 100f, 0f, 1f);
            int fill = (int)(gaugeWidth * load);
            if (fill > 0)
            {
                // 只有「滿載」值得用顏色警示，其餘一律白色 —— 負載高低看長度就夠了
                Color loadColor = full ? LoadFull : Color.White * 0.55f;
                sb.Draw(pixel, new Rectangle(gaugeRect.X, gaugeRect.Y, fill, gaugeRect.Height), loadColor);
            }

            string loadText = full ? "FULL" : $"{server.LoadPercentage}%";
            float loadScale = 0.46f;
            var loadSize = _font.MeasureString(loadText) * loadScale;
            sb.DrawString(_font, loadText,
                new Vector2(rect.Right - loadSize.X - 18, rect.Y + 12),
                full ? LoadFull : TextGray, 0f, Vector2.Zero, loadScale, SpriteEffects.None, 0f);
        }

        private void DrawEmptyState(SpriteBatch sb)
        {
            const string text = "Waiting for server list...";
            float scale = 0.5f;
            var size = _font!.MeasureString(text) * scale;
            sb.DrawString(_font, text,
                new Vector2(_panelRect.X + (_panelRect.Width - size.X) * 0.5f, _panelRect.Y + TitleHeight + 26),
                TextGray, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }
}
