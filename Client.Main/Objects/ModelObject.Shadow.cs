using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Client.Main.Objects
{
    public abstract partial class ModelObject
    {
        private static readonly VertexPositionTexture[] BlobShadowVertices =
        [
            // MU worlds use X/Y as the ground plane and Z as height. Keeping the blob in
            // XY avoids the legacy projected-mesh rotations which could turn actor shadows
            // nearly vertical or push them below the terrain.
            new VertexPositionTexture(new Vector3(-1f, -1f, 0f), new Vector2(0f, 1f)),
            new VertexPositionTexture(new Vector3(1f, -1f, 0f), new Vector2(1f, 1f)),
            new VertexPositionTexture(new Vector3(1f, 1f, 0f), new Vector2(1f, 0f)),
            new VertexPositionTexture(new Vector3(-1f, 1f, 0f), new Vector2(0f, 0f))
        ];

        private static readonly short[] BlobShadowIndices = [0, 1, 2, 0, 2, 3];
        private static readonly object BlobShadowTextureLock = new();
        private static Texture2D _blobShadowTexture;

        private ModelObject GetShadowActorRoot()
        {
            ModelObject root = this;
            while (root.Parent is ModelObject parentModel)
                root = parentModel;
            return root;
        }

        private bool IsPlayerOrNpcShadowPart()
        {
            ModelObject root = GetShadowActorRoot();
            return root is PlayerObject || root is NPCObject;
        }

        private bool UsesRenderedShadowMapForCurrentObject()
        {
            if (!Constants.ENABLE_DYNAMIC_LIGHTING_SHADER)
                return false;

            var renderer = GraphicsManager.Instance.ShadowMapRenderer;
            ModelObject casterRoot = GetShadowActorRoot();
            return renderer?.IsReady == true && renderer.HasRenderedCaster(casterRoot);
        }

        private bool SupportsGpuSkinnedShadowCaster()
        {
            var effect = GraphicsManager.Instance.DynamicLightingEffect;
            return TryGetTechnique(effect, "ShadowCaster_Skinned") != null;
        }

        private bool NeedsModularShadowSafetyBlob()
        {
            if (this is not WalkerObject)
                return false;

            int substantialParts = 0;
            int childCount = Children.Count;
            for (int i = 0; i < childCount; i++)
            {
                if (Children[i] is not ModelObject child ||
                    child.Hidden ||
                    !child.RenderShadow ||
                    child.Status != GameControlStatus.Ready ||
                    child.Model?.Meshes == null)
                {
                    continue;
                }

                substantialParts++;
                if (substantialParts >= 2)
                    return true;
            }

            return false;
        }

        private bool RequiresPersistentActorGroundShadow()
        {
            // Player-model actors are assembled from multiple asynchronously prepared parts.
            // A shadow-map update may temporarily contain only one part (for example a helmet),
            // so keep a stable root footprint independently of caster completeness.
            return this is PlayerObject || this is NPCObject;
        }

        private bool ValidateWorldMatrix(Matrix matrix)
        {
            for (int i = 0; i < 16; i++)
            {
                if (float.IsNaN(matrix[i]))
                    return false;
            }
            return true;
        }

        private bool TryGetGroundBlobShadowMatrix(out Matrix shadowWorld)
        {
            shadowWorld = Matrix.Identity;

            try
            {
                if (World?.Terrain == null)
                    return false;

                ModelObject root = this;
                while (root.Parent is ModelObject parentModel)
                    root = parentModel;

                Vector3 position = root.WorldPosition.Translation;
                float terrainHeight = World.Terrain.RequestTerrainHeight(position.X, position.Y);
                if (!float.IsFinite(terrainHeight))
                    return false;

                const float groundBias = 0.75f;
                shadowWorld = Matrix.CreateRotationZ(root.TotalAngle.Z) *
                              Matrix.CreateTranslation(position.X, position.Y, terrainHeight + groundBias);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Error creating ground blob shadow matrix: {Message}", ex.Message);
                return false;
            }
        }

        private bool TryGetShadowMatrix(out Matrix shadowWorld)
        {
            shadowWorld = Matrix.Identity;

            try
            {
                // For bone-attached models (weapons, wings, etc.) reuse the parent's blob-shadow basis
                // so attachments share the same shadow anchor/orientation as the character.
                if (ParentBoneLink >= 0 && Parent is ModelObject parentModel)
                {
                    if (!parentModel.TryGetShadowMatrix(out Matrix parentShadowWorld))
                        return false;

                    Matrix localMatrix = Matrix.CreateScale(Scale) *
                                         Matrix.CreateFromQuaternion(MathUtils.AngleQuaternion(Angle)) *
                                         Matrix.CreateTranslation(Position);

                    shadowWorld = localMatrix * ParentBodyOrigin * parentShadowWorld;
                    return true;
                }

                Vector3 position = WorldPosition.Translation;
                float terrainH = World.Terrain.RequestTerrainHeight(position.X, position.Y);

                float heightAboveTerrain = position.Z - terrainH;
                float angleRad = MathHelper.ToRadians(45);

                Vector3 shadowPos = new(
                    position.X - (heightAboveTerrain / 2),
                    position.Y - (heightAboveTerrain / 4.5f),
                    terrainH + 1f);

                float yaw = TotalAngle.Y + MathHelper.ToRadians(110);
                float pitch = TotalAngle.X + MathHelper.ToRadians(120);
                float roll = TotalAngle.Z + MathHelper.ToRadians(90);

                Quaternion rotQ = Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);

                const float shadowBias = 0.1f;
                shadowWorld =
                      Matrix.CreateFromQuaternion(rotQ)
                    * Matrix.CreateScale(1.0f * TotalScale, 0.01f * TotalScale, 1.0f * TotalScale)
                    * Matrix.CreateRotationX(-MathHelper.PiOver2) // keep shadow flat; skip extra terrain samples
                    * Matrix.CreateRotationZ(angleRad)
                    * Matrix.CreateTranslation(shadowPos + new Vector3(0f, 0f, shadowBias));

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Error creating shadow matrix: {Message}", ex.Message);
                return false;
            }
        }

        public virtual void DrawBlobShadow(Matrix view, Matrix projection, Matrix shadowWorld, float shadowOpacity)
        {
            if (shadowOpacity <= 0.001f)
                return;

            try
            {
                // Skip shadow rendering if shadows are disabled for this world
                if (MuGame.Instance.ActiveScene?.World is WorldControl world && !world.EnableShadows)
                    return;

                var effect = GraphicsManager.Instance.ShadowEffect;
                var blobTexture = GetOrCreateBlobShadowTexture();
                if (effect == null || blobTexture == null)
                    return;

                ModelEffectBindings bindings = GetModelEffectBindings(effect);
                EffectTechnique shadowTechnique = TryGetTechnique(effect, "Shadow");
                if (shadowTechnique == null)
                    return;

                var previousBlend = GraphicsDevice.BlendState;
                var previousDepth = GraphicsDevice.DepthStencilState;
                var previousRaster = GraphicsDevice.RasterizerState;
                var previousTechnique = effect.CurrentTechnique;

                float constBias = 1f / (1 << 24);
                RasterizerState shadowRasterizer = GraphicsManager.GetCachedRasterizerState(constBias * -20, CullMode.None);

                GraphicsDevice.BlendState = Blendings.ShadowBlend;
                GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                GraphicsDevice.RasterizerState = shadowRasterizer;
                effect.CurrentTechnique = shadowTechnique;

                try
                {
                    float localWidth = BoundingBoxLocal.Max.X - BoundingBoxLocal.Min.X;
                    float localDepth = BoundingBoxLocal.Max.Y - BoundingBoxLocal.Min.Y;
                    float scaleX = MathF.Max(45f, localWidth * 0.55f);
                    float scaleZ = MathF.Max(45f, localDepth * 0.55f);

                    Matrix blobWorld = Matrix.CreateScale(scaleX, scaleZ, 1f) * shadowWorld;

                    bindings.World?.SetValue(blobWorld);
                    bindings.ViewProjection?.SetValue(view * projection);
                    bindings.ShadowTint?.SetValue(new Vector4(0f, 0f, 0f, shadowOpacity));
                    bindings.ShadowTexture?.SetValue(blobTexture);

                    foreach (var pass in effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        GraphicsDevice.DrawUserIndexedPrimitives(
                            PrimitiveType.TriangleList,
                            BlobShadowVertices,
                            0,
                            BlobShadowVertices.Length,
                            BlobShadowIndices,
                            0,
                            2);
                    }
                }
                finally
                {
                    GraphicsDevice.BlendState = previousBlend;
                    GraphicsDevice.DepthStencilState = previousDepth;
                    GraphicsDevice.RasterizerState = previousRaster;
                    if (previousTechnique != null)
                        effect.CurrentTechnique = previousTechnique;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Error in DrawBlobShadow: {Message}", ex.Message);
            }
        }

        public virtual void DrawShadowMesh(int mesh, Matrix view, Matrix projection, Matrix shadowWorld, float shadowOpacity)
        {
            try
            {
                // Skip shadow rendering if shadows are disabled for this world
                if (MuGame.Instance.ActiveScene?.World is WorldControl world && !world.EnableShadows)
                    return;

                if (IsHiddenMesh(mesh) || _meshes == null)
                    return;

                if (!ValidateWorldMatrix(WorldPosition))
                {
                    _logger?.LogDebug("Invalid WorldPosition matrix detected - skipping shadow rendering");
                    return;
                }

                VertexBuffer vertexBuffer = _meshes[mesh].CpuVertexBuffer;
                IndexBuffer indexBuffer = _meshes[mesh].CpuIndexBuffer;
                if (vertexBuffer == null || indexBuffer == null)
                    return;

                int primitiveCount = indexBuffer.IndexCount / 3;

                var prevBlendState = GraphicsDevice.BlendState;
                var prevDepthState = GraphicsDevice.DepthStencilState;
                var prevRasterizerState = GraphicsDevice.RasterizerState;

                float constBias = 1f / (1 << 24);

                // PERFORMANCE: Use cached RasterizerState to avoid per-mesh allocation
                RasterizerState ShadowRasterizer = GraphicsManager.GetCachedRasterizerState(constBias * -20, CullMode.None);

                GraphicsDevice.BlendState = Blendings.ShadowBlend;
                GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                GraphicsDevice.RasterizerState = ShadowRasterizer;

                try
                {
                    var effect = GraphicsManager.Instance.ShadowEffect;
                    if (effect == null || _meshes?[mesh]?.Texture == null)
                        return;

                    ModelEffectBindings bindings = GetModelEffectBindings(effect);
                    bindings.World?.SetValue(shadowWorld);
                    bindings.ViewProjection?.SetValue(view * projection);
                    bindings.ShadowTint?.SetValue(new Vector4(0, 0, 0, shadowOpacity));
                    bindings.ShadowTexture?.SetValue(_meshes[mesh].Texture);

                    foreach (var pass in effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        GraphicsDevice.SetVertexBuffer(vertexBuffer);
                        GraphicsDevice.Indices = indexBuffer;
                        GraphicsDevice.DrawIndexedPrimitives(
                            PrimitiveType.TriangleList,
                            0, 0, primitiveCount);
                    }
                }
                finally
                {
                    GraphicsDevice.BlendState = prevBlendState;
                    GraphicsDevice.DepthStencilState = prevDepthState;
                    GraphicsDevice.RasterizerState = prevRasterizerState;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Error in DrawShadowMesh: {Message}", ex.Message);
            }
        }

        private Texture2D GetOrCreateBlobShadowTexture()
        {
            var texture = _blobShadowTexture;
            if (texture != null && !texture.IsDisposed)
                return texture;

            lock (BlobShadowTextureLock)
            {
                texture = _blobShadowTexture;
                if (texture != null && !texture.IsDisposed)
                    return texture;

                const int size = 64;
                texture = new Texture2D(GraphicsDevice, size, size, false, SurfaceFormat.Color);
                var data = new Color[size * size];
                float center = (size - 1) * 0.5f;
                float invRadius = 1f / center;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x - center) * invRadius;
                        float dy = (y - center) * invRadius;
                        float dist = MathF.Sqrt(dx * dx + dy * dy);
                        float alpha = MathF.Max(0f, 1f - dist);
                        alpha *= alpha;
                        data[y * size + x] = new Color((byte)255, (byte)255, (byte)255, (byte)(alpha * 255f));
                    }
                }

                texture.SetData(data);
                _blobShadowTexture = texture;
                return texture;
            }
        }

        public virtual int DrawShadowCaster(Effect shadowEffect, Matrix lightViewProjection)
        {
            if (shadowEffect == null)
                return 0;

            int drawnMeshCount = 0;
            int shadowSize = GraphicsManager.Instance.ShadowMapRenderer?.ShadowMap?.Width ?? Math.Max(256, Constants.SHADOW_MAP_SIZE);
            Vector2 shadowTexel = new Vector2(1f / shadowSize, 1f / shadowSize);

            // Draw own meshes if available. A missing technique or one invalid body part
            // must not suppress valid child casters.
            if (Model?.Meshes != null && _meshes != null)
            {
                try
                {
                    var gd = GraphicsDevice;
                    var prevBlend = gd.BlendState;
                    var prevDepth = gd.DepthStencilState;
                    var prevRaster = gd.RasterizerState;
                    var prevTechnique = shadowEffect.CurrentTechnique;

                    try
                    {
                        ModelEffectBindings bindings = GetModelEffectBindings(shadowEffect);
                        var shadowCasterTechnique = bindings.GetTechnique("ShadowCaster");
                        var shadowCasterSkinnedTechnique = bindings.GetTechnique("ShadowCaster_Skinned");
                        if (shadowCasterTechnique != null)
                        {
                            bindings.World?.SetValue(WorldPosition);
                            bindings.LightViewProjection?.SetValue(lightViewProjection);
                            bindings.ShadowMapTexelSize?.SetValue(shadowTexel);
                            bindings.ShadowBias?.SetValue(Constants.SHADOW_BIAS);
                            bindings.ShadowNormalBias?.SetValue(Constants.SHADOW_NORMAL_BIAS);
                            bindings.SunDirection?.SetValue(GraphicsManager.Instance.ShadowMapRenderer?.LightDirection ?? Constants.SUN_DIRECTION);
                            bindings.UseProceduralTerrainUv?.SetValue(0.0f);
                            bindings.IsWaterTexture?.SetValue(0.0f);

                            gd.BlendState = BlendState.Opaque;
                            gd.DepthStencilState = DepthStencilState.Default;

                            int meshCount = Model.Meshes.Length;
                            EffectTechnique activeTechnique = null;
                            int uploadedSkinnedBoneCount = 0;

                            for (int i = 0; i < meshCount; i++)
                            {
                                if (IsHiddenMesh(i))
                                    continue;

                                bool useGpuSkinning = shadowCasterSkinnedTechnique != null &&
                                                      EnsureGpuSkinnedMeshForMainPass(i);

                                VertexBuffer vb = useGpuSkinning ? _meshes[i].GpuVertexBuffer : _meshes?[i]?.CpuVertexBuffer;
                                IndexBuffer ib = useGpuSkinning ? _meshes[i].GpuIndexBuffer : _meshes?[i]?.CpuIndexBuffer;
                                var tex = _meshes[i].Texture;
                                if (vb == null || ib == null || tex == null)
                                    continue;

                                if (useGpuSkinning)
                                {
                                    int requiredBoneCount = _meshes != null && (uint)i < (uint)_meshes.Length
                                        ? _meshes[i].GpuBoneCount
                                        : 0;

                                    if (requiredBoneCount > uploadedSkinnedBoneCount)
                                    {
                                        if (!TryUploadGpuSkinBoneMatrices(shadowEffect, bindings, requiredBoneCount))
                                        {
                                            useGpuSkinning = false;
                                            vb = _meshes?[i]?.CpuVertexBuffer;
                                            ib = _meshes?[i]?.CpuIndexBuffer;
                                            if (vb == null || ib == null)
                                                continue;
                                        }
                                        else
                                        {
                                            uploadedSkinnedBoneCount = requiredBoneCount;
                                        }
                                    }
                                }

                                var targetTechnique = useGpuSkinning ? shadowCasterSkinnedTechnique : shadowCasterTechnique;
                                if (targetTechnique != activeTechnique)
                                {
                                    shadowEffect.CurrentTechnique = targetTechnique;
                                    activeTechnique = targetTechnique;
                                }

                                bool isTwoSided = IsMeshTwoSided(i, IsBlendMesh(i));
                                gd.RasterizerState = isTwoSided ? _cullNone : _cullClockwise;
                                bindings.DiffuseTexture?.SetValue(tex);

                                foreach (var pass in shadowEffect.CurrentTechnique.Passes)
                                {
                                    pass.Apply();
                                    gd.SetVertexBuffer(vb);
                                    gd.Indices = ib;
                                    gd.DrawIndexedPrimitives(
                                        PrimitiveType.TriangleList,
                                        0, 0, ib.IndexCount / 3);
                                }

                                drawnMeshCount++;
                            }
                        }
                    }
                    finally
                    {
                        gd.BlendState = prevBlend;
                        gd.DepthStencilState = prevDepth;
                        gd.RasterizerState = prevRaster;
                        if (prevTechnique != null)
                            shadowEffect.CurrentTechnique = prevTechnique;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug("Error drawing shadow caster: {Message}", ex.Message);
                }
            }

            // Recursively draw shadow casters for all children (armor, weapons, helm, etc.).
            int childCount = Children.Count;
            bool skipSmallParts = Constants.SHADOW_SKIP_SMALL_PARTS;
            for (int i = 0; i < childCount; i++)
            {
                var child = Children[i];
                if (child is ModelObject modelChild &&
                    modelChild.Status == GameControlStatus.Ready &&
                    !modelChild.Hidden &&
                    modelChild.RenderShadow)
                {
                    if (skipSmallParts && IsSmallShadowPart(modelChild))
                        continue;

                    drawnMeshCount += modelChild.DrawShadowCaster(shadowEffect, lightViewProjection);
                }
            }

            return drawnMeshCount;
        }

        /// <summary>
        /// Checks if a model child is a small part that can be skipped for shadow casting.
        /// Small parts like weapons, gloves, and boots don't contribute much to shadow silhouette.
        /// </summary>
        private static bool IsSmallShadowPart(ModelObject modelChild)
        {
            return modelChild is WeaponObject ||
                   modelChild is PlayerGloveObject ||
                   modelChild is PlayerBootObject ||
                   modelChild is PlayerMaskHelmObject;
        }
    }
}
