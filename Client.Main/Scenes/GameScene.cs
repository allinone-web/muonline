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

        // 手機用的虛擬搖桿，取代點擊移動（見 UpdateVirtualJoystick）
        private VirtualJoystickControl _virtualJoystick;
        private TouchActionButtonsControl _touchActionButtons;

        /// <summary>搖桿是否啟用 —— 只有觸控平台需要。</summary>
        public static bool UseVirtualJoystick => OperatingSystem.IsIOS() || OperatingSystem.IsAndroid();
        public ChatLogWindow ChatLog => _chatLog;
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
                _virtualJoystick = new VirtualJoystickControl();
                Controls.Add(_virtualJoystick);

                _touchActionButtons = new TouchActionButtonsControl(
                    () => _modernHud?.AssignedSkills ?? System.Array.Empty<Core.Client.SkillEntryState>());
                Controls.Add(_touchActionButtons);
            }

            _mapListControl = new MapListControl { Visible = false };
            _chatLog = new ChatLogWindow
            {
                X = 5,
                Y = UiScaler.VirtualSize.Y - 160 - ChatInputBoxControl.CHATBOX_HEIGHT
            };
            Controls.Add(_chatLog);

            _chatInput = new ChatInputBoxControl(_chatLog, MuGame.AppLoggerFactory)
            {
                X = 5,
                Y = UiScaler.VirtualSize.Y - 65 - ChatInputBoxControl.CHATBOX_HEIGHT
            };
            Controls.Add(_chatInput);
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

            _characterInfoWindow = new CharacterInfoWindowControl { X = 20, Y = 50, Visible = false };
            Controls.Add(_characterInfoWindow);
            _miniMap = new MiniMapControl(this);
            Controls.Add(_miniMap);
            _partyPanel = new PartyPanelControl();
            Controls.Add(_partyPanel);

            _fpsLabel = new LabelControl
            {
                Text = "FPS: --",
                Align = ControlAlign.Top | ControlAlign.Right,
                Margin = new Margin { Top = 5, Right = 5 },
                FontSize = 10,
                TextColor = Color.LightGreen
            };
            Controls.Add(_fpsLabel);

            _pingLabel = new LabelControl
            {
                Text = "Ping: --",
                Align = ControlAlign.Top | ControlAlign.Right,
                Margin = new Margin { Top = 22, Right = 5 },
                FontSize = 10,
                TextColor = Color.White
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

            // 搖桿是直接操控，不要繞路 —— 用直線路徑，撞到障礙就停住，
            // 這比自動繞遠路更符合手感。
            _hero.MoveTo(tile, sendToServer: true, usePathfinding: false);
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

            if (_initialWorldLoadInProgress || _mapController?.IsChangingWorld == true || _pendingWorldActivation != null || _initialWorldActivationCooldown || World == null || !World.Visible || World.Status != GameControlStatus.Ready)
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

            using (new SpriteBatchScope(
                       GraphicsManager.Instance.Sprite,
                       SpriteSortMode.Deferred,
                       BlendState.AlphaBlend,
                       SamplerState.LinearClamp,
                       DepthStencilState.None,
                       transform: UiScaler.SpriteTransform))
            {
                var controls = Controls.GetSnapshotArray();
                for (int i = 0; i < controls.Length; i++)
                {
                    var ctrl = controls[i];
                    if (ctrl == null || ctrl == World || ctrl == _fpsLabel || ctrl == _pingLabel || !ctrl.Visible)
                    {
                        continue;
                    }

                    ctrl.Draw(gameTime);
                }

            }

            base.Draw(gameTime);

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
