using Client.Main.Controls;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Client.Main.Objects
{
    /// <summary>
    /// Centralized hover-picking system. Called once per frame from WorldControl
    /// instead of per-object in WorldObject.Update, removing static state from the base class.
    /// </summary>
    internal static class WorldHoverSystem
    {
        private const int HoverChecksPerFrame = 32;

        private static int _hoverFrame = -1;
        private static int _hoverChecksThisFrame;

        public static void UpdateHover(IReadOnlyList<WorldObject> objects, BaseScene scene)
        {
            if (scene == null) return;

            // Reset all hover states each frame
            for (int i = 0; i < objects.Count; i++)
                objects[i].IsMouseHover = false;
            scene.MouseHoverObject = null;

            if (scene.MouseHoverControl is not null && scene.MouseHoverControl != scene.World)
                return;

            int frame = MuGame.FrameIndex;
            if (_hoverFrame != frame)
            {
                _hoverFrame = frame;
                _hoverChecksThisFrame = 0;
            }

            for (int i = 0; i < objects.Count && _hoverChecksThisFrame < HoverChecksPerFrame; i++)
            {
                var obj = objects[i];
                if (obj is not WalkerObject) continue;
                if (obj.Parent?.IsMouseHover == true)
                {
                    obj.IsMouseHover = true;
                    if (scene.MouseHoverObject is null)
                        scene.MouseHoverObject = obj;
                    continue;
                }
                TryCheckHover(obj, scene, isImportant: true);
            }

            // Second pass: other interactive objects (dropped items, etc.) up to budget
            for (int i = 0; i < objects.Count && _hoverChecksThisFrame < HoverChecksPerFrame; i++)
            {
                var obj = objects[i];
                if (obj is WalkerObject) continue;
                if (!obj.Interactive && !Constants.DRAW_BOUNDING_BOXES) continue;
                TryCheckHover(obj, scene, isImportant: false);
            }
        }

        private static void TryCheckHover(WorldObject obj, BaseScene scene, bool isImportant)
        {
            if (!isImportant && _hoverChecksThisFrame >= HoverChecksPerFrame)
            {
                obj.IsMouseHover = false;
                return;
            }

            _hoverChecksThisFrame++;
            float? intersectionDistance = MuGame.Instance.MouseRay.Intersects(obj.BoundingBoxWorld);
            ContainmentType contains = obj.BoundingBoxWorld.Contains(MuGame.Instance.MouseRay.Position);
            bool isHover = intersectionDistance.HasValue || contains == ContainmentType.Contains;

            obj.IsMouseHover = isHover;

            if (isHover && scene.MouseHoverObject is null)
                scene.MouseHoverObject = obj;
        }
    }
}
