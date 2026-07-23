#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Skills
{
    /// <summary>
    /// Master skill tree visualization for 4th class characters.
    /// Shows 3 trees per class with skill nodes and dependencies.
    /// </summary>
    public class MasterSkillTreeControl : UIControl
    {
        private SpriteFont? _font;
        private readonly List<MasterSkillNode> _nodes = new();

        private const int NodeSize = 52;
        private const int NodeSpacingX = 80;
        private const int NodeSpacingY = 70;
        private const int TreeWidth = 3 * NodeSpacingX + NodeSize;

        public event Action<ushort>? SkillNodeClicked;

        public MasterSkillTreeControl()
        {
            AutoViewSize = false;
            Interactive = true;
            BackgroundColor = new Color(12, 14, 20, 240);
            BorderColor = ModernHudTheme.BorderOuter;
            BorderThickness = 2;
            ControlSize = new Point(TreeWidth + 40, 420);
            ViewSize = ControlSize;
            Visible = false;

            _nodes.Add(new MasterSkillNode { SkillId = 300, Name = "Evil Spirit Up", X = 1, Y = 0, RequiredSkillId = 9 });
            _nodes.Add(new MasterSkillNode { SkillId = 301, Name = "Hell Fire Up", X = 1, Y = 1, RequiredSkillId = 10 });
            _nodes.Add(new MasterSkillNode { SkillId = 302, Name = "Ice Up", X = 1, Y = 2, RequiredSkillId = 11 });
            _nodes.Add(new MasterSkillNode { SkillId = 303, Name = "Soul Up", X = 1, Y = 3, RequiredSkillId = 12 });
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready) return;
            var sb = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            _font ??= GraphicsManager.Instance.Font;
            if (sb == null || pixel == null || _font == null) return;

            Rectangle rect = DisplayRectangle;
            sb.Draw(pixel, rect, BackgroundColor * Alpha);

            // Title
            var title = "Master Skill Tree";
            var tsz = _font.MeasureString(title) * 0.7f;
            sb.DrawString(_font, title, new Vector2(rect.Center.X - tsz.X / 2, rect.Y + 8),
                ModernHudTheme.TextGold * Alpha, 0f, Vector2.Zero, 0.7f, SpriteEffects.None, 0f);

            // Draw connection lines first
            foreach (var node in _nodes)
            {
                if (node.RequiredSkillId > 0)
                {
                    var parent = _nodes.Find(n => n.SkillId == node.RequiredSkillId);
                    if (parent != null)
                    {
                        var pPos = GetNodeCenter(rect, parent);
                        var nPos = GetNodeCenter(rect, node);
                        sb.Draw(pixel, new Rectangle((int)pPos.X, (int)pPos.Y, (int)(nPos.X - pPos.X), 1), ModernHudTheme.TextDark * Alpha);
                    }
                }
            }

            // Draw nodes
            foreach (var node in _nodes)
            {
                var nodeRect = GetNodeRect(rect, node);
                bool hovered = nodeRect.Contains(MuGame.Instance.UiMouseState.Position);

                Color bg = hovered ? new Color(50, 50, 60, 220) : new Color(30, 32, 42, 200);
                sb.Draw(pixel, nodeRect, bg * Alpha);
                sb.Draw(pixel, new Rectangle(nodeRect.X, nodeRect.Y, nodeRect.Width, 1), ModernHudTheme.AccentDim * Alpha);

                var nameSize = _font.MeasureString(node.Name) * 0.35f;
                float nx = nodeRect.X + (nodeRect.Width - nameSize.X) / 2f;
                float ny = nodeRect.Y + (nodeRect.Height - nameSize.Y) / 2f;
                sb.DrawString(_font, node.Name, new Vector2(nx, ny), ModernHudTheme.TextWhite * Alpha, 0f, Vector2.Zero, 0.35f, SpriteEffects.None, 0f);
            }
        }

        private Rectangle GetNodeRect(Rectangle panel, MasterSkillNode node)
        {
            int x = panel.X + 30 + node.X * NodeSpacingX;
            int y = panel.Y + 50 + node.Y * NodeSpacingY;
            return new Rectangle(x, y, NodeSize, NodeSize);
        }

        private Vector2 GetNodeCenter(Rectangle panel, MasterSkillNode node)
        {
            var r = GetNodeRect(panel, node);
            return new Vector2(r.Center.X, r.Center.Y);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!Visible) return;
            if (MuGame.Instance.Keyboard.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape) &&
                MuGame.Instance.PrevKeyboard.IsKeyUp(Microsoft.Xna.Framework.Input.Keys.Escape))
                Visible = false;

            bool leftJustPressed = MuGame.Instance.UiMouseState.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed &&
                                   MuGame.Instance.PrevMouseState.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Released;
            if (!leftJustPressed)
                return;

            Point mousePos = MuGame.Instance.UiMouseState.Position;
            Rectangle rect = DisplayRectangle;
            foreach (var node in _nodes)
            {
                if (GetNodeRect(rect, node).Contains(mousePos))
                {
                    SkillNodeClicked?.Invoke(node.SkillId);
                    return;
                }
            }
        }

        private class MasterSkillNode
        {
            public ushort SkillId;
            public string Name = string.Empty;
            public int X, Y;
            public ushort RequiredSkillId;
        }
    }
}
