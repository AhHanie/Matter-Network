using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public class CompProperties_MatterThingOwnerAdapter : CompProperties
    {
        public bool respectStoreSettings = true;
        public bool respectIHaulDestination = true;

        public CompProperties_MatterThingOwnerAdapter()
        {
            compClass = typeof(CompMatterThingOwnerAdapter);
        }
    }

    public class CompMatterThingOwnerAdapter : CompMatterInventoryAdapter
    {
        private ThingOwner Owner => parent.TryGetInnerInteractableThingOwner();
        private CompProperties_MatterThingOwnerAdapter Props => (CompProperties_MatterThingOwnerAdapter)props;

        public override bool IsValid => parent.Spawned && !parent.Destroyed && Owner != null;

        public override System.Collections.Generic.IEnumerable<Thing> GetStoredThings()
        {
            ThingOwner owner = Owner;
            for (int i = 0; i < owner.Count; i++)
            {
                yield return owner[i];
            }
        }

        public override bool CanPull(Thing thing, int count)
        {
            ThingOwner owner = Owner;
            return IsValid && thing != null && !thing.Destroyed && count > 0 && owner.Contains(thing);
        }

        public override int PullTo(Thing thing, int count, ThingOwner destination)
        {
            ThingOwner owner = Owner;
            if (!CanPull(thing, count) || destination == null)
            {
                return 0;
            }

            return owner.TryTransferToContainer(thing, destination, count);
        }

        public override bool Accepts(Thing thing)
        {
            if (!IsValid || thing == null || thing.Destroyed)
            {
                return false;
            }

            if (Props.respectIHaulDestination && parent is IHaulDestination haulDestination && !haulDestination.Accepts(thing))
            {
                return false;
            }

            if (Props.respectStoreSettings && parent is IStoreSettingsParent storeSettingsParent && !storeSettingsParent.GetStoreSettings().AllowedToAccept(thing))
            {
                return false;
            }

            return Owner.CanAcceptAnyOf(thing);
        }

        public override int CountCanAccept(Thing thing)
        {
            if (!Accepts(thing))
            {
                return 0;
            }

            return Owner.GetCountCanAccept(thing);
        }

        public override int PushFrom(Thing thing, int count)
        {
            if (!Accepts(thing) || count <= 0 || thing.holdingOwner == null)
            {
                return 0;
            }

            return thing.holdingOwner.TryTransferToContainer(thing, Owner, count);
        }

        public override string GetStatus()
        {
            ThingOwner owner = Owner;
            return "MN_MatterIOPortStatusThingOwner".Translate(parent.LabelCap, owner.Count);
        }
    }
}
