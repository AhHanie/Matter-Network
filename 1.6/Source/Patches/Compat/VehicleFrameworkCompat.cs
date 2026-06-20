using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SK_Matter_Network.Patches
{
    public static class VehicleFrameworkCompat
    {
        private const string PackageId = "SmashPhil.VehicleFramework";
        private const int SharedVehicleItemReservationMaxPawns = 10;

        private static readonly Type VehiclePawnType =
            AccessTools.TypeByName("Vehicles.VehiclePawn");
        private static readonly Type CompFueledTravelType =
            AccessTools.TypeByName("Vehicles.CompFueledTravel");
        private static readonly Type CompPropertiesFueledTravelType =
            AccessTools.TypeByName("Vehicles.CompProperties_FueledTravel");
        private static readonly Type WorkGiverRefuelVehicleType =
            AccessTools.TypeByName("Vehicles.WorkGiver_RefuelVehicle");
        private static readonly Type JobDriverRefuelVehicleType =
            AccessTools.TypeByName("Vehicles.JobDriver_RefuelVehicle");
        private static readonly Type JobDriverLoadVehicleBaseType =
            AccessTools.TypeByName("Vehicles.JobDriverLoadVehicleBase");
        private static readonly Type JobDriverGetItemForVehicleBaseType =
            AccessTools.TypeByName("Vehicles.JobDriverGetItemForVehicleBase");
        private static readonly Type JobDriverLoadVehicleType =
            AccessTools.TypeByName("Vehicles.JobDriver_LoadVehicle");
        private static readonly Type JobDefOfVehiclesType =
            AccessTools.TypeByName("Vehicles.JobDefOf_Vehicles");
        private static readonly Type JobDriverGiveItemToVehicleType =
            AccessTools.TypeByName("Vehicles.JobDriver_GiveItemToVehicle");
        private static readonly Type VehicleTurretType =
            AccessTools.TypeByName("Vehicles.VehicleTurret");
        private static readonly Type VehicleTurretDefType =
            AccessTools.TypeByName("Vehicles.VehicleTurretDef");

        private static readonly PropertyInfo CompFueledTravel_Props =
            AccessTools.Property(CompFueledTravelType, "Props");
        private static readonly FieldInfo CompProperties_FuelType =
            AccessTools.Field(CompPropertiesFueledTravelType, "fuelType");
        private static readonly PropertyInfo VehiclePawn_CompFueledTravel =
            AccessTools.Property(VehiclePawnType, "CompFueledTravel");
        private static readonly PropertyInfo CompFueledTravel_FuelCountToFull =
            AccessTools.Property(CompFueledTravelType, "FuelCountToFull");
        private static readonly FieldInfo JobDefOf_RefuelVehicle =
            AccessTools.Field(JobDefOfVehiclesType, "RefuelVehicle");
        private static readonly MethodInfo CountLeftToPackMethod =
            AccessTools.Method(JobDriverGetItemForVehicleBaseType, "CountLeftToPack",
                new[] { typeof(Pawn), typeof(JobDef), typeof(ThingDefCountClass) });
        private static readonly FieldInfo VehicleTurret_LoadedAmmoField =
            AccessTools.Field(AccessTools.TypeByName("Vehicles.VehicleTurret"), "loadedAmmo");
        private static readonly FieldInfo VehicleTurret_DefField =
            AccessTools.Field(AccessTools.TypeByName("Vehicles.VehicleTurret"), "def");
        private static readonly FieldInfo VehicleTurretDef_AmmunitionField =
            AccessTools.Field(AccessTools.TypeByName("Vehicles.VehicleTurretDef"), "ammunition");

        // Pawns currently in an active network-item caravan gather job.
        // Serializes network-item gathering to one pawn at a time so VF's CountFromJob
        // cannot cross-contaminate countBeingPacked across pawns in the same lord.
        private static readonly HashSet<Pawn> NetworkCaravanGatherers = new HashSet<Pawn>();

        public static bool IsAvailable()
        {
            return ModsConfig.IsActive(PackageId)
                && VehiclePawnType != null
                && CompFueledTravelType != null
                && JobDriverLoadVehicleBaseType != null
                && JobDriverGetItemForVehicleBaseType != null;
        }

        private static bool IsNetworkTarget(Pawn pawn, LocalTargetInfo target)
        {
            if (pawn?.Map == null || !target.HasThing)
                return false;
            return pawn.Map.GetComponent<NetworksMapComponent>().TryGetItemNetwork(target.Thing, out _);
        }

        private static int ResolveNetworkReservationCount(Pawn pawn, Job job, LocalTargetInfo target, int desiredCount = -1)
        {
            if (!target.HasThing || pawn?.Map == null)
                return 0;
            int desired = desiredCount > 0 ? desiredCount : (job.count > 0 ? job.count : -1);
            return NetworkItemSearchUtility.GetReservableNetworkStackCount(
                pawn, target.Thing, SharedVehicleItemReservationMaxPawns, desired);
        }

        // Shared reserve toil used by transpilers in place of Toils_Reserve.Reserve when the target may be a network item.
        public static Toil MakeSharedVehicleItemReserveToil(
            TargetIndex ind,
            int maxPawns = 1,
            int stackCount = -1,
            ReservationLayerDef layer = null,
            bool ignoreOtherReservations = false)
        {
            Toil toil = ToilMaker.MakeToil("MN_ReserveSharedVehicleItem");
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                Job curJob = actor?.CurJob;
                if (actor == null || curJob == null)
                    return;

                LocalTargetInfo target = curJob.GetTarget(ind);
                if (!IsNetworkTarget(actor, target))
                {
                    if (!actor.Reserve(target, curJob, maxPawns, stackCount, layer, errorOnFailed: false, ignoreOtherReservations))
                        actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                int desiredForReserve = stackCount > 0 ? stackCount : -1;
                int countToReserve = ResolveNetworkReservationCount(actor, curJob, target, desiredForReserve);
                if (countToReserve <= 0
                    || !actor.Reserve(target, curJob, SharedVehicleItemReservationMaxPawns, countToReserve,
                        layer, errorOnFailed: false, ignoreOtherReservations))
                {
                    actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.atomicWithPrevious = true;
            return toil;
        }

        private static Thing FindClosestNetworkItemForDef(Pawn pawn, ThingDef def)
        {
            return NetworkItemSearchUtility.FindClosestReachableThing(pawn, item =>
                item.def == def
                && !item.IsForbidden(pawn)
                && item.def.EverHaulable
                && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                && NetworkItemSearchUtility.GetReservableNetworkStackCount(
                    pawn, item, SharedVehicleItemReservationMaxPawns) > 0
                && NetworkItemSearchUtility.IsUsableNetworkItemForExtraction(pawn, item, out _),
                out _);
        }

        private static int GetCountLeftToPack(Pawn pawn, JobDef jobDef, ThingDefCountClass thingDefCount)
        {
            if (CountLeftToPackMethod == null)
                return thingDefCount.count;
            try
            {
                return (int)CountLeftToPackMethod.Invoke(null, new object[] { pawn, jobDef, thingDefCount });
            }
            catch
            {
                return thingDefCount.count;
            }
        }

        private static int GetVehicleFuelCountToFull(Job job, LocalTargetInfo vehicleTarget)
        {
            if (job.count > 0)
                return job.count;
            if (VehiclePawn_CompFueledTravel == null || CompFueledTravel_FuelCountToFull == null)
                return -1;
            Thing vehicle = vehicleTarget.Thing;
            if (vehicle == null) return -1;
            object compFueled = VehiclePawn_CompFueledTravel.GetValue(vehicle);
            if (compFueled == null) return -1;
            return (int)CompFueledTravel_FuelCountToFull.GetValue(compFueled);
        }

        // Vehicle Refueling

        // Fallback: when ClosestFuelAvailable fails due to shared-reservation mismatch, find network fuel directly.
        [HarmonyPatch]
        public static class WorkGiver_RefuelVehicle_JobOnThing
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!ModsConfig.IsActive(PackageId)) return false;
                if (WorkGiverRefuelVehicleType == null
                    || VehiclePawn_CompFueledTravel == null
                    || CompFueledTravel_Props == null
                    || CompProperties_FuelType == null
                    || JobDefOf_RefuelVehicle == null)
                {
                    if (ModsConfig.IsActive(PackageId))
                        Logger.Warning("VehicleFrameworkCompat: Refuel WorkGiver members not found; refuel network fallback disabled.");
                    return false;
                }
                return true;
            }

            public static MethodBase TargetMethod() =>
                AccessTools.Method(WorkGiverRefuelVehicleType, "JobOnThing");

            public static void Postfix(Thing t, Pawn pawn, ref Job __result)
            {
                if (__result != null || pawn?.Map == null) return;

                object compFueled = VehiclePawn_CompFueledTravel.GetValue(t);
                if (compFueled == null) return;

                object props = CompFueledTravel_Props.GetValue(compFueled);
                if (props == null) return;

                ThingDef fuelType = CompProperties_FuelType.GetValue(props) as ThingDef;
                if (fuelType == null) return;

                Thing networkFuel = FindClosestNetworkItemForDef(pawn, fuelType);
                if (networkFuel == null) return;

                JobDef refuelDef = JobDefOf_RefuelVehicle.GetValue(null) as JobDef;
                if (refuelDef == null) return;

                __result = JobMaker.MakeJob(refuelDef, t, networkFuel);
            }
        }

        // Replace single-pawn fuel reservation with shared reservation for network items.
        [HarmonyPatch]
        public static class JobDriver_RefuelVehicle_TryMakePreToilReservations
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!ModsConfig.IsActive(PackageId)) return false;
                if (JobDriverRefuelVehicleType == null)
                {
                    Logger.Warning("VehicleFrameworkCompat: JobDriver_RefuelVehicle not found; shared refuel reservation disabled.");
                    return false;
                }
                return true;
            }

            public static MethodBase TargetMethod() =>
                AccessTools.Method(JobDriverRefuelVehicleType, "TryMakePreToilReservations");

            public static bool Prefix(JobDriver __instance, bool errorOnFailed, ref bool __result)
            {
                LocalTargetInfo fuelTarget = __instance.job.GetTarget(TargetIndex.B);
                if (!IsNetworkTarget(__instance.pawn, fuelTarget))
                    return true;

                LocalTargetInfo vehicleTarget = __instance.job.GetTarget(TargetIndex.A);
                if (!__instance.pawn.Reserve(vehicleTarget, __instance.job, 1, -1, null, errorOnFailed))
                {
                    __result = false;
                    return false;
                }

                int fuelCountToFull = GetVehicleFuelCountToFull(__instance.job, vehicleTarget);
                int countToReserve = ResolveNetworkReservationCount(__instance.pawn, __instance.job, fuelTarget, fuelCountToFull);
                __result = countToReserve > 0
                    && __instance.pawn.Reserve(fuelTarget, __instance.job,
                        SharedVehicleItemReservationMaxPawns, countToReserve, null, errorOnFailed);
                return false;
            }
        }

        // Replace in-toil fuel reservation with shared version so multiple pawns can draw from the same network stack.
        [HarmonyPatch]
        public static class JobDriver_RefuelVehicle_MakeNewToils
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!ModsConfig.IsActive(PackageId)) return false;
                if (JobDriverRefuelVehicleType == null) return false;
                if (TargetMethod() == null)
                {
                    Logger.Warning("VehicleFrameworkCompat: JobDriver_RefuelVehicle.MakeNewToils enumerator not found; shared reserve toil disabled.");
                    return false;
                }
                return true;
            }

            public static MethodBase TargetMethod()
            {
                MethodInfo makeNewToils = AccessTools.Method(JobDriverRefuelVehicleType, "MakeNewToils");
                return makeNewToils != null ? AccessTools.EnumeratorMoveNext(makeNewToils) : null;
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo reserveMethod = AccessTools.Method(typeof(Toils_Reserve), nameof(Toils_Reserve.Reserve),
                    new[] { typeof(TargetIndex), typeof(int), typeof(int), typeof(ReservationLayerDef), typeof(bool) });
                MethodInfo sharedMethod = AccessTools.Method(typeof(VehicleFrameworkCompat),
                    nameof(MakeSharedVehicleItemReserveToil));

                int replaced = 0;
                foreach (CodeInstruction instr in instructions)
                {
                    if (instr.opcode == OpCodes.Call
                        && instr.operand is MethodInfo m && m == reserveMethod)
                    {
                        replaced++;
                        yield return new CodeInstruction(OpCodes.Call, sharedMethod);
                        continue;
                    }
                    yield return instr;
                }

                if (replaced == 0)
                    Logger.Warning("VehicleFrameworkCompat: JobDriver_RefuelVehicle.MakeNewToils transpiler made no replacements.");
            }
        }

        // Generic Vehicle Item Loading (upgrades + turret ammo)

        // Extend FindThingToPack to fall back to the network when the map search finds nothing.
        [HarmonyPatch]
        public static class JobDriverGetItemForVehicleBase_FindThingToPack
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!ModsConfig.IsActive(PackageId)) return false;
                if (JobDriverGetItemForVehicleBaseType == null || VehiclePawnType == null)
                {
                    Logger.Warning("VehicleFrameworkCompat: JobDriverGetItemForVehicleBase not found; turret/upgrade network fallback disabled.");
                    return false;
                }
                return true;
            }

            public static MethodBase TargetMethod()
            {
                // Patch the 4-argument overload that performs the actual search.
                return AccessTools.Method(
                    JobDriverGetItemForVehicleBaseType,
                    "FindThingToPack",
                    new[] { VehiclePawnType, typeof(Pawn), typeof(JobDef), typeof(IEnumerable<ThingDefCountClass>) });
            }

            public static void Postfix(
                Pawn pawn,
                JobDef jobDef,
                IEnumerable<ThingDefCountClass> thingDefCounts,
                ref Thing __result)
            {
                if (__result != null || pawn?.Map == null || thingDefCounts == null) return;

                foreach (ThingDefCountClass thingDefCount in thingDefCounts)
                {
                    if (thingDefCount?.thingDef == null) continue;
                    if (GetCountLeftToPack(pawn, jobDef, thingDefCount) <= 0) continue;

                    Thing networkItem = FindClosestNetworkItemForDef(pawn, thingDefCount.thingDef);
                    if (networkItem == null) continue;

                    __result = networkItem;
                    return;
                }
            }
        }

        // Replace single-pawn item reservation with shared reservation when the target is a network item.
        [HarmonyPatch]
        public static class JobDriverLoadVehicleBase_TryMakePreToilReservations
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!ModsConfig.IsActive(PackageId)) return false;
                if (JobDriverLoadVehicleBaseType == null)
                {
                    Logger.Warning("VehicleFrameworkCompat: JobDriverLoadVehicleBase not found; shared load reservation disabled.");
                    return false;
                }
                return true;
            }

            public static MethodBase TargetMethod() =>
                AccessTools.Method(JobDriverLoadVehicleBaseType, "TryMakePreToilReservations");

            public static bool Prefix(JobDriver __instance, bool errorOnFailed, ref bool __result)
            {
                LocalTargetInfo toHaul = __instance.job.GetTarget(TargetIndex.A);
                if (!toHaul.HasThing || !IsNetworkTarget(__instance.pawn, toHaul))
                    return true;

                int desired = __instance.job.count > 0 ? __instance.job.count : -1;
                int countToReserve = ResolveNetworkReservationCount(__instance.pawn, __instance.job, toHaul, desired);
                if (countToReserve <= 0)
                {
                    __result = !errorOnFailed;
                    return false;
                }

                if (!__instance.pawn.Reserve(toHaul, __instance.job,
                    SharedVehicleItemReservationMaxPawns, countToReserve, null, errorOnFailed))
                {
                    __result = false;
                    return false;
                }

                List<LocalTargetInfo> queue = __instance.job.GetTargetQueue(TargetIndex.A);
                if (!queue.NullOrEmpty())
                {
                    foreach (LocalTargetInfo queueTarget in queue)
                    {
                        if (!queueTarget.HasThing) continue;
                        if (!IsNetworkTarget(__instance.pawn, queueTarget))
                        {
                            __instance.pawn.Reserve(queueTarget, __instance.job, 1, -1, null, errorOnFailed: false);
                        }
                        else
                        {
                            int qCount = ResolveNetworkReservationCount(__instance.pawn, __instance.job, queueTarget);
                            if (qCount > 0)
                            {
                                __instance.pawn.Reserve(queueTarget, __instance.job,
                                    SharedVehicleItemReservationMaxPawns, qCount, null, errorOnFailed: false);
                            }
                        }
                    }
                }

                __result = true;
                return false;
            }
        }

        // Replace Toils_Reserve.Reserve(TargetIndex.A) in the carry and haul-in-inventory paths with a shared version.
        [HarmonyPatch]
        public static class JobDriverLoadVehicleBase_MakeNewToils
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!ModsConfig.IsActive(PackageId)) return false;
                if (JobDriverLoadVehicleBaseType == null) return false;

                bool any = false;
                foreach (MethodBase mb in TargetMethods()) { any = true; break; }
                if (!any)
                    Logger.Warning("VehicleFrameworkCompat: JobDriverLoadVehicleBase carry/haul methods not found; shared reserve toil disabled.");
                return any;
            }

            public static IEnumerable<MethodBase> TargetMethods()
            {
                if (JobDriverLoadVehicleBaseType == null) yield break;

                MethodInfo carry = AccessTools.Method(JobDriverLoadVehicleBaseType, "MakeNewToilsCarry");
                if (carry != null)
                {
                    MethodBase mn = AccessTools.EnumeratorMoveNext(carry);
                    if (mn != null) yield return mn;
                }

                MethodInfo haul = AccessTools.Method(JobDriverLoadVehicleBaseType, "MakeNewToilsHaulInInventory");
                if (haul != null)
                {
                    MethodBase mn = AccessTools.EnumeratorMoveNext(haul);
                    if (mn != null) yield return mn;
                }
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo reserveMethod = AccessTools.Method(typeof(Toils_Reserve), nameof(Toils_Reserve.Reserve),
                    new[] { typeof(TargetIndex), typeof(int), typeof(int), typeof(ReservationLayerDef), typeof(bool) });
                MethodInfo sharedMethod = AccessTools.Method(typeof(VehicleFrameworkCompat),
                    nameof(MakeSharedVehicleItemReserveToil));

                int replaced = 0;
                foreach (CodeInstruction instr in instructions)
                {
                    if (instr.opcode == OpCodes.Call
                        && instr.operand is MethodInfo m && m == reserveMethod)
                    {
                        replaced++;
                        yield return new CodeInstruction(OpCodes.Call, sharedMethod);
                        continue;
                    }
                    yield return instr;
                }

                if (replaced == 0)
                    Logger.Warning("VehicleFrameworkCompat: JobDriverLoadVehicleBase carry/haul transpiler made no replacements.");
            }
        }

        // Turret Ammo Network Fallback
        //
        // FindThingDefsToPack (called by WorkGiver_RefuelVehicleTurret) builds its count list from
        // FindThingsToPack, which uses RefuelWorkGiverUtility.FindEnoughReservableThings — a map-only
        // search. When ammo is only in the network, FindThingsToPack returns empty, AddThingDefsToPackForTurret
        // returns early, and FindThingDefsToPack returns an empty list. WorkGiver_CarryToVehicle.JobOnThing
        // sees an empty list and returns null, so no job is ever created.
        //
        // The fix is to append a network item to FindThingsToPack's result when the map search finds nothing.
        // AddThingDefsToPackForTurret then counts the network item's stack, FindThingDefsToPack returns a
        // non-empty list, and the subsequent FindThingToPack call finds the item via our existing GenClosest patch.
        [HarmonyPatch]
        public static class JobDriver_GiveItemToVehicle_FindThingsToPack
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!ModsConfig.IsActive(PackageId)) return false;
                if (JobDriverGiveItemToVehicleType == null || VehicleTurretType == null
                    || VehicleTurret_LoadedAmmoField == null || VehicleTurret_DefField == null
                    || VehicleTurretDef_AmmunitionField == null)
                {
                    Logger.Warning("VehicleFrameworkCompat: VehicleTurret members not found; turret ammo network fallback disabled.");
                    return false;
                }
                return true;
            }

            public static MethodBase TargetMethod() =>
                AccessTools.Method(JobDriverGiveItemToVehicleType, "FindThingsToPack",
                    new[] { VehiclePawnType, typeof(Pawn), VehicleTurretType, typeof(int) });

            public static void Postfix(Pawn pawn, object turret, ref List<Thing> __result)
            {
                if (!__result.NullOrEmpty()) return;

                ThingDef reloadDef = VehicleTurret_LoadedAmmoField.GetValue(turret) as ThingDef;
                if (reloadDef == null)
                {
                    object turretDef = VehicleTurret_DefField.GetValue(turret);
                    if (turretDef != null)
                    {
                        ThingFilter ammunition = VehicleTurretDef_AmmunitionField.GetValue(turretDef) as ThingFilter;
                        if (ammunition != null)
                        {
                            foreach (ThingDef allowedDef in ammunition.AllowedThingDefs)
                            {
                                reloadDef = allowedDef;
                                break;
                            }
                        }
                    }
                }

                if (reloadDef == null) return;

                Thing networkItem = FindClosestNetworkItemForDef(pawn, reloadDef);
                if (networkItem == null) return;

                if (__result == null) __result = new List<Thing>();
                __result.Add(networkItem);
            }
        }

        // Vehicle Cargo Loading

        // Fall back to the network when FindThingToPack finds no map-spawned item for a transferable.
        [HarmonyPatch]
        public static class JobDriver_LoadVehicle_FindThingToPack
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!ModsConfig.IsActive(PackageId)) return false;
                if (JobDriverLoadVehicleType == null)
                {
                    Logger.Warning("VehicleFrameworkCompat: JobDriver_LoadVehicle not found; network cargo fallback disabled.");
                    return false;
                }
                return true;
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    JobDriverLoadVehicleType,
                    "FindThingToPack",
                    new[] { typeof(Pawn), typeof(JobDef), typeof(List<TransferableOneWay>), typeof(Lord) });
            }

            public static void Postfix(
                Pawn pawn,
                List<TransferableOneWay> transferables,
                ref Thing __result)
            {
                if (__result != null || pawn?.Map == null || transferables.NullOrEmpty()) return;

                // Drop finished gatherers: a pawn is done when its job driver is no longer
                // a JobDriverLoadVehicleBase subtype (job ended or switched to something else).
                NetworkCaravanGatherers.RemoveWhere(p =>
                    p.Destroyed || p.jobs?.curDriver == null
                    || JobDriverLoadVehicleBaseType == null
                    || !JobDriverLoadVehicleBaseType.IsInstanceOfType(p.jobs.curDriver));

                // Serialize network-item gathering: VF's CountFromJob counts ALL same-lord
                // pawns' ToHaul.stackCount regardless of which transferable they're filling.
                // When pawn A carries item X (now off-network) and pawn B targets item Y,
                // B's countBeingPacked incorrectly includes A's carry stackCount, making
                // DetermineNumToHaul return 0 and loop.  Allowing only one pawn at a time
                // to have a network-item gather job prevents this cross-contamination.
                // The same pawn is permitted to re-enter (for multi-item carry loops).
                foreach (Pawn gatherer in NetworkCaravanGatherers)
                {
                    if (gatherer != pawn) return;
                }

                foreach (TransferableOneWay transferable in transferables)
                {
                    if (!transferable.HasAnyThing || transferable.CountToTransfer <= 0) continue;

                    Thing networkItem = FindClosestNetworkItemForDef(pawn, transferable.ThingDef);
                    if (networkItem == null) continue;

                    if (!transferable.things.Contains(networkItem))
                        transferable.things.Add(networkItem);

                    NetworkCaravanGatherers.Add(pawn);
                    __result = networkItem;
                    return;
                }
            }
        }
    }
}
