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
        /// 切換 render target 之前，把目前開著的批次<b>結束掉</b>；離開時再開回來。
        ///
        /// <para>
        /// 為什麼一定要這樣做：SpriteBatch 是延遲送出的，Begin 之後排進去的東西
        /// 要到 End 才真正畫下去 —— 而畫到<b>那一刻綁定的 render target</b>。
        /// 直接 SetRenderTarget 再開新的批次，會讓外層批次在切換之後才被 End
        /// （SpriteBatchScope 的建構子遇到狀態不同的外層就會先 End 它），
        /// 於是外層排隊中的所有東西全部被畫進那張新的 render target 裡面。
        /// </para>
        ///
        /// <para>
        /// 實際症狀：小地圖把自己畫進一張 render target，而畫面上的聊天訊息
        /// 「testgmDw entered the game.」就這樣被烤進地圖的貼圖裡，
        /// 跟著地圖一起顯示在畫面中央 —— 使用者回報的「map 和 note 的文字合併重疊」。
        /// 這不是滑鼠時代的 bug，是 render target 與批次的順序問題。
        /// </para>
        ///
        /// 用法：
        /// <code>
        /// using (var section = SpriteBatchScope.BeginRenderTarget(gd, target))
        /// {
        ///     gd.Clear(Color.Transparent);
        ///     using (new SpriteBatchScope(sprite, ...)) { ... }
        /// }
        /// </code>
        /// </summary>
        public static RenderTargetSection BeginRenderTarget(GraphicsDevice device, RenderTarget2D target)
            => new(device, target);

        /// <summary>見 <see cref="BeginRenderTarget"/>。</summary>
        public readonly struct RenderTargetSection : IDisposable
        {
            private readonly GraphicsDevice _device;
            private readonly RenderTargetBinding[] _previousTargets;
            private readonly bool _suspended;

            internal RenderTargetSection(GraphicsDevice device, RenderTarget2D target)
            {
                _device = device;

                // 先把外層批次送出去 —— 此時綁定的還是原本的 target，內容才會畫對地方。
                var stack = Stack;
                _suspended = stack.Count > 0;
                if (_suspended)
                {
                    ScopeEntry current = stack.Peek();
                    current.State.End(current.Batch);
                }

                _previousTargets = device.GetRenderTargets();
                device.SetRenderTarget(target);
            }

            public void Dispose()
            {
                _device.SetRenderTargets(_previousTargets);

                if (!_suspended)
                    return;

                var stack = Stack;
                if (stack.Count == 0)
                    return;

                ScopeEntry current = stack.Peek();
                current.State.Begin(current.Batch);
            }
        }

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
