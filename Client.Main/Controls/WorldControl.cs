using Client.Data.ATT;
using Client.Data.CAP;
using Client.Data.OBJS;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Particles;
using Client.Main.Objects.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Main.Controls
{
    // Comparers for deterministic depth ordering. Opaque objects are rendered front-to-back,
    // while transparent objects are rendered back-to-front.
    sealed class WorldObjectDepthAsc : IComparer<WorldObject>
    {
        public static readonly WorldObjectDepthAsc Instance = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(WorldObject a, WorldObject b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a is null) return -1;
            if (b is null) return 1;

            int cmp = a.Depth.CompareTo(b.Depth);
            if (cmp != 0) return cmp;
            return a.NetworkId.CompareTo(b.NetworkId);
        }
    }

    sealed class WorldObjectDepthDesc : IComparer<WorldObject>
    {
        public static readonly WorldObjectDepthDesc Instance = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(WorldObject a, WorldObject b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a is null) return 1;
            if (b is null) return -1;

            int cmp = b.Depth.CompareTo(a.Depth);
            if (cmp != 0) return cmp;
            return b.NetworkId.CompareTo(a.NetworkId);
        }
    }

    sealed class WorldObjectOpaqueBatchComparer : IComparer<WorldObject>
    {
        public static readonly WorldObjectOpaqueBatchComparer Instance = new();
        private const float DepthBucketSize = 512f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ReferenceKey(object value) => value == null ? 0 : RuntimeHelpers.GetHashCode(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int DepthBucket(float depth)
        {
            if (!float.IsFinite(depth))
                return int.MaxValue;
            return (int)MathF.Floor(depth / DepthBucketSize);
        }

        public int Compare(WorldObject a, WorldObject b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a is null) return -1;
            if (b is null) return 1;

            // Keep approximate front-to-back ordering for early-Z, then group nearby
            // objects by model/material to reduce state and geometry switches.
            int comparison = DepthBucket(a.Depth).CompareTo(DepthBucket(b.Depth));
            if (comparison != 0) return comparison;

            bool aIsModel = a is ModelObject;
            bool bIsModel = b is ModelObject;
            comparison = bIsModel.CompareTo(aIsModel);
            if (comparison != 0) return comparison;

            if (a is ModelObject aModel && b is ModelObject bModel)
            {
                comparison = ReferenceKey(aModel.Model).CompareTo(ReferenceKey(bModel.Model));
                if (comparison != 0) return comparison;

                comparison = ReferenceKey(aModel.GetSortTextureHint()).CompareTo(ReferenceKey(bModel.GetSortTextureHint()));
                if (comparison != 0) return comparison;
            }

            comparison = ReferenceKey(a.BlendState).CompareTo(ReferenceKey(b.BlendState));
            if (comparison != 0) return comparison;

            comparison = a.Depth.CompareTo(b.Depth);
            if (comparison != 0) return comparison;

            comparison = a.NetworkId.CompareTo(b.NetworkId);
            return comparison != 0
                ? comparison
                : ReferenceKey(a).CompareTo(ReferenceKey(b));
        }
    }

    /// <summary>
    /// Base class for rendering and managing world objects in a game scene.
    /// </summary>
    public abstract class WorldControl : GameControl
    {
        // --- Fields & Constants ---
        private int _renderCounter;
        private int _lastRenderMetricsLogFrame = -10000;
        private DepthStencilState _currentDepthState = DepthStencilState.Default;
        private static readonly DepthStencilState DepthStateDefault = DepthStencilState.Default;
        private static readonly DepthStencilState DepthStateDepthRead = DepthStencilState.DepthRead;


        private readonly List<WorldObject> _solidBehind = [];
        private readonly List<WorldObject> _transparentObjects = [];
        private readonly List<ModelObject> _queuedCrowdSidePasses = [];
        private readonly List<WalkerObject> _walkers = [];
        private readonly List<PlayerObject> _players = [];
        private readonly List<MonsterObject> _monsters = [];
        private readonly List<DroppedItemObject> _droppedItems = [];
        private readonly DroppedItemRenderSelector _droppedItemSelector = new();
        private readonly Queue<WorldObject> _objectsToInitialize = [];
        private readonly HashSet<WorldObject> _queuedForInitialization = [];
        private readonly List<WorldObject> _visibleObjects = [];

        // Snapshot of objects that survived this frame's visibility/culling pass.
        // Overlay/UI passes (nameplates, bbox, hover) should iterate this rather than the
        // full World.Objects snapshot to avoid touching everything on the map.
        public IReadOnlyList<WorldObject> VisibleObjects => _visibleObjects;
        private readonly HashSet<WorldObject> _visibleObjectSet = [];
        private readonly Dictionary<WorldObject, int> _visibleObjectIndices = [];
        private bool _isUpdatingVisibleObjects;
        private bool _visibleObjectsNeedCompaction;
        private readonly HashSet<WorldObject> _positionDirtyObjects = [];
        private WorldObject[] _dirtyVisibilityScratch = Array.Empty<WorldObject>();
        private readonly List<WorldObject> _pendingVisibleAdd = new(256);
        private readonly List<WorldObject> _pendingVisibleRemove = new(256);
        private readonly object _visibleMergeLock = new();
        private bool _dirtyVisibleObjects = true;
        private bool _hasVisibilitySnapshot;
        private ulong _lastCulledCameraVersion;
        private Vector3 _lastCulledCameraPosition;
        private Vector3 _lastCulledCameraDirection = Vector3.UnitY;
        private float _lastCulledViewFar = float.NaN;
        private float _lastCulledFov = float.NaN;
        private float _lastCulledAspectRatio = float.NaN;
        private readonly Stopwatch _cullingStopwatch = new();
        private const int MaxObjectInitializationsPerFrame = 8;
        private const int MaxConcurrentObjectInitializations = 4;
        private int _activeObjectInitializations;
        private const float CameraCullMoveThreshold = 32f;
        private const float CameraCullDirectionDotThreshold = 0.9995f;
        private const float WorldCullGuardBand = 64f;
        private const float NearUpdateDistanceSq = 2200f * 2200f;
        private const float MidUpdateDistanceSq = 4200f * 4200f;
        private const float FarUpdateDistanceSq = 6200f * 6200f;
        private const int ParallelVisibleRebuildThreshold = 1536;
        private const int ParallelDirtyRefreshThreshold = 1024;
        private const int SpatialSectorTileSize = 16;
        private const int SpatialSectorsPerAxis = Constants.TERRAIN_SIZE / SpatialSectorTileSize;
        private const int SpatialInvalidSector = -1;
        private const int SpatialRebuildPaddingSectors = 1;
        private static readonly ParallelOptions VisibleParallelOptions = new()
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2)
        };
        private readonly List<WorldObject>[,] _spatialSectors = new List<WorldObject>[SpatialSectorsPerAxis, SpatialSectorsPerAxis];
        private readonly List<WorldObject> _spatialOffGridObjects = [];
        private readonly Dictionary<WorldObject, int> _spatialObjectSectors = [];
        private readonly List<WorldObject> _spatialCandidates = [];
        private readonly HashSet<WorldObject> _spatialCandidateSet = [];

        public Dictionary<ushort, WalkerObject> WalkerObjectsById { get; } = [];

        private ILogger _logger = ModelObject.AppLoggerFactory?.CreateLogger<WorldControl>();

        // --- Properties ---

        public string BackgroundMusicPath { get; set; }
        public string AmbientSoundPath { get; set; }

        public TerrainControl Terrain { get; }
        public WorldFrameMetrics FrameMetrics { get; } = new();

        private static long s_nextWorldInstanceId;

        public long WorldInstanceId { get; }
        public short WorldIndex { get; private set; }
        public bool IsSunWorld { get; protected set; } = true;

        public bool EnableShadows { get; protected set; } = true;

        public ChildrenCollection<WorldObject> Objects { get; private set; }
        = new ChildrenCollection<WorldObject>(null);
        public IReadOnlyList<WalkerObject> Walkers => _walkers;
        public IReadOnlyList<PlayerObject> Players => _players;
        public IReadOnlyList<MonsterObject> Monsters => _monsters;
        public IReadOnlyList<DroppedItemObject> DroppedItems => _droppedItems;
        public ulong LastCullCameraVersion => _lastCulledCameraVersion;
        public int LastCullCandidateCount { get; private set; }
        public int LastCullVisibleCount { get; private set; }
        public float LastCullRebuildMs { get; private set; }
        public bool LastCullWasRebuild { get; private set; }

        public Type[] MapTileObjects { get; } = new Type[Constants.TERRAIN_SIZE];

        public ushort MapId { get; protected set; }

        public new string Name { get; protected set; }

        // --- Constructor ---

        public WorldControl(short worldIndex)
        {
            WorldInstanceId = Interlocked.Increment(ref s_nextWorldInstanceId);
            AutoViewSize = false;
            ViewSize = new(MuGame.Instance.Width, MuGame.Instance.Height);
            WorldIndex = worldIndex;
            if (Constants.SUN_WORLD_INDICES != null && Constants.SUN_WORLD_INDICES.Length > 0)
            {
                IsSunWorld = Array.IndexOf(Constants.SUN_WORLD_INDICES, worldIndex) >= 0;
            }

            var worldInfo = (WorldInfoAttribute)Attribute.GetCustomAttribute(GetType(), typeof(WorldInfoAttribute));
            if (worldInfo != null)
            {
                MapId = worldInfo.MapId;
                Name = worldInfo.DisplayName;
            }

            Controls.Add(Terrain = new TerrainControl { WorldIndex = worldIndex });
            Objects.ControlAdded += OnObjectAdded;
            Objects.ControlRemoved += OnObjectRemoved;

            for (int y = 0; y < SpatialSectorsPerAxis; y++)
            {
                for (int x = 0; x < SpatialSectorsPerAxis; x++)
                {
                    _spatialSectors[x, y] = new List<WorldObject>(16);
                }
            }
        }

        private void Object_PositionChanged(object sender, EventArgs e)
        {
            if (sender is WorldObject obj)
            {
                _positionDirtyObjects.Add(obj);
                UpdateSpatialRegistration(obj);
            }
            else
                _dirtyVisibleObjects = true;

            MarkWorldGeometryChanged();
        }

        private static int s_worldGeometryTick;

        // Monotonic counter bumped whenever tracked world geometry or caster visibility changes.
        // Read by ShadowMapRenderer to detect a fully static frame and skip redundant casters.
        public static int WorldGeometryTick => System.Threading.Volatile.Read(ref s_worldGeometryTick);

        private static void MarkWorldGeometryChanged()
        {
            unchecked { System.Threading.Interlocked.Increment(ref s_worldGeometryTick); }
        }

        private void Object_StatusChanged(object sender, EventArgs e)
        {
            if (sender is not WorldObject obj)
                return;

            if (obj.Status == GameControlStatus.Ready)
            {
                _positionDirtyObjects.Add(obj);
                UpdateSpatialRegistration(obj);
                MarkWorldGeometryChanged();
                return;
            }

            if (obj.Status == GameControlStatus.Disposed || obj.Status == GameControlStatus.Error)
            {
                RemoveVisibleObject(obj);
                _positionDirtyObjects.Remove(obj);
                UnregisterSpatialObject(obj);
                MarkWorldGeometryChanged();
            }
        }

        // --- Lifecycle Methods ---

        public override async Task Load()
        {
            await base.Load();

            CreateMapTileObjects();
            Camera.Instance.AspectRatio = GraphicsDevice.Viewport.AspectRatio;

            var worldFolder = $"World{WorldIndex}";
            var dataPath = Constants.DataPath;
            var tasks = new List<Task>();

            // Load camera settings
            var capPath = Path.Combine(dataPath, worldFolder, "Camera_Angle_Position.bmd");
            if (File.Exists(capPath))
            {
                var capReader = new CAPReader();
                var data = await capReader.Load(capPath);
                Camera.Instance.FOV = data.CameraFOV * Constants.FOV_SCALE;
                Camera.Instance.Position = data.CameraPosition;
                Camera.Instance.Target = data.HeroPosition;
            }

            // Load terrain OBJ
            var objPath = Path.Combine(dataPath, worldFolder, $"EncTerrain{WorldIndex}.obj");
            if (File.Exists(objPath))
            {
                var reader = new OBJReader();
                OBJ obj = await reader.Load(objPath);
                foreach (var mapObj in obj.Objects)
                {
                    var instance = WorldObjectFactory.CreateMapTileObject(this, mapObj);
                    if (instance != null) tasks.Add(instance.Load());
                }
            }

            // tasks.Add(Container.Load());
            await Task.WhenAll(tasks);

            // Play or stop background music
            if (!string.IsNullOrEmpty(BackgroundMusicPath))
                SoundController.Instance.PlayBackgroundMusic(BackgroundMusicPath);
            else
                SoundController.Instance.StopBackgroundMusic();

            // Play or stop ambient sound
            if (!string.IsNullOrEmpty(AmbientSoundPath))
                SoundController.Instance.PlayAmbientSound(AmbientSoundPath);
            else
                SoundController.Instance.StopAmbientSound();
        }

        public override void AfterLoad()
        {
            base.AfterLoad();
        }

        public override void Update(GameTime time)
        {
            base.Update(time);

            if (Status != GameControlStatus.Ready) return;
            FrameMetrics.Reset();
            FrameMetrics.CullCandidates = LastCullCandidateCount;
            FrameMetrics.VisibleObjects = LastCullVisibleCount;
            FrameMetrics.CullMs = LastCullRebuildMs;
            LastCullWasRebuild = false;

            if (_objectsToInitialize.Count > 0)
            {
                int availableSlots = Math.Max(0, MaxConcurrentObjectInitializations - Volatile.Read(ref _activeObjectInitializations));
                int initCount = Math.Min(
                    Math.Min(MaxObjectInitializationsPerFrame, _objectsToInitialize.Count),
                    availableSlots);
                for (int i = 0; i < initCount; i++)
                {
                    var obj = _objectsToInitialize.Dequeue();
                    _queuedForInitialization.Remove(obj);

                    if (obj == null || !ReferenceEquals(obj.World, this) || obj.Status != GameControlStatus.NonInitialized)
                        continue;

                    if (!QueueObjectInitialization(obj))
                        EnqueueObjectInitialization(obj);
                }
            }

            // Keep update list current for object movement, but defer full camera recull to end of update
            // so rendering uses the latest camera state from this frame.
            if (_positionDirtyObjects.Count > 0 && !_dirtyVisibleObjects)
            {
                RefreshDirtyVisibleObjects();
            }

            UpdateVisibleObjects(time);

            var camera = Camera.Instance;
            ulong cameraVersion = camera.CullingStateVersion;
            bool needsFullRebuild =
                _dirtyVisibleObjects ||
                (Constants.ENABLE_CROWD_SPATIAL_CULLING && _spatialObjectSectors.Count != Objects.Count) ||
                HasSignificantCameraCullChange(camera) ||
                !_hasVisibilitySnapshot;

            if (needsFullRebuild)
            {
                RebuildVisibleObjects();
                _dirtyVisibleObjects = false;
                CaptureCulledCameraState(camera, cameraVersion);
            }
            else if (_positionDirtyObjects.Count > 0)
            {
                RefreshDirtyVisibleObjects();
            }

            WorldHoverSystem.UpdateHover(_visibleObjects, Scene);
        }

        private bool QueueObjectInitialization(WorldObject obj)
        {
            if (obj == null || obj.Status != GameControlStatus.NonInitialized)
                return true;

            var scheduler = MuGame.TaskScheduler;
            if (scheduler == null || Volatile.Read(ref _activeObjectInitializations) >= MaxConcurrentObjectInitializations)
                return false;

            Interlocked.Increment(ref _activeObjectInitializations);
            bool queued = scheduler.QueueTask(
                () => LoadInitializedObjectAsync(obj),
                Controllers.TaskScheduler.Priority.Low);

            if (!queued)
                Interlocked.Decrement(ref _activeObjectInitializations);

            return queued;
        }

        private void EnqueueObjectInitialization(WorldObject obj)
        {
            if (obj == null || obj.Status != GameControlStatus.NonInitialized)
                return;

            if (_queuedForInitialization.Add(obj))
                _objectsToInitialize.Enqueue(obj);
        }

        private async Task LoadInitializedObjectAsync(WorldObject obj)
        {
            try
            {
                await obj.Load();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize world object {ObjectType} ({NetworkId:X4}).", obj.GetType().Name, obj.NetworkId);
                MuGame.ScheduleOnMainThread(() =>
                {
                    if (ReferenceEquals(obj.World, this))
                    {
                        RemoveObject(obj);
                    }

                    if (obj.Status != GameControlStatus.Disposed)
                    {
                        obj.Dispose();
                    }
                });
            }
            finally
            {
                Interlocked.Decrement(ref _activeObjectInitializations);
            }
        }

        public override void Draw(GameTime time)
        {
            if (Status != GameControlStatus.Ready) return;

            OverheadNameplateRenderer.BeginFrame();

            // Build shadow map before any backbuffer drawing so terrain tiles aren't lost
            if (EnableShadows && Constants.ENABLE_SHADOW_MAPPING && GraphicsManager.Instance.ShadowMapRenderer != null)
            {
                GraphicsManager.Instance.ShadowMapRenderer.RenderShadowMap(this);
            }

            base.Draw(time);
            RenderObjects(time);
        }

        // --- Object Management ---

        public bool IsWalkable(Vector2 position)
        {
            var terrainFlag = Terrain.RequestTerrainFlag((int)position.X, (int)position.Y);
            bool hasNoMove = terrainFlag.HasFlag(TWFlags.NoMove);

            // In Blood Castle, when the event is active (timer started), allow crossing the bridge
            // even if NoMove flag is set (the bridge area is normally blocked until event starts)
            if (hasNoMove && UI.Game.BloodCastleTimeControl.IsEventActive)
            {
                // Check if we're on a Blood Castle map (map IDs 11-17 and 52)
                var charState = MuGame.Network?.GetCharacterState();
                if (charState != null)
                {
                    var mapId = charState.MapId;
                    if ((mapId >= 11 && mapId <= 17) || mapId == 52)
                    {
                        return true; // Allow movement during active Blood Castle event
                    }
                }
            }

            return !hasNoMove;
        }

        private void OnObjectAdded(object sender, ChildrenEventArgs<WorldObject> e)
        {
            WorldObject worldObject = e.Control;

            // NPC roots are world-local. A stale asynchronous spawn or a retained scene
            // reference must never attach an NPC created for another WorldControl instance
            // to the new map. Hide it immediately and remove it through the main-thread queue.
            if (worldObject is NPCObject &&
                worldObject.OwningWorldInstanceId != 0 &&
                worldObject.OwningWorldInstanceId != WorldInstanceId)
            {
                long previousWorldInstanceId = worldObject.OwningWorldInstanceId;
                worldObject.Hidden = true;

                _logger?.LogWarning(
                    "Rejected stale NPC {NpcType} ({NetworkId:X4}) from world instance {OldWorld}; current world instance is {CurrentWorld}.",
                    worldObject.GetType().Name,
                    worldObject.NetworkId,
                    previousWorldInstanceId,
                    WorldInstanceId);

                MuGame.ScheduleOnMainThread(() =>
                {
                    Objects.Remove(worldObject);
                    worldObject.Dispose();
                }, MainThreadDispatcher.WorkPriority.Critical);
                return;
            }

            if (worldObject.OwningWorldInstanceId == 0 || worldObject is PlayerObject)
                worldObject.OwningWorldInstanceId = WorldInstanceId;

            worldObject.World = this;
            worldObject.HiddenChanged += Object_HiddenChanged;
            e.Control.PositionChanged += Object_PositionChanged;
            e.Control.StatusChanged += Object_StatusChanged;

            TrackObjectType(e.Control);
            if (e.Control is WalkerObject walker &&
                walker.NetworkId != 0 &&
                walker.NetworkId != 0xFFFF)
            {
                if (WalkerObjectsById.TryGetValue(walker.NetworkId, out var existing))
                {
                    if (!ReferenceEquals(existing, walker))
                    {
                        _logger?.LogWarning("Replacing WalkerObject ID {Id:X4} - old: {OldType}, new: {NewType}",
                                           walker.NetworkId, existing.GetType().Name, walker.GetType().Name);
                        existing.Dispose(); // Dispose the old one
                    }
                }
                WalkerObjectsById[walker.NetworkId] = walker; // Always update/add
            }

            RegisterSpatialObject(e.Control);
            _positionDirtyObjects.Add(e.Control);
            MarkWorldGeometryChanged();
            if (e.Control.Status == GameControlStatus.NonInitialized)
                EnqueueObjectInitialization(e.Control);
        }

        private void Object_HiddenChanged(object sender, EventArgs e)
        {
            var obj = sender as WorldObject;
            if (obj == null)
                return;

            if (obj.Hidden)
                RemoveVisibleObject(obj);
            else
                _positionDirtyObjects.Add(obj);

            MarkWorldGeometryChanged();
        }

        private void OnObjectRemoved(object sender, ChildrenEventArgs<WorldObject> e)
        {
            UntrackObjectType(e.Control);
            if (e.Control is WalkerObject walker &&
                walker.NetworkId != 0 &&
                walker.NetworkId != 0xFFFF)
            {
                if (WalkerObjectsById.TryGetValue(walker.NetworkId, out var storedWalker))
                {
                    // Only remove if it's the same object reference
                    if (ReferenceEquals(storedWalker, walker))
                    {
                        WalkerObjectsById.Remove(walker.NetworkId);
                        _logger?.LogTrace("Removed walker {Id:X4} from dictionary.", walker.NetworkId);
                    }
                    else
                    {
                        _logger?.LogDebug("Walker {Id:X4} removed from Objects but different object in dictionary.", walker.NetworkId);
                    }
                }
            }

            e.Control.HiddenChanged -= Object_HiddenChanged;
            e.Control.PositionChanged -= Object_PositionChanged;
            e.Control.StatusChanged -= Object_StatusChanged;

            RemoveVisibleObject(e.Control);
            _positionDirtyObjects.Remove(e.Control);
            _queuedForInitialization.Remove(e.Control);
            UnregisterSpatialObject(e.Control);
            MarkWorldGeometryChanged();
        }

        private void TrackObjectType(WorldObject obj)
        {
            if (obj is WalkerObject walker)
                _walkers.Add(walker);

            if (obj is PlayerObject player)
                _players.Add(player);

            if (obj is MonsterObject monster)
                _monsters.Add(monster);

            if (obj is DroppedItemObject drop)
                _droppedItems.Add(drop);
        }

        private void UntrackObjectType(WorldObject obj)
        {
            if (obj is WalkerObject walker)
                _walkers.Remove(walker);

            if (obj is PlayerObject player)
                _players.Remove(player);

            if (obj is MonsterObject monster)
                _monsters.Remove(monster);

            if (obj is DroppedItemObject drop)
                _droppedItems.Remove(drop);
        }

        public void DebugListWalkers()
        {
            _logger?.LogDebug("=== Walker Debug Info ===");
            _logger?.LogDebug("Objects collection count: {Count}", _walkers.Count);
            _logger?.LogDebug("WalkerObjectsById count: {Count}", WalkerObjectsById.Count);

            foreach (var walker in _walkers)
            {
                _logger?.LogDebug("Objects: {Type} {Id:X4}", walker.GetType().Name, walker.NetworkId);
            }

            foreach (var kvp in WalkerObjectsById)
            {
                _logger?.LogDebug("Dictionary: {Id:X4} -> {Type}", kvp.Key, kvp.Value.GetType().Name);
            }

            if (this is WalkableWorldControl walkable && walkable.Walker != null)
            {
                _logger?.LogDebug("Local player: {Type} {Id:X4}", walkable.Walker.GetType().Name, walkable.Walker.NetworkId);
            }
            _logger?.LogDebug("=== End Walker Debug ===");
        }

        /// <summary>
        /// Attempts to retrieve a walker by its network ID.
        /// Checks local player first, then dictionary, then full Objects search as fallback.
        /// </summary>
        public virtual bool TryGetWalkerById(ushort networkId, out WalkerObject walker)
        {
            // First check: local player in WalkableWorldControl
            if (this is WalkableWorldControl walkable &&
                walkable.Walker?.NetworkId == networkId)
            {
                walker = walkable.Walker;
                return true;
            }

            // Second check: WalkerObjectsById dictionary
            if (WalkerObjectsById.TryGetValue(networkId, out walker))
            {
                return true;
            }

            // Third check: fallback search in tracked walkers list
            for (int i = 0; i < _walkers.Count; i++)
            {
                var candidate = _walkers[i];
                if (candidate != null && candidate.NetworkId == networkId)
                {
                    walker = candidate;
                    if (!WalkerObjectsById.ContainsKey(networkId))
                    {
                        WalkerObjectsById[networkId] = walker;
                        _logger?.LogDebug("Sync fix: Added walker {Id:X4} to dictionary during lookup.", networkId);
                    }
                    return true;
                }
            }

            return false;
        }

        public bool ContainsWalkerId(ushort networkId) =>
            WalkerObjectsById.ContainsKey(networkId);

        public WalkerObject FindWalkerById(ushort networkId) =>
            TryGetWalkerById(networkId, out var walker) ? walker : null;

        public PlayerObject FindPlayerById(ushort networkId)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                if (player != null && player.NetworkId == networkId)
                    return player;
            }
            return null;
        }

        public DroppedItemObject FindDroppedItemById(ushort networkId)
        {
            for (int i = 0; i < _droppedItems.Count; i++)
            {
                var drop = _droppedItems[i];
                if (drop != null && drop.NetworkId == networkId)
                    return drop;
            }
            return null;
        }

        public MonsterObject FindMonsterById(ushort networkId)
        {
            for (int i = 0; i < _monsters.Count; i++)
            {
                var monster = _monsters[i];
                if (monster != null && monster.NetworkId == networkId)
                    return monster;
            }
            return null;
        }

        /// <summary>
        /// Removes an object from the scene and dictionary if applicable.
        /// </summary>
        public bool RemoveObject(WorldObject obj)
        {
            bool removed = Objects.Remove(obj);
            if (removed && obj is WalkerObject walker &&
                walker.NetworkId != 0 &&
                walker.NetworkId != 0xFFFF)
            {
                WalkerObjectsById.Remove(walker.NetworkId);
            }
            return removed;
        }

        // --- Rendering Helpers ---

        private void RenderObjects(GameTime time)
        {
            _droppedItemSelector.SelectRenderableItems(_droppedItems, time);

            _renderCounter = 0;
            _solidBehind.Clear();
            _transparentObjects.Clear();

            var objects = _visibleObjects;

            for (var i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];

                if (!obj.Visible)
                    continue;

                if (obj.IsTransparent)
                {
                    _transparentObjects.Add(obj);
                }
                else
                {
                    _solidBehind.Add(obj);
                }
            }

            FrameMetrics.SolidBehindObjects = _solidBehind.Count;
            FrameMetrics.SolidInFrontObjects = 0;
            FrameMetrics.TransparentObjects = _transparentObjects.Count;

            if (_solidBehind.Count > 1)
            {
                IComparer<WorldObject> comparer = Constants.ENABLE_BATCH_OPTIMIZED_SORTING
                    ? WorldObjectOpaqueBatchComparer.Instance
                    : WorldObjectDepthAsc.Instance;
                _solidBehind.Sort(comparer);
            }

            if (_transparentObjects.Count > 1)
                _transparentObjects.Sort(WorldObjectDepthDesc.Instance);

            DrawListWithSpriteBatchGrouping(_solidBehind, DepthStateDefault, time);
            DrawListWithSpriteBatchGrouping(_transparentObjects, DepthStateDepthRead, time);

            // Draw post-pass (DrawAfter)
            DrawAfterPass(_solidBehind, DepthStateDefault, time);
            DrawAfterPass(_transparentObjects, DepthStateDepthRead, time);

            OverheadNameplateRenderer.FlushQueuedNameplates(GraphicsManager.Instance.Sprite);
            LogRenderMetricsIfEnabled();
        }

        private void LogRenderMetricsIfEnabled()
        {
            if (Constants.RENDER_METRICS_LEVEL < 2)
                return;

            int frame = MuGame.FrameIndex;
            if (frame - _lastRenderMetricsLogFrame < 180)
                return;

            _lastRenderMetricsLogFrame = frame;
            _logger?.LogInformation(
                "World perf W:{WorldIndex} Cull:{CullMode} C:{CullCandidates} V:{Visible} Ms:{CullMs:F2} Lists:{Behind}/{Front}/{Transparent} DrawObj S:{SpriteObjects} M:{ModelObjects} Anim U:{AnimUpdates} Skip:{AnimSkips} LQ:{LowQuality}",
                WorldIndex,
                LastCullWasRebuild ? "R" : "I",
                FrameMetrics.CullCandidates,
                FrameMetrics.VisibleObjects,
                FrameMetrics.CullMs,
                FrameMetrics.SolidBehindObjects,
                FrameMetrics.SolidInFrontObjects,
                FrameMetrics.TransparentObjects,
                FrameMetrics.SpriteBatchObjects,
                FrameMetrics.ModelObjects,
                FrameMetrics.AnimationUpdates,
                FrameMetrics.AnimationSkips,
                FrameMetrics.LowQualityObjects);
        }

        private void DrawListWithSpriteBatchGrouping(List<WorldObject> list, DepthStencilState depthState, GameTime time)
        {
            if (list.Count == 0)
                return;

            SetDepthState(depthState);
            bool canUseMapInstancing = depthState == DepthStateDefault && Constants.ENABLE_MAP_OBJECT_INSTANCING;
            bool canUseWalkerCrowdInstancing = depthState == DepthStateDefault && Constants.ENABLE_WALKER_CROWD_INSTANCING;
            _queuedCrowdSidePasses.Clear();

            var spriteBatch = GraphicsManager.Instance.Sprite;
            Helpers.SpriteBatchScope? scope = null;
            BlendState currentBlend = null;
            SamplerState currentSampler = null;
            DepthStencilState currentBatchDepth = null;

            for (int i = 0; i < list.Count; i++)
            {
                var obj = list[i];
                if (obj == null)
                    continue;

                bool usesSpriteBatch =
                    obj is SpriteObject ||
                    obj is WaterMistParticleSystem ||
                    obj is ElfBuffOrbTrail;

                if (usesSpriteBatch)
                {
                    FrameMetrics.SpriteBatchObjects++;

                    if (canUseWalkerCrowdInstancing)
                        FlushWalkerCrowdBatchesAndSidePasses(time);

                    if (canUseMapInstancing && ModelObject.HasPendingStaticMapInstancingBatches())
                        ModelObject.FlushStaticMapInstancingBatches(this);

                    var blend = obj.BlendState ?? BlendState.AlphaBlend;
                    SamplerState sampler;
                    if (obj is WaterMistParticleSystem || obj is ElfBuffOrbTrail)
                    {
                        sampler = SamplerState.LinearClamp;
                    }
                    else
                    {
                        sampler = ReferenceEquals(blend, BlendState.Additive)
                            ? GraphicsManager.GetQualityLinearSamplerState()
                            : GraphicsManager.GetQualitySamplerState();
                    }
                    // Additive sprites must not write depth because their quads would occlude
                    // later transparent geometry.
                    var objectDepthState = ResolveObjectDepthState(obj, depthState);
                    var batchDepth =
                        obj is WaterMistParticleSystem ||
                        obj is ElfBuffOrbTrail ||
                        obj is ElfBuffOrbitingLight
                            ? DepthStencilState.DepthRead
                            : objectDepthState;

                    if (scope == null ||
                        !ReferenceEquals(blend, currentBlend) ||
                        !ReferenceEquals(sampler, currentSampler) ||
                        !ReferenceEquals(batchDepth, currentBatchDepth))
                    {
                        scope?.Dispose();
                        scope = new Helpers.SpriteBatchScope(spriteBatch, SpriteSortMode.Deferred, blend, sampler, batchDepth);
                        currentBlend = blend;
                        currentSampler = sampler;
                        currentBatchDepth = batchDepth;
                    }

                    obj.Draw(time);
                }
                else
                {
                    if (obj is ModelObject)
                        FrameMetrics.ModelObjects++;

                    scope?.Dispose();
                    scope = null;
                    currentBlend = null;
                    currentSampler = null;
                    currentBatchDepth = null;

                    if (canUseWalkerCrowdInstancing && ModelObject.TryQueueWalkerCrowdForInstancing(obj))
                    {
                        if (obj is ModelObject queuedMonster)
                            _queuedCrowdSidePasses.Add(queuedMonster);

                        obj.RenderOrder = ++_renderCounter;
                        continue;
                    }

                    var staticMapQueueResult = canUseMapInstancing
                        ? ModelObject.TryQueueStaticMapObjectForInstancing(obj)
                        : ModelObject.StaticMapInstancingQueueResult.None;

                    if (canUseMapInstancing &&
                        staticMapQueueResult == ModelObject.StaticMapInstancingQueueResult.None &&
                        ModelObject.IsStaticMapInstancingPathAvailable() &&
                        obj is ModelObject mapModel &&
                        mapModel.IsMapPlacementObject)
                        ModelObject.RegisterStaticMapInstancingFallback();

                    SetDepthState(ResolveObjectDepthState(obj, depthState));
                    obj.Draw(time);
                }

                obj.RenderOrder = ++_renderCounter;
            }

            scope?.Dispose();

            if (canUseWalkerCrowdInstancing)
                FlushWalkerCrowdBatchesAndSidePasses(time);

            if (canUseMapInstancing && ModelObject.HasPendingStaticMapInstancingBatches())
                ModelObject.FlushStaticMapInstancingBatches(this);
        }

        private void FlushWalkerCrowdBatchesAndSidePasses(GameTime time)
        {
            if (ModelObject.HasPendingWalkerCrowdInstancingBatches())
                ModelObject.FlushWalkerCrowdInstancingBatches(this);

            for (int i = 0; i < _queuedCrowdSidePasses.Count; i++)
                _queuedCrowdSidePasses[i]?.DrawQueuedCrowdInstancingSidePasses(time);

            _queuedCrowdSidePasses.Clear();
        }

        private void DrawAfterPass(List<WorldObject> list, DepthStencilState state, GameTime time)
        {
            var objCount = list.Count;
            if (objCount == 0) return;
            SetDepthState(state);

            // Damage texts use identical sprite-batch state and are depth-disabled overlays.
            // Defer them so we open one scope per pass rather than one per instance.
            int damageCount = 0;
            for (var i = 0; i < objCount; i++)
            {
                var obj = list[i];
                if (obj is DamageTextObject)
                {
                    damageCount++;
                    continue;
                }
                SetDepthState(ResolveObjectDepthState(obj, state));
                obj.DrawAfter(time);
            }

            if (damageCount > 0)
            {
                var sb = GraphicsManager.Instance.Sprite;
                using (new Helpers.SpriteBatchScope(
                    sb,
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.AnisotropicClamp,
                    DepthStencilState.None,
                    RasterizerState.CullNone,
                    null,
                    UiScaler.SpriteTransform))
                {
                    for (var i = 0; i < objCount; i++)
                    {
                        if (list[i] is DamageTextObject)
                            list[i].DrawAfter(time);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetDepthState(DepthStencilState state)
        {
            if (_currentDepthState != state)
            {
                GraphicsDevice.DepthStencilState = state;
                _currentDepthState = state;
            }
        }

        private static DepthStencilState ResolveObjectDepthState(WorldObject obj, DepthStencilState passState)
        {
            if (obj?.DepthState != null && !ReferenceEquals(obj.DepthState, DepthStateDefault))
                return obj.DepthState;

            return passState;
        }

        // Fast path for loops where camera info is already cached
        private static bool IsObjectInView(WorldObject obj, Vector2 cam2, float maxDistSq, BoundingFrustum frustum)
        {
            var pos3 = obj.WorldPosition.Translation;
            float dx = cam2.X - pos3.X;
            float dy = cam2.Y - pos3.Y;
            if (dx * dx + dy * dy > maxDistSq)
                return false;

            if (frustum == null)
                return false;

            BoundingBox bounds = obj.BoundingBoxWorld;
            Vector3 margin = new(WorldCullGuardBand);
            return frustum.Contains(new BoundingBox(bounds.Min - margin, bounds.Max + margin)) != ContainmentType.Disjoint;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldForceVisible(WorldObject obj)
        {
            if (obj == null)
                return false;

            var policy = obj.RenderPolicy;

            if (policy.ForceVisible || (obj is WalkerObject walker && walker.IsMainWalker))
            {
                return true;
            }

            return obj.World?.WorldIndex == 95
                && (policy.ForceVisibleInLoginWorld || HasForceVisibleEffectChild(obj));
        }

        private static bool HasForceVisibleEffectChild(WorldObject obj)
        {
            if (obj == null)
                return false;

            var children = obj.Children;
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child?.RenderPolicy.ForceVisible == true || child?.RenderPolicy.ForceVisibleInLoginWorld == true)
                    return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddVisibleObject(WorldObject obj)
        {
            if (obj == null)
                return;

            if (_visibleObjectSet.Add(obj))
            {
                _visibleObjectIndices[obj] = _visibleObjects.Count;
                _visibleObjects.Add(obj);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveVisibleObject(WorldObject obj)
        {
            if (obj == null)
                return;

            if (!_visibleObjectSet.Remove(obj))
                return;

            if (!_visibleObjectIndices.TryGetValue(obj, out int index) ||
                (uint)index >= (uint)_visibleObjects.Count)
            {
                index = _visibleObjects.IndexOf(obj);
                if (index < 0)
                {
                    _visibleObjectIndices.Remove(obj);
                    return;
                }
            }

            if (_isUpdatingVisibleObjects)
            {
                _visibleObjects[index] = null;
                _visibleObjectIndices.Remove(obj);
                _visibleObjectsNeedCompaction = true;
                return;
            }

            int lastIndex = _visibleObjects.Count - 1;
            WorldObject last = _visibleObjects[lastIndex];
            if (index != lastIndex)
            {
                _visibleObjects[index] = last;
                if (last != null)
                    _visibleObjectIndices[last] = index;
            }

            _visibleObjects.RemoveAt(lastIndex);
            _visibleObjectIndices.Remove(obj);
        }

        private void CompactVisibleObjects()
        {
            if (!_visibleObjectsNeedCompaction)
                return;

            int writeIndex = 0;
            for (int readIndex = 0; readIndex < _visibleObjects.Count; readIndex++)
            {
                WorldObject obj = _visibleObjects[readIndex];
                if (obj == null)
                    continue;

                _visibleObjects[writeIndex] = obj;
                _visibleObjectIndices[obj] = writeIndex;
                writeIndex++;
            }

            if (writeIndex < _visibleObjects.Count)
                _visibleObjects.RemoveRange(writeIndex, _visibleObjects.Count - writeIndex);

            _visibleObjectsNeedCompaction = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PackSpatialSector(int sectorX, int sectorY) => (sectorY * SpatialSectorsPerAxis) + sectorX;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetSpatialSector(Vector3 worldPos, out int sectorX, out int sectorY)
        {
            int tileX = (int)MathF.Floor(worldPos.X / Constants.TERRAIN_SCALE);
            int tileY = (int)MathF.Floor(worldPos.Y / Constants.TERRAIN_SCALE);

            if ((uint)tileX >= Constants.TERRAIN_SIZE || (uint)tileY >= Constants.TERRAIN_SIZE)
            {
                sectorX = 0;
                sectorY = 0;
                return false;
            }

            sectorX = tileX / SpatialSectorTileSize;
            sectorY = tileY / SpatialSectorTileSize;

            if ((uint)sectorX >= SpatialSectorsPerAxis || (uint)sectorY >= SpatialSectorsPerAxis)
            {
                sectorX = 0;
                sectorY = 0;
                return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ResolveSpatialSector(WorldObject obj)
        {
            if (obj == null)
                return SpatialInvalidSector;

            return TryGetSpatialSector(obj.WorldPosition.Translation, out int sectorX, out int sectorY)
                ? PackSpatialSector(sectorX, sectorY)
                : SpatialInvalidSector;
        }

        private void AddToSpatialBucket(WorldObject obj, int sector)
        {
            if (sector == SpatialInvalidSector)
            {
                _spatialOffGridObjects.Add(obj);
                return;
            }

            int sectorX = sector % SpatialSectorsPerAxis;
            int sectorY = sector / SpatialSectorsPerAxis;
            _spatialSectors[sectorX, sectorY].Add(obj);
        }

        private void RemoveFromSpatialBucket(WorldObject obj, int sector)
        {
            if (sector == SpatialInvalidSector)
            {
                _spatialOffGridObjects.Remove(obj);
                return;
            }

            int sectorX = sector % SpatialSectorsPerAxis;
            int sectorY = sector / SpatialSectorsPerAxis;
            _spatialSectors[sectorX, sectorY].Remove(obj);
        }

        private void RegisterSpatialObject(WorldObject obj)
        {
            if (obj == null || _spatialObjectSectors.ContainsKey(obj))
                return;

            int sector = ResolveSpatialSector(obj);
            _spatialObjectSectors[obj] = sector;
            AddToSpatialBucket(obj, sector);
        }

        private void UnregisterSpatialObject(WorldObject obj)
        {
            if (obj == null || !_spatialObjectSectors.TryGetValue(obj, out int oldSector))
                return;

            RemoveFromSpatialBucket(obj, oldSector);
            _spatialObjectSectors.Remove(obj);
        }

        private void UpdateSpatialRegistration(WorldObject obj)
        {
            if (obj == null)
                return;

            if (obj.Status == GameControlStatus.Disposed || obj.Status == GameControlStatus.Error)
            {
                UnregisterSpatialObject(obj);
                return;
            }

            int newSector = ResolveSpatialSector(obj);
            if (!_spatialObjectSectors.TryGetValue(obj, out int oldSector))
            {
                _spatialObjectSectors[obj] = newSector;
                AddToSpatialBucket(obj, newSector);
                return;
            }

            if (oldSector == newSector)
                return;

            RemoveFromSpatialBucket(obj, oldSector);
            AddToSpatialBucket(obj, newSector);
            _spatialObjectSectors[obj] = newSector;
        }

        private void RebuildSpatialGridFromSnapshot()
        {
            foreach (var pair in _spatialSectors)
                pair.Clear();

            _spatialOffGridObjects.Clear();
            _spatialObjectSectors.Clear();

            var snapshot = Objects.GetSnapshot();
            for (int i = 0; i < snapshot.Count; i++)
            {
                var obj = snapshot[i];
                if (obj == null)
                    continue;

                int sector = ResolveSpatialSector(obj);
                _spatialObjectSectors[obj] = sector;
                AddToSpatialBucket(obj, sector);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddSpatialCandidate(WorldObject obj)
        {
            if (obj == null)
                return;

            if (_spatialCandidateSet.Add(obj))
                _spatialCandidates.Add(obj);
        }

        private void BuildSpatialCandidates(Vector2 center, float maxViewDistance)
        {
            _spatialCandidates.Clear();
            _spatialCandidateSet.Clear();

            if (!TryGetSpatialSector(new Vector3(center, 0f), out int centerSectorX, out int centerSectorY))
            {
                centerSectorX = Math.Clamp((int)MathF.Floor(center.X / Constants.TERRAIN_SCALE) / SpatialSectorTileSize, 0, SpatialSectorsPerAxis - 1);
                centerSectorY = Math.Clamp((int)MathF.Floor(center.Y / Constants.TERRAIN_SCALE) / SpatialSectorTileSize, 0, SpatialSectorsPerAxis - 1);
            }

            int sectorRadius = (int)MathF.Ceiling(maxViewDistance / (Constants.TERRAIN_SCALE * SpatialSectorTileSize)) + SpatialRebuildPaddingSectors;
            int minSectorX = Math.Max(0, centerSectorX - sectorRadius);
            int maxSectorX = Math.Min(SpatialSectorsPerAxis - 1, centerSectorX + sectorRadius);
            int minSectorY = Math.Max(0, centerSectorY - sectorRadius);
            int maxSectorY = Math.Min(SpatialSectorsPerAxis - 1, centerSectorY + sectorRadius);

            for (int sectorY = minSectorY; sectorY <= maxSectorY; sectorY++)
            {
                for (int sectorX = minSectorX; sectorX <= maxSectorX; sectorX++)
                {
                    var bucket = _spatialSectors[sectorX, sectorY];
                    for (int i = 0; i < bucket.Count; i++)
                        AddSpatialCandidate(bucket[i]);
                }
            }

            for (int i = 0; i < _spatialOffGridObjects.Count; i++)
                AddSpatialCandidate(_spatialOffGridObjects[i]);

            var snapshot = Objects.GetSnapshot();
            for (int i = 0; i < snapshot.Count; i++)
            {
                var obj = snapshot[i];
                if (ShouldForceVisible(obj))
                    AddSpatialCandidate(obj);
            }
        }

        private void RebuildVisibleObjects()
        {
            _cullingStopwatch.Restart();
            _visibleObjects.Clear();
            _visibleObjectSet.Clear();
            _visibleObjectIndices.Clear();
            _visibleObjectsNeedCompaction = false;
            _positionDirtyObjects.Clear();

            var camera = Camera.Instance;
            var camPos = camera.Position;
            var cam2 = new Vector2(camPos.X, camPos.Y);
            float maxViewDistance = camera.ViewFar + Constants.MAX_CAMERA_DISTANCE + 250f;
            float maxDistSq = maxViewDistance * maxViewDistance;
            var frustum = camera.Frustum;
            IReadOnlyList<WorldObject> snapshot;
            if (Constants.ENABLE_CROWD_SPATIAL_CULLING)
            {
                if (_spatialObjectSectors.Count != Objects.Count)
                {
                    RebuildSpatialGridFromSnapshot();
                }

                var focus = camera.Target;
                BuildSpatialCandidates(new Vector2(focus.X, focus.Y), maxViewDistance);
                snapshot = _spatialCandidates;
            }
            else
            {
                snapshot = Objects.GetSnapshot();
            }

            LastCullCandidateCount = snapshot.Count;
            bool useParallel = Environment.ProcessorCount > 1 &&
                               snapshot.Count >= ParallelVisibleRebuildThreshold;

            if (useParallel)
            {
                Parallel.For(
                    0,
                    snapshot.Count,
                    VisibleParallelOptions,
                    () => new List<WorldObject>(32),
                    (i, _, localVisible) =>
                    {
                        var obj = snapshot[i];
                        if (obj != null && obj.Visible &&
                            (ShouldForceVisible(obj) || IsObjectInView(obj, cam2, maxDistSq, frustum)))
                        {
                            localVisible.Add(obj);
                        }

                        return localVisible;
                    },
                    localVisible =>
                    {
                        if (localVisible.Count == 0)
                            return;

                        lock (_visibleMergeLock)
                        {
                            _visibleObjects.AddRange(localVisible);
                        }
                    });
            }
            else
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var obj = snapshot[i];
                    if (obj == null || !obj.Visible)
                        continue;

                    if (ShouldForceVisible(obj) || IsObjectInView(obj, cam2, maxDistSq, frustum))
                        _visibleObjects.Add(obj);
                }
            }

            for (int i = 0; i < _visibleObjects.Count; i++)
            {
                var obj = _visibleObjects[i];
                if (obj != null)
                {
                    _visibleObjectSet.Add(obj);
                    _visibleObjectIndices[obj] = i;
                }
            }

            _cullingStopwatch.Stop();
            _hasVisibilitySnapshot = true;
            LastCullVisibleCount = _visibleObjects.Count;
            LastCullRebuildMs = (float)_cullingStopwatch.Elapsed.TotalMilliseconds;
            LastCullWasRebuild = true;
            FrameMetrics.CullCandidates = LastCullCandidateCount;
            FrameMetrics.VisibleObjects = LastCullVisibleCount;
            FrameMetrics.CullMs = LastCullRebuildMs;
            FrameMetrics.CullWasRebuild = true;
        }

        private void RefreshDirtyVisibleObjects()
        {
            int dirtyCount = _positionDirtyObjects.Count;
            if (dirtyCount == 0)
                return;

            _cullingStopwatch.Restart();
            var camera = Camera.Instance;
            var camPos = camera.Position;
            var cam2 = new Vector2(camPos.X, camPos.Y);
            float maxViewDistance = camera.ViewFar + Constants.MAX_CAMERA_DISTANCE + 250f;
            float maxDistSq = maxViewDistance * maxViewDistance;
            var frustum = camera.Frustum;

            EnsureDirtyVisibilityScratchCapacity(dirtyCount);
            _positionDirtyObjects.CopyTo(_dirtyVisibilityScratch);
            _positionDirtyObjects.Clear();

            for (int i = 0; i < dirtyCount; i++)
                UpdateSpatialRegistration(_dirtyVisibilityScratch[i]);

            bool useParallel = Environment.ProcessorCount > 2 &&
                               dirtyCount >= ParallelDirtyRefreshThreshold;

            if (useParallel)
            {
                _pendingVisibleAdd.Clear();
                _pendingVisibleRemove.Clear();
                if (_pendingVisibleAdd.Capacity < dirtyCount) _pendingVisibleAdd.Capacity = dirtyCount;
                if (_pendingVisibleRemove.Capacity < dirtyCount) _pendingVisibleRemove.Capacity = dirtyCount;

                Parallel.For(
                    0,
                    dirtyCount,
                    VisibleParallelOptions,
                    () => (add: new List<WorldObject>(32), remove: new List<WorldObject>(32)),
                    (i, _, local) =>
                    {
                        var obj = _dirtyVisibilityScratch[i];
                        if (obj == null || !obj.Visible)
                        {
                            if (obj != null)
                                local.remove.Add(obj);
                            return local;
                        }

                        bool inView = ShouldForceVisible(obj) || IsObjectInView(obj, cam2, maxDistSq, frustum);
                        if (inView)
                            local.add.Add(obj);
                        else
                            local.remove.Add(obj);

                        return local;
                    },
                    local =>
                    {
                        if (local.add.Count == 0 && local.remove.Count == 0)
                            return;

                        lock (_visibleMergeLock)
                        {
                            _pendingVisibleAdd.AddRange(local.add);
                            _pendingVisibleRemove.AddRange(local.remove);
                        }
                    });

                for (int i = 0; i < _pendingVisibleRemove.Count; i++)
                    RemoveVisibleObject(_pendingVisibleRemove[i]);

                for (int i = 0; i < _pendingVisibleAdd.Count; i++)
                    AddVisibleObject(_pendingVisibleAdd[i]);
            }
            else
            {
                for (int i = 0; i < dirtyCount; i++)
                {
                    var obj = _dirtyVisibilityScratch[i];
                    if (obj == null || !obj.Visible)
                    {
                        if (obj != null)
                            RemoveVisibleObject(obj);
                        continue;
                    }

                    bool inView = ShouldForceVisible(obj) || IsObjectInView(obj, cam2, maxDistSq, frustum);
                    if (inView)
                        AddVisibleObject(obj);
                    else
                        RemoveVisibleObject(obj);
                }
            }

            Array.Clear(_dirtyVisibilityScratch, 0, dirtyCount);
            _cullingStopwatch.Stop();
            LastCullCandidateCount = dirtyCount;
            LastCullVisibleCount = _visibleObjects.Count;
            LastCullRebuildMs = (float)_cullingStopwatch.Elapsed.TotalMilliseconds;
            LastCullWasRebuild = false;
            FrameMetrics.CullCandidates = LastCullCandidateCount;
            FrameMetrics.VisibleObjects = LastCullVisibleCount;
            FrameMetrics.CullMs = LastCullRebuildMs;
            FrameMetrics.CullWasRebuild = false;
        }

        private void EnsureDirtyVisibilityScratchCapacity(int required)
        {
            if (_dirtyVisibilityScratch.Length >= required)
                return;

            int capacity = 256;
            while (capacity < required)
                capacity <<= 1;

            _dirtyVisibilityScratch = new WorldObject[capacity];
        }

        private void UpdateVisibleObjects(GameTime time)
        {
            var objects = _visibleObjects;
            if (objects.Count == 0)
                return;

            var camera = Camera.Instance;
            var camPos = camera.Position;
            float camX = camPos.X;
            float camY = camPos.Y;
            int frame = MuGame.FrameIndex;

            _isUpdatingVisibleObjects = true;
            try
            {
                for (int i = objects.Count - 1; i >= 0; i--)
                {
                    var obj = objects[i];
                    if (obj == null || !obj.Visible)
                        continue;

                    int stride = 1;
                    if (!ShouldAlwaysUpdate(obj))
                    {
                        var position = obj.WorldPosition.Translation;
                        float dx = camX - position.X;
                        float dy = camY - position.Y;
                        float distanceSquared = dx * dx + dy * dy;
                        stride = Constants.ENABLE_ANIMATION_THROTTLING
                            ? ResolveUpdateStride(distanceSquared)
                            : 1;
                    }

                    bool lowQuality = stride > 1;
                    obj.SetLowQuality(lowQuality);
                    if (obj is ModelObject model)
                        model.SetAnimationUpdateStride(stride);

                    if (lowQuality)
                    {
                        FrameMetrics.LowQualityObjects++;
                        if (((frame + obj.UpdateOffset) % stride) != 0)
                            FrameMetrics.AnimationSkips++;
                        else
                            FrameMetrics.AnimationUpdates++;
                    }
                    else
                    {
                        FrameMetrics.AnimationUpdates++;
                    }

                    // Movement, network interpolation and gameplay state still update every
                    // frame. ModelObject throttles only its expensive bone-pose calculation.
                    obj.Update(time);
                }
            }
            finally
            {
                _isUpdatingVisibleObjects = false;
                CompactVisibleObjects();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldAlwaysUpdate(WorldObject obj)
        {
            if (obj == null)
                return false;

            if (obj.RenderPolicy.AlwaysUpdate)
                return true;

            return (obj.Interactive && obj is not MonsterObject)
                || obj is DroppedItemObject
                || (obj is MonsterObject monster && monster.IsOneShotPlaying);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ResolveUpdateStride(float distSq)
        {
            if (distSq <= NearUpdateDistanceSq) return 1;
            if (distSq <= MidUpdateDistanceSq) return 2;
            if (distSq <= FarUpdateDistanceSq) return 4;
            return 6;
        }


        private bool HasSignificantCameraCullChange(Camera camera)
        {
            if (!_hasVisibilitySnapshot || camera == null)
                return true;

            float moveThresholdSq = CameraCullMoveThreshold * CameraCullMoveThreshold;
            if (Vector3.DistanceSquared(camera.Position, _lastCulledCameraPosition) >= moveThresholdSq)
                return true;

            Vector3 direction = camera.Target - camera.Position;
            if (direction.LengthSquared() > 1e-8f)
                direction.Normalize();
            else
                direction = Vector3.UnitY;

            if (Vector3.Dot(direction, _lastCulledCameraDirection) < CameraCullDirectionDotThreshold)
                return true;

            return MathF.Abs(camera.ViewFar - _lastCulledViewFar) > 0.01f ||
                   MathF.Abs(camera.FOV - _lastCulledFov) > 0.001f ||
                   MathF.Abs(camera.AspectRatio - _lastCulledAspectRatio) > 0.0001f;
        }

        private void CaptureCulledCameraState(Camera camera, ulong cameraVersion)
        {
            _lastCulledCameraVersion = cameraVersion;
            _lastCulledCameraPosition = camera.Position;

            Vector3 direction = camera.Target - camera.Position;
            if (direction.LengthSquared() > 1e-8f)
                direction.Normalize();
            else
                direction = Vector3.UnitY;

            _lastCulledCameraDirection = direction;
            _lastCulledViewFar = camera.ViewFar;
            _lastCulledFov = camera.FOV;
            _lastCulledAspectRatio = camera.AspectRatio;
        }

        internal void CollectObjectsNear(Vector3 center, float radius, List<WorldObject> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();
            float safeRadius = MathF.Max(0f, radius);

            if (!Constants.ENABLE_CROWD_SPATIAL_CULLING)
            {
                var snapshot = Objects.GetSnapshot();
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var obj = snapshot[i];
                    if (obj != null)
                        destination.Add(obj);
                }
                return;
            }

            if (_spatialObjectSectors.Count != Objects.Count)
                RebuildSpatialGridFromSnapshot();

            int centerTileX = (int)MathF.Floor(center.X / Constants.TERRAIN_SCALE);
            int centerTileY = (int)MathF.Floor(center.Y / Constants.TERRAIN_SCALE);
            int centerSectorX = Math.Clamp(centerTileX / SpatialSectorTileSize, 0, SpatialSectorsPerAxis - 1);
            int centerSectorY = Math.Clamp(centerTileY / SpatialSectorTileSize, 0, SpatialSectorsPerAxis - 1);
            int sectorRadius = (int)MathF.Ceiling(safeRadius / (Constants.TERRAIN_SCALE * SpatialSectorTileSize)) + 1;

            int minX = Math.Max(0, centerSectorX - sectorRadius);
            int maxX = Math.Min(SpatialSectorsPerAxis - 1, centerSectorX + sectorRadius);
            int minY = Math.Max(0, centerSectorY - sectorRadius);
            int maxY = Math.Min(SpatialSectorsPerAxis - 1, centerSectorY + sectorRadius);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var bucket = _spatialSectors[x, y];
                    for (int i = 0; i < bucket.Count; i++)
                        destination.Add(bucket[i]);
                }
            }

            for (int i = 0; i < _spatialOffGridObjects.Count; i++)
                destination.Add(_spatialOffGridObjects[i]);
        }


        // --- Map Tile Initialization ---

        protected virtual void CreateMapTileObjects()
        {
            var defaultType = typeof(MapTileObject);
            for (int i = 0; i < MapTileObjects.Length; i++)
                MapTileObjects[i] = defaultType;
        }

        // --- Disposal ---

        public override void Dispose()
        {
            var sw = Stopwatch.StartNew();

            // Dispose can occur after objects were queued for a later instanced flush. Clear
            // those per-frame queues before releasing world objects to prevent one-frame ghosts
            // or persistent stale batches after a teleport.
            ModelObject.ResetWorldScopedInstancingState();

            // Dispose and remove all objects except the local player
            foreach (var obj in Objects.ToArray())
            {
                if (this is WalkableWorldControl wc &&
                    obj is PlayerObject player &&
                    wc.Walker == player)
                    continue;

                RemoveObject(obj);
                obj.Dispose();
            }

            Objects.Clear();
            WalkerObjectsById.Clear();
            _walkers.Clear();
            _players.Clear();
            _monsters.Clear();
            _droppedItems.Clear();
            _objectsToInitialize.Clear();
            _queuedForInitialization.Clear();
            _visibleObjectSet.Clear();
            _visibleObjectIndices.Clear();
            _visibleObjectsNeedCompaction = false;
            _isUpdatingVisibleObjects = false;
            _positionDirtyObjects.Clear();
            _spatialObjectSectors.Clear();
            _spatialOffGridObjects.Clear();
            _spatialCandidates.Clear();
            _spatialCandidateSet.Clear();
            _hasVisibilitySnapshot = false;
            foreach (var bucket in _spatialSectors)
                bucket.Clear();

            sw.Stop();
            var elapsedObjects = sw.ElapsedMilliseconds;
            sw.Restart();

            base.Dispose();

            sw.Stop();
            var elapsedBase = sw.ElapsedMilliseconds;
            _logger?.LogDebug($"Dispose WorldControl {WorldIndex} - Objects: {elapsedObjects}ms, Base: {elapsedBase}ms");
        }

        public void OnWorldObjectStatusChanged(WorldObject worldObject)
        {
            if (worldObject.Status == GameControlStatus.NonInitialized)
            {
                EnqueueObjectInitialization(worldObject);
                return;
            }

            if (worldObject.Status == GameControlStatus.Ready)
            {
                _positionDirtyObjects.Add(worldObject);
                UpdateSpatialRegistration(worldObject);
                return;
            }

            if (worldObject.Status == GameControlStatus.Disposed || worldObject.Status == GameControlStatus.Error)
            {
                RemoveVisibleObject(worldObject);
                _positionDirtyObjects.Remove(worldObject);
                UnregisterSpatialObject(worldObject);
            }
        }
    }
}
