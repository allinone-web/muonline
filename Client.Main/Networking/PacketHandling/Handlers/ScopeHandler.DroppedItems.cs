using Client.Main.Core.Models;
using System.Collections.Concurrent;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    public partial class ScopeHandler
    {
        private static readonly ConcurrentQueue<DroppedItemWorkItem> _droppedItemQueue = new();
        private static int _droppedItemWorkerRunning;

        private readonly struct DroppedItemWorkItem
        {
            public DroppedItemWorkItem(
                ScopeObject dropObj,
                ushort maskedId,
                string soundPath,
                bool isFreshDrop)
            {
                DropObject = dropObj;
                MaskedId = maskedId;
                SoundPath = soundPath;
                IsFreshDrop = isFreshDrop;
            }

            public ScopeObject DropObject { get; }
            public ushort MaskedId { get; }
            public string SoundPath { get; }
            public bool IsFreshDrop { get; }
        }
    }
}
