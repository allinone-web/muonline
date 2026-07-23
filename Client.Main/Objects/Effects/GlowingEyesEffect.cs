using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Glowing eyes effect with motion trail. Tracks world-space positions of two eye bones
    /// and renders billboarded glow sprites at current positions plus a fading trail strip
    /// connecting recent positions (same technique as WeaponTrailEffect).
    ///
    /// Reusable — configure <see cref="LeftEyeBone"/>, <see cref="RightEyeBone"/> and visual
    /// settings per monster. The effect reads bone transforms from the parent ModelObject.
    ///
    /// Mirrors original MU SKULL eye glow:  BITMAP_LIGHT sprite at eye position
    ///                                    + BITMAP_SHINY sparkle overlay
    ///                                    + motion trail from head movement.
    /// </summary>
    public class GlowingEyesEffect : EffectObject
    {
        private const int MaxTrailSamples = 20;

        // --- per-eye trail sample buffers ---
        private readonly List<TrailSample> _leftSamples = new(MaxTrailSamples);
        private readonly List<TrailSample> _rightSamples = new(MaxTrailSamples);

        // --- trail geometry (rebuilt each frame) ---
        private readonly VertexPositionColor[] _trailVertices = new VertexPositionColor[MaxTrailSamples * 2];
        private readonly short[] _trailIndices = new short[(MaxTrailSamples - 1) * 6];

        // --- previous eye positions for delta sampling ---
        private Vector3 _prevLeftWorld;
        private Vector3 _prevRightWorld;
        private bool _hasPrevFrame;

        // --- procedural glow texture ---
        private Texture2D _glowTexture;
        private Vector2 _glowTexCenter;

        // --- time accumulator for per-frame sampling ---
        private float _timeSinceLastSample;

        // ========================================================================
        // Configuration (set before or during Load)
        // ========================================================================

        /// <summary>Bone index for the left eye.</summary>
        public int LeftEyeBone { get; set; } = -1;

        /// <summary>Bone index for the right eye.</summary>
        public int RightEyeBone { get; set; } = -1;

        /// <summary>Base glow color (alpha in GlowAlpha). Default: red-orange.</summary>
        public Color GlowColor { get; set; } = new Color(255, 50, 0);

        /// <summary>Scale of the glow sprite at each eye.</summary>
        public float GlowScale { get; set; } = 1.8f;

        /// <summary>Peak alpha of the glow sprite.</summary>
        public float GlowAlpha { get; set; } = 0.85f;

        /// <summary>Whether to draw the motion trail.</summary>
        public bool EnableTrail { get; set; } = true;

        /// <summary>How long (seconds) a trail sample lives before fading out.</summary>
        public float TrailDuration { get; set; } = 0.3f;

        /// <summary>Width of the trail strip (world units).</summary>
        public float TrailWidth { get; set; } = 3f;

        /// <summary>Minimum distance (world units) between consecutive trail samples.</summary>
        public float MinSampleDistance { get; set; } = 4f;

        /// <summary>Maximum time (seconds) between forced trail samples.</summary>
        public float MaxSampleInterval { get; set; } = 0.04f;

        /// <summary>Trail start alpha multiplier.</summary>
        public float TrailStartAlpha { get; set; } = 0.55f;

        /// <summary>Trail end alpha multiplier.</summary>
        public float TrailEndAlpha { get; set; } = 0f;

        // ========================================================================
        // Constructor
        // ========================================================================

        public GlowingEyesEffect()
        {
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

        // ========================================================================
        // Update — sample eye positions and age trail samples
        // ========================================================================

        public override void Update(GameTime gameTime)
        {
            var parentModel = Parent as ModelObject;

            // Mirror parent visibility
            if (parentModel != null)
            {
                Hidden = parentModel.Hidden || parentModel.Model == null;
                if (parentModel.LowQuality || Hidden)
                {
                    _leftSamples.Clear();
                    _rightSamples.Clear();
                    _hasPrevFrame = false;
                    base.Update(gameTime);
                    return;
                }
            }

            if (Hidden || LeftEyeBone < 0 || RightEyeBone < 0)
            {
                base.Update(gameTime);
                return;
            }

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt <= 0f)
            {
                base.Update(gameTime);
                return;
            }

            // --- Age and cull trail samples ---
            CullSamples(_leftSamples, dt);
            CullSamples(_rightSamples, dt);

            // --- Get current eye world positions ---
            if (!TryGetEyeWorldPositions(out Vector3 leftWorld, out Vector3 rightWorld))
            {
                base.Update(gameTime);
                return;
            }

            // --- Sample trail points ---
            if (!_hasPrevFrame)
            {
                AddSample(_leftSamples, leftWorld);
                AddSample(_rightSamples, rightWorld);
                _prevLeftWorld = leftWorld;
                _prevRightWorld = rightWorld;
                _hasPrevFrame = true;
                _timeSinceLastSample = 0f;
            }
            else
            {
                _timeSinceLastSample += dt;

                float leftDistSq = Vector3.DistanceSquared(leftWorld, _prevLeftWorld);
                float rightDistSq = Vector3.DistanceSquared(rightWorld, _prevRightWorld);
                float minDistSq = MinSampleDistance * MinSampleDistance;

                bool shouldSample = leftDistSq >= minDistSq
                                 || rightDistSq >= minDistSq
                                 || _timeSinceLastSample >= MaxSampleInterval;

                if (shouldSample)
                {
                    AddSample(_leftSamples, leftWorld);
                    AddSample(_rightSamples, rightWorld);
                    _prevLeftWorld = leftWorld;
                    _prevRightWorld = rightWorld;
                    _timeSinceLastSample = 0f;
                }
            }

            base.Update(gameTime);
        }

        // ========================================================================
        // DrawAfter — trail strip + glow sprites
        // ========================================================================

        public override void DrawAfter(GameTime gameTime)
        {
            if (Hidden || LeftEyeBone < 0 || RightEyeBone < 0)
            {
                base.DrawAfter(gameTime);
                return;
            }

            if (!TryGetEyeWorldPositions(out Vector3 leftWorld, out Vector3 rightWorld))
            {
                base.DrawAfter(gameTime);
                return;
            }

            var gd = GraphicsManager.Instance.GraphicsDevice;
            if (gd == null) { base.DrawAfter(gameTime); return; }

            var effect = GraphicsManager.Instance.BasicEffect3D;
            var camera = Camera.Instance;
            if (effect == null || camera == null) { base.DrawAfter(gameTime); return; }

            EnsureGlowTexture(gd);

            // Save GPU state
            var prevBlend = gd.BlendState;
            var prevDepth = gd.DepthStencilState;
            var prevRast = gd.RasterizerState;
            bool prevTex = effect.TextureEnabled;
            bool prevVC = effect.VertexColorEnabled;
            bool prevLight = effect.LightingEnabled;
            Matrix prevWorld = effect.World;
            Matrix prevView = effect.View;
            Matrix prevProj = effect.Projection;

            gd.BlendState = BlendState.Additive;
            gd.DepthStencilState = DepthStencilState.DepthRead;
            gd.RasterizerState = RasterizerState.CullNone;
            effect.TextureEnabled = false;
            effect.VertexColorEnabled = true;
            effect.LightingEnabled = false;
            effect.World = Matrix.Identity;
            effect.View = camera.View;
            effect.Projection = camera.Projection;

            // --- Draw trail strips ---
            if (EnableTrail)
            {
                DrawTrail(gd, effect, camera, _leftSamples);
                DrawTrail(gd, effect, camera, _rightSamples);
            }

            // Restore state for sprite drawing
            effect.TextureEnabled = prevTex;
            effect.VertexColorEnabled = prevVC;
            effect.LightingEnabled = prevLight;
            effect.World = prevWorld;
            effect.View = prevView;
            effect.Projection = prevProj;
            gd.BlendState = prevBlend;
            gd.DepthStencilState = prevDepth;
            gd.RasterizerState = prevRast;

            // --- Draw glow sprites at current eye positions (batched) ---
            DrawGlowSprites(gd, leftWorld, rightWorld, camera);

            base.DrawAfter(gameTime);
        }

        // ========================================================================
        // Internal helpers
        // ========================================================================

        private bool TryGetEyeWorldPositions(out Vector3 leftWorld, out Vector3 rightWorld)
        {
            leftWorld = Vector3.Zero;
            rightWorld = Vector3.Zero;

            if (Parent is not ModelObject parentModel)
                return false;

            var bones = parentModel.GetBoneTransforms();
            if (bones == null || LeftEyeBone >= bones.Length || RightEyeBone >= bones.Length)
                return false;

            Matrix world = parentModel.WorldPosition;
            leftWorld = Vector3.Transform(bones[LeftEyeBone].Translation, world);
            rightWorld = Vector3.Transform(bones[RightEyeBone].Translation, world);
            return true;
        }

        private static void CullSamples(List<TrailSample> samples, float dt)
        {
            for (int i = samples.Count - 1; i >= 0; i--)
            {
                var s = samples[i];
                s.Age += dt;
                if (s.Age >= s.MaxAge)
                    samples.RemoveAt(i);
                else
                    samples[i] = s;
            }
        }

        private void AddSample(List<TrailSample> samples, Vector3 worldPos)
        {
            if (samples.Count >= MaxTrailSamples)
                samples.RemoveAt(0);

            samples.Add(new TrailSample
            {
                Position = worldPos,
                Age = 0f,
                MaxAge = TrailDuration
            });
        }

        private void DrawTrail(GraphicsDevice gd, BasicEffect effect, Camera camera, List<TrailSample> samples)
        {
            int count = samples.Count;
            if (count < 2)
                return;

            // Build vertices
            int vertexCount = 0;
            for (int i = 0; i < count; i++)
            {
                var s = samples[i];
                float life = MathHelper.Clamp(1f - s.Age / s.MaxAge, 0f, 1f);

                // Direction for billboarding: use segment direction or camera-facing
                Vector3 dir;
                if (i == 0)
                    dir = samples[i + 1].Position - s.Position;
                else if (i == count - 1)
                    dir = s.Position - samples[i - 1].Position;
                else
                    dir = samples[i + 1].Position - samples[i - 1].Position;

                if (dir.LengthSquared() < 0.0001f)
                    dir = Vector3.Normalize(camera.Position - s.Position);
                else
                    dir.Normalize();

                Vector3 view = camera.Position - s.Position;
                view.Normalize();
                Vector3 side = Vector3.Cross(dir, view);
                if (side.LengthSquared() < 0.0001f)
                    side = Vector3.Cross(Vector3.Up, view);
                if (side.LengthSquared() < 0.0001f)
                    side = Vector3.Right;
                side.Normalize();

                float width = TrailWidth * MathHelper.Lerp(0.2f, 1f, life);
                Vector3 offset = side * (width * 0.5f);

                float alpha = MathHelper.Lerp(TrailEndAlpha, TrailStartAlpha, life);
                var colorVec = GlowColor.ToVector4() * alpha;
                var color = new Color(colorVec);

                int vi = i * 2;
                _trailVertices[vi] = new VertexPositionColor(s.Position + offset, color);
                _trailVertices[vi + 1] = new VertexPositionColor(s.Position - offset, color);
                vertexCount += 2;
            }

            // Build indices
            int primCount = (count - 1) * 2;
            for (int i = 0; i < count - 1; i++)
            {
                int baseVert = i * 2;
                int nextVert = (i + 1) * 2;
                int idx = i * 6;
                _trailIndices[idx] = (short)baseVert;
                _trailIndices[idx + 1] = (short)(baseVert + 1);
                _trailIndices[idx + 2] = (short)nextVert;
                _trailIndices[idx + 3] = (short)(baseVert + 1);
                _trailIndices[idx + 4] = (short)(nextVert + 1);
                _trailIndices[idx + 5] = (short)nextVert;
            }

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _trailVertices, 0, vertexCount,
                    _trailIndices, 0, primCount);
            }
        }

        private void DrawGlowSprites(GraphicsDevice gd, Vector3 leftWorld, Vector3 rightWorld, Camera camera)
        {
            if (_glowTexture == null)
                return;

            var spriteBatch = GraphicsManager.Instance.Sprite;
            if (spriteBatch == null)
                return;

            var viewProj = camera.View * camera.Projection;
            var vp = gd.Viewport;

            // Project both eyes to screen
            if (!ProjectToScreen(leftWorld, viewProj, vp, out var leftScreen, out float leftDepth, out float leftScale))
                return;
            if (!ProjectToScreen(rightWorld, viewProj, vp, out var rightScreen, out float rightDepth, out float rightScale))
                return;

            Color color = GlowColor * GlowAlpha;

            spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.Additive,
                SamplerState.LinearClamp,
                DepthStencilState.DepthRead,
                RasterizerState.CullNone);

            spriteBatch.Draw(_glowTexture, leftScreen, null, color, 0f, _glowTexCenter, leftScale, SpriteEffects.None, leftDepth);
            spriteBatch.Draw(_glowTexture, rightScreen, null, color, 0f, _glowTexCenter, rightScale, SpriteEffects.None, rightDepth);

            spriteBatch.End();
        }

        private bool ProjectToScreen(Vector3 worldPos, Matrix viewProj, Viewport vp, out Vector2 screen, out float depth, out float scale)
        {
            screen = Vector2.Zero;
            depth = 0f;
            scale = 0f;

            Vector4 clipPos = Vector4.Transform(worldPos, viewProj);
            if (clipPos.W <= 0.001f)
                return false;

            float invW = 1f / clipPos.W;
            screen = new Vector2(
                (clipPos.X * invW * 0.5f + 0.5f) * vp.Width,
                (0.5f - clipPos.Y * invW * 0.5f) * vp.Height);
            depth = clipPos.Z * invW;

            if (depth < 0f || depth > 1f)
                return false;

            float distScale = MathHelper.Clamp(800f / clipPos.W, 0.3f, 3f);
            scale = GlowScale * distScale * Constants.RENDER_SCALE;
            return true;
        }

        private void EnsureGlowTexture(GraphicsDevice gd)
        {
            if (_glowTexture != null)
                return;

            const int size = 64;
            _glowTexture = new Texture2D(gd, size, size);
            var pixels = new Color[size * size];
            float center = size * 0.5f;
            float maxRadius = center;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center + 0.5f;
                    float dy = y - center + 0.5f;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    float t = MathHelper.Clamp(1f - dist / maxRadius, 0f, 1f);
                    // Soft glow: smoothstep falloff
                    float alpha = t * t * (3f - 2f * t);
                    alpha *= alpha; // extra softness
                    pixels[y * size + x] = new Color((byte)255, (byte)255, (byte)255, (byte)(alpha * 255f));
                }
            }

            _glowTexture.SetData(pixels);
            _glowTexCenter = new Vector2(size * 0.5f, size * 0.5f);
        }

        public override void Dispose()
        {
            _glowTexture?.Dispose();
            _glowTexture = null;
            base.Dispose();
        }

        // ========================================================================
        // Trail sample struct
        // ========================================================================

        private struct TrailSample
        {
            public Vector3 Position;
            public float Age;
            public float MaxAge;
        }
    }
}
