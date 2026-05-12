using System.Collections.Generic;
using Verse;

namespace SK_Matter_Network
{
    public interface IMatterInventoryAdapter
    {
        Thing ParentThing { get; }
        string Label { get; }
        bool IsValid { get; }

        IEnumerable<Thing> GetStoredThings();
        bool CanPull(Thing thing, int count);
        int PullTo(Thing thing, int count, ThingOwner destination);
        bool Accepts(Thing thing);
        int CountCanAccept(Thing thing);
        int PushFrom(Thing thing, int count);
        string GetStatus();
    }

    public interface IMatterInputForbiddenPolicy
    {
        bool UseDirectForbiddenCheckForInput { get; }
    }
}
