using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;

namespace GK2Coop
{
    /// <summary>
    /// Planting reaches every machine.
    ///
    /// Garden beds grow on each machine (a finished bed becomes <c>garden_*_ready</c> under the
    /// same id, so both machines agree once both have grown). Planting was local, though: a seed
    /// put in on one machine grew only there.
    ///
    /// When a player plants, the planting machine announces the bed, the crop and the seed; the
    /// host passes it on to the other joiners. Each other machine plants the same crop in the same
    /// bed with the parameters the game derives from that seed — but with no requirements, because
    /// the seed was already spent by the player who planted it. Taking it again would come out of
    /// that machine's own player or out of a shared chest.
    /// </summary>
    internal static class CoopGardenSync
    {
        internal const string PlantMessage = "GK2Coop.GardenPlant.v2";

        private const byte KindPlant = 0;
        private const byte KindFertilize = 1;

        private static ManualLogSource log;
        private static bool applying;
        private static int sent;
        private static int applied;

        internal static bool Enabled { get; set; } = true;

        internal static string Describe()
        {
            return $"garden sync: plantings/fertilisings sent={sent}, applied={applied}";
        }

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static void Install(Harmony harmony)
        {
            if (!Enabled)
            {
                return;
            }
            try
            {
                MethodInfo applySeed = AccessTools.Method(Plugin.FindGameType("GardenInteractionHandler"), "TryApplySeed");
                if (applySeed == null)
                {
                    log.LogWarning("Garden sync: GardenInteractionHandler.TryApplySeed not found; planting stays local.");
                    Enabled = false;
                    return;
                }
                harmony.Patch(applySeed, postfix: new HarmonyMethod(
                    typeof(CoopGardenSync).GetMethod(nameof(TryApplySeedPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                // The planting's tool was the planter's: a mirrored planting needs none here (found in
                // play: a joiner who had joined with an empty inventory saw the host's new field empty,
                // the game refusing the mirrored planting for want of this player's shovel).
                MethodInfo hasTool = AccessTools.PropertyGetter(Plugin.FindGameType("CraftParamsData"), "HasRequiredTool");
                if (hasTool != null)
                {
                    harmony.Patch(hasTool, prefix: new HarmonyMethod(
                        typeof(CoopGardenSync).GetMethod(nameof(HasRequiredToolPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                }
                else
                {
                    log.LogWarning("Garden sync: CraftParamsData.HasRequiredTool not found; a player without a shovel may not see others' plantings.");
                }
                MethodInfo applyFertilizer = AccessTools.Method(Plugin.FindGameType("GardenInteractionHandler"), "TryApplyFertilizer");
                if (applyFertilizer != null)
                {
                    harmony.Patch(applyFertilizer, postfix: new HarmonyMethod(
                        typeof(CoopGardenSync).GetMethod(nameof(TryApplyFertilizerPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                }
                log.LogInfo("Garden sync: planting and fertilising are shared with the other players.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Garden sync disabled: " + ex.Message);
            }
        }

        private static bool HasRequiredToolPrefix(ref bool __result)
        {
            if (!applying)
            {
                return true;
            }
            __result = true;
            return false;
        }

        private static void TryApplySeedPostfix(object seed, object cropCraft, object wgoData, bool __result)
        {
            Announce(KindPlant, seed, cropCraft, wgoData, __result);
        }

        private static void TryApplyFertilizerPostfix(object fertilizer, object cropCraft, object wgoData, bool __result)
        {
            Announce(KindFertilize, fertilizer, cropCraft, wgoData, __result);
        }

        private static void Announce(byte kind, object item, object cropCraft, object wgoData, bool succeeded)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || applying || !succeeded || netcode == null || !netcode.IsListening || !netcode.IsConnectedClient && !netcode.IsHost)
            {
                return;
            }
            try
            {
                Send(netcode, null, kind, WgoId(wgoData), Convert.ToString(CoopDiagnostics.GetMember(cropCraft, "id")), Convert.ToString(CoopDiagnostics.GetMember(item, "id")));
                sent++;
            }
            catch (Exception ex)
            {
                log.LogWarning("Garden sync: could not announce a planting: " + ex.Message);
            }
        }

        private static void Send(NetworkManager netcode, ulong? except, byte kind, string wgoId, string cropId, string seedId)
        {
            using (var writer = new FastBufferWriter(320, Allocator.Temp))
            {
                writer.WriteValueSafe(kind);
                writer.WriteValueSafe(new FixedString64Bytes(wgoId));
                writer.WriteValueSafe(new FixedString128Bytes(cropId));
                writer.WriteValueSafe(new FixedString128Bytes(seedId));
                if (netcode.IsHost)
                {
                    foreach (ulong clientId in netcode.ConnectedClientsIds)
                    {
                        if (clientId != netcode.LocalClientId && clientId != except)
                        {
                            netcode.CustomMessagingManager.SendNamedMessage(PlantMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
                        }
                    }
                }
                else
                {
                    netcode.CustomMessagingManager.SendNamedMessage(PlantMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                }
            }
        }

        internal static void Receive(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null)
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out byte kind);
                reader.ReadValueSafe(out FixedString64Bytes wgoText);
                reader.ReadValueSafe(out FixedString128Bytes cropText);
                reader.ReadValueSafe(out FixedString128Bytes seedText);
                if (netcode.IsHost)
                {
                    // Pass a joiner's planting on to the other joiners.
                    Send(netcode, sender, kind, wgoText.ToString(), cropText.ToString(), seedText.ToString());
                }
                if (kind == KindFertilize)
                {
                    Fertilize(wgoText.ToString(), cropText.ToString(), seedText.ToString());
                }
                else
                {
                    Plant(wgoText.ToString(), cropText.ToString(), seedText.ToString());
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Garden sync: could not apply a planting from " + sender + ": " + Inner(ex).Message);
            }
        }

        /// <summary>What <c>TryApplySeed</c> does, minus taking the seed.</summary>
        private static void Plant(string wgoId, string cropId, string seedId)
        {
            object wgo = FindWgo(wgoId);
            object component = wgo == null ? null : CoopDiagnostics.GetMember(wgo, "CraftComponent");
            object crop = AccessTools.Method(Plugin.FindGameType("GameBalance"), "GetCraftDefBase", new[] { typeof(string) }).Invoke(null, new object[] { cropId });
            if (component == null || crop == null)
            {
                log.LogWarning($"Garden sync: bed {Short(wgoId)} or crop {cropId} not found; planting not mirrored.");
                return;
            }
            Type itemType = Plugin.FindGameType("Item");
            object seed = Activator.CreateInstance(itemType, seedId, 1);
            object seedDef = CoopDiagnostics.GetMember(seed, "Definition");
            int talent = Convert.ToInt32(CoopDiagnostics.GetMember(seedDef, "talentValue"));

            Type handler = Plugin.FindGameType("GardenInteractionHandler");
            object needs = AccessTools.Method(handler, "FormNeedItems").Invoke(null, new[] { crop, seed });
            Type paramsType = Plugin.FindGameType("CraftParamsData");
            Type paramsKind = paramsType.GetNestedType("CraftParamsType");
            object parameters = Activator.CreateInstance(paramsType, cropId, wgo, Enum.Parse(paramsKind, "GardenPlanting"), talent);
            object worker = CoopDiagnostics.GetMember(wgo, "Worker") ?? CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerController");
            AccessTools.Method(paramsType, "RecalculateParams", new[] { needs.GetType(), Plugin.FindGameType("IWorker") }).Invoke(parameters, new[] { needs, worker });

            Type needType = Plugin.FindGameType("NeedItemData");
            object noRequirements = Activator.CreateInstance(typeof(List<>).MakeGenericType(needType));
            object element = Activator.CreateInstance(Plugin.FindGameType("CraftElement"), cropId, 1, noRequirements, parameters);

            applying = true;
            try
            {
                component.GetType().GetMethod("Clear", Type.EmptyTypes).Invoke(component, null);
                object status = component.GetType().GetMethods().First(m => m.Name == "GetStartCraftStatus" && m.GetParameters().Length == 2).Invoke(component, new[] { element, null });
                bool started = Convert.ToBoolean(component.GetType().GetMethod("TryStartCraft").Invoke(component, new[] { element }));
                AccessTools.Method(wgo.GetType(), "SetGameRes", new[] { typeof(string), typeof(int) }).Invoke(wgo, new object[] { "seed_mastery_lock", talent });
                applied++;
                log.LogInfo($"Garden sync: planted {cropId} at {Short(wgoId)} for another player (started={started}, start status {status}).");
            }
            finally
            {
                applying = false;
            }
        }

        /// <summary>
        /// What <c>TryApplyFertilizer</c> and the interaction after it do, minus taking the
        /// fertiliser: the instant craft (whose end scripts add the fertiliser perk to the bed) and
        /// the perk's slot on the bed.
        /// </summary>
        private static void Fertilize(string wgoId, string cropId, string fertilizerId)
        {
            object wgo = FindWgo(wgoId);
            object component = wgo == null ? null : CoopDiagnostics.GetMember(wgo, "CraftComponent");
            object crop = AccessTools.Method(Plugin.FindGameType("GameBalance"), "GetCraftDefBase", new[] { typeof(string) }).Invoke(null, new object[] { cropId });
            if (component == null || crop == null)
            {
                log.LogWarning($"Garden sync: bed {Short(wgoId)} or craft {cropId} not found; fertilising not mirrored.");
                return;
            }
            object fertilizer = Activator.CreateInstance(Plugin.FindGameType("Item"), fertilizerId, 1);
            int talent = Convert.ToInt32(CoopDiagnostics.GetMember(CoopDiagnostics.GetMember(fertilizer, "Definition"), "talentValue"));
            object needs = AccessTools.Method(Plugin.FindGameType("GardenInteractionHandler"), "FormNeedItems").Invoke(null, new[] { crop, fertilizer });
            Type paramsType = Plugin.FindGameType("CraftParamsData");
            object parameters = Activator.CreateInstance(paramsType, cropId, wgo, Enum.Parse(paramsType.GetNestedType("CraftParamsType"), "Common"), talent);
            object worker = CoopDiagnostics.GetMember(wgo, "Worker") ?? CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerController");
            AccessTools.Method(paramsType, "RecalculateParams", new[] { needs.GetType(), Plugin.FindGameType("IWorker") }).Invoke(parameters, new[] { needs, worker });
            object noRequirements = Activator.CreateInstance(typeof(List<>).MakeGenericType(Plugin.FindGameType("NeedItemData")));
            object element = Activator.CreateInstance(Plugin.FindGameType("CraftElement"), cropId, 1, noRequirements, parameters);
            applying = true;
            try
            {
                AccessTools.Method(component.GetType(), "ProcessInstantCraft").Invoke(component, new[] { wgo, element });
                AssignNewestFertilizerSlot(wgo);
                applied++;
                log.LogInfo($"Garden sync: fertilised bed {Short(wgoId)} with {fertilizerId} for another player.");
            }
            finally
            {
                applying = false;
            }
        }

        /// <summary>The slot rule of <c>GardenInteractionHandler.TryAssignPerkSlotForNewestAddedPerk</c>.</summary>
        private static void AssignNewestFertilizerSlot(object wgo)
        {
            var free = new List<int> { 1, 2, 3 };
            object newest = null;
            MethodInfo getInt = AccessTools.Method(wgo.GetType(), "GetGameResInt", new[] { typeof(string) });
            if (!(CoopDiagnostics.GetMember(wgo, "ActivePerks") is System.Collections.IEnumerable perks))
            {
                return;
            }
            foreach (object perk in perks)
            {
                object definition = CoopDiagnostics.GetMember(perk, "Definition");
                if (!Convert.ToBoolean(CoopDiagnostics.GetMember(definition, "IsFertilizerPerk")))
                {
                    continue;
                }
                int slot = Convert.ToInt32(getInt.Invoke(wgo, new object[] { "perk_fertilize_" + CoopDiagnostics.GetMember(definition, "id") }));
                if (slot > 0)
                {
                    free.Remove(slot);
                }
                else
                {
                    newest = perk;
                }
            }
            if (newest != null && free.Count > 0)
            {
                string key = "perk_fertilize_" + CoopDiagnostics.GetMember(CoopDiagnostics.GetMember(newest, "Definition"), "id");
                AccessTools.Method(wgo.GetType(), "SetGameRes", new[] { typeof(string), typeof(int) }).Invoke(wgo, new object[] { key, free[0] });
            }
        }

        private static object FindWgo(string id)
        {
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            object save = mainGame == null ? null : CoopDiagnostics.GetMember(mainGame, "GameSave");
            object world = save == null ? null : CoopDiagnostics.GetMember(save, "worldData");
            Type sguid = Plugin.FindGameType("SGuid");
            MethodInfo parse = AccessTools.Method(sguid, "Parse");
            MethodInfo get = world == null ? null : AccessTools.Method(world.GetType(), "GetWgoData", new[] { sguid });
            return parse == null || get == null ? null : get.Invoke(world, new[] { parse.Invoke(null, new object[] { id }) });
        }

        private static string WgoId(object wgo)
        {
            return Convert.ToString(CoopDiagnostics.GetMember(CoopDiagnostics.GetMember(wgo, "UniqueId"), "Id"));
        }

        private static string Short(string id)
        {
            return string.IsNullOrEmpty(id) ? "?" : (id.Length > 8 ? id.Substring(0, 8) : id);
        }

        private static Exception Inner(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null)
            {
                ex = ex.InnerException;
            }
            return ex;
        }
    }
}
