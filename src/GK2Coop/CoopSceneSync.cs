using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Players in different scenes do not see each other.
    ///
    /// The world is several game scenes — the open world, the prison, sewers, a temple — and they
    /// all use the same coordinates (every scene's offset is zero). Without this, a player who went
    /// into the prison was drawn to everyone outside standing at the prison's coordinates in the
    /// open world. Each player tells the others which scene they are in
    /// (<c>PlayerData.currentGameSceneId</c>); a body whose player is in another scene has its
    /// renderers forced off, and its name tag is left out, until they are in the same scene
    /// again. Only rendering changes: the body keeps receiving its movement.
    /// </summary>
    internal static class CoopSceneSync
    {
        internal const string SceneMessage = "GK2Coop.Scene.v1";

        private static ManualLogSource log;
        private static readonly Dictionary<ulong, string> scenes = new Dictionary<ulong, string>();
        private static readonly Dictionary<int, List<Renderer>> hiddenRenderers = new Dictionary<int, List<Renderer>>();
        private static readonly Dictionary<int, string> decisions = new Dictionary<int, string>();
        private static string announced;
        private static int announcedPeers = -1;

        internal static bool Enabled { get; set; } = true;

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static string Describe()
        {
            var parts = new List<string>();
            foreach (KeyValuePair<ulong, string> entry in scenes)
            {
                parts.Add(entry.Key + "@" + entry.Value);
            }
            return "scene sync: local=" + (LocalScene() ?? "?") + ", players=" + string.Join(",", parts.ToArray()) + ", hidden bodies=" + hiddenRenderers.Count;
        }

        /// <summary>For name tags: whether that player is in a scene other than ours.</summary>
        internal static bool IsElsewhere(ulong clientId)
        {
            string local = LocalScene();
            return Enabled && local != null && scenes.TryGetValue(clientId, out string remote) &&
                   !string.IsNullOrEmpty(remote) && remote != local;
        }

        /// <summary>For the status panel: the scene another player is in, if known.</summary>
        internal static string SceneOf(ulong clientId)
        {
            return scenes.TryGetValue(clientId, out string scene) ? scene : null;
        }

        /// <summary>Driven from the plugin's one-second poll.</summary>
        internal static void Tick()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening)
            {
                if (scenes.Count > 0 || hiddenRenderers.Count > 0)
                {
                    scenes.Clear();
                    ShowAll();
                }
                announced = null;
                announcedPeers = -1;
                return;
            }
            if (!netcode.IsHost && !CoopSession.Welcomed)
            {
                // Not yet accepted: wait, but keep what the host already told us. The host announces
                // its scene as soon as a joiner connects, which can be before the welcome; clearing
                // here lost it, and the joiner then never hid the host.
                return;
            }
            try
            {
                string local = LocalScene();
                int peers = netcode.IsHost ? netcode.ConnectedClientsIds.Count : 1;
                if (local != null && (local != announced || peers != announcedPeers))
                {
                    announced = local;
                    scenes[netcode.LocalClientId] = local;
                    Send(netcode, netcode.LocalClientId, local, null);
                    if (netcode.IsHost && peers != announcedPeers)
                    {
                        // A newcomer needs everyone else's scene too.
                        foreach (KeyValuePair<ulong, string> entry in new List<KeyValuePair<ulong, string>>(scenes))
                        {
                            if (entry.Key != netcode.LocalClientId)
                            {
                                Send(netcode, entry.Key, entry.Value, null);
                            }
                        }
                    }
                    announcedPeers = peers;
                }
                UpdateBodies(local);
            }
            catch (Exception ex)
            {
                log.LogWarning("Scene sync: " + ex.Message);
            }
        }

        internal static void Forget(ulong clientId)
        {
            scenes.Remove(clientId);
        }

        private static void Send(NetworkManager netcode, ulong origin, string scene, ulong? except)
        {
            using (var writer = new FastBufferWriter(160, Allocator.Temp))
            {
                writer.WriteValueSafe(origin);
                writer.WriteValueSafe(new FixedString128Bytes(scene));
                if (netcode.IsHost)
                {
                    foreach (ulong clientId in netcode.ConnectedClientsIds)
                    {
                        if (clientId != netcode.LocalClientId && clientId != origin && clientId != except)
                        {
                            netcode.CustomMessagingManager.SendNamedMessage(SceneMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
                        }
                    }
                }
                else
                {
                    netcode.CustomMessagingManager.SendNamedMessage(SceneMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                }
            }
        }

        private static void SendTo(NetworkManager netcode, ulong target, ulong origin, string scene)
        {
            using (var writer = new FastBufferWriter(160, Allocator.Temp))
            {
                writer.WriteValueSafe(origin);
                writer.WriteValueSafe(new FixedString128Bytes(scene));
                netcode.CustomMessagingManager.SendNamedMessage(SceneMessage, target, writer, NetworkDelivery.ReliableSequenced);
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
                reader.ReadValueSafe(out ulong origin);
                reader.ReadValueSafe(out FixedString128Bytes sceneText);
                if (netcode.IsHost)
                {
                    // A joiner speaks only for itself.
                    origin = sender;
                }
                string scene = sceneText.ToString();
                if (!scenes.TryGetValue(origin, out string previous) || previous != scene)
                {
                    log.LogInfo("Scene sync: " + CoopSession.NameFor(origin) + " is in " + scene + ".");
                }
                scenes[origin] = scene;
                if (netcode.IsHost)
                {
                    Send(netcode, origin, scene, sender);
                    // And the announcer learns where everyone else is, whatever it may have missed.
                    foreach (KeyValuePair<ulong, string> entry in new List<KeyValuePair<ulong, string>>(scenes))
                    {
                        if (entry.Key != sender)
                        {
                            SendTo(netcode, sender, entry.Key, entry.Value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Scene sync: could not read a scene from " + sender + ": " + ex.Message);
            }
        }

        private static void UpdateBodies(string local)
        {
            object localData = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerData");
            Type bodyType = Plugin.FindGameType("PlayerPhysicalBody");
            var seen = new HashSet<int>();
            foreach (Component body in Resources.FindObjectsOfTypeAll(bodyType))
            {
                if (body == null || !body.gameObject.scene.IsValid())
                {
                    continue;
                }
                object data = CoopDiagnostics.GetMember(body, "playerData");
                if (data == null || ReferenceEquals(data, localData) || !CoopHud.TryResolveClientId(data, out ulong clientId))
                {
                    continue;
                }
                int key = body.GetInstanceID();
                seen.Add(key);
                scenes.TryGetValue(clientId, out string remote);
                bool hide = local != null && !string.IsNullOrEmpty(remote) && remote != local;
                string decision = clientId + ":" + (remote ?? "?") + "/" + (local ?? "?") + "=" + hide;
                if (!decisions.TryGetValue(key, out string previousDecision) || previousDecision != decision)
                {
                    decisions[key] = decision;
                    log.LogInfo("Scene sync: body of " + CoopSession.NameFor(clientId) + " — they are in " + (remote ?? "an unknown scene") + ", we are in " + (local ?? "?") + (hide ? "; hiding." : "; shown."));
                }
                if (hide)
                {
                    bool first = !hiddenRenderers.TryGetValue(key, out List<Renderer> switchedOff);
                    if (first)
                    {
                        switchedOff = new List<Renderer>();
                        hiddenRenderers[key] = switchedOff;
                        log.LogInfo("Scene sync: hid " + CoopSession.NameFor(clientId) + " (in " + remote + ", we are in " + local + ").");
                    }
                    // forceRenderingOff rather than enabled: the game switches parts' enabled flag
                    // back on as it animates them. Every tick, for parts added by a look change.
                    foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!renderer.forceRenderingOff)
                        {
                            renderer.forceRenderingOff = true;
                            switchedOff.Add(renderer);
                        }
                    }
                }
                else if (!hide && hiddenRenderers.TryGetValue(key, out List<Renderer> renderers))
                {
                    Restore(renderers);
                    hiddenRenderers.Remove(key);
                    log.LogInfo("Scene sync: showing " + CoopSession.NameFor(clientId) + " again.");
                }
            }
            // Bodies that went away take their bookkeeping with them.
            foreach (int key in new List<int>(hiddenRenderers.Keys))
            {
                if (!seen.Contains(key))
                {
                    hiddenRenderers.Remove(key);
                }
            }
        }

        private static void ShowAll()
        {
            foreach (List<Renderer> renderers in hiddenRenderers.Values)
            {
                Restore(renderers);
            }
            hiddenRenderers.Clear();
        }

        private static void Restore(List<Renderer> renderers)
        {
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                {
                    renderer.forceRenderingOff = false;
                }
            }
        }

        private static string LocalScene()
        {
            object data = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerData");
            string scene = data == null ? null : Convert.ToString(CoopDiagnostics.GetMember(data, "currentGameSceneId"));
            return string.IsNullOrEmpty(scene) ? null : scene;
        }
    }
}
