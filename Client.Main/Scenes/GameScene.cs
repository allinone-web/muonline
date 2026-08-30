// GameScene.cs
using Client.Main.Controls;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Game;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Client.Main.Worlds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using Client.Main.Objects;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Effects.Skills;
using Client.Main.Core.Utilities;
using Client.Main.Networking.PacketHandling.Handlers; // For CharacterClassNumber
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Controls.UI.Game.Map;
using Client.Main.Controls.UI.Game.Party;
using Client.Main.Controls.UI.Game.PauseMenu;
using Client.Main.Controls.UI.Game.Character;
using Client.Main.Controls.UI.Game.Trade;
using Client.Main.Controls.UI.Game.Quest;
using Microsoft.Xna.Framework.Graphics;
using Client.Main.Networking;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using Client.Main.Controls.UI.Game.Buffs;
using Client.Main.Controls.UI.Game.Hud;
using MUnique.OpenMU.Network.Packets;
using Client.Main.Controllers;
using Client.Main.Helpers;

namespace Client.Main.Scenes
{
    public class GameScene : BaseScene
    {
        // ──────────────────────────── Fields ────────────────────────────
        private readonly HeroObject _hero;
        private ModernBottomHud _modernHud;
        private EquipmentDurabilityHud _equipmentDurabilityHud;
        private GameSceneMapController _mapController;
        private MapListControl _mapListControl;
        private ChatLogWindow _chatLog;
        private MoveCommandWindow _moveCommandWindow;
        private ChatInputBoxControl _chatInput;
        private InventoryControl _inventoryControl;
        private Controls.UI.NotificationManager _notificationManager;
        private PartyPanelControl _partyPanel;
        private readonly (string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance) _characterInfo;
        private CharacterInfoWindowControl _characterInfoWindow;
        private MiniMapControl _miniMap;
        private ILogger _logger = MuGame.AppLoggerFactory?.CreateLogger<GameScene>() ?? NullLogger<GameScene>.Instance;
        private LabelControl _pingLabel; // Displays current ping
        private LabelControl _fpsLabel; // Displays current FPS independently of DebugPanel
        private double _pingTimer = 0;
        private double _fpsTimer = 0;
        private int? _lastPingValue = null;
        private int _lastFpsValue = -1;
        private PauseMenuControl _pauseMenu; // ESC menu
        // (SkillQuickSlot removed — replaced by ModernBottomHud)
        private Controls.UI.Game.Skills.SkillSelectionPanel _skillSelectionPanel; // Skill selection panel (independent)
        private CurrentLocationControl _currentLocationControl; // Current map + coordinates (top-left)
        private ActiveBuffsPanel _activeBuffsPanel; // Active buffs display (top-left corner)
        private Texture2D _backgroundTexture;
        private ProgressBarControl _progressBar;
        private GameSceneSkillController _skillController;
        private GameSceneNotificationController _notificationController;
        private GameScenePlayerMenuController _playerMenuController;
        private GameSceneHotkeys _hotkeys;
        private GameSceneScopeImportController _scopeImportController;
        private GameSceneObjectEditorController _objectEditorController;
        private GameSceneDuelController _duelController;
        private GameSceneChatController _chatController;
        private GameSceneUiPreloadController _uiPreloadController;
        private GameSceneWindowCloseController _windowCloseController;
        private Task _sceneShellInitializationTask;
        private Task _firstPresentedFramePreparationTask;
        private LoadingScreenControl _initialLoadingScreen;
        private bool _sceneShellInitialized;
        private Action _pendingWorldActivation;
        private TaskCompletionSource<bool> _pendingWorldActivationCompletion;
        private bool _pendingWorldActivationScheduled;
        private bool _pendingWorldActivationCleansLoadingUi;
        private string _pendingWorldActivationName;
        private bool _initialWorldActivationCooldown;
        private bool _initialWorldLoadInProgress = true;

        // Performance optimization fields - track object IDs for O(1) lookups
        // ───────────────────────── Properties ─────────────────────────
        public HeroObject Hero => _hero;

        /// <summary>手機用：對最近的敵人施放技能（null 為普通攻擊）。</summary>
        public bool AttackNearestEnemy(Core.Client.SkillEntryState skill)
            => _skillController?.AttackNearestEnemy(skill) ?? false;

        /// <summary>最近一次出手失敗的原因（可能為 null = 不需要回報）。</summary>
        public string LastSkillFailureReason => _skillController?.LastFailureReason;

        /// <summary>劍士連擊的進度（僅供顯示，傷害加成由伺服器計算）。</summary>
        public Core.Client.SkillComboTracker ComboTracker { get; } = new();

        // 手機用的虛擬搖桿，取代點擊移動（見 UpdateVirtualJoystick）
        private VirtualJoystickControl _virtualJoystick;
        // 卡住診斷的心跳計時（見 UpdateMoveDiagnosticsHeartbeat）
        private double _moveDiagElapsedSeconds;
        private TouchActionButtonsControl _touchActionButtons;
        private TouchPickupListControl _touchPickupList;

        /// <summary>搖桿是否啟用 —— 只有觸控平台需要。</summary>
        public static bool UseVirtualJoystick => OperatingSystem.IsIOS() || OperatingSystem.IsAndroid();

        /// <summary>手機的聊天視窗上緣，需避開左上角的 HP/MP/SD/AG 與寵物血條（見 ModernBottomHud.RefreshMobileLayout）。</summary>
        private const int MobileChatLogTop = 140;

        /// <summary>手機的 FPS / Ping 上緣，需避開右上角的介面按鈕。</summary>
        private const int MobileStatusLabelTop = 112;

        /// <summary>手機的左側視窗起點：頭像框右緣之後。</summary>
        private const int MobileCharWindowLeft = 140;

        /// <summary>
        /// 座標是否落在手機的 UI 上（HUD、動作按鈕，或任何開著的視窗）。
        ///
        /// 沒有這個判斷，開著背包挑東西時每一次點擊都會同時把角色指令出去。
        /// </summary>
        private bool IsPointOverTouchUi(Point position)
        {
            if (_modernHud?.ContainsInteractivePoint(position) ?? false)
                return true;

            if (_touchActionButtons?.ContainsPoint(new Vector2(position.X, position.Y)) ?? false)
                return true;

            if (_touchPickupList?.ContainsPoint(position) ?? false)
                return true;

            return IsPointOverOpenWindow(position);
        }

