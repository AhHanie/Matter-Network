using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public class CompProperties_MatterSlotGroupAdapter : CompProperties
    {
        public bool useDirectForbiddenCheckForInput;

        public CompProperties_MatterSlotGroupAdapter()
        {
            compClass = typeof(CompMatterSlotGroupAdapter);
        }
    }

    public class CompMatterSlotGroupAdapter : CompMatterInventoryAdapter, IMatterInputForbiddenPolicy
    {
        private Building_Storage Storage => parent as Building_Storage;
        private CompProperties_MatterSlotGroupAdapter Props => (CompProperties_MatterSlotGroupAdapter)props;

        public override bool IsValid => parent.Spawned && !parent.Destroyed && Storage?.GetSlotGroup() != null;
        public bool UseDirectForbiddenCheckForInput => Props.useDirectForbiddenCheckForInput;

        public override IEnumerable<Thing> GetStoredThings()
        {
            if (!IsValid)
            {
                yield break;
            }

            foreach (Thing thing in Adapter.GetStoredThings())
            {
                yield return thing;
            }
        }

        public override bool CanPull(Thing thing, int count)
        {
            return IsValid && Adapter.CanPull(thing, count);
        }

        public override int PullTo(Thing thing, int count, ThingOwner destination)
        {
            return IsValid ? Adapter.PullTo(thing, count, destination) : 0;
        }

        public override bool Accepts(Thing thing)
        {
            return IsValid && Adapter.Accepts(thing);
        }

        public override int CountCanAccept(Thing thing)
        {
            return IsValid ? Adapter.CountCanAccept(thing) : 0;
        }

        public override int PushFrom(Thing thing, int count)
        {
            return IsValid ? Adapter.PushFrom(thing, count) : 0;
        }

        public override string GetStatus()
        {
            return IsValid ? Adapter.GetStatus() : "MN_MatterIOPortNoCompatibleTarget".Translate().ToString();
        }

        private MatterSlotGroupInventoryAdapter Adapter => new MatterSlotGroupInventoryAdapter(Storage.GetSlotGroup(), parent.Position, parent.Map);
    }
}
