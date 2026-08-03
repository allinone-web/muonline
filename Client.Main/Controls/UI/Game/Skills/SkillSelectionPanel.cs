#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Client.Data.BMD;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game.Skills
{
    /// <summary>
    /// Popup panel displaying all available skills in a modern grid + detail layout.
    /// </summary>
    public class SkillSelectionPanel : UIControl, IUiTexturePreloadable
    {
        private const int COLUMNS = 6;
        private const int OUTER_PADDING = 14;
        private const int HEADER_HEIGHT = 52;
        private const int CONTENT_GAP = 12;
        private const int GRID_PADDING = 12;
        private const int SLOT_GAP = 10;
        private const int DETAIL_WIDTH = 320;
        private const int DETAIL_PADDING = 14;
        private const int MIN_DETAIL_HEIGHT = 300;
        private const float TARGET_SLOT_SCALE = 1.24f;
        private const float OPEN_ANIMATION_DURATION = 0.18f;
        private const int OPEN_ANIMATION_OFFSET_Y = 18;

        private readonly List<SkillSlotControl> _skillSlots = new();
        private readonly LabelControl _titleLabel;
        private readonly UIControl _detailPanel;
        private readonly LabelControl _detailNameLabel;
        private readonly LabelControl _detailTypeLabel;
        private readonly LabelControl _detailStatsLabel;

        private Rectangle _headerRectLocal;
        private Rectangle _contentRectLocal;
        private Rectangle _gridRectLocal;
        private Rectangle _detailRectLocal;
        private ushort? _selectedSkillId;
        private float _slotScale = TARGET_SLOT_SCALE;
        private bool _isOpeningAnimation;
        private float _openAnimationElapsedSeconds;

        private sealed class PanelControl : UIControl { }

        /// <summary>
        /// Fired when a skill is selected from the panel.
        /// </summary>
        public event Action<SkillEntryState>? SkillSelected;

        public SkillSelectionPanel()
        {
            Interactive = true;
            BackgroundColor = Color.Transparent;
            BorderColor = Color.Transparent;
            BorderThickness = 0;
            Visible = false;
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;

            _titleLabel = new LabelControl
            {
                Text = "Select Skill",
                TextColor = ModernHudTheme.TextGold,
                FontSize = 15f,
                X = OUTER_PADDING,
                Y = 14,
                ViewSize = new Point(460, 26),
                Align = ControlAlign.HorizontalCenter
            };
            Controls.Add(_titleLabel);

            _detailPanel = new PanelControl
            {
                AutoViewSize = false,
                ControlSize = new Point(DETAIL_WIDTH, MIN_DETAIL_HEIGHT),
                ViewSize = new Point(DETAIL_WIDTH, MIN_DETAIL_HEIGHT),
                BackgroundColor = Color.Transparent,
                BorderColor = Color.Transparent,
                BorderThickness = 0,
                Interactive = false
            };
            Controls.Add(_detailPanel);

            _detailNameLabel = new LabelControl
            {
                Text = "Skill Info",
                TextColor = ModernHudTheme.TextGold,
                FontSize = 15f,
                X = DETAIL_PADDING,
                Y = DETAIL_PADDING,
                ViewSize = new Point(DETAIL_WIDTH - DETAIL_PADDING * 2, 28)
            };
            _detailPanel.Controls.Add(_detailNameLabel);

            _detailTypeLabel = new LabelControl
            {
                Text = string.Empty,
                TextColor = ModernHudTheme.SecondaryBright,
                FontSize = 12f,
                X = DETAIL_PADDING,
                Y = DETAIL_PADDING + 28,
                ViewSize = new Point(DETAIL_WIDTH - DETAIL_PADDING * 2, 22)
            };
            _detailPanel.Controls.Add(_detailTypeLabel);

            _detailStatsLabel = new LabelControl
            {
                Text = "Hover a skill to see details.",
                TextColor = ModernHudTheme.TextGray,
                X = DETAIL_PADDING,
                Y = DETAIL_PADDING + 54,
                ViewSize = new Point(DETAIL_WIDTH - DETAIL_PADDING * 2, 200),
                Scale = 0.9f
            };
            _detailPanel.Controls.Add(_detailStatsLabel);

            Alpha = 1f;
            Offset = Point.Zero;
        }

        /// <summary>
        /// Opens the panel and populates it with the character's skills.
        /// </summary>
        public void Open(CharacterState characterState)
        {
            if (characterState == null)
            {
                return;
            }

            var skills = characterState
                .GetSkills()
                .OrderBy(s => SkillDatabase.GetSkillName(s.SkillId))
                .ThenBy(s => s.SkillId)
                .ToList();

            _titleLabel.Text = $"Select Skill ({skills.Count} available)";

            foreach (var slot in _skillSlots)
            {
                slot.HoverChanged -= OnSkillSlotHover;
                Controls.Remove(slot);
            }
            _skillSlots.Clear();

            int rows = (int)Math.Ceiling(skills.Count / (float)COLUMNS);
            if (rows == 0)
            {
                rows = 1;
            }

            _slotScale = CalculateSlotScale(rows);

            int slotWidth = Math.Max(1, (int)MathF.Round(SkillSlotControl.SLOT_WIDTH * _slotScale));
            int slotHeight = Math.Max(1, (int)MathF.Round(SkillSlotControl.SLOT_HEIGHT * _slotScale));

            int gridSlotsWidth = (COLUMNS * slotWidth) + ((COLUMNS - 1) * SLOT_GAP);
            int gridSlotsHeight = (rows * slotHeight) + ((rows - 1) * SLOT_GAP);

            int gridPanelWidth = gridSlotsWidth + (GRID_PADDING * 2);
            int gridPanelHeight = Math.Max(gridSlotsHeight + (GRID_PADDING * 2), MIN_DETAIL_HEIGHT);
            int contentHeight = Math.Max(gridPanelHeight, MIN_DETAIL_HEIGHT);
            int contentWidth = gridPanelWidth + CONTENT_GAP + DETAIL_WIDTH;

            int totalWidth = contentWidth + (OUTER_PADDING * 2);
            int totalHeight = HEADER_HEIGHT + contentHeight + OUTER_PADDING;

            ViewSize = new Point(totalWidth, totalHeight);
            ControlSize = ViewSize;

            _headerRectLocal = new Rectangle(0, 0, totalWidth, HEADER_HEIGHT);
            _contentRectLocal = new Rectangle(OUTER_PADDING, HEADER_HEIGHT, contentWidth, contentHeight);
            _gridRectLocal = new Rectangle(_contentRectLocal.X, _contentRectLocal.Y, gridPanelWidth, contentHeight);
            _detailRectLocal = new Rectangle(_gridRectLocal.Right + CONTENT_GAP, _contentRectLocal.Y, DETAIL_WIDTH, contentHeight);

            _titleLabel.ViewSize = new Point(totalWidth - (OUTER_PADDING * 2), 28);
            _titleLabel.X = OUTER_PADDING;

            int slotsStartX = _gridRectLocal.X + GRID_PADDING;
            int slotsStartY = _gridRectLocal.Y + GRID_PADDING;

            for (int i = 0; i < skills.Count; i++)
            {
                int row = i / COLUMNS;
                int col = i % COLUMNS;

                var slot = new SkillSlotControl
                {
                    Skill = skills[i],
                    X = slotsStartX + (col * (slotWidth + SLOT_GAP)),
                    Y = slotsStartY + (row * (slotHeight + SLOT_GAP)),
                    Scale = _slotScale,
                    IsTooltipEnabled = false,
                    ShowFooter = false
                };

                slot.Click += (sender, args) => OnSkillSlotClicked(slot);
                slot.HoverChanged += OnSkillSlotHover;
                slot.IsSelected = _selectedSkillId.HasValue && slot.Skill?.SkillId == _selectedSkillId.Value;
                _skillSlots.Add(slot);
                Controls.Add(slot);
            }

            _detailPanel.X = _detailRectLocal.X;
            _detailPanel.Y = _detailRectLocal.Y;
            _detailPanel.ControlSize = new Point(_detailRectLocal.Width, _detailRectLocal.Height);
            _detailPanel.ViewSize = _detailPanel.ControlSize;

            _detailNameLabel.ViewSize = new Point(_detailRectLocal.Width - DETAIL_PADDING * 2, 28);
            _detailTypeLabel.ViewSize = new Point(_detailRectLocal.Width - DETAIL_PADDING * 2, 22);

            int statsHeight = Math.Max(_detailRectLocal.Height - (DETAIL_PADDING + 54), 60);
            _detailStatsLabel.ViewSize = new Point(_detailRectLocal.Width - DETAIL_PADDING * 2, statsHeight);

            if (_selectedSkillId.HasValue)
            {
                HighlightSkill(_selectedSkillId.Value);
            }

            UpdateDetail(null);

            Visible = true;
            BringToFront();
            StartOpenAnimation();
        }

        /// <summary>
        /// Closes the panel.
        /// </summary>
        public void Close()
        {
            _isOpeningAnimation = false;
            _openAnimationElapsedSeconds = 0f;
            Alpha = 1f;
            Offset = Point.Zero;
            ApplyAlphaToChildren(1f);
            Visible = false;
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
            {
                return;
            }

            var spriteBatch = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            if (spriteBatch == null || pixel == null)
            {
                return;
            }

            DrawWindowFrame(spriteBatch, pixel, Alpha);

            var controls = Controls.GetSnapshotArray();
            for (int i = 0; i < controls.Length; i++)
            {
                controls[i].Draw(gameTime);
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!Visible)
            {
                return;
            }

            UpdateOpenAnimation(gameTime);
            HandleOutsideClickClose();
        }

        private void DrawWindowFrame(SpriteBatch spriteBatch, Texture2D pixel, float alpha)
        {
            Rectangle rect = DisplayRectangle;
            spriteBatch.Draw(pixel, rect, ModernHudTheme.BorderOuter * alpha);

            Rectangle inner = new(rect.X + 1, rect.Y + 1, Math.Max(1, rect.Width - 2), Math.Max(1, rect.Height - 2));
            UiDrawHelper.DrawVerticalGradient(spriteBatch, inner, new Color(20, 24, 32, 252) * alpha, new Color(10, 12, 16, 255) * alpha);
            UiDrawHelper.DrawCornerAccents(spriteBatch, rect, ModernHudTheme.Accent * 0.35f * alpha, size: 8, thickness: 1);

            Rectangle headerRect = ToDisplayRect(_headerRectLocal);
            UiDrawHelper.DrawPanel(
                spriteBatch,
                headerRect,
                ModernHudTheme.BgMid * alpha,
                ModernHudTheme.BorderInner * alpha,
                ModernHudTheme.BorderOuter * alpha,
                ModernHudTheme.BorderHighlight * 0.3f * alpha);

            var headerInner = new Rectangle(
                headerRect.X + 1,
                headerRect.Y + 1,
                Math.Max(1, headerRect.Width - 2),
                Math.Max(1, headerRect.Height - 2));
            UiDrawHelper.DrawVerticalGradient(spriteBatch, headerInner, ModernHudTheme.BgLight * alpha, ModernHudTheme.BgMid * alpha);

            spriteBatch.Draw(
                pixel,
                new Rectangle(headerInner.X + 10, headerInner.Bottom - 2, Math.Max(1, headerInner.Width - 20), 1),
                ModernHudTheme.Accent * 0.6f * alpha);

            Rectangle gridRect = ToDisplayRect(_gridRectLocal);
            DrawSectionPanel(spriteBatch, gridRect, ModernHudTheme.BgMid * alpha, ModernHudTheme.BgDarkest * alpha, alpha);

            Rectangle detailRect = ToDisplayRect(_detailRectLocal);
            DrawSectionPanel(spriteBatch, detailRect, new Color(20, 26, 36, 250) * alpha, new Color(9, 12, 17, 255) * alpha, alpha);

            spriteBatch.Draw(
                pixel,
                new Rectangle(detailRect.X + 1, detailRect.Y + 44, Math.Max(1, detailRect.Width - 2), 1),
                ModernHudTheme.BorderInner * 0.35f * alpha);
        }

        private static void DrawSectionPanel(SpriteBatch spriteBatch, Rectangle rect, Color topColor, Color bottomColor, float alpha)
        {
            UiDrawHelper.DrawPanel(
                spriteBatch,
                rect,
                topColor,
                ModernHudTheme.BorderInner * 0.7f * alpha,
                ModernHudTheme.BorderOuter * alpha,
                ModernHudTheme.BorderHighlight * 0.2f * alpha);

            var inner = new Rectangle(
                rect.X + 1,
                rect.Y + 1,
                Math.Max(1, rect.Width - 2),
                Math.Max(1, rect.Height - 2));

            UiDrawHelper.DrawVerticalGradient(spriteBatch, inner, topColor, bottomColor);
        }

        private void StartOpenAnimation()
        {
            _isOpeningAnimation = true;
            _openAnimationElapsedSeconds = 0f;
            Alpha = 0f;
            Offset = new Point(0, OPEN_ANIMATION_OFFSET_Y);
            ApplyAlphaToChildren(Alpha);
        }

        private void UpdateOpenAnimation(GameTime gameTime)
        {
            if (!_isOpeningAnimation)
            {
                return;
            }

            _openAnimationElapsedSeconds += (float)gameTime.ElapsedGameTime.TotalSeconds;
            float t = MathHelper.Clamp(_openAnimationElapsedSeconds / OPEN_ANIMATION_DURATION, 0f, 1f);
            float eased = 1f - MathF.Pow(1f - t, 3f);

            Alpha = eased;
            Offset = new Point(0, (int)MathF.Round((1f - eased) * OPEN_ANIMATION_OFFSET_Y));
            ApplyAlphaToChildren(eased);

            if (t >= 1f)
            {
                _isOpeningAnimation = false;
                Alpha = 1f;
                Offset = Point.Zero;
                ApplyAlphaToChildren(1f);
            }
        }

        private void HandleOutsideClickClose()
        {
            bool leftJustPressed =
                CurrentMouseState.LeftButton == ButtonState.Pressed &&
                PreviousMouseState.LeftButton == ButtonState.Released;

            if (!leftJustPressed)
            {
                return;
            }

            Point mousePos = CurrentMouseState.Position;
            if (DisplayRectangle.Contains(mousePos))
            {
                return;
            }

            Close();
            Scene?.SetMouseInputConsumed();
        }

        private void ApplyAlphaToChildren(float alpha)
        {
            var controls = Controls.GetSnapshotArray();
            for (int i = 0; i < controls.Length; i++)
            {
                ApplyAlphaRecursive(controls[i], alpha);
            }
        }

        private static void ApplyAlphaRecursive(GameControl control, float alpha)
        {
            control.Alpha = alpha;
            if (control is LabelControl label)
            {
                label.Alpha = alpha;
            }

            var children = control.Controls.GetSnapshotArray();
            for (int i = 0; i < children.Length; i++)
            {
                ApplyAlphaRecursive(children[i], alpha);
            }
        }

        private Rectangle ToDisplayRect(Rectangle localRect)
        {
            Point p = DisplayPosition;
            return new Rectangle(p.X + localRect.X, p.Y + localRect.Y, localRect.Width, localRect.Height);
        }

        private static float CalculateSlotScale(int rows)
        {
            float scale = TARGET_SLOT_SCALE;
            int availableHeight = Math.Max(300, UiScaler.VirtualSize.Y - 220);

            while (scale > 1.0f)
            {
                int slotHeight = Math.Max(1, (int)MathF.Round(SkillSlotControl.SLOT_HEIGHT * scale));
                int gridSlotsHeight = (rows * slotHeight) + ((rows - 1) * SLOT_GAP);
                int panelHeight = gridSlotsHeight + (GRID_PADDING * 2);
                if (panelHeight <= availableHeight)
                {
                    break;
                }

                scale -= 0.05f;
            }

            return Math.Max(1.0f, scale);
        }

        private void OnSkillSlotClicked(SkillSlotControl slot)
        {
            if (slot.Skill == null)
            {
                return;
            }

            _selectedSkillId = slot.Skill.SkillId;
            SkillSelected?.Invoke(slot.Skill);
            Close();
        }

        public void HighlightSkill(ushort skillId)
        {
            _selectedSkillId = skillId;

            SkillEntryState? selected = null;
            foreach (var slot in _skillSlots)
            {
                bool isMatch = slot.Skill?.SkillId == skillId;
                slot.IsSelected = isMatch;
                if (isMatch)
                {
                    selected = slot.Skill;
                }
            }

        }

        private void OnSkillSlotHover(SkillEntryState? skill)
        {
            UpdateDetail(skill);
        }

        private void UpdateDetail(SkillEntryState? skill)
        {
            if (skill == null)
            {
                _detailNameLabel.Text = "Skill Info";
                _detailTypeLabel.Text = string.Empty;
                _detailStatsLabel.Text = "Hover a skill to see details.";
                _detailStatsLabel.TextColor = ModernHudTheme.TextGray;
                return;
            }

            var definition = SkillDatabase.GetSkillDefinition(skill.SkillId);
            var type = SkillDatabase.GetSkillType(skill.SkillId);

            string typeText = type switch
            {
                SkillType.Area => "Area",
                SkillType.Self => "Self",
                _ => "Target"
            };

            _detailNameLabel.Text = SkillDatabase.GetSkillName(skill.SkillId);
            _detailTypeLabel.Text = $"Type: {typeText}  |  Level {skill.SkillLevel}";

            var sb = new StringBuilder();
            sb.AppendLine($"Skill ID: {skill.SkillId}");

            if (definition != null)
            {
                if (definition.RequiredLevel > 0)
                {
                    sb.AppendLine($"Required Level: {definition.RequiredLevel}");
                }
                if (definition.RequiredStrength > 0)
                {
                    sb.AppendLine($"Required Strength: {definition.RequiredStrength}");
                }
                if (definition.RequiredDexterity > 0)
                {
                    sb.AppendLine($"Required Dexterity: {definition.RequiredDexterity}");
                }
                if (definition.RequiredEnergy > 0)
                {
                    sb.AppendLine($"Required Energy: {definition.RequiredEnergy}");
                }
                if (definition.RequiredLeadership > 0)
                {
                    sb.AppendLine($"Required Command: {definition.RequiredLeadership}");
                }

                if (definition.ManaCost > 0 || definition.AbilityGaugeCost > 0)
                {
                    sb.Append("Cost: ");
                    if (definition.ManaCost > 0)
                    {
                        sb.Append($"Mana {definition.ManaCost}");
                    }
                    if (definition.AbilityGaugeCost > 0)
                    {
                        if (definition.ManaCost > 0)
                        {
                            sb.Append(" | ");
                        }
                        sb.Append($"AG {definition.AbilityGaugeCost}");
                    }
                    sb.AppendLine();
                }

                if (definition.Damage > 0)
                {
                    sb.AppendLine($"Base Damage: {definition.Damage}");
                }
                if (definition.Distance > 0)
                {
                    sb.AppendLine($"Range: {definition.Distance}");
                }
                if (definition.Delay > 0)
                {
                    sb.AppendLine($"Cooldown: {definition.Delay} ms");
                }
            }

            if (sb.Length == 0)
            {
                sb.Append("No additional data available.");
            }

            _detailStatsLabel.Text = sb.ToString();
            _detailStatsLabel.TextColor = ModernHudTheme.TextWhite;
        }

        public IEnumerable<string> GetPreloadTexturePaths() => SkillIconAtlas.TexturePaths;
    }
}