        /// <summary>
        /// 座標是否落在某個「開著的視窗」上（背包、商店、技能面板…）。
        ///
        /// MouseControl 是這一幀游標下最上層的「可互動且可見」控制項（見 BaseScene）。
        /// 動作按鈕與搖桿都不走 UI 的點擊路由，兩者都得自己問這個問題 ——
        /// 少了它，開著技能面板選技能時，同一次觸控會連帶把角色的技能也放出去。
        /// </summary>
        public bool IsPointOverOpenWindow(Point position)
        {
            _ = position;   // 命中判定已由 BaseScene 完成，見下方註解

            var control = MouseControl;
            if (control == null || ReferenceEquals(control, World))
                return false;

            // 保險：若某個控制項幾乎鋪滿整個畫面（例如全螢幕的容器），
            // 擋掉搖桿等於讓玩家完全動不了，而且是無聲的。這種情況不擋。
            var rect = control.DisplayRectangle;
            var size = UiScaler.VirtualSize;
            if (rect.Width >= size.X * 0.9f && rect.Height >= size.Y * 0.9f)
                return false;

            // MouseControl 依定義就是這個座標下最上層的可互動控制項，
            // 命中判定已經由 BaseScene 做過，這裡不再自行比對矩形
            // —— 有些控制項的命中形狀不等於 DisplayRectangle。
            return true;
        }
        public ChatLogWindow ChatLog => _chatLog;

        /// <summary>手機的 MAP / CHAT 按鈕需要 —— 桌面是 M / Enter 快捷鍵開啟。</summary>
        public MiniMapControl MiniMap => _miniMap;
        public ChatInputBoxControl ChatInput => _chatInput;

        /// <summary>最近一次量到的延遲（毫秒）。手機的狀態列由 ModernBottomHud 繪製。</summary>
        public int? LastPing => _lastPingValue;

        /// <summary>
        /// 撿起指定的掉落物（或金幣）。
        ///
        /// 桌面是按空白鍵撿最近的一件（GameSceneHotkeys），手機沒有鍵盤、
        /// 也已經停用點擊世界，因此撿東西的功能一度整個消失。
        /// 這裡把流程抽出來共用，觸控的撿取清單（TouchPickupListControl）指定要撿哪一件。
        /// </summary>
        /// <returns>是否成功送出請求。</returns>
        public bool PickupItem(ushort rawId)
        {
            var network = MuGame.Network;
            var scopeManager = network?.GetScopeManager();
            var characterState = network?.GetCharacterState();
            if (scopeManager == null || characterState == null)
                return false;

            ushort maskedId = (ushort)(rawId & 0x7FFF);
            var scopeObject = scopeManager.GetScopeObjectByMaskedId(maskedId);
            if (scopeObject == null)
            {
                _logger?.LogWarning("Pickup: scope object {MaskedId:X4} disappeared before request", maskedId);
                return false;
            }

            // 要先把待撿取的資料暫存起來，伺服器回覆時才知道該把什麼放進背包
            characterState.SetPendingPickupRawId(rawId);

            if (scopeObject is Core.Models.ItemScopeObject itemScope)
            {
                characterState.StashPickedItem(itemScope.ItemData.ToArray());
            }
            else if (scopeObject is not Core.Models.MoneyScopeObject)
            {
                _logger?.LogWarning("Pickup: unsupported scope object type {Type}", scopeObject.ObjectType);
                return false;
            }

            var characterService = network.GetCharacterService();
            if (characterService == null)
                return false;

            _ = Task.Run(async () =>
            {
                try
                {
                    await characterService.SendPickupItemRequestAsync(rawId, network.TargetVersion);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during pickup request for RawId {RawId}", rawId);
                }
            });

            return true;
        }

        public InventoryControl InventoryControl => _inventoryControl;
        public TradeControl TradeControl => TradeControl.Instance;
        public PauseMenuControl PauseMenu => _pauseMenu;

        public override bool CanRenderWhileInitializing => true;

