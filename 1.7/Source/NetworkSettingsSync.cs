using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    // Keeps each DataNetwork's StorageSettings (the single authoritative policy) and any vanilla
    // StorageGroup linked to its terminals in agreement.
    //
    // Terminals and chutes report the network's settings object while connected, so the only way a
    // second source of truth can exist is a StorageGroup. A group and the networks reachable through
    // its terminals form one "component"; an edit on any member copies the edited settings to every
    // other member of the component.
    public static class NetworkSettingsSync
    {
        // While > 0, settings callbacks are ignored: the caller applies the change itself.
        private static int suppressDepth;
        // While > 0, a StorageSettings.CopyFrom is running; its several priority/filter callbacks
        // collapse into a single propagation when the outermost copy completes.
        private static int copyDepth;
        private static StorageSettings pendingSource;

        public static bool IsSuppressed => suppressDepth > 0;

        public static bool IsNetworkOrGroupSettings(StorageSettings settings)
        {
            return settings != null && (settings.owner is StorageGroup || DataNetwork.FromSettings(settings) != null);
        }

        public static void CopyQuietly(StorageSettings destination, StorageSettings source)
        {
            if (ReferenceEquals(destination, source)) return;

            suppressDepth++;
            try
            {
                destination.CopyFrom(source);
            }
            finally
            {
                suppressDepth--;
            }
        }

        public static void BeginCopy()
        {
            copyDepth++;
        }

        public static void EndCopy()
        {
            copyDepth--;
            if (copyDepth == 0 && pendingSource != null)
            {
                StorageSettings source = pendingSource;
                pendingSource = null;
                PropagateFrom(source);
            }
        }

        // The filter or priority of a network- or group-owned StorageSettings was edited.
        public static void NotifyEdited(StorageSettings edited)
        {
            // Not while loading: group/network members and references may still be unresolved.
            // ReconcileAfterLoad handles the loaded state once it is complete.
            if (suppressDepth > 0 || edited == null || Current.ProgramState != ProgramState.Playing) return;

            if (copyDepth > 0)
            {
                pendingSource = edited;
                return;
            }

            PropagateFrom(edited);
        }

        // A terminal joined or left a storage group. Joining adopts the group's settings (vanilla
        // link behavior); leaving changes no values, so there is nothing to push.
        public static void NotifyEndpointGroupChanged(IStorageGroupMember endpoint, StorageGroup group)
        {
            if (group != null && group.members.Contains(endpoint))
                NotifyEdited(group.GetStoreSettings());
        }

        public static void PropagateFromNetwork(DataNetwork network)
        {
            PropagateFrom(network.StorageSettings);
        }

        private static void PropagateFrom(StorageSettings source)
        {
            DataNetwork network = DataNetwork.FromSettings(source);
            StorageGroup group = source.owner as StorageGroup;
            if (network == null && group == null) return;

            List<DataNetwork> networks = new List<DataNetwork>();
            List<StorageGroup> groups = new List<StorageGroup>();
            CollectComponent(network, group, networks, groups);

            // A group with no network terminals is vanilla's business alone.
            if (networks.Count == 0) return;

            ApplyToComponent(source, networks, groups);
        }

        private static void ApplyToComponent(StorageSettings source, List<DataNetwork> networks, List<StorageGroup> groups)
        {
            suppressDepth++;
            try
            {
                for (int i = 0; i < networks.Count; i++)
                {
                    StorageSettings target = networks[i].StorageSettings;
                    if (!ReferenceEquals(target, source)) target.CopyFrom(source);
                }

                for (int i = 0; i < groups.Count; i++)
                {
                    StorageSettings target = groups[i].GetStoreSettings();
                    if (!ReferenceEquals(target, source)) target.CopyFrom(source);
                    // Vanilla only notifies slot-group members here; our terminals are not slot groups.
                    groups[i].Notify_SettingsChanged();
                }

                for (int i = 0; i < networks.Count; i++)
                    networks[i].ApplyPolicyChanged();
            }
            finally
            {
                suppressDepth--;
            }
        }

        // Called once per map after load, when every storage group and member reference is resolved.
        // A saved network policy is authoritative; where one storage group links several networks that
        // disagree, the seeded network with the lowest NetworkId wins.
        public static void ReconcileAfterLoad(IEnumerable<DataNetwork> allNetworks)
        {
            HashSet<DataNetwork> visited = new HashSet<DataNetwork>();
            foreach (DataNetwork start in allNetworks.OrderBy(n => n.NetworkId, StringComparer.Ordinal).ToList())
            {
                if (visited.Contains(start)) continue;

                List<DataNetwork> networks = new List<DataNetwork>();
                List<StorageGroup> groups = new List<StorageGroup>();
                CollectComponent(start, null, networks, groups);
                visited.UnionWith(networks);

                if (groups.Count == 0 && networks.Count == 1) continue;

                DataNetwork winner = networks
                    .OrderByDescending(n => n.HasSeededStorageSettings)
                    .ThenBy(n => n.NetworkId, StringComparer.Ordinal)
                    .First();

                if (Prefs.DevMode && networks.Count > 1)
                {
                    Log.Warning($"[Matter Network] Storage group links {networks.Count} networks; reconciled their storage settings to network {winner.NetworkId}.");
                }

                ApplyToComponent(winner.StorageSettings, networks, groups);
            }
        }

        private static void CollectComponent(DataNetwork startNetwork, StorageGroup startGroup, List<DataNetwork> networks, List<StorageGroup> groups)
        {
            HashSet<DataNetwork> seenNetworks = new HashSet<DataNetwork>();
            HashSet<StorageGroup> seenGroups = new HashSet<StorageGroup>();
            Queue<DataNetwork> networkQueue = new Queue<DataNetwork>();
            Queue<StorageGroup> groupQueue = new Queue<StorageGroup>();

            if (startNetwork != null && seenNetworks.Add(startNetwork))
            {
                networks.Add(startNetwork);
                networkQueue.Enqueue(startNetwork);
            }

            if (startGroup != null && seenGroups.Add(startGroup))
            {
                groups.Add(startGroup);
                groupQueue.Enqueue(startGroup);
            }

            while (networkQueue.Count > 0 || groupQueue.Count > 0)
            {
                if (networkQueue.Count > 0)
                {
                    DataNetwork network = networkQueue.Dequeue();
                    foreach (IStorageGroupMember endpoint in EndpointsOf(network))
                    {
                        StorageGroup group = endpoint.Group;
                        if (group != null && group.members.Contains(endpoint) && seenGroups.Add(group))
                        {
                            groups.Add(group);
                            groupQueue.Enqueue(group);
                        }
                    }
                }
                else
                {
                    StorageGroup group = groupQueue.Dequeue();
                    foreach (IStorageGroupMember member in group.members)
                    {
                        DataNetwork memberNetwork = NetworkOf(member);
                        if (memberNetwork != null && seenNetworks.Add(memberNetwork))
                        {
                            networks.Add(memberNetwork);
                            networkQueue.Enqueue(memberNetwork);
                        }
                    }
                }
            }
        }

        private static IEnumerable<IStorageGroupMember> EndpointsOf(DataNetwork network)
        {
            foreach (NetworkBuildingNetworkInterface iface in network.NetworkInterfaces)
            {
                if (iface != null && !iface.Destroyed) yield return iface;
            }

            foreach (NetworkBuildingNetworkChute chute in network.NetworkChutes)
            {
                if (chute != null && !chute.Destroyed) yield return chute;
            }
        }

        private static DataNetwork NetworkOf(IStorageGroupMember member)
        {
            if (member is NetworkBuildingNetworkInterface iface) return iface.ParentNetwork;
            if (member is NetworkBuildingNetworkChute chute) return chute.ParentNetwork;
            return null;
        }
    }
}
