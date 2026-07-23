using Client.Main.Objects;
using System;
using Client.Main.Controllers;

namespace Client.Main.Controls
{
    public static class WorldMutationQueue
    {
        public static void Add(WorldControl world, WorldObject obj)
        {
            if (world == null || obj == null)
                return;

            world.Objects.Add(obj);
        }

        public static bool Remove(WorldControl world, WorldObject obj)
        {
            if (world == null || obj == null)
                return false;

            return world.Objects.Remove(obj);
        }

        public static bool RemoveAndDispose(WorldControl world, WorldObject obj)
        {
            var removed = Remove(world, obj);
            obj?.Dispose();
            return removed;
        }

        public static bool RemoveAndRecycle(WorldControl world, DroppedItemObject obj)
        {
            var removed = Remove(world, obj);
            obj?.Recycle();
            return removed;
        }

        public static void ScheduleRemoveAndRecycle(WorldControl world, DroppedItemObject obj)
        {
            MuGame.ScheduleOnMainThread(
                () => RemoveAndRecycle(world, obj),
                MainThreadDispatcher.WorkPriority.Critical);
        }

        public static void ScheduleRemoveAndRecycle(WorldControl world, DroppedItemObject obj, Action afterMutation)
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                RemoveAndRecycle(world, obj);
                afterMutation?.Invoke();
            }, MainThreadDispatcher.WorkPriority.Critical);
        }
    }
}
