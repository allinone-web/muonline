#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Controls.UI.Game.Skills;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game.Hud
{
    public sealed class ModernBottomHud : UIControl
    {
        // ──────────────── Bar-specific colors ────────────────
        private static readonly Color HpColor = new(200, 45, 45);
        private static readonly Color HpColorBright = new(255, 80, 80);
        private static readonly Color HpColorDark = new(100, 18, 18);
        private static readonly Color HpGlow = new(255, 60, 60, 50);

        private static readonly Color MpColor = new(55, 120, 210);
        private static readonly Color MpColorBright = new(100, 170, 255);
        private static readonly Color MpColorDark = new(25, 55, 110);
        private static readonly Color MpGlow = new(80, 150, 255, 50);

        private static readonly Color SdColor = new(210, 185, 50);
        private static readonly Color SdColorBright = new(255, 230, 90);
        private static readonly Color SdColorDark = new(110, 90, 20);
        private static readonly Color SdGlow = new(255, 220, 60, 45);

        private static readonly Color AgColor = new(150, 70, 200);
        private static readonly Color AgColorBright = new(200, 120, 255);
        private static readonly Color AgColorDark = new(70, 30, 100);
        private static readonly Color AgGlow = new(180, 100, 255, 45);

        private static readonly Color ExpColor = new(212, 175, 85);
        private static readonly Color ExpColorBright = new(255, 220, 130);
        private static readonly Color ExpColorDark = new(110, 88, 35);
        private static readonly Color ExpGlow = new(255, 210, 100, 35);

        private static readonly Color CompanionColorGood = new(92, 188, 122);
        private static readonly Color CompanionColorWarn = new(234, 186, 78);
        private static readonly Color CompanionColorDanger = new(220, 88, 88);
        private static readonly HashSet<int> HelperLifeIds = new() { 0, 1, 2, 3, 4 };

        // ──────────────── State ────────────────
        private readonly CharacterState _state;
        private readonly SkillSelectionPanel _skillPanel;

        private SpriteFont? _font;
        private Point _lastVirtualSize = Point.Zero;
        private double _totalTime;

        // Resource bar display values (lerped for animation)
        private float _displayHpPct, _displayMpPct, _displaySdPct, _displayAgPct;
        private float _displayExpPct;
        private float _targetHpPct, _targetMpPct, _targetSdPct, _targetAgPct;
        private uint _lastCurrentHealth = uint.MaxValue;
        private uint _lastMaximumHealth = uint.MaxValue;
        private uint _lastCurrentShield = uint.MaxValue;
        private uint _lastMaximumShield = uint.MaxValue;
        private uint _lastCurrentMana = uint.MaxValue;
        private uint _lastMaximumMana = uint.MaxValue;
        private uint _lastCurrentAbility = uint.MaxValue;
        private uint _lastMaximumAbility = uint.MaxValue;
        private string _healthText = string.Empty;
        private string _shieldText = string.Empty;
        private string _manaText = string.Empty;
        private string _abilityText = string.Empty;
        private const float LerpSpeed = 6f;

        // 掉的時候快、回的時候慢。
        //
        // 這不只是好看：受傷是**要立刻知道**的資訊，弧線必須馬上縮到位；
        // 回復則是持續發生的過程，慢慢長回去才看得出「正在回」。
        // 兩邊用同一個速度會讓受傷顯得遲鈍、回復顯得跳動。
        private const float LerpSpeedFalling = 14f;
        private const float LerpSpeedRising = 3.5f;

        private static float LerpResource(float current, float target, float dt)
        {
            float speed = target < current ? LerpSpeedFalling : LerpSpeedRising;
            return MathHelper.Lerp(current, target, MathHelper.Clamp(speed * dt, 0f, 1f));
        }

        // Layout rects (recomputed on resize)
        private Rectangle _panelRect;
        /// <summary>寵物／守護獸血條的擺放區域。桌面是底部面板，手機是畫面上緣中央。</summary>
        private Rectangle _companionAreaRect;
        private Rectangle _hpBarRect, _sdBarRect, _mpBarRect, _agBarRect;
        private Rectangle _expBarRect;
        private Rectangle[] _slotRects = Array.Empty<Rectangle>();
        private Rectangle[] _btnRects = Array.Empty<Rectangle>();
        private float _barFontScale;
        private float _slotFontScale;
        private float _btnFontScale;
        private float _expFontScale;
        private readonly CompanionLifeInfo?[] _companionInfos = new CompanionLifeInfo?[2];

        // Skill slots: 0-2 = potion (Q/W/E), 3-12 = skills (1-0)
        private const int SlotCount = 13;
        private const int PotionSlotCount = 3;
        private readonly SkillEntryState?[] _slotSkills = new SkillEntryState?[SlotCount];
        private int _activeSkillSlot = 3;
        private int _pendingAssignSlot = -1;
        private bool _quickSlotsRestored;
        private bool _lastDarkRavenEquipped;

        // Potion slot assignments (Q=0, W=1, E=2) — stores item type
        private readonly (byte Group, int Id)?[] _potionAssignments = new (byte, int)?[PotionSlotCount];
        private readonly Dictionary<string, Texture2D> _potionTextureCache = new();
        private const int PotionIconCacheSize = 48; // fixed size for BMD preview caching

        // Potion picker popup
        private bool _potionPickerOpen;
        private int _potionPickerSlot = -1;
        private readonly List<PotionCandidate> _potionCandidates = new();
        private int _hoveredPotionCandidate = -1;
        private Rectangle _potionPickerRect;
        private Rectangle[] _potionPickerItemRects = Array.Empty<Rectangle>();

        // Interface buttons
        private static readonly string[] ButtonLabels = { "MENU", "CHAR", "INV", "PARTY", "GUILD", "QUEST" };

        // 手機版的按鈕組：GUILD 與 QUEST 目前沒有實作（點了不會有反應），
        // 手機空間寶貴，換成真正需要的地圖與聊天 —— 兩者在桌面是靠鍵盤開啟的，
        // 手機沒有鍵盤，等於原本完全無法使用。
        //
        // PARTY 換成 SKILL：技能面板原本只有「長按右下角的技能鈕」一條路進得去，
        // 沒人告訴玩家要長按。組隊在手機上還可以從角色資訊那邊處理，
        // 技能沒有第二條路。
        // CHAR 拿掉了：左上角的頭像本來就是「打開角色資訊」的入口
        //（見 UpdateMobileTouch 的 avatarWasPressed），一個功能不需要兩顆按鈕。
        // 少一顆之後右上角是 3 欄 x 2 列裡的 5 格，最後一格留白。
        private static readonly string[] MobileButtonLabels = { "MENU", "BAG", "SKILL", "MAP", "CHAT" };
        private static readonly int[] MobileButtonActions = { 0, 2, 8, 6, 7 };

        /// <summary>手機右下角的技能鈕數量。直接引用來源常數，避免兩邊各自改動而失準。</summary>
        private const int MobileSkillButtonCount = TouchActionButtonsControl.MaxSkillButtons;

        private static bool IsMobile => MobileUi.IsMobile;
        private static string[] ActiveButtonLabels => IsMobile ? MobileButtonLabels : ButtonLabels;

        private int _hoveredButton = -1;
        private int _hoveredSlot = -1;

        // --- 觸控長按 ---
        // 桌面上「點擊格子」是指派、「按鍵盤 Q/W/E/1-0」才是使用。
        // 手機沒有鍵盤，等於永遠無法使用藥水與技能。
        // 改為：輕點 = 使用（格子已有內容時），長按 = 指派。
        private int _pressedSlot = -1;
        private double _pressElapsedSeconds;
        private bool _longPressHandled;
        private const double LongPressSeconds = 0.45;

        // 手機自行處理的觸控狀態（見 UpdateMobileTouch）
        private bool _mobileWasPressed;
        private int _mobilePressedButton = -1;

        // 手機右上角的經驗條與狀態列、左上角的數值文字（見 RefreshMobileLayout）
        private Rectangle _vitalsTextRect;
        private Rectangle _statusReadoutRect;
        private string _statusText;
        private double _statusTextBuiltAt = double.NegativeInfinity;

        // 手機左上角的圓形頭像框（見 RefreshMobileLayout / DrawAvatar）
        private Vector2 _avatarCenter;
        private float _avatarRadius;
        private Rectangle _avatarRect;
        private bool _avatarPressed;

        // Keyboard
        private static readonly Keys[] SlotKeys =
        {
            Keys.Q, Keys.W, Keys.E,
            Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5,
            Keys.D6, Keys.D7, Keys.D8, Keys.D9, Keys.D0
        };
        private static readonly string[] SlotKeyLabels =
        {
            "Q", "W", "E",
            "1", "2", "3", "4", "5", "6", "7", "8", "9", "0"
        };

        private readonly record struct CompanionLifeInfo(string Name, int Current, int Maximum, Color FillColor);

        public SkillEntryState? SelectedSkill => _slotSkills[_activeSkillSlot];

        /// <summary>
        /// 手機的第 <paramref name="index"/> 顆技能鈕對應的技能（可能為 null = 未指派）。
        ///
        /// 直接對應快捷格 3、4、5…，而不是「已指派技能的第 N 個」——
        /// 後者在中間的格子被清空時，所有按鈕的內容會整批位移，
        /// 玩家的肌肉記憶會失效。
        /// </summary>
        public SkillEntryState? GetMobileSkill(int index)
        {
            int slot = PotionSlotCount + index;
            return slot >= PotionSlotCount && slot < SlotCount ? _slotSkills[slot] : null;
        }

        /// <summary>開啟技能選擇面板，把選到的技能指派給手機的第 N 顆技能鈕。</summary>
        public void OpenMobileSkillAssignment(int index)
        {
            int slot = PotionSlotCount + index;
            if (slot < PotionSlotCount || slot >= SlotCount)
                return;

            _pendingAssignSlot = slot;
            _skillPanel.AssignTargetLabel = MobileSkillButtonLabel(slot);
            _skillPanel.Open(_state);
        }

        /// <summary>
        /// 從右上角的 SKILL 鈕開啟技能面板。
        ///
        /// 沒有指定要指派到哪一顆按鈕，因此挑第一個空的技能鈕；四顆都滿了就換掉
        /// 目前選中的那顆。無論如何都會把目標寫在確認鈕上，不會默默覆蓋。
        /// </summary>
        public void OpenMobileSkillBrowser()
        {
            int slot = -1;
            for (int i = PotionSlotCount; i < PotionSlotCount + MobileSkillButtonCount && i < SlotCount; i++)
            {
                if (_slotSkills[i] == null)
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                slot = Math.Clamp(_activeSkillSlot, PotionSlotCount, PotionSlotCount + MobileSkillButtonCount - 1);
            }

            _pendingAssignSlot = slot;
            _skillPanel.AssignTargetLabel = MobileSkillButtonLabel(slot);
            _skillPanel.Open(_state);
        }

        private static string MobileSkillButtonLabel(int slot) => $"SKILL {slot - PotionSlotCount + 1}";

        public ModernBottomHud(CharacterState state, SkillSelectionPanel skillPanel)
        {
            _state = state;
            _skillPanel = skillPanel;

            AutoViewSize = false;

            // 手機的 HUD 元素散布在畫面四角，控制項必須涵蓋整個畫面；
            // 但只要 Interactive = true，任何一次觸控都會把場景焦點搶過來
            // （GameControl 命中後會呼叫 FocusControlIfInteractive），
            // 聊天輸入框會因此失焦、iOS 鍵盤跟著收起來。
            // 因此手機改為自行處理觸控，見 UpdateMobileTouch。
            Interactive = !IsMobile;
            BackgroundColor = Color.Transparent;
            BorderColor = Color.Transparent;
            BorderThickness = 0;

            _skillPanel.SkillSelected += OnSkillSelectedFromPanel;

            RefreshLayout();
        }

        protected override void OnScreenSizeChanged()
        {
            base.OnScreenSizeChanged();
            _lastVirtualSize = Point.Zero;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            RefreshLayout();

            if (_lastDarkRavenEquipped != _state.IsDarkRavenEquipped)
            {
                _lastDarkRavenEquipped = _state.IsDarkRavenEquipped;
                _quickSlotsRestored = false;
            }

            RestoreQuickSlotsIfNeeded();

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _totalTime = gameTime.TotalGameTime.TotalSeconds;

            UpdateSlotLongPress(gameTime);
            if (IsMobile)
                UpdateMobileTouch();

            _targetHpPct = _state.MaximumHealth > 0 ? _state.CurrentHealth / (float)_state.MaximumHealth : 0f;
            _targetMpPct = _state.MaximumMana > 0 ? _state.CurrentMana / (float)_state.MaximumMana : 0f;
            _targetSdPct = _state.MaximumShield > 0 ? _state.CurrentShield / (float)_state.MaximumShield : 0f;
            _targetAgPct = _state.MaximumAbility > 0 ? _state.CurrentAbility / (float)_state.MaximumAbility : 0f;
            RefreshResourceTexts();

            // 經驗值只會往前，升級時才會歸零重來 —— 歸零要立刻，往前才補間
            float targetExp = MathHelper.Clamp((float)(CalculateExpPercent() / 100.0), 0f, 1f);
            _displayExpPct = targetExp < _displayExpPct
                ? targetExp
                : MathHelper.Lerp(_displayExpPct, targetExp, MathHelper.Clamp(4f * dt, 0f, 1f));

            if (IsMobile)
            {
                // 手機的生命與魔力是頭像外圈的弧線，變化的節奏就是玩家讀狀態的方式
                _displayHpPct = LerpResource(_displayHpPct, _targetHpPct, dt);
                _displayMpPct = LerpResource(_displayMpPct, _targetMpPct, dt);
                _displaySdPct = LerpResource(_displaySdPct, _targetSdPct, dt);
                _displayAgPct = LerpResource(_displayAgPct, _targetAgPct, dt);
            }
            else
            {
                _displayHpPct = MathHelper.Lerp(_displayHpPct, _targetHpPct, LerpSpeed * dt);
                _displayMpPct = MathHelper.Lerp(_displayMpPct, _targetMpPct, LerpSpeed * dt);
                _displaySdPct = MathHelper.Lerp(_displaySdPct, _targetSdPct, LerpSpeed * dt);
                _displayAgPct = MathHelper.Lerp(_displayAgPct, _targetAgPct, LerpSpeed * dt);
            }

            RefreshCompanionLifeInfos();
            HandleKeyboard();
            HandleMouseHover();
            HandlePotionPickerClick();
            EnsurePotionIconsCached();
        }

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            var spriteBatch = GraphicsManager.Instance.Sprite;
            if (spriteBatch == null)
                return;

            SpriteBatchScope? scope = null;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                scope = new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.LinearClamp,
                    transform: UiScaler.SpriteTransform);
            }

            try
            {
                _font ??= GraphicsManager.Instance.Font;
                if (_font == null)
                    return;

                var pixel = GraphicsManager.Instance.Pixel;
                if (pixel == null)
                    return;

                // 手機沒有底部面板，畫了會蓋住遊戲畫面（版面見 RefreshMobileLayout）
                if (!IsMobile)
                    DrawPanelBackground(spriteBatch, pixel);

                DrawCompanionLifeBars(spriteBatch, pixel);

                if (IsMobile)
                {
                    // 生命與魔力由頭像外圈的兩道弧線表示，數值以純文字補上 ——
                    // 四條彩色長條在手機上佔位置又太花，見 DrawAvatar / DrawVitalsText。
                    DrawAvatar(spriteBatch);
                    DrawVitalsText(spriteBatch);
                    DrawStatusReadout(spriteBatch);
                    DrawZenReadout(spriteBatch);
                }
                else
                {
                    // Left bars: HP + SD (next to quick slots)
                    DrawResourceBar(spriteBatch, pixel, _hpBarRect, _displayHpPct,
                        HpColorDark, HpColor, HpColorBright, HpGlow,
                        _healthText, "HP", critical: _targetHpPct < 0.25f);
                    DrawResourceBar(spriteBatch, pixel, _sdBarRect, _displaySdPct,
                        SdColorDark, SdColor, SdColorBright, SdGlow,
                        _shieldText, "SD", critical: false);

                    // Right bars: MP + AG (next to quick slots)
                    DrawResourceBar(spriteBatch, pixel, _mpBarRect, _displayMpPct,
                        MpColorDark, MpColor, MpColorBright, MpGlow,
                        _manaText, "MP", critical: _targetMpPct < 0.15f);
                    DrawResourceBar(spriteBatch, pixel, _agBarRect, _displayAgPct,
                        AgColorDark, AgColor, AgColorBright, AgGlow,
                        _abilityText, "AG", critical: false);
                }

                DrawQuickSlots(spriteBatch, pixel);
                DrawInterfaceButtons(spriteBatch, pixel);
                DrawExpBar(spriteBatch, pixel);

                if (_potionPickerOpen)
                    DrawPotionPicker(spriteBatch, pixel);
            }
            finally
            {
                scope?.Dispose();
            }
        }

        public override bool OnClick()
        {
            // 手機一律不走這條路。
            //
            // 分派有兩條：這裡（UI 點擊路由）和 UpdateMobileTouch（自行處理觸控）。
            // 手機靠建構子裡的 Interactive = !IsMobile 讓 OnClick 不會被呼叫 ——
            // 但那是「只要沒有人改那一行就成立」的約定，而不是保證。
            // 哪天有人為了別的理由把 Interactive 打開，兩條路就會同時觸發，
            // 症狀是每個按鈕都做兩次（背包開了又關、技能放兩次）。
            // 這裡直接擋掉，讓那個約定變成保證。
            if (IsMobile)
                return false;

            base.OnClick();

            var mousePos = MuGame.Instance.UiMouseState;

            // If picker is open, clicks are handled in HandlePotionPickerClick (Update)
            if (_potionPickerOpen)
                return true;

            for (int i = 0; i < _slotRects.Length; i++)
            {
                if (_slotRects[i].Contains(mousePos.X, mousePos.Y))
                {
                    // 長按已經開過指派面板了，放開時不要再觸發使用
                    if (_longPressHandled)
                    {
                        _longPressHandled = false;
                        return true;
                    }

                    ActivateOrAssignSlot(i);
                    return true;
                }
            }

            for (int i = 0; i < _btnRects.Length; i++)
            {
                if (_btnRects[i].Contains(mousePos.X, mousePos.Y))
                {
                    OnButtonClicked(IsMobile && i < MobileButtonActions.Length ? MobileButtonActions[i] : i);
                    return true;
                }
            }

            if (_panelRect.Contains(mousePos.X, mousePos.Y) || _expBarRect.Contains(mousePos.X, mousePos.Y))
                return true;

            return false;
        }

        /// <summary>
        /// 輕點格子：已有內容就直接使用（等同桌面按 Q/W/E 或 1-0），
        /// 空格子則開啟指派面板。長按一律開啟指派面板（見 UpdateSlotLongPress）。
        /// </summary>
        private void ActivateOrAssignSlot(int slot)
        {
            if (slot < PotionSlotCount)
            {
                bool hasPotion = _potionAssignments[slot].HasValue;
                if (hasPotion)
                    ConsumePotionInSlot(slot);
                else
                    OpenPotionPicker(slot);
                return;
            }

            bool hasSkill = _slotSkills[slot] != null;
            if (hasSkill)
            {
                _activeSkillSlot = slot;
                PersistQuickSlots();
            }
            else
            {
                _pendingAssignSlot = slot;
                _skillPanel.Open(_state);
            }
        }

        /// <summary>
        /// 長按格子開啟指派面板 —— 手機上沒有右鍵或其他修飾鍵可用。
        /// </summary>
        private void UpdateSlotLongPress(GameTime gameTime)
        {
            if (_potionPickerOpen)
            {
                _pressedSlot = -1;
                return;
            }

            var mouse = MuGame.Instance.UiMouseState;
            bool pressed = mouse.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed;

            if (!pressed)
            {
                _pressedSlot = -1;
                _pressElapsedSeconds = 0;
                return;
            }

            int slotUnderCursor = -1;
            for (int i = 0; i < _slotRects.Length; i++)
            {
                if (_slotRects[i].Contains(mouse.X, mouse.Y))
                {
                    slotUnderCursor = i;
                    break;
                }
            }

            if (slotUnderCursor < 0)
            {
                _pressedSlot = -1;
                _pressElapsedSeconds = 0;
                return;
            }

            // 手指滑到別的格子就重新計時
            if (slotUnderCursor != _pressedSlot)
            {
                _pressedSlot = slotUnderCursor;
                _pressElapsedSeconds = 0;
                _longPressHandled = false;
                return;
            }

            _pressElapsedSeconds += gameTime.ElapsedGameTime.TotalSeconds;
            if (_pressElapsedSeconds < LongPressSeconds || _longPressHandled)
                return;

            _longPressHandled = true;
            if (_pressedSlot < PotionSlotCount)
            {
                OpenPotionPicker(_pressedSlot);
            }
            else
            {
                _pendingAssignSlot = _pressedSlot;
                _skillPanel.Open(_state);
            }
        }

        private void RefreshResourceTexts()
        {
            if (_lastCurrentHealth != _state.CurrentHealth || _lastMaximumHealth != _state.MaximumHealth)
            {
                _lastCurrentHealth = _state.CurrentHealth;
                _lastMaximumHealth = _state.MaximumHealth;
                _healthText = $"{_lastCurrentHealth}/{_lastMaximumHealth}";
            }

            if (_lastCurrentShield != _state.CurrentShield || _lastMaximumShield != _state.MaximumShield)
            {
                _lastCurrentShield = _state.CurrentShield;
                _lastMaximumShield = _state.MaximumShield;
                _shieldText = $"{_lastCurrentShield}/{_lastMaximumShield}";
            }

            if (_lastCurrentMana != _state.CurrentMana || _lastMaximumMana != _state.MaximumMana)
            {
                _lastCurrentMana = _state.CurrentMana;
                _lastMaximumMana = _state.MaximumMana;
                _manaText = $"{_lastCurrentMana}/{_lastMaximumMana}";
            }

            if (_lastCurrentAbility != _state.CurrentAbility || _lastMaximumAbility != _state.MaximumAbility)
            {
                _lastCurrentAbility = _state.CurrentAbility;
                _lastMaximumAbility = _state.MaximumAbility;
                _abilityText = $"{_lastCurrentAbility}/{_lastMaximumAbility}";
            }
        }

        private void HandleKeyboard()
        {
            var kb = MuGame.Instance.Keyboard;
            var prev = MuGame.Instance.PrevKeyboard;

            // Q/W/E → consume assigned potion
            for (int i = 0; i < PotionSlotCount; i++)
            {
                if (kb.IsKeyDown(SlotKeys[i]) && !prev.IsKeyDown(SlotKeys[i]))
                {
                    ConsumePotionInSlot(i);
                }
            }

            // 1-0 → select skill slot
            for (int i = PotionSlotCount; i < SlotCount; i++)
            {
                if (kb.IsKeyDown(SlotKeys[i]) && !prev.IsKeyDown(SlotKeys[i]))
                {
                    _activeSkillSlot = i;
                    PersistQuickSlots();
                }
            }

            // Escape → close potion picker
            if (_potionPickerOpen && kb.IsKeyDown(Keys.Escape) && !prev.IsKeyDown(Keys.Escape))
            {
                _potionPickerOpen = false;
            }
        }

        private void HandleMouseHover()
        {
            var mousePos = MuGame.Instance.UiMouseState;
            _hoveredButton = -1;
            _hoveredSlot = -1;
            _hoveredPotionCandidate = -1;

            // 手機沒有「游標懸停」。手指離開螢幕後游標會留在最後的觸控位置
            // （見 MuGame 對 iOS 的處理），不擋掉的話最後按過的按鈕會一直亮著。
            if (IsMobile && mousePos.LeftButton != Microsoft.Xna.Framework.Input.ButtonState.Pressed)
                return;

            // Check potion picker first (it's on top)
            if (_potionPickerOpen)
            {
                for (int i = 0; i < _potionPickerItemRects.Length; i++)
                {
                    if (_potionPickerItemRects[i].Contains(mousePos.X, mousePos.Y))
                    {
                        _hoveredPotionCandidate = i;
                        return;
                    }
                }
            }

            for (int i = 0; i < _slotRects.Length; i++)
            {
                if (_slotRects[i].Contains(mousePos.X, mousePos.Y))
                {
                    _hoveredSlot = i;
                    break;
                }
            }

            for (int i = 0; i < _btnRects.Length; i++)
            {
                if (_btnRects[i].Contains(mousePos.X, mousePos.Y))
                {
                    _hoveredButton = i;
                    break;
                }
            }
        }

        /// <summary>
        /// 手機的點擊處理。HUD 在手機上不是 Interactive（見建構式的說明），
        /// 因此不會收到 <see cref="OnClick"/>，改在這裡自行判斷「按下再放開同一個元素」。
        /// </summary>
        private void UpdateMobileTouch()
        {
            var mouse = MuGame.Instance.UiMouseState;
            bool pressed = mouse.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed;
            var position = new Point(mouse.X, mouse.Y);

            if (pressed && !_mobileWasPressed)
            {
                // 規則：<b>看得到的那一層贏</b>。
                //
                // HUD 畫在所有視窗上面，所以落在 HUD 元件上的觸控就是要給 HUD ——
                // 即使那個位置同時也在某個開著的視窗範圍內。
                //
                // 上一版反過來：只要點在開著的視窗上就整個交給視窗。那讓背包與
                // 技能視窗變成關不掉 —— 背包在手機上幾乎鋪滿畫面，它的範圍蓋住了
                // 右上角的 BAG 按鈕，於是「再按一次 BAG 關閉」永遠不會被處理。
                //
                // ContainsInteractivePoint 本身已經排除了聊天輸入列蓋住的區域
                // （那一層才是畫在 HUD 上面的）。
                if (!ContainsInteractivePoint(position))
                {
                    _mobilePressedButton = -1;
                    _avatarPressed = false;
                    _mobileWasPressed = true;
                    return;
                }

                _mobilePressedButton = HitTestButton(position);
                _avatarPressed = AvatarContains(position);
            }
            else if (!pressed && _mobileWasPressed)
            {
                int button = _mobilePressedButton;
                bool avatarWasPressed = _avatarPressed;
                _mobilePressedButton = -1;
                _avatarPressed = false;
                _mobileWasPressed = false;

                // 藥水格的長按已經開過選單，放開時不要再觸發一次使用
                if (_longPressHandled)
                {
                    _longPressHandled = false;
                    return;
                }

                // 選單開著時的點擊由 HandlePotionPickerClick 處理
                if (_potionPickerOpen)
                    return;

                if (avatarWasPressed && AvatarContains(position))
                {
                    OnButtonClicked(1);   // 等同 CHAR
                    return;
                }

                if (button >= 0 && button == HitTestButton(position))
                {
                    OnButtonClicked(button < MobileButtonActions.Length ? MobileButtonActions[button] : button);
                    return;
                }

                for (int i = 0; i < _slotRects.Length; i++)
                {
                    if (_slotRects[i].Width > 0 && _slotRects[i].Contains(position))
                    {
                        ActivateOrAssignSlot(i);
                        return;
                    }
                }

                return;
            }

            _mobileWasPressed = pressed;
        }

        private int HitTestButton(Point position)
        {
            for (int i = 0; i < _btnRects.Length; i++)
            {
                if (_btnRects[i].Contains(position))
                    return i;
            }

            return -1;
        }

        private void HandlePotionPickerClick()
        {
            if (!_potionPickerOpen)
                return;

            var mouse = MuGame.Instance.UiMouseState;
            var prevMouse = MuGame.Instance.PrevUiMouseState;

            bool leftJustPressed = mouse.LeftButton == ButtonState.Pressed
                && prevMouse.LeftButton == ButtonState.Released;

            if (!leftJustPressed)
                return;

            // Check if clicked on a picker item
            for (int i = 0; i < _potionPickerItemRects.Length; i++)
            {
                if (_potionPickerItemRects[i].Contains(mouse.X, mouse.Y))
                {
                    if (i < _potionCandidates.Count && _potionPickerSlot >= 0 && _potionPickerSlot < PotionSlotCount)
                    {
                        var candidate = _potionCandidates[i];
                        _potionAssignments[_potionPickerSlot] = (candidate.Group, candidate.Id);
                        PersistQuickSlots();
                        SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav");
                    }
                    _potionPickerOpen = false;
                    return;
                }
            }

            // Click outside picker → close
            if (!_potionPickerRect.Contains(mouse.X, mouse.Y))
            {
                _potionPickerOpen = false;
            }
        }

        private void EnsurePotionIconsCached()
        {
            // Pre-generate BMD previews (outside SpriteBatch scope) using fixed cache size
            for (int i = 0; i < PotionSlotCount; i++)
            {
                var assignment = _potionAssignments[i];
                if (assignment == null) continue;
                var def = ItemDatabase.GetItemDefinition(assignment.Value.Group, (short)assignment.Value.Id);
                if (def?.TexturePath != null && def.TexturePath.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase))
                {
                    if (BmdPreviewRenderer.TryGetCachedPreview(def, PotionIconCacheSize, PotionIconCacheSize) == null)
                        BmdPreviewRenderer.GetPreview(def, PotionIconCacheSize, PotionIconCacheSize);
                }
            }

            if (_potionPickerOpen)
            {
                foreach (var candidate in _potionCandidates)
                {
                    var def = ItemDatabase.GetItemDefinition(candidate.Group, (short)candidate.Id);
                    if (def?.TexturePath != null && def.TexturePath.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase))
                    {
                        if (BmdPreviewRenderer.TryGetCachedPreview(def, PotionIconCacheSize, PotionIconCacheSize) == null)
                            BmdPreviewRenderer.GetPreview(def, PotionIconCacheSize, PotionIconCacheSize);
                    }
                }
            }
        }

        private void OnSkillSelectedFromPanel(SkillEntryState skill)
        {
            int targetSlot = _pendingAssignSlot >= PotionSlotCount ? _pendingAssignSlot : _activeSkillSlot;
            if (targetSlot < PotionSlotCount)
                targetSlot = 3;

            _slotSkills[targetSlot] = skill;
            _activeSkillSlot = targetSlot;
            _pendingAssignSlot = -1;
            PersistQuickSlots();
        }

        private void RestoreQuickSlotsIfNeeded()
        {
            if (_quickSlotsRestored)
                return;

            string? characterName = GetPersistentCharacterName();
            if (string.IsNullOrWhiteSpace(characterName))
                return;

            var learnedSkills = _state.GetSkills().ToDictionary(skill => skill.SkillId);
            if (learnedSkills.Count == 0)
                return;

            if (MuGame.TryLoadQuickSlotAssignments(characterName, out int activeSkillSlot, out ushort?[] savedSkillSlots, out (byte Group, int Id)?[] savedPotionSlots))
            {
                for (int i = PotionSlotCount; i < Math.Min(SlotCount, savedSkillSlots.Length); i++)
                {
                    ushort? skillId = savedSkillSlots[i];
                    if (skillId.HasValue && learnedSkills.TryGetValue(skillId.Value, out var skill))
                    {
                        _slotSkills[i] = skill;
                    }
                }

                for (int i = 0; i < Math.Min(PotionSlotCount, savedPotionSlots.Length); i++)
                {
                    _potionAssignments[i] = savedPotionSlots[i];
                }

                if (activeSkillSlot >= PotionSlotCount && activeSkillSlot < SlotCount)
                {
                    _activeSkillSlot = activeSkillSlot;
                }
            }

            EnsureActiveSkillSelection(learnedSkills.Values.FirstOrDefault());
            _quickSlotsRestored = true;
        }

        private void EnsureActiveSkillSelection(SkillEntryState? fallbackSkill)
        {
            if (_slotSkills[3] == null && fallbackSkill != null)
            {
                _slotSkills[3] = fallbackSkill;
            }

            if (_activeSkillSlot >= PotionSlotCount &&
                _activeSkillSlot < SlotCount &&
                _slotSkills[_activeSkillSlot] != null)
            {
                return;
            }

            for (int i = PotionSlotCount; i < SlotCount; i++)
            {
                if (_slotSkills[i] != null)
                {
                    _activeSkillSlot = i;
                    return;
                }
            }

            _activeSkillSlot = 3;
        }

        private void PersistQuickSlots()
        {
            if (!_quickSlotsRestored)
                return;

            string? characterName = GetPersistentCharacterName();
            if (string.IsNullOrWhiteSpace(characterName))
                return;

            ushort?[] skillIds = new ushort?[SlotCount];
            for (int i = PotionSlotCount; i < SlotCount; i++)
            {
                skillIds[i] = _slotSkills[i]?.SkillId;
            }

            MuGame.PersistQuickSlotAssignments(characterName, _activeSkillSlot, skillIds, _potionAssignments);
        }

        private string? GetPersistentCharacterName()
        {
            string? name = _state.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name == "???")
                return null;

            return name;
        }

        private void OnButtonClicked(int index)
        {
            SoundController.Instance.PlayBuffer("Sound/iButtonClick.wav");

            if (MuGame.Instance?.ActiveScene is not Scenes.GameScene gs)
                return;

            switch (index)
            {
                case 0: gs.PauseMenu.Visible = !gs.PauseMenu.Visible; break;
                case 1: ToggleWindow<Character.CharacterInfoWindowControl>(gs); break;
                case 2:
                    if (gs.InventoryControl != null)
                    {
                        if (gs.InventoryControl.Visible)
                        {
                            gs.InventoryControl.Hide();
                        }
                        else
                        {
                            gs.InventoryControl.Show();
                        }
                    }
                    break;
                case 3: ToggleWindow<Party.PartyPanelControl>(gs); break;

                // 手機專用。桌面是 M / Enter 快捷鍵，手機沒有鍵盤。
                case 6:
                    if (gs.MiniMap != null)
                    {
                        if (gs.MiniMap.Visible)
                        {
                            gs.MiniMap.Hide();
                        }
                        else
                        {
                            gs.MiniMap.Show();
                            gs.MiniMap.BringToFront();
                        }
                    }
                    break;
                case 7:
                    if (gs.ChatInput != null)
                    {
                        if (gs.ChatInput.Visible) gs.ChatInput.Hide();
                        else gs.ChatInput.Show();
                    }
                    break;

                // 手機專用：技能面板。桌面靠快捷列的格子進入，手機的快捷列沒有技能格。
                case 8:
                    if (_skillPanel.Visible)
                        _skillPanel.Close();
                    else
                        OpenMobileSkillBrowser();
                    break;
            }
        }

        private static void ToggleWindow<T>(Scenes.GameScene gs) where T : GameControl
        {
            var controls = gs.Controls.GetSnapshotArray();
            for (int i = 0; i < controls.Length; i++)
            {
                if (controls[i] is T ctrl)
                {
                    ctrl.Visible = !ctrl.Visible;

                    // 打開時要置頂。否則先開地圖再開角色資訊，角色視窗會被壓在地圖底下
                    // —— 看得到卻點不到。
                    if (ctrl.Visible)
                        ctrl.BringToFront();

                    return;
                }
            }
        }

        // ════════════════════════════ Layout ════════════════════════════
        //
        // Layout (left → right):
        //   [PARTY][GUILD][QUEST] | HP SD | [Q][W][E]  [1][2]...[0] | MP AG | [MENU][CHAR][INV]

        private void RefreshLayout()
        {
            Point virtualSize = UiScaler.VirtualSize;
            if (virtualSize == _lastVirtualSize)
                return;

            _lastVirtualSize = virtualSize;

            if (IsMobile)
            {
                RefreshMobileLayout(virtualSize);
                return;
            }

            int vw = virtualSize.X;
            int vh = virtualSize.Y;

            int panelH = 92;
            int expH = 12;
            int panelY = vh - panelH - expH;

            _panelRect = new Rectangle(0, panelY, vw, panelH);
            _expBarRect = new Rectangle(0, vh - expH, vw, expH);

            // Font scales
            _barFontScale = 0.45f;
            _slotFontScale = 0.36f;
            _btnFontScale = 0.40f;
            _expFontScale = 0.42f;

            int pad = 6;
            int innerTop = panelY + pad;
            int innerH = panelH - pad * 2;

            // ── Buttons (edges, tall, stacked vertically) ──
            int btnW = 56;
            int btnGap = 3;
            int btnCount = 3;
            int btnH = (innerH - btnGap * (btnCount - 1)) / btnCount;

            _btnRects = new Rectangle[ButtonLabels.Length];

            // Left side buttons: PARTY(3), GUILD(4), QUEST(5)
            int leftBtnX = pad;
            for (int i = 0; i < 3; i++)
            {
                _btnRects[3 + i] = new Rectangle(
                    leftBtnX, innerTop + i * (btnH + btnGap),
                    btnW, btnH);
            }

            // Right side buttons: MENU(0), CHAR(1), INV(2)
            int rightBtnX = vw - pad - btnW;
            for (int i = 0; i < 3; i++)
            {
                _btnRects[i] = new Rectangle(
                    rightBtnX, innerTop + i * (btnH + btnGap),
                    btnW, btnH);
            }

            // ── Available center space ──
            int contentLeft = leftBtnX + btnW + 6;
            int contentRight = rightBtnX - 6;
            int contentW = contentRight - contentLeft;

            // ── Quick slots first — compute how big they can be ──
            int slotGap = 3;
            int potionGap = 10;
            int fixedGaps = (SlotCount - 1) * slotGap + potionGap;

            // Slots take ~45% of center, bars take rest
            int barW = (int)(contentW * 0.19f);
            int barSlotGap = 6;
            int slotsAreaW = contentW - 2 * barW - 2 * barSlotGap;
            int slotSize = Math.Min(
                (slotsAreaW - fixedGaps) / SlotCount,
                innerH); // don't exceed panel height
            slotSize = Math.Max(slotSize, 30); // minimum
            int slotWidth = Math.Max(28, slotSize - 4);
            int slotHeight = Math.Min(innerH, slotSize + 4);

            int totalSlotW = SlotCount * slotWidth + fixedGaps;
            int slotsAreaLeft = contentLeft + barW + barSlotGap;
            int slotsAreaRight = contentRight - barW - barSlotGap;
            int actualSlotsW = slotsAreaRight - slotsAreaLeft;
            int slotStartX = slotsAreaLeft + (actualSlotsW - totalSlotW) / 2;
            int slotY = panelY + (panelH - slotHeight) / 2;

            _slotRects = new Rectangle[SlotCount];
            int sx = slotStartX;
            for (int i = 0; i < SlotCount; i++)
            {
                _slotRects[i] = new Rectangle(sx, slotY, slotWidth, slotHeight);
                sx += slotWidth + slotGap;
                if (i == PotionSlotCount - 1) sx += potionGap;
            }

            // ── Resource bars (between buttons and slots, vertically centered) ──
            int barH = 24;
            int barGapV = 4;
            int barsBlockH = barH * 2 + barGapV;
            int barsTopY = panelY + (panelH - barsBlockH) / 2;

            // Left bars: HP + SD
            _hpBarRect = new Rectangle(contentLeft, barsTopY, barW, barH);
            _sdBarRect = new Rectangle(contentLeft, barsTopY + barH + barGapV, barW, barH);

            // Right bars: MP + AG
            int rightBarX = contentRight - barW;
            _mpBarRect = new Rectangle(rightBarX, barsTopY, barW, barH);
            _agBarRect = new Rectangle(rightBarX, barsTopY + barH + barGapV, barW, barH);

            _companionAreaRect = new Rectangle(_panelRect.X, _panelRect.Y + 4, _panelRect.Width, 13);

            X = 0;
            Y = panelY;
            ControlSize = new Point(vw, panelH + expH);
            ViewSize = ControlSize;
        }

        // ════════════════════════ 手機版面 ════════════════════════
        //
        // 桌面是一整條貼在底部的面板：13 個快捷格 + 6 顆文字鈕 + 4 條資源條。
        // 那條面板在手機上正好壓在虛擬搖桿的啟用區，而且格子只有指頭的一半寬。
        //
        // 手機改成手遊 MMO 的標準配置，把畫面中央完全讓給遊戲本身：
        //
        //   ┌────────────────────────────────────────────────┐
        //   │ ▬▬▬▬▬▬▬▬▬▬ EXP ▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬ │
        //   │ HP ▬▬▬▬▬▬▬                    [MENU][CHAR][BAG]│
        //   │ MP ▬▬▬▬▬▬▬                    [SKILL][MAP][CHAT]│
        //   │ SD ▬▬  AG ▬▬                                    │
        //   │                                                 │
        //   │                                          ◔ 技能 │
        //   │   ◎ 搖桿              ○ ○ ○ 藥水      ◉ ATK    │
        //   └────────────────────────────────────────────────┘
        //
        // 技能格（3-12）在手機上不繪製 —— 技能改由右下角的觸控按鈕使用與指派，
        // 那裡才是拇指構得到的位置。快捷格只保留三個藥水格。
        private void RefreshMobileLayout(Point virtualSize)
        {
            int vw = virtualSize.X;
            int vh = virtualSize.Y;

            const int EdgeMargin = 14;
            // 螢幕圓角會斜切掉四個角落，角落的元素要再往內縮
            const int Corner = MobileUi.CornerInset;

            // 手機螢幕小，字要放大才看得清楚
            // 全部改走統一級距（見 MobileUi 的文字級距）。
            // 生命／魔力那三行是這個畫面最常看的數字 —— 用內文級，不是標籤級。
            _barFontScale = MobileUi.ScaleFor(MobileUi.TextBody);
            _slotFontScale = MobileUi.ScaleFor(MobileUi.TextCaption);
            _btnFontScale = MobileUi.ScaleFor(MobileUi.TextHeading);
            _expFontScale = MobileUi.ScaleFor(MobileUi.TextLabel);

            // 底部面板整條移除，OnClick 才不會把畫面下緣的觸控全部吃掉
            _panelRect = Rectangle.Empty;

            const int ExpH = 8;

            // ── 左上角：圓形頭像框 ──
            // 圓角螢幕的左上角是斜切的，方形的血條放在那裡一定會被吃掉一塊。
            // 圓形正好貼合圓角，而且外圈兩圈弧線就把生命與魔力交代完了 ——
            // 底下不再需要四條彩色長條，畫面乾淨很多。
            const int AvatarRadius = 48;
            const int TopMargin = 16;

            // 頭像是特例，可以比對齊線更靠邊。
            // 它是圓的 —— 圓形正好貼合螢幕圓角，不會被斜切掉一角（見界面規格 2.x），
            // 而且它在左上角，不在鏡頭挖孔那一段。旁邊的文字就沒有這個豁免：
            // 方形的字塊貼邊一定會被啃到，所以文字仍然從頭像右緣起算。
            const int AvatarEdgeInset = 10;
            _avatarCenter = new Vector2(AvatarEdgeInset + AvatarRadius, TopMargin + AvatarRadius);
            _avatarRadius = AvatarRadius;
            _avatarRect = new Rectangle(
                (int)(_avatarCenter.X - AvatarRadius), (int)(_avatarCenter.Y - AvatarRadius),
                AvatarRadius * 2, AvatarRadius * 2);

            // 數值改成頭像右側的純文字（白／灰兩色），不再用彩色長條
            int textLeft = _avatarRect.Right + 14;
            // 三行文字：HP / MP / (SD + AG)，行距 30
            _vitalsTextRect = new Rectangle(textLeft, TopMargin + 4, 300, 82);

            // 四條資源條在手機上不繪製（空矩形，DrawResourceBar 會略過）
            _hpBarRect = Rectangle.Empty;
            _mpBarRect = Rectangle.Empty;
            _sdBarRect = Rectangle.Empty;
            _agBarRect = Rectangle.Empty;

            // 寵物血條接在數值文字下方
            _companionAreaRect = new Rectangle(textLeft, _avatarRect.Bottom + 6, 268, 13);

            // ── 右上：介面按鈕，3 欄 2 列 ──
            const int BtnW = 96;
            const int BtnH = 42;
            const int BtnGap = 6;
            const int BtnCols = 3;

            int btnBlockW = BtnCols * BtnW + (BtnCols - 1) * BtnGap;
            int btnLeft = vw - Corner - btnBlockW;

            _btnRects = new Rectangle[ActiveButtonLabels.Length];
            for (int i = 0; i < _btnRects.Length; i++)
            {
                int col = i % BtnCols;
                int row = i / BtnCols;
                _btnRects[i] = new Rectangle(
                    btnLeft + col * (BtnW + BtnGap),
                    TopMargin + row * (BtnH + BtnGap),
                    BtnW, BtnH);
            }

            int btnBlockBottom = _btnRects.Length > 0 ? _btnRects[^1].Bottom : TopMargin + BtnH;

            // ── 經驗值：接在按鈕區塊正下方，寬度與按鈕區塊對齊 ──
            // 原本是畫面最上緣的一條通欄細線，貼著螢幕邊緣不好看，
            // 而且與按鈕區塊各自為政。對齊之後右上角是一個完整的區塊。
            _expBarRect = new Rectangle(btnLeft, btnBlockBottom + 8, btnBlockW, ExpH);

            // ── 狀態列：時間、電量、FPS、延遲，同樣靠右對齊到按鈕區塊 ──
            _statusReadoutRect = new Rectangle(btnLeft, _expBarRect.Bottom + 7, btnBlockW, 20);

            // ── 金幣：狀態列的下一行 ──
            // 放在這裡而不是塞回背包裡，是因為錢是<b>隨時要知道</b>的數字：
            // 撿到東西、賣掉東西、買補品，全都會動。要為了看一眼餘額而開背包，
            // 等於每次交易都要多兩次點擊。
            // 和裝備按鈕（BAG）同一欄、同一條右對齊線，讀起來是一組。
            _zenReadoutRect = new Rectangle(btnLeft, _statusReadoutRect.Bottom + 4, btnBlockW, 22);

            // ── 右下：三個藥水鈕，排在技能弧線的左側 ──
            // 右邊界留給 TouchActionButtonsControl 的 ATK 與技能弧線，
            // 兩者的間距在 TouchActionButtonsControl 有對應的註解。
            const int PotionSize = 64;
            const int PotionGap = 12;

            // 藥水列的右緣與技能弧線最左那顆之間要留的距離。
            //
            // 原本是寫死的「距離右緣 300」，而技能弧線的位置後來調整過幾次 ——
            // 兩者就這樣靠到只剩 1 px，使用者回報「幾乎重疊」。
            // 改成從技能群的實際左緣往回推，任何一邊再被調整都不會再撞上。
            const int PotionClusterGap = 40;

            int potionRowW = PotionSlotCount * PotionSize + (PotionSlotCount - 1) * PotionGap;
            int potionRight = Game.TouchActionButtonsControl.ClusterLeftEdge - PotionClusterGap;
            int potionLeft = Math.Max(MobileUi.LeftEdge, potionRight - potionRowW);
            int potionTop = vh - EdgeMargin - PotionSize;

            _slotRects = new Rectangle[SlotCount];
            for (int i = 0; i < PotionSlotCount; i++)
            {
                _slotRects[i] = new Rectangle(
                    potionLeft + i * (PotionSize + PotionGap),
                    potionTop, PotionSize, PotionSize);
            }
            for (int i = PotionSlotCount; i < SlotCount; i++)
            {
                // 空矩形 = 不繪製也不接受觸控（見 DrawQuickSlots 與 OnClick）
                _slotRects[i] = Rectangle.Empty;
            }

            // HUD 的元素散布在四個角，控制項本身必須涵蓋整個畫面。
            // OnClick 在沒有命中任何元素時回傳 false，不會擋住世界的觸控。
            X = 0;
            Y = 0;
            ControlSize = new Point(vw, vh);
            ViewSize = ControlSize;
        }

        /// <summary>
        /// 這個座標是否落在 HUD 的可互動元素上。
        /// 供虛擬搖桿判斷「這一下是在按 HUD，不是要移動」—— 搖桿直接讀觸控狀態，
        /// 不走 UI 的點擊路由，沒有這個判斷就會邊按按鈕邊走路。
        /// </summary>
        public bool ContainsInteractivePoint(Point position)
        {
            if (!Visible)
                return false;

            // 聊天輸入列開著的時候橫跨整個畫面下緣，會蓋住藥水鈕。
            // 蓋住的部分不算 HUD —— 否則想點輸入欄會變成喝藥水。
            if (MuGame.Instance.ActiveScene is Scenes.GameScene scene &&
                scene.ChatInput is { Visible: true } chat &&
                chat.DisplayRectangle.Contains(position))
            {
                return false;
            }

            if (_potionPickerOpen && _potionPickerRect.Contains(position))
                return true;

            for (int i = 0; i < _slotRects.Length; i++)
            {
                if (_slotRects[i].Width > 0 && _slotRects[i].Contains(position))
                    return true;
            }

            for (int i = 0; i < _btnRects.Length; i++)
            {
                if (_btnRects[i].Contains(position))
                    return true;
            }

            if (AvatarContains(position))
                return true;

            return _panelRect.Width > 0 && _panelRect.Contains(position);
        }

        private void RefreshCompanionLifeInfos()
        {
            _companionInfos[0] = null;
            _companionInfos[1] = null;

            var items = _state.GetInventoryItems();
            int writeIndex = 0;

            if (TryGetHelperLifeInfo(items, out var helper))
            {
                _companionInfos[writeIndex++] = helper;
            }

            if (writeIndex < _companionInfos.Length && TryGetDarkRavenLifeInfo(items, out var raven))
            {
                _companionInfos[writeIndex] = raven;
            }
        }

        private static bool TryGetHelperLifeInfo(IReadOnlyDictionary<byte, byte[]> items, out CompanionLifeInfo info)
        {
            info = default;

            const byte helperSlot = 8;
            if (!items.TryGetValue(helperSlot, out var helperData) || helperData == null || helperData.Length == 0)
            {
                return false;
            }

            var definition = ItemDatabase.GetItemDefinition(helperData);
            if (definition == null || definition.Group != 13 || !HelperLifeIds.Contains(definition.Id))
            {
                return false;
            }

            int currentLife = ItemDatabase.GetItemDurability(helperData);
            const int maxLife = 255;
            info = new CompanionLifeInfo(
                GetCompanionName(definition.Id, definition.Name),
                currentLife,
                maxLife,
                ResolveCompanionFillColor(currentLife, maxLife));
            return true;
        }

        private static bool TryGetDarkRavenLifeInfo(IReadOnlyDictionary<byte, byte[]> items, out CompanionLifeInfo info)
        {
            info = default;

            // Reference client reads Dark Raven life from weapon-left slot.
            // Keep a fallback check on weapon-right for server slot layout variations.
            Span<byte> candidateSlots = stackalloc byte[] { 1, 0 };

            for (int i = 0; i < candidateSlots.Length; i++)
            {
                byte slot = candidateSlots[i];
                if (!items.TryGetValue(slot, out var itemData) || itemData == null || itemData.Length == 0)
                {
                    continue;
                }

                var definition = ItemDatabase.GetItemDefinition(itemData);
                if (definition == null || definition.Group != 13 || definition.Id != 5)
                {
                    continue;
                }

                int currentLife = ItemDatabase.GetItemDurability(itemData);
                const int maxLife = 255;
                info = new CompanionLifeInfo(
                    GetCompanionName(definition.Id, definition.Name),
                    currentLife,
                    maxLife,
                    ResolveCompanionFillColor(currentLife, maxLife));
                return true;
            }

            return false;
        }

        private static string GetCompanionName(int itemId, string? defaultName)
        {
            return itemId switch
            {
                0 => "Guardian Angel",
                1 => "Imp",
                2 => "Uniria",
                3 => "Dinorant",
                4 => "Dark Horse",
                5 => "Dark Raven",
                _ => string.IsNullOrWhiteSpace(defaultName) ? "Companion" : defaultName
            };
        }

        private static Color ResolveCompanionFillColor(int current, int maximum)
        {
            if (maximum <= 0)
            {
                return CompanionColorDanger;
            }

            float ratio = MathHelper.Clamp(current / (float)maximum, 0f, 1f);
            if (ratio <= 0.2f)
            {
                return CompanionColorDanger;
            }

            if (ratio <= 0.5f)
            {
                return CompanionColorWarn;
            }

            return CompanionColorGood;
        }

        private void DrawCompanionLifeBars(SpriteBatch sb, Texture2D pixel)
        {
            if (_font == null)
            {
                return;
            }

            int count = 0;
            for (int i = 0; i < _companionInfos.Length; i++)
            {
                if (_companionInfos[i].HasValue)
                    count++;
            }

            if (count == 0)
                return;

            int barHeight = 13;
            int barGap = 6;
            int barWidth = Math.Clamp((int)(_companionAreaRect.Width * 0.12f), 120, 156);
            int totalWidth = (count * barWidth) + ((count - 1) * barGap);
            // 手機的寵物血條接在左上角的資源條下方，靠左對齊才會與上面切齊
            int startX = IsMobile
                ? _companionAreaRect.X
                : _companionAreaRect.Center.X - (totalWidth / 2);
            int y = _companionAreaRect.Y;
            int drawn = 0;

            for (int i = 0; i < _companionInfos.Length; i++)
            {
                if (!_companionInfos[i].HasValue)
                    continue;

                var rect = new Rectangle(startX + drawn * (barWidth + barGap), y, barWidth, barHeight);
                DrawCompanionLifeBar(sb, pixel, rect, _companionInfos[i]!.Value);
                drawn++;
            }
        }

        private void DrawCompanionLifeBar(SpriteBatch sb, Texture2D pixel, Rectangle rect, CompanionLifeInfo info)
        {
            sb.Draw(pixel, rect, ModernHudTheme.BorderOuter);

            var track = new Rectangle(rect.X + 1, rect.Y + 1, Math.Max(1, rect.Width - 2), Math.Max(1, rect.Height - 2));
            UiDrawHelper.DrawVerticalGradient(sb, track,
                new Color(18, 20, 28, 242),
                new Color(8, 10, 14, 252));

            float lifeRatio = info.Maximum > 0
                ? MathHelper.Clamp(info.Current / (float)info.Maximum, 0f, 1f)
                : 0f;
            int fillWidth = (int)(track.Width * lifeRatio);
            if (fillWidth > 0)
            {
                var fillRect = new Rectangle(track.X, track.Y, fillWidth, track.Height);
                UiDrawHelper.DrawHorizontalGradient(sb, fillRect,
                    Color.Lerp(info.FillColor * 0.55f, ModernHudTheme.BgDark, 0.45f),
                    info.FillColor);
                sb.Draw(pixel, new Rectangle(fillRect.X, fillRect.Y, fillRect.Width, 1), info.FillColor * 0.65f);
            }

            string text = $"{info.Name} {info.Current}/{info.Maximum}";
            float scale = MobileUi.ScaleFor(MobileUi.TextCaption);
            Vector2 size = _font!.MeasureString(text) * scale;
            Vector2 textPos = new(
                rect.X + (rect.Width - size.X) * 0.5f,
                rect.Y + (rect.Height - size.Y) * 0.5f);

            sb.DrawString(_font, text, textPos + Vector2.One,
                Color.Black * 0.8f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.DrawString(_font, text, textPos,
                ModernHudTheme.TextWhite, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        // ════════════════════════════ Drawing ════════════════════════════

        private void DrawPanelBackground(SpriteBatch sb, Texture2D pixel)
        {
            // Top shadow fade above the panel
            var shadowRect = new Rectangle(_panelRect.X, _panelRect.Y - 8, _panelRect.Width, 8);
            UiDrawHelper.DrawVerticalGradient(sb, shadowRect,
                Color.Transparent, new Color(0, 0, 0, 100));

            // Outer border frame
            sb.Draw(pixel, _panelRect, ModernHudTheme.BorderOuter);

            // Inner gradient background
            var inner = new Rectangle(_panelRect.X + 1, _panelRect.Y + 1,
                Math.Max(1, _panelRect.Width - 2), Math.Max(1, _panelRect.Height - 2));
            UiDrawHelper.DrawVerticalGradient(sb, inner,
                new Color(20, 24, 32, 252), new Color(8, 10, 14, 255));

            // Top accent line (gold)
            sb.Draw(pixel,
                new Rectangle(inner.X + 2, inner.Y, Math.Max(1, inner.Width - 4), 1),
                ModernHudTheme.Accent * 0.55f);

            // Second subtle highlight line
            sb.Draw(pixel,
                new Rectangle(inner.X + 2, inner.Y + 1, Math.Max(1, inner.Width - 4), 1),
                ModernHudTheme.BorderInner * 0.25f);

            // Vertical separators between buttons and bars
            DrawVerticalSeparator(sb, pixel,
                _btnRects[0].Right + 3, _panelRect.Y + 4, _panelRect.Height - 8);
            DrawVerticalSeparator(sb, pixel,
                _btnRects[3].X - 4, _panelRect.Y + 4, _panelRect.Height - 8);
        }

        private static void DrawVerticalSeparator(SpriteBatch sb, Texture2D pixel, int x, int y, int height)
        {
            sb.Draw(pixel, new Rectangle(x, y, 1, height), ModernHudTheme.BorderOuter * 0.9f);
            sb.Draw(pixel, new Rectangle(x + 1, y, 1, height), ModernHudTheme.BorderInner * 0.3f);
            sb.Draw(pixel, new Rectangle(x - 1, y, 3, 2), ModernHudTheme.Accent * 0.45f);
        }

        private void DrawResourceBar(SpriteBatch sb, Texture2D pixel, Rectangle rect,
            float pct, Color darkColor, Color mainColor, Color brightColor, Color glowColor,
            string valueText, string label, bool critical)
        {
            float clampedPct = MathHelper.Clamp(pct, 0f, 1f);

            // Pulsing alpha for critical state
            float critAlpha = 1f;
            if (critical && clampedPct > 0f)
            {
                critAlpha = 0.65f + 0.35f * (float)Math.Sin(_totalTime * 4.0);
            }

            // Outer frame with rounded-look bevel
            sb.Draw(pixel, rect, ModernHudTheme.BorderOuter);

            // Inner track
            var track = new Rectangle(rect.X + 1, rect.Y + 1,
                Math.Max(1, rect.Width - 2), Math.Max(1, rect.Height - 2));

            // Track background with subtle gradient
            UiDrawHelper.DrawVerticalGradient(sb, track,
                new Color(18, 20, 28, 240), new Color(8, 10, 14, 250));

            // Fill bar
            int fillW = Math.Max(0, (int)(track.Width * clampedPct));
            if (fillW > 0)
            {
                var fillRect = new Rectangle(track.X, track.Y, fillW, track.Height);

                // Main gradient fill (dark → bright)
                UiDrawHelper.DrawHorizontalGradient(sb, fillRect, darkColor * critAlpha, mainColor * critAlpha);

                // Top shine line (bright, 1px)
                sb.Draw(pixel, new Rectangle(fillRect.X, fillRect.Y, fillRect.Width, 1),
                    brightColor * 0.6f * critAlpha);

                // Second shine line (softer)
                if (fillRect.Height > 4)
                {
                    sb.Draw(pixel, new Rectangle(fillRect.X, fillRect.Y + 1, fillRect.Width, 1),
                        brightColor * 0.2f * critAlpha);
                }

                // Bottom shadow line
                sb.Draw(pixel, new Rectangle(fillRect.X, fillRect.Bottom - 1, fillRect.Width, 1),
                    Color.Black * 0.3f);

                // Right edge glow at fill boundary
                if (fillW > 2 && glowColor.A > 0)
                {
                    int glowW = Math.Min(6, fillW);
                    sb.Draw(pixel, new Rectangle(fillRect.Right - glowW, fillRect.Y, glowW, fillRect.Height),
                        glowColor * critAlpha);
                }

                // Segment tick marks every 25%
                for (int seg = 1; seg < 4; seg++)
                {
                    int tickX = track.X + (int)(track.Width * (seg / 4f));
                    if (tickX < fillRect.Right && tickX > track.X)
                    {
                        sb.Draw(pixel, new Rectangle(tickX, track.Y, 1, track.Height),
                            Color.Black * 0.25f);
                    }
                }
            }

            // Segment tick marks (unfilled region too, very subtle)
            for (int seg = 1; seg < 4; seg++)
            {
                int tickX = track.X + (int)(track.Width * (seg / 4f));
                if (tickX >= track.X + fillW)
                {
                    sb.Draw(pixel, new Rectangle(tickX, track.Y, 1, track.Height),
                        ModernHudTheme.BorderInner * 0.15f);
                }
            }

            // Inner border highlight (top-left bevel)
            sb.Draw(pixel, new Rectangle(rect.X + 1, rect.Y + 1, Math.Max(1, rect.Width - 2), 1),
                ModernHudTheme.BorderHighlight * 0.12f);

            // Text
            if (_font != null)
            {
                float textScale = _barFontScale;

                // Label (left-aligned)
                var labelSize = _font.MeasureString(label) * textScale;
                float labelX = rect.X + 5;
                float labelY = rect.Y + (rect.Height - labelSize.Y) / 2f;
                DrawTextWithShadow(sb, label, new Vector2(labelX, labelY), mainColor * 0.9f, textScale);

                // Value (right-aligned)
                var valSize = _font.MeasureString(valueText) * textScale;
                float valX = rect.Right - valSize.X - 5;
                float valY = rect.Y + (rect.Height - valSize.Y) / 2f;
                DrawTextWithShadow(sb, valueText, new Vector2(valX, valY), ModernHudTheme.TextWhite, textScale);
            }
        }

        // 色票只有一份，在 MobileUi。這裡曾經另外定義一組幾乎相同但不完全相同的
        // 值（灰色差了 2-4）—— 兩份真相遲早會走散，改成直接引用。
        private static Color MobileText => MobileUi.TextPrimary;
        private static Color MobileTextDim => MobileUi.TextDim;
        private static Color MobileHp => MobileUi.Hp;
        private static Color MobileMp => MobileUi.Mp;
        private static Color MobileTrack => MobileUi.Track;

        /// <summary>
        /// 左上角的圓形頭像框。
        ///
        /// 圓角螢幕會斜切左上角，用圓形正好貼合。外圈兩道弧線就是生命與魔力 ——
        /// 眼角餘光判斷狀態不必去讀數字，也省下四條彩色長條的空間。
        /// 框內是等級。點一下等同 CHAR 按鈕。
        /// </summary>
        private void DrawAvatar(SpriteBatch sb)
        {
            if (_avatarRadius <= 0f)
                return;

            float r = _avatarRadius;
            float hpRadius = r * 0.94f;
            float mpRadius = r * 0.78f;
            float arcThickness = r * 0.085f;

            MobileUi.DrawGlow(sb, _avatarCenter + new Vector2(0f, r * 0.08f), r * 1.32f, Color.Black * 0.40f);
            MobileUi.DrawDisc(sb, r > 0 ? _avatarCenter : Vector2.Zero, r,
                (_avatarPressed ? new Color(40, 46, 58) : new Color(18, 21, 28)) * 0.92f);

            // 底環
            MobileUi.DrawRing(sb, _avatarCenter, hpRadius, MobileTrack * 0.55f, arcThickness);
            MobileUi.DrawRing(sb, _avatarCenter, mpRadius, MobileTrack * 0.40f, arcThickness * 0.75f);

            // 弧線一律用不透明色。半透明的圓點相疊會在重疊處累積出深淺相間的邊，
            // 看起來就像鋸齒 —— 這是先前那圈紅色看起來毛毛的原因。
            float hp = MathHelper.Clamp(_displayHpPct, 0f, 1f);
            if (hp > 0f)
            {
                MobileUi.DrawArc(sb, _avatarCenter, hpRadius,
                    -MathHelper.PiOver2, MathHelper.TwoPi * hp, MobileHp, arcThickness);
            }

            float mp = MathHelper.Clamp(_displayMpPct, 0f, 1f);
            if (mp > 0f)
            {
                MobileUi.DrawArc(sb, _avatarCenter, mpRadius,
                    -MathHelper.PiOver2, MathHelper.TwoPi * mp, MobileMp, arcThickness * 0.75f);
            }

            // 生命偏低時整圈脈動，不另外加顏色
            if (_targetHpPct < 0.25f)
            {
                float pulse = 0.25f + 0.35f * (float)Math.Sin(_totalTime * 5.0);
                MobileUi.DrawRing(sb, _avatarCenter, r, MobileHp * MathHelper.Clamp(pulse, 0f, 1f), r * 0.05f);
            }

            if (_font == null)
                return;

            string level = _state.Level.ToString();
            float levelScale = level.Length >= 4 ? 0.78f : 1.0f;
            var levelSize = _font.MeasureString(level) * levelScale;
            DrawTextWithShadow(sb, level,
                new Vector2(_avatarCenter.X - levelSize.X * 0.5f, _avatarCenter.Y - levelSize.Y * 0.5f),
                MobileText, levelScale);
        }

        /// <summary>
        /// 頭像右側的數值。只有白與灰兩色 —— 顏色的資訊量已經由頭像的弧線承擔了。
        /// </summary>
        private void DrawVitalsText(SpriteBatch sb)
        {
            if (_font == null || _vitalsTextRect.Width <= 0)
                return;

            // 統一級距。數值比標籤大一級 —— 玩家要看的是數字，不是 "HP" 這兩個字母。
            float LabelScale = MobileUi.ScaleFor(MobileUi.TextLabel);
            float ValueScale = MobileUi.ScaleFor(MobileUi.TextBody);
            float SmallScale = MobileUi.ScaleFor(MobileUi.TextLabel);

            int x = _vitalsTextRect.X;
            int y = _vitalsTextRect.Y;

            DrawLabelledValue(sb, "HP", _healthText, x, y, LabelScale, ValueScale);
            y += 30;
            DrawLabelledValue(sb, "MP", _manaText, x, y, LabelScale, ValueScale);
            y += 30;

            // SD 與 AG 放同一行，字級小一階 —— 它們變動不頻繁
            DrawLabelledValue(sb, "SD", _shieldText, x, y, SmallScale, SmallScale);
            DrawLabelledValue(sb, "AG", _abilityText, x + 150, y, SmallScale, SmallScale);
        }

        private void DrawLabelledValue(SpriteBatch sb, string label, string value, int x, int y,
            float labelScale, float valueScale)
        {
            DrawTextWithShadow(sb, label, new Vector2(x, y + 2), MobileTextDim, labelScale);

            float labelWidth = _font!.MeasureString(label).X * labelScale;
            DrawTextWithShadow(sb, value ?? string.Empty,
                new Vector2(x + labelWidth + 8, y), MobileText, valueScale);
        }

        /// <summary>
        /// 右上角的狀態列：時間、電量、FPS、延遲。靠右對齊到介面按鈕區塊，
        /// 不貼螢幕邊緣。純白半透明，不用彩色 —— 這是背景資訊，不該搶注意力。
        /// </summary>
        private void DrawStatusReadout(SpriteBatch sb)
        {
            if (_font == null || _statusReadoutRect.Width <= 0)
                return;

            // 每幀重組字串會產生固定的 GC 壓力，而這行字一秒變一次就夠了。
            if (_totalTime - _statusTextBuiltAt >= 1.0 || _statusText == null)
            {
                _statusTextBuiltAt = _totalTime;

                var parts = new List<string>(4) { DateTime.Now.ToString("HH:mm") };

                float battery = MobileUi.BatteryLevelProvider?.Invoke() ?? -1f;
                if (battery >= 0f)
                    parts.Add($"{(int)MathF.Round(battery * 100f)}%");

                parts.Add($"{(int)Controllers.FPSCounter.Instance.FPS_AVG} FPS");

                if (MuGame.Instance?.ActiveScene is Scenes.GameScene gs && gs.LastPing is int ping)
                    parts.Add($"{ping} ms");

                _statusText = string.Join("   ", parts);
            }

            string text = _statusText;
            float scale = MobileUi.ScaleFor(MobileUi.TextLabel);
            var size = _font.MeasureString(text) * scale;

            DrawTextWithShadow(sb, text,
                new Vector2(_statusReadoutRect.Right - size.X, _statusReadoutRect.Y),
                MobileText * 0.72f, scale);
        }

        /// <summary>金幣。狀態列的下一行，同一條右對齊線。</summary>
        private void DrawZenReadout(SpriteBatch sb)
        {
            if (_font == null || _zenReadoutRect.Width <= 0)
                return;

            long zen = MuGame.Network?.GetCharacterState()?.InventoryZen ?? 0L;

            if (zen != _zenValueBuiltFrom || _zenText == null)
            {
                _zenValueBuiltFrom = zen;
                // 千分位：七位數以上不分節就只是一串數字，看不出量級。
                _zenText = zen.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            }

            // 金幣比狀態列大一級：它是數值，不是狀態
            float scale = MobileUi.ScaleFor(MobileUi.TextBody);
            var size = _font.MeasureString(_zenText) * scale;

            // 右對齊，和上面的狀態列與按鈕區塊同一條線
            var position = new Vector2(_zenReadoutRect.Right - size.X, _zenReadoutRect.Y);
            DrawTextWithShadow(sb, _zenText, position, MobileText * 0.92f, scale);

            // 左邊一個小圓點當作幣值記號。不畫金色的硬幣 ——
            // 整個 HUD 只有這裡出現飽和色的話，眼睛會一直被它拉走。
            var dotCenter = new Vector2(position.X - 16, _zenReadoutRect.Y + size.Y * 0.5f);
            MobileUi.DrawDisc(sb, dotCenter, 5f, Color.White * 0.55f);
        }

        private Rectangle _zenReadoutRect;
        private string _zenText;
        private long _zenValueBuiltFrom = -1;

        private bool AvatarContains(Point position)
            => _avatarRadius > 0f
            && Vector2.Distance(new Vector2(position.X, position.Y), _avatarCenter) <= _avatarRadius;

        private void DrawQuickSlots(SpriteBatch sb, Texture2D pixel)
        {
            for (int i = 0; i < _slotRects.Length; i++)
            {
                var rect = _slotRects[i];
                if (rect.Width <= 0 || rect.Height <= 0)
                    continue;   // 手機上技能格不繪製，見 RefreshMobileLayout

                bool isActive = !IsMobile && i == _activeSkillSlot;
                bool isHovered = i == _hoveredSlot;
                bool isSkillSlot = i >= PotionSlotCount;
                bool isPotionSlot = i < PotionSlotCount;

                // Active slot: outer glow aura
                if (isActive)
                {
                    float glowPulse = 0.35f + 0.15f * (float)Math.Sin(_totalTime * 3.0);
                    var glowRect = new Rectangle(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4);
                    sb.Draw(pixel, glowRect, ModernHudTheme.AccentGlow * glowPulse);
                }

                Rectangle inner;

                if (IsMobile)
                {
                    // 手機的快捷格改成圓形 —— 和右下角的技能鈕同一套語彙，
                    // 手指落點也比方角更寬容。
                    var center = new Vector2(rect.Center.X, rect.Center.Y);
                    float radius = rect.Width * 0.5f;
                    bool pressed = i == _pressedSlot;

                    MobileUi.DrawGlow(sb, center, radius * 1.32f, Color.Black * 0.32f);
                    MobileUi.DrawDisc(sb, center, radius, new Color(8, 10, 14) * 0.38f);
                    MobileUi.DrawRing(sb, center, radius,
                        Color.White * (pressed ? 0.55f : 0.34f), radius * 0.055f);

                    // 內縮越小圖示越大。0.30 時圖示只佔直徑的 6 成，實測偏小；
                    // 0.18 約 7 成 5，藥瓶本身有透明邊，略微超出內接正方形也不會露角。
                    int iconPad = (int)MathF.Round(radius * 0.18f);
                    inner = new Rectangle(rect.X + iconPad, rect.Y + iconPad,
                        Math.Max(1, rect.Width - iconPad * 2), Math.Max(1, rect.Height - iconPad * 2));
                }
                else
                {
                    // Slot outer border
                    Color borderColor = isActive ? ModernHudTheme.Accent
                        : isHovered ? ModernHudTheme.SlotHover
                        : isPotionSlot ? new Color(55, 45, 65, 180) // slightly purple tint for potions
                        : ModernHudTheme.SlotBorder;

                    sb.Draw(pixel, rect, borderColor);

                    // Slot inner background with gradient
                    inner = new Rectangle(rect.X + 1, rect.Y + 1,
                        Math.Max(1, rect.Width - 2), Math.Max(1, rect.Height - 2));
                    UiDrawHelper.DrawVerticalGradient(sb, inner,
                        new Color(16, 18, 24, 245), new Color(8, 10, 14, 250));

                    // Inner top highlight
                    sb.Draw(pixel, new Rectangle(inner.X, inner.Y, inner.Width, 1),
                        ModernHudTheme.BorderHighlight * 0.15f);

                    // Hover highlight overlay
                    if (isHovered && !isActive)
                    {
                        sb.Draw(pixel, inner, ModernHudTheme.SlotHover * 0.15f);
                    }
                }

                // Draw skill icon if assigned
                if (isSkillSlot && _slotSkills[i] != null)
                {
                    DrawSkillIcon(sb, inner, _slotSkills[i]!);
                }

                // Potion slot: draw assigned item icon or empty indicator
                if (isPotionSlot)
                {
                    DrawPotionSlotContent(sb, pixel, inner, i);
                }

                // Key label badge (top-left) —— 手機沒有鍵盤，標 Q/W/E 只是雜訊
                if (_font != null && !IsMobile)
                {
                    string keyLabel = SlotKeyLabels[i];
                    float keyScale = _slotFontScale;
                    var keySize = _font.MeasureString(keyLabel) * keyScale;

                    // Badge background
                    int badgeW = (int)keySize.X + 5;
                    int badgeH = (int)keySize.Y + 2;
                    var badgeRect = new Rectangle(rect.X, rect.Y, badgeW, badgeH);
                    sb.Draw(pixel, badgeRect, Color.Black * 0.55f);

                    float kx = rect.X + 2;
                    float ky = rect.Y + 1;
                    Color keyColor = isActive ? ModernHudTheme.AccentBright
                        : isHovered ? ModernHudTheme.TextWhite
                        : ModernHudTheme.TextGray;
                    sb.DrawString(_font, keyLabel, new Vector2(kx, ky), keyColor,
                        0f, Vector2.Zero, keyScale, SpriteEffects.None, 0f);
                }

                // Active slot bottom indicator bar
                if (isActive)
                {
                    sb.Draw(pixel, new Rectangle(rect.X + 2, rect.Bottom - 2, rect.Width - 4, 2),
                        ModernHudTheme.Accent * 0.9f);
                }
            }
        }

        private void DrawSkillIcon(SpriteBatch sb, Rectangle dest, SkillEntryState skill)
        {
            var definition = SkillDatabase.GetSkillDefinition(skill.SkillId);
            if (!SkillIconAtlas.TryResolve(skill.SkillId, definition, out var frame))
                return;

            var tex = TextureLoader.Instance.GetTexture2D(frame.TexturePath);
            if (tex == null)
                return;

            int pad = 3;
            var iconBounds = new Rectangle(dest.X + pad, dest.Y + pad,
                Math.Max(1, dest.Width - pad * 2), Math.Max(1, dest.Height - pad * 2));

            float fitScale = MathF.Min(
                iconBounds.Width / (float)SkillIconAtlas.IconWidth,
                iconBounds.Height / (float)SkillIconAtlas.IconHeight);

            int drawW = Math.Max(1, (int)MathF.Round(SkillIconAtlas.IconWidth * fitScale));
            int drawH = Math.Max(1, (int)MathF.Round(SkillIconAtlas.IconHeight * fitScale));

            var iconDest = new Rectangle(
                iconBounds.X + (iconBounds.Width - drawW) / 2,
                iconBounds.Y + (iconBounds.Height - drawH) / 2,
                drawW,
                drawH);
            sb.Draw(tex, iconDest, frame.SourceRectangle, Color.White);

            DrawSkillCooldownOverlay(sb, iconDest, skill);
            DrawSkillCooldownTimer(sb, iconDest, skill);
        }

        private void DrawSkillCooldownOverlay(SpriteBatch spriteBatch, Rectangle iconRect, SkillEntryState skill)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (pixel == null)
                return;

            double now = MuGame.Instance?.GameTime?.TotalGameTime.TotalMilliseconds ?? Environment.TickCount64;
            float ratio = SkillCooldownTracker.GetCooldownRatio(skill.SkillId, now);
            if (ratio <= 0f)
                return;

            int overlayHeight = Math.Max(1, (int)(iconRect.Height * ratio));
            var overlayRect = new Rectangle(iconRect.X, iconRect.Y, iconRect.Width, overlayHeight);

            spriteBatch.Draw(pixel, overlayRect, new Color(0, 0, 0, 160) * Alpha);
            spriteBatch.Draw(
                pixel,
                new Rectangle(overlayRect.X, overlayRect.Y + overlayHeight - 1, overlayRect.Width, 1),
                ModernHudTheme.Accent * 0.5f * Alpha);
        }

        private void DrawSkillCooldownTimer(SpriteBatch spriteBatch, Rectangle iconRect, SkillEntryState skill)
        {
            if (_font == null)
                return;

            double now = MuGame.Instance?.GameTime?.TotalGameTime.TotalMilliseconds ?? Environment.TickCount64;
            int remainingMs = SkillCooldownTracker.GetRemainingMs(skill.SkillId, now);
            if (remainingMs <= 0)
                return;

            // 和 TouchActionButtonsControl 同一個公式 —— 那裡修過的 bug 這裡也有：
            // (remainingMs + 99) / 100f 少除了一個 10，剩 999 ms 時算出 10.98，
            // 最後一秒會從 11.0 數回 1.0。無條件捨去到十分位，顯示值不會大於真值。
            string timerText = remainingMs >= 1000
                ? ((remainingMs + 999) / 1000).ToString()
                : (MathF.Floor(remainingMs / 100f) / 10f).ToString("F1");

            float textScale = MobileUi.ScaleFor(MobileUi.TextBody);
            Vector2 textSize = _font.MeasureString(timerText) * textScale;
            float tx = iconRect.X + (iconRect.Width - textSize.X) * 0.5f;
            float ty = iconRect.Y + (iconRect.Height - textSize.Y) * 0.5f;

            spriteBatch.DrawString(_font, timerText, new Vector2(tx + 1f, ty + 1f),
                Color.Black * 0.85f * Alpha, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(_font, timerText, new Vector2(tx, ty),
                ModernHudTheme.TextWhite * Alpha, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
        }

        private void DrawInterfaceButtons(SpriteBatch sb, Texture2D pixel)
        {
            for (int i = 0; i < _btnRects.Length; i++)
            {
                var rect = _btnRects[i];
                bool isHovered = i == _hoveredButton;

                if (IsMobile)
                {
                    // 沒有邊框。六顆按鈕排成 3x2 的話，框線會兩兩相鄰疊在一起 ——
                    // 中間那幾條會比外圍的粗一倍，看起來像沒對齊。
                    // 一塊底色就足以說明「這裡可以按」，按下去再亮一階。
                    sb.Draw(pixel, rect, (isHovered ? MobileUi.TitleBarFill * 1.5f : MobileUi.TitleBarFill) * MobileUi.PanelAlpha);

                    if (_font != null)
                    {
                        string mobileLabel = ActiveButtonLabels[i];
                        var mobileSize = _font.MeasureString(mobileLabel) * _btnFontScale;
                        DrawTextWithShadow(sb, mobileLabel,
                            new Vector2(rect.X + (rect.Width - mobileSize.X) / 2f,
                                        rect.Y + (rect.Height - mobileSize.Y) / 2f),
                            isHovered ? MobileUi.TextPrimary : MobileUi.TextDim,
                            _btnFontScale);
                    }

                    continue;
                }

                // Button border
                sb.Draw(pixel, rect, isHovered ? ModernHudTheme.BorderInner : ModernHudTheme.BorderOuter);

                // Button background with gradient
                var inner = new Rectangle(rect.X + 1, rect.Y + 1,
                    Math.Max(1, rect.Width - 2), Math.Max(1, rect.Height - 2));

                if (isHovered)
                {
                    UiDrawHelper.DrawVerticalGradient(sb, inner,
                        ModernHudTheme.BgLighter, ModernHudTheme.BgMid);
                    // Hover glow underline
                    sb.Draw(pixel, new Rectangle(rect.X + 2, rect.Bottom - 1, rect.Width - 4, 1),
                        ModernHudTheme.Accent * 0.5f);
                }
                else
                {
                    UiDrawHelper.DrawVerticalGradient(sb, inner,
                        ModernHudTheme.BgMid, ModernHudTheme.BgDark);
                }

                // Top highlight
                sb.Draw(pixel, new Rectangle(inner.X, inner.Y, inner.Width, 1),
                    ModernHudTheme.BorderHighlight * (isHovered ? 0.3f : 0.12f));

                // Button text
                if (_font != null)
                {
                    string label = ActiveButtonLabels[i];
                    float btnScale = _btnFontScale;
                    var textSize = _font.MeasureString(label) * btnScale;
                    float tx = rect.X + (rect.Width - textSize.X) / 2f;
                    float ty = rect.Y + (rect.Height - textSize.Y) / 2f;

                    Color textColor = isHovered ? ModernHudTheme.TextGold : ModernHudTheme.TextGray;
                    DrawTextWithShadow(sb, label, new Vector2(tx, ty), textColor, btnScale);
                }
            }
        }

        private void DrawExpBar(SpriteBatch sb, Texture2D pixel)
        {
            if (IsMobile)
            {
                DrawMobileExpBar(sb, pixel);
                return;
            }

            // Frame
            sb.Draw(pixel, _expBarRect, ModernHudTheme.BorderOuter);

            // Track with gradient
            var track = new Rectangle(_expBarRect.X + 1, _expBarRect.Y + 1,
                Math.Max(1, _expBarRect.Width - 2), Math.Max(1, _expBarRect.Height - 2));
            UiDrawHelper.DrawVerticalGradient(sb, track,
                new Color(12, 14, 20, 245), new Color(6, 8, 12, 250));

            // Calculate EXP percentage
            double expPercent = 0;
            if (_state.ExperienceForNextLevel > 0)
            {
                ushort currentLevel = _state.Level;
                ulong prevLevelExp = currentLevel > 1
                    ? (ulong)((currentLevel - 1 + 9) * (currentLevel - 1) * (currentLevel - 1) * 10)
                    : 0;
                ulong expInCurrentLevel = _state.Experience >= prevLevelExp ? _state.Experience - prevLevelExp : 0;
                ulong expNeededForLevel = _state.ExperienceForNextLevel >= prevLevelExp
                    ? _state.ExperienceForNextLevel - prevLevelExp : 1;
                expPercent = expNeededForLevel > 0 ? (expInCurrentLevel / (double)expNeededForLevel) * 100.0 : 0.0;
            }

            float pct = MathHelper.Clamp((float)(expPercent / 100.0), 0f, 1f);
            int fillW = (int)(track.Width * pct);

            if (fillW > 0)
            {
                var fillRect = new Rectangle(track.X, track.Y, fillW, track.Height);

                // Main gradient fill
                UiDrawHelper.DrawHorizontalGradient(sb, fillRect, ExpColorDark, ExpColor);

                // Top shine
                sb.Draw(pixel, new Rectangle(fillRect.X, fillRect.Y, fillRect.Width, 1),
                    ExpColorBright * 0.5f);

                // Bottom shadow
                sb.Draw(pixel, new Rectangle(fillRect.X, fillRect.Bottom - 1, fillRect.Width, 1),
                    Color.Black * 0.3f);

                // Glow at the fill edge
                if (fillW > 3 && ExpGlow.A > 0)
                {
                    int glowW = Math.Min(8, fillW);
                    sb.Draw(pixel, new Rectangle(fillRect.Right - glowW, fillRect.Y, glowW, fillRect.Height),
                        ExpGlow);
                }

                // Animated shimmer moving across the bar
                float shimmerPhase = (float)(_totalTime * 0.3 % 1.0);
                int shimmerX = track.X + (int)(track.Width * shimmerPhase);
                int shimmerW = 20;
                if (shimmerX < fillRect.Right && shimmerX + shimmerW > fillRect.X)
                {
                    int clippedX = Math.Max(shimmerX, fillRect.X);
                    int clippedR = Math.Min(shimmerX + shimmerW, fillRect.Right);
                    int clippedW = clippedR - clippedX;
                    if (clippedW > 0)
                    {
                        sb.Draw(pixel, new Rectangle(clippedX, fillRect.Y, clippedW, fillRect.Height),
                            ExpColorBright * 0.15f);
                    }
                }
            }

            // 10% segment tick marks
            for (int seg = 1; seg < 10; seg++)
            {
                int tickX = track.X + (int)(track.Width * (seg / 10f));
                Color tickColor = tickX < track.X + fillW
                    ? Color.Black * 0.2f
                    : ModernHudTheme.BorderInner * 0.12f;
                sb.Draw(pixel, new Rectangle(tickX, track.Y, 1, track.Height), tickColor);
            }

            // EXP text —— 手機的經驗條只有 8 px 高，塞字反而糊成一團
            if (_font != null && _expBarRect.Height >= 11)
            {
                string expText = $"EXP {expPercent:F1}%";
                float textScale = _expFontScale;
                var textSize = _font.MeasureString(expText) * textScale;
                float tx = _expBarRect.X + (_expBarRect.Width - textSize.X) / 2f;
                float ty = _expBarRect.Y + (_expBarRect.Height - textSize.Y) / 2f;

                // Text shadow
                sb.DrawString(_font, expText, new Vector2(tx + 1, ty + 1),
                    Color.Black * 0.8f, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
                sb.DrawString(_font, expText, new Vector2(tx, ty),
                    ExpColorBright, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
            }
        }

        /// <summary>
        /// 手機的經驗條：單色扁平。原本的漸層、光暈、流光與十等分刻度在
        /// 8 px 高的細條上只會變成雜訊，而且又多帶進三種顏色。
        /// </summary>
        private void DrawMobileExpBar(SpriteBatch sb, Texture2D pixel)
        {
            sb.Draw(pixel, _expBarRect, new Color(8, 10, 14) * 0.55f);

            // 經驗值用補間，不要跳。打完一隻怪條子往前推一小段，
            // 那個動作本身就是回饋 —— 直接跳到新長度等於把回饋丟掉。
            int fillWidth = (int)(_expBarRect.Width * _displayExpPct);

            if (fillWidth > 0)
            {
                sb.Draw(pixel,
                    new Rectangle(_expBarRect.X, _expBarRect.Y, fillWidth, _expBarRect.Height),
                    ExpColor);
            }
        }

        /// <summary>目前等級內的經驗百分比。手機與桌面共用。</summary>
        private double CalculateExpPercent()
        {
            if (_state.ExperienceForNextLevel <= 0)
                return 0;

            ushort currentLevel = _state.Level;
            ulong prevLevelExp = currentLevel > 1
                ? (ulong)((currentLevel - 1 + 9) * (currentLevel - 1) * (currentLevel - 1) * 10)
                : 0;
            ulong expInCurrentLevel = _state.Experience >= prevLevelExp ? _state.Experience - prevLevelExp : 0;
            ulong expNeededForLevel = _state.ExperienceForNextLevel >= prevLevelExp
                ? _state.ExperienceForNextLevel - prevLevelExp : 1;

            return expNeededForLevel > 0 ? (expInCurrentLevel / (double)expNeededForLevel) * 100.0 : 0.0;
        }

        // ════════════════════════════ Potions ════════════════════════════

        private record struct PotionCandidate(byte Group, int Id, string Name, string? TexturePath, int Count, byte FirstSlot);

        private void OpenPotionPicker(int slotIndex)
        {
            _potionPickerSlot = slotIndex;
            BuildPotionCandidates();

            if (_potionCandidates.Count == 0)
            {
                _potionPickerOpen = false;
                return;
            }

            _potionPickerOpen = true;
            LayoutPotionPicker();
        }

        private void BuildPotionCandidates()
        {
            _potionCandidates.Clear();

            var items = _state.GetInventoryItems();
            var grouped = new Dictionary<(byte, int), (string Name, string? TexturePath, int Count, byte FirstSlot)>();

            foreach (var kvp in items)
            {
                if (kvp.Key < 12) continue; // skip equipment slots

                var def = ItemDatabase.GetItemDefinition(kvp.Value);
                if (def == null || !def.IsQuickSlotConsumable() || def.IsJewel() || def.IsUpgradeJewel())
                    continue;

                byte durability = ItemDatabase.GetItemDurability(kvp.Value);
                int stack = Math.Max(1, (int)durability);

                var key = ((byte)def.Group, def.Id);
                if (grouped.TryGetValue(key, out var existing))
                {
                    grouped[key] = (existing.Name, existing.TexturePath, existing.Count + stack, existing.FirstSlot);
                }
                else
                {
                    grouped[key] = (def.Name ?? $"Item {def.Group}/{def.Id}", def.TexturePath, stack, kvp.Key);
                }
            }

            foreach (var kvp in grouped.OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2))
            {
                _potionCandidates.Add(new PotionCandidate(
                    kvp.Key.Item1, kvp.Key.Item2,
                    kvp.Value.Name, kvp.Value.TexturePath,
                    kvp.Value.Count, kvp.Value.FirstSlot));
            }
        }

        private void LayoutPotionPicker()
        {
            if (_potionPickerSlot < 0 || _potionPickerSlot >= _slotRects.Length || _potionCandidates.Count == 0)
                return;

            int itemH = 28;
            int padX = 6;
            int padY = 4;
            int pickerW = 180;
            int pickerH = padY * 2 + _potionCandidates.Count * itemH;

            var slotRect = _slotRects[_potionPickerSlot];
            int pickerX = slotRect.X + (slotRect.Width - pickerW) / 2;
            int pickerY = slotRect.Y - pickerH - 4;

            // Clamp to screen
            // 夾到對齊線，不是螢幕邊緣
            pickerX = Math.Clamp(pickerX,
                MobileUi.LeftEdge,
                Math.Max(MobileUi.LeftEdge, MobileUi.RightEdge - pickerW));
            pickerY = Math.Max(2, pickerY);

            _potionPickerRect = new Rectangle(pickerX, pickerY, pickerW, pickerH);

            _potionPickerItemRects = new Rectangle[_potionCandidates.Count];
            for (int i = 0; i < _potionCandidates.Count; i++)
            {
                _potionPickerItemRects[i] = new Rectangle(
                    pickerX + padX, pickerY + padY + i * itemH,
                    pickerW - padX * 2, itemH);
            }
        }

        private void DrawPotionPicker(SpriteBatch sb, Texture2D pixel)
        {
            if (_potionCandidates.Count == 0)
                return;

            // Background
            sb.Draw(pixel, _potionPickerRect, ModernHudTheme.BorderOuter);
            var inner = new Rectangle(_potionPickerRect.X + 1, _potionPickerRect.Y + 1,
                Math.Max(1, _potionPickerRect.Width - 2), Math.Max(1, _potionPickerRect.Height - 2));
            UiDrawHelper.DrawVerticalGradient(sb, inner,
                new Color(22, 26, 35, 250), new Color(12, 14, 20, 255));

            // Top accent
            sb.Draw(pixel, new Rectangle(inner.X + 2, inner.Y, Math.Max(1, inner.Width - 4), 1),
                ModernHudTheme.Accent * 0.5f);

            for (int i = 0; i < _potionCandidates.Count; i++)
            {
                var candidate = _potionCandidates[i];
                var rect = _potionPickerItemRects[i];
                bool hovered = i == _hoveredPotionCandidate;

                if (hovered)
                {
                    sb.Draw(pixel, rect, ModernHudTheme.SlotHover * 0.25f);
                }

                // Icon area (left side)
                int iconSize = Math.Min(rect.Height - 4, 22);
                var iconRect = new Rectangle(rect.X + 2, rect.Y + (rect.Height - iconSize) / 2, iconSize, iconSize);

                // Draw item icon
                var candidateDef = ItemDatabase.GetItemDefinition(candidate.Group, (short)candidate.Id);
                Texture2D? iconTex = ResolveItemIcon(candidateDef);
                if (iconTex != null)
                {
                    sb.Draw(iconTex, iconRect, Color.White);
                }
                else
                {
                    // Fallback colored square
                    sb.Draw(pixel, iconRect, new Color(60, 50, 80) * 0.5f);
                }

                // Name text
                if (_font != null)
                {
                    float nameScale = MobileUi.ScaleFor(MobileUi.TextLabel);
                    string displayName = candidate.Name;
                    float nameX = iconRect.Right + 5;
                    float nameY = rect.Y + (rect.Height - _font.MeasureString(displayName).Y * nameScale) / 2f;

                    Color nameColor = hovered ? ModernHudTheme.TextGold : ModernHudTheme.TextWhite;
                    DrawTextWithShadow(sb, displayName, new Vector2(nameX, nameY), nameColor, nameScale);

                    // Count (right-aligned)
                    string countText = $"x{candidate.Count}";
                    var countSize = _font.MeasureString(countText) * nameScale;
                    float countX = rect.Right - countSize.X - 2;
                    float countY = nameY;
                    DrawTextWithShadow(sb, countText, new Vector2(countX, countY), ModernHudTheme.TextGray, nameScale);
                }

                // Separator line
                if (i < _potionCandidates.Count - 1)
                {
                    sb.Draw(pixel, new Rectangle(rect.X, rect.Bottom, rect.Width, 1),
                        ModernHudTheme.BorderInner * 0.15f);
                }
            }

            UiDrawHelper.DrawCornerAccents(sb, _potionPickerRect,
                ModernHudTheme.Accent * 0.3f, size: 5, thickness: 1);
        }

        private void DrawPotionSlotContent(SpriteBatch sb, Texture2D pixel, Rectangle inner, int slotIndex)
        {
            var assignment = _potionAssignments[slotIndex];
            if (assignment == null)
            {
                // Empty potion slot indicator
                if (_font != null && IsMobile)
                {
                    // 手機沒有滑鼠提示，空格畫個 "+" 明示「點一下可以指派」
                    const string plus = "+";
                    float plusScale = _slotFontScale * 2.0f;
                    var plusSize = _font.MeasureString(plus) * plusScale;
                    sb.DrawString(_font, plus,
                        new Vector2(inner.Center.X - plusSize.X * 0.5f, inner.Center.Y - plusSize.Y * 0.5f),
                        new Color(190, 200, 220) * 0.5f, 0f, Vector2.Zero, plusScale, SpriteEffects.None, 0f);
                }
                else if (_font != null)
                {
                    int dSize = 4;
                    int cx = inner.X + inner.Width / 2;
                    int cy = inner.Y + inner.Height / 2 + 2;
                    sb.Draw(pixel, new Rectangle(cx - dSize / 2, cy - dSize / 2, dSize, dSize),
                        new Color(100, 80, 130) * 0.35f);
                }
                return;
            }

            var (group, id) = assignment.Value;
            var def = ItemDatabase.GetItemDefinition(group, (short)id);
            if (def == null) return;

            // Draw item icon
            Texture2D? tex = ResolveItemIcon(def);
            if (tex != null)
            {
                int pad = 3;
                var iconDest = new Rectangle(inner.X + pad, inner.Y + pad,
                    Math.Max(1, inner.Width - pad * 2), Math.Max(1, inner.Height - pad * 2));
                sb.Draw(tex, iconDest, Color.White);
            }

            // Count badge (bottom-right)
            if (_font != null)
            {
                int count = CountPotionInInventory(group, id);
                if (count > 0)
                {
                    string countText = count.ToString();
                    float countScale = _slotFontScale * 0.9f;
                    var countSize = _font.MeasureString(countText) * countScale;
                    float cx = inner.Right - countSize.X - 1;
                    float cy = inner.Bottom - countSize.Y - 1;

                    // Badge background
                    sb.Draw(pixel, new Rectangle((int)cx - 1, (int)cy, (int)countSize.X + 3, (int)countSize.Y + 1),
                        Color.Black * 0.65f);
                    sb.DrawString(_font, countText, new Vector2(cx, cy),
                        ModernHudTheme.TextWhite, 0f, Vector2.Zero, countScale, SpriteEffects.None, 0f);
                }
                else if (IsMobile)
                {
                    // No stock — dim the icon（圓形格子用圓形遮罩，方形遮罩會露出角）
                    MobileUi.DrawDisc(sb, new Vector2(inner.Center.X, inner.Center.Y),
                        inner.Width * 0.5f + 2f, Color.Black * 0.55f);
                }
                else
                {
                    // No stock — dim the icon
                    sb.Draw(pixel, inner, Color.Black * 0.5f);
                }
            }
        }

        private void ConsumePotionInSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= PotionSlotCount)
                return;

            var assignment = _potionAssignments[slotIndex];
            if (assignment == null) return;

            var (group, id) = assignment.Value;

            // Find first matching item in inventory
            var items = _state.GetInventoryItems();
            byte? foundSlot = null;

            foreach (var kvp in items)
            {
                if (kvp.Key < 12) continue;

                var def = ItemDatabase.GetItemDefinition(kvp.Value);
                if (def != null && def.Group == group && def.Id == id)
                {
                    foundSlot = kvp.Key;
                    break;
                }
            }

            if (foundSlot == null) return;

            // Play consumption sound
            var itemDef = ItemDatabase.GetItemDefinition(group, (short)id);
            string itemName = itemDef?.Name?.ToLowerInvariant() ?? string.Empty;
            if (itemName.Contains("apple"))
                SoundController.Instance.PlayBuffer("Sound/pEatApple.wav");
            else
                SoundController.Instance.PlayBuffer("Sound/pDrink.wav");

            byte slot = foundSlot.Value;
            var svc = MuGame.Network?.GetCharacterService();
            if (svc != null)
            {
                _ = Task.Run(async () =>
                {
                    await svc.SendConsumeItemRequestAsync(slot);
                    await Task.Delay(300);
                    MuGame.ScheduleOnMainThread(() => _state.RaiseInventoryChanged());
                });
            }
        }

        private int CountPotionInInventory(byte group, int id)
        {
            int total = 0;
            var items = _state.GetInventoryItems();

            foreach (var kvp in items)
            {
                if (kvp.Key < 12) continue;

                var def = ItemDatabase.GetItemDefinition(kvp.Value);
                if (def != null && def.Group == group && def.Id == id)
                {
                    byte durability = ItemDatabase.GetItemDurability(kvp.Value);
                    total += Math.Max(1, (int)durability);
                }
            }

            return total;
        }

        private Texture2D? ResolveItemIcon(ItemDefinition? def)
        {
            if (def?.TexturePath == null)
                return null;

            string texturePath = def.TexturePath;

            // BMD models: use pre-cached preview at fixed size (generated in Update, scaled on draw)
            if (texturePath.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase))
                return BmdPreviewRenderer.TryGetCachedPreview(def, PotionIconCacheSize, PotionIconCacheSize);

            // Non-BMD textures: load directly
            if (_potionTextureCache.TryGetValue(texturePath, out var cached))
                return cached;

            var tex = TextureLoader.Instance.GetTexture2D(texturePath);
            if (tex != null)
                _potionTextureCache[texturePath] = tex;

            return tex;
        }

        // ════════════════════════════ Helpers ════════════════════════════

        private void DrawTextWithShadow(SpriteBatch sb, string text, Vector2 pos, Color color, float scale)
        {
            sb.DrawString(_font!, text, pos + new Vector2(1, 1),
                Color.Black * 0.7f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.DrawString(_font!, text, pos, color,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }
}
