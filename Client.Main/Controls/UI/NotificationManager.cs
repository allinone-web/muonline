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

            float lineHeight = UiScaler.VirtualSize.Y * ORIGINAL_NOTICE_LINE_HEIGHT;
            _spawnCenter = new Vector2(
                UiScaler.VirtualSize.X * 0.5f,
                UiScaler.VirtualSize.Y * ORIGINAL_NOTICE_FIRST_LINE + lineHeight * 0.5f);
        }

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
        public override void Update(GameTime gameTime)
        {
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
