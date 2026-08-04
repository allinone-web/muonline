using System;
using System.Collections.Generic;

namespace Client.Main.Graphics
{
    /// <summary>
    /// Shares immutable triangle-list indices for independent four-vertex quads.
    /// Effects can keep only their mutable vertex arrays instead of allocating and
    /// repeatedly rebuilding identical index arrays for every instance.
    /// </summary>
    internal static class QuadIndexCache
    {
        private const int MaxShortQuadCount = short.MaxValue / 4;
        private static readonly Dictionary<int, short[]> Cache = new();
        private static readonly object Sync = new();

        public static short[] Get(int quadCapacity)
        {
            if (quadCapacity <= 0)
                return Array.Empty<short>();
            if (quadCapacity > MaxShortQuadCount)
                throw new ArgumentOutOfRangeException(nameof(quadCapacity));

            lock (Sync)
            {
                if (Cache.TryGetValue(quadCapacity, out short[] indices))
                    return indices;

                indices = Create(quadCapacity);
                Cache.Add(quadCapacity, indices);
                return indices;
            }
        }

        private static short[] Create(int quadCapacity)
        {
            var indices = new short[quadCapacity * 6];
            for (int quad = 0; quad < quadCapacity; quad++)
            {
                int vertex = quad * 4;
                int index = quad * 6;
                indices[index] = (short)vertex;
                indices[index + 1] = (short)(vertex + 1);
                indices[index + 2] = (short)(vertex + 2);
                indices[index + 3] = (short)vertex;
                indices[index + 4] = (short)(vertex + 2);
                indices[index + 5] = (short)(vertex + 3);
            }

            return indices;
        }
    }
}
