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

        // 手機：確認鈕與關閉鈕。桌面靠滑鼠停留看說明、點一下就指派，
        // 觸控沒有「停留」這個動作 —— 點下去就直接指派並關窗，
        // 於是說明欄永遠是空的，玩家根本讀不到任何技能資訊。
        private const int ASSIGN_BUTTON_HEIGHT = 46;
        private const int ASSIGN_BUTTON_MARGIN = 12;
        private const int CLOSE_BUTTON_SIZE = 38;

        // 手機的格子尺寸。桌面的 28x48 是配著滑鼠與底部快捷列設計的直立小格，
        // 在 iPhone 上換算後只有約 13x19 pt —— 遠低於可以放心點的大小（44 pt）。
        // 手機改成正方形大格：形狀更適合圓角觸控目標，也讓 20x28 的圖示放得更大。
        // 一行 4 格。更多欄數雖然能讓每格更大，但面板會橫向撐滿整個手機螢幕，
        // 反而不好用 —— 眼睛要掃過整個畫面才找得到一個技能。
        private const int MOBILE_COLUMNS = 4;

        // 技能非常多（例如 GM 角色學了整棵樹）時，4 欄會排到畫面外。
        // 那種情況才允許加欄，一般角色永遠是 4 欄。
        private const int MOBILE_COLUMNS_MAX = 8;

        private const int MOBILE_SLOT_GAP = 14;
        private const int MOBILE_SLOT_MIN = 58;    // 約 40 pt，好按的下限
        private const int MOBILE_SLOT_FLOOR = 44;  // 約 30 pt，技能很多時才會用到的底線
        private const int MOBILE_SLOT_MAX = 104;
        private const int MOBILE_DETAIL_WIDTH = 360;
        private const int MOBILE_GRID_MARGIN_Y = 200;   // 標題列、確認鈕與上下留白

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

        private static bool IsMobile => MobileUi.IsMobile;

        /// <summary>手機上「點一下先預覽」所選中的技能，再點一次（或按確認鈕）才指派。</summary>
        private SkillEntryState? _previewSkill;
        private Rectangle _assignRectLocal;
        private Rectangle _closeRectLocal;
        private bool _assignPressed;
        private bool _closePressed;

        /// <summary>指派目標的名稱（例如「SKILL 2」），由呼叫端在 Open 之前設定。</summary>
        public string? AssignTargetLabel { get; set; }

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

            bool mobile = IsMobile;
            int columns = mobile ? MOBILE_COLUMNS : COLUMNS;
            int slotGap = mobile ? MOBILE_SLOT_GAP : SLOT_GAP;
            int detailWidth = mobile ? MOBILE_DETAIL_WIDTH : DETAIL_WIDTH;

            int rows;
            int slotWidth, slotHeight;

            if (mobile)
            {
                // 正方形大格。先固定 4 欄算尺寸；只有在「連縮到底線都排不進畫面」時才加欄。
                _slotScale = 1f;   // 手機直接指定格子尺寸，不靠 Scale 放大

                var virtualSize = UiScaler.VirtualSize;
                int availableHeight = virtualSize.Y - MOBILE_GRID_MARGIN_Y;

                int size;
                while (true)
                {
                    rows = Math.Max(1, (int)Math.Ceiling(skills.Count / (float)columns));

                    int availableWidth = virtualSize.X
                        - detailWidth - CONTENT_GAP - (OUTER_PADDING * 2) - (GRID_PADDING * 2) - 40;

                    int byHeight = (availableHeight - ((rows - 1) * slotGap) - (GRID_PADDING * 2)) / rows;
                    int byWidth = (availableWidth - ((columns - 1) * slotGap)) / columns;

                    size = Math.Clamp(Math.Min(byHeight, byWidth), MOBILE_SLOT_FLOOR, MOBILE_SLOT_MAX);

                    // 縮到底線之後仍然超出畫面高度，才退一步加欄
                    int neededHeight = (rows * size) + ((rows - 1) * slotGap) + (GRID_PADDING * 2);
                    if (neededHeight <= availableHeight || columns >= MOBILE_COLUMNS_MAX)
                        break;

                    columns++;
                }

                slotWidth = slotHeight = size;
            }
            else
            {
                rows = (int)Math.Ceiling(skills.Count / (float)columns);
                if (rows == 0)
                {
                    rows = 1;
                }

                _slotScale = CalculateSlotScale(rows);
                slotWidth = Math.Max(1, (int)MathF.Round(SkillSlotControl.SLOT_WIDTH * _slotScale));
                slotHeight = Math.Max(1, (int)MathF.Round(SkillSlotControl.SLOT_HEIGHT * _slotScale));
            }

            int gridSlotsWidth = (columns * slotWidth) + ((columns - 1) * slotGap);
            int gridSlotsHeight = (rows * slotHeight) + ((rows - 1) * slotGap);

            int gridPanelWidth = gridSlotsWidth + (GRID_PADDING * 2);
            int gridPanelHeight = Math.Max(gridSlotsHeight + (GRID_PADDING * 2), MIN_DETAIL_HEIGHT);
            int contentHeight = Math.Max(gridPanelHeight, MIN_DETAIL_HEIGHT);
            int contentWidth = gridPanelWidth + CONTENT_GAP + detailWidth;

            int totalWidth = contentWidth + (OUTER_PADDING * 2);
            int totalHeight = HEADER_HEIGHT + contentHeight + OUTER_PADDING;

            ViewSize = new Point(totalWidth, totalHeight);
            ControlSize = ViewSize;

            _headerRectLocal = new Rectangle(0, 0, totalWidth, HEADER_HEIGHT);
            _contentRectLocal = new Rectangle(OUTER_PADDING, HEADER_HEIGHT, contentWidth, contentHeight);
            _gridRectLocal = new Rectangle(_contentRectLocal.X, _contentRectLocal.Y, gridPanelWidth, contentHeight);
            _detailRectLocal = new Rectangle(_gridRectLocal.Right + CONTENT_GAP, _contentRectLocal.Y, detailWidth, contentHeight);

            _titleLabel.ViewSize = new Point(totalWidth - (OUTER_PADDING * 2), 28);
            _titleLabel.X = OUTER_PADDING;

            int slotsStartX = _gridRectLocal.X + GRID_PADDING;
            int slotsStartY = _gridRectLocal.Y + GRID_PADDING;

            for (int i = 0; i < skills.Count; i++)
            {
                int row = i / columns;
                int col = i % columns;

                var slot = new SkillSlotControl
                {
                    Skill = skills[i],
                    X = slotsStartX + (col * (slotWidth + slotGap)),
                    Y = slotsStartY + (row * (slotHeight + slotGap)),
                    Scale = _slotScale,
                    IsTooltipEnabled = false,
                    ShowFooter = false
                };

                if (mobile)
                {
                    // DisplaySize = ViewSize x Scale（GameControl），Scale 已設為 1，
                    // 所以直接指定 ViewSize 就能得到正方形的大格子。
                    slot.ControlSize = new Point(slotWidth, slotHeight);
                    slot.ViewSize = slot.ControlSize;
                }

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

            // 手機在說明欄底部保留確認鈕的高度，標題列右側保留關閉鈕
            if (IsMobile)
            {
                _assignRectLocal = new Rectangle(
                    _detailRectLocal.X + ASSIGN_BUTTON_MARGIN,
                    _detailRectLocal.Bottom - ASSIGN_BUTTON_MARGIN - ASSIGN_BUTTON_HEIGHT,
                    _detailRectLocal.Width - ASSIGN_BUTTON_MARGIN * 2,
                    ASSIGN_BUTTON_HEIGHT);

                _closeRectLocal = new Rectangle(
                    totalWidth - OUTER_PADDING - CLOSE_BUTTON_SIZE,
                    (HEADER_HEIGHT - CLOSE_BUTTON_SIZE) / 2,
                    CLOSE_BUTTON_SIZE,
                    CLOSE_BUTTON_SIZE);
            }
            else
            {
                _assignRectLocal = Rectangle.Empty;
                _closeRectLocal = Rectangle.Empty;
            }

            int reservedBottom = IsMobile ? ASSIGN_BUTTON_HEIGHT + ASSIGN_BUTTON_MARGIN * 2 : 0;
            int statsHeight = Math.Max(_detailRectLocal.Height - (DETAIL_PADDING + 54) - reservedBottom, 60);
            _detailStatsLabel.ViewSize = new Point(_detailRectLocal.Width - DETAIL_PADDING * 2, statsHeight);

            _previewSkill = null;
            _assignPressed = false;
            _closePressed = false;

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
            _previewSkill = null;
            _assignPressed = false;
            _closePressed = false;
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

            if (IsMobile)
            {
                DrawMobileButtons(spriteBatch, pixel, Alpha);
            }
        }

        /// <summary>手機的確認鈕與關閉鈕。子控制項畫完才畫，才不會被說明欄蓋住。</summary>
        private void DrawMobileButtons(SpriteBatch spriteBatch, Texture2D pixel, float alpha)
        {
            var font = GraphicsManager.Instance.Font;

            if (_assignRectLocal.Width > 0)
            {
                Rectangle rect = ToDisplayRect(_assignRectLocal);
                bool ready = _previewSkill != null;

                Color fill = ready
                    ? (_assignPressed ? new Color(70, 110, 160) : new Color(44, 72, 108)) * alpha
                    : new Color(26, 30, 38) * (0.8f * alpha);

                UiDrawHelper.DrawPanel(
                    spriteBatch,
                    rect,
                    fill,
                    ModernHudTheme.BorderInner * alpha,
                    ModernHudTheme.BorderOuter * alpha,
                    ModernHudTheme.BorderHighlight * 0.3f * alpha);

                if (font != null)
                {
                    string label = ready
                        ? (string.IsNullOrEmpty(AssignTargetLabel) ? "ASSIGN" : $"ASSIGN → {AssignTargetLabel}")
                        : "TAP A SKILL";

                    Color textColor = (ready ? ModernHudTheme.TextGold : ModernHudTheme.TextGray) * alpha;
                    DrawCenteredText(spriteBatch, font, label, rect, 0.62f, textColor);
                }
            }

            if (_closeRectLocal.Width > 0)
            {
                Rectangle rect = ToDisplayRect(_closeRectLocal);
                UiDrawHelper.DrawPanel(
                    spriteBatch,
                    rect,
                    (_closePressed ? new Color(120, 52, 52) : new Color(40, 26, 26)) * alpha,
                    ModernHudTheme.BorderInner * alpha,
                    ModernHudTheme.BorderOuter * alpha,
                    ModernHudTheme.BorderHighlight * 0.3f * alpha);

                if (font != null)
                {
                    DrawCenteredText(spriteBatch, font, "X", rect, 0.7f, ModernHudTheme.TextWhite * alpha);
                }
            }
        }

        private static void DrawCenteredText(SpriteBatch spriteBatch, SpriteFont font, string text,
            Rectangle rect, float scale, Color color)
        {
            var size = font.MeasureString(text) * scale;
            var position = new Vector2(
                rect.X + (rect.Width - size.X) * 0.5f,
                rect.Y + (rect.Height - size.Y) * 0.5f);

            spriteBatch.DrawString(font, text, position, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!Visible)
            {
                return;
            }

            UpdateOpenAnimation(gameTime);

            if (IsMobile && HandleMobileButtons())
            {
                return;
            }

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

            // 手機：第一下只預覽 —— 觸控沒有「滑鼠停留」，
            // 原本點下去就指派並關窗，說明欄根本沒有機會被讀到。
            // 第二下（或按確認鈕）才真的指派，和選角的「點兩下進入」一致。
            if (IsMobile && _previewSkill?.SkillId != slot.Skill.SkillId)
            {
                _previewSkill = slot.Skill;
                HighlightSkill(slot.Skill.SkillId);
                UpdateDetail(slot.Skill);
                return;
            }

            Commit(slot.Skill);
        }

        private void Commit(SkillEntryState skill)
        {
            _selectedSkillId = skill.SkillId;
            SkillSelected?.Invoke(skill);
            Close();
        }

        /// <summary>
        /// 手機的確認鈕與關閉鈕。和 <see cref="HandleOutsideClickClose"/> 一樣直接讀滑鼠狀態 ——
        /// 這兩個是自繪的矩形，沒有對應的子控制項。
        /// </summary>
        /// <returns>是否吃掉了這一次點擊。</returns>
        private bool HandleMobileButtons()
        {
            bool pressed = CurrentMouseState.LeftButton == ButtonState.Pressed;
            bool justPressed = pressed && PreviousMouseState.LeftButton == ButtonState.Released;
            bool justReleased = !pressed && PreviousMouseState.LeftButton == ButtonState.Pressed;
            Point mouse = CurrentMouseState.Position;

            bool overAssign = _previewSkill != null && ToDisplayRect(_assignRectLocal).Contains(mouse);
            bool overClose = _closeRectLocal.Width > 0 && ToDisplayRect(_closeRectLocal).Contains(mouse);

            if (justPressed)
            {
                _assignPressed = overAssign;
                _closePressed = overClose;
                return _assignPressed || _closePressed;
            }

            if (!justReleased)
            {
                return _assignPressed || _closePressed;
            }

            bool assign = _assignPressed && overAssign;
            bool close = _closePressed && overClose;
            _assignPressed = false;
            _closePressed = false;

            if (assign && _previewSkill != null)
            {
                Commit(_previewSkill);
                Scene?.SetMouseInputConsumed();
                return true;
            }

            if (close)
            {
                Close();
                Scene?.SetMouseInputConsumed();
                return true;
            }

            return false;
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
                _detailStatsLabel.Text = IsMobile
                    ? "Tap a skill to read it.\nTap again to assign."
                    : "Hover a skill to see details.";
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

            // 型別本身就是最重要的資訊 —— 它決定按鈕按下去會發生什麼事。
            // 手機的技能鈕是自動鎖定，玩家更需要知道「這顆會打誰」。
            sb.AppendLine(type switch
            {
                SkillType.Area => "Hits everything around the impact point.",
                SkillType.Self => "Cast on yourself. No target needed.",
                _ => "Single target. Locks the nearest enemy."
            });
            sb.AppendLine();
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
