using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    // Lets Unique Weapons Unbound's customization dialog and haul planners
    // (Sequential/Sweep/Thorough) treat Matter Network stored items as
    // ordinary haulable ingredients: counted in the dialog, added to the
    // planner candidate pool, and extractable by the customize job even
    // though they are unspawned. UWU is resolved entirely through
    // AccessTools since Matter Network has no compile-time reference to it.
    public static class UniqueWeaponsUnboundCompat
    {
        private const string PackageId = "shunter.uniqueweaponsunbound";

        private static readonly Type ingredientReservationType =
            AccessTools.TypeByName("UniqueWeaponsUnbound.HaulPlanning.IngredientReservation");
        private static readonly Type haulCandidateType =
            AccessTools.TypeByName("UniqueWeaponsUnbound.HaulPlanning.HaulCandidate");
        private static readonly Type jobDriverType =
            AccessTools.TypeByName("UniqueWeaponsUnbound.JobDriver_CustomizeWeapon");

        private static readonly MethodInfo countAvailableMethod = ingredientReservationType == null ? null :
            AccessTools.Method(ingredientReservationType, "CountAvailable", new[] { typeof(Map), typeof(ThingDef), typeof(Pawn) });
        private static readonly MethodInfo buildHaulPoolMethod = ingredientReservationType == null ? null :
            AccessTools.Method(ingredientReservationType, "BuildHaulPool", new[] { typeof(Pawn), typeof(Dictionary<ThingDef, int>), typeof(float), typeof(int) });
        private static readonly MethodInfo gotoIngredientFailConditionMethod = jobDriverType == null ? null :
            AccessTools.Method(jobDriverType, "GotoIngredientFailCondition");
        private static readonly MethodInfo doCarryTrackerPickupMethod = jobDriverType == null ? null :
            AccessTools.Method(jobDriverType, "DoCarryTrackerPickup");
        private static readonly MethodInfo doInventoryPickupMethod = jobDriverType == null ? null :
            AccessTools.Method(jobDriverType, "DoInventoryPickup");
        private static readonly MethodInfo setBailMessageMethod = jobDriverType == null ? null :
            AccessTools.Method(jobDriverType, "SetBailMessage");
        private static readonly PropertyInfo weaponLabelProperty = jobDriverType == null ? null :
            AccessTools.Property(jobDriverType, "WeaponLabel");
        private static readonly FieldInfo currentTripInvLoadField = jobDriverType == null ? null :
            AccessTools.Field(jobDriverType, "currentTripInvLoad");
        private static readonly FieldInfo pickupDestinationsField = jobDriverType == null ? null :
            AccessTools.Field(jobDriverType, "pickupDestinations");
        private static readonly FieldInfo pickupLastInTripField = jobDriverType == null ? null :
            AccessTools.Field(jobDriverType, "pickupLastInTrip");
        private static readonly FieldInfo currentPickupLastInTripField = jobDriverType == null ? null :
            AccessTools.Field(jobDriverType, "currentPickupLastInTrip");

        private static readonly FieldInfo candidateThingField = haulCandidateType == null ? null :
            AccessTools.Field(haulCandidateType, "Thing");
        private static readonly FieldInfo candidatePositionField = haulCandidateType == null ? null :
            AccessTools.Field(haulCandidateType, "Position");
        private static readonly FieldInfo candidateAvailableCountField = haulCandidateType == null ? null :
            AccessTools.Field(haulCandidateType, "AvailableCount");
        private static readonly FieldInfo candidateMassPerUnitField = haulCandidateType == null ? null :
            AccessTools.Field(haulCandidateType, "MassPerUnit");

        private static bool _warnedMissingApi;

        private static bool IsAvailable()
        {
            return ingredientReservationType != null
                && haulCandidateType != null
                && jobDriverType != null
                && countAvailableMethod != null
                && buildHaulPoolMethod != null
                && gotoIngredientFailConditionMethod != null
                && doCarryTrackerPickupMethod != null
                && doInventoryPickupMethod != null
                && setBailMessageMethod != null
                && weaponLabelProperty != null
                && currentTripInvLoadField != null
                && pickupDestinationsField != null
                && pickupLastInTripField != null
                && currentPickupLastInTripField != null
                && candidateThingField != null
                && candidatePositionField != null
                && candidateAvailableCountField != null
                && candidateMassPerUnitField != null;
        }

        private static bool Prepare()
        {
            bool active = ModsConfig.IsActive(PackageId);
            bool available = active && IsAvailable();
            if (active && !available && !_warnedMissingApi)
            {
                _warnedMissingApi = true;
                Logger.Warning("[UWU Compat] Unique Weapons Unbound is loaded but expected API was not found. UWU network-hauling compat disabled.");
            }
            return available;
        }

        // ---- shared helpers ----

        private static bool IsNetworkHeld(Thing thing)
        {
            Map map = thing?.MapHeld;
            if (map == null) return false;
            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            return mapComp != null && mapComp.TryGetItemNetwork(thing, out _);
        }

        // Usable + reservable check in one place: not destroyed/empty, not
        // spawned (defensive - network items should never be), not forbidden
        // to the player or the pawn, network extraction operational and
        // reachable, and still reservable for this pawn.
        private static bool TryGetNetworkIngredient(Pawn pawn, Thing item, out DataNetwork network, out int reservable)
        {
            network = null;
            reservable = 0;
            if (item == null || item.Destroyed || item.stackCount <= 0) return false;
            if (item.Spawned) return false;
            if (item.IsForbidden(Faction.OfPlayer) || item.IsForbidden(pawn)) return false;
            if (!NetworkItemSearchUtility.IsUsableNetworkItemForExtraction(pawn, item, out network)) return false;

            reservable = NetworkItemSearchUtility.GetReservableNetworkStackCount(pawn, item, 1);
            return reservable > 0;
        }

        private static string GetWeaponLabel(object driver)
        {
            return weaponLabelProperty.GetValue(driver) as string ?? "";
        }

        private static void SetBailMessage(object driver, string text)
        {
            setBailMessageMethod.Invoke(driver, new object[] { text });
        }

        private static void BailIngredientLost(JobDriver driver)
        {
            SetBailMessage(driver, "UWU_BailIngredientLost".Translate(GetWeaponLabel(driver)));
            driver.EndJobWith(JobCondition.Incompletable);
        }

        private static IList GetOrCreateList(object driver, FieldInfo field, Type elementType)
        {
            object value = field.GetValue(driver);
            if (value == null)
            {
                value = Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
                field.SetValue(driver, value);
            }

            return (IList)value;
        }

        // ---- 1. Dialog availability counts ----

        [HarmonyPatch]
        public static class Patch_CountAvailable
        {
            [HarmonyPrepare]
            public static bool Prepare() => UniqueWeaponsUnboundCompat.Prepare();

            public static MethodBase TargetMethod() => countAvailableMethod;

            public static void Postfix(Map map, ThingDef thingDef, Pawn pawn, ref int __result)
            {
                if (map == null || thingDef == null || pawn == null) return;

                foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    if (item.def != thingDef) continue;
                    if (!TryGetNetworkIngredient(pawn, item, out _, out int reservable)) continue;
                    __result += reservable;
                }
            }
        }

        // ---- 2. Haul planner candidate pool ----

        [HarmonyPatch]
        public static class Patch_BuildHaulPool
        {
            [HarmonyPrepare]
            public static bool Prepare() => UniqueWeaponsUnboundCompat.Prepare();

            public static MethodBase TargetMethod() => buildHaulPoolMethod;

            public static void Postfix(Pawn pawn, Dictionary<ThingDef, int> demand, float multiplier, int capPerDef, ref object __result)
            {
                if (pawn?.Map == null || demand == null || demand.Count == 0) return;
                if (!(__result is IDictionary pool)) return;

                Map map = pawn.Map;
                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                if (mapComp == null || mapComp.Networks.Count == 0) return;

                foreach (KeyValuePair<ThingDef, int> entry in demand)
                {
                    ThingDef def = entry.Key;
                    int needed = entry.Value;
                    if (needed <= 0) continue;

                    IList list = GetOrCreateCandidateList(pool, def);
                    if (list == null) continue;

                    int cumulative = 0;
                    foreach (object existing in list)
                    {
                        cumulative += (int)candidateAvailableCountField.GetValue(existing);
                    }

                    int targetCount = Mathf.CeilToInt(needed * multiplier);
                    if (cumulative >= targetCount) continue;
                    if (list.Count >= capPerDef) continue;

                    List<Thing> candidates = new List<Thing>();
                    foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(map))
                    {
                        if (item.def != def) continue;
                        if (!TryGetNetworkIngredient(pawn, item, out _, out _)) continue;
                        candidates.Add(item);
                    }
                    if (candidates.Count == 0) continue;

                    candidates.Sort((a, b) =>
                        NetworkItemSearchUtility.GetClosestReachableInterfaceDistanceSquared(pawn, a)
                            .CompareTo(NetworkItemSearchUtility.GetClosestReachableInterfaceDistanceSquared(pawn, b)));

                    foreach (Thing item in candidates)
                    {
                        if (cumulative >= targetCount) break;
                        if (list.Count >= capPerDef) break;

                        if (!mapComp.TryGetItemNetwork(item, out DataNetwork network)) continue;

                        NetworkBuildingNetworkInterface closestInterface =
                            Patch_Toils_Goto.GotoThing.FindClosestReachableInterface(pawn, network);
                        if (closestInterface == null) continue;

                        int reservable = NetworkItemSearchUtility.GetReservableNetworkStackCount(pawn, item, 1);
                        if (reservable <= 0) continue;

                        object candidate = Activator.CreateInstance(haulCandidateType);
                        candidateThingField.SetValue(candidate, item);
                        candidatePositionField.SetValue(candidate, closestInterface.InteractionCell);
                        candidateAvailableCountField.SetValue(candidate, reservable);
                        candidateMassPerUnitField.SetValue(candidate, item.GetStatValue(StatDefOf.Mass));

                        list.Add(candidate);
                        cumulative += reservable;
                    }
                }
            }

            private static IList GetOrCreateCandidateList(IDictionary pool, ThingDef def)
            {
                if (pool.Contains(def))
                {
                    return pool[def] as IList;
                }

                object newList = Activator.CreateInstance(typeof(List<>).MakeGenericType(haulCandidateType));
                pool[def] = newList;
                return newList as IList;
            }
        }

        // ---- 3. Accept network-held ingredients in the goto fail condition ----

        [HarmonyPatch]
        public static class Patch_GotoIngredientFailCondition
        {
            [HarmonyPrepare]
            public static bool Prepare() => UniqueWeaponsUnboundCompat.Prepare();

            public static MethodBase TargetMethod() => gotoIngredientFailConditionMethod;

            public static bool Prefix(JobDriver __instance, ref bool __result)
            {
                Thing ing = __instance.job.GetTarget(TargetIndex.A).Thing;
                if (ing == null || !IsNetworkHeld(ing)) return true;

                if (TryGetNetworkIngredient(__instance.pawn, ing, out _, out _))
                {
                    __result = false;
                    return false;
                }

                SetBailMessage(__instance, "UWU_BailIngredientLost".Translate(GetWeaponLabel(__instance)));
                __result = true;
                return false;
            }
        }

        // ---- 4. Carry-tracker pickup from a network (Sweep/Thorough hybrid path) ----

        [HarmonyPatch]
        public static class Patch_DoCarryTrackerPickup
        {
            [HarmonyPrepare]
            public static bool Prepare() => UniqueWeaponsUnboundCompat.Prepare();

            public static MethodBase TargetMethod() => doCarryTrackerPickupMethod;

            public static bool Prefix(JobDriver __instance)
            {
                Thing thing = __instance.job.GetTarget(TargetIndex.A).Thing;
                if (thing == null || !IsNetworkHeld(thing)) return true;

                Pawn pawn = __instance.pawn;
                if (!TryGetNetworkIngredient(pawn, thing, out DataNetwork network, out _))
                {
                    BailIngredientLost(__instance);
                    return false;
                }

                Job job = __instance.job;
                int requested = job.count;
                int volAvail = pawn.carryTracker.AvailableStackSpace(thing.def);
                if (volAvail <= 0)
                {
                    BailIngredientLost(__instance);
                    return false;
                }

                int take = Mathf.Min(Mathf.Min(requested, thing.stackCount), volAvail);
                if (take <= 0)
                {
                    BailIngredientLost(__instance);
                    return false;
                }

                int actualTaken = pawn.carryTracker.TryStartCarry(thing, take, reserve: true);
                network.MarkBytesDirty();
                if (actualTaken <= 0)
                {
                    BailIngredientLost(__instance);
                    return false;
                }

                int residual = requested - actualTaken;
                if (residual > 0)
                {
                    if (job.targetQueueA == null) job.targetQueueA = new List<LocalTargetInfo>();
                    if (job.countQueue == null) job.countQueue = new List<int>();
                    IList destinations = GetOrCreateList(__instance, pickupDestinationsField, typeof(int));
                    IList lastInTrip = GetOrCreateList(__instance, pickupLastInTripField, typeof(bool));

                    job.targetQueueA.Insert(0, thing);
                    job.countQueue.Insert(0, residual);
                    destinations.Insert(0, 0); // PickupDestination.CarryTracker == 0
                    lastInTrip.Insert(0, true);
                    currentPickupLastInTripField.SetValue(__instance, true);
                }

                return false;
            }
        }

        // ---- 5. Inventory pickup from a network (Sweep/Thorough hybrid path) ----

        [HarmonyPatch]
        public static class Patch_DoInventoryPickup
        {
            [HarmonyPrepare]
            public static bool Prepare() => UniqueWeaponsUnboundCompat.Prepare();

            public static MethodBase TargetMethod() => doInventoryPickupMethod;

            public static bool Prefix(JobDriver __instance)
            {
                Thing thing = __instance.job.GetTarget(TargetIndex.A).Thing;
                if (thing == null || !IsNetworkHeld(thing)) return true;

                Pawn pawn = __instance.pawn;
                if (!TryGetNetworkIngredient(pawn, thing, out DataNetwork network, out _))
                {
                    BailIngredientLost(__instance);
                    return false;
                }

                int requested = __instance.job.count;
                if (thing.stackCount < requested)
                {
                    BailIngredientLost(__instance);
                    return false;
                }

                Thing splitOff = thing.SplitOff(requested);
                if (splitOff == null)
                {
                    BailIngredientLost(__instance);
                    return false;
                }

                if (!pawn.inventory.innerContainer.TryAdd(splitOff, canMergeWithExistingStacks: true))
                {
                    if (splitOff != thing && !splitOff.Destroyed)
                    {
                        thing.TryAbsorbStack(splitOff, respectStackLimit: false);
                    }

                    network.MarkBytesDirty();
                    SetBailMessage(__instance, "UWU_BailIngredientPlacementFailed".Translate(GetWeaponLabel(__instance)));
                    __instance.EndJobWith(JobCondition.Incompletable);
                    return false;
                }

                network.MarkBytesDirty();

                IList tripLoad = GetOrCreateList(__instance, currentTripInvLoadField, typeof(ThingDefCountClass));
                tripLoad.Add(new ThingDefCountClass(thing.def, requested));

                return false;
            }
        }
    }
}
