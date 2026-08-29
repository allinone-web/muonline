// NotificationManager.cs
using Client.Main.Controllers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Client.Main.Controls.UI
{
    /// <summary>
    /// Manages on-screen floating text notifications.
    /// </summary>
    public class NotificationManager : UIControl
    {
        // ──────────────────────────── Fields ────────────────────────────
        private readonly List<FloatingText> _active = new List<FloatingText>();
        private readonly object _sync = new object();

        private Vector2 _spawnCenter;
        private float _latestTotalSeconds;
        private float _noticeCountdownSeconds = NOTICE_INTERVAL_SECONDS;

        private const int MAX_NOTICES = 6;
        private const float NOTICE_INTERVAL_SECONDS = 12f;
        private const float ORIGINAL_NOTICE_FIRST_LINE = 300f / 480f;
        private const float ORIGINAL_NOTICE_LINE_HEIGHT = 13f / 480f;

        // ───────────────────────── Constructors ─────────────────────────
        public NotificationManager()
        {
            Visible = true;
            Interactive = false;

            RefreshSpawnAnchor();
        }

        /// <summary>
        /// 公告的落點。
        ///
        /// 桌面沿用原版：畫面正中央、上緣往下 300/480。
        ///
        /// <b>手機改到左側。</b>正中央是所有視窗的位置（地圖、技能、背包都置中），
        /// 公告每 12 秒刷新一次，只要視窗開著就會有一行金字直接蓋在上面 ——
        /// 使用者回報的「點地圖，左邊的公告忽然跑到地圖中間」就是這件事：
        /// 公告一直都在中央，只是平常背景是場景所以不明顯。
        ///
        /// 左欄本來就是訊息的位置（聊天記錄也在那裡），公告放過去才是一致的。
        ///
        /// 這個值必須<b>可以重算</b>：畫布尺寸會在執行期改變（安全區域在啟動後
        /// 約半秒才讀得到真值，見 MuGame.PollSafeArea），只在建構子算一次的話
        /// 之後就一直是錯的。
        /// </summary>
        private void RefreshSpawnAnchor()
        {
            var canvas = UiScaler.VirtualSize;
            float lineHeight = canvas.Y * ORIGINAL_NOTICE_LINE_HEIGHT;

            if (MobileUi.IsMobile)
            {
                // 左欄，聊天記錄下方。X 是文字的<b>中心</b>（FloatingText 置中對齊），
                // 所以取欄寬的一半。
                _spawnCenter = new Vector2(
                    MobileUi.LeftEdge + MobileNoticeColumnWidth * 0.5f,
                    canvas.Y * MobileNoticeTop);
            }
            else
            {
                _spawnCenter = new Vector2(
                    canvas.X * 0.5f,
                    canvas.Y * ORIGINAL_NOTICE_FIRST_LINE + lineHeight * 0.5f);
            }

            _spawnAnchorFor = canvas;
        }

        /// <summary>手機公告欄的寬度與上緣（畫面高度的比例）。與聊天記錄同一欄。</summary>
        private const int MobileNoticeColumnWidth = 560;
        private const float MobileNoticeTop = 0.62f;

        private Point _spawnAnchorFor = Point.Zero;

        // ────────────────────────── Public API ──────────────────────────
        /// <summary>
        /// Adds a new notification, using the last known game time.
        /// </summary>
        public void AddNotification(string text, Color color)
        {
            AddNotificationInternal(text, color, _latestTotalSeconds);
        }

        /// <summary>
        /// Adds a new notification at the specified game time.
        /// </summary>
        public void AddNotification(string text, Color color, GameTime gameTime)
        {
            AddNotificationInternal(text, color, (float)gameTime.TotalGameTime.TotalSeconds);
        }

        // ──────────────────────── Private Methods ───────────────────────
        private void AddNotificationInternal(string text, Color color, float creationTime)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            lock (_sync)
            {
                AddNoticeSlot(text, color, creationTime);
                _noticeCountdownSeconds = NOTICE_INTERVAL_SECONDS;
            }
        }

        private void AddNoticeSlot(string text, Color color, float creationTime)
        {
            if (_active.Count >= MAX_NOTICES)
            {
                var oldest = _active[0];
                if (Parent != null)
                    Parent.Controls.Remove(oldest);

                _active.RemoveAt(0);
            }

            var note = new FloatingText(text, color, _spawnCenter, creationTime);
            _active.Add(note);

            if (Parent != null)
                Parent.Controls.Add(note);

            RecalculateStack();
        }

        /// <summary>
        /// Arranges notifications in a vertical stack without overlap.
        /// </summary>
        private void RecalculateStack()
        {
            float currentY = _spawnCenter.Y;
            foreach (var note in _active)
            {
                note.SetCenterY(currentY);
                currentY += UiScaler.VirtualSize.Y * ORIGINAL_NOTICE_LINE_HEIGHT;
            }
        }

        // ───────────────────────── Overrides ──────────────────────────
        protected override void OnScreenSizeChanged()
        {
            base.OnScreenSizeChanged();
            RefreshSpawnAnchor();
            lock (_sync)
            {
                RecalculateStack();
            }
        }

        public override void Update(GameTime gameTime)
        {
            // 畫布可能在執行期改變（安全區域、轉向）。OnScreenSizeChanged 不一定
            // 會傳到這裡 —— 這個控制項沒有可見的大小，所以自己比對一次。
            if (UiScaler.VirtualSize != _spawnAnchorFor)
            {
                RefreshSpawnAnchor();
                lock (_sync)
                {
                    RecalculateStack();
                }
            }

            _latestTotalSeconds = (float)gameTime.TotalGameTime.TotalSeconds;
            _noticeCountdownSeconds -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            bool removedAny = false;

            lock (_sync)
            {
                while (_noticeCountdownSeconds <= 0f)
                {
                    AddNoticeSlot(string.Empty, Color.Goldenrod, _latestTotalSeconds);
                    _noticeCountdownSeconds += NOTICE_INTERVAL_SECONDS;
                }

                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    var note = _active[i];
                    if (!note.Visible || note.Status == GameControlStatus.Disposed)
                    {
                        if (Parent != null)
                            Parent.Controls.Remove(note);

                        _active.RemoveAt(i);
                        removedAny = true;
                    }
                }

                if (removedAny)
                {
                    RecalculateStack();
                }
            }
        }

        // Draw: empty because each FloatingText draws itself
    }
}
