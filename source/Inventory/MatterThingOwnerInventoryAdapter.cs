using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public class MatterThingOwnerInventoryAdapter : IMatterInventoryAdapter
    {
        protected readonly Thing parentThing;
        protected readonly ThingOwner owner;
        private readonly bool respectStoreSettings;
        private readonly bool respectHaulDestination;

        public Thing ParentThing => parentThing;
        public string Label => parentThing.LabelCap;
        public bool IsValid => parentThing.Spawned && !parentThing.Destroyed && owner != null;

        public MatterThingOwnerInventoryAdapter(Thing parentThing, ThingOwner owner, bool respectStoreSettings = true, bool respectHaulDestination = true)
        {
            this.parentThing = parentThing;
            this.owner = owner;
            this.respectStoreSettings = respectStoreSettings;
            this.respectHaulDestination = respectHaulDestination;
        }

        public IEnumerable<Thing> GetStoredThings()
        {
            for (int i = 0; i < owner.Count; i++)
            {
                yield return owner[i];
            }
        }

        public bool CanPull(Thing thing, int count)
        {
            return IsValid && thing != null && !thing.Destroyed && count > 0 && owner.Contains(thing);
        }

        public int PullTo(Thing thing, int count, ThingOwner destination)
        {
            if (!CanPull(thing, count) || destination == null)
            {
                return 0;
            }

            return owner.TryTransferToContainer(thing, destination, count);
        }

        public bool Accepts(Thing thing)
        {
            if (!IsValid || thing == null || thing.Destroyed)
            {
                return false;
            }

            if (respectHaulDestination && parentThing is IHaulDestination haulDestination && !haulDestination.Accepts(thing))
            {
                return false;
            }

            if (respectStoreSettings && parentThing is IStoreSettingsParent storeSettingsParent && !storeSettingsParent.GetStoreSettings().AllowedToAccept(thing))
            {
                return false;
            }

            return owner.CanAcceptAnyOf(thing);
        }

        public int CountCanAccept(Thing thing)
        {
            if (!Accepts(thing))
            {
                return 0;
            }

            return owner.GetCountCanAccept(thing);
        }

        public int PushFrom(Thing thing, int count)
        {
            if (!Accepts(thing) || count <= 0 || thing.holdingOwner == null)
            {
                return 0;
            }

            return thing.holdingOwner.TryTransferToContainer(thing, owner, count);
        }

        public string GetStatus()
        {
            return "MN_MatterIOPortStatusThingOwner".Translate(Label, owner.Count);
        }
    }
}
