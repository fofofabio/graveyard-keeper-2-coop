using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using Unity.Collections;
using Unity.Netcode;

namespace GK2Coop
{
    /// <summary>
    /// One tech tree, one recipe book for everyone.
    ///
    /// Learned techs, unlocked recipes, buildings, alchemy formulas, dialogue phrases and organs
    /// live in <c>GameSave.knowledgeSystem</c>, world state, while the tech points that buy them are
    /// each player's own. A joiner who bought a tech had it only on their own machine; the next
    /// copied-world join brought back the host's tree without it, and the points were gone.
    ///
    /// Each machine compares the knowledge lists with what it last saw every couple of seconds and
    /// sends only what is new; the host applies a joiner's news and passes it on. Knowledge only
    /// grows here: what one player learns, everyone knows. A learned tech is applied through the
    /// game's own <c>TechDef.Unlock(free: true)</c>, so its recipes, buildings and perks follow
    /// without charging the receiver. A joiner also receives the host's whole knowledge on joining,
    /// for anything learned since the save it copied.
    /// </summary>
    internal static class CoopKnowledgeSync
    {
        internal const string KnowledgeMessage = "GK2Coop.Knowledge.v1";

        /// <summary>Shared lists, each with the game method that adds one entry (null: plain list add).</summary>
        private static readonly KeyValuePair<string, string>[] Fields =
        {
            new KeyValuePair<string, string>("unlockedTechs", null),
            new KeyValuePair<string, string>("revealedTechs", "RevealTech"),
            new KeyValuePair<string, string>("unlockedCrafts", "UnlockCraft"),
            new KeyValuePair<string, string>("oneTimeCompletedCrafts", null),
            new KeyValuePair<string, string>("unlockedBuildings", "UnlockBuilding"),
            new KeyValuePair<string, string>("unlockedTownBuildings", "UnlockTownBuilding"),
            new KeyValuePair<string, string>("unlockedAlchemyFormulas", "UnlockAlchemyFormula"),
            new KeyValuePair<string, string>("knownMixCrafts", null),
            new KeyValuePair<string, string>("unlockedPhrases", "UnlockPhrase"),
            new KeyValuePair<string, string>("unlockedTalentIds", "UnlockTalentBranch"),
            new KeyValuePair<string, string>("unlockedVendorsForOrders", "UnlockVendorForOrders"),
            new KeyValuePair<string, string>("unlockedOrgans", "UnlockOrgan"),
            new KeyValuePair<string, string>("knownMapZones", null),
            // The game keeps the opposite, the locked tabs (a new game locks them, the intro's
            // scripts unlock them); shared as what is unlocked, so it only grows like the rest.
            new KeyValuePair<string, string>(UnlockedCharTabs, null),
            new KeyValuePair<string, string>(UnlockedTechTabs, null),
        };

        private const string UnlockedCharTabs = "unlockedCharTabs";
        private const string UnlockedTechTabs = "unlockedTechTabs";

        private static ManualLogSource log;
        private static readonly Dictionary<string, HashSet<string>> seen = new Dictionary<string, HashSet<string>>();
        private static object baselineOf;
        private static float nextCheck;
        private static int sent;
        private static int applied;

        internal static bool Enabled { get; set; } = true;

