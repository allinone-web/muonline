#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Core.Models;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// 手機用的撿取清單。
    ///
    /// 桌面是「按空白鍵撿最近的一件」或「用滑鼠點地上的東西」。手機兩條路都沒有：
    /// 沒有鍵盤，而點擊世界已經整個停用（否則會和虛擬搖桿打架）—— 撿東西的功能
    /// 因此一度完全消失。
    ///
    /// 做法參考 PUBG：把腳邊撿得到的東西列成一排，點哪一列就撿哪一件。
    /// 只列出<b>已經在撿取範圍內</b>的東西，玩家不必猜「走近一點是不是就撿得到」。
    /// </summary>
    public class TouchPickupListControl : UIControl
    {
        private const int MaxRows = 4;
        private const int RowHeight = 54;
        private const int RowGap = 6;
        private const int PanelWidth = 300;

        /// <summary>與伺服器端一致的撿取距離（格），見 ScopeManager.FindNearestPickupItemRawId。</summary>
        private const double PickupRangeSquared = 5 * 5;

        /// <summary>清單重算的間隔。每幀掃描整個 scope 沒有必要。</summary>
        private const double RefreshIntervalSeconds = 0.2;

        private readonly List<(ushort RawId, string Name, bool IsMoney)> _entries = new();
        private readonly List<Rectangle> _rowRects = new();

        private double _refreshElapsed = RefreshIntervalSeconds;
        private int _pressedRow = -1;
        private bool _wasPressed;
        private SpriteFont? _font;

        public TouchPickupListControl()
        {
            AutoViewSize = false;
            Interactive = false;   // 自行處理觸控，與其他手機控制項一致
            ViewSize = new Point(1, 1);
        }

        /// <summary>這個座標是否落在清單上 —— 供搖桿避開。</summary>
        public bool ContainsPoint(Point position)
        {
            if (!Visible)
                return false;

            for (int i = 0; i < _rowRects.Count; i++)
            {
                if (_rowRects[i].Contains(position))
                    return true;
            }

            return false;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready || !Visible)
                return;

            _refreshElapsed += gameTime.ElapsedGameTime.TotalSeconds;
            if (_refreshElapsed >= RefreshIntervalSeconds)
            {
                _refreshElapsed = 0;
                RefreshEntries();
            }

            if (_entries.Count == 0)
            {
                _pressedRow = -1;
                _wasPressed = false;
                return;
            }

            var mouse = MuGame.Instance.UiMouseState;
            bool pressed = mouse.LeftButton == ButtonState.Pressed;
            var position = new Point(mouse.X, mouse.Y);

            if (pressed && !_wasPressed)
            {
                _pressedRow = HitTest(position);
            }
            else if (!pressed && _wasPressed)
            {
                int row = _pressedRow;
                _pressedRow = -1;
                _wasPressed = false;

                if (row >= 0 && row == HitTest(position) && row < _entries.Count)
                {
                    var entry = _entries[row];
                    if (MuGame.Instance.ActiveScene is GameScene scene && scene.PickupItem(entry.RawId))
                    {
                        SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav");

                        // 立刻從清單移除，避免伺服器回應前重複點擊同一件
                        _entries.RemoveAt(row);
                        RefreshLayout();
                    }
                }

                return;
            }

            _wasPressed = pressed;
        }

        private void RefreshEntries()
        {
            _entries.Clear();

            var network = MuGame.Network;
            var scopeManager = network?.GetScopeManager();
            var characterState = network?.GetCharacterState();
            if (scopeManager == null || characterState == null)
            {
                RefreshLayout();
                return;
            }

            var candidates = scopeManager.GetScopeItems(ScopeObjectType.Item)
                .Concat(scopeManager.GetScopeItems(ScopeObjectType.Money))
                .Where(o => o.PositionX != 0 || o.PositionY != 0)
                .Select(o => (Object: o, DistanceSq: DistanceSquared(characterState, o)))
                .Where(x => x.DistanceSq <= PickupRangeSquared)
                .OrderBy(x => x.DistanceSq)
                .Take(MaxRows);

            foreach (var candidate in candidates)
            {
                bool isMoney = candidate.Object.ObjectType == ScopeObjectType.Money;
                string name = scopeManager.TryGetScopeObjectName(candidate.Object.RawId, out var resolved) && !string.IsNullOrWhiteSpace(resolved)
                    ? resolved
                    : (isMoney ? "Zen" : "Item");

                _entries.Add((candidate.Object.RawId, name, isMoney));
            }

            RefreshLayout();
        }

        private static double DistanceSquared(CharacterState state, ScopeObject obj)
        {
            double dx = state.PositionX - obj.PositionX;
            double dy = state.PositionY - obj.PositionY;
            return dx * dx + dy * dy;
        }

        private void RefreshLayout()
        {
            _rowRects.Clear();
            if (_entries.Count == 0)
                return;

            var canvas = UiScaler.VirtualSize;

            // 右側、技能弧線的上方 —— 那塊區域是空的，而且右手拇指本來就在附近。
            // 由下往上排，最近的一件永遠在最下面（離拇指最近）。
            int right = canvas.X - MobileUi.CornerInset;
            int bottom = canvas.Y - 300;

            for (int i = 0; i < _entries.Count; i++)
            {
                int y = bottom - (i + 1) * RowHeight - i * RowGap;
                _rowRects.Add(new Rectangle(right - PanelWidth, y, PanelWidth, RowHeight));
            }
        }

        private int HitTest(Point position)
        {
            for (int i = 0; i < _rowRects.Count; i++)
            {
                if (_rowRects[i].Contains(position))
                    return i;
            }

            return -1;
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible || _entries.Count == 0)
                return;

            var sb = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            _font ??= GraphicsManager.Instance.Font;
            if (sb == null || pixel == null || _font == null)
                return;

            SpriteBatchScope? scope = null;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                scope = new SpriteBatchScope(
                    sb, SpriteSortMode.Deferred, BlendState.AlphaBlend,
                    SamplerState.LinearClamp, transform: UiScaler.SpriteTransform);
            }

            try
            {
                for (int i = 0; i < _rowRects.Count && i < _entries.Count; i++)
                    DrawRow(sb, pixel, i);
            }
            finally
            {
                scope?.Dispose();
            }

            base.Draw(gameTime);
        }

        private void DrawRow(SpriteBatch sb, Texture2D pixel, int index)
        {
            var rect = _rowRects[index];
            var entry = _entries[index];
            bool pressed = index == _pressedRow;

            MobileUi.DrawPanel(sb, rect, 0, pressed ? 0.95f : MobileUi.PanelAlpha);

            // 左側一個小圓點區分金幣與道具 —— 只用明暗，不另外加顏色
            var dotCenter = new Vector2(rect.X + 26, rect.Center.Y);
            MobileUi.DrawDisc(sb, dotCenter, 11f, Color.White * (entry.IsMoney ? 0.55f : 0.28f));

            string label = entry.Name;
            float scale = 0.48f;
            var size = _font!.MeasureString(label) * scale;

            // 名稱太長就從右邊截斷，不要溢出面板
            int maxWidth = rect.Width - 60;
            if (size.X > maxWidth)
            {
                int keep = Math.Max(1, (int)(label.Length * (maxWidth / size.X)) - 1);
                label = label.Substring(0, keep) + "…";
                size = _font.MeasureString(label) * scale;
            }

            var position = new Vector2(rect.X + 46, rect.Center.Y - size.Y * 0.5f);
            sb.DrawString(_font, label, position + Vector2.One, Color.Black * 0.75f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.DrawString(_font, label, position, MobileUi.TextPrimary, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }
}
