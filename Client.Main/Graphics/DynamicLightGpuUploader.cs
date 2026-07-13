using Client.Main.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Client.Main.Graphics
{
    /// <summary>
    /// Shared dynamic-light selection and GPU upload path for terrain, objects and instancing.
    /// </summary>
    public sealed class DynamicLightGpuUploader
    {
        private sealed class EffectBindings
        {
            public EffectBindings(Effect effect)
            {
                LightPosInvRadius = effect.Parameters["LightPosInvRadius"];
                LightColorIntensity = effect.Parameters["LightColorIntensity"];
            }

            public EffectParameter LightPosInvRadius { get; }
            public EffectParameter LightColorIntensity { get; }

            public int ResolveCapacity(int fallbackCapacity)
            {
                int fallback = Math.Max(1, fallbackCapacity);
                int positionCapacity = GetEffectArrayCapacity(LightPosInvRadius, fallback);
                int colorCapacity = GetEffectArrayCapacity(LightColorIntensity, fallback);
                return Math.Max(1, Math.Min(positionCapacity, colorCapacity));
            }
        }

        private static readonly ConditionalWeakTable<Effect, EffectBindings> _effectBindings = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static EffectBindings GetBindings(Effect effect) =>
            _effectBindings.GetValue(effect, static value => new EffectBindings(value));

        private readonly int _fallbackCapacity;
        private readonly float _minInfluence;

        private Vector4[] _lightPosInvRadius = Array.Empty<Vector4>();
        private Vector4[] _lightColorIntensity = Array.Empty<Vector4>();
        private int[] _selectedIndices = Array.Empty<int>();
        private float[] _selectedScores = Array.Empty<float>();

        public DynamicLightGpuUploader(int fallbackCapacity = 32, float minInfluence = 0.001f)
        {
            _fallbackCapacity = Math.Max(1, fallbackCapacity);
            _minInfluence = Math.Max(0f, minInfluence);
        }

        public int Upload(Effect effect, IReadOnlyList<DynamicLightSnapshot> lights, Vector2 focus, int maxLights, float focusRadius = 0f)
        {
            if (effect == null)
                return 0;

            int capacity = ResolveEffectCapacity(effect, _fallbackCapacity);
            EnsureCapacity(capacity);
            ClearBuffers(capacity);

            if (lights == null || lights.Count == 0 || maxLights <= 0)
            {
                ApplyToEffect(effect, capacity);
                return 0;
            }

            int budget = Math.Min(capacity, maxLights);
            int selectedCount = SelectRelevantLights(lights, focus, Math.Max(0f, focusRadius), budget);

            for (int i = 0; i < selectedCount; i++)
            {
                int idx = _selectedIndices[i];
                if ((uint)idx >= (uint)lights.Count)
                    continue;

                var light = lights[idx];
                float radius = Math.Max(light.Radius, 0.0001f);
                float intensity = Math.Max(0f, light.Intensity);
                _lightPosInvRadius[i] = new Vector4(light.Position, 1f / radius);
                _lightColorIntensity[i] = new Vector4(light.Color, intensity);
            }

            ApplyToEffect(effect, capacity);
            return selectedCount;
        }

        public void Clear(Effect effect)
        {
            if (effect == null)
                return;

            int capacity = ResolveEffectCapacity(effect, _fallbackCapacity);
            EnsureCapacity(capacity);
            ClearBuffers(capacity);
            ApplyToEffect(effect, capacity);
        }

        public static int ResolveEffectCapacity(Effect effect, int fallbackCapacity)
        {
            if (effect == null)
                return Math.Max(1, fallbackCapacity);

            return GetBindings(effect).ResolveCapacity(fallbackCapacity);
        }

        private static int GetEffectArrayCapacity(EffectParameter parameter, int fallback)
        {
            if (parameter?.Elements == null || parameter.Elements.Count <= 0)
                return fallback;

            return parameter.Elements.Count;
        }

        private void EnsureCapacity(int capacity)
        {
            if (_lightPosInvRadius.Length != capacity)
            {
                _lightPosInvRadius = new Vector4[capacity];
                _lightColorIntensity = new Vector4[capacity];
            }

            if (_selectedIndices.Length != capacity)
            {
                _selectedIndices = new int[capacity];
                _selectedScores = new float[capacity];
            }
        }

        private void ClearBuffers(int capacity)
        {
            Array.Clear(_lightPosInvRadius, 0, capacity);
            Array.Clear(_lightColorIntensity, 0, capacity);
        }

        private static bool IsFinite(in Vector3 value)
        {
            return !(float.IsNaN(value.X) || float.IsInfinity(value.X) ||
                     float.IsNaN(value.Y) || float.IsInfinity(value.Y) ||
                     float.IsNaN(value.Z) || float.IsInfinity(value.Z));
        }

        private int SelectRelevantLights(IReadOnlyList<DynamicLightSnapshot> lights, Vector2 focus, float focusRadius, int budget)
        {
            if (budget <= 0)
                return 0;

            int selected = 0;
            float weakestScore = float.MaxValue;
            int weakestIndex = 0;

            for (int i = 0; i < lights.Count; i++)
            {
                var light = lights[i];
                if (light.Intensity <= 0f || !IsFinite(light.Position) || !IsFinite(light.Color))
                    continue;

                float radius = light.Radius;
                float radiusSq = radius * radius;
                if (radiusSq <= 0.0001f)
                    continue;

                var lightPos = new Vector2(light.Position.X, light.Position.Y);
                float distSq = Vector2.DistanceSquared(lightPos, focus);
                float combinedRadius = radius + focusRadius;
                float combinedRadiusSq = combinedRadius * combinedRadius;
                if (distSq >= combinedRadiusSq)
                    continue;

                float edgeDistance = 0f;
                if (focusRadius > 0f)
                {
                    float dist = MathF.Sqrt(distSq);
                    edgeDistance = MathF.Max(0f, dist - focusRadius);
                }
                else
                {
                    edgeDistance = MathF.Sqrt(distSq);
                }

                float edgeDistanceSq = edgeDistance * edgeDistance;
                float score = (1f - edgeDistanceSq / radiusSq) * light.Intensity;
                if (score <= _minInfluence)
                    continue;

                if (selected < budget)
                {
                    _selectedIndices[selected] = i;
                    _selectedScores[selected] = score;
                    if (score < weakestScore)
                    {
                        weakestScore = score;
                        weakestIndex = selected;
                    }

                    selected++;
                }
                else if (score > weakestScore)
                {
                    _selectedIndices[weakestIndex] = i;
                    _selectedScores[weakestIndex] = score;

                    weakestScore = _selectedScores[0];
                    weakestIndex = 0;
                    for (int j = 1; j < selected; j++)
                    {
                        float s = _selectedScores[j];
                        if (s < weakestScore)
                        {
                            weakestScore = s;
                            weakestIndex = j;
                        }
                    }
                }
            }

            SortSelectedByScoreDesc(selected);
            return selected;
        }

        private void SortSelectedByScoreDesc(int count)
        {
            for (int i = 1; i < count; i++)
            {
                float keyScore = _selectedScores[i];
                int keyIdx = _selectedIndices[i];
                int j = i - 1;
                while (j >= 0 && _selectedScores[j] < keyScore)
                {
                    _selectedScores[j + 1] = _selectedScores[j];
                    _selectedIndices[j + 1] = _selectedIndices[j];
                    j--;
                }

                _selectedScores[j + 1] = keyScore;
                _selectedIndices[j + 1] = keyIdx;
            }
        }

        private void ApplyToEffect(Effect effect, int capacity)
        {
            if (effect == null || capacity <= 0)
                return;

            EffectBindings bindings = GetBindings(effect);
            bindings.LightPosInvRadius?.SetValue(_lightPosInvRadius);
            bindings.LightColorIntensity?.SetValue(_lightColorIntensity);
        }
    }
}
