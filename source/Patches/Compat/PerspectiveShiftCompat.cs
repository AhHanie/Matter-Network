using HarmonyLib;
using RimWorld;
using System;
using System.Reflection;
using UnityEngine;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class PerspectiveShiftCompat
    {
        private const string PackageId = "ferny.PerspectiveShift";
        private const string AvatarTypeName = "PerspectiveShift.Avatar";
        private const string StateTypeName = "PerspectiveShift.State";
        private const string ModTypeName = "PerspectiveShift.PerspectiveShiftMod";

        private static readonly Type avatarType = AccessTools.TypeByName(AvatarTypeName);
        private static readonly Type stateType = AccessTools.TypeByName(StateTypeName);
        private static readonly Type modType = AccessTools.TypeByName(ModTypeName);
        private static readonly MethodInfo handleSelectorClickMethod = AccessTools.Method(avatarType, "HandleSelectorClick");
        private static readonly FieldInfo avatarPawnField = AccessTools.Field(avatarType, "pawn");
        private static readonly PropertyInfo stateIsActiveProperty = AccessTools.Property(stateType, "IsActive");
        private static readonly FieldInfo modSettingsField = AccessTools.Field(modType, "settings");
        private static FieldInfo grabRangeField;
        private static bool warnedMissingApi;

        private static bool IsAvailable()
        {
            return ModsConfig.IsActive(PackageId)
                && avatarType != null
                && stateType != null
                && handleSelectorClickMethod != null
                && avatarPawnField != null
                && stateIsActiveProperty != null;
        }

        [HarmonyPatch]
        public static class AvatarHandleSelectorClick
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                bool available = IsAvailable();
                if (ModsConfig.IsActive(PackageId) && !available && !warnedMissingApi)
                {
                    warnedMissingApi = true;
                    Logger.Warning("[Perspective Shift] Compatibility disabled because expected Perspective Shift members were not found.");
                }

                return available;
            }

            public static MethodBase TargetMethod()
            {
                return handleSelectorClickMethod;
            }

            public static bool Prefix(object __instance, ref bool __result)
            {
                if (!IsActive())
                {
                    return true;
                }

                Event current = Event.current;
                if (current == null || current.type != EventType.MouseDown || current.button != 0)
                {
                    return true;
                }

                Pawn pawn = avatarPawnField.GetValue(__instance) as Pawn;
                if (pawn == null || pawn.Map == null || !pawn.Spawned || pawn.Downed || pawn.InMentalState || pawn.Drafted)
                {
                    return true;
                }

                if (Find.TickManager.Paused || Find.Targeter.IsTargeting || MouseOverBlockingUI())
                {
                    return true;
                }

                if (!TryGetClickedNetworkInterface(pawn, out NetworkBuildingNetworkInterface networkInterface))
                {
                    return true;
                }

                if (!IsWithinInteractionRange(pawn, networkInterface))
                {
                    return true;
                }

                DataNetwork network = networkInterface.ParentNetwork;
                if (network == null || !network.IsOperational)
                {
                    Messages.Message("MN_PSNetworkStorageOffline".Translate(), networkInterface, MessageTypeDefOf.RejectInput, false);
                    current.Use();
                    __result = true;
                    return false;
                }

                Find.WindowStack.Add(new Dialog_PerspectiveShiftNetworkStorage(networkInterface, pawn));
                current.Use();
                __result = true;
                return false;
            }
        }

        private static bool IsActive()
        {
            object value = stateIsActiveProperty.GetValue(null, null);
            return value is bool && (bool)value;
        }

        private static bool MouseOverBlockingUI()
        {
            if (Find.WindowStack.GetWindowAt(UI.MousePositionOnUIInverted) != null)
            {
                return true;
            }

            return Find.MainTabsRoot?.OpenTab != null;
        }

        private static bool TryGetClickedNetworkInterface(Pawn pawn, out NetworkBuildingNetworkInterface networkInterface)
        {
            networkInterface = null;
            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(pawn.Map))
            {
                return false;
            }

            foreach (Thing thing in cell.GetThingList(pawn.Map))
            {
                networkInterface = thing as NetworkBuildingNetworkInterface;
                if (networkInterface != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsWithinInteractionRange(Pawn pawn, NetworkBuildingNetworkInterface networkInterface)
        {
            if (networkInterface.def.hasInteractionCell && networkInterface.InteractionCell == pawn.Position)
            {
                return true;
            }

            float grabRange = GetGrabRange();
            foreach (IntVec3 cell in networkInterface.OccupiedRect())
            {
                if (pawn.Position.DistanceTo(cell) <= grabRange)
                {
                    return true;
                }
            }

            return pawn.Position.AdjacentTo8WayOrInside(networkInterface.Position);
        }

        private static float GetGrabRange()
        {
            try
            {
                object settings = modSettingsField?.GetValue(null);
                if (settings == null)
                {
                    return 1.5f;
                }

                if (grabRangeField == null)
                {
                    grabRangeField = AccessTools.Field(settings.GetType(), "grabRange");
                }

                object value = grabRangeField?.GetValue(settings);
                if (value is float grabRange)
                {
                    return Mathf.Max(0.5f, grabRange);
                }
            }
            catch
            {
            }

            return 1.5f;
        }
    }
}