        internal static string Describe()
        {
            return "knowledge sync: entries sent=" + sent + ", learned from others=" + applied;
        }

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        /// <summary>Driven from the plugin's one-second poll.</summary>
        internal static void Tick()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening || (!netcode.IsHost && !CoopSession.Welcomed))
            {
                seen.Clear();
                baselineOf = null;
                return;
            }
            if (UnityEngine.Time.unscaledTime < nextCheck)
            {
                return;
            }
            nextCheck = UnityEngine.Time.unscaledTime + 2f;
            try
            {
                SendNews(netcode);
            }
            catch (Exception ex)
            {
                log.LogWarning("Knowledge sync: " + Inner(ex).Message);
                nextCheck = UnityEngine.Time.unscaledTime + 30f;
            }
        }

        /// <summary>Host: everything it knows, to a player who just joined.</summary>
        internal static void SendAllTo(ulong clientId)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            object knowledge = Knowledge();
            if (!Enabled || netcode == null || !netcode.IsHost || knowledge == null)
            {
                return;
            }
            try
            {
                var all = new List<KeyValuePair<string, string>>();
                foreach (KeyValuePair<string, string> field in Fields)
                {
                    foreach (string value in Read(knowledge, field.Key))
                    {
                        all.Add(new KeyValuePair<string, string>(field.Key, value));
                    }
                }
                Send(netcode, clientId, null, all);
            }
            catch (Exception ex)
            {
                log.LogWarning("Knowledge sync: could not send the host's knowledge to client " + clientId + ": " + Inner(ex).Message);
            }
        }

        private static void SendNews(NetworkManager netcode)
        {
            object knowledge = Knowledge();
            if (knowledge == null)
            {
                return;
            }
            if (!ReferenceEquals(baselineOf, knowledge))
            {
                // A new world: what it holds now is the starting point, not news.
                Rebaseline(knowledge);
                return;
            }
            var news = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> field in Fields)
            {
                HashSet<string> known = Seen(field.Key);
                foreach (string value in Read(knowledge, field.Key))
                {
                    if (known.Add(value))
                    {
                        news.Add(new KeyValuePair<string, string>(field.Key, value));
                    }
                }
            }
            if (news.Count == 0)
            {
                return;
            }
            if (netcode.IsHost)
            {
                Send(netcode, null, null, news);
            }
            else
            {
                Send(netcode, NetworkManager.ServerClientId, null, news);
            }
            log.LogInfo("Knowledge sync: shared " + news.Count + " new entr" + (news.Count == 1 ? "y" : "ies") + " (" + Preview(news) + ").");
        }

        private static void Send(NetworkManager netcode, ulong? target, ulong? except, List<KeyValuePair<string, string>> entries)
        {
            var text = new StringBuilder();
            foreach (KeyValuePair<string, string> entry in entries)
            {
                text.Append(entry.Key).Append('\t').Append(entry.Value).Append('\n');
            }
            byte[] compressed = Compress(Encoding.UTF8.GetBytes(text.ToString()));
            using (var writer = new FastBufferWriter(16 + compressed.Length, Allocator.Temp))
            {
                writer.WriteValueSafe(compressed.Length);
                writer.WriteBytesSafe(compressed, compressed.Length);
                NetworkDelivery delivery = compressed.Length > 1000 ? NetworkDelivery.ReliableFragmentedSequenced : NetworkDelivery.ReliableSequenced;
                if (target.HasValue)
                {
                    netcode.CustomMessagingManager.SendNamedMessage(KnowledgeMessage, target.Value, writer, delivery);
                }
                else
                {
                    foreach (ulong clientId in netcode.ConnectedClientsIds)
                    {
                        if (clientId != netcode.LocalClientId && clientId != except)
                        {
                            netcode.CustomMessagingManager.SendNamedMessage(KnowledgeMessage, clientId, writer, delivery);
                        }
                    }
                }
            }
            sent += entries.Count;
        }

        internal static void Receive(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening)
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out int length);
                if (length <= 0 || length > 1024 * 1024)
                {
                    return;
                }
                byte[] compressed = new byte[length];
                reader.ReadBytesSafe(ref compressed, length);
                var entries = new List<KeyValuePair<string, string>>();
                foreach (string line in Encoding.UTF8.GetString(Decompress(compressed)).Split('\n'))
                {
                    int tab = line.IndexOf('\t');
                    if (tab > 0)
                    {
                        entries.Add(new KeyValuePair<string, string>(line.Substring(0, tab), line.Substring(tab + 1)));
                    }
                }
                object knowledge = Knowledge();
                if (knowledge == null)
                {
                    return;
                }
                // Our own unsent news first, so the re-baseline below cannot swallow it.
                if (ReferenceEquals(baselineOf, knowledge))
                {
                    SendNews(netcode);
                }
                int learned = Apply(knowledge, entries);
                Rebaseline(knowledge);
                if (netcode.IsHost && sender != netcode.LocalClientId)
                {
                    Send(netcode, null, sender, entries);
                }
                if (learned > 0)
                {
                    applied += learned;
                    log.LogInfo("Knowledge sync: learned " + learned + " entr" + (learned == 1 ? "y" : "ies") + " from " + (sender == NetworkManager.ServerClientId ? "the host" : "client " + sender) + " (" + Preview(entries) + ").");
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Knowledge sync: could not apply knowledge from " + sender + ": " + Inner(ex).Message);
            }
        }

        private static int Apply(object knowledge, List<KeyValuePair<string, string>> entries)
        {
            int learned = 0;
            Type type = knowledge.GetType();
            foreach (KeyValuePair<string, string> entry in entries)
            {
                string method = null;
                bool known = false;
                foreach (KeyValuePair<string, string> field in Fields)
                {
                    if (field.Key == entry.Key)
                    {
                        method = field.Value;
                        known = true;
                    }
                }
                if (!known || Contains(knowledge, entry.Key, entry.Value))
                {
                    continue;
                }
                try
                {
                    if (entry.Key == "unlockedTechs")
                    {
                        UnlockTech(knowledge, entry.Value);
                    }
                    else if (entry.Key == UnlockedCharTabs || entry.Key == UnlockedTechTabs)
                    {
                        UnlockTab((KnowledgeSystem)knowledge, entry.Key, entry.Value);
                        learned++;
                        continue;
                    }
                    else if (entry.Key == "unlockedOrgans")
                    {
                        Type itemType = Plugin.FindGameType("ItemType");
                        type.GetMethod(method, new[] { itemType }).Invoke(knowledge, new[] { Enum.Parse(itemType, entry.Value) });
                    }
                    else if (method != null && type.GetMethod(method, new[] { typeof(string) }) is MethodInfo add)
                    {
                        add.Invoke(knowledge, new object[] { entry.Value });
                    }
                    if (!Contains(knowledge, entry.Key, entry.Value) && CoopDiagnostics.GetMember(knowledge, entry.Key) is IList list)
                    {
                        list.Add(entry.Value);
                    }
                    learned++;
                }
                catch (Exception ex)
                {
                    log.LogWarning("Knowledge sync: could not add " + entry.Key + " '" + entry.Value + "': " + Inner(ex).Message);
                }
            }
            return learned;
        }

        /// <summary>Through the tech's own unlock, free, so its recipes, buildings and perks come with it.</summary>
        private static void UnlockTech(object knowledge, string techId)
        {
            Type techType = Plugin.FindGameType("TechDef");
            object balance = CoopDiagnostics.GetStatic(Plugin.FindGameType("GameBalance"), "Me");
            MethodInfo getData = null;
            foreach (MethodInfo method in balance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (method.Name == "GetData" && method.IsGenericMethodDefinition && method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType == typeof(string))
                {
                    getData = method.MakeGenericMethod(techType);
                    break;
                }
            }
            object tech = getData?.Invoke(balance, new object[] { techId });
            if (tech != null)
            {
                techType.GetMethod("Unlock", new[] { typeof(bool) }).Invoke(tech, new object[] { true });
            }
            else
            {
                knowledge.GetType().GetMethod("UnlockTech", new[] { typeof(string), typeof(bool) }).Invoke(knowledge, new object[] { techId, false });
            }
        }

        private static void Rebaseline(object knowledge)
        {
            seen.Clear();
            foreach (KeyValuePair<string, string> field in Fields)
            {
                HashSet<string> known = Seen(field.Key);
                foreach (string value in Read(knowledge, field.Key))
                {
                    known.Add(value);
                }
            }
            baselineOf = knowledge;
        }

        private static HashSet<string> Seen(string field)
        {
            if (!seen.TryGetValue(field, out HashSet<string> set))
            {
                set = new HashSet<string>();
                seen[field] = set;
            }
            return set;
        }

        /// <summary>A tab the other player has: the game's own unlock (it takes it off the locked list).</summary>
        private static void UnlockTab(KnowledgeSystem knowledge, string field, string value)
        {
            if (field == UnlockedCharTabs)
            {
                knowledge.UnlockCharTab((CharacterWindowData.CharPage)Enum.Parse(typeof(CharacterWindowData.CharPage), value));
            }
            else
            {
                knowledge.UnlockTechTab((TechTreeTab)Enum.Parse(typeof(TechTreeTab), value));
            }
        }

        /// <summary>The tabs of the character window, or of the tech tree, that are not locked.</summary>
        private static IEnumerable<string> UnlockedTabs(KnowledgeSystem knowledge, string field)
        {
            if (field == UnlockedCharTabs)
            {
                foreach (CharacterWindowData.CharPage page in Enum.GetValues(typeof(CharacterWindowData.CharPage)))
                {
                    if (page != CharacterWindowData.CharPage.Undefined && !knowledge.IsCharTabLocked(page))
                    {
                        yield return page.ToString();
                    }
                }
            }
            else
            {
                foreach (TechTreeTab tab in Enum.GetValues(typeof(TechTreeTab)))
                {
                    if (!knowledge.IsTechTabLocked(tab))
                    {
                        yield return tab.ToString();
                    }
                }
            }
        }

        private static IEnumerable<string> Read(object knowledge, string field)
        {
            if ((field == UnlockedCharTabs || field == UnlockedTechTabs) && knowledge is KnowledgeSystem system)
            {
                foreach (string tab in UnlockedTabs(system, field))
                {
                    yield return tab;
                }
                yield break;
            }
            if (CoopDiagnostics.GetMember(knowledge, field) is IEnumerable values)
            {
                foreach (object value in values)
                {
                    if (value != null)
                    {
                        yield return value.ToString();
                    }
                }
            }
        }

        private static bool Contains(object knowledge, string field, string value)
        {
            foreach (string existing in Read(knowledge, field))
            {
                if (existing == value)
                {
                    return true;
                }
            }
            return false;
        }

        internal static object Knowledge()
        {
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            object save = mainGame == null ? null : CoopDiagnostics.GetMember(mainGame, "GameSave");
            return save == null ? null : CoopDiagnostics.GetMember(save, "knowledgeSystem");
        }

        private static string Preview(List<KeyValuePair<string, string>> entries)
        {
            var parts = new List<string>();
            for (int i = 0; i < entries.Count && i < 4; i++)
            {
                parts.Add(entries[i].Key + ":" + entries[i].Value);
            }
            return string.Join(", ", parts.ToArray()) + (entries.Count > 4 ? ", …" : string.Empty);
        }

        private static byte[] Compress(byte[] raw)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }
                return output.ToArray();
            }
        }

        private static byte[] Decompress(byte[] compressed)
        {
            using (var input = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                input.CopyTo(output);
                return output.ToArray();
            }
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
