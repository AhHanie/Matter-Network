using Verse;

namespace SK_Matter_Network
{
    public abstract class CompMatterInventoryAdapter : ThingComp, IMatterInventoryAdapter
    {
        public Thing ParentThing => parent;
        public virtual string Label => parent.LabelCap;
        public abstract bool IsValid { get; }

        public abstract System.Collections.Generic.IEnumerable<Thing> GetStoredThings();
        public abstract bool CanPull(Thing thing, int count);
        public abstract int PullTo(Thing thing, int count, ThingOwner destination);
        public abstract bool Accepts(Thing thing);
        public abstract int CountCanAccept(Thing thing);
        public abstract int PushFrom(Thing thing, int count);
        public abstract string GetStatus();
    }
}
