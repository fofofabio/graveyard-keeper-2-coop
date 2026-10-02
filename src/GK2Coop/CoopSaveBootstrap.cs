using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Imports the save the host loaded while the joiner is still on the normal main menu.
    /// The game itself then opens that copy through its Continue button. No loaded GameSave,
    /// player body, scene, or menu is replaced by this class.
    ///
    /// The copy must never take the place of the joiner's own game. The game's Continue opens the
    /// newest loadable save, and the copy used to be dated "now" so that Continue would open it;
    /// the joiner's game then also saved into it (after sleeping, on quit), so after a session the
    /// next single-player Continue loaded the friend's world. Now the copy is dated 1 January
    /// 2000, Continue is pointed at it only while it is being opened
    /// (<see cref="ActiveSlotPostfix"/>), a joiner does not save into it (the host keeps their
    /// progress), and copies left from earlier sessions are moved out of the save list at start
    /// (<see cref="TidyWorldCopies"/>).
    /// </summary>
    internal static class CoopSaveBootstrap
    {
        internal const string RequestMessage = "GK2Coop.BootstrapRequest.v1";
        internal const string MetaMessage = "GK2Coop.BootstrapMeta.v1";
        internal const string ChunkMessage = "GK2Coop.BootstrapChunk.v1";
        internal const string AckMessage = "GK2Coop.BootstrapAck.v1";
        internal const string ErrorMessage = "GK2Coop.BootstrapError.v1";

        private const int ChunkSize = 1024;
        private const int MaxCompressedBytes = 32 * 1024 * 1024;
        private const int MaxRawBytes = 128 * 1024 * 1024;

        private enum Stage { Idle, Connecting, Retrying, Requesting, Receiving, Disconnecting, WaitingShutdown, Continue, Done, Failed }
        private static Stage stage;
        private static ManualLogSource log;
        private static string hostAddress;
        private static ushort hostPort;
        private static float lastActivity;
        private static float lastRequest;
        private static int connectRetries;
        // One more try when the host does not answer: a connection can come up silent (seen once over
        // Steam after a crash: both sides connected, no message either way); a fresh one gets through.
        private const int ConnectRetries = 1;
        private static byte[] receivedCompressed;
        private static int receivedRawLength;
        private static string receivedHash;
        private static string receivedInfo;
        private static int receivedTransferId;
        private static int nextReceivedChunk;
        private static string importedSlot;
        private static Outbound outbound;
        private static float receivedStartedAt;

        private sealed class Outbound
        {
            internal ulong ClientId;
            internal int TransferId;
            internal byte[] Compressed;
            internal int RawLength;
            internal string Hash;
            internal string Info;
            /// <summary>The next chunk to send.</summary>
            internal int NextChunk;
            /// <summary>Chunks the joiner has confirmed, in order.</summary>
            internal int Acked;
            internal float LastProgress;
        }

        /// <summary>
        /// Chunks in flight before the host waits for the joiner. One at a time (up to 0.64.0) made
        /// every kilobyte cost a full round trip of both games' frames: about 2 KB/s on a busy PC,
        /// minutes for a world. The joiner is unchanged: it takes chunks in order and confirms each.
        /// </summary>
        private const int ChunksInFlight = 16;

        internal static void Init(ManualLogSource source) { log = source; }

        private static readonly Regex WorldCopyName = new Regex(@"^GK2Coop_[0-9a-f]{16}(_backup_\d+)?$");
        // Slots of the mod's tests and playground (on the test copies' machine they share this
        // save folder, and Steam Cloud can bring one back into a player's list).
        private static readonly Regex TestSlotName = new Regex(@"^GK2Coop_(Test_[A-Za-z0-9_]+|Playground)(_backup_\d+)?$");
        private static string forcedSlot;
        private static int savesSkipped;

        /// <summary>
        /// Test copies only (the tests and the playground set it): the game's save folder is
        /// <c>GK2COOP_TEST_SAVE_FOLDER</c> instead of the player's own, so tests never create,
        /// load or remove a slot among the player's saves (which Steam Cloud also syncs), and two
        /// test runs in parallel do not see each other's slots.
        /// </summary>
        internal static string TestSaveFolder
        {
            get
            {
                string folder = Environment.GetEnvironmentVariable("GK2COOP_TEST_SAVE_FOLDER");
                return string.IsNullOrEmpty(folder) ? null : folder.TrimEnd('\\', '/') + "/";
            }
        }

        private static void SaveFolderPostfix(ref string __result)
        {
            string folder = TestSaveFolder;
            if (folder != null)
            {
                __result = folder;
            }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                if (TestSaveFolder != null)
                {
                    Directory.CreateDirectory(TestSaveFolder);
                    harmony.Patch(HarmonyLib.AccessTools.PropertyGetter(typeof(SaveSystem), nameof(SaveSystem.SaveFolder)),
                        postfix: new HarmonyLib.HarmonyMethod(typeof(CoopSaveBootstrap), nameof(SaveFolderPostfix)));
                    log.LogInfo("Save bootstrap: saves of this test copy live in " + TestSaveFolder);
                }
                harmony.Patch(HarmonyLib.AccessTools.Method(typeof(SaveSystem), nameof(SaveSystem.GetActiveSaveData)),
                    postfix: new HarmonyLib.HarmonyMethod(typeof(CoopSaveBootstrap), nameof(ActiveSlotPostfix)));
                MethodInfo save = typeof(SaveSystem).GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .First(m => m.Name == "Save" && m.GetParameters().Length >= 3 && m.GetParameters()[0].ParameterType == typeof(SaveSlotData));
                harmony.Patch(save, prefix: new HarmonyLib.HarmonyMethod(typeof(CoopSaveBootstrap), nameof(JoinerSavePrefix)));
            }
            catch (Exception ex)
            {
                log.LogWarning("Save bootstrap: could not keep world copies out of Continue: " + ex.Message);
            }
        }

        /// <summary>Continue opens the world copy while it is being opened, or a slot a test asked for; otherwise the game's choice.</summary>
        private static void ActiveSlotPostfix(ref SaveSlotData __result)
        {
            string wanted = stage == Stage.Continue && importedSlot != null ? importedSlot : forcedSlot;
            if (wanted == null)
            {
                return;
            }
            foreach (SaveSlotData slot in SaveSystem.SaveSlotDataList)
            {
                if (slot != null && slot.slotName == wanted)
                {
                    __result = slot;
                    return;
                }
            }
        }

        /// <summary>
        /// Nothing saves into a world copy: the host keeps the world and the joiner's progress.
        /// Also after the connection is gone (the host left first, the joiner goes back to the
        /// menu), or the copy would be the newest save again.
        /// </summary>
        private static bool JoinerSavePrefix(SaveSlotData slotData, Action callbackSuccessful)
        {
            // While joined, a joiner's game saves nowhere (the host keeps the world and their
            // progress): whatever slot is open, a joiner's own save can never be written from a
            // session. Afterwards, still never into a world copy.
            NetworkManager netcode = NetworkManager.Singleton;
            bool joined = netcode != null && netcode.IsListening && netcode.IsClient && !netcode.IsHost;
            if (slotData == null || (!joined && !WorldCopyName.IsMatch(slotData.slotName ?? string.Empty)))
            {
                return true;
            }
            if (savesSkipped++ == 0)
            {
                log.LogInfo("Save bootstrap: not saving '" + slotData.slotName + "' (" + (joined ? "joined; " : "a world copy; ") + "the host keeps the world).");
            }
            callbackSuccessful?.Invoke();
            return false;
        }

        /// <summary>Tests and the playground: Continue opens this slot (it can then carry an old date).</summary>
        internal static void ForceActiveSlotForTest(string slotName)
        {
            forcedSlot = string.IsNullOrEmpty(slotName) ? null : slotName;
        }

        /// <summary>
        /// At start, before the game lists its saves: world copies from earlier sessions leave the
        /// save list (they stay, in GK2Coop\world-copies), and so do slots the mod's tests or its
        /// playground left behind (GK2Coop\test-slots) — Steam Cloud uploads whatever is in the
        /// save folder while the game runs and restores it later, so they are moved while this game
        /// runs and Steam takes the removal along when it quits. A slot written in the last minutes
        /// may belong to a game running beside this one, and the slot this copy was told to
        /// continue is its own; both are left alone.
        /// </summary>
        internal static void TidyWorldCopies()
        {
            try
            {
                string folder = TestSaveFolder ?? Application.persistentDataPath;
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                // Test runs leave slots alone (a run on another pair of copies may be using them),
                // except the one test of this tidying.
                if (Environment.GetEnvironmentVariable("GK2COOP_TEST_HOST_PATH") != null && Environment.GetEnvironmentVariable("GK2COOP_TEST_TIDY") != "1") return;
                string copies = Path.Combine(Path.Combine(folder, "GK2Coop"), "world-copies");
                string tests = Path.Combine(Path.Combine(folder, "GK2Coop"), "test-slots");
                // A test copy (its tests or the playground put these slots there) leaves them alone.
                bool testCopy = CoopTestTools.Enabled || Environment.GetEnvironmentVariable("GK2COOP_TEST_HOST_PATH") != null ||
                                Environment.GetEnvironmentVariable("GK2COOP_TEST_CONTINUE_SLOT") != null;
                int moved = 0;
                foreach (string file in Directory.GetFiles(folder, "GK2Coop_*", SearchOption.TopDirectoryOnly))
                {
                    string extension = Path.GetExtension(file);
                    string slot = Path.GetFileNameWithoutExtension(file);
                    if (extension != ".dat" && extension != ".info") continue;
                    string keep = WorldCopyName.IsMatch(slot) ? copies : TestSlotName.IsMatch(slot) && !testCopy ? tests : null;
                    if (keep == null) continue;
                    // Copies keep their original write time; a file put here recently is new either way.
                    DateTime touched = File.GetCreationTime(file) > File.GetLastWriteTime(file) ? File.GetCreationTime(file) : File.GetLastWriteTime(file);
                    if (DateTime.Now - touched < TimeSpan.FromMinutes(5)) continue;
                    Directory.CreateDirectory(keep);
                    string target = Path.Combine(keep, Path.GetFileName(file));
                    if (File.Exists(target)) target = Path.Combine(keep, Path.GetFileNameWithoutExtension(file) + "-" + DateTime.Now.ToString("yyyyMMddHHmmss") + extension);
                    File.Move(file, target);
                    moved++;
                }
                if (moved > 0) log.LogInfo("Save bootstrap: moved " + moved + " file(s) of earlier world copies or test slots out of the save list (GK2Coop\\world-copies, GK2Coop\\test-slots).");
            }
            catch (Exception ex)
            {
                log.LogWarning("Save bootstrap: could not tidy earlier world copies: " + ex.Message);
            }
        }
        internal static bool Active => stage != Stage.Idle && stage != Stage.Done && stage != Stage.Failed;

        internal static bool HasFailed => stage == Stage.Failed;

        /// <summary>A reachable host answers within a second or two; waiting the transport's full minute only delays the bad news.</summary>
        private const float ConnectTimeoutSeconds = 20f;

        internal static void Begin(string address, ushort port)
        {
            if (Active) throw new InvalidOperationException("A co-op save transfer is already running.");
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            if (Convert.ToString(CoopDiagnostics.GetMember(mainGame, "gameState")) != "MainMenu")
                throw new InvalidOperationException("Join from the main menu before starting a game.");
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || netcode.IsListening)
                throw new InvalidOperationException("Menu networking is not ready or is already connected.");
            object native = CoopDiagnostics.GetStatic(Plugin.FindGameType("LazyNetwork"), "NetworkManager");
            MethodInfo connect = native == null ? null : native.GetType().GetMethod("ConnectToHost", new[] { typeof(string), typeof(ushort) });
            if (connect == null) throw new MissingMethodException("LazyNetwork.ConnectToHost");

            hostAddress = address;
            hostPort = port;
            importedSlot = null;
            menuWaitSince = -1f;
            closedOverMenu = false;
            receivedCompressed = null;
            receivedInfo = null;
            nextReceivedChunk = 0;
            connectRetries = 0;
            CoopSession.SuppressHello = true;
            stage = Stage.Connecting;
            lastActivity = Time.unscaledTime;
            if (!(bool)connect.Invoke(native, new object[] { address, port }))
            {
                Fail(L.F("Could not start a connection to {0}.", address + ":" + port));
                return;
            }
            CoopStatus.Set(CoopPhase.Connecting, L.F("Connecting to {0}…", address + ":" + port));
            CoopStatus.SetStep(JoinStep.Connecting);
            log.LogInfo("Started menu-stage save bootstrap from " + address + ":" + port + ".");
        }

        internal static void Tick()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (outbound != null) PumpOutbound(netcode);
            if (stage == Stage.Failed && CoopSession.SuppressHello &&
                (netcode == null || (!netcode.IsListening && !netcode.ShutdownInProgress)))
            {
                CoopSession.Reset();
                CoopSession.SuppressHello = false;
            }
            if (!Active) return;

            if (stage == Stage.Connecting && Time.unscaledTime - lastActivity > ConnectTimeoutSeconds &&
                (netcode == null || !netcode.IsConnectedClient))
            {
                if (connectRetries < ConnectRetries)
                {
                    connectRetries++;
                    log.LogInfo("Save bootstrap: no answer from " + hostAddress + ":" + hostPort + " yet; trying once more.");
                    CoopStatus.Set(CoopPhase.Connecting, L.F("No reply from the host. Trying again ({0} of {1})…", connectRetries, ConnectRetries));
                    // Often the attempt has already ended by itself; shutting down a stopped manager
                    // leaves it "shutting down" for good, so only a live one is shut down.
                    if (netcode != null && netcode.IsListening) netcode.Shutdown();
                    stage = Stage.Retrying;
                    lastActivity = Time.unscaledTime;
                    return;
                }
                Fail(CoopStatus.DescribeUnreachableHost(hostAddress, hostPort));
                return;
            }
            if (stage == Stage.Retrying)
            {
                if (netcode != null && (netcode.IsListening || (netcode.ShutdownInProgress && Time.unscaledTime - lastActivity < 5f))) return;
                object native = CoopDiagnostics.GetStatic(Plugin.FindGameType("LazyNetwork"), "NetworkManager");
                MethodInfo connect = native == null ? null : native.GetType().GetMethod("ConnectToHost", new[] { typeof(string), typeof(ushort) });
                stage = Stage.Connecting;
                lastActivity = Time.unscaledTime;
                if (connect == null || !(bool)connect.Invoke(native, new object[] { hostAddress, hostPort }))
                {
                    Fail(CoopStatus.DescribeUnreachableHost(hostAddress, hostPort));
                }
                return;
            }
            if (Time.unscaledTime - lastActivity > 60f)
            {
                Fail(L.T("The host stopped sending the world. Check the connection and try again."));
                return;
            }
            if (stage == Stage.Connecting || stage == Stage.Requesting)
            {
                if (netcode != null && netcode.IsConnectedClient && !netcode.IsHost)
                {
                    if (Time.unscaledTime - lastRequest >= 3f)
                    {
                        CoopSession.EnsureHandlers();
                        using (var writer = new FastBufferWriter(8, Allocator.Temp))
                        {
                            writer.WriteValueSafe(CoopSession.ProtocolVersion);
                            netcode.CustomMessagingManager.SendNamedMessage(RequestMessage,
                                NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
                        }
                        lastRequest = Time.unscaledTime;
                        stage = Stage.Requesting;
                    }
                }
                return;
            }
            if (stage == Stage.Disconnecting)
            {
                netcode?.Shutdown();
                stage = Stage.WaitingShutdown;
                lastActivity = Time.unscaledTime;
                return;
            }
            if (stage == Stage.WaitingShutdown)
            {
                if (netcode != null && (netcode.IsListening || netcode.ShutdownInProgress)) return;
                CoopSession.Reset();
                CoopSession.SuppressHello = false;
                stage = Stage.Continue;
            }
            if (stage == Stage.Continue)
            {
                TryContinueImportedSlot();
            }
        }

        private static void PumpOutbound(NetworkManager netcode)
        {
            Outbound transfer = outbound;
            if (netcode == null || !netcode.IsHost || !netcode.ConnectedClientsIds.Contains(transfer.ClientId))
            {
                outbound = null;
                return;
            }
            int chunks = (transfer.Compressed.Length + ChunkSize - 1) / ChunkSize;
            if (transfer.Acked >= chunks)
            {
                log.LogInfo("Finished save bootstrap " + transfer.TransferId + " to client " + transfer.ClientId + ".");
                outbound = null;
                return;
            }
            if (transfer.NextChunk - transfer.Acked >= ChunksInFlight || transfer.NextChunk >= chunks)
            {
                if (Time.unscaledTime - transfer.LastProgress < 1.5f) return;
                // Nothing confirmed for a while: send again from the first unconfirmed chunk (the
                // joiner confirms again, and ignores, what it already has).
                transfer.NextChunk = transfer.Acked;
                transfer.LastProgress = Time.unscaledTime;
            }
            while (transfer.NextChunk < chunks && transfer.NextChunk - transfer.Acked < ChunksInFlight)
            {
                int offset = transfer.NextChunk * ChunkSize;
                int length = Math.Min(ChunkSize, transfer.Compressed.Length - offset);
                using (var writer = new FastBufferWriter(32 + length, Allocator.Temp))
                {
                    writer.WriteValueSafe(transfer.TransferId);
                    writer.WriteValueSafe(transfer.NextChunk);
                    writer.WriteValueSafe(length);
                    writer.WriteBytesSafe(transfer.Compressed, length, offset);
                    netcode.CustomMessagingManager.SendNamedMessage(ChunkMessage, transfer.ClientId,
                        writer, NetworkDelivery.ReliableSequenced);
                }
                transfer.NextChunk++;
            }
        }

        internal static void ReceiveRequest(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsHost || !netcode.ConnectedClientsIds.Contains(sender)) return;
            try
            {
                reader.ReadValueSafe(out int protocol);
                // The joiner shows this in its own language with both versions (Explain); older joiners show it as it is.
                if (protocol != CoopSession.ProtocolVersion) throw new InvalidOperationException("Both players need the same co-op mod version. The host has " + Plugin.Version + ".");
                if (outbound != null && outbound.ClientId == sender)
                {
                    // The joiner starts receiving from the first chunk again.
                    outbound.NextChunk = 0;
                    outbound.Acked = 0;
                    outbound.LastProgress = Time.unscaledTime;
                    SendMeta(outbound);
                    return;
                }
                if (outbound != null) throw new InvalidOperationException("The host is already sending a save to another player.");
                object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
                object slot = CoopDiagnostics.GetMember(mainGame, "SaveSlotData");
                string slotName = Convert.ToString(CoopDiagnostics.GetMember(slot, "slotName"));
                if (string.IsNullOrEmpty(slotName) || !Regex.IsMatch(slotName, @"^[A-Za-z0-9_-]{1,80}$"))
                    throw new InvalidOperationException("The host has no loaded save slot. Save the game first, then retry.");
                string saveFolder = Convert.ToString(CoopDiagnostics.GetStatic(Plugin.FindGameType("SaveSystem"), "SaveFolder"));
                string datPath = Path.Combine(saveFolder, slotName + ".dat");
                string infoPath = Path.Combine(saveFolder, slotName + ".info");
                if (!File.Exists(datPath) || !File.Exists(infoPath))
                    throw new FileNotFoundException("The host's loaded save files are missing. Save the game and retry.");
                // The world as it is now, not as it was at the host's last save: gardens that grew,
                // repairs, learned techs and everything else since then come along.
                byte[] raw = SerializeLiveWorld(mainGame);
                bool live = raw != null;
                if (raw == null) raw = File.ReadAllBytes(datPath);
                if (raw.Length == 0 || raw.Length > MaxRawBytes) throw new InvalidDataException("The host save is too large to transfer.");
                string info = File.ReadAllText(infoPath);
                if (Encoding.UTF8.GetByteCount(info) > 480) throw new InvalidDataException("The host save metadata is too large to transfer.");
                byte[] compressed = Compress(raw);
                if (compressed.Length == 0 || compressed.Length > MaxCompressedBytes) throw new InvalidDataException("The compressed host save is too large.");
                outbound = new Outbound
                {
                    ClientId = sender, TransferId = Environment.TickCount, Compressed = compressed,
                    RawLength = raw.Length, Hash = Hash(raw), Info = info
                };
                SendMeta(outbound);
                log.LogInfo("Serving " + (live ? "the live world of" : "the saved file of") + " '" + slotName + "' to client " + sender + ": " + raw.Length +
                    " bytes, compressed to " + compressed.Length + " bytes.");
            }
            catch (Exception ex)
            {
                string reason = ex.GetBaseException().Message;
                log.LogWarning("Could not start save bootstrap for client " + sender + ": " + reason);
                using (var writer = new FastBufferWriter(512, Allocator.Temp))
                {
                    writer.WriteValueSafe(new FixedString512Bytes(reason.Length > 450 ? reason.Substring(0, 450) : reason));
                    netcode.CustomMessagingManager.SendNamedMessage(ErrorMessage, sender, writer, NetworkDelivery.Reliable);
                }
            }
        }

        /// <summary>
        /// The loaded world serialized exactly as the game's own save writes it
        /// (<c>OnBeforeSerialize</c>, then the save system's Odin serializer), in memory — no file
        /// is written and the host's slot is untouched. Null if the game's save path cannot be
        /// found, in which case the last saved file is sent instead.
        /// </summary>
        private static byte[] SerializeLiveWorld(object mainGame)
        {
            try
            {
                object gameSave = CoopDiagnostics.GetMember(mainGame, "GameSave");
                Type saveSystem = Plugin.FindGameType("SaveSystem");
                PropertyInfo serializerProperty = saveSystem?.GetProperty("OdinBinaryFileSerializer", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                object serializer = serializerProperty?.GetValue(null, null);
                if (gameSave == null || serializer == null)
                {
                    return null;
                }
                MethodInfo serialize = null;
                foreach (MethodInfo method in serializer.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (method.Name == "Serialize" && method.IsGenericMethodDefinition && method.GetParameters().Length == 1)
                    {
                        serialize = method.MakeGenericMethod(gameSave.GetType());
                    }
                }
                if (serialize == null)
                {
                    return null;
                }
                // Written as a single-player save, like the file: the live session's network player
                // records (the joiner is already connected while it copies) would otherwise load on
                // the joiner as extra players and confuse which one is its own. Swapped out for the
                // one synchronous call on the main thread, then put straight back.
                FieldInfo hostField = gameSave.GetType().GetField("hostPlayer");
                FieldInfo clientsField = gameSave.GetType().GetField("clientPlayers");
                object host = hostField?.GetValue(gameSave);
                object clients = clientsField?.GetValue(gameSave);
                byte[] bytes;
                try
                {
                    hostField?.SetValue(gameSave, null);
                    clientsField?.SetValue(gameSave, Activator.CreateInstance(clientsField.FieldType));
                    gameSave.GetType().GetMethod("OnBeforeSerialize", Type.EmptyTypes)?.Invoke(gameSave, null);
                    bytes = (byte[])serialize.Invoke(serializer, new[] { gameSave });
                }
                finally
                {
                    hostField?.SetValue(gameSave, host);
                    clientsField?.SetValue(gameSave, clients);
                }
                return bytes != null && bytes.Length > 0 ? bytes : null;
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not serialize the live world; sending the last saved file instead: " + ex.GetBaseException().Message);
                return null;
            }
        }

        private static void SendMeta(Outbound transfer)
        {
            using (var writer = new FastBufferWriter(768, Allocator.Temp))
            {
                writer.WriteValueSafe(transfer.TransferId);
                writer.WriteValueSafe(transfer.Compressed.Length);
                writer.WriteValueSafe(transfer.RawLength);
                writer.WriteValueSafe(new FixedString128Bytes(transfer.Hash));
                writer.WriteValueSafe(new FixedString512Bytes(transfer.Info));
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MetaMessage,
                    transfer.ClientId, writer, NetworkDelivery.ReliableSequenced);
            }
        }

        internal static void ReceiveMeta(ulong sender, FastBufferReader reader)
        {
            if (sender != NetworkManager.ServerClientId || (stage != Stage.Requesting && stage != Stage.Receiving)) return;
            try
            {
                reader.ReadValueSafe(out int transferId);
                reader.ReadValueSafe(out int compressedLength);
                reader.ReadValueSafe(out int rawLength);
                reader.ReadValueSafe(out FixedString128Bytes hash);
                reader.ReadValueSafe(out FixedString512Bytes info);
                if (compressedLength < 1 || compressedLength > MaxCompressedBytes || rawLength < 1 || rawLength > MaxRawBytes)
                    throw new InvalidDataException("The host sent an invalid save size.");
                if (stage == Stage.Receiving && transferId == receivedTransferId) return;
                receivedTransferId = transferId;
                receivedCompressed = new byte[compressedLength];
                receivedRawLength = rawLength;
                receivedHash = hash.ToString();
                receivedInfo = info.ToString();
                nextReceivedChunk = 0;
                receivedStartedAt = Time.unscaledTime;
                lastActivity = Time.unscaledTime;
                stage = Stage.Receiving;
                CoopStatus.Set(CoopPhase.Connecting, L.F("Copying the host's world ({0}%)…", 0));
                CoopStatus.SetStep(JoinStep.Copying, 0f);
                log.LogInfo("Receiving host save bootstrap " + transferId + ": " + compressedLength + " compressed bytes.");
            }
            catch (Exception ex) { Fail(L.F("Could not read the host's world: {0}", ex.GetBaseException().Message)); }
        }

        internal static void ReceiveChunk(ulong sender, FastBufferReader reader)
        {
            if (sender != NetworkManager.ServerClientId || stage != Stage.Receiving) return;
            try
            {
                reader.ReadValueSafe(out int transferId);
                reader.ReadValueSafe(out int index);
                reader.ReadValueSafe(out int length);
                if (transferId != receivedTransferId || index < 0 || length < 1 || length > ChunkSize) return;
                if (index > nextReceivedChunk) return;
                int offset = index * ChunkSize;
                if (offset < 0 || offset + length > receivedCompressed.Length) throw new InvalidDataException("Save chunk is out of range.");
                if (index == nextReceivedChunk)
                {
                    if (length != Math.Min(ChunkSize, receivedCompressed.Length - offset))
                        throw new InvalidDataException("Save chunk has the wrong length.");
                    reader.ReadBytesSafe(ref receivedCompressed, length, offset);
                    nextReceivedChunk++;
                    lastActivity = Time.unscaledTime;
                    if (nextReceivedChunk % 100 == 0)
                        CoopStatus.Set(CoopPhase.Connecting, L.F("Copying the host's world ({0}%)…",
                            (offset + length) * 100 / receivedCompressed.Length));
                    if (nextReceivedChunk % 20 == 0)
                        CoopStatus.SetStep(JoinStep.Copying, (float)(offset + length) / receivedCompressed.Length);
                }
                using (var writer = new FastBufferWriter(16, Allocator.Temp))
                {
                    writer.WriteValueSafe(transferId);
                    writer.WriteValueSafe(index);
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(AckMessage,
                        NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                }
                if (nextReceivedChunk * ChunkSize >= receivedCompressed.Length)
                {
                    byte[] raw = Decompress(receivedCompressed, receivedRawLength);
                    if (!string.Equals(Hash(raw), receivedHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The transferred save failed its integrity check.");
                    importedSlot = WriteIsolatedSlot(raw, receivedInfo);
                    float took = Mathf.Max(0.1f, Time.unscaledTime - receivedStartedAt);
                    log.LogInfo("Verified and imported host save into isolated slot '" + importedSlot + "' (" + receivedCompressed.Length / 1024 + " KB in " + took.ToString("0.0") + " s, " + (receivedCompressed.Length / 1024f / took).ToString("0") + " KB/s).");
                    CoopStatus.Set(CoopPhase.Connecting, L.T("World copied. Loading it…"));
                    CoopStatus.SetStep(JoinStep.Loading, 1f);
                    stage = Stage.Disconnecting;
                }
            }
            catch (Exception ex) { Fail(L.F("Could not import the host's world: {0}", ex.GetBaseException().Message)); }
        }

        internal static void ReceiveAck(ulong sender, FastBufferReader reader)
        {
            Outbound transfer = outbound;
            if (transfer == null || sender != transfer.ClientId) return;
            reader.ReadValueSafe(out int transferId);
            reader.ReadValueSafe(out int index);
            if (transferId == transfer.TransferId && index == transfer.Acked)
            {
                transfer.Acked++;
                transfer.LastProgress = Time.unscaledTime;
                if (transfer.NextChunk < transfer.Acked) transfer.NextChunk = transfer.Acked;
            }
        }

        internal static void ReceiveError(ulong sender, FastBufferReader reader)
        {
            if (sender != NetworkManager.ServerClientId || !Active) return;
            reader.ReadValueSafe(out FixedString512Bytes reason);
            Fail(Explain(reason.ToString()));
        }

        /// <summary>
        /// A host's refusal in the joiner's own language. The host sends English (it cannot know the
        /// joiner's language, and older hosts send the same sentences); the known ones are
        /// recognised here, a version mismatch with both versions named.
        /// </summary>
        internal static string Explain(string reason)
        {
            Match mismatch = Regex.Match(reason ?? string.Empty, @"^Both players need the same co-op mod version\.(?: The host has (\S+)\.)?$");
            if (mismatch.Success)
            {
                return mismatch.Groups[1].Success
                    ? L.F("The host has co-op mod {0}, you have {1}. Both players need the same version.", mismatch.Groups[1].Value, Plugin.Version)
                    : L.F("The host has a different co-op mod version; you have {0}. Both players need the same version.", Plugin.Version);
            }
            if (reason == "The host has no loaded save slot. Save the game first, then retry." ||
                reason == "The host is already sending a save to another player.")
            {
                return L.T(reason);
            }
            return reason;
        }

        private static string WriteIsolatedSlot(byte[] raw, string info)
        {
            string folder = Convert.ToString(CoopDiagnostics.GetStatic(Plugin.FindGameType("SaveSystem"), "SaveFolder"));
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) throw new DirectoryNotFoundException("Game save folder not found.");
            string name = "GK2Coop_" + Guid.NewGuid().ToString("N").Substring(0, 16);
            string dat = Path.Combine(folder, name + ".dat");
            string meta = Path.Combine(folder, name + ".info");
            string cultureName = Regex.Match(info, "\"serializedCulture\":\"([^\"]*)\"").Groups[1].Value;
            System.Globalization.CultureInfo culture;
            try { culture = string.IsNullOrEmpty(cultureName) ? System.Globalization.CultureInfo.CurrentCulture : System.Globalization.CultureInfo.GetCultureInfo(cultureName); }
            catch (System.Globalization.CultureNotFoundException) { culture = System.Globalization.CultureInfo.CurrentCulture; }
            // Old on purpose: never the newest save, so never what the joiner's own Continue opens.
            string date = new DateTime(2000, 1, 1, 0, 0, 0).ToString("G", culture);
            if (!Regex.IsMatch(info, "\"saveDateTime\":\"[^\"]*\"")) throw new InvalidDataException("Host save metadata has no date.");
            info = Regex.Replace(info, "\"saveDateTime\":\"[^\"]*\"", "\"saveDateTime\":\"" + date + "\"");
            string datPart = dat + ".part";
            string metaPart = meta + ".part";
            try
            {
                if (File.Exists(dat) || File.Exists(meta)) throw new IOException("The isolated save slot already exists.");
                File.WriteAllBytes(datPart, raw);
                File.WriteAllText(metaPart, info, new UTF8Encoding(false));
                File.Move(datPart, dat);
                File.Move(metaPart, meta);
                return name;
            }
            catch
            {
                if (File.Exists(datPart)) File.Delete(datPart);
                if (File.Exists(metaPart)) File.Delete(metaPart);
                if (File.Exists(dat)) File.Delete(dat);
                if (File.Exists(meta)) File.Delete(meta);
                throw;
            }
        }

        private static void TryContinueImportedSlot()
        {
            try
            {
                Type saveSystem = Plugin.FindGameType("SaveSystem");
                object active = saveSystem.GetMethod("GetActiveSaveData", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
                if (Convert.ToString(CoopDiagnostics.GetMember(active, "slotName")) != importedSlot)
                    throw new InvalidOperationException("The imported save is not the active slot. Select it from Load Game instead.");
                Type menuType = Plugin.FindGameType("UIMainMenuWindow");
                Component menu = Resources.FindObjectsOfTypeAll(menuType).OfType<Component>()
                    .FirstOrDefault(window => window.gameObject.activeInHierarchy);
                if (menu == null)
                {
                    // The player opened something over the main menu while the world was copying
                    // (Credits, Settings, Load): the join waited for the menu for ever. Those windows
                    // are closed; if the menu still does not show, it is used from where it is.
                    float now = Time.unscaledTime;
                    if (menuWaitSince < 0f) menuWaitSince = now;
                    if (now - menuWaitSince < 1f) return;
                    if (!closedOverMenu)
                    {
                        closedOverMenu = true;
                        log.LogInfo("Join: the main menu was covered (" + CloseWindowsOver(menuType) + "); closed to load the host's world.");
                        return;
                    }
                    if (now - menuWaitSince < 4f) return;
                    menu = Resources.FindObjectsOfTypeAll(menuType).OfType<Component>().FirstOrDefault(window => window != null && window.gameObject.scene.IsValid());
                    if (menu == null) return;
                    log.LogInfo("Join: the main menu stayed hidden; loading the host's world from it anyway.");
                }
                CoopSession.SuppressHello = false;
                // Still in Stage.Continue while the button runs: it asks for the active save first
                // thing, and only then does ActiveSlotPostfix hand it the copy. Done afterwards;
                // done first, Continue opened the newest save — the joiner's own.
                menuType.GetMethod("OnContinueButtonClicked", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Invoke(menu, null);
                stage = Stage.Done;
                log.LogInfo("Invoked the game's Continue button for imported co-op slot '" + importedSlot + "'.");
            }
            catch (Exception ex) { Fail(L.F("Could not load the host's world: {0}", ex.GetBaseException().Message)); }
        }

        private static float menuWaitSince = -1f;
        private static bool closedOverMenu;

        /// <summary>Closes the game's shown windows other than the main menu; returns their names.</summary>
        private static string CloseWindowsOver(Type menuType)
        {
            var closed = new System.Collections.Generic.List<string>();
            foreach (MonoBehaviour window in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                Type type = window.GetType();
                if (type == menuType || type.Namespace == typeof(CoopSaveBootstrap).Namespace) continue;
                Type lazy = type;
                while (lazy != null && !(lazy.IsGenericType && lazy.GetGenericTypeDefinition().Name == "LazyWindow`1")) lazy = lazy.BaseType;
                if (lazy == null || !(CoopDiagnostics.GetMember(window, "IsShown") is bool shown) || !shown) continue;
                try
                {
                    MethodInfo close = lazy.GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(m => m.Name == "Close").OrderBy(m => m.GetParameters().Length).First();
                    close.Invoke(window, close.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)).ToArray());
                    closed.Add(type.Name);
                }
                catch (Exception ex)
                {
                    closed.Add(type.Name + " (could not close: " + ex.GetBaseException().Message + ")");
                }
            }
            return closed.Count == 0 ? "nothing to close" : string.Join(", ", closed.ToArray());
        }

        private static void Fail(string message)
        {
            stage = Stage.Failed;
            // Keep the normal handshake suppressed until the bootstrap connection is fully
            // shut down; otherwise a failed transfer can send Hello to a menu-only client.
            CoopSession.SuppressHello = true;
            CoopStatus.Set(CoopPhase.Failed, message);
            // The status window says it (with OK); a notice as well sat half under the window.
            // Only the plain look, which has no such window, needs the notice.
            if (!GameUi.Ready) CoopStatus.Announce(message, true, 20f);
            log.LogWarning("Save bootstrap failed: " + message);
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost)
                NetworkManager.Singleton.Shutdown();
        }

        private static byte[] Compress(byte[] raw)
        {
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, true)) deflate.Write(raw, 0, raw.Length);
                return output.ToArray();
            }
        }

        private static byte[] Decompress(byte[] compressed, int expectedLength)
        {
            // One allocation of the declared size. A growing MemoryStream doubles its buffer and then
            // copies it again in ToArray, so a 12.8 MB save briefly needed several large blocks at
            // once; Mono's heap ran out of contiguous space for that inside a running game.
            byte[] raw = new byte[expectedLength];
            using (var input = new MemoryStream(compressed))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            {
                int total = 0;
                int read;
                while (total < expectedLength && (read = deflate.Read(raw, total, expectedLength - total)) > 0)
                {
                    total += read;
                }
                if (total != expectedLength) throw new InvalidDataException("Host save length mismatch.");
                if (deflate.Read(new byte[1], 0, 1) != 0) throw new InvalidDataException("Host save expanded beyond its declared size.");
                return raw;
            }
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
    }
}
