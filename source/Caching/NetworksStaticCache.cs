using System.Collections.Generic;
using Verse;

namespace SK_Matter_Network
{
    public static class NetworksStaticCache
    {
        private static Dictionary<Thing, bool> isInValidBestStorageByThing = new Dictionary<Thing, bool>();

        public static Dictionary<Thing, bool> IsInValidBestStorageByThing => isInValidBestStorageByThing;

        public static void Reset()
        {
            isInValidBestStorageByThing.Clear();
        }

        public static bool TryGetIsInValidBestStorage(Thing thing, out bool isInValidBestStorage)
        {
            return isInValidBestStorageByThing.TryGetValue(thing, out isInValidBestStorage);
        }

        public static void SetIsInValidBestStorage(Thing thing, bool isInValidBestStorage)
        {
            isInValidBestStorageByThing[thing] = isInValidBestStorage;
        }

        public static void RemoveThing(Thing thing)
        {
            isInValidBestStorageByThing.Remove(thing);
        }

        public static void RemoveThingsForMap(Map map)
        {
            List<Thing> thingsToRemove = new List<Thing>();
            foreach (Thing thing in isInValidBestStorageByThing.Keys)
            {
                if (thing.MapHeld == map)
                {
                    thingsToRemove.Add(thing);
                }
            }

            foreach (Thing thing in thingsToRemove)
            {
                isInValidBestStorageByThing.Remove(thing);
            }
        }
    }
}
