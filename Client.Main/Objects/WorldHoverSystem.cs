using Client.Main.Controls;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Client.Main.Objects
{
    /// <summary>
    /// Centralized hover-picking system. The previous hover target is cleared directly,
    /// avoiding an O(n) reset of every visible object each frame. Candidate tests are
    /// split between walkers and other interactive objects and scanned round-robin so
    /// objects later in the visible list are not permanently starved.
    /// </summary>
    internal static class WorldHoverSystem
    {
        private const int WalkerChecksPerFrame = 24;
        private const int OtherChecksPerFrame = 8;

        private static WorldObject _previousHovered;
        private static int _walkerScanOffset;
        private static int _otherScanOffset;

        public static void UpdateHover(IReadOnlyList<WorldObject> objects, BaseScene scene)
        {
            if (scene == null)
                return;

            ClearPreviousHover();
            scene.MouseHoverObject = null;

            if (scene.MouseHoverControl is not null && scene.MouseHoverControl != scene.World)
                return;

            if (objects == null || objects.Count == 0)
                return;

            Ray mouseRay = MuGame.Instance.MouseRay;
            WorldObject bestObject = null;
            float bestDistance = float.MaxValue;

            // Keep the current target responsive even when the round-robin cursor is
            // currently scanning another section of a large crowd.
            if (_previousHovered != null &&
                _previousHovered.Visible &&
                ReferenceEquals(_previousHovered.World, scene.World))
                TrySelectCandidate(_previousHovered, mouseRay, ref bestObject, ref bestDistance);

            ScanCategory(
                objects,
                walkers: true,
                WalkerChecksPerFrame,
                mouseRay,
                ref _walkerScanOffset,
                ref bestObject,
                ref bestDistance);

            ScanCategory(
                objects,
                walkers: false,
                OtherChecksPerFrame,
                mouseRay,
                ref _otherScanOffset,
                ref bestObject,
                ref bestDistance);

            if (bestObject == null)
            {
                _previousHovered = null;
                return;
            }

            bestObject.IsMouseHover = true;
            scene.MouseHoverObject = bestObject;
            _previousHovered = bestObject;
        }

        private static void ClearPreviousHover()
        {
            if (_previousHovered != null)
                _previousHovered.IsMouseHover = false;
        }

        private static void ScanCategory(
            IReadOnlyList<WorldObject> objects,
            bool walkers,
            int budget,
            Ray mouseRay,
            ref int scanOffset,
            ref WorldObject bestObject,
            ref float bestDistance)
        {
            int count = objects.Count;
            if (count == 0 || budget <= 0)
                return;

            int start = scanOffset % count;
            int checkedCandidates = 0;
            int visited = 0;

            while (visited < count && checkedCandidates < budget)
            {
                int index = (start + visited) % count;
                visited++;

                WorldObject obj = objects[index];
                if (obj == null || ReferenceEquals(obj, _previousHovered) || !obj.Visible)
                    continue;

                bool isWalker = obj is WalkerObject;
                if (isWalker != walkers)
                    continue;

                if (!walkers && !obj.Interactive && !Constants.DRAW_BOUNDING_BOXES)
                    continue;

                checkedCandidates++;
                TrySelectCandidate(obj, mouseRay, ref bestObject, ref bestDistance);
            }

            scanOffset = (start + Math.Max(1, visited)) % count;
        }

        private static void TrySelectCandidate(
            WorldObject obj,
            Ray mouseRay,
            ref WorldObject bestObject,
            ref float bestDistance)
        {
            float? intersectionDistance = mouseRay.Intersects(obj.BoundingBoxWorld);
            bool containsOrigin = obj.BoundingBoxWorld.Contains(mouseRay.Position) == ContainmentType.Contains;

            if (!intersectionDistance.HasValue && !containsOrigin)
                return;

            float distance = containsOrigin ? 0f : intersectionDistance.Value;
            if (distance >= bestDistance)
                return;

            bestDistance = distance;
            bestObject = obj;
        }
    }
}
