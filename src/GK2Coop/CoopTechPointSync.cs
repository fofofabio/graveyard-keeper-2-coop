using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// The red, green and blue points on the ground (<c>TechPointDropData</c>), shared.
    ///
    /// The points a station, a craft or a zombie's work drops were each machine's own: the host's
    /// zombies dropped them on the host only, and a joiner's world copy carried the host's points
    /// of the moment, which both could then collect into the one shared pool (Workshop report,
    /// 5 October 2026: points lying about, doubled). The game gives a point no identity, so this
    /// gives each one an id when it is dropped. A dropped point appears for everyone; a collected
    /// point goes for everyone. A joiner matches the points of its world copy to the host's list
    /// when it joins, so each point is there once.
    /// </summary>
    internal static class CoopTechPointSync
    {
        internal const string SpawnMessage = "GK2Coop.TechPoint.Spawn.v1";
        internal const string TakenMessage = "GK2Coop.TechPoint.Taken.v1";

        private static ManualLogSource log;
        private static bool applyingRemote;
        private static readonly Dictionary<string, TechPointDropData> byId = new Dictionary<string, TechPointDropData>(StringComparer.Ordinal);
        private static ConditionalWeakTable<TechPointDropData, string> ids = new ConditionalWeakTable<TechPointDropData, string>();
        private static int spawnsSent;
        private static int spawnsApplied;
        private static int takesSent;
        private static int takesApplied;
        private static int matchedAtJoin;

        private static readonly MethodInfo Unregister = AccessTools.Method(typeof(TechPointDrop), "UnregisterActive");
        private static readonly MethodInfo StopMove = AccessTools.Method(typeof(TechPointDrop), "StopTimedMove");
        private static readonly FieldInfo DropPool = AccessTools.Field(typeof(TechPointDrop), "pool");
        private static readonly FieldInfo DropData = AccessTools.Field(typeof(TechPointDrop), "data");
        private static readonly FieldInfo DropReady = AccessTools.Field(typeof(TechPointDrop), "isInitialized");
        private static readonly FieldInfo ActiveDrops = AccessTools.Field(typeof(TechPointDrop), "activeDrops");

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static string Describe()
        {
            return "points: spawns sent/applied=" + spawnsSent + "/" + spawnsApplied + ", taken sent/applied=" + takesSent + "/" + takesApplied +
                   ", matched at join=" + matchedAtJoin + ", known=" + byId.Count;
        }

        internal static void Install(Harmony harmony)
        {
            if (!CoopDropSync.Enabled)
            {
                return;
            }
            try
            {
                harmony.Patch(AccessTools.Method(typeof(GameSceneData), nameof(GameSceneData.AddTechPointDrop)),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(CoopTechPointSync), nameof(AddedPostfix))));
                harmony.Patch(AccessTools.Method(typeof(GameSceneData), nameof(GameSceneData.RemoveTechPointDrop)),
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(CoopTechPointSync), nameof(RemovingPrefix))));
                if (Unregister == null || StopMove == null || DropPool == null || DropData == null || DropReady == null || ActiveDrops == null)
                {
                    log.LogWarning("Point sync: a TechPointDrop member was not found; a point taken elsewhere stays visible here until collected.");
                }
                log.LogInfo("Patched the points on the ground for sharing.");
            }
            catch (Exception ex)
            {
                log.LogWarning("Point sync not installed: " + Plugin.Unwrap(ex).Message);
            }
        }

        internal static void ResetSession()
        {
            byId.Clear();
            ids = new ConditionalWeakTable<TechPointDropData, string>();
        }

        private static bool Live(out NetworkManager netcode)
        {
            netcode = NetworkManager.Singleton;
            return CoopDropSync.Enabled && netcode != null && netcode.IsListening && netcode.IsConnectedClient;
        }

        private static string IdOf(TechPointDropData data, bool create)
        {
            if (data == null)
            {
                return null;
            }
            if (ids.TryGetValue(data, out string id))
            {
                return id;
            }
            if (!create)
            {
                return null;
            }
            id = Guid.NewGuid().ToString("N");
            Remember(id, data);
            return id;
        }

        private static void Remember(string id, TechPointDropData data)
        {
            ids.Remove(data);
            ids.Add(data, id);
            byId[id] = data;
        }

        // ------------------------------------------------------------------ dropped

        private static void AddedPostfix(TechPointDropData drop)
        {
            if (applyingRemote || drop == null || !Live(out NetworkManager netcode))
            {
                return;
            }
            try
            {
                string id = IdOf(drop, true);
                using (var writer = new FastBufferWriter(256, Allocator.Temp))
                {
                    WriteSpawn(writer, id, drop, false);
                    Broadcast(netcode, SpawnMessage, writer, ulong.MaxValue);
                }
                spawnsSent++;
            }
            catch (Exception ex)
            {
                log.LogWarning("Point sync: could not share a dropped point: " + ex.Message);
            }
        }

        private static void WriteSpawn(FastBufferWriter writer, string id, TechPointDropData drop, bool atJoin)
        {
            writer.WriteValueSafe(atJoin);
            writer.WriteValueSafe(new FixedString64Bytes(id));
            writer.WriteValueSafe(new FixedString128Bytes(drop.worldId ?? string.Empty));
            writer.WriteValueSafe((int)drop.type);
            writer.WriteValueSafe(drop.pos);
        }

        internal static void ReceiveSpawn(ulong senderClientId, FastBufferReader reader)
        {
            if (!Live(out NetworkManager netcode))
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out bool atJoin);
                reader.ReadValueSafe(out FixedString64Bytes rawId);
                reader.ReadValueSafe(out FixedString128Bytes rawWorld);
                reader.ReadValueSafe(out int type);
                reader.ReadValueSafe(out Vector3 pos);
                string id = rawId.ToString();
                string worldId = rawWorld.ToString();
                if (netcode.IsHost)
                {
                    using (var writer = new FastBufferWriter(256, Allocator.Temp))
                    {
                        writer.WriteValueSafe(false);
                        writer.WriteValueSafe(rawId);
                        writer.WriteValueSafe(rawWorld);
                        writer.WriteValueSafe(type);
                        writer.WriteValueSafe(pos);
                        Broadcast(netcode, SpawnMessage, writer, senderClientId);
                    }
                }
                if (byId.ContainsKey(id))
                {
                    return;
                }
                GameSceneData scene = MainGame.Instance?.GameSave?.WorldData?.GetGameSceneDataById(worldId);
                if (scene == null)
                {
                    return;
                }
                // At join the host lists its points: the joiner's world copy has the same ones,
                // without ids. Take the copy's point at that spot instead of adding a second one.
                TechPointDropData copy = atJoin && senderClientId == NetworkManager.ServerClientId ? Unnamed(scene, (TechPointsSpawner.Type)type, pos) : null;
                if (copy != null)
                {
                    Remember(id, copy);
                    matchedAtJoin++;
                    return;
                }
                var data = new TechPointDropData(pos, (TechPointsSpawner.Type)type, worldId);
                applyingRemote = true;
                try
                {
                    scene.AddTechPointDrop(data);
                }
                finally
                {
                    applyingRemote = false;
                }
                Remember(id, data);
                // Shown now if this player is in that scene; otherwise the scene shows it when entered.
                var shown = MainGame.PlayerController == null ? null : CoopDiagnostics.GetMember(MainGame.PlayerController, "CurrentGameScene") as Component;
                if (shown != null && string.Equals(Convert.ToString(CoopDiagnostics.GetMember(shown, "Id")), worldId, StringComparison.Ordinal))
                {
                    TechPointDrop.Spawn(data, shown.transform);
                }
                spawnsApplied++;
            }
            catch (Exception ex)
            {
                log.LogWarning("Point sync: could not show a point dropped by " + senderClientId + ": " + Plugin.Unwrap(ex).Message);
            }
        }

        private static TechPointDropData Unnamed(GameSceneData scene, TechPointsSpawner.Type type, Vector3 pos)
        {
            TechPointDropData best = null;
            float bestGap = 0.05f;
            foreach (TechPointDropData data in scene.techPointDrops)
            {
                if (data == null || data.type != type || IdOf(data, false) != null)
                {
                    continue;
                }
                float gap = (data.pos - pos).sqrMagnitude;
                if (gap < bestGap)
                {
                    best = data;
                    bestGap = gap;
                }
            }
            return best;
        }

        /// <summary>The join snapshot: every point on the ground of the host's world, with its id.</summary>
        internal static int SendSnapshotTo(ulong clientId)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!CoopDropSync.Enabled || netcode == null || !netcode.IsHost || MainGame.Instance?.GameSave?.WorldData == null)
            {
                return 0;
            }
            int count = 0;
            foreach (GameSceneData scene in MainGame.Instance.GameSave.WorldData.gameSceneDataList)
            {
                if (scene?.techPointDrops == null)
                {
                    continue;
                }
                foreach (TechPointDropData data in scene.techPointDrops)
                {
                    if (data == null)
                    {
                        continue;
                    }
                    using (var writer = new FastBufferWriter(256, Allocator.Temp))
                    {
                        WriteSpawn(writer, IdOf(data, true), data, true);
                        netcode.CustomMessagingManager.SendNamedMessage(SpawnMessage, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
                    }
                    count++;
                }
            }
            return count;
        }

        // ------------------------------------------------------------------ collected

        private static void RemovingPrefix(TechPointDropData drop)
        {
            if (applyingRemote || drop == null)
            {
                return;
            }
            string id = IdOf(drop, false);
            if (id == null)
            {
                return;
            }
            byId.Remove(id);
            if (!Live(out NetworkManager netcode))
            {
                return;
            }
            try
            {
                using (var writer = new FastBufferWriter(128, Allocator.Temp))
                {
                    writer.WriteValueSafe(new FixedString64Bytes(id));
                    Broadcast(netcode, TakenMessage, writer, ulong.MaxValue);
                }
                takesSent++;
            }
            catch (Exception ex)
            {
                log.LogWarning("Point sync: could not share a collected point: " + ex.Message);
            }
        }

        internal static void ReceiveTaken(ulong senderClientId, FastBufferReader reader)
        {
            if (!Live(out NetworkManager netcode))
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out FixedString64Bytes rawId);
                if (netcode.IsHost)
                {
                    using (var writer = new FastBufferWriter(128, Allocator.Temp))
                    {
                        writer.WriteValueSafe(rawId);
                        Broadcast(netcode, TakenMessage, writer, senderClientId);
                    }
                }
                string id = rawId.ToString();
                if (!byId.TryGetValue(id, out TechPointDropData data))
                {
                    return;
                }
                byId.Remove(id);
                HideView(data);
                GameSceneData scene = MainGame.Instance?.GameSave?.WorldData?.GetGameSceneDataById(data.worldId);
                if (scene != null)
                {
                    applyingRemote = true;
                    try
                    {
                        scene.RemoveTechPointDrop(data);
                    }
                    finally
                    {
                        applyingRemote = false;
                    }
                }
                takesApplied++;
            }
            catch (Exception ex)
            {
                log.LogWarning("Point sync: could not remove a point collected by " + senderClientId + ": " + Plugin.Unwrap(ex).Message);
            }
        }

        /// <summary>The point's view put back in the game's pool without being collected here (no sound, no gain).</summary>
        private static void HideView(TechPointDropData data)
        {
            if (Unregister == null || StopMove == null || DropPool == null || DropData == null || DropReady == null ||
                !(ActiveDrops?.GetValue(null) is List<TechPointDrop> active))
            {
                return;
            }
            for (int i = active.Count - 1; i >= 0; i--)
            {
                TechPointDrop view = active[i];
                if (view == null || view.Data != data)
                {
                    continue;
                }
                StopMove.Invoke(view, null);
                Unregister.Invoke(view, null);
                DropReady.SetValue(view, false);
                DropData.SetValue(view, null);
                object pool = DropPool.GetValue(null);
                if (pool != null)
                {
                    AccessTools.Method(pool.GetType(), "ReleaseObject")?.MakeGenericMethod(typeof(TechPointDrop)).Invoke(pool, new object[] { view });
                }
                return;
            }
        }

        private static void Broadcast(NetworkManager netcode, string message, FastBufferWriter writer, ulong except)
        {
            if (netcode.IsHost)
            {
                foreach (ulong clientId in netcode.ConnectedClientsIds)
                {
                    if (clientId != netcode.LocalClientId && clientId != except)
                    {
                        netcode.CustomMessagingManager.SendNamedMessage(message, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
                    }
                }
            }
            else
            {
                netcode.CustomMessagingManager.SendNamedMessage(message, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }
    }
}
