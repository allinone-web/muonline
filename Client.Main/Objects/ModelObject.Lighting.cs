using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Client.Main.Objects
{
    public abstract partial class ModelObject
    {
        private static EffectTechnique TryGetTechnique(Effect effect, string name)
        {
            if (effect == null || string.IsNullOrEmpty(name))
                return null;

            var techniques = effect.Techniques;
            int count = techniques.Count;
            for (int i = 0; i < count; i++)
            {
                var technique = techniques[i];
                if (string.Equals(technique.Name, name, StringComparison.Ordinal))
                    return technique;
            }

            return null;
        }

        private bool TryUploadGpuSkinBoneMatrices(Effect effect, int requiredBoneCount)
        {
            if (effect == null || requiredBoneCount <= 0 || requiredBoneCount > MaxGpuSkinBones)
                return false;

            Matrix[] bones = GetCachedBoneTransforms();
            bones = GetRenderBoneTransforms(bones) ?? bones;
            if (bones == null || bones.Length == 0)
                return false;

            int copyCount = Math.Min(requiredBoneCount, Math.Min(bones.Length, MaxGpuSkinBones));
            if (copyCount <= 0)
                return false;

            if (_gpuSkinBoneUploadBuffer == null || _gpuSkinBoneUploadBuffer.Length != copyCount)
                _gpuSkinBoneUploadBuffer = new Matrix[copyCount];

            Array.Copy(bones, 0, _gpuSkinBoneUploadBuffer, 0, copyCount);
            effect.Parameters["BoneMatrices"]?.SetValue(_gpuSkinBoneUploadBuffer);
            return true;
        }

        private void PrepareDynamicLightingEffect(Effect effect, bool useGpuSkinning = false, int requiredBoneCount = 0)
        {
            if (effect == null)
                return;

            var dynamicLightingTechnique = TryGetTechnique(effect, "DynamicLighting");
            if (dynamicLightingTechnique == null)
                return;

            var skinnedTechnique = useGpuSkinning ? TryGetTechnique(effect, "DynamicLighting_Skinned") : null;
            bool usingSkinnedTechnique = skinnedTechnique != null &&
                                         TryUploadGpuSkinBoneMatrices(effect, requiredBoneCount);

            effect.CurrentTechnique = usingSkinnedTechnique ? skinnedTechnique : dynamicLightingTechnique;
            GraphicsManager.Instance.ShadowMapRenderer?.ApplyShadowParameters(effect);

            var camera = Camera.Instance;
            if (camera == null)
                return;

            effect.Parameters["World"]?.SetValue(WorldPosition);
            effect.Parameters["View"]?.SetValue(camera.View);
            effect.Parameters["Projection"]?.SetValue(camera.Projection);
            effect.Parameters["WorldViewProjection"]?.SetValue(WorldPosition * camera.View * camera.Projection);
            effect.Parameters["EyePosition"]?.SetValue(camera.Position);

            Vector3 sunDir = GraphicsManager.Instance.ShadowMapRenderer?.LightDirection ?? Constants.SUN_DIRECTION;
            if (sunDir.LengthSquared() < 0.0001f)
                sunDir = new Vector3(1f, 0f, -0.6f);
            sunDir = Vector3.Normalize(sunDir);

            bool worldAllowsSun = World is WorldControl wc ? wc.IsSunWorld : true;
            bool sunEnabled = Constants.SUN_ENABLED && worldAllowsSun && UseSunLight && !HasWalkerAncestor();

            effect.Parameters["SunDirection"]?.SetValue(sunDir);
            effect.Parameters["SunColor"]?.SetValue(_sunColor);
            effect.Parameters["SunStrength"]?.SetValue(sunEnabled ? SunCycleManager.GetEffectiveSunStrength() : 0f);
            effect.Parameters["ShadowStrength"]?.SetValue(sunEnabled ? SunCycleManager.GetEffectiveShadowStrength() : 0f);

            effect.Parameters["Alpha"]?.SetValue(TotalAlpha);
            effect.Parameters["TerrainDynamicIntensityScale"]?.SetValue(1.5f);
            effect.Parameters["AmbientLight"]?.SetValue(_ambientLightVector * SunCycleManager.AmbientMultiplier);
            effect.Parameters["DebugLightingAreas"]?.SetValue(Constants.DEBUG_LIGHTING_AREAS ? 1.0f : 0.0f);

            Vector3 worldTranslation = WorldPosition.Translation;
            Vector3 terrainLight = Vector3.One;
            if (LightEnabled && World?.Terrain != null)
                terrainLight = World.Terrain.EvaluateTerrainLight(worldTranslation.X, worldTranslation.Y);
            // EvaluateTerrainLight is already normalized to 0..1. Dynamic lights
            // are uploaded separately below and must not be baked into this value.
            terrainLight = Vector3.Clamp(terrainLight, Vector3.Zero, Vector3.One);
            effect.Parameters["TerrainLight"]?.SetValue(terrainLight);

            if (!Constants.ENABLE_DYNAMIC_LIGHTS)
            {
                _dynamicLightUploader.Clear(effect);
                return;
            }

            var terrain = World?.Terrain;
            var visibleLights = terrain?.VisibleLights;
            if (visibleLights == null || visibleLights.Count == 0)
            {
                _dynamicLightUploader.Clear(effect);
                return;
            }

            int maxLights = ResolveDynamicObjectLightBudget(worldTranslation);
            var focus = new Vector2(worldTranslation.X, worldTranslation.Y);
            float focusRadius = ResolveDynamicObjectLightFocusRadius();
            _dynamicLightUploader.Upload(effect, visibleLights, focus, maxLights, focusRadius);
        }

        private int ResolveDynamicObjectLightBudget(Vector3 worldTranslation)
        {
            bool isMonster = this is MonsterObject;
            int maxLights = isMonster
                ? (Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 2 : 4)
                : (Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 4 : 12);

            if (LowQuality)
            {
                maxLights = Math.Min(maxLights, isMonster
                    ? 1
                    : (Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 2 : 6));
            }

            var camera = Camera.Instance;
            if (camera == null)
                return Math.Max(1, maxLights);

            var camPos = camera.Position;
            float dx = camPos.X - worldTranslation.X;
            float dy = camPos.Y - worldTranslation.Y;
            float distSq = dx * dx + dy * dy;

            const float nearSq = 1500f * 1500f;
            const float midSq = 3200f * 3200f;
            const float farSq = 5200f * 5200f;

            if (distSq > farSq)
                maxLights = Math.Min(maxLights, Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 1 : 3);
            else if (distSq > midSq)
                maxLights = Math.Min(maxLights, isMonster
                    ? (Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 1 : 2)
                    : (Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 2 : 4));
            else if (distSq > nearSq)
                maxLights = Math.Min(maxLights, isMonster
                    ? (Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 2 : 3)
                    : (Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 3 : 6));

            return Math.Max(1, maxLights);
        }

        private float ResolveDynamicObjectLightFocusRadius()
        {
            var bounds = BoundingBoxWorld;
            Vector3 extent = (bounds.Max - bounds.Min) * 0.5f;
            float radius = MathF.Sqrt(extent.X * extent.X + extent.Y * extent.Y);

            if (!float.IsFinite(radius) || radius < 32f)
                return 32f;

            return radius;
        }
    }
}
