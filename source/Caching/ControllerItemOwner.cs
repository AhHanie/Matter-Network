using System.Collections.Generic;
using Verse;

namespace SK_Matter_Network
{
    public class ControllerItemOwner : ThingOwner<Thing>
    {
        private NetworkBuildingController ctrl;

        public ControllerItemOwner() { }

        public ControllerItemOwner(NetworkBuildingController owner) : base(owner, oneStackOnly: false)
        {
            ctrl = owner;
            dontTickContents = true;
        }

        public override bool TryAdd(Thing item, bool canMergeWithExistingStacks = true)
        {
            if (item == null || item.Destroyed)
                return false;

            DataNetwork network = ctrl?.ParentNetwork;

            if (canMergeWithExistingStacks)
            {
                for (int i = 0; i < InnerListForReading.Count; i++)
                {
                    Thing existing = InnerListForReading[i];
                    if (!existing.CanStackWith(item))
                        continue;

                    int absorbed = item.stackCount;
                    existing.TryAbsorbStack(item, respectStackLimit: false);
                    NotifyAddedAndMergedWith(existing, absorbed);
                    network?.MarkBytesDirty();
                    return true;
                }
            }

            bool result = base.TryAdd(item, canMergeWithExistingStacks: false);
            if (result && !item.Destroyed && item.stackCount > 0)
            {
                network?.storedItems.Add(item);
                network?.MarkBytesDirty();
            }
            return result;
        }
    }
}
