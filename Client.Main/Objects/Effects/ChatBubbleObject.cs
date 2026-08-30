using System;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Controls.UI;
using Client.Main.Objects.Player;
using Client.Main.Models;
using System.Text;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Simple chat bubble displayed above a player for a short time.
    /// </summary>
    public class ChatBubbleObject : EffectObject
    {
        private const float DefaultLifetime = 5f;
        private const float OffsetZ = 60f;
        private const int PixelGap = 8;
        /// <summary>
        /// 泡泡的最大寬度。字放大之後這個值也要跟著放大 ——
        /// 否則同一句話會被折成兩倍的行數，泡泡變成又窄又高的一長條。
        /// </summary>
        private static int MaxBubbleWidth => (int)(200 * Client.Main.Controls.UI.MobileUi.WorldTextScale);
        // Keep UI-backed bubbles in the off-grid update path; labels project from the target each frame.
        private static readonly Vector3 OverlayLifecyclePosition = new(-Constants.TERRAIN_SCALE, -Constants.TERRAIN_SCALE, 0f);

        private string _text;
        private readonly string _playerName;
        private readonly ushort _targetId;
        private float _lifetime;
        private float _originalLifetime;

        private LabelControl _nameLabel;
        private LabelControl _textLabel;
        private SpriteFont _font;
        private float _elapsed;

        /// <summary>
        /// Creates a new chat bubble.
        /// </summary>
        /// <param name="text">Message text to display.</param>
        /// <param name="targetId">Network id of the player.</param>
        /// <param name="playerName">Name of the player.</param>
        /// <param name="lifetime">Optional lifetime in seconds.</param>
        public ChatBubbleObject(string text, ushort targetId, string playerName, float lifetime = DefaultLifetime)
        {
            _text = text ?? string.Empty;
            _playerName = playerName ?? string.Empty;
            _targetId = targetId;
            _lifetime = lifetime;
            _originalLifetime = lifetime;

            IsTransparent = true;
            AffectedByTransparency = false;
            Position = OverlayLifecyclePosition;
            BoundingBoxLocal = new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

        public override async Task Load()
        {
            _font = GraphicsManager.Instance.Font;

            _nameLabel = new LabelControl
            {
                Text = _playerName,
                // 角色頭上的對話泡泡。10 是桌面尺寸，手機放大
                // —— 見 Client.Main.Controls.UI.MobileUi.WorldTextScale。
                FontSize = 10f * Client.Main.Controls.UI.MobileUi.WorldTextScale,
                TextColor = Color.Yellow,
                HasShadow = true,
                ShadowColor = Color.Black,
                ShadowOpacity = 0.8f,
                BackgroundColor = new Color(20, 20, 60, 180),
                Padding = new Margin { Left = 4, Right = 4, Top = 2, Bottom = 2 },
                UseManualPosition = true,
                UseControlSizeBackground = true,
                Visible = false
            };

            _textLabel = new LabelControl
            {
                Text = _text,
                // 角色頭上的對話泡泡。10 是桌面尺寸，手機放大
                // —— 見 Client.Main.Controls.UI.MobileUi.WorldTextScale。
                FontSize = 10f * Client.Main.Controls.UI.MobileUi.WorldTextScale,
                TextColor = Color.White,
                HasShadow = true,
                ShadowColor = Color.Black,
                ShadowOpacity = 0.8f,
                BackgroundColor = new Color(0, 0, 0, 160),
                Padding = new Margin { Left = 4, Right = 4, Top = 2, Bottom = 2 },
                UseManualPosition = true,
                UseControlSizeBackground = true,
                Visible = false
            };

            _textLabel.Text = WrapText(_textLabel.Text, _textLabel.FontSize, MaxBubbleWidth);

            if (World?.Scene != null)
            {
                World.Scene.Controls.Add(_nameLabel);
                World.Scene.Controls.Add(_textLabel);
                await _nameLabel.Load();
                await _textLabel.Load();
            }

            Status = GameControlStatus.Ready;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready) return;

            _elapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_elapsed >= _lifetime)
            {
                RemoveLabels();
                World?.RemoveObject(this);
                Dispose();
                return;
            }

            var target = ResolveTarget();
            if (target == null || target.Hidden || target.Status != GameControlStatus.Ready)
            {
                HideLabels();
                return;
            }

            UpdateLabelPosition(target);
        }

        public override void Draw(GameTime gameTime) { }

        private WalkerObject ResolveTarget()
        {
            if (World == null) return null;
            return World.TryGetWalkerById(_targetId, out var walker) ? walker : null;
        }

        private void UpdateLabelPosition(WalkerObject target)
        {
            Vector3 anchor = new(
                (target.BoundingBoxWorld.Min.X + target.BoundingBoxWorld.Max.X) * 0.5f,
                (target.BoundingBoxWorld.Min.Y + target.BoundingBoxWorld.Max.Y) * 0.5f,
                target.BoundingBoxWorld.Max.Z + OffsetZ);

            Vector3 screen = GraphicsDevice.Viewport.Project(
                anchor,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);

            if (screen.Z < 0f || screen.Z > 1f)
            {
                HideLabels();
                return;
            }

            // Convert screen coordinates to virtual coordinates for UI system
            var virtualPos = UiScaler.ToVirtual(new Point((int)screen.X, (int)screen.Y));

            Vector2 nameSize = MeasureLabelSize(_nameLabel);
            Vector2 textSize = MeasureLabelSize(_textLabel);

            int nameWidth = (int)nameSize.X + _nameLabel.Padding.Left + _nameLabel.Padding.Right;
            int textWidth = (int)textSize.X + _textLabel.Padding.Left + _textLabel.Padding.Right;

            int nameHeight = (int)nameSize.Y + _nameLabel.Padding.Top + _nameLabel.Padding.Bottom;
            int textHeight = (int)textSize.Y + _textLabel.Padding.Top + _textLabel.Padding.Bottom;

            int maxWidth = Math.Max(nameWidth, textWidth);

            _nameLabel.ControlSize = new Point(
                maxWidth - (_nameLabel.Padding.Left + _nameLabel.Padding.Right),
                (int)nameSize.Y);
            _textLabel.ControlSize = new Point(
                maxWidth - (_textLabel.Padding.Left + _textLabel.Padding.Right),
                (int)textSize.Y);

            int bubbleHeight = nameHeight + textHeight;

            int bubbleX = (int)(virtualPos.X - maxWidth / 2f);

            _nameLabel.X = bubbleX;
            _nameLabel.Y = (int)(virtualPos.Y - bubbleHeight - PixelGap);

            _textLabel.X = bubbleX;
            _textLabel.Y = _nameLabel.Y + nameHeight;

            _nameLabel.Visible = true;
            _textLabel.Visible = true;
        }

        private void HideLabels()
        {
            if (_nameLabel != null)
                _nameLabel.Visible = false;

            if (_textLabel != null)
                _textLabel.Visible = false;
        }

        private void RemoveLabels()
        {
            if (_nameLabel != null)
            {
                _nameLabel.Parent?.Controls.Remove(_nameLabel);
                _nameLabel.Dispose();
                _nameLabel = null;
            }

            if (_textLabel != null)
            {
                _textLabel.Parent?.Controls.Remove(_textLabel);
                _textLabel.Dispose();
                _textLabel = null;
            }
        }

        private Vector2 MeasureLabelSize(LabelControl label)
        {
            if (_font == null)
                return Vector2.Zero;

            float scale = label.FontSize / Constants.BASE_FONT_SIZE;
            Vector2 size = _font.MeasureString(label.Text) * scale;

            if (label.HasShadow)
            {
                size.X += (float)Math.Ceiling(Math.Abs(label.ShadowOffset.X));
                size.Y += (float)Math.Ceiling(Math.Abs(label.ShadowOffset.Y));
            }

            if (label.IsBold)
            {
                size.X += (float)Math.Ceiling(label.BoldStrength * 2);
                size.Y += (float)Math.Ceiling(label.BoldStrength * 2);
            }

            return size;
        }

        private string WrapText(string rawText, float fontSize, int maxWidth)
        {
            if (_font == null || string.IsNullOrEmpty(rawText))
                return rawText;

            float scale = fontSize / Constants.BASE_FONT_SIZE;
            var words = rawText.Split(' ');
            var sb = new StringBuilder();
            var current = new StringBuilder();

            foreach (var w in words)
            {
                string test = current.Length == 0 ? w : current + " " + w;
                float width = _font.MeasureString(test).X * scale;

                if (width <= maxWidth)
                {
                    current.Clear();
                    current.Append(test);
                }
                else
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(current);
                    current.Clear();
                    current.Append(w);
                }
            }

            if (current.Length > 0)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(current);
            }

            return sb.ToString();
        }

        public void AppendMessage(string newMessage)
        {
            if (string.IsNullOrEmpty(newMessage)) return;
            
            _text = newMessage + "\n" + _text;
            _lifetime = _elapsed + _originalLifetime;
            
            if (_textLabel != null)
            {
                _textLabel.Text = WrapText(_text, _textLabel.FontSize, MaxBubbleWidth);
            }
        }

        public ushort TargetId => _targetId;

        public override void Dispose()
        {
            RemoveLabels();
            base.Dispose();
        }
    }
}
