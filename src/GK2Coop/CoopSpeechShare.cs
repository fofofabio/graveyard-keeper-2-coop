using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using LazyBearTechnology;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Other players' conversations, seen from nearby. A line of dialogue shows only on the machine
    /// of the player talking (Bubble.Talk); here each line also goes to the others, and a player
    /// in the same area within a few steps sees it too: an NPC's line above that NPC in their own
    /// world, the talking player's own line above that player's character. The text travels as the
    /// game's translation key, so everyone reads it in their own language. Only what is shown is
    /// shared; the conversation's effects stay with the player having it (and reach the others
    /// through the quest and world syncs as before).
    /// </summary>
    internal static class CoopSpeechShare
    {
        internal const string SpeechMessage = "GK2Coop.Speech.v1";
        private const float HearingDistance = 14f;

        private static ManualLogSource log;
        private static bool showingMirrored;
        private static int sent;
        private static int shown;
        private static int ignored;
        private static FieldInfo presetsField;

        internal static bool Enabled { get; set; } = true;

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
                harmony.Patch(AccessTools.Method(typeof(Bubble), nameof(Bubble.Talk)), prefix: new HarmonyMethod(typeof(CoopSpeechShare), nameof(TalkPrefix)));
                presetsField = AccessTools.Field(typeof(Bubble), "speechBubblePresets");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Speech share disabled: " + ex.Message);
            }
        }

        internal static string Describe()
        {
            return "speech: sent=" + sent + ", shown=" + shown + ", ignored=" + ignored;
        }

        // ---------------------------------------------------------------- the player talking

        private static void TalkPrefix(PhraseData data)
        {
            if (!Enabled || showingMirrored || CoopSceneShare.Replaying || string.IsNullOrEmpty(data.text))
            {
                return;
            }
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsListening || (netcode.IsHost && netcode.ConnectedClientsIds.Count < 2))
            {
                return;
            }
            try
            {
                string npc = string.Empty;
                if (!data.isPlayer && data.npcWgoData != null)
                {
                    npc = data.npcWgoData.UniqueId.ToString();
                }
                PlayerData player = MainGame.PlayerData;
                Vector3 where = MainGame.PlayerController != null ? ((Component)MainGame.PlayerController).transform.position : Vector3.zero;
                Send(netcode, netcode.LocalClientId, data.isPlayer, npc, data.text, player == null ? string.Empty : player.currentGameSceneId, where, null);
                sent++;
            }
            catch (Exception ex)
            {
                log.LogWarning("Speech share: could not send a line: " + ex.Message);
            }
        }

        private static void Send(NetworkManager netcode, ulong from, bool isPlayer, string npc, string text, string scene, Vector3 where, ulong? except)
        {
            using (var writer = new FastBufferWriter(1024, Allocator.Temp))
            {
                writer.WriteValueSafe(from);
                writer.WriteValueSafe(isPlayer);
                writer.WriteValueSafe(new FixedString64Bytes(npc ?? string.Empty));
                writer.WriteValueSafe(new FixedString512Bytes(text.Length > 250 ? text.Substring(0, 250) : text));
                writer.WriteValueSafe(new FixedString64Bytes(scene ?? string.Empty));
                writer.WriteValueSafe(where);
                if (netcode.IsHost)
                {
                    foreach (ulong clientId in netcode.ConnectedClientsIds)
                    {
                        if (clientId != netcode.LocalClientId && clientId != except && clientId != from)
                        {
                            netcode.CustomMessagingManager.SendNamedMessage(SpeechMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
                        }
                    }
                }
                else
                {
                    netcode.CustomMessagingManager.SendNamedMessage(SpeechMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                }
            }
        }

        // ---------------------------------------------------------------- the others

        internal static void Receive(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null)
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out ulong from);
                reader.ReadValueSafe(out bool isPlayer);
                reader.ReadValueSafe(out FixedString64Bytes npc);
                reader.ReadValueSafe(out FixedString512Bytes text);
                reader.ReadValueSafe(out FixedString64Bytes scene);
                reader.ReadValueSafe(out Vector3 where);
                if (netcode.IsHost)
                {
                    from = sender;
                    Send(netcode, from, isPlayer, npc.ToString(), text.ToString(), scene.ToString(), where, sender);
                }
                Show(from, isPlayer, npc.ToString(), text.ToString(), scene.ToString(), where);
            }
            catch (Exception ex)
            {
                log.LogWarning("Speech share: could not read a line from " + sender + ": " + ex.Message);
            }
        }

        private static void Show(ulong from, bool isPlayer, string npc, string text, string scene, Vector3 where)
        {
            // Watching a scene: its lines play here already.
            PlayerController controller = MainGame.PlayerController;
            PlayerData player = MainGame.PlayerData;
            if (CoopSceneShare.Replaying || controller == null || player == null || player.currentGameSceneId != scene ||
                Vector3.Distance(((Component)controller).transform.position, where) > HearingDistance)
            {
                ignored++;
                return;
            }
            WgoData speaker = null;
            if (!isPlayer && npc.Length > 0)
            {
                speaker = MainGame.Instance.GameSave.worldData.GetWgoData(SGuid.Parse(npc));
            }
            bool overPlayer = isPlayer || speaker == null || speaker.Definition.usePortraitInDialogues || speaker.id == "player_wisp";
            try
            {
                showingMirrored = true;
                if (!overPlayer)
                {
                    // Above the NPC, in this player's own world.
                    Bubble.Talk(new PhraseData { isPlayer = false, npcWgoData = speaker, text = text, onFinished = () => { }, speechType = SpeechBubbleType.Talk });
                }
                else
                {
                    ShowOverPlayer(from, text);
                }
                shown++;
            }
            finally
            {
                showingMirrored = false;
            }
        }

        /// <summary>A line above another player's character, following it.</summary>
        private static void ShowOverPlayer(ulong from, string text)
        {
            Transform anchor = CoopHud.BubblePointOf(from);
            if (anchor == null)
            {
                ignored++;
                return;
            }
            SpeechBubblePreset preset = null;
            if (presetsField != null && AccessTools.Field(typeof(Bubble), "instance")?.GetValue(null) is Bubble bubble)
            {
                var presets = presetsField.GetValue(bubble) as System.Collections.Generic.List<SpeechBubblePreset>;
                if (presets != null && presets.Count > 2) preset = presets[2];
            }
            Func<Vector2> follow = () => anchor == null ? Vector2.zero : (Vector2)CameraSystem.WorldToScreenPoint(anchor.position);
            UISpeechBubble.ShowMessage(speakerId: unchecked((int)(from + 7919)), localKey: text, uiPosition: follow(), bubblePreset: preset,
                voiceId: VoiceID.Feather, onDisappeared: null, onTextAnimationEnd: null, onUpdatePosition: follow, onForceHideCondition: () => anchor == null);
        }

        // ---------------------------------------------------------------- tests

        /// <summary>Tests: a line said here, as a conversation would (an NPC by object id, or "player").</summary>
        internal static string SayForTest(string who, string text)
        {
            if (who == "player")
            {
                Bubble.Talk(new PhraseData { isPlayer = true, text = text, onFinished = () => { }, speechType = SpeechBubbleType.Talk });
                return "player said " + text;
            }
            var found = MainGame.WorldData.GetWgoDataList(who);
            WgoData npc = found == null || found.Count == 0 ? null : found[0];
            if (npc == null)
            {
                return "no " + who;
            }
            Bubble.Talk(new PhraseData { isPlayer = false, npcWgoData = npc, text = text, onFinished = () => { }, speechType = SpeechBubbleType.Talk });
            return npc.id + " said " + text;
        }
    }
}
