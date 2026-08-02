using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Core.Models;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Client.Main.Networking.PacketHandling.Handlers;
using Client.Main.Worlds;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework;
using Client.Main.Controls;
using TaskScheduler = Client.Main.Controllers.TaskScheduler;

namespace Client.Main.Scenes
{
    internal sealed class GameSceneScopeImportController
    {
        private readonly GameScene _scene;
        private readonly ILogger _logger;
        private readonly HashSet<ushort> _activePlayerIds = new();
        private readonly HashSet<ushort> _activeMonsterIds = new();
        private readonly HashSet<ushort> _activeNpcIds = new();
        private readonly HashSet<ushort> _activeItemIds = new();

        public GameSceneScopeImportController(GameScene scene, ILogger logger)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _logger = logger ?? NullLogger<GameSceneScopeImportController>.Instance;
        }

        public async Task ImportPendingNpcsMonstersAsync()
        {
            if (_scene.World is not WalkableWorldControl world)
                return;

            var list = ScopeHandler.TakePendingNpcsMonsters();
            if (list.Count > 0)
                ScopeHandler.EnqueuePendingNpcsMonsters(list, world);

            long started = Stopwatch.GetTimestamp();
            int slice = 0;
            while (ScopeHandler.HasPendingNpcSpawnWork)
            {
                ScopeHandler.PumpNpcSpawnQueue(world, maxPerFrame: 8);
                slice++;

                if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(10))
                {
                    _logger.LogWarning(
                        "Timed out waiting for NPC/monster scope imports. Pending work: {PendingCount}.",
                        ScopeHandler.PendingNpcSpawnWorkCount);
                    break;
                }

                await MuGame.YieldToNextFrameAsync(
                    $"ScopeImport.NpcsMonsters.{slice}",
                    MainThreadDispatcher.WorkPriority.High);
            }
        }

        public async Task ImportPendingRemotePlayersAsync()
        {
            if (_scene.World is not WalkableWorldControl world)
                return;

            var list = ScopeHandler.TakePendingPlayers();
            if (list.Count == 0)
                return;

            var loadTasks = new List<Task>(list.Count);
            ushort heroId = MuGame.Network.GetCharacterState().Id;
            foreach (var scopeObject in list)
            {
                if (scopeObject.Id == heroId || _activePlayerIds.Contains(scopeObject.Id))
                    continue;

                var remote = new PlayerObject(new AppearanceData(scopeObject.AppearanceData))
                {
                    NetworkId = scopeObject.Id,
                    Name = scopeObject.Name,
                    CharacterClass = scopeObject.Class,
                    Location = new Vector2(scopeObject.PositionX, scopeObject.PositionY),
                    World = world,
                    Hidden = true
                };

                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                loadTasks.Add(completion.Task);

                bool queued = MuGame.TaskScheduler.QueueTask(async () =>
                {
                    try
                    {
                        await remote.Load().ConfigureAwait(false);
                        await remote.PrepareGpuTexturesForFirstFrameAsync().ConfigureAwait(false);

                        if (remote.Status != GameControlStatus.Ready)
                        {
                            throw new InvalidOperationException(
                                $"Remote player {scopeObject.Id:X4} finished Load() with status {remote.Status}.");
                        }

                        var publishCompletion = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);

                        MuGame.ScheduleOnMainThread(() =>
                        {
                            try
                            {
                                if (!ReferenceEquals(_scene.World, world) ||
                                    world.Status != GameControlStatus.Ready ||
                                    _activePlayerIds.Contains(scopeObject.Id))
                                {
                                    remote.Dispose();
                                    publishCompletion.TrySetResult(false);
                                    return;
                                }

                                remote.SnapToTerrainHeight(updateCamera: false);
                                remote.PrepareRenderResourcesForFirstFrame();
                                world.Objects.Add(remote);
                                publishCompletion.TrySetResult(true);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(
                                    ex,
                                    "Error publishing pending remote player {PlayerName} ({PlayerId:X4}).",
                                    scopeObject.Name,
                                    scopeObject.Id);
                                remote.Dispose();
                                publishCompletion.TrySetResult(false);
                            }
                        }, MainThreadDispatcher.WorkPriority.High, $"ScopeImport.PublishPlayer.{scopeObject.Id:X4}");

                        bool published = await publishCompletion.Task.ConfigureAwait(false);
                        if (!published)
                        {
                            completion.TrySetResult(false);
                            return;
                        }

                        await MuGame.YieldToNextFrameAsync(
                            $"ScopeImport.ActivatePlayer.{scopeObject.Id:X4}",
                            MainThreadDispatcher.WorkPriority.High);

                        if (ReferenceEquals(_scene.World, world) &&
                            world.Status == GameControlStatus.Ready &&
                            world.FindWalkerById(scopeObject.Id) == remote)
                        {
                            bool activated = world.ActivateObjectForRendering(
                                remote,
                                forceFullVisibilityRebuild: true);
                            if (activated)
                            {
                                _activePlayerIds.Add(scopeObject.Id);
                                if ((scopeObject.RawId & 0x8000) != 0)
                                    CharacterSpawnEffect.Start(remote);
                                ElfBuffEffectManager.Instance?.EnsureBuffsForPlayer(scopeObject.Id);
                            }
                            else
                            {
                                _activePlayerIds.Remove(scopeObject.Id);
                                world.RemoveObject(remote);
                                remote.Dispose();
                            }

                            completion.TrySetResult(activated);
                        }
                        else
                        {
                            world.RemoveObject(remote);
                            remote.Dispose();
                            completion.TrySetResult(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Error loading pending remote player {PlayerName} ({PlayerId:X4}).",
                            scopeObject.Name,
                            scopeObject.Id);
                        remote.Dispose();
                        completion.TrySetResult(false);
                    }
                }, TaskScheduler.Priority.High, $"ScopeImport.LoadPlayer.{scopeObject.Id:X4}");

                if (!queued)
                {
                    remote.Dispose();
                    completion.TrySetResult(false);
                }
            }

            if (loadTasks.Count > 0)
                await Task.WhenAll(loadTasks).ConfigureAwait(false);
        }

        public Task ImportPendingDroppedItemsAsync()
        {
            if (_scene.World is not WalkableWorldControl world)
                return Task.CompletedTask;

            var scopeManager = MuGame.Network?.GetScopeManager();
            if (scopeManager == null)
                return Task.CompletedTask;

            var allDrops = scopeManager.GetScopeItems(ScopeObjectType.Item)
                .Concat(scopeManager.GetScopeItems(ScopeObjectType.Money))
                .Cast<ScopeObject>();
            var loadTasks = new List<Task>();

            foreach (var scopeObject in allDrops)
            {
                if (_activeItemIds.Contains(scopeObject.Id))
                    continue;

                var droppedItem = DroppedItemObject.Rent(
                    scopeObject,
                    MuGame.Network.GetCharacterState().Id,
                    MuGame.Network.GetCharacterService(),
                    MuGame.AppLoggerFactory.CreateLogger<DroppedItemObject>());
                droppedItem.World = world;
                droppedItem.Hidden = true;
                int loadGeneration = droppedItem.LoadGeneration;

                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                loadTasks.Add(completion.Task);

                bool queued = MuGame.TaskScheduler.QueueTask(async () =>
                {
                    try
                    {
                        await droppedItem.Load().ConfigureAwait(false);
                        await droppedItem.PrepareGpuTexturesForFirstFrameAsync().ConfigureAwait(false);
                        MuGame.ScheduleOnMainThread(() =>
                        {
                            try
                            {
                                if (!ReferenceEquals(_scene.World, world) ||
                                    droppedItem.World != world ||
                                    droppedItem.LoadGeneration != loadGeneration ||
                                    _activeItemIds.Contains(scopeObject.Id))
                                {
                                    droppedItem.Recycle();
                                    completion.TrySetResult(false);
                                    return;
                                }

                                droppedItem.PrepareRenderResourcesForFirstFrame();
                                world.Objects.Add(droppedItem);
                                _activeItemIds.Add(scopeObject.Id);
                                droppedItem.Hidden = false;
                                completion.TrySetResult(true);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error publishing pending dropped item {ItemId:X4}.", scopeObject.Id);
                                droppedItem.Recycle();
                                completion.TrySetResult(false);
                            }
                        }, MainThreadDispatcher.WorkPriority.Low, $"ScopeImport.PublishDrop.{scopeObject.Id:X4}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error loading pending dropped item {ItemId:X4}.", scopeObject.Id);
                        droppedItem.Recycle();
                        completion.TrySetResult(false);
                    }
                }, TaskScheduler.Priority.Low, $"ScopeImport.LoadDrop.{scopeObject.Id:X4}");

                if (!queued)
                {
                    droppedItem.Recycle();
                    completion.TrySetResult(false);
                }
            }

            return loadTasks.Count == 0 ? Task.CompletedTask : Task.WhenAll(loadTasks);
        }

        public void ClearObjectTracking()
        {
            if (_scene.World?.Objects != null)
            {
                var objectsToRemove = new List<WorldObject>();

                foreach (var obj in _scene.World.Objects.ToList())
                {
                    if (obj == _scene.Hero) continue;

                    objectsToRemove.Add(obj);
                }

                foreach (var obj in objectsToRemove)
                {
                    _scene.World.Objects.Remove(obj);
                    if (obj is DroppedItemObject drop)
                        drop.Recycle();
                    else
                        obj.Dispose();
                }

                _logger?.LogDebug("ClearObjectTracking: Removed {Count} objects from previous map", objectsToRemove.Count);
            }

            _activePlayerIds.Clear();
            _activeMonsterIds.Clear();
            _activeNpcIds.Clear();
            _activeItemIds.Clear();

            var scopeManager = MuGame.Network?.GetScopeManager();
            if (scopeManager != null)
            {
                scopeManager.ClearDroppedItemsFromScope();
                _logger?.LogDebug("ClearObjectTracking: Manually cleared dropped items from ScopeManager");
            }
        }

        public void RemoveObjectFromTracking(ushort networkId)
        {
            _activePlayerIds.Remove(networkId);
            _activeMonsterIds.Remove(networkId);
            _activeNpcIds.Remove(networkId);
            _activeItemIds.Remove(networkId);
        }

        public void EnsureHeroNetworkId(ushort expectedId, string context = "")
        {
            if (_scene.Hero.NetworkId != expectedId)
            {
                _logger?.LogWarning($"NetworkId mismatch in {context}. Fixing: {_scene.Hero.NetworkId:X4} -> {expectedId:X4}");
                _scene.Hero.NetworkId = expectedId;
            }
        }

        public void EnsureWalkerNetworkId(WalkableWorldControl walkable, ushort expectedId, string context = "")
        {
            if (walkable?.Walker?.NetworkId != expectedId)
            {
                _logger?.LogWarning($"Walker NetworkId mismatch in {context}. Fixing: {walkable.Walker?.NetworkId:X4} -> {expectedId:X4}");
                if (walkable.Walker != null)
                {
                    walkable.Walker.NetworkId = expectedId;
                }
            }
        }
    }
}
