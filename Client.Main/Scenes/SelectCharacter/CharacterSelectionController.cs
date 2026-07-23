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
        // === Private state ===
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
                    Position = displayPosition,
                    Angle = displayAngle,
                    Interactive = false,
                    World = world,
                    CurrentAction = PlayerAction.PlayerStopMale,
                    Hidden = true,
                };

                player.Click += OnPlayerClick;

                try
                {
                    // Load before publication so the world initialization queue cannot race
                    // this scene-owned character load.
                    await player.Load();
                    cancellationToken.ThrowIfCancellationRequested();
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

            for (int i = 0; i < characters.Count; i++)
            {
                var (name, _, lvl, appearanceConfig) = characters[i];
                var player = new PlayerObject(new AppearanceData())
                {
                    Name = name,
                    CharacterClass = CharacterClassNumber.DarkWizard,
                    Position = displayPosition,
                    Angle = displayAngle,
                    Interactive = false,
                    World = world,
                    CurrentAction = PlayerAction.PlayerStopMale,
                    Hidden = true
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
                Text = $"Lv.{level}  {name}",
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
                bool isActive = i == index;

                player.Hidden = !isActive;
                player.Interactive = isActive;

                if (_labels.TryGetValue(player, out var label))
                    label.Visible = isActive;
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

            if (_activeIndex < 0 || _characters[_activeIndex] != clickedPlayer)
            {
                _logger.LogDebug("Ignoring click on inactive character '{Name}'.", clickedPlayer.Name);
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

            _characterInfos.Clear();
            _activeIndex = -1;
        }
    }
}
