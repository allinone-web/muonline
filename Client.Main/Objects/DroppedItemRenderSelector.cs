using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects
{
    /// <summary>
    /// Per-frame selector that decides which dropped items render visuals.
    /// When many drops stack on the same tile, only one representative renders.
    /// When unique tiles exceed the budget, stride-based selection caps the total.
    /// Owned by WorldControl; runs once per frame before Draw.
    /// </summary>
    internal sealed class DroppedItemRenderSelector
    {
        private const int RenderCullStartCount = 80;
        private const int MaxRenderedModelsPerFrame = 220;
        private const double FrameTimeMs = 1000.0 / 60.0;

        private readonly Dictionary<int, ushort> _tileSelectedRawId = new(512);
        private uint _cullFrameId = uint.MaxValue;
        private uint _strideFrameId = uint.MaxValue;
        private int _globalStride = 1;

        /// <summary>
        /// Call once per frame before drawing dropped items.
        /// Sets DroppedItemObject.RenderVisuals on each item.
        /// </summary>
        public void SelectRenderableItems(IReadOnlyList<DroppedItemObject> items, GameTime gameTime)
        {
            if (items.Count < RenderCullStartCount)
            {
                // Not enough items to bother culling — show all visuals.
                for (int i = 0; i < items.Count; i++)
                    items[i].RenderVisuals = true;
                return;
            }

            uint frameId = (uint)(gameTime.TotalGameTime.TotalMilliseconds / FrameTimeMs);
            bool newFrame = _cullFrameId != frameId;
            if (newFrame)
            {
                _cullFrameId = frameId;
                _tileSelectedRawId.Clear();
                _strideFrameId = uint.MaxValue;
                _globalStride = 1;
            }

            // Pass 1: select best RawId per tile
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!item.Visible) continue;

                int tileKey = item.TileKey;
                if (_tileSelectedRawId.TryGetValue(tileKey, out ushort selected))
                {
                    if (item.RawId < selected)
                        _tileSelectedRawId[tileKey] = item.RawId;
                }
                else
                {
                    _tileSelectedRawId[tileKey] = item.RawId;
                }
            }

            // Compute stride if tile count exceeds budget
            if (newFrame)
            {
                int tileCount = _tileSelectedRawId.Count;
                _globalStride = tileCount > MaxRenderedModelsPerFrame
                    ? (int)MathF.Ceiling(tileCount / (float)MaxRenderedModelsPerFrame)
                    : 1;
                _strideFrameId = frameId;
            }

            // Pass 2: mark each item
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!item.Visible)
                {
                    item.RenderVisuals = false;
                    continue;
                }

                if (!_tileSelectedRawId.TryGetValue(item.TileKey, out ushort selected) || selected != item.RawId)
                {
                    item.RenderVisuals = false;
                    continue;
                }

                item.RenderVisuals = _globalStride <= 1 || (item.RawId % _globalStride) == 0;
            }
        }
    }
}
