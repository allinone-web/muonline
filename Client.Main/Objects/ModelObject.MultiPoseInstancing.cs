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
        private const bool EnableWalkerCrowdMultiPoseInstancing = true;
        private const int CrowdBonePaletteMaxWidth = MaxGpuSkinBones * 4;
        private const int InitialCrowdPoseCapacity = 16;

        private readonly struct WalkerCrowdMultiPoseBatchKey : IEquatable<WalkerCrowdMultiPoseBatchKey>
        {
            public WalkerCrowdMultiPoseBatchKey(BMD model, int meshIndex, Texture2D texture, bool twoSided)
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

            public bool Equals(WalkerCrowdMultiPoseBatchKey other) =>
                ReferenceEquals(Model, other.Model) &&
                MeshIndex == other.MeshIndex &&
                ReferenceEquals(Texture, other.Texture) &&
                TwoSided == other.TwoSided;

            public override bool Equals(object obj) => obj is WalkerCrowdMultiPoseBatchKey other && Equals(other);

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

        private readonly struct WalkerCrowdPoseKey : IEquatable<WalkerCrowdPoseKey>
        {
            public WalkerCrowdPoseKey(Matrix[] bones, uint poseVersion)
            {
                Bones = bones;
                PoseVersion = poseVersion;
            }

            public Matrix[] Bones { get; }
            public uint PoseVersion { get; }

            public bool Equals(WalkerCrowdPoseKey other) =>
                ReferenceEquals(Bones, other.Bones) && PoseVersion == other.PoseVersion;

            public override bool Equals(object obj) => obj is WalkerCrowdPoseKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (RuntimeHelpers.GetHashCode(Bones) * 397) ^ (int)PoseVersion;
                }
            }
        }

        private readonly struct WalkerCrowdPoseUpload
        {
            public WalkerCrowdPoseUpload(Matrix[] bones, uint poseVersion)
            {
                Bones = bones;
                PoseVersion = poseVersion;
            }

            public Matrix[] Bones { get; }
            public uint PoseVersion { get; }
        }

        private sealed class PackedImmutableCrowdPose
        {
            public PackedImmutableCrowdPose(Matrix[] bones)
            {
                int boneCount = Math.Min(bones?.Length ?? 0, MaxGpuSkinBones);
                BoneCount = boneCount;
                Rows = new Vector4[boneCount * 4];

                for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
                {
                    Matrix matrix = bones[boneIndex];
                    int row = boneIndex * 4;
                    Rows[row + 0] = new Vector4(matrix.M11, matrix.M12, matrix.M13, matrix.M14);
                    Rows[row + 1] = new Vector4(matrix.M21, matrix.M22, matrix.M23, matrix.M24);
                    Rows[row + 2] = new Vector4(matrix.M31, matrix.M32, matrix.M33, matrix.M34);
                    Rows[row + 3] = new Vector4(matrix.M41, matrix.M42, matrix.M43, matrix.M44);
                }
            }

            public int BoneCount { get; }
            public Vector4[] Rows { get; }
        }

        private sealed class WalkerCrowdMultiPoseBatch : IDisposable
        {
            public VertexBuffer GeometryVertexBuffer;
            public IndexBuffer GeometryIndexBuffer;
            public int PrimitiveCount;
            public bool TwoSided;
            public Texture2D Texture;
            public readonly List<SkinnedCrowdInstanceData> Instances = new(64);
            public DynamicVertexBuffer InstanceBuffer;
            public int InstanceBufferCapacity;
            public SkinnedCrowdInstanceData[] UploadBuffer = Array.Empty<SkinnedCrowdInstanceData>();
            public readonly VertexBufferBinding[] VertexBindings = new VertexBufferBinding[2];

            public void Dispose()
            {
                InstanceBuffer?.Dispose();
                InstanceBuffer = null;
                InstanceBufferCapacity = 0;
                UploadBuffer = Array.Empty<SkinnedCrowdInstanceData>();
                Instances.Clear();
            }
        }

        private sealed class WalkerCrowdPaletteTextureSlot : IDisposable
        {
            public Texture2D Texture;
            public int Width;
            public int Height;

            public void Dispose()
            {
                Texture?.Dispose();
                Texture = null;
                Width = 0;
                Height = 0;
            }
        }

        private static readonly Dictionary<WalkerCrowdMultiPoseBatchKey, WalkerCrowdMultiPoseBatch>
            _walkerCrowdMultiPoseBatches = new(128);
        private static readonly List<WalkerCrowdMultiPoseBatch> _walkerCrowdMultiPoseActiveBatches = new(128);
        private static readonly Dictionary<WalkerCrowdPoseKey, int> _walkerCrowdPoseRows = new(128);
        private static readonly List<WalkerCrowdPoseUpload> _walkerCrowdPoseUploads = new(128);

        private static Effect _cachedWalkerCrowdMultiPoseEffect;
        private static EffectTechnique _cachedWalkerCrowdMultiPoseTechnique;
        private static readonly ConditionalWeakTable<Matrix[], PackedImmutableCrowdPose>
            _packedImmutableCrowdPoses = new();
        private static readonly WalkerCrowdPaletteTextureSlot[] _walkerCrowdPaletteTextureRing =
        {
            new WalkerCrowdPaletteTextureSlot(),
            new WalkerCrowdPaletteTextureSlot(),
            new WalkerCrowdPaletteTextureSlot(),
        };
        private static int _walkerCrowdPaletteTextureCursor;
        private static Texture2D _walkerCrowdActiveBonePaletteTexture;
        private static int _walkerCrowdMaxBonesThisFlush;
        private static Vector4[] _walkerCrowdBonePaletteUpload = Array.Empty<Vector4>();
        private static bool _walkerCrowdMultiPoseInstancingFailed;

        private static int _walkerCrowdMultiPoseObjectsThisFrame;
        private static int _walkerCrowdMultiPoseMeshInstancesThisFrame;
        private static int _walkerCrowdMultiPoseDrawCallsThisFrame;
        private static int _walkerCrowdMultiPoseUniquePosesThisFrame;
        private static int _walkerCrowdMultiPosePaletteUploadsThisFrame;

        public static int LastFrameWalkerCrowdMultiPoseObjects { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseMeshInstances { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseDrawCalls { get; private set; }
        public static int LastFrameWalkerCrowdMultiPoseUniquePoses { get; private set; }
        public static int LastFrameWalkerCrowdMultiPosePaletteUploads { get; private set; }
        public static bool IsWalkerCrowdMultiPoseActive => IsWalkerCrowdMultiPoseInstancingSupported();

        private static void BeginFrameWalkerCrowdMultiPoseMetrics()
        {
            LastFrameWalkerCrowdMultiPoseObjects = _walkerCrowdMultiPoseObjectsThisFrame;
            LastFrameWalkerCrowdMultiPoseMeshInstances = _walkerCrowdMultiPoseMeshInstancesThisFrame;
            LastFrameWalkerCrowdMultiPoseDrawCalls = _walkerCrowdMultiPoseDrawCallsThisFrame;
            LastFrameWalkerCrowdMultiPoseUniquePoses = _walkerCrowdMultiPoseUniquePosesThisFrame;
            LastFrameWalkerCrowdMultiPosePaletteUploads = _walkerCrowdMultiPosePaletteUploadsThisFrame;

            _walkerCrowdMultiPoseObjectsThisFrame = 0;
            _walkerCrowdMultiPoseMeshInstancesThisFrame = 0;
            _walkerCrowdMultiPoseDrawCallsThisFrame = 0;
            _walkerCrowdMultiPoseUniquePosesThisFrame = 0;
            _walkerCrowdMultiPosePaletteUploadsThisFrame = 0;
        }

        private static bool IsWalkerCrowdInstancingSupported() =>
            IsWalkerCrowdMultiPoseInstancingSupported() || IsWalkerCrowdLegacyInstancingSupported();

        private static bool IsWalkerCrowdMultiPoseInstancingSupported()
        {
            if (!EnableWalkerCrowdMultiPoseInstancing ||
                _walkerCrowdMultiPoseInstancingFailed ||
                !Constants.ENABLE_WALKER_CROWD_INSTANCING ||
                !Constants.ENABLE_GPU_SKINNING ||
                !SupportsGpuDynamicSkinning)
            {
                return false;
            }

            Effect effect = GraphicsManager.Instance?.DynamicLightingEffect;
            if (effect == null)
                return false;

            if (!ReferenceEquals(_cachedWalkerCrowdMultiPoseEffect, effect))
            {
                _cachedWalkerCrowdMultiPoseEffect = effect;
                _cachedWalkerCrowdMultiPoseTechnique = TryGetTechnique(
                    effect,
                    "DynamicLighting_SkinnedMultiPoseInstanced");
            }

            ModelEffectBindings bindings = GetModelEffectBindings(effect);
            return _cachedWalkerCrowdMultiPoseTechnique != null &&
                   bindings?.CrowdBonePaletteTexture != null &&
                   bindings.CrowdBonePaletteRowCount != null;
        }

        private bool TryQueueWalkerCrowdForInstancing()
        {
            if (IsWalkerCrowdMultiPoseInstancingSupported() && TryQueueWalkerCrowdMultiPoseForInstancing())
                return true;

            return TryQueueWalkerCrowdLegacyForInstancing();
        }

        internal static bool HasPendingWalkerCrowdInstancingBatches() =>
            _walkerCrowdMultiPoseActiveBatches.Count > 0 || HasPendingWalkerCrowdLegacyInstancingBatches();

        internal static void FlushWalkerCrowdInstancingBatches(WorldControl world)
        {
            if (_walkerCrowdMultiPoseActiveBatches.Count > 0)
                FlushWalkerCrowdMultiPoseInstancingBatches(world);

            if (HasPendingWalkerCrowdLegacyInstancingBatches())
                FlushWalkerCrowdLegacyInstancingBatches(world);
        }

        private bool TryQueueWalkerCrowdMultiPoseForInstancing()
        {
            if (!CanUseWalkerCrowdInstancing() || Model?.Meshes == null || _meshes == null)
                return false;

            int meshCount = Model.Meshes.Length;
            bool queuedAnyMesh = false;
            int requiredBoneCount = 0;

            // Validate the complete object before mutating any frame batch. This guarantees
            // that a failed multi-pose attempt can safely fall back to the legacy path.
            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                if (!ShouldQueueWalkerCrowdMesh(meshIndex))
                    continue;

                if (!CanUseWalkerCrowdMeshForInstancing(meshIndex))
                    return false;

                if (!BMDLoader.Instance.TryGetGpuSkinnedMeshBuffers(
                    Model,
                    meshIndex,
                    out _,
                    out _,
                    out int boneCount) ||
                    boneCount <= 0 ||
                    boneCount > MaxGpuSkinBones)
                {
                    return false;
                }

                requiredBoneCount = Math.Max(requiredBoneCount, boneCount);
                queuedAnyMesh = true;
            }

            if (!queuedAnyMesh)
                return false;

            Matrix[] bones = GetEffectiveBoneTransforms();
            bones = GetRenderBoneTransforms(bones) ?? bones;
            if (bones == null || bones.Length == 0 || bones.Length > MaxGpuSkinBones)
                return false;

            int paletteRow = RegisterWalkerCrowdPose(
                bones,
                GetEffectiveBonePoseVersion(),
                requiredBoneCount);
            if (paletteRow < 0)
                return false;

            var instanceData = new SkinnedCrowdInstanceData(
                WorldPosition,
                GetCrowdInstancingBodyColor(),
                paletteRow);

            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                if (!ShouldQueueWalkerCrowdMesh(meshIndex))
                    continue;

                if (!BMDLoader.Instance.TryGetGpuSkinnedMeshBuffers(
                    Model,
                    meshIndex,
                    out VertexBuffer geometryVB,
                    out IndexBuffer geometryIB,
                    out _))
                {
                    return false;
                }

                bool twoSided = IsMeshTwoSided(meshIndex, false);
                Texture2D texture = _meshes[meshIndex].Texture;
                var key = new WalkerCrowdMultiPoseBatchKey(Model, meshIndex, texture, twoSided);

                if (!_walkerCrowdMultiPoseBatches.TryGetValue(key, out WalkerCrowdMultiPoseBatch batch))
                {
                    batch = new WalkerCrowdMultiPoseBatch();
                    _walkerCrowdMultiPoseBatches[key] = batch;
                }

                batch.GeometryVertexBuffer = geometryVB;
                batch.GeometryIndexBuffer = geometryIB;
                batch.PrimitiveCount = geometryIB.IndexCount / 3;
                batch.TwoSided = twoSided;
                batch.Texture = texture;

                if (batch.Instances.Count == 0)
                    _walkerCrowdMultiPoseActiveBatches.Add(batch);

                batch.Instances.Add(instanceData);
                _walkerCrowdMultiPoseMeshInstancesThisFrame++;
            }

            _walkerCrowdMultiPoseObjectsThisFrame++;
            return true;
        }

        private static int RegisterWalkerCrowdPose(
            Matrix[] bones,
            uint poseVersion,
            int requiredBoneCount)
        {
            _walkerCrowdMaxBonesThisFlush = Math.Max(
                _walkerCrowdMaxBonesThisFlush,
                Math.Min(Math.Max(requiredBoneCount, bones?.Length ?? 0), MaxGpuSkinBones));

            var key = new WalkerCrowdPoseKey(bones, poseVersion);
            if (_walkerCrowdPoseRows.TryGetValue(key, out int existingRow))
                return existingRow;

            int row = _walkerCrowdPoseUploads.Count;
            _walkerCrowdPoseRows.Add(key, row);
            _walkerCrowdPoseUploads.Add(new WalkerCrowdPoseUpload(bones, poseVersion));
            return row;
        }

        private static void FlushWalkerCrowdMultiPoseInstancingBatches(WorldControl world)
        {
            if (_walkerCrowdMultiPoseActiveBatches.Count == 0)
                return;

            if (!IsWalkerCrowdMultiPoseInstancingSupported())
            {
                ClearWalkerCrowdMultiPoseQueues();
                return;
            }

            GraphicsManager graphicsManager = GraphicsManager.Instance;
            Effect effect = graphicsManager.DynamicLightingEffect;
            GraphicsDevice gd = graphicsManager.GraphicsDevice;
            ModelEffectBindings bindings = GetModelEffectBindings(effect);

            if (effect == null || gd == null || bindings == null ||
                _cachedWalkerCrowdMultiPoseTechnique == null)
            {
                ClearWalkerCrowdMultiPoseQueues();
                return;
            }

            BlendState previousBlend = gd.BlendState;
            RasterizerState previousRasterizer = gd.RasterizerState;
            SamplerState previousSampler = gd.SamplerStates[0];

            try
            {
                int poseCount = _walkerCrowdPoseUploads.Count;
                if (poseCount <= 0 || !UploadWalkerCrowdBonePalette(gd, poseCount))
                    throw new InvalidOperationException("Unable to upload the multi-pose crowd bone palette.");

                PrepareStaticMapInstancingEffect(effect, world, _cachedWalkerCrowdMultiPoseTechnique);
                bindings.CrowdBonePaletteTexture.SetValue(_walkerCrowdActiveBonePaletteTexture);
                bindings.CrowdBonePaletteRowCount.SetValue((float)poseCount);

                gd.BlendState = BlendState.Opaque;
                gd.SamplerStates[0] = GraphicsManager.GetQualityLinearSamplerState();

                for (int i = 0; i < _walkerCrowdMultiPoseActiveBatches.Count; i++)
                {
                    WalkerCrowdMultiPoseBatch batch = _walkerCrowdMultiPoseActiveBatches[i];
                    int instanceCount = batch.Instances.Count;
                    if (instanceCount <= 0 ||
                        batch.GeometryVertexBuffer == null ||
                        batch.GeometryVertexBuffer.IsDisposed ||
                        batch.GeometryIndexBuffer == null ||
                        batch.GeometryIndexBuffer.IsDisposed ||
                        batch.Texture == null ||
                        batch.Texture.IsDisposed)
                    {
                        continue;
                    }

                    EnsureWalkerCrowdMultiPoseUploadBuffer(batch, instanceCount);
                    for (int j = 0; j < instanceCount; j++)
                        batch.UploadBuffer[j] = batch.Instances[j];

                    EnsureWalkerCrowdMultiPoseInstanceBuffer(gd, batch, instanceCount);
                    batch.InstanceBuffer.SetData(batch.UploadBuffer, 0, instanceCount, SetDataOptions.Discard);

                    gd.RasterizerState = batch.TwoSided
                        ? RasterizerState.CullNone
                        : RasterizerState.CullClockwise;
                    bindings.DiffuseTexture?.SetValue(batch.Texture);

                    batch.VertexBindings[0] = new VertexBufferBinding(batch.GeometryVertexBuffer);
                    batch.VertexBindings[1] = new VertexBufferBinding(batch.InstanceBuffer, 0, 1);
                    gd.SetVertexBuffers(batch.VertexBindings);
                    gd.Indices = batch.GeometryIndexBuffer;

                    RegisterGpuSkinnedMeshDraw(instanceCount);
                    int passCount = effect.CurrentTechnique.Passes.Count;
                    for (int passIndex = 0; passIndex < passCount; passIndex++)
                    {
                        effect.CurrentTechnique.Passes[passIndex].Apply();
                        gd.DrawInstancedPrimitives(
                            PrimitiveType.TriangleList,
                            0,
                            0,
                            batch.PrimitiveCount,
                            instanceCount);
                        _walkerCrowdMultiPoseDrawCallsThisFrame++;
                    }
                }

                _walkerCrowdMultiPoseUniquePosesThisFrame += poseCount;
                _walkerCrowdMultiPosePaletteUploadsThisFrame++;
            }
            catch (Exception ex)
            {
                _walkerCrowdMultiPoseInstancingFailed = true;
                DisposeWalkerCrowdMultiPoseGpuResources();
                MuGame.AppLoggerFactory?
                    .CreateLogger<ModelObject>()?
                    .LogWarning(
                        ex,
                        "Multi-pose crowd instancing disabled after runtime failure; the legacy GPU crowd path will be used.");
            }
            finally
            {
                gd.BlendState = previousBlend;
                gd.RasterizerState = previousRasterizer;
                gd.SamplerStates[0] = previousSampler;
                ClearWalkerCrowdMultiPoseQueues();
            }
        }

        private static bool UploadWalkerCrowdBonePalette(GraphicsDevice gd, int poseCount)
        {
            int paletteBoneCapacity = ResolveCrowdPaletteBoneCapacity(_walkerCrowdMaxBonesThisFlush);
            int uploadWidth = paletteBoneCapacity * 4;
            _walkerCrowdActiveBonePaletteTexture = AcquireWalkerCrowdBonePaletteTexture(
                gd,
                uploadWidth,
                poseCount);
            if (_walkerCrowdActiveBonePaletteTexture == null || _walkerCrowdActiveBonePaletteTexture.IsDisposed)
                return false;

            int vectorCount = checked(uploadWidth * poseCount);
            if (_walkerCrowdBonePaletteUpload.Length < vectorCount)
                _walkerCrowdBonePaletteUpload = new Vector4[RoundUpToPowerOfTwo(vectorCount)];

            for (int poseIndex = 0; poseIndex < poseCount; poseIndex++)
            {
                WalkerCrowdPoseUpload pose = _walkerCrowdPoseUploads[poseIndex];
                Matrix[] bones = pose.Bones;
                int rowOffset = poseIndex * uploadWidth;
                int boneCount = Math.Min(bones?.Length ?? 0, paletteBoneCapacity);
                int copiedRows = 0;

                if (pose.PoseVersion == uint.MaxValue && bones != null)
                {
                    PackedImmutableCrowdPose packed = _packedImmutableCrowdPoses.GetValue(
                        bones,
                        static source => new PackedImmutableCrowdPose(source));
                    copiedRows = Math.Min(packed.Rows.Length, boneCount * 4);
                    if (copiedRows > 0)
                        Array.Copy(packed.Rows, 0, _walkerCrowdBonePaletteUpload, rowOffset, copiedRows);
                }
                else
                {
                    for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
                    {
                        Matrix matrix = bones[boneIndex];
                        int texel = rowOffset + (boneIndex * 4);
                        _walkerCrowdBonePaletteUpload[texel + 0] = new Vector4(matrix.M11, matrix.M12, matrix.M13, matrix.M14);
                        _walkerCrowdBonePaletteUpload[texel + 1] = new Vector4(matrix.M21, matrix.M22, matrix.M23, matrix.M24);
                        _walkerCrowdBonePaletteUpload[texel + 2] = new Vector4(matrix.M31, matrix.M32, matrix.M33, matrix.M34);
                        _walkerCrowdBonePaletteUpload[texel + 3] = new Vector4(matrix.M41, matrix.M42, matrix.M43, matrix.M44);
                    }
                    copiedRows = boneCount * 4;
                }

                for (int texel = copiedRows; texel < uploadWidth; texel += 4)
                {
                    int target = rowOffset + texel;
                    _walkerCrowdBonePaletteUpload[target + 0] = new Vector4(1f, 0f, 0f, 0f);
                    _walkerCrowdBonePaletteUpload[target + 1] = new Vector4(0f, 1f, 0f, 0f);
                    _walkerCrowdBonePaletteUpload[target + 2] = new Vector4(0f, 0f, 1f, 0f);
                    _walkerCrowdBonePaletteUpload[target + 3] = new Vector4(0f, 0f, 0f, 1f);
                }
            }

            _walkerCrowdActiveBonePaletteTexture.SetData(
                0,
                new Rectangle(0, 0, uploadWidth, poseCount),
                _walkerCrowdBonePaletteUpload,
                0,
                vectorCount);
            return true;
        }

        private static Texture2D AcquireWalkerCrowdBonePaletteTexture(
            GraphicsDevice gd,
            int requiredWidth,
            int poseCount)
        {
            int slotIndex = _walkerCrowdPaletteTextureCursor++ % _walkerCrowdPaletteTextureRing.Length;
            WalkerCrowdPaletteTextureSlot slot = _walkerCrowdPaletteTextureRing[slotIndex];
            int requiredHeight = Math.Max(InitialCrowdPoseCapacity, RoundUpToPowerOfTwo(poseCount));

            if (slot.Texture == null ||
                slot.Texture.IsDisposed ||
                !ReferenceEquals(slot.Texture.GraphicsDevice, gd) ||
                slot.Width < requiredWidth ||
                slot.Height < requiredHeight)
            {
                int textureWidth = Math.Min(
                    CrowdBonePaletteMaxWidth,
                    Math.Max(requiredWidth, slot.Width));
                int textureHeight = Math.Max(requiredHeight, slot.Height);

                slot.Dispose();
                slot.Texture = new Texture2D(
                    gd,
                    textureWidth,
                    textureHeight,
                    false,
                    SurfaceFormat.Vector4);
                slot.Width = textureWidth;
                slot.Height = textureHeight;
            }

            return slot.Texture;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ResolveCrowdPaletteBoneCapacity(int boneCount)
        {
            if (boneCount <= 32)
                return 32;
            if (boneCount <= 64)
                return 64;
            if (boneCount <= 128)
                return 128;
            return MaxGpuSkinBones;
        }

        private static void EnsureWalkerCrowdMultiPoseUploadBuffer(
            WalkerCrowdMultiPoseBatch batch,
            int instanceCount)
        {
            if (batch.UploadBuffer.Length >= instanceCount)
                return;

            int newSize = Math.Max(
                instanceCount,
                batch.UploadBuffer.Length == 0 ? 64 : batch.UploadBuffer.Length * 2);
            batch.UploadBuffer = new SkinnedCrowdInstanceData[newSize];
        }

        private static void EnsureWalkerCrowdMultiPoseInstanceBuffer(
            GraphicsDevice gd,
            WalkerCrowdMultiPoseBatch batch,
            int instanceCount)
        {
            if (batch.InstanceBuffer != null &&
                !batch.InstanceBuffer.IsDisposed &&
                ReferenceEquals(batch.InstanceBuffer.GraphicsDevice, gd) &&
                batch.InstanceBufferCapacity >= instanceCount)
            {
                return;
            }

            batch.InstanceBuffer?.Dispose();
            int capacity = Math.Max(instanceCount, 64);
            batch.InstanceBuffer = new DynamicVertexBuffer(
                gd,
                SkinnedCrowdInstanceData.VertexDeclaration,
                capacity,
                BufferUsage.WriteOnly);
            batch.InstanceBufferCapacity = capacity;
        }

        private static void ClearWalkerCrowdMultiPoseQueues()
        {
            for (int i = 0; i < _walkerCrowdMultiPoseActiveBatches.Count; i++)
                _walkerCrowdMultiPoseActiveBatches[i].Instances.Clear();

            _walkerCrowdMultiPoseActiveBatches.Clear();
            _walkerCrowdPoseRows.Clear();
            _walkerCrowdPoseUploads.Clear();
            _walkerCrowdMaxBonesThisFlush = 0;
        }

        private static void DisposeWalkerCrowdMultiPoseGpuResources()
        {
            foreach (WalkerCrowdMultiPoseBatch batch in _walkerCrowdMultiPoseBatches.Values)
                batch.Dispose();

            _walkerCrowdMultiPoseBatches.Clear();
            _walkerCrowdMultiPoseActiveBatches.Clear();
            _walkerCrowdPoseRows.Clear();
            _walkerCrowdPoseUploads.Clear();

            for (int i = 0; i < _walkerCrowdPaletteTextureRing.Length; i++)
                _walkerCrowdPaletteTextureRing[i].Dispose();

            _walkerCrowdPaletteTextureCursor = 0;
            _walkerCrowdActiveBonePaletteTexture = null;
            _walkerCrowdMaxBonesThisFlush = 0;
            _walkerCrowdBonePaletteUpload = Array.Empty<Vector4>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int RoundUpToPowerOfTwo(int value)
        {
            if (value <= 1)
                return 1;

            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }
    }
}