        public static readonly IReadOnlyDictionary<byte, Type> MapWorldRegistry = DiscoverWorlds();

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "World registry uses reflection; trimming is not supported for scene discovery.")]
        private static IReadOnlyDictionary<byte, Type> DiscoverWorlds()
        {
            var registry = new Dictionary<byte, Type>();
            var worldTypes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(WalkableWorldControl).IsAssignableFrom(t));

            foreach (var type in worldTypes)
            {
                var attr = type.GetCustomAttribute<WorldInfoAttribute>();
                if (attr != null)
                {
                    if (!registry.TryAdd((byte)attr.MapId, type))
                    {
                        // Optionally log a warning about duplicate MapId
                    }
                }
            }
            return registry;
        }

        // ──────────────────────── Constructors ────────────────────────
        public GameScene((string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance) characterInfo)
        {
            _characterInfo = characterInfo;
            _logger?.LogDebug(
                "GameScene shell created for Character: {Name} ({Class})",
                _characterInfo.Name,
                _characterInfo.Class);

            // Keep the constructor intentionally small. Scene construction happens inside a
            // main-thread dispatcher action, so building the full HUD here previously made
            // HandleEnteredGame block the game for roughly 150 ms.
            _hero = new HeroObject(new AppearanceData(characterInfo.Appearance));
        }

        public override Task PrepareForFirstPresentedFrameAsync()
        {
            _firstPresentedFramePreparationTask ??= PrepareFirstPresentedFrameCoreAsync();
            return _firstPresentedFramePreparationTask;
        }

        private async Task PrepareFirstPresentedFrameCoreAsync()
        {
            // Build only scene-owned loading resources here. Shared/singleton game controls are
            // attached after the previous GameScene has been disposed, so a GameScene-to-GameScene
            // fallback cannot detach or reset controls which already belong to the new scene.
            if (_backgroundTexture == null)
            {
                try
                {
                    _backgroundTexture = MuGame.Instance.Content.Load<Texture2D>("Background");
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug("[GameScene] Background load failed: {Message}", ex.Message);
                }
            }

            if (_progressBar == null)
            {
                _progressBar = new ProgressBarControl
                {
                    Progress = 0.01f,
                    StatusText = "Preparing game interface...",
                    Visible = true
                };
                Controls.Add(_progressBar);
            }

            if (_initialLoadingScreen == null)
            {
                _initialLoadingScreen = new LoadingScreenControl
                {
                    Visible = true,
                    Message = "Preparing game interface...",
                    Progress = 0.01f
                };
                Controls.Add(_initialLoadingScreen);
            }

            if (_progressBar.Status == GameControlStatus.NonInitialized)
                await _progressBar.Initialize();

            if (_initialLoadingScreen.Status == GameControlStatus.NonInitialized)
                await _initialLoadingScreen.Initialize();
        }

        public override async Task InitializeWithProgressReporting(Action<string, float> progressCallback)
        {
            await PrepareForFirstPresentedFrameAsync();

            Action<string, float> effectiveProgressCallback = progressCallback ?? UpdateLoadProgress;
            _sceneShellInitializationTask ??= InitializeSceneShellAsync(effectiveProgressCallback);
            await _sceneShellInitializationTask;

            await MuGame.YieldToNextFrameAsync(
                "GameScene.InitializeControls",
                MainThreadDispatcher.WorkPriority.High);
            await base.InitializeWithProgressReporting(effectiveProgressCallback);
        }

        private async Task InitializeSceneShellAsync(Action<string, float> progressCallback)
        {
            void Report(string message, float progress)
            {
                progressCallback?.Invoke(message, progress);

                var loading = _initialLoadingScreen ?? _mapController?.LoadingScreen;
                if (loading != null)
                {
                    loading.Message = message;
                    loading.Progress = progress;
                }

                if (_progressBar != null)
                {
                    _progressBar.StatusText = message;
                    _progressBar.Progress = progress;
                }
            }

            Report("Preparing game interface...", 0.01f);

            // Phase 1: controls required by the loading and messaging paths.
            Controls.Add(NpcShopControl.Instance);
            Controls.Add(VaultControl.Instance);
            Controls.Add(ChaosMixControl.Instance);
            Controls.Add(TradeControl.Instance);
            Controls.Add(QuestDialogControl.Instance);
            Controls.Add(DevilSquareEnterControl.Instance);
            Controls.Add(BloodCastleEnterControl.Instance);
            Controls.Add(BloodCastleTimeControl.Instance);
            Controls.Add(BloodCastleResultControl.Instance);

            if (UseVirtualJoystick)
            {
                _virtualJoystick = new VirtualJoystickControl
                {
                    // 搖桿不走 UI 的點擊路由，得自己避開 HUD，
                    // 否則按底部的藥水鈕會同時把角色指令出去。
                    IsBlocked = IsPointOverTouchUi
                };
                Controls.Add(_virtualJoystick);

                _touchActionButtons = new TouchActionButtonsControl(
                    index => _modernHud?.GetMobileSkill(index),
                    index => _modernHud?.OpenMobileSkillAssignment(index));
                Controls.Add(_touchActionButtons);

                // 撿東西：桌面靠空白鍵或滑鼠點地面，手機兩條路都沒有（見 TouchPickupListControl）
                _touchPickupList = new TouchPickupListControl();
                Controls.Add(_touchPickupList);
            }

            _mapListControl = new MapListControl { Visible = false };
            // 手機：聊天視窗原本在左下角，正好蓋在虛擬搖桿的啟用區上 ——
            // 要嘛按不到聊天，要嘛想打字卻讓角色跑起來。改放到左上、資源條下方，
            // 把整個左下角讓給搖桿。
            _chatLog = new ChatLogWindow
            {
                X = UseVirtualJoystick ? 14 : 5,
                Y = UseVirtualJoystick
                    ? MobileChatLogTop
                    : UiScaler.VirtualSize.Y - 160 - ChatInputBoxControl.CHATBOX_HEIGHT
            };
            Controls.Add(_chatLog);

            if (UseVirtualJoystick)
            {
                // 手機的左側是直的一長條：資源條 → 寵物 → 聊天。
                // 聊天保持精簡，才不會往下長到搖桿的啟用區。
                _chatLog.SetShowingLines(4);
            }

            _chatInput = new ChatInputBoxControl(_chatLog, MuGame.AppLoggerFactory)
            {
                X = 5,
                Y = UiScaler.VirtualSize.Y - 65 - ChatInputBoxControl.CHATBOX_HEIGHT
            };
            Controls.Add(_chatInput);

            // 輸入框本來就是隱藏起步的；手機沒有 Enter 鍵，
            // 改由 HUD 的 CHAT 按鈕開啟（見 ModernBottomHud.OnButtonClicked）。
            _duelController = new GameSceneDuelController(this, _chatLog, _logger);

            _notificationManager = new Controls.UI.NotificationManager();
            Controls.Add(_notificationManager);
            _notificationManager.BringToFront();
            _notificationController = new GameSceneNotificationController(_notificationManager, _chatLog);
            _notificationController.AddPending(ChatMessageHandler.TakePendingServerMessages());
            _scopeImportController = new GameSceneScopeImportController(this, _logger);

            await MuGame.YieldToNextFrameAsync(
                "GameScene.BuildShell.Inventory",
                MainThreadDispatcher.WorkPriority.High);

            // Phase 2: inventory and common windows.
            _inventoryControl = new InventoryControl(MuGame.Network, MuGame.AppLoggerFactory);
            Controls.Add(_inventoryControl);
            _inventoryControl.HookEvents();
            _windowCloseController = new GameSceneWindowCloseController(_inventoryControl, _logger);

            _moveCommandWindow = new MoveCommandWindow(MuGame.AppLoggerFactory, MuGame.Network);
            Controls.Add(_moveCommandWindow);
            _moveCommandWindow.MapWarpRequested += OnMapWarpRequested;

            // 手機：預設位置會被左上角的頭像與資源文字蓋住（HUD 畫在最上層），
            // 往右移到頭像旁邊、往下移到文字下方。
            _characterInfoWindow = new CharacterInfoWindowControl
            {
                X = UseVirtualJoystick ? MobileCharWindowLeft : 20,
                Y = UseVirtualJoystick ? InventoryControl.MobileTopSafeY : 50,

                // 不再整體放大。
                //
                // 1.35 倍只是「變大的擁擠」—— 280 寬要塞下屬性名、數值、三行說明
                // 和加點鈕，放大之後每一欄仍然只有幾十像素。
                // 改成讓 CharacterInfoWindowControl 自己有一套手機版面
                // （460x660、每列 96 高、加點鈕 52 見方），這裡就不需要縮放了。
                Scale = 1f,
                Visible = false
            };
            Controls.Add(_characterInfoWindow);
            _miniMap = new MiniMapControl(this);
            Controls.Add(_miniMap);
            _partyPanel = new PartyPanelControl();
            Controls.Add(_partyPanel);

            // 手機的右上角是介面按鈕（3 欄 2 列，見 ModernBottomHud.RefreshMobileLayout），
            // FPS / Ping 要讓到按鈕下方，否則兩者疊在一起。
            int statusTop = UseVirtualJoystick ? MobileStatusLabelTop : 5;

            // 手機把 FPS / Ping / 時間 / 電量合併成 HUD 右上角的一行
            // （見 ModernBottomHud.DrawStatusReadout），這兩個標籤只在桌面顯示。
            _fpsLabel = new LabelControl
            {
                Text = "FPS: --",
                Align = ControlAlign.Top | ControlAlign.Right,
                Margin = new Margin { Top = statusTop, Right = 5 },
                FontSize = 10,
                TextColor = Color.LightGreen,
                Visible = !UseVirtualJoystick
            };
            Controls.Add(_fpsLabel);

            _pingLabel = new LabelControl
            {
                Text = "Ping: --",
                Align = ControlAlign.Top | ControlAlign.Right,
                Margin = new Margin { Top = statusTop + 17, Right = 5 },
                FontSize = 10,
                TextColor = Color.White,
                Visible = !UseVirtualJoystick
            };
            Controls.Add(_pingLabel);

            await MuGame.YieldToNextFrameAsync(
                "GameScene.BuildShell.Hud",
                MainThreadDispatcher.WorkPriority.High);

            // Phase 3: HUD and interaction controllers.
            var characterState = MuGame.Network.GetCharacterState();
            _pauseMenu = new PauseMenuControl();
            Controls.Add(_pauseMenu);

            _skillSelectionPanel = new Controls.UI.Game.Skills.SkillSelectionPanel();
            Controls.Add(_skillSelectionPanel);

            _modernHud = new ModernBottomHud(characterState, _skillSelectionPanel);
            Controls.Add(_modernHud);
            _equipmentDurabilityHud = new EquipmentDurabilityHud(characterState);
            Controls.Add(_equipmentDurabilityHud);
            _skillController = new GameSceneSkillController(
                this,
                _modernHud,
                _logger,
                _duelController.IsDuelAttackTarget);

            _currentLocationControl = new CurrentLocationControl(characterState);
            Controls.Add(_currentLocationControl);
            _activeBuffsPanel = new ActiveBuffsPanel(characterState, _currentLocationControl);
            Controls.Add(_activeBuffsPanel);

            var duelHud = new DuelHudControl(characterState);
            Controls.Add(duelHud);
            Controls.Add(DevilSquareCountdownControl.Instance);

            await MuGame.YieldToNextFrameAsync(
                "GameScene.BuildShell.Controllers",
                MainThreadDispatcher.WorkPriority.High);

            // Phase 4: interaction controllers.
            _playerMenuController = new GameScenePlayerMenuController(
                this,
                StartWhisperToPlayer,
                _duelController.OnDuelRequestedFromContextMenu);
            _playerMenuController.Initialize();
            _objectEditorController = new GameSceneObjectEditorController(this, _logger);
            _objectEditorController.Initialize();
            _hotkeys = new GameSceneHotkeys(
                this,
                _pauseMenu,
                _playerMenuController,
                _moveCommandWindow,
                _inventoryControl,
                _characterInfoWindow,
                _miniMap,
                _chatInput,
                _chatLog,
                _objectEditorController,
                _logger);

            await MuGame.YieldToNextFrameAsync(
                "GameScene.BuildShell.LoadingInfrastructure",
                MainThreadDispatcher.WorkPriority.High);

            // Phase 5: complete the loading infrastructure prepared before scene activation.
            // The background and progress controls already exist, so no active frame can expose
            // only the cleared render target while this heavier shell is being assembled.
            _mapController = new GameSceneMapController(
                this,
                _modernHud,
                _progressBar,
                _chatLog,
                _chatInput,
                _mapListControl,
                DebugPanel,
                Cursor,
                _scopeImportController,
                _logger,
                _initialLoadingScreen);
            _initialLoadingScreen = null;
            _mapController.EnsureLoadingScreen();
            _chatController = new GameSceneChatController(_mapController, _duelController, _chatLog, _logger);
            _chatInput.MessageSendRequested += _chatController.OnChatMessageSendRequested;
            _uiPreloadController = new GameSceneUiPreloadController(this, _logger);

            await MuGame.YieldToNextFrameAsync(
                "GameScene.BuildShell.Ordering",
                MainThreadDispatcher.WorkPriority.High);

            // Phase 6: z-order changes are separated because BringToFront mutates the controls
            // collection and repeatedly recalculates ordering.
            _fpsLabel.BringToFront();
            _pingLabel.BringToFront();
            _chatInput.BringToFront();
            _pauseMenu.BringToFront();
            _modernHud.BringToFront();
            _virtualJoystick?.BringToFront();
            _touchActionButtons?.BringToFront();
            _touchPickupList?.BringToFront();
            _currentLocationControl.BringToFront();
            _activeBuffsPanel.BringToFront();
            duelHud.BringToFront();
            DevilSquareCountdownControl.Instance.BringToFront();
            DebugPanel.BringToFront();
            Cursor.BringToFront();

            _sceneShellInitialized = true;
            Report("Game interface prepared.", 0.04f);

            // Optional assets are deliberately not awaited by the scene transition.
            _ = _uiPreloadController.StartPreloadAsync();
        }

        public GameScene() : this(GetCharacterInfoFromState())
        {
        }

        public GameScene((string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance) characterInfo, NetworkManager networkManager)
            : this(characterInfo)
        {
            // Optionally store networkManager if needed in the future
        }

        private static (string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance) GetCharacterInfoFromState()
        {
            var state = MuGame.Network?.GetCharacterState();
            if (state != null)
            {
                return (state.Name ?? "Unknown", state.Class, state.Level, Array.Empty<byte>());
            }
            return ("Unknown", CharacterClassNumber.DarkKnight, 1, Array.Empty<byte>());
        }

        // ───────────────────── Content Loading (Progressive) ─────────────────────
        private void UpdateLoadProgress(string message, float progress)
        {
            if (MuGame.IsMainThread)
            {
                _mapController?.UpdateLoadProgress(message, progress);
                return;
            }

            MuGame.ScheduleOnMainThread(
                () => _mapController?.UpdateLoadProgress(message, progress),
                MainThreadDispatcher.WorkPriority.High,
                "GameScene.UpdateLoadProgress");
        }

        protected override async Task LoadSceneContentWithProgress(Action<string, float> progressCallback)
        {
            WorldControl worldInstance = null;
            try
            {
                UpdateLoadProgress("Initializing Game Scene...", 0.0f);

                var charState = MuGame.Network?.GetCharacterState();
                if (charState == null)
                {
                    UpdateLoadProgress("Error: CharacterState is null.", 1.0f);
                    _logger?.LogDebug("CharacterState is null in GameScene.Load, cannot proceed.");
                    _modernHud.Visible = false;
                    return;
                }

                // Phase 1: apply the small, data-only hero state.
                UpdateLoadProgress("Setting up hero info...", 0.05f);
                _hero.CharacterClass = _characterInfo.Class;
                _hero.Name = _characterInfo.Name;
                charState.UpdateCoreCharacterInfo(
                    charState.Id,
                    _characterInfo.Name,
                    _characterInfo.Class,
                    _characterInfo.Level,
                    charState.PositionX,
                    charState.PositionY,
                    charState.MapId);
                _hero.NetworkId = charState.Id;
                _hero.Location = new Vector2(charState.PositionX, charState.PositionY);
                if (_windowCloseController != null)
                {
                    _hero.PlayerMoved += _windowCloseController.OnHeroMoved;
                    _hero.PlayerTookDamage += _windowCloseController.OnHeroTookDamage;
                }

                Type initialWorldType = typeof(LorenciaWorld);
                if (MapWorldRegistry.TryGetValue((byte)charState.MapId, out Type mappedType))
                    initialWorldType = mappedType;
                else
                    _logger?.LogDebug("Unknown MapId {MapId}. Defaulting to Lorencia.", charState.MapId);

                await MuGame.YieldToNextFrameAsync(
                    $"GameScene.Load.CreateWorld.{initialWorldType.Name}",
                    MainThreadDispatcher.WorkPriority.Critical);

                // Phase 2: create a hidden world shell. Keeping it hidden prevents the renderer
                // from cold-starting terrain, culling and model buffers before loading completes.
                UpdateLoadProgress($"Creating world: {initialWorldType.Name}...", 0.20f);
                if (World != null)
                {
                    Controls.Remove(World);
                    World.Dispose();
                    World = null;
                }

                worldInstance = (WorldControl)Activator.CreateInstance(initialWorldType);
                worldInstance.Visible = false;
                Controls.Add(worldInstance);
                World = worldInstance;

                if (worldInstance is WalkableWorldControl walkable)
                {
                    walkable.Walker = _hero;
                    _scopeImportController?.EnsureWalkerNetworkId(walkable, charState.Id, "initial world shell");
                }

                _hero.World = worldInstance;

                await MuGame.YieldToNextFrameAsync(
                    $"GameScene.Load.InitializeWorld.{initialWorldType.Name}",
                    MainThreadDispatcher.WorkPriority.Critical);

                // Phase 3: initialize the hidden world. Any unavoidable cold I/O is now isolated
                // to a named transition phase and cannot be combined with hero publication.
                UpdateLoadProgress($"Loading world: {initialWorldType.Name}...", 0.30f);
                await worldInstance.Initialize();
                UpdateLoadProgress($"World {initialWorldType.Name} initialized.", 0.60f);

                await MuGame.YieldToNextFrameAsync(
                    "GameScene.Load.HeroAssets",
                    MainThreadDispatcher.WorkPriority.Critical);

                // Phase 4: load the hero before adding it to the live object collection.
                UpdateLoadProgress("Loading hero assets...", 0.65f);
                if (_hero.Status == GameControlStatus.NonInitialized ||
                    _hero.Status == GameControlStatus.Initializing)
                {
                    await _hero.Load();
                }

                // Asset loading may complete on the thread pool. Marshal back before touching
                // model buffers and split prewarm from publication into separate frames.
                await MuGame.YieldToNextFrameAsync(
                    "GameScene.Load.PrepareHero",
                    MainThreadDispatcher.WorkPriority.Critical);
                _scopeImportController?.EnsureHeroNetworkId(charState.Id, "after hero Load()");
                _hero.SnapToTerrainHeight(updateCamera: false);
                await _hero.PrepareGpuTexturesForFirstFrameAsync();
                _hero.PrepareRenderResourcesForFirstFrame();
                await CharacterSpawnEffect.PreloadAsync();

                await MuGame.YieldToNextFrameAsync(
                    "GameScene.Load.PublishHero",
                    MainThreadDispatcher.WorkPriority.Critical);

                if (!worldInstance.Objects.Contains(_hero))
                {
                    worldInstance.Objects.Add(_hero);
                    CharacterSpawnEffect.Start(_hero);
                }
                if (worldInstance is WalkableWorldControl initializedWalkable)
                    _scopeImportController?.EnsureWalkerNetworkId(initializedWalkable, charState.Id, "after hero publication");

                // Phase 5: queue each scope category in a separate frame. Remote objects load
                // asynchronously and are published only after their own assets are ready.
                UpdateLoadProgress("Importing nearby players...", 0.80f);
                await (_scopeImportController?.ImportPendingRemotePlayersAsync() ?? Task.CompletedTask);
                await MuGame.YieldToNextFrameAsync(
                    "GameScene.Load.ImportNpcsMonsters",
                    MainThreadDispatcher.WorkPriority.High);

                UpdateLoadProgress("Importing nearby NPCs and monsters...", 0.86f);
                await (_scopeImportController?.ImportPendingNpcsMonstersAsync() ?? Task.CompletedTask);
                await MuGame.YieldToNextFrameAsync(
                    "GameScene.Load.ImportDroppedItems",
                    MainThreadDispatcher.WorkPriority.High);

                UpdateLoadProgress("Importing dropped items...", 0.90f);
                await (_scopeImportController?.ImportPendingDroppedItemsAsync() ?? Task.CompletedTask);

                await MuGame.YieldToNextFrameAsync(
                    "GameScene.Load.PrepareVisibility",
                    MainThreadDispatcher.WorkPriority.High);

                // Build the first visibility snapshot while the loading screen is still active.
                // This moves the initial spatial/culling rebuild out of the first gameplay frame.
                await worldInstance.PrepareInitialRenderResourcesAsync(
                    "GameScene.Load.PrewarmModel");

                await MuGame.YieldToNextFrameAsync(
                    "GameScene.Load.PreloadSounds",
                    MainThreadDispatcher.WorkPriority.Low);
                UpdateLoadProgress("Preloading sounds...", 0.96f);
                await PreloadSoundsAsync();

                await MuGame.YieldToNextFrameAsync(
                    "GameScene.Load.Finalize",
                    MainThreadDispatcher.WorkPriority.Critical);

                if (worldInstance is WalkableWorldControl finalWalkable)
                    _scopeImportController?.EnsureWalkerNetworkId(finalWalkable, charState.Id, "final verification");

                _mapController?.UpdateLoadProgress("Preparing first frame...", 0.99f);
                _ = QueueWorldActivationAfterLoadingFrame(() =>
                {
                    _hero.SnapToTerrainHeight();
                    worldInstance.Visible = true;
                    _modernHud.Visible = true;
                    _mapController?.UpdateLoadProgress("Game ready!", 1.0f);
                    ScheduleMapNameUpdateNextFrame("GameScene.UpdateInitialMapName");
                    _ = RefreshMiniMapAsync();
                }, "GameScene.ActivateInitialWorld");

                // Complete this nested async workflow outside the dispatcher action which ran
                // GameScene.Load.Finalize. This prevents parent scene-initialization continuations
                // from being charged to (and executed inside) the same frame-budgeted action.
                await Task.Yield();
            }
            finally
            {
                // Activation owns loading-screen cleanup. On failures there is no queued
                // activation, so release the loading UI immediately.
                if (_pendingWorldActivation == null)
                {
                    _initialWorldLoadInProgress = false;
                    _mapController?.DisposeLoadingScreen();
                    if (_progressBar != null)
                        _progressBar.Visible = false;
                }
            }
        }

        public override async Task Load()
        {
            // This method is called by BaseScene.Initialize() if LoadSceneContentWithProgress is not overridden,
            // OR if the overridden method calls base.Load().
            // For GameScene, we want the progressive loading, so we'll call it from here if this Load is hit.
            // However, with the new structure, InitializeWithProgressReporting should call LoadSceneContentWithProgress directly.
            // This is a fallback / ensures old paths might still work or for clarity.
            if (Status == GameControlStatus.Initializing) // Check if we are already in the new init flow
            {
                await LoadSceneContentWithProgress(UpdateLoadProgress);
            }
            else
            {
                // Fallback to old behavior or log a warning
                _logger?.LogDebug("GameScene.Load() called outside of InitializeWithProgressReporting flow. Consider refactoring.");
                await base.Load(); // Which is empty in BaseScene, then calls derived GameScene's old Load logic
            }
        }

        private async void OnMapWarpRequested(int mapIndex, string mapDisplayName)
        {
            _logger?.LogDebug($"Player requested warp to map index: {mapIndex}");
            var mapName = mapDisplayName;
            _chatLog.AddMessage("System", $"Warping to {mapName} (ID {mapIndex})...", MessageType.System);

            try
            {
                await MuGame.Network.SendWarpRequestAsync((ushort)mapIndex);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, $"Error sending warp request for map index {mapIndex}.");
                _chatLog.AddMessage("System", $"Error warping: {ex.Message}", MessageType.Error);
            }
        }

        // ─────────────────── Map Change Logic (Remains largely the same) ───────────────────
        public async Task ChangeMap([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type worldType)
        {
            if (_mapController != null)
            {
                await _mapController.ChangeMap(worldType);
            }
        }

        public async Task ChangeMap<T>() where T : WalkableWorldControl, new()
        {
            await ChangeMap(typeof(T));
        }

        // ─────────────────── Notification Handling ───────────────────
        public void ShowNotificationMessage(ServerMessage.MessageType messageType, string message)
        {
            _notificationController?.Enqueue(messageType, message);
        }

        // ─────────────────────────── Update Loop ───────────────────────────
        /// <summary>
        /// 把搖桿方向換算成移動指令。
        ///
        /// MU 的協議是「送一條路徑」而不是「送方向」，所以這裡不改協議：
        /// 取角色前方數格、位於搖桿方向上的格子作為目標，走既有的 MoveTo。
        /// 螢幕方向要先轉成世界方向 —— 鏡頭是等角視角且可旋轉，
        /// 直接把螢幕向量當世界向量會導致走的方向和手指不一致。
        /// </summary>
        private void UpdateVirtualJoystick(GameTime gameTime)
        {
            if (_virtualJoystick == null || _hero == null)
                return;

            // 觸控落在技能按鈕上時不要同時驅動搖桿
            var uiMouse = MuGame.Instance.UiMouseState;
            if (_touchActionButtons != null
                && _touchActionButtons.ContainsPoint(new Vector2(uiMouse.X, uiMouse.Y)))
            {
                return;
            }

            if (!_virtualJoystick.ShouldIssueMove(gameTime, out var screenDirection))
                return;

            var worldDirection = ScreenDirectionToWorld(screenDirection);
            if (worldDirection == Vector2.Zero)
                return;

            var target = _hero.Location + worldDirection * VirtualJoystickControl.TileDistance;
            var tile = new Vector2(MathF.Round(target.X), MathF.Round(target.Y));

            LogBlockedMove(tile);

            // 搖桿是直接操控，不要繞路 —— 用直線路徑，撞到障礙就停住，
            // 這比自動繞遠路更符合手感。
            _hero.MoveTo(tile, sendToServer: true, usePathfinding: false);
        }

        // 卡住診斷。用 Console.WriteLine 而不是 logger：裝置的 console provider
        // 預設關掉 Debug 等級，devicectl --console 只讀得到直接輸出。

        /// <summary>
        /// 搖桿下了移動指令、但直線路徑長度是 0（第一步就被擋住）時，
        /// 把「客戶端自己認定的角色格子」「目標格」「被擋在哪一格」印出來。
        /// 伺服器只知道它最後核可的位置，看不到客戶端這一側的漂移，
        /// 所以卡住的時候要看的是這裡的 hero 值。
        /// </summary>
        private void LogBlockedMove(Vector2 tile)
        {
            var world = _hero?.World;
            if (world == null)
                return;

            var heroTile = new Vector2((int)_hero.Location.X, (int)_hero.Location.Y);
            if (Pathfinding.BuildDirectPath(heroTile, tile, world).Count > 0)
                return;

            var step = heroTile;
            if (step.X != tile.X)
                step.X += MathF.Sign(tile.X - step.X);
            if (step.Y != tile.Y)
                step.Y += MathF.Sign(tile.Y - step.Y);

            Console.WriteLine(
                $"[MoveDiag] BLOCKED hero=({heroTile.X},{heroTile.Y}) standable={world.IsWalkable(heroTile)} "
                + $"target=({tile.X},{tile.Y}) blockedAt=({step.X},{step.Y})");
        }

        /// <summary>
        /// 每秒一次的心跳。停止輸出的那一秒就是畫面停住的那一秒，
        /// 而最後一行會留下卡住當下客戶端認定的座標與移動狀態。
        /// </summary>
        private void UpdateMoveDiagnosticsHeartbeat(GameTime gameTime)
        {
            if (_hero == null)
                return;

            _moveDiagElapsedSeconds += gameTime.ElapsedGameTime.TotalSeconds;
            if (_moveDiagElapsedSeconds < 1.0)
                return;

            _moveDiagElapsedSeconds = 0;

            var world = _hero.World;
            var heroTile = new Vector2((int)_hero.Location.X, (int)_hero.Location.Y);
            string standable = world == null ? "n/a" : world.IsWalkable(heroTile).ToString();

            Console.WriteLine(
                $"[MoveDiag] tick hero=({heroTile.X},{heroTile.Y}) standable={standable} "
                + $"moving={_hero.IsMoving} intent={_hero.MovementIntent}");
        }

        /// <summary>
        /// 螢幕方向轉世界方向。鏡頭 yaw 會旋轉整個視角，
        /// 必須把螢幕向量反向旋轉回世界座標，玩家才會覺得「往哪推就往哪走」。
        /// </summary>
        private static Vector2 ScreenDirectionToWorld(Vector2 screenDirection)
        {
            if (screenDirection == Vector2.Zero)
                return Vector2.Zero;

            // 螢幕 Y 向下，世界 Y 向上
            var dir = new Vector2(screenDirection.X, -screenDirection.Y);

            float yaw = Constants.DEFAULT_CAMERA_YAW;
            float cos = MathF.Cos(-yaw);
            float sin = MathF.Sin(-yaw);

            var rotated = new Vector2(
                dir.X * cos - dir.Y * sin,
                dir.X * sin + dir.Y * cos);

            return rotated == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(rotated);
        }

        public override void Update(GameTime gameTime)
        {
            UpdateVirtualJoystick(gameTime);
            UpdateMoveDiagnosticsHeartbeat(gameTime);

            if (Status != GameControlStatus.Ready)
            {
                _mapController?.UpdateLoading(gameTime);
                return;
            }

            if (_initialWorldLoadInProgress ||
                _mapController?.IsChangingWorld == true ||
                _pendingWorldActivation != null ||
                _initialWorldActivationCooldown ||
                World == null ||
                !World.Visible ||
                World.Status != GameControlStatus.Ready)
            {
                _mapController?.UpdateLoading(gameTime);
                return;
            }

            var currentKeyboardState = MuGame.Instance.Keyboard;
            var previousKeyboardState = MuGame.Instance.PrevKeyboard;

            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            long buffsStarted = UpdatePassProfiler.Start();
            MuGame.Network?.UpdateBuffs();
            MuGame.Network?.GetCharacterState()?.ExpireActiveBuffs();
            _hotkeys?.HandleGlobal(currentKeyboardState, previousKeyboardState);
            UpdatePassProfiler.AddGameBuffs(buffsStarted);

            long notificationsStarted = UpdatePassProfiler.Start();
            _notificationManager?.Update(gameTime);
            _notificationController?.ProcessPending();
            UpdatePassProfiler.AddGameNotifications(notificationsStarted);

            long scopeStarted = UpdatePassProfiler.Start();
            if (World is WalkableWorldControl walkableWorld)
                ScopeHandler.PumpNpcSpawnQueue(walkableWorld);
            UpdatePassProfiler.AddGameScopePump(scopeStarted);

            if (World == null || World.Status != GameControlStatus.Ready)
            {
                _playerMenuController?.ResetOnWorldUnavailable();
                _skillController?.ClearPending();
                return;
            }

            long interactionStarted = UpdatePassProfiler.Start();
            var uiMouse = MuGame.Instance.UiMouseState;
            var prevUiMouse = MuGame.Instance.PrevUiMouseState;

            long playerMenuStarted = UpdatePassProfiler.Start();
            _playerMenuController?.Update(gameTime, currentKeyboardState, uiMouse, prevUiMouse);
            UpdatePassProfiler.AddGamePlayerMenu(playerMenuStarted);

            long skillUpdateStarted = UpdatePassProfiler.Start();
            _skillController?.Update();
            UpdatePassProfiler.AddGameSkillUpdate(skillUpdateStarted);

            long attackInputStarted = UpdatePassProfiler.Start();
            // Handle attack clicks on monsters with proper validation
            if (!IsMouseInputConsumedThisFrame &&
                !WorldHoverSystem.IsAltPressed() &&
                MuGame.Instance.Mouse.LeftButton == ButtonState.Pressed &&
                MuGame.Instance.PrevMouseState.LeftButton == ButtonState.Released) // Fresh press
            {
                MonsterObject hoveredAttackMonster = WorldHoverSystem.FindBestLiveMonster(
                    World.VisibleObjects,
                    MuGame.Instance.MouseRay,
                    World);

                if (hoveredAttackMonster != null &&
                    Hero != null &&
                    !Hero.IsDead && // Don't attack if player is dead
                    Vector2.Distance(Hero.Location, hoveredAttackMonster.Location) <= Hero.GetAttackRangeTiles()) // Check range
                {
                    Hero.Attack(hoveredAttackMonster);
                    SetMouseInputConsumed(); // Consume the click
                }
            }

            // Handle attack clicks on duel opponent players (treat as monster during duel)
            if (!IsMouseInputConsumedThisFrame &&
                MouseHoverObject is PlayerObject targetPlayer &&
                targetPlayer != _hero &&
                (_duelController?.IsDuelAttackTarget(targetPlayer) == true) &&
                MuGame.Instance.Mouse.LeftButton == ButtonState.Pressed &&
                MuGame.Instance.PrevMouseState.LeftButton == ButtonState.Released) // Fresh press
            {
                if (Hero != null &&
                    !Hero.IsDead &&
                    !targetPlayer.IsDead &&
                    targetPlayer.World == World)
                {
                    Hero.Attack(targetPlayer);
                    SetMouseInputConsumed();
                }
            }

            UpdatePassProfiler.AddGameAttackInput(attackInputStarted);

            // Handle skill usage with right-click. These paths are measured independently so
            // the next runtime trace can identify packet/effect cold starts precisely.
            long rightClickSkillStarted = UpdatePassProfiler.Start();
            _skillController?.HandleRightClickSkillUsage();
            UpdatePassProfiler.AddGameRightClickSkill(rightClickSkillStarted);

            long hotkeysStarted = UpdatePassProfiler.Start();
            _hotkeys?.HandleInWorld(currentKeyboardState, previousKeyboardState);
            UpdatePassProfiler.AddGameHotkeys(hotkeysStarted);
            UpdatePassProfiler.AddGameInteraction(interactionStarted);

            long housekeepingStarted = UpdatePassProfiler.Start();
            // Update ping every 5 seconds to reduce network overhead
            _pingTimer += gameTime.ElapsedGameTime.TotalSeconds;
            if (_pingTimer >= 5.0)
            {
                _pingTimer = 0;
                _ = UpdatePingAsync();
            }

            // Keep this separate from DebugPanel so it is always available.
            _fpsTimer += gameTime.ElapsedGameTime.TotalSeconds;
            if (_fpsTimer >= 0.25)
            {
                _fpsTimer = 0;
                UpdateFpsLabel();
            }
            UpdatePassProfiler.AddGameHousekeeping(housekeepingStarted);
        }

        internal void ScheduleMapNameUpdateNextFrame(string actionName)
        {
            _ = UpdateMapNameNextFrameAsync(actionName);
        }

        internal Task RefreshMiniMapAsync()
        {
            return _miniMap != null && World != null
                ? _miniMap.LoadContentForWorld(World.WorldIndex)
                : Task.CompletedTask;
        }

        private async Task UpdateMapNameNextFrameAsync(string actionName)
        {
            await MuGame.YieldToNextFrameAsync(
                string.IsNullOrWhiteSpace(actionName) ? "GameScene.UpdateMapName" : actionName,
                MainThreadDispatcher.WorkPriority.High);
            _mapController?.UpdateMapName();
        }

        internal Task QueueWorldActivationAfterLoadingFrame(
            Action activation,
            string actionName,
            bool cleanupLoadingUi = true)
        {
            ArgumentNullException.ThrowIfNull(activation);
            if (_pendingWorldActivation != null)
                throw new InvalidOperationException("A world activation is already pending.");

            _pendingWorldActivation = activation;
            _pendingWorldActivationScheduled = false;
            _pendingWorldActivationCleansLoadingUi = cleanupLoadingUi;
            _pendingWorldActivationName = string.IsNullOrWhiteSpace(actionName)
                ? "GameScene.ActivateWorldAfterLoadingFrame"
                : actionName;
            _pendingWorldActivationCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return _pendingWorldActivationCompletion.Task;
        }

        private void SchedulePendingWorldActivation()
        {
            if (_pendingWorldActivation == null || _pendingWorldActivationScheduled)
                return;

            _pendingWorldActivationScheduled = true;
            MuGame.ScheduleOnMainThread(() =>
            {
                Action activation = _pendingWorldActivation;
                TaskCompletionSource<bool> completion = _pendingWorldActivationCompletion;
                try
                {
                    activation?.Invoke();
                    if (_pendingWorldActivationCleansLoadingUi)
                        _initialWorldActivationCooldown = true;
                    completion?.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion?.TrySetException(ex);
                    throw;
                }
                finally
                {
                    _pendingWorldActivation = null;
                    _pendingWorldActivationCompletion = null;
                    _pendingWorldActivationScheduled = false;
                    _pendingWorldActivationCleansLoadingUi = false;
                    _pendingWorldActivationName = null;
                }
            }, MainThreadDispatcher.WorkPriority.Critical, _pendingWorldActivationName);
        }

        // ─────────────────────────── Draw Loop ───────────────────────────
        public override void Draw(GameTime gameTime)
        {
            if (!_sceneShellInitialized)
            {
                GraphicsDevice.Clear(new Color(12, 12, 20));
                DrawBackground();

                var initialLoading = _initialLoadingScreen ?? _mapController?.LoadingScreen;
                if (_progressBar != null)
                {
                    _progressBar.Progress = initialLoading?.Progress ?? _progressBar.Progress;
                    _progressBar.StatusText = initialLoading?.Message ?? "Preparing game interface...";
                    _progressBar.Visible = true;
                    _progressBar.Draw(gameTime);
                }
                return;
            }

            if (IsShowingLoadingScreen)
            {
                GraphicsDevice.Clear(new Color(12, 12, 20));
                DrawBackground();
                var loading = _mapController?.LoadingScreen;
                _progressBar.Progress = loading?.Progress ?? 0f;
                _progressBar.StatusText = loading?.Message ?? "Loading...";
                _progressBar.Visible = true;
                _progressBar.Draw(gameTime);
                SchedulePendingWorldActivation();
                if (_initialWorldActivationCooldown && _pendingWorldActivation == null)
                {
                    _initialWorldActivationCooldown = false;
                    _initialWorldLoadInProgress = false;
                    _mapController?.DisposeLoadingScreen();
                    _progressBar.Visible = false;
                }
                return;
            }

            // 這裡原本還有一趟「先畫一次所有 UI 控制項」。那一趟完全是浪費：
            // 緊接著的 base.Draw 會先畫 3D 世界（帶深度寫入）把它整片蓋掉，
            // 然後 Pass 3 又把同一批控制項重畫一次。等於每幀多畫一整個 HUD
            // 卻看不到。移除後畫面完全相同。

            base.Draw(gameTime);
        }

        /// <summary>
        /// 這一幀是不是還停在載入畫面上。
        /// </summary>
        private bool IsShowingLoadingScreen
            => !_sceneShellInitialized
            || _initialWorldLoadInProgress
            || _mapController?.IsChangingWorld == true
            || _pendingWorldActivation != null
            || _initialWorldActivationCooldown
            || World == null
            || !World.Visible
            || World.Status != GameControlStatus.Ready;

        public override void DrawUi(GameTime gameTime)
        {
            // 載入畫面期間不要畫 UI。
            //
            // Draw() 在載入時會提早 return，但 DrawUi 是引擎另外呼叫的一趟 ——
            // 它沒有被擋住，於是 HUD、按鈕、聊天視窗全部畫在載入畫面上面。
            // 使用者看到的是「還在讀條，介面就已經出現了」。
            if (IsShowingLoadingScreen)
                return;

            base.DrawUi(gameTime);

            // Final top-most pass: draw dragged item previews above all UI windows
            using (new SpriteBatchScope(
                       GraphicsManager.Instance.Sprite,
                       SpriteSortMode.Deferred,
                       BlendState.AlphaBlend,
                       SamplerState.LinearClamp,
                       DepthStencilState.None,
                       transform: UiScaler.SpriteTransform))
            {
                var sprite = GraphicsManager.Instance.Sprite;
                _inventoryControl?._pickedItemRenderer?.Draw(sprite, gameTime);
                VaultControl.Instance?.DrawPickedPreview(sprite, gameTime);
                ChaosMixControl.Instance?.DrawPickedPreview(sprite, gameTime);
                TradeControl.Instance?.DrawPickedPreview(sprite, gameTime);
                DrawPerformanceOverlay(gameTime);
            }
        }

        private void DrawPerformanceOverlay(GameTime gameTime)
        {
            _fpsLabel?.Draw(gameTime);
            _pingLabel?.Draw(gameTime);
        }

        private new void DrawBackground()
        {
            if (_backgroundTexture == null) return;

            using var scope = new SpriteBatchScope(
                GraphicsManager.Instance.Sprite, SpriteSortMode.Deferred,
                BlendState.AlphaBlend, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone,
                null, UiScaler.SpriteTransform);

            GraphicsManager.Instance.Sprite.Draw(_backgroundTexture,
                new Rectangle(0, 0, UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y), Color.White);
        }


        private Task PreloadSoundsAsync()
        {
            // Move reflection-based skill effect discovery out of the first combat packet.
            SkillVisualEffectRegistry.Initialize();

            return Task.WhenAll(
                SoundController.Instance.PreloadSoundAsync("Sound/pDropItem.wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/pDropMoney.wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/eGem.wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/Jewel_Sound.wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/pGetItem.wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/pWalk(Grass).wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/pWalk(Snow).wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/pWalk(Soil).wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/pSwim.wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/mHomord1.wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/mHomordAttack1.wav"),
                SoundController.Instance.PreloadSoundAsync("Sound/mHomordDie.wav"));
        }

        private async Task UpdatePingAsync()
        {
            if (MuGame.Network == null)
                return;

            // System.Net Ping may perform an expensive synchronous first-use setup. Keep that
            // work away from the game thread and only publish the final value through the
            // dispatcher.
            int? ping = await Task.Run(
                async () => await MuGame.Network.PingServerAsync().ConfigureAwait(false))
                .ConfigureAwait(false);
            MuGame.ScheduleOnMainThread(() =>
            {
                if (_pingLabel == null)
                    return;

                if (ping == _lastPingValue)
                    return;

                _lastPingValue = ping;
                _pingLabel.Text = ping.HasValue ? $"Ping: {ping.Value} ms" : "Ping: --";
            });
        }

        private void UpdateFpsLabel()
        {
            if (_fpsLabel == null)
                return;

            int fps = (int)FPSCounter.Instance.FPS_AVG;
            if (fps == _lastFpsValue)
                return;

            _lastFpsValue = fps;
            _fpsLabel.Text = $"FPS: {fps}";
        }

        private void StartWhisperToPlayer(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName) || _chatInput == null)
            {
                return;
            }

            _chatInput.StartWhisperTo(playerName);
        }

        internal void SetWorldInternal(WorldControl world)
        {
            World = world;
        }

        internal void NotifyLocalSkillAnimation(ushort skillId)
        {
            _skillController?.NotifyLocalSkillAnimation(skillId);

            // 連擊的進度以「伺服器回來的動畫」為準，不是以「送出封包」為準 ——
            // 送出去的技能可能被伺服器丟掉，那樣段數就會跟伺服器對不上。
            double now = MuGame.Instance?.GameTime?.TotalGameTime.TotalSeconds ?? 0;
            if (skillId == Core.Client.SkillComboTracker.ComboAchievedSkillId)
            {
                ComboTracker.NotifyComboAchieved(now);
            }
            else
            {
                var characterClass = MuGame.Network?.GetCharacterState()?.Class
                    ?? MUnique.OpenMU.Network.Packets.CharacterClassNumber.DarkWizard;
                ComboTracker.RegisterConfirmedSkill(skillId, now, characterClass);
            }
        }

        public override void Dispose()
        {
            _pendingWorldActivation = null;
            _pendingWorldActivationScheduled = false;
            _initialWorldActivationCooldown = false;
            _pendingWorldActivationCompletion?.TrySetCanceled();
            _pendingWorldActivationCompletion = null;

            if (_hero != null)
            {
                if (_windowCloseController != null)
                {
                    _hero.PlayerMoved -= _windowCloseController.OnHeroMoved;
                    _hero.PlayerTookDamage -= _windowCloseController.OnHeroTookDamage;
                }
            }
            base.Dispose();
        }
    }
}
