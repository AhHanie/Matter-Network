using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_BookUtility
    {
        [HarmonyPatch(typeof(BookUtility), nameof(BookUtility.TryGetRandomBookToRead))]
        public static class TryGetRandomBookToRead
        {
            public static void Postfix(Pawn pawn, ref Book book, ref bool __result)
            {
                if (__result)
                {
                    return;
                }

                Thing networkBook = NetworkItemSearchUtility.FindClosestReachableThing(pawn, item => IsValidBookForReader(item, pawn), out _);
                if (networkBook is Book foundBook)
                {
                    book = foundBook;
                    __result = true;
                }
            }

            private static bool IsValidBookForReader(Thing item, Pawn pawn)
            {
                if (!(item is Book) || item.IsForbiddenHeld(pawn) || pawn.reading?.CurrentPolicy == null)
                {
                    return false;
                }

                if (!pawn.reading.CurrentPolicy.defFilter.Allows(item) || !pawn.reading.CurrentPolicy.effectFilter.Allows(item))
                {
                    return false;
                }

                if (!pawn.CanReserve(item) || item.IsForbidden(pawn) || !item.IsPoliticallyProper(pawn) || item.VacuumConcernTo(pawn))
                {
                    return false;
                }

                return BookUtility.CanReadBook((Book)item, pawn, out _);
            }
        }
    }
}
