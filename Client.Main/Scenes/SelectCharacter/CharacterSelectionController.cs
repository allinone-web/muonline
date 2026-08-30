using Client.Main.Controls;
using Client.Main.Controls.UI;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Objects.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using MUnique.OpenMU.Network.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Main.Scenes.SelectCharacter
{
    public class CharacterSelectionController : IDisposable
    {

        /// <summary>
        /// 選角展示用角色的模型倍率。手機上 6.5 吋螢幕看角色偏小，
        /// 放大模型比拉近鏡頭好 —— 鏡頭拉近會把背景一起放大，而背景不需要更大。
        /// 只影響這個畫面的展示角色。
        /// </summary>
        private static float SelectionCharacterScale =>
            MobileUi.IsMobile
                ? (MuGame.AppSettings?.Graphics?.Mobile?.SelectCharacterScale ?? 1f)
                : 1f;

        // === Private state ===
        /// <summary>舞台中心。SetActiveCharacter 要用它重算每個角色的站位。</summary>
        private Vector3 _displayPosition;

        /// <summary>每個角色沒被選中時該站的位置。選中時往鏡頭方向偏移。</summary>
        private readonly Dictionary<PlayerObject, Vector3> _slotBase = new();

        /// <summary>每秒趨近目標位置的比例。走過去比瞬移好看。</summary>
        private const float SelectionMoveSpeed = 7f;

        private readonly List<PlayerObject> _characters = new();
        private readonly List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)> _characterInfos = new();
        private readonly Dictionary<PlayerObject, LabelControl> _labels = new();
        private readonly ILogger<CharacterSelectionController> _logger;
        private int _activeIndex = -1;

        // Double-click detection
        private DateTime _lastClickTime = DateTime.MinValue;
        private string _lastClickedCharacter;
        private const double DoubleClickThresholdMs = 500;

        // Random emote
        private readonly Random _random = new();

        // === Public data (read-only) ===
        public IReadOnlyList<PlayerObject> Characters => _characters;
        public IReadOnlyDictionary<PlayerObject, LabelControl> Labels => _labels;

        // === State ===
        public int ActiveIndex => _activeIndex;
        public PlayerObject ActiveCharacter =>
            _activeIndex >= 0 && _activeIndex < _characters.Count
                ? _characters[_activeIndex]
                : null;

        // === Events ===
        public event EventHandler<string> CharacterClicked;
        public event EventHandler<string> CharacterDoubleClicked;

        // === Constructor ===
        public CharacterSelectionController(ILogger<CharacterSelectionController> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // === Character Creation ===
        public async Task CreateCharactersAsync(
            List<(string Name, CharacterClassNumber Class, ushort Level, byte[] Appearance)> characterInfos,
            WorldControl world,
            GameControl scene,
            Vector3 displayPosition,
            Vector3 displayAngle,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(characterInfos);
            _logger.LogInformation("Creating {Count} character objects...", characterInfos.Count);

            await DisposeCharactersAsync(world, scene, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            _characterInfos.Clear();
            _characterInfos.AddRange(characterInfos);
            _activeIndex = -1;
            _displayPosition = displayPosition;

            if (characterInfos.Count == 0)
            {
                _logger.LogInformation("No characters provided for selection.");
                return;
            }

            for (int i = 0; i < characterInfos.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (name, cls, lvl, appearanceBytes) = characterInfos[i];
                var player = new PlayerObject(new AppearanceData(appearanceBytes))
                {
                    Name = name,
                    CharacterClass = cls,
                    Position = displayPosition + Worlds.SelectWorld.SlotOffset(i, characterInfos.Count),
                    Angle = displayAngle,
                    Interactive = false,
                    World = world,
                    CurrentAction = PlayerAction.PlayerStopMale,
                    Hidden = true,
                    Scale = SelectionCharacterScale,
                };

                player.Click += OnPlayerClick;

                try
                {
                    // Load before publication so the world initialization queue cannot race
                    // this scene-owned character load.
                    await player.Load();
                    cancellationToken.ThrowIfCancellationRequested();
                    SwapPreviewWeaponHands(player);
                    if (player.Status != GameControlStatus.Ready)
                    {
                        throw new InvalidOperationException(
                            $"Selection character '{name}' failed to load (status: {player.Status}).");
                    }

                    var label = CreateCharacterLabel(lvl, name);
                    _characters.Add(player);
                    _labels.Add(player, label);
                    world.Objects.Add(player);
                    scene.Controls.Add(label);
                    label.BringToFront();
                }
                catch
                {
                    player.Click -= OnPlayerClick;
                    player.Dispose();
                    throw;
                }
            }

            if (_characters.Count > 0)
                SetActiveCharacter(0);

            await MuGame.YieldToNextFrameAsync(
                "CharacterSelection.PrepareVisibility",
                MainThreadDispatcher.WorkPriority.High);
            world.PrepareInitialVisibilitySnapshot();

            _logger.LogInformation("Finished creating and loading character objects and labels.");
        }

        // 換手的邏輯搬進 PlayerObject —— 那裡才知道哪些裝備不該進手裡（例如箭袋）。
        private static void SwapPreviewWeaponHands(PlayerObject player) => player.SwapPreviewWeaponHands();

        // Overload for TestAnimationScene compatibility (uses PlayerClass and AppearanceConfig)
        public async Task CreateCharactersAsync(
            List<(string Name, PlayerClass Class, ushort Level, AppearanceConfig Appearance)> characters,
            WorldControl world,
            GameControl scene,
            Vector3 displayPosition,
            Vector3 displayAngle)
        {
            _logger.LogInformation("Creating {Count} character objects (AppearanceConfig version)...", characters.Count);

            DisposeCharacters(world, scene);

            _characterInfos.Clear();
            var converted = characters.Select(p => (p.Name, (CharacterClassNumber)p.Class, p.Level, Array.Empty<byte>()));
            _characterInfos.AddRange(converted);
            _activeIndex = -1;
            _displayPosition = displayPosition;

            for (int i = 0; i < characters.Count; i++)
            {
                var (name, _, lvl, appearanceConfig) = characters[i];
                var player = new PlayerObject(new AppearanceData())
                {
                    Name = name,
                    CharacterClass = CharacterClassNumber.DarkWizard,
                    Position = displayPosition + Worlds.SelectWorld.SlotOffset(i, characters.Count),
                    Angle = displayAngle,
                    Interactive = false,
                    World = world,
                    CurrentAction = PlayerAction.PlayerStopMale,
                    Hidden = true,
                    Scale = SelectionCharacterScale,
                };

                player.Click += OnPlayerClick;
                try
                {
                    await player.Load(appearanceConfig.PlayerClass);
                    await player.UpdateEquipmentAppearanceFromConfig(appearanceConfig);
                    if (player.Status != GameControlStatus.Ready)
                    {
                        throw new InvalidOperationException(
                            $"Selection character '{name}' failed to load (status: {player.Status}).");
                    }

                    var label = CreateCharacterLabel(lvl, name);
                    _characters.Add(player);
                    _labels.Add(player, label);
                    world.Objects.Add(player);
                    scene.Controls.Add(label);
                    label.BringToFront();
                }
                catch
                {
                    player.Click -= OnPlayerClick;
                    player.Dispose();
                    throw;
                }
            }

            if (_characters.Count > 0)
                SetActiveCharacter(0);

            world.PrepareInitialVisibilitySnapshot();
            _logger.LogInformation("Finished creating and loading character objects and labels.");
        }

        private static LabelControl CreateCharacterLabel(ushort level, string name)
        {
            return new LabelControl
            {
                // 兩行：等級一行、名字一行。並排展示時單行會太寬，
                // 相鄰角色的標籤容易左右相碰。
                Text = $"Lv.{level}\n{name}",
                FontSize = 14,
                TextColor = Color.White,
                HasShadow = true,
                ShadowColor = Color.Black * 0.8f,
                ShadowOffset = new Vector2(1, 1),
                UseManualPosition = true,
                Visible = false
            };
        }

        // === Active Character Management ===
        public void SetActiveCharacter(int index)
        {
            if (_characters.Count == 0)
            {
                _activeIndex = -1;
                return;
            }

            if (index < 0 || index >= _characters.Count)
            {
                _logger.LogWarning("Attempted to activate character at invalid index {Index}", index);
                return;
            }

            for (int i = 0; i < _characters.Count; i++)
            {
                var player = _characters[i];

                // 並排展示：全部都看得見、全部都點得到。
                // 原本是 MU 原版「一次只顯示一個」的做法，換成舞台之後不適用 ——
                // 選中與否改用動作與特效表達，不是用隱藏其他人。
                player.Hidden = false;
                player.Interactive = true;

                if (_labels.TryGetValue(player, out var label))
                    label.Visible = true;

                _slotBase[player] = _displayPosition + Worlds.SelectWorld.SlotOffset(i, _characters.Count);

                if (player.Status != GameControlStatus.Ready)
                    continue;

                if (i == index)
                {
                    // PlayAction 設的是循環動作，會被每幀的待機邏輯蓋回去 ——
                    // 招牌動作要走 PlayEmoteAnimation（既有表情功能用的同一條路徑）。
                    player.PlayEmoteAnimation(SignatureAction(player.CharacterClass));
                }
                else
                {
                    player.PlayAction(player.GetCorrectIdleAction());
                }
            }

            _activeIndex = index;

            var activePlayer = _characters[index];
            activePlayer.PlayAction(activePlayer.GetCorrectIdleAction());

            if (activePlayer.World != null)
            {
                activePlayer.World.ActivateObjectForRendering(
                    activePlayer,
                    forceFullVisibilityRebuild: !activePlayer.World.IsObjectVisibleInSnapshot(activePlayer));
            }
        }

        /// <summary>
        /// 選中時播的招牌動作，每個職業不一樣。
        ///
        /// 全部用現成的動作編號，不需要任何新素材、也不需要新的 shader
        /// （iOS 的 .fx 在 macOS 編不動，要送 CI）。
        /// 職業編號是每四個一組：0-3 法師、4-7 騎士、8-11 精靈、
        /// 12-13 魔劍士、16- 魔劍公爵，之後是召喚師與格鬥家。
        /// </summary>
        private static PlayerAction SignatureAction(CharacterClassNumber characterClass) =>
            ((int)characterClass / 4) switch
            {
                0 => PlayerAction.PlayerSkillHand1,     // 法師：雙手施法
                1 => PlayerAction.PlayerSkillWeapon1,   // 騎士：武器技
                2 => PlayerAction.PlayerSkillElf1,      // 精靈：精靈專屬
                3 => PlayerAction.PlayerSkillWeapon2,   // 魔劍士
                4 => PlayerAction.PlayerSkillHand2,     // 魔劍公爵
                5 => PlayerAction.PlayerSkillFlash,     // 召喚師
                _ => PlayerAction.PlayerAttackFist,     // 格鬥家
            };

        /// <summary>
        /// 每幀推進選中的視覺表現：選中的角色走向鏡頭、其餘退回自己的站位，
        /// 並讓每個角色各自面向鏡頭。
        ///
        /// 位置原本是在 SetActiveCharacter 裡直接指派的，看起來是瞬移；
        /// 改成逐幀趨近才像「往前踏一步」。
        /// </summary>
        public void UpdateSelectionMotion(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
                return;

            float t = MathF.Min(1f, deltaSeconds * SelectionMoveSpeed);

            for (int i = 0; i < _characters.Count; i++)
            {
                var player = _characters[i];
                if (player.Status != GameControlStatus.Ready)
                    continue;

                if (!_slotBase.TryGetValue(player, out var slot))
                    continue;

                var target = i == _activeIndex
                    ? slot + Worlds.SelectWorld.SelectedStepOffset
                    : slot;

                player.Position = Vector3.DistanceSquared(player.Position, target) < 1f
                    ? target
                    : Vector3.Lerp(player.Position, target, t);

                var angle = player.Angle;
                player.Angle = new Vector3(
                    angle.X, angle.Y, Worlds.SelectWorld.FacingAngleFor(player.Position));
            }
        }

        internal void EnsureActiveCharacterVisible(WorldControl world)
        {
            var activePlayer = ActiveCharacter;
            if (activePlayer == null || world == null || !ReferenceEquals(activePlayer.World, world))
                return;

            if (activePlayer.Status != GameControlStatus.Ready)
            {
                _logger.LogWarning(
                    "Active selection character {CharacterName} is not ready ({Status}).",
                    activePlayer.Name,
                    activePlayer.Status);
                return;
            }

            bool needsRepair = activePlayer.Hidden || !world.IsObjectVisibleInSnapshot(activePlayer);
            if (activePlayer.Hidden)
                activePlayer.Hidden = false;

            world.ClearObjectRenderFault(activePlayer);
            if (needsRepair)
            {
                world.ActivateObjectForRendering(
                    activePlayer,
                    forceFullVisibilityRebuild: true);
            }

            if (_labels.TryGetValue(activePlayer, out var label))
                label.Visible = true;
        }

        // === Emote Animations ===
        private void PlayRandomEmote(PlayerObject player)
        {
            if (player == null || player.Hidden)
                return;

            if (player.IsOneShotPlaying)
                return;

            bool isFemale = PlayerActionMapper.IsCharacterFemale(player.CharacterClass);
            var availableEmotes = isFemale
                ? new[] { PlayerAction.PlayerSeeFemale1, PlayerAction.PlayerWinFemale1, PlayerAction.PlayerSmileFemale1 }
                : new[] { PlayerAction.PlayerSee1, PlayerAction.PlayerWin1, PlayerAction.PlayerSmile1 };

            var randomEmote = availableEmotes[_random.Next(availableEmotes.Length)];

            _logger.LogDebug("Playing random emote {Emote} for character {CharacterName} (Female: {IsFemale})",
                randomEmote, player.Name, isFemale);

            player.PlayEmoteAnimation(randomEmote);
        }

        public void PlayEmoteAnimation(PlayerAction action)
        {
            var activePlayer = ActiveCharacter;
            if (activePlayer == null || activePlayer.Hidden || activePlayer.IsOneShotPlaying)
                return;

            activePlayer.PlayEmoteAnimation(action);
        }

        // === Click Handling ===
        private void OnPlayerClick(object sender, EventArgs e)
        {
            PlayerObject clickedPlayer = null;

            if (sender is PlayerObject player)
            {
                clickedPlayer = player;
            }
            else if (sender is ModelObject bodyPart && bodyPart.Parent is PlayerObject parentPlayer)
            {
                clickedPlayer = parentPlayer;
            }

            if (clickedPlayer == null)
                return;

            int clickedIndex = _characters.IndexOf(clickedPlayer);
            if (clickedIndex < 0)
                return;

            // 點到「不是目前選中的」角色，就是要選它。
            //
            // 這裡原本直接忽略非選中的角色，因為舊版一次只顯示一個 ——
            // 能被點到的必然就是目前那個，點擊只用來觸發表情與雙擊進入。
            // 五個角色並排、全部可點之後，這個假設不成立了：點別人會被丟掉，
            // 選擇永遠停在第 0 個，於是不管點誰，進遊戲的都是同一隻。
            if (clickedIndex != _activeIndex)
            {
                SetActiveCharacter(clickedIndex);

                // 切換選擇不算雙擊的前半 —— 否則緊接著的第二下會被判成
                // 「雙擊進入遊戲」，等於點兩下不同角色就直接進去了。
                _lastClickTime = DateTime.MinValue;
                _lastClickedCharacter = clickedPlayer.Name;

                _logger.LogInformation("Character '{Name}' selected by click.", clickedPlayer.Name);
                CharacterClicked?.Invoke(this, clickedPlayer.Name);
                return;
            }

            // Check for double-click
            DateTime now = DateTime.UtcNow;
            double timeSinceLastClick = (now - _lastClickTime).TotalMilliseconds;
            bool isDoubleClick = timeSinceLastClick < DoubleClickThresholdMs &&
                                _lastClickedCharacter == clickedPlayer.Name;

            _lastClickTime = now;
            _lastClickedCharacter = clickedPlayer.Name;

            if (isDoubleClick)
            {
                _logger.LogInformation("Character '{Name}' double-clicked - joining game.", clickedPlayer.Name);
                CharacterDoubleClicked?.Invoke(this, clickedPlayer.Name);
            }
            else
            {
                _logger.LogInformation("Character '{Name}' clicked.", clickedPlayer.Name);
                CharacterClicked?.Invoke(this, clickedPlayer.Name);
            }
        }

        // === Cleanup ===
        private async Task DisposeCharactersAsync(
            WorldControl world,
            GameControl scene,
            CancellationToken cancellationToken)
        {
            int processed = 0;
            while (_characters.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int last = _characters.Count - 1;
                var player = _characters[last];
                _characters.RemoveAt(last);
                player.Click -= OnPlayerClick;

                if (_labels.Remove(player, out var label))
                {
                    scene?.Controls.Remove(label);
                    label.Dispose();
                }

                world?.Objects.Remove(player);
                player.Dispose();
                processed++;

                if (_characters.Count > 0)
                {
                    await MuGame.YieldToNextFrameAsync(
                        $"CharacterSelection.DisposeSlot.{processed}",
                        MainThreadDispatcher.WorkPriority.High);
                }
            }

            _characterInfos.Clear();
            _activeIndex = -1;
        }

        private void DisposeCharacters(WorldControl world, GameControl scene)
        {
            foreach (var player in _characters)
            {
                player.Click -= OnPlayerClick;
                world?.Objects.Remove(player);
                player.Dispose();
            }
            _characters.Clear();

            foreach (var label in _labels.Values)
            {
                scene?.Controls.Remove(label);
                label.Dispose();
            }
            _labels.Clear();
            _slotBase.Clear();
            _characterInfos.Clear();
            _activeIndex = -1;
        }

        public void Dispose()
        {
            foreach (var player in _characters)
            {
                player.Click -= OnPlayerClick;
                player.Dispose();
            }
            _characters.Clear();

            foreach (var label in _labels.Values)
            {
                label.Dispose();
            }
            _labels.Clear();
            _slotBase.Clear();

            _characterInfos.Clear();
            _activeIndex = -1;
        }
    }
}
