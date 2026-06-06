using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Controls;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Client.Main.Objects
{
    public abstract partial class ModelObject
    {
        internal enum StaticMapInstancingQueueResult
        {
            None,
            Partial,
            Full,
        }

        private readonly struct StaticMapInstancingBatchKey : IEquatable<StaticMapInstancingBatchKey>
        {
            public StaticMapInstancingBatchKey(BMD model, int meshIndex, Texture2D texture, bool twoSided)
            {
                Model = model;
                MeshIndex = meshIndex;
                Texture = texture;
                TwoSided = twoSided;
            }

            public BMD Model { get; }
            public int MeshIndex { get; }
            public Texture2D Texture { get; }
            public bool TwoSided { get; }

            public bool Equals(StaticMapInstancingBatchKey other)
            {
                return ReferenceEquals(Model, other.Model)
                    && MeshIndex == other.MeshIndex
                    && ReferenceEquals(Texture, other.Texture)
                    && TwoSided == other.TwoSided;
            }

            public override bool Equals(object obj) => obj is StaticMapInstancingBatchKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = (hash * 31) + RuntimeHelpers.GetHashCode(Model);
                    hash = (hash * 31) + MeshIndex;
                    hash = (hash * 31) + RuntimeHelpers.GetHashCode(Texture);
                    hash = (hash * 31) + (TwoSided ? 1 : 0);
                    return hash;
                }
            }
        }

        private readonly struct MonsterCrowdInstancingBatchKey : IEquatable<MonsterCrowdInstancingBatchKey>
        {
            public MonsterCrowdInstancingBatchKey(
                BMD model,
                int meshIndex,
                Texture2D texture,
                bool twoSided,
                int actionIndex,
                int frame0,
                int frame1,
                int interpolationBucket)
            {
                Model = model;
                MeshIndex = meshIndex;
                Texture = texture;
                TwoSided = twoSided;
                ActionIndex = actionIndex;
                Frame0 = frame0;
                Frame1 = frame1;
                InterpolationBucket = interpolationBucket;
            }

            public BMD Model { get; }
            public int MeshIndex { get; }
            public Texture2D Texture { get; }
            public bool TwoSided { get; }
            public int ActionIndex { get; }
            public int Frame0 { get; }
            public int Frame1 { get; }
            public int InterpolationBucket { get; }

            public bool Equals(MonsterCrowdInstancingBatchKey other)
            {
                return ReferenceEquals(Model, other.Model)
                    && MeshIndex == other.MeshIndex
                    && ReferenceEquals(Texture, other.Texture)
                    && TwoSided == other.TwoSided
                    && ActionIndex == other.ActionIndex
                    && Frame0 == other.Frame0
                    && Frame1 == other.Frame1
                    && InterpolationBucket == other.InterpolationBucket;
            }

            public override bool Equals(object obj) => obj is MonsterCrowdInstancingBatchKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = (hash * 31) + RuntimeHelpers.GetHashCode(Model);
                    hash = (hash * 31) + MeshIndex;
                    hash = (hash * 31) + RuntimeHelpers.GetHashCode(Texture);
                    hash = (hash * 31) + (TwoSided ? 1 : 0);
                    hash = (hash * 31) + ActionIndex;
                    hash = (hash * 31) + Frame0;
                    hash = (hash * 31) + Frame1;
                    hash = (hash * 31) + InterpolationBucket;
                    return hash;
                }
            }
        }

        private sealed class StaticMapInstancingBatch : IDisposable
        {
            public VertexBuffer GeometryVertexBuffer;
            public IndexBuffer GeometryIndexBuffer;
            public int PrimitiveCount;
            public int BoneCount;
            public bool TwoSided;
            public Texture2D Texture;
            public ModelObject PoseSource;
            public readonly List<StaticModelInstanceData> Instances = new List<StaticModelInstanceData>(64);
            public DynamicVertexBuffer InstanceBuffer;
            public int InstanceBufferCapacity;
            public StaticModelInstanceData[] UploadBuffer = Array.Empty<StaticModelInstanceData>();
            public readonly VertexBufferBinding[] VertexBindings = new VertexBufferBinding[2];

            public void Dispose()
            {
                InstanceBuffer?.Dispose();
                InstanceBuffer = null;
                InstanceBufferCapacity = 0;
                UploadBuffer = Array.Empty<StaticModelInstanceData>();
                Instances.Clear();
            }
        }

        private sealed class MonsterCrowdInstancingBatch : IDisposable
        {
            public VertexBuffer GeometryVertexBuffer;
            public IndexBuffer GeometryIndexBuffer;
            public int PrimitiveCount;
            public int BoneCount;
            public bool TwoSided;
            public Texture2D Texture;
            public ModelObject PoseSource;
            public readonly List<StaticModelInstanceData> Instances = new List<StaticModelInstanceData>(64);
            public DynamicVertexBuffer InstanceBuffer;
            public int InstanceBufferCapacity;
            public StaticModelInstanceData[] UploadBuffer = Array.Empty<StaticModelInstanceData>();
            public readonly VertexBufferBinding[] VertexBindings = new VertexBufferBinding[2];

            public void Dispose()
            {
                InstanceBuffer?.Dispose();
                InstanceBuffer = null;
                InstanceBufferCapacity = 0;
                UploadBuffer = Array.Empty<StaticModelInstanceData>();
                Instances.Clear();
            }
        }

        private static readonly Dictionary<StaticMapInstancingBatchKey, StaticMapInstancingBatch> _staticMapInstancingBatches = new Dictionary<StaticMapInstancingBatchKey, StaticMapInstancingBatch>(128);
        private static readonly List<StaticMapInstancingBatch> _staticMapInstancingActiveBatches = new List<StaticMapInstancingBatch>(128);
        private static readonly DynamicLightGpuUploader _staticInstancingLightUploader = new(32);
        private static readonly Dictionary<MonsterCrowdInstancingBatchKey, MonsterCrowdInstancingBatch> _monsterCrowdInstancingBatches = new Dictionary<MonsterCrowdInstancingBatchKey, MonsterCrowdInstancingBatch>(128);
        private static readonly List<MonsterCrowdInstancingBatch> _monsterCrowdInstancingActiveBatches = new List<MonsterCrowdInstancingBatch>(128);
        private static bool _staticMapInstancingFailed = false;
        private static EffectTechnique _cachedStaticMapInstancingTechnique;
        private static readonly Matrix _identity = Matrix.Identity;

        private static int _staticMapInstancedObjectsThisFrame = 0;
        private static int _staticMapInstancedMeshInstancesThisFrame = 0;
        private static int _staticMapInstancedBatchesThisFrame = 0;
        private static int _staticMapInstancedDrawCallsThisFrame = 0;
        private static int _staticMapInstancingFallbacksThisFrame = 0;

        public static int LastFrameStaticMapInstancedObjects { get; private set; }
        public static int LastFrameStaticMapInstancedMeshInstances { get; private set; }
        public static int LastFrameStaticMapInstancedBatches { get; private set; }
        public static int LastFrameStaticMapInstancedDrawCalls { get; private set; }
        public static int LastFrameStaticMapInstancingFallbacks { get; private set; }
        public static bool IsStaticMapInstancingBackendSupported => SupportsGpuDynamicSkinning;
        public static bool IsStaticMapInstancingRuntimeDisabled => _staticMapInstancingFailed;

        private static void BeginFrameStaticMapInstancingMetrics()
        {
            LastFrameStaticMapInstancedObjects = _staticMapInstancedObjectsThisFrame;
            LastFrameStaticMapInstancedMeshInstances = _staticMapInstancedMeshInstancesThisFrame;
            LastFrameStaticMapInstancedBatches = _staticMapInstancedBatchesThisFrame;
            LastFrameStaticMapInstancedDrawCalls = _staticMapInstancedDrawCallsThisFrame;
            LastFrameStaticMapInstancingFallbacks = _staticMapInstancingFallbacksThisFrame;

            _staticMapInstancedObjectsThisFrame = 0;
            _staticMapInstancedMeshInstancesThisFrame = 0;
            _staticMapInstancedBatchesThisFrame = 0;
            _staticMapInstancedDrawCallsThisFrame = 0;
            _staticMapInstancingFallbacksThisFrame = 0;
        }

        internal static void RegisterStaticMapInstancingFallback()
        {
            _staticMapInstancingFallbacksThisFrame++;
        }

        internal static bool IsStaticMapInstancingPathAvailable()
        {
            return IsStaticMapInstancingSupported();
        }

        internal static StaticMapInstancingQueueResult TryQueueStaticMapObjectForInstancing(WorldObject obj)
        {
            if (obj is not ModelObject modelObject)
                return StaticMapInstancingQueueResult.None;

            return modelObject.TryQueueStaticMapObjectForInstancing();
        }

        internal static bool TryQueueMonsterCrowdForInstancing(WorldObject obj)
        {
            if (obj is not ModelObject modelObject)
                return false;

            return modelObject.TryQueueMonsterCrowdForInstancing();
        }

        internal static void FlushStaticMapInstancingBatches(WorldControl world)
        {
            if (_staticMapInstancingActiveBatches.Count == 0)
                return;

            if (_staticMapInstancingFailed || !IsStaticMapInstancingSupported())
            {
                ClearStaticMapInstancingQueues();
                return;
            }

            var graphicsManager = GraphicsManager.Instance;
            var effect = graphicsManager.DynamicLightingEffect;
            if (effect == null || _cachedStaticMapInstancingTechnique == null)
            {
                ClearStaticMapInstancingQueues();
                return;
            }

            var gd = graphicsManager.GraphicsDevice;
            var prevBlend = gd.BlendState;
            var prevRaster = gd.RasterizerState;
            var prevSampler = gd.SamplerStates[0];

            try
            {
                PrepareStaticMapInstancingEffect(effect, world);

                gd.BlendState = BlendState.Opaque;
                gd.SamplerStates[0] = GraphicsManager.GetQualityLinearSamplerState();

                for (int i = 0; i < _staticMapInstancingActiveBatches.Count; i++)
                {
                    var batch = _staticMapInstancingActiveBatches[i];
                    int instanceCount = batch.Instances.Count;
                    if (instanceCount <= 0 ||
                        batch.GeometryVertexBuffer == null ||
                        batch.GeometryIndexBuffer == null ||
                        batch.Texture == null ||
                        batch.PoseSource == null)
                    {
                        continue;
                    }

                    if (!batch.PoseSource.TryUploadGpuSkinBoneMatrices(effect, batch.BoneCount))
                        continue;

                    EnsureInstanceUploadBuffer(batch, instanceCount);
                    for (int j = 0; j < instanceCount; j++)
                        batch.UploadBuffer[j] = batch.Instances[j];

                    EnsureInstanceVertexBuffer(gd, batch, instanceCount);
                    batch.InstanceBuffer.SetData(batch.UploadBuffer, 0, instanceCount, SetDataOptions.Discard);

                    gd.RasterizerState = batch.TwoSided ? RasterizerState.CullNone : RasterizerState.CullClockwise;
                    effect.Parameters["DiffuseTexture"]?.SetValue(batch.Texture);

                    batch.VertexBindings[0] = new VertexBufferBinding(batch.GeometryVertexBuffer);
                    batch.VertexBindings[1] = new VertexBufferBinding(batch.InstanceBuffer, 0, 1);
                    gd.SetVertexBuffers(batch.VertexBindings);
                    gd.Indices = batch.GeometryIndexBuffer;

                    _staticMapInstancedBatchesThisFrame++;
                    int passCount = effect.CurrentTechnique.Passes.Count;
                    for (int p = 0; p < passCount; p++)
                    {
                        effect.CurrentTechnique.Passes[p].Apply();
                        _staticMapInstancedDrawCallsThisFrame++;
                        gd.DrawInstancedPrimitives(
                            PrimitiveType.TriangleList,
                            0,
                            0,
                            batch.PrimitiveCount,
                            instanceCount);
                    }
                }
            }
            catch (Exception ex)
            {
                _staticMapInstancingFailed = true;
                MuGame.AppLoggerFactory?.CreateLogger<ModelObject>()?.LogWarning(ex, "Static map hardware instancing disabled after runtime failure.");
            }
            finally
            {
                gd.BlendState = prevBlend;
                gd.RasterizerState = prevRaster;
                gd.SamplerStates[0] = prevSampler;
                ClearStaticMapInstancingQueues();
            }
        }

        internal static void FlushMonsterCrowdInstancingBatches(WorldControl world)
        {
            if (_monsterCrowdInstancingActiveBatches.Count == 0)
                return;

            if (_staticMapInstancingFailed || !IsMonsterCrowdInstancingSupported())
            {
                ClearMonsterCrowdInstancingQueues();
                return;
            }

            var graphicsManager = GraphicsManager.Instance;
            var effect = graphicsManager.DynamicLightingEffect;
            if (effect == null || _cachedStaticMapInstancingTechnique == null)
            {
                ClearMonsterCrowdInstancingQueues();
                return;
            }

            var gd = graphicsManager.GraphicsDevice;
            var prevBlend = gd.BlendState;
            var prevRaster = gd.RasterizerState;
            var prevSampler = gd.SamplerStates[0];

            try
            {
                PrepareStaticMapInstancingEffect(effect, world);

                gd.BlendState = BlendState.Opaque;
                gd.SamplerStates[0] = GraphicsManager.GetQualityLinearSamplerState();

                for (int i = 0; i < _monsterCrowdInstancingActiveBatches.Count; i++)
                {
                    var batch = _monsterCrowdInstancingActiveBatches[i];
                    int instanceCount = batch.Instances.Count;
                    if (instanceCount <= 0 ||
                        batch.GeometryVertexBuffer == null ||
                        batch.GeometryIndexBuffer == null ||
                        batch.Texture == null ||
                        batch.PoseSource == null)
                    {
                        continue;
                    }

                    if (!batch.PoseSource.TryUploadGpuSkinBoneMatrices(effect, batch.BoneCount))
                        continue;

                    EnsureInstanceUploadBuffer(batch, instanceCount);
                    for (int j = 0; j < instanceCount; j++)
                        batch.UploadBuffer[j] = batch.Instances[j];

                    EnsureInstanceVertexBuffer(gd, batch, instanceCount);
                    batch.InstanceBuffer.SetData(batch.UploadBuffer, 0, instanceCount, SetDataOptions.Discard);

                    gd.RasterizerState = batch.TwoSided ? RasterizerState.CullNone : RasterizerState.CullClockwise;
                    effect.Parameters["DiffuseTexture"]?.SetValue(batch.Texture);

                    batch.VertexBindings[0] = new VertexBufferBinding(batch.GeometryVertexBuffer);
                    batch.VertexBindings[1] = new VertexBufferBinding(batch.InstanceBuffer, 0, 1);
                    gd.SetVertexBuffers(batch.VertexBindings);
                    gd.Indices = batch.GeometryIndexBuffer;

                    int passCount = effect.CurrentTechnique.Passes.Count;
                    for (int p = 0; p < passCount; p++)
                    {
                        effect.CurrentTechnique.Passes[p].Apply();
                        gd.DrawInstancedPrimitives(
                            PrimitiveType.TriangleList,
                            0,
                            0,
                            batch.PrimitiveCount,
                            instanceCount);
                    }
                }
            }
            catch (Exception ex)
            {
                _staticMapInstancingFailed = true;
                MuGame.AppLoggerFactory?.CreateLogger<ModelObject>()?.LogWarning(ex, "Monster crowd hardware instancing disabled after runtime failure.");
            }
            finally
            {
                gd.BlendState = prevBlend;
                gd.RasterizerState = prevRaster;
                gd.SamplerStates[0] = prevSampler;
                ClearMonsterCrowdInstancingQueues();
            }
        }

        internal static bool HasPendingStaticMapInstancingBatches() => _staticMapInstancingActiveBatches.Count > 0;
        internal static bool HasPendingMonsterCrowdInstancingBatches() => _monsterCrowdInstancingActiveBatches.Count > 0;

        private static void EnsureInstanceUploadBuffer(StaticMapInstancingBatch batch, int instanceCount)
        {
            if (batch.UploadBuffer.Length >= instanceCount)
                return;

            int newSize = Math.Max(instanceCount, batch.UploadBuffer.Length == 0 ? 64 : batch.UploadBuffer.Length * 2);
            batch.UploadBuffer = new StaticModelInstanceData[newSize];
        }

        private static void EnsureInstanceVertexBuffer(GraphicsDevice gd, StaticMapInstancingBatch batch, int instanceCount)
        {
            if (batch.InstanceBuffer != null &&
                !batch.InstanceBuffer.IsDisposed &&
                batch.InstanceBufferCapacity >= instanceCount)
            {
                return;
            }

            batch.InstanceBuffer?.Dispose();
            int capacity = Math.Max(instanceCount, 64);
            batch.InstanceBuffer = new DynamicVertexBuffer(
                gd,
                StaticModelInstanceData.VertexDeclaration,
                capacity,
                BufferUsage.WriteOnly);
            batch.InstanceBufferCapacity = capacity;
        }

        private static void EnsureInstanceUploadBuffer(MonsterCrowdInstancingBatch batch, int instanceCount)
        {
            if (batch.UploadBuffer.Length >= instanceCount)
                return;

            int newSize = Math.Max(instanceCount, batch.UploadBuffer.Length == 0 ? 64 : batch.UploadBuffer.Length * 2);
            batch.UploadBuffer = new StaticModelInstanceData[newSize];
        }

        private static void EnsureInstanceVertexBuffer(GraphicsDevice gd, MonsterCrowdInstancingBatch batch, int instanceCount)
        {
            if (batch.InstanceBuffer != null &&
                !batch.InstanceBuffer.IsDisposed &&
                batch.InstanceBufferCapacity >= instanceCount)
            {
                return;
            }

            batch.InstanceBuffer?.Dispose();
            int capacity = Math.Max(instanceCount, 64);
            batch.InstanceBuffer = new DynamicVertexBuffer(
                gd,
                StaticModelInstanceData.VertexDeclaration,
                capacity,
                BufferUsage.WriteOnly);
            batch.InstanceBufferCapacity = capacity;
        }

        private static void ClearStaticMapInstancingQueues()
        {
            for (int i = 0; i < _staticMapInstancingActiveBatches.Count; i++)
                _staticMapInstancingActiveBatches[i].Instances.Clear();

            _staticMapInstancingActiveBatches.Clear();
        }

        private static void ClearMonsterCrowdInstancingQueues()
        {
            for (int i = 0; i < _monsterCrowdInstancingActiveBatches.Count; i++)
                _monsterCrowdInstancingActiveBatches[i].Instances.Clear();

            _monsterCrowdInstancingActiveBatches.Clear();
        }

        private static bool IsStaticMapInstancingSupported()
        {
            if (_staticMapInstancingFailed ||
                !Constants.ENABLE_MAP_OBJECT_INSTANCING ||
                !SupportsGpuDynamicSkinning)
            {
                return false;
            }

            var effect = GraphicsManager.Instance.DynamicLightingEffect;
            if (effect == null)
                return false;

            _cachedStaticMapInstancingTechnique ??= TryGetTechnique(effect, "DynamicLighting_SkinnedInstanced");
            return _cachedStaticMapInstancingTechnique != null;
        }

        private static bool IsMonsterCrowdInstancingSupported()
        {
            if (_staticMapInstancingFailed ||
                !Constants.ENABLE_MONSTER_CROWD_INSTANCING ||
                !Constants.ENABLE_GPU_SKINNING ||
                !SupportsGpuDynamicSkinning)
            {
                return false;
            }

            var effect = GraphicsManager.Instance.DynamicLightingEffect;
            if (effect == null)
                return false;

            _cachedStaticMapInstancingTechnique ??= TryGetTechnique(effect, "DynamicLighting_SkinnedInstanced");
            return _cachedStaticMapInstancingTechnique != null;
        }

        private StaticMapInstancingQueueResult TryQueueStaticMapObjectForInstancing()
        {
            if (!CanUseStaticMapInstancing())
                return StaticMapInstancingQueueResult.None;

            if (Model?.Meshes == null || _boneTextures == null)
                return StaticMapInstancingQueueResult.None;

            int meshCount = Model.Meshes.Length;
            EnsureStaticMapInstancingFrameTags(meshCount);
            int instancingFrameTag = MuGame.FrameIndex + 1;
            int opaqueMeshCount = 0;
            int queuedOpaqueMeshCount = 0;
            byte alpha = (byte)MathHelper.Clamp(TotalAlpha * 255f, 0f, 255f);
            var instanceData = new StaticModelInstanceData(WorldPosition, new Color((byte)255, (byte)255, (byte)255, alpha));

            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                if (!ShouldQueueStaticMapMesh(meshIndex))
                    continue;

                opaqueMeshCount++;

                if (!CanUseStaticMapMeshForInstancing(meshIndex))
                    continue;

                if (!BMDLoader.Instance.TryGetGpuSkinnedMeshBuffers(
                    Model,
                    meshIndex,
                    out var geometryVB,
                    out var geometryIB,
                    out var boneCount))
                {
                    continue;
                }

                bool twoSided = IsMeshTwoSided(meshIndex, false);
                Texture2D texture = _boneTextures[meshIndex];
                var key = new StaticMapInstancingBatchKey(Model, meshIndex, texture, twoSided);
                if (!_staticMapInstancingBatches.TryGetValue(key, out var batch))
                {
                    batch = new StaticMapInstancingBatch();
                    _staticMapInstancingBatches[key] = batch;
                }

                batch.GeometryVertexBuffer = geometryVB;
                batch.GeometryIndexBuffer = geometryIB;
                batch.PrimitiveCount = geometryIB.IndexCount / 3;
                batch.BoneCount = boneCount;
                batch.TwoSided = twoSided;
                batch.Texture = texture;

                if (batch.PoseSource == null || !ReferenceEquals(batch.PoseSource.Model, Model))
                    batch.PoseSource = this;

                if (batch.Instances.Count == 0)
                    _staticMapInstancingActiveBatches.Add(batch);

                batch.Instances.Add(instanceData);
                _staticMapInstancedMeshFrameTags[meshIndex] = instancingFrameTag;
                _staticMapInstancedMeshInstancesThisFrame++;
                queuedOpaqueMeshCount++;
            }

            if (opaqueMeshCount == 0)
                return StaticMapInstancingQueueResult.None;

            if (queuedOpaqueMeshCount == 0)
                return StaticMapInstancingQueueResult.None;

            _staticMapInstancedObjectsThisFrame++;
            return queuedOpaqueMeshCount == opaqueMeshCount
                ? StaticMapInstancingQueueResult.Full
                : StaticMapInstancingQueueResult.Partial;
        }

        private bool TryQueueMonsterCrowdForInstancing()
        {
            if (!CanUseMonsterCrowdInstancing())
                return false;

            if (Model?.Meshes == null || _boneTextures == null)
                return false;

            int meshCount = Model.Meshes.Length;
            var instanceData = new StaticModelInstanceData(WorldPosition, GetCrowdInstancingBodyColor());
            bool queuedAnyMesh = false;

            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                if (!ShouldQueueMonsterCrowdMesh(meshIndex))
                    continue;

                if (!CanUseMonsterCrowdMeshForInstancing(meshIndex))
                    return false;

                queuedAnyMesh = true;
            }

            if (!queuedAnyMesh)
                return false;

            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                if (!ShouldQueueMonsterCrowdMesh(meshIndex))
                    continue;

                if (!BMDLoader.Instance.TryGetGpuSkinnedMeshBuffers(
                    Model,
                    meshIndex,
                    out _,
                    out _,
                    out _))
                {
                    return false;
                }
            }

            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                if (!ShouldQueueMonsterCrowdMesh(meshIndex))
                    continue;

                if (!BMDLoader.Instance.TryGetGpuSkinnedMeshBuffers(
                    Model,
                    meshIndex,
                    out var geometryVB,
                    out var geometryIB,
                    out var boneCount))
                {
                    return false;
                }

                bool twoSided = IsMeshTwoSided(meshIndex, false);
                Texture2D texture = _boneTextures[meshIndex];
                var key = new MonsterCrowdInstancingBatchKey(
                    Model,
                    meshIndex,
                    texture,
                    twoSided,
                    _animationSampleActionIndex,
                    _animationSampleFrame0,
                    _animationSampleFrame1,
                    _animationSampleInterpolationBucket);

                if (!_monsterCrowdInstancingBatches.TryGetValue(key, out var batch))
                {
                    batch = new MonsterCrowdInstancingBatch();
                    _monsterCrowdInstancingBatches[key] = batch;
                }

                batch.GeometryVertexBuffer = geometryVB;
                batch.GeometryIndexBuffer = geometryIB;
                batch.PrimitiveCount = geometryIB.IndexCount / 3;
                batch.BoneCount = boneCount;
                batch.TwoSided = twoSided;
                batch.Texture = texture;

                if (batch.Instances.Count == 0)
                {
                    batch.PoseSource = this;
                    _monsterCrowdInstancingActiveBatches.Add(batch);
                }

                batch.Instances.Add(instanceData);
            }

            return true;
        }

        private bool CanUseStaticMapInstancing()
        {
            if (!IsStaticMapInstancingSupported())
                return false;

            if (!IsMapPlacementObject || !AllowMapObjectInstancing)
                return false;

            if (UsesMutableMeshData)
                return false;

            if (HasAnimatedCurrentAction())
                return false;

            if (!Visible || Children.Count > 0 || Model?.Meshes == null || Model.Meshes.Length == 0)
                return false;

            if (LinkParentAnimation || ParentBoneLink >= 0 || RequiresPerFrameAnimation || ContinuousAnimation)
                return false;

            if (TotalAlpha < 0.999f)
                return false;

            if (HasVisibleTransparentMapMesh())
                return false;

            return true;
        }

        private bool CanUseMonsterCrowdInstancing()
        {
            if (!IsMonsterCrowdInstancingSupported())
                return false;

            // Allow any WalkerObject crowd member. Player characters intentionally excluded:
            // they carry per-instance equipment children, weapon glow, and frequent action cancels
            // that would either break the shared bone palette or split every player into its own batch.
            if (this is not WalkerObject walker)
                return false;

            if (walker is Player.PlayerObject)
                return false;

            if (walker is MonsterObject)
                return false;

            if (UsesMutableMeshData)
                return false;

            if (!Visible || IsMouseHover || Model?.Meshes == null || Model.Meshes.Length == 0)
                return false;

            if (LinkParentAnimation || ParentBoneLink >= 0 || ContinuousAnimation || _isBlending || !_animationSampleValid)
                return false;

            // Death/one-shot animations cancel pose sharing: keep them on the per-instance path.
            if (walker is MonsterObject monster && (monster.IsDead || monster.IsOneShotPlaying))
                return false;
            if (walker is not MonsterObject && walker.IsOneShotPlaying)
                return false;

            if (TotalAlpha < 0.999f || EnableCustomShader)
                return false;

            if (_animationSampleActionIndex < 0)
                return false;

            return true;
        }

        private bool HasAnimatedCurrentAction()
        {
            if (Model?.Actions == null || Model.Actions.Length == 0)
                return false;

            int actionIndex = Math.Clamp(CurrentAction, 0, Model.Actions.Length - 1);
            var action = Model.Actions[actionIndex];
            return action != null && action.NumAnimationKeys > 1;
        }

        private bool CanUseStaticMapMeshForInstancing(int meshIndex)
        {
            if (!ShouldQueueStaticMapMesh(meshIndex))
                return false;

            if (_boneTextures == null || meshIndex >= _boneTextures.Length || _boneTextures[meshIndex] == null)
                return false;

            var shaderSelection = DetermineShaderForMesh(meshIndex);
            if (shaderSelection.UseItemMaterial || shaderSelection.UseMonsterMaterial)
                return false;

            return shaderSelection.UseDynamicLighting;
        }

        private bool CanUseMonsterCrowdMeshForInstancing(int meshIndex)
        {
            if (Model?.Meshes == null || meshIndex < 0 || meshIndex >= Model.Meshes.Length)
                return false;

            if (_boneTextures == null || meshIndex >= _boneTextures.Length || _boneTextures[meshIndex] == null)
                return false;

            var shaderSelection = DetermineShaderForMesh(meshIndex);
            if (shaderSelection.UseItemMaterial || shaderSelection.UseMonsterMaterial)
                return false;

            var blendState = GetMeshBlendState(meshIndex, false);
            if (!ReferenceEquals(blendState, BlendState.Opaque))
                return false;

            return shaderSelection.UseDynamicLighting;
        }

        private void EnsureStaticMapInstancingFrameTags(int meshCount)
        {
            if (_staticMapInstancedMeshFrameTags != null && _staticMapInstancedMeshFrameTags.Length >= meshCount)
                return;

            _staticMapInstancedMeshFrameTags = new int[meshCount];
        }

        private bool ShouldQueueStaticMapMesh(int meshIndex)
        {
            if (Model?.Meshes == null || meshIndex < 0 || meshIndex >= Model.Meshes.Length)
                return false;

            if (IsHiddenMesh(meshIndex))
                return false;

            bool isBlend = IsBlendMesh(meshIndex);
            bool isRGBA = _meshIsRGBA != null &&
                          (uint)meshIndex < (uint)_meshIsRGBA.Length &&
                          _meshIsRGBA[meshIndex];

            if (isBlend || isRGBA)
                return false;

            string blendingMode = Model.Meshes[meshIndex].BlendingMode;
            return string.IsNullOrEmpty(blendingMode) ||
                   string.Equals(blendingMode, "Opaque", StringComparison.OrdinalIgnoreCase);
        }

        private bool HasVisibleTransparentMapMesh()
        {
            if (Model?.Meshes == null)
                return false;

            for (int meshIndex = 0; meshIndex < Model.Meshes.Length; meshIndex++)
            {
                if (IsHiddenMesh(meshIndex))
                    continue;

                bool isBlend = IsBlendMesh(meshIndex);
                bool isRGBA = _meshIsRGBA != null &&
                              (uint)meshIndex < (uint)_meshIsRGBA.Length &&
                              _meshIsRGBA[meshIndex];

                if (isBlend || isRGBA)
                    return true;

                string blendingMode = Model.Meshes[meshIndex].BlendingMode;
                if (!string.IsNullOrEmpty(blendingMode) &&
                    !string.Equals(blendingMode, "Opaque", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private bool ShouldQueueMonsterCrowdMesh(int meshIndex)
        {
            if (Model?.Meshes == null || meshIndex < 0 || meshIndex >= Model.Meshes.Length)
                return false;

            if (IsHiddenMesh(meshIndex))
                return false;

            bool isBlend = IsBlendMesh(meshIndex);
            bool isRGBA = _meshIsRGBA != null &&
                          (uint)meshIndex < (uint)_meshIsRGBA.Length &&
                          _meshIsRGBA[meshIndex];

            return !isRGBA && !isBlend;
        }

        private Color GetCrowdInstancingBodyColor()
        {
            Vector3 meshLight = Light;
            if (LightEnabled && World?.Terrain != null)
            {
                Vector3 worldTranslation = WorldPosition.Translation;
                meshLight = World.Terrain.EvaluateTerrainLight(worldTranslation.X, worldTranslation.Y) + Light;
            }

            float lightScale = TotalAlpha;
            byte alpha = (byte)MathHelper.Clamp(TotalAlpha * 255f, 0f, 255f);
            float r = MathF.Min(Color.R * (meshLight.X * lightScale), 255f);
            float g = MathF.Min(Color.G * (meshLight.Y * lightScale), 255f);
            float b = MathF.Min(Color.B * (meshLight.Z * lightScale), 255f);
            return new Color((byte)r, (byte)g, (byte)b, alpha);
        }

        private static void PrepareStaticMapInstancingEffect(Effect effect, WorldControl world)
        {
            if (effect == null || _cachedStaticMapInstancingTechnique == null)
                return;

            effect.CurrentTechnique = _cachedStaticMapInstancingTechnique;

            var camera = Camera.Instance;
            if (camera == null)
                return;

            effect.Parameters["World"]?.SetValue(_identity);
            effect.Parameters["View"]?.SetValue(camera.View);
            effect.Parameters["Projection"]?.SetValue(camera.Projection);
            effect.Parameters["WorldViewProjection"]?.SetValue(camera.View * camera.Projection);
            effect.Parameters["EyePosition"]?.SetValue(camera.Position);
            effect.Parameters["Alpha"]?.SetValue(1f);
            effect.Parameters["TerrainDynamicIntensityScale"]?.SetValue(1.5f);
            effect.Parameters["DebugLightingAreas"]?.SetValue(Constants.DEBUG_LIGHTING_AREAS ? 1.0f : 0.0f);

            Vector3 sunDir = GraphicsManager.Instance.ShadowMapRenderer?.LightDirection ?? Constants.SUN_DIRECTION;
            if (sunDir.LengthSquared() < 0.0001f)
                sunDir = new Vector3(1f, 0f, -0.6f);
            sunDir = Vector3.Normalize(sunDir);
            bool sunEnabled = Constants.SUN_ENABLED && (world?.IsSunWorld ?? true);

            effect.Parameters["SunDirection"]?.SetValue(sunDir);
            effect.Parameters["SunColor"]?.SetValue(_sunColor);
            effect.Parameters["SunStrength"]?.SetValue(sunEnabled ? SunCycleManager.GetEffectiveSunStrength() : 0f);
            effect.Parameters["ShadowStrength"]?.SetValue(sunEnabled ? SunCycleManager.GetEffectiveShadowStrength() : 0f);
            effect.Parameters["AmbientLight"]?.SetValue(_ambientLightVector * SunCycleManager.AmbientMultiplier);

            GraphicsManager.Instance.ShadowMapRenderer?.ApplyShadowParameters(effect);
            UploadStaticMapInstancingDynamicLights(effect, world);
        }

        private static void UploadStaticMapInstancingDynamicLights(Effect effect, WorldControl world)
        {
            var terrain = world?.Terrain;
            if (!Constants.ENABLE_DYNAMIC_LIGHTS || terrain == null)
            {
                _staticInstancingLightUploader.Clear(effect);
                return;
            }

            var visibleLights = terrain.VisibleLights;
            if (visibleLights == null || visibleLights.Count == 0)
            {
                _staticInstancingLightUploader.Clear(effect);
                return;
            }

            int maxLights = Math.Min(
                DynamicLightGpuUploader.ResolveEffectCapacity(effect, 32),
                Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 16 : 32);

            Vector2 focus = Camera.Instance != null
                ? new Vector2(Camera.Instance.Target.X, Camera.Instance.Target.Y)
                : Vector2.Zero;
            float focusRadius = ResolveStaticMapInstancingLightCoverageRadius();

            _staticInstancingLightUploader.Upload(effect, visibleLights, focus, maxLights, focusRadius);
        }

        private static float ResolveStaticMapInstancingLightCoverageRadius()
        {
            var camera = Camera.Instance;
            if (camera == null)
                return Constants.MAX_CAMERA_DISTANCE;

            float cameraDistance = Vector3.Distance(camera.Position, camera.Target);
            if (!float.IsFinite(cameraDistance) || cameraDistance <= 0f)
                return Constants.MAX_CAMERA_DISTANCE;

            return MathHelper.Clamp(cameraDistance * 1.6f, 900f, 3200f);
        }
    }
}
