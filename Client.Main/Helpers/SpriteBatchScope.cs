using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Helpers
{
    /// <summary>
    /// Manages nested SpriteBatch.Begin/End calls while preserving the previous batch state.
    /// Identical nested states reuse the active batch instead of forcing another End/Begin pair.
    /// </summary>
    public struct SpriteBatchScope : IDisposable
    {
        [ThreadStatic]
        private static Stack<ScopeEntry> _threadStack;

        private readonly SpriteBatch _batch;
        private readonly SavedState _myState;
        private readonly DepthStencilState _prevDepth;
        private readonly RasterizerState _prevRaster;
        private readonly SamplerState _prevSampler;
        private readonly bool _ownsTransition;
        private readonly bool _active;

        private static Stack<ScopeEntry> Stack => _threadStack ??= new Stack<ScopeEntry>(8);

        /// <summary>
        /// True if a SpriteBatch is currently open on this thread.
        /// </summary>
        public static bool BatchIsBegun => Stack.Count > 0;

        /// <summary>
        /// Clears a corrupted nested SpriteBatch scope after a contained render exception.
        /// Only the currently active batch is ended; all bookkeeping entries are then removed
        /// so the next object starts from a known state.
        /// </summary>
        internal static void ForceReset()
        {
            var stack = Stack;
            if (stack.Count == 0)
                return;

            try
            {
                ScopeEntry current = stack.Peek();
                current.State.End(current.Batch);
            }
            catch
            {
                // The batch may already have been ended by the failing object. Clearing the
                // bookkeeping stack is still required to allow recovery on the next draw.
            }
            finally
            {
                stack.Clear();
            }
        }

        public SpriteBatchScope(
            SpriteBatch batch,
            SpriteSortMode sort = SpriteSortMode.Deferred,
            BlendState blend = null,
            SamplerState sampler = null,
            DepthStencilState depth = null,
            RasterizerState raster = null,
            Effect effect = null,
            Matrix? transform = null)
        {
            _batch = batch ?? throw new ArgumentNullException(nameof(batch));

            var gd = batch.GraphicsDevice;
            _prevDepth = gd.DepthStencilState;
            _prevRaster = gd.RasterizerState;
            _prevSampler = gd.SamplerStates[0];

            _myState = new SavedState(
                sort,
                blend ?? BlendState.AlphaBlend,
                sampler ?? Controllers.GraphicsManager.GetQualitySamplerState(),
                depth,
                raster,
                effect,
                transform);

            var stack = Stack;
            if (stack.Count > 0)
            {
                ScopeEntry current = stack.Peek();
                if (ReferenceEquals(current.Batch, batch) && current.State.Equals(_myState))
                {
                    stack.Push(new ScopeEntry(batch, _myState, ownsTransition: false));
                    _ownsTransition = false;
                    _active = true;
                    return;
                }

                // End the batch that is actually active. The old implementation used
                // the newly requested batch here, which is incorrect when two different
                // SpriteBatch instances are nested.
                current.State.End(current.Batch);
            }

            _myState.Begin(batch);
            stack.Push(new ScopeEntry(batch, _myState, ownsTransition: true));
            _ownsTransition = true;
            _active = true;
        }

        public void Dispose()
        {
            if (!_active)
                return;

            var stack = Stack;
            if (stack.Count == 0)
                return;

            ScopeEntry entry = stack.Pop();
            if (!_ownsTransition || !entry.OwnsTransition)
                return;

            entry.State.End(entry.Batch);

            var gd = entry.Batch.GraphicsDevice;
            gd.DepthStencilState = _prevDepth;
            gd.RasterizerState = _prevRaster;
            gd.SamplerStates[0] = _prevSampler;

            if (stack.Count > 0)
            {
                ScopeEntry previous = stack.Peek();
                previous.State.Begin(previous.Batch);
            }
        }

        private readonly struct ScopeEntry
        {
            public ScopeEntry(SpriteBatch batch, SavedState state, bool ownsTransition)
            {
                Batch = batch;
                State = state;
                OwnsTransition = ownsTransition;
            }

            public SpriteBatch Batch { get; }
            public SavedState State { get; }
            public bool OwnsTransition { get; }
        }

        private readonly struct SavedState : IEquatable<SavedState>
        {
            public SavedState(
                SpriteSortMode sort,
                BlendState blend,
                SamplerState sampler,
                DepthStencilState depth,
                RasterizerState raster,
                Effect effect,
                Matrix? transform)
            {
                Sort = sort;
                Blend = blend;
                Sampler = sampler;
                Depth = depth;
                Rasterizer = raster;
                Effect = effect;
                Transform = transform;
            }

            public SpriteSortMode Sort { get; }
            public BlendState Blend { get; }
            public SamplerState Sampler { get; }
            public DepthStencilState Depth { get; }
            public RasterizerState Rasterizer { get; }
            public Effect Effect { get; }
            public Matrix? Transform { get; }

            public bool Equals(SavedState other)
            {
                return Sort == other.Sort &&
                       ReferenceEquals(Blend, other.Blend) &&
                       ReferenceEquals(Sampler, other.Sampler) &&
                       ReferenceEquals(Depth, other.Depth) &&
                       ReferenceEquals(Rasterizer, other.Rasterizer) &&
                       ReferenceEquals(Effect, other.Effect) &&
                       Nullable.Equals(Transform, other.Transform);
            }

            public override bool Equals(object obj) => obj is SavedState other && Equals(other);

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    Sort,
                    Blend,
                    Sampler,
                    Depth,
                    Rasterizer,
                    Effect,
                    Transform);
            }

            public void Begin(SpriteBatch batch)
            {
                batch.Begin(
                    Sort,
                    Blend,
                    Sampler,
                    Depth,
                    Rasterizer,
                    Effect,
                    Transform);
            }

            public void End(SpriteBatch batch)
            {
                batch.End();
            }
        }
    }
}
