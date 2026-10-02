using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Each joining player keeps their own inventory, tool belt, money, tech points, energy and
    /// health from one session to the next, stored by the host.
    ///
    /// The problem this fixes: "Copy host world and join" loads the host's save on the joiner,
    /// and a save has one player in it — the host. So every joiner started as a copy of the host,
    /// carrying the host's inventory and money. Items were duplicated, and nothing the joiner
    /// earned survived to the next session, because the next session copied the host again.
    ///
    /// Identity is a random key made once per installation (<c>[Session] PlayerKey</c>), not the
    /// Netcode client id, which changes every connection. The joiner sends its whole
    /// <c>PlayerData</c>, serialized by the game's own <c>LazySerializer</c>, to the host every
    /// <see cref="UploadSeconds"/>. The host writes it under
    /// <c>BepInEx\config\GK2Coop\players\&lt;host save slot&gt;\&lt;key&gt;.dat</c>, never inside the
    /// game's save files. When a joiner arrives in a copied world, the host sends back that
    /// player's stored state, or tells it to start fresh with the game's own new-player start.
    ///
    /// Restoring copies values into the live objects rather than replacing them: the game and
    /// its UI hold references to the inventories and to the resource store, and a replaced object
    /// would leave them watching a stale one. A returning player is put back where they last were
    /// when that spot is in the scene the copied world starts in; otherwise they appear where the
    /// host's world puts them. The player's look (customization) is restored and re-applied,
    /// and <see cref="CoopAppearanceSync"/> shows it to the others.
    ///
    /// A joiner who joined from their own matching save (not a copy) keeps what that save has,
    /// and still uploads, so a later copied-world join can restore it.
    /// </summary>
    internal static class CoopPlayerProfiles
    {
        internal const string HelloMessage = "GK2Coop.Profile.Hello.v1";
        internal const string DataMessage = "GK2Coop.Profile.Data.v1";

        private static readonly string[] OwnResources = { "money", "energy", "insanity" };
        private static readonly string[] OwnResourcesAndGems =
        {
            "money", "energy", "insanity", "tech_red", "tech_green", "tech_blue"
        };

        /// <summary>
        /// Resource values restored per player; every other resource is world state. The action
        /// gems are one of each player's own only when the host plays with per-player gems
        /// (<see cref="CoopSharedGems"/>).
        /// </summary>
        internal static string[] PersonalResources => CoopSharedGems.Shared ? OwnResources : OwnResourcesAndGems;

        private const float UploadSeconds = 30f;
        private const int ChunkSize = 1024;
        private const int MaxBytes = 4 * 1024 * 1024;

        private enum Mode
        {
            KeepOwn = 0,
            NewPlayer = 1,
            Stored = 2,
            Upload = 3,
            // The host to a joiner: another player here already goes by your key (a game folder
            // copied from a friend carries their key along). Make a key of your own and say hello again.
            KeyInUse = 4
        }

        /// <summary>Stores a newly made key in this game's settings (set by the plugin).</summary>
        internal static Action<string> SaveKey { get; set; }

        private sealed class Incoming
        {
            internal Mode Mode;
            internal int TransferId;
            internal int Count;
            internal byte[] Buffer;
            internal int Received;
        }

        private static ManualLogSource log;
        private static string localKey = string.Empty;

        // Client side
        private static bool helloSent;
        private static bool settled;
        private static float nextUpload;
        private static int outgoingTransfer;
        private static Incoming incomingRestore;
        private static byte[] pendingRestore;
        private static Mode pendingMode = Mode.KeepOwn;
        private static bool pendingApply;

        // Host side
        private static readonly Dictionary<ulong, string> keysByClient = new Dictionary<ulong, string>();
        private static readonly Dictionary<ulong, Incoming> uploads = new Dictionary<ulong, Incoming>();

        internal static bool Enabled { get; set; } = true;

        /// <summary>For the HUD and tests: what happened to this player's state this session.</summary>
        internal static string LastOutcome { get; private set; } = "none";

        internal static void Init(ManualLogSource source, string playerKey)
        {
            log = source;
            localKey = playerKey ?? string.Empty;
        }

        internal static void Reset()
        {
            helloSent = false;
            settled = false;
            incomingRestore = null;
            pendingRestore = null;
            pendingApply = false;
            nextUpload = 0f;
        }

        internal static void ForgetClient(ulong clientId)
        {
            keysByClient.Remove(clientId);
            uploads.Remove(clientId);
        }

        /// <summary>At the end of a hosted session: the next session numbers its players afresh.</summary>
        internal static void ForgetAllClients()
        {
            keysByClient.Clear();
            uploads.Clear();
        }

        // ------------------------------------------------------------------ client

        /// <summary>Driven from the plugin's one-second poll.</summary>
        internal static void Tick()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening || netcode.IsHost)
            {
                return;
            }
            if (!netcode.IsConnectedClient)
            {
                if (helloSent) Reset();
                return;
            }
            if (!CoopSession.Welcomed || !InGame())
            {
                return;
            }
            if (!helloSent)
            {
                SendHello(netcode);
                return;
            }
            if (pendingApply)
            {
                pendingApply = false;
                // Uploads start only after a successful restore. After a failed one this player is
                // still carrying the host's copy, and uploading it would overwrite their real
                // stored state with the host's inventory.
                settled = Apply(pendingMode, pendingRestore);
                pendingRestore = null;
                nextUpload = Time.unscaledTime + 5f;
            }
            if (settled && Time.unscaledTime >= nextUpload)
            {
                nextUpload = Time.unscaledTime + UploadSeconds;
                Upload(netcode, ownChanged ? "after a change" : "periodic");
                ownChanged = false;
            }
        }

        /// <summary>
        /// A joiner's own inventory changed: the host stores it within a second or two rather than at
        /// the next periodic upload. What a joiner took from a chest left the chest on the host at
        /// once; their game ending (a crash, a lost connection) before the next upload lost it for
        /// good. Several changes in that time are one upload.
        /// </summary>
        private static bool ownChanged;

        internal static void OwnInventoryChanged(object inventory)
        {
            if (!Enabled || !settled || inventory == null)
            {
                return;
            }
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || netcode.IsHost || !netcode.IsConnectedClient)
            {
                return;
            }
            object player = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerData");
            if (player == null || !ReferenceEquals(CoopDiagnostics.GetMember(player, "inventory"), inventory))
            {
                return;
            }
            float soon = Time.unscaledTime + 1f;
            if (nextUpload > soon)
            {
                nextUpload = soon;
                ownChanged = true;
            }
        }

        /// <summary>Best effort on the way out, so a clean quit loses nothing since the last upload.</summary>
        internal static void UploadNow(string reason)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (Enabled && settled && netcode != null && netcode.IsConnectedClient && !netcode.IsHost)
            {
                Upload(netcode, reason);
            }
        }

        private static void SendHello(NetworkManager netcode)
        {
            bool copied = LoadedSlotName().StartsWith("GK2Coop_", StringComparison.Ordinal);
            using (var writer = new FastBufferWriter(128, Allocator.Temp))
            {
                writer.WriteValueSafe(new FixedString64Bytes(localKey));
                writer.WriteValueSafe(copied);
                netcode.CustomMessagingManager.SendNamedMessage(HelloMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            }
            helloSent = true;
            log.LogInfo("Player profile: introduced as " + ShortKey(localKey) + (copied ? " in a copied host world." : " in this player's own save."));
        }

        private static void Upload(NetworkManager netcode, string reason)
        {
            try
            {
                object playerData = LocalPlayerData();
                if (playerData == null)
                {
                    return;
                }
                byte[] compressed = Compress(Serialize(playerData));
                SendChunks(netcode, NetworkManager.ServerClientId, Mode.Upload, compressed);
                log.LogInfo($"Player profile: uploaded {compressed.Length} bytes ({reason}).");
            }
            catch (Exception ex)
            {
                log.LogWarning("Player profile: could not upload this player's state: " + Inner(ex).Message);
            }
        }

        private static bool Apply(Mode mode, byte[] compressed)
        {
            if (mode == Mode.KeepOwn)
            {
                LastOutcome = "kept own save";
                log.LogInfo("Player profile: keeping this player's own save state.");
                return true;
            }
            try
            {
                object live = LocalPlayerData();
                object storedTalents = null;
                object source = mode == Mode.Stored ? DeserializeBundle(Decompress(compressed), out storedTalents) : CreateFreshPlayer();
                string before = Summary(live);
                CopyState(source, live);
                ApplyLook(live, mode == Mode.Stored ? GetField(source, "customization") : null);
                string where = mode == Mode.Stored ? ReturnToLastPosition(live, source) : string.Empty;
                // A first-time joiner keeps the talents the copied world gave them (the host's):
                // the game gates even basic station recipes on talent levels, so fresh talents
                // locked a new player out of every workbench. From here on they progress apart.
                where += mode == Mode.Stored ? RestoreTalents(storedTalents) : string.Empty;
                LastOutcome = mode == Mode.Stored ? "restored" : "new player";
                string message = mode == Mode.Stored
                    ? L.T("Welcome back. Your inventory and progress are restored.")
                    : L.T("You start with a fresh inventory.");
                CoopStatus.Announce(message, false, 8f);
                log.LogInfo("Player profile: " + LastOutcome + ". Before: " + before + ". After: " + Summary(live) + "." + where);
                return true;
            }
            catch (Exception ex)
            {
                LastOutcome = "failed";
                CoopStatus.Announce(L.T("Your inventory could not be restored. You have the host's copy for now."), true, 12f);
                log.LogError("Player profile: could not apply " + mode + ": " + Inner(ex) +
                             " Uploads are off for this session so the stored state is kept.");
                return false;
            }
        }

        // ------------------------------------------------------------------ host

        internal static void ReceiveHello(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsHost || sender == netcode.LocalClientId)
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out FixedString64Bytes keyText);
                reader.ReadValueSafe(out bool copied);
                string key = keyText.ToString();
                if (!Regex.IsMatch(key, "^[0-9a-f]{32}$"))
                {
                    log.LogWarning("Player profile: client " + sender + " sent an invalid player key; ignoring its profile.");
                    return;
                }
                // Two players with one key would store over each other's inventory and progress
                // (found by `mix3`: test copies cloned from one folder share a key, as a game folder
                // copied to a friend would). The second gets told and makes a key of its own.
                foreach (KeyValuePair<ulong, string> other in keysByClient)
                {
                    if (other.Key != sender && other.Value == key && System.Linq.Enumerable.Contains(netcode.ConnectedClientsIds, other.Key))
                    {
                        SendChunks(netcode, sender, Mode.KeyInUse, new byte[0]);
                        log.LogWarning($"Player profile: client {sender} ({CoopSession.NameFor(sender)}) has the same player key as client {other.Key}; told it to make its own.");
                        return;
                    }
                }
                keysByClient[sender] = key;
                string path = ProfilePath(key);
                byte[] stored = File.Exists(path) ? File.ReadAllBytes(path) : null;
                Mode mode = !copied ? Mode.KeepOwn : stored != null ? Mode.Stored : Mode.NewPlayer;
                SendChunks(netcode, sender, mode, mode == Mode.Stored ? stored : new byte[0]);
                if (stored != null)
                {
                    MirrorIntoShadow(sender, stored);
                }
                log.LogInfo($"Player profile: client {sender} is {ShortKey(key)} ({CoopSession.NameFor(sender)}); sent {mode}" +
                            (stored != null ? $" ({stored.Length} bytes)" : string.Empty) + ".");
            }
            catch (Exception ex)
            {
                log.LogWarning("Player profile: could not answer client " + sender + ": " + Inner(ex).Message);
            }
        }

        internal static void ReceiveData(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening)
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out int modeValue);
                reader.ReadValueSafe(out int transferId);
                reader.ReadValueSafe(out int index);
                reader.ReadValueSafe(out int count);
                reader.ReadValueSafe(out int total);
                reader.ReadValueSafe(out int length);
                var mode = (Mode)modeValue;
                bool toHost = mode == Mode.Upload;
                if (toHost != netcode.IsHost || (!toHost && sender != NetworkManager.ServerClientId))
                {
                    return;
                }
                if (total < 0 || total > MaxBytes || count < 0 || count > MaxBytes / ChunkSize + 1 || length < 0 || length > ChunkSize)
                {
                    throw new InvalidDataException("profile transfer header out of range");
                }

                Incoming state;
                if (toHost)
                {
                    uploads.TryGetValue(sender, out state);
                }
                else
                {
                    state = incomingRestore;
                }
                if (state == null || state.TransferId != transferId)
                {
                    if (index != 0)
                    {
                        return;
                    }
                    state = new Incoming { Mode = mode, TransferId = transferId, Count = count, Buffer = new byte[total] };
                    if (toHost) uploads[sender] = state; else incomingRestore = state;
                }
                if (length > 0)
                {
                    int offset = index * ChunkSize;
                    if (offset + length > state.Buffer.Length)
                    {
                        throw new InvalidDataException("profile chunk out of range");
                    }
                    byte[] chunk = new byte[length];
                    reader.ReadBytesSafe(ref chunk, length);
                    Buffer.BlockCopy(chunk, 0, state.Buffer, offset, length);
                    state.Received++;
                }
                if (state.Received < state.Count)
                {
                    return;
                }

                if (toHost)
                {
                    uploads.Remove(sender);
                    StoreUpload(sender, state.Buffer);
                }
                else if (state.Mode == Mode.KeyInUse)
                {
                    incomingRestore = null;
                    localKey = Guid.NewGuid().ToString("N");
                    SaveKey?.Invoke(localKey);
                    helloSent = false;
                    log.LogWarning("Player profile: another player in this session has this game's player key (a copied game folder?); made a new one, " + ShortKey(localKey) + ", and introducing again as a new player.");
                }
                else
                {
                    incomingRestore = null;
                    pendingMode = state.Mode;
                    pendingRestore = state.Buffer;
                    pendingApply = true;
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Player profile: dropped a malformed transfer from " + sender + ": " + Inner(ex).Message);
            }
        }

        private static void StoreUpload(ulong sender, byte[] compressed)
        {
            if (!keysByClient.TryGetValue(sender, out string key))
            {
                log.LogWarning("Player profile: client " + sender + " uploaded state before introducing itself; ignored.");
                return;
            }
            // Reject anything the game cannot read back before it replaces a good copy on disk.
            Deserialize(Decompress(compressed));
            string path = ProfilePath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, compressed);
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
            File.WriteAllText(Path.ChangeExtension(path, ".txt"),
                CoopSession.NameFor(sender) + "\r\n" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n");
            MirrorIntoShadow(sender, compressed);
            log.LogInfo($"Player profile: stored {compressed.Length} bytes for {CoopSession.NameFor(sender)} ({ShortKey(key)}).");
        }

        /// <summary>
        /// The host keeps its own copy of each joiner's player data and checks pickups against
        /// that copy's inventory space. Keeping it in step with what the joiner really carries
        /// stops the host accepting a pickup for a full backpack, or refusing one with room.
        /// </summary>
        private static void MirrorIntoShadow(ulong clientId, byte[] compressed)
        {
            try
            {
                object shadow = CoopDropSync.ResolvePlayerDataFor(clientId);
                if (shadow == null)
                {
                    return;
                }
                object stored = Deserialize(Decompress(compressed));
                CopyInventory(stored, shadow, "inventory");
                CopyInventory(stored, shadow, "toolBeltInventory");
            }
            catch (Exception ex)
            {
                log.LogWarning("Player profile: could not refresh the host's copy of client " + clientId + ": " + Inner(ex).Message);
            }
        }

        private static string ProfilePath(string key)
        {
            string slot = LoadedSlotName();
            if (string.IsNullOrEmpty(slot) || !Regex.IsMatch(slot, "^[A-Za-z0-9_-]{1,80}$"))
            {
                slot = "unsaved";
            }
            return Path.Combine(Paths.ConfigPath, "GK2Coop", "players", slot, key + ".dat");
        }

        // ------------------------------------------------------------------ transfer

        private static void SendChunks(NetworkManager netcode, ulong target, Mode mode, byte[] data)
        {
            int count = (data.Length + ChunkSize - 1) / ChunkSize;
            int transferId = ++outgoingTransfer;
            // Zero chunks still sends one header, so a "keep" or "new player" answer arrives.
            for (int index = 0; index < Math.Max(1, count); index++)
            {
                int offset = index * ChunkSize;
                int length = count == 0 ? 0 : Math.Min(ChunkSize, data.Length - offset);
                using (var writer = new FastBufferWriter(32 + length, Allocator.Temp))
                {
                    writer.WriteValueSafe((int)mode);
                    writer.WriteValueSafe(transferId);
                    writer.WriteValueSafe(index);
                    writer.WriteValueSafe(count);
                    writer.WriteValueSafe(data.Length);
                    writer.WriteValueSafe(length);
                    if (length > 0)
                    {
                        writer.WriteBytesSafe(data, length, offset);
                    }
                    netcode.CustomMessagingManager.SendNamedMessage(DataMessage, target, writer, NetworkDelivery.ReliableSequenced);
                }
            }
        }

        // ------------------------------------------------------------------ game state

        /// <summary>
        /// Everything that belongs to a person rather than to the world: what they carry, their
        /// wallet and tech points, their energy and health, and which tutorials they have seen.
        /// </summary>
        private static void CopyState(object source, object target)
        {
            CopyInventory(source, target, "inventory");
            CopyInventory(source, target, "toolBeltInventory");

            CopyResources(GetField(source, "res"), GetField(target, "res"));

            object sourceHp = GetField(source, "hpComponent");
            object targetHp = GetField(target, "hpComponent");
            foreach (string field in new[] { "maxHpValue", "hp", "prevHp" })
            {
                SetField(targetHp, field, GetField(sourceHp, field));
            }

            foreach (string field in new[]
            {
                "overheadItems", "pinnedItems", "isInTutorialMode", "tutorialModeExcludedList",
                "openedCraftWindowOnce", "interactedWithFishingReservoirOnce", "sawFightTutorialOnce",
                "sawInspirationTutorialOnce", "sawInspirationTalentsTutorialOnce",
                "interactedWithChalkBoardOnce", "openedGraveWindowOnce"
            })
            {
                FieldInfo info = FindField(target.GetType(), field);
                if (info != null)
                {
                    info.SetValue(target, info.GetValue(source));
                }
            }
        }

        /// <summary>
        /// Writes the stored values straight into the live resource store. Going through
        /// <c>GameRes.Set</c> routes energy, insanity and tech points through their resource systems,
        /// which clamp against balance expressions; one of those threw inside the game while a
        /// restore was half done. The values came from the game in the first place, so they need no
        /// clamping. Each system is then set silently to its own value, which is what refreshes
        /// the HUD, and a system that fails there is skipped rather than aborting the restore.
        /// </summary>
        private static void CopyResources(object sourceRes, object targetRes)
        {
            Type resType = targetRes.GetType();
            MethodInfo setRaw = resType.GetMethod("SetWithoutSystemsCheck", new[] { typeof(string), typeof(float) });
            MethodInfo getRaw = resType.GetMethod("GetWithoutSystemsCheck", new[] { typeof(string), typeof(float) });
            var atoms = new List<KeyValuePair<string, float>>();
            // Only these belong to a person. The same store also carries the world's state — zone
            // ratings (wz_*), congregation size, sermon readiness, farming modifiers — which the
            // copied save already holds with the host's current values and which must stay that way.
            foreach (string type in PersonalResources)
            {
                float value = Convert.ToSingle(getRaw.Invoke(sourceRes, new object[] { type, 0f }));
                setRaw.Invoke(targetRes, new object[] { type, value });
                atoms.Add(new KeyValuePair<string, float>(type, value));
            }
            MethodInfo getSystem = resType.GetMethod("GetSystem", new[] { typeof(string) });
            foreach (KeyValuePair<string, float> atom in atoms)
            {
                try
                {
                    object system = getSystem?.Invoke(targetRes, new object[] { atom.Key });
                    system?.GetType().GetMethod("Set", new[] { typeof(float), typeof(bool) })?.Invoke(system, new object[] { atom.Value, false });
                }
                catch (Exception ex)
                {
                    setRaw.Invoke(targetRes, new object[] { atom.Key, atom.Value });
                    log.LogInfo("Player profile: " + atom.Key + " restored without its HUD refresh (" + Inner(ex).GetType().Name + ").");
                }
            }
        }

        /// <summary>
        /// The player's own look, applied through the game's local-player path. A new player's
        /// look is the game's default one: the customization of a freshly created PlayerData is
        /// empty, and the game's skin builder throws on an empty one. A look that cannot be applied
        /// is logged and skipped — it must never fail the rest of the restore.
        /// </summary>
        private static void ApplyLook(object live, object stored)
        {
            try
            {
                Type customizationType = Plugin.FindGameType("PlayerCustomizationData");
                object look = stored;
                if (look == null || !(CoopDiagnostics.GetMember(look, "customizationPartsData") is System.Collections.ICollection parts) || parts.Count == 0)
                {
                    look = customizationType.GetMethod("CreateDefault", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
                }
                live.GetType().GetMethod("ApplyCustomization").Invoke(live, new[] { look });
            }
            catch (Exception ex)
            {
                log.LogWarning("Player profile: kept the current look; could not apply the stored one: " + Inner(ex).Message);
            }
        }

        /// <summary>
        /// Puts a returning player back where they were when they last played here. Only within the
        /// scene the copied world has loaded: another scene (an interior, a dungeon) needs the game's
        /// cross-scene teleport and its lighting preset, which a stored position does not carry, so
        /// such a player starts at the host's saved spot instead. Returns a note for the log.
        /// </summary>
        private static string ReturnToLastPosition(object live, object stored)
        {
            try
            {
                string storedScene = Convert.ToString(GetField(stored, "currentGameSceneId"));
                string liveScene = Convert.ToString(GetField(live, "currentGameSceneId"));
                if (string.IsNullOrEmpty(storedScene) || storedScene != liveScene)
                {
                    return " Position: last seen in scene '" + storedScene + "', starting in '" + liveScene + "' at the host's spot.";
                }
                Vector3 target = (Vector3)CoopDiagnostics.GetMember(GetField(stored, "position"), "Value");
                Vector3 current = (Vector3)CoopDiagnostics.GetMember(GetField(live, "position"), "Value");
                if (Vector3.Distance(target, current) < 1f)
                {
                    return string.Empty;
                }
                object controller = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerController");
                MethodInfo setPosition = controller?.GetType().GetMethod("SetPosition", new[] { typeof(Vector3), typeof(bool), typeof(bool) });
                if (setPosition == null)
                {
                    return " Position: not restored (no player controller).";
                }
                setPosition.Invoke(controller, new object[] { target, true, true });
                return " Position: returned to " + target.ToString("F1") + ".";
            }
            catch (Exception ex)
            {
                log.LogWarning("Player profile: could not return this player to their last position: " + Inner(ex).Message);
                return string.Empty;
            }
        }

        private static object TalentSystem()
        {
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            object save = mainGame == null ? null : CoopDiagnostics.GetMember(mainGame, "GameSave");
            return save == null ? null : CoopDiagnostics.GetMember(save, "talentSystemData");
        }

        /// <summary>
        /// Copies each talent branch's progress into the live branches (the game and its UI hold
        /// those objects), then lets the talent system rebuild its caches. Returns a note for the log.
        /// </summary>
        private static string RestoreTalents(object source)
        {
            object live = TalentSystem();
            if (source == null || live == null)
            {
                return string.Empty;
            }
            try
            {
                int copied = 0;
                MethodInfo branch = live.GetType().GetMethod("GetTalentBranch", new[] { typeof(string) });
                if (CoopDiagnostics.GetMember(source, "talentData") is System.Collections.IEnumerable branches)
                {
                    foreach (object stored in branches)
                    {
                        object target = branch.Invoke(live, new object[] { Convert.ToString(CoopDiagnostics.GetMember(stored, "id")) });
                        if (target == null)
                        {
                            continue;
                        }
                        foreach (string field in new[] { "curExp", "curTalentLevel", "talentExpPoints", "curTalentValue", "studiedLevelUps", "inspirationsProgression", "isTalentLevelUpActionIndicatorBlocked" })
                        {
                            FieldInfo info = FindField(target.GetType(), field);
                            if (info != null)
                            {
                                info.SetValue(target, info.GetValue(stored));
                            }
                        }
                        copied++;
                    }
                }
                live.GetType().GetMethod("PrepareForGame", Type.EmptyTypes)?.Invoke(live, null);
                return " Talents: " + copied + " branches restored.";
            }
            catch (Exception ex)
            {
                log.LogWarning("Player profile: could not restore this player's talents: " + Inner(ex).Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Swaps the contents, not the Inventory object: the game's inventory UI and the equipped
        /// tool subscribe to the object's events and would otherwise keep showing the old one.
        /// </summary>
        private static void CopyInventory(object sourcePlayer, object targetPlayer, string field)
        {
            object source = GetField(sourcePlayer, field);
            object target = GetField(targetPlayer, field);
            if (source == null || target == null)
            {
                return;
            }
            SetField(target, "inventoryItem", GetField(source, "inventoryItem"));
            MethodInfo refresh = target.GetType().GetMethod("ForceTriggerOnItemsAddEventWithoutItems", Type.EmptyTypes);
            refresh?.Invoke(target, null);
        }

        private static object CreateFreshPlayer()
        {
            Type playerType = Plugin.FindGameType("PlayerData");
            object fresh = playerType.GetMethod("CreatePlayerData", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
            playerType.GetMethod("TryApplyStartState", BindingFlags.Instance | BindingFlags.Public).Invoke(fresh, null);
            return fresh;
        }

        /// <summary>
        /// In the game's own save format since 0.36.0: the older <c>LazySerializer</c> form dropped
        /// item properties (tool wear and the like). Profiles stored in the older form still load.
        /// </summary>
        private static byte[] Serialize(object playerData)
        {
            // The player and their talents: talent experience and levels live in the shared save,
            // but are this player's own progress.
            return CoopGameSerializer.Serialize(new object[] { playerData, TalentSystem() });
        }

        private static object Deserialize(byte[] raw)
        {
            return DeserializeBundle(raw, out object _);
        }

        /// <summary>
        /// The stored player, and their talents when the profile carries them (0.36.0 on); older
        /// profiles hold the player alone, in either serializer's format.
        /// </summary>
        private static object DeserializeBundle(byte[] raw, out object talents)
        {
            talents = null;
            object result = null;
            if (CoopGameSerializer.IsGameFormat(raw))
            {
                try
                {
                    if (CoopGameSerializer.Deserialize(typeof(object[]), raw) is object[] bundle && bundle.Length >= 1)
                    {
                        result = bundle[0];
                        talents = bundle.Length > 1 ? bundle[1] : null;
                    }
                }
                catch (Exception)
                {
                    result = null;
                }
                if (result == null || result.GetType() != Plugin.FindGameType("PlayerData"))
                {
                    talents = null;
                    result = CoopGameSerializer.Deserialize(Plugin.FindGameType("PlayerData"), raw);
                }
            }
            else
            {
                MethodInfo deserialize = SerializerMethod("Deserialize", typeof(byte[]));
                result = deserialize.MakeGenericMethod(Plugin.FindGameType("PlayerData")).Invoke(null, new object[] { raw });
            }
            if (result == null || GetField(result, "inventory") == null)
            {
                throw new InvalidDataException("stored player state did not deserialize to a player");
            }
            return result;
        }

        private static MethodInfo SerializerMethod(string name, Type parameter)
        {
            Type serializer = Plugin.FindGameType("LazyBearTechnology.LazySerializer");
            foreach (MethodInfo method in serializer.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (method.Name == name && method.IsGenericMethodDefinition)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    // Serialize(T) and Deserialize(byte[]) — not the Stream overloads.
                    if (parameters.Length == 1 && (name == "Serialize" || parameters[0].ParameterType == parameter))
                    {
                        return method;
                    }
                }
            }
            throw new MissingMethodException("LazySerializer." + name);
        }

        private static byte[] Compress(byte[] raw)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress))
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
                var buffer = new byte[16384];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    if (output.Length > 64L * 1024 * 1024)
                    {
                        throw new InvalidDataException("player state too large");
                    }
                }
                return output.ToArray();
            }
        }

        private static object LocalPlayerData()
        {
            return CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerData");
        }

        private static bool InGame()
        {
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            return mainGame != null &&
                   string.Equals(Convert.ToString(CoopDiagnostics.GetMember(mainGame, "gameState")), "InGame", StringComparison.Ordinal) &&
                   LocalPlayerData() != null;
        }

        private static string LoadedSlotName()
        {
            try
            {
                object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
                object slot = mainGame == null ? null : CoopDiagnostics.GetMember(mainGame, "SaveSlotData");
                return slot == null ? string.Empty : Convert.ToString(CoopDiagnostics.GetMember(slot, "slotName")) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>A short, human-checkable line for the log and the test probe.</summary>
        internal static string Summary(object playerData)
        {
            if (playerData == null)
            {
                return "no player";
            }
            object res = GetField(playerData, "res");
            MethodInfo get = res.GetType().GetMethod("Get", new[] { typeof(string), typeof(float) });
            Func<string, string> value = id => Convert.ToString(get.Invoke(res, new object[] { id, 0f }), System.Globalization.CultureInfo.InvariantCulture);
            object inventoryItem = GetField(GetField(playerData, "inventory"), "inventoryItem");
            int stacks = inventoryItem == null ? 0 : Convert.ToInt32(CoopDiagnostics.GetMember(inventoryItem, "InventoryCount"));
            return "money=" + value("money") + " red=" + value("tech_red") + " stacks=" + stacks +
                   " hp=" + GetField(GetField(playerData, "hpComponent"), "hp");
        }

        private static object GetField(object instance, string name)
        {
            FieldInfo field = instance == null ? null : FindField(instance.GetType(), name);
            if (field == null)
            {
                throw new MissingFieldException(instance?.GetType().Name ?? "null", name);
            }
            return field.GetValue(instance);
        }

        private static void SetField(object instance, string name, object value)
        {
            FieldInfo field = FindField(instance.GetType(), name);
            if (field == null)
            {
                throw new MissingFieldException(instance.GetType().Name, name);
            }
            field.SetValue(instance, value);
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }
            }
            return null;
        }

        private static string ShortKey(string key)
        {
            return string.IsNullOrEmpty(key) || key.Length < 8 ? "player ?" : "player " + key.Substring(0, 8);
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
