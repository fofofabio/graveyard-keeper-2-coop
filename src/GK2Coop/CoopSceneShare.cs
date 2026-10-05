using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using FlowCanvas;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Watching a story scene together.
    ///
    /// A scene plays only on the machine of the player who triggers it. When one starts there —
    /// a script's event fired, then the cinematic bars or the player's control taken — the others
    /// are asked whether to watch. Whoever accepts is teleported to the triggering player and the
    /// same scene plays on their machine: its camera, dialogue, animations and the story NPCs it
    /// moves or spawns (which the mod does not sync, so this brings their world to the same state),
    /// while every step the mod already syncs from the triggering player — quests, rewards, world
    /// values, weather, time, buildings, fights, zombies, further scripts — is skipped, so nothing
    /// happens twice. Afterwards the viewer is taken back to where they were.
    /// </summary>
    internal static class CoopSceneShare
    {
        // Not "Scene": that name belongs to CoopSceneSync (which area each player is in).
        internal const string ShareMessage = "GK2Coop.SceneShare.v1";

        /// <summary>
        /// Steps a replay skips: their effect already reaches every player from the one who
        /// triggered the scene, or they would start more of the story on the viewer's machine.
        /// Everything else in a scene plays, including NPCs moved, spawned or removed.
        /// </summary>
        private static readonly HashSet<string> Skipped = new HashSet<string>(StringComparer.Ordinal)
        {
            // Quests, rewards and world values (quest, drop, world-value and profile sync)
            "Flow_LazyExpression", "Flow_QuestCustomTrigger", "Flow_AddGameRes", "Flow_SetGameResStr", "Flow_SmartRes",
            "Flow_AddItem", "Flow_DropItem", "Flow_RemoveItemsFromWgoData", "Flow_RemoveDrop", "Flow_SetDropPosition",
            "Flow_DropZombie", "Flow_DropPrayFaith", "Flow_PlayerDropOverhead", "Flow_UnlockePhrase", "Flow_AddPhraseToBlackList",
            // More of the story: other scripts and global triggers
            "Flow_FireEventTrigger", "Flow_FireEventOnGlobalScript", "Flow_RunGlobalScript",
            // Weather and time (host-driven), buildings, bodies, zombies, conveyors, fights, sleep, sermons
            "Flow_SetWeatherState", "Flow_TimeController", "Flow_SetTimeOfDayPreset", "Flow_CreateTownBuilding",
            "Flow_GenerateBody", "Flow_Body", "Flow_ShowNewBodyNotification", "Flow_TryToAddCorpseToPallet",
            "Flow_CreateZombieWgoData", "Flow_AttachZombieToConveyorWorkbench", "Flow_UnAttachZombieFromConveyorWorkbench",
            "Flow_StartCraftAtConveyorWorkbench", "Flow_TryAddToConveyorSystem", "Flow_SetConveyorSystemPauseState",
            "Flow_DisableWorkshopConveyors", "Flow_ToggleNearestWgoWorkEvent", "Flow_AddWgoToSimGroup",
            "Flow_SetFightingState", "Flow_EnablePreFight", "Flow_SetWasInPreFightState", "Flow_SetCapturePointTeam",
            "Flow_CreateAgentAIInstance", "Flow_DestroyAgentAIInstance", "Flow_SetAgentAI",
            "Flow_GoToSleep", "Flow_FinishSermon", "Flow_EndGame",
            // Windows that belong to the player who triggered it
            "Flow_ShowTutorialWindow", "Flow_OpenDemoWindow", "Flow_SetCreditsWindowBackButtonState", "Flow_TryInteract",
        };

        private const byte KindOffer = 0;
        private const byte KindEnd = 1;
        private const byte KindAnswer = 2;

        private const float PromptSeconds = 10f;
        private const float StartWindow = 3f;

        private static ManualLogSource log;
        private static string pendingScript;
        private static string pendingEvent;
        private static float pendingAt = -10f;
        private static bool announcedPending;
        private static readonly HashSet<GameObject> replays = new HashSet<GameObject>();
        private static int skippedSteps;
        private static int replaysStarted;
        private static int announced;
        private static int replaysFinished;
        private static int returned;

        // The last steps of the scene being shared, on either side (tests, and the log when one stalls)
        private static string trailScript;
        private static float trailUntil;
        private static readonly Queue<string> trail = new Queue<string>();

        // The offer on screen
        private static Offer offer;
        private static float offerUntil;
        private static bool acceptClicked;
        private static bool declineClicked;

        // Where a viewer was, to take them back
        private static string returnScene;
        private static Vector3 returnPosition;
        private static bool returnPending;

        // A viewer on the way to the scene
        private static Offer arriving;
        private static float arrivingSince;

        // The scene being watched: whose, since when, and when to stop it (the scene has ended
        // for the player who triggered it)
        private static Offer watching;
        private static float replayStartedAt;
        private static float stopAt = -1f;
        private static bool stopClicked;
        private static int replaysStopped;
        private static bool previewWatching;

        // Steps of a stopped replay still arriving from tweens and timers: dropped
        private static readonly HashSet<object> stoppedGraphs = new HashSet<object>();
        private static float stoppedUntil;

        // The scene this player triggered and offered, to tell the others when it ends
        private static string sharedScript;
        private static float sharedSince;
        private static GameObject sharedObject;
        private static bool sharedObjectFound;
        private static float sharedFreeSince = -1f;

        // Answers: the triggering player's choices, heard before or after the replay reaches them
        private static readonly Dictionary<string, string> answersHeard = new Dictionary<string, string>();
        private static readonly Dictionary<string, HeldAnswer> answersHeld = new Dictionary<string, HeldAnswer>();
        private static int answersSent;
        private static int answersFollowed;

        private sealed class HeldAnswer
        {
            internal Flow_MultiAnswer Node;
            internal Flow Flow;
        }

        // Host: who is showing which scene, to end it for the others if they leave
        private static readonly Dictionary<ulong, string> sharers = new Dictionary<ulong, string>();

        private sealed class Offer
        {
            internal ulong From;
            internal string Name;
            internal string Script;
            internal string Event;
            internal string SceneId;
            internal Vector3 Position;

            // A sermon: which one and whether it went well ("id|1"), for the viewer's copy to show the same
            internal string Sermon = string.Empty;

            // The triggering player's answers so far, for a viewer who starts watching later
            internal readonly Dictionary<string, string> Answers = new Dictionary<string, string>();
        }

        internal static bool Enabled { get; set; } = true;

        internal static bool Replaying => replays.Count > 0;

        internal static string Describe()
        {
            return "scene share: announced=" + announced + ", replays=" + replaysStarted + ", finished=" + replaysFinished + ", stopped=" + replaysStopped + ", returned=" + returned + ", late joins=" + lateJoins + ", running elsewhere=" + running.Count + ", skipped steps=" + skippedSteps + ", answers sent=" + answersSent + ", answers followed=" + answersFollowed +
                   (offer != null ? ", offer from " + offer.Name + " (" + offer.Script + ")" : string.Empty) + (Replaying ? ", replaying" : string.Empty) +
                   (trail.Count > 0 ? ", last steps " + string.Join(" > ", trail.ToArray()) : string.Empty);
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
                harmony.Patch(AccessTools.Method(typeof(GlobalScriptsManager), nameof(GlobalScriptsManager.FireEvent)),
                    prefix: new HarmonyMethod(typeof(CoopSceneShare), nameof(FireEventPrefix)));
                harmony.Patch(AccessTools.Method(typeof(UICinematic), nameof(UICinematic.EnableCinematic)),
                    prefix: new HarmonyMethod(typeof(CoopSceneShare), nameof(SceneShowing)));
                harmony.Patch(AccessTools.Method(typeof(PlayerController), nameof(PlayerController.SetControlTakenType)),
                    prefix: new HarmonyMethod(typeof(CoopSceneShare), nameof(ControlTaken)));
                harmony.Patch(AccessTools.Method(typeof(FlowOutput), nameof(FlowOutput.Call)),
                    prefix: new HarmonyMethod(typeof(CoopSceneShare), nameof(FlowCallPrefix)));
                MethodInfo gameLogics = AccessTools.Method(typeof(GameLogicsSystem), nameof(GameLogicsSystem.CustomUpdate));
                if (gameLogics != null)
                {
                    harmony.Patch(gameLogics, prefix: new HarmonyMethod(typeof(CoopSceneShare), nameof(GameLogicsPrefix)));
                }
                else
                {
                    log.LogWarning("Scene share: GameLogicsSystem.CustomUpdate not found; a joiner starts timed events too.");
                }
                MethodInfo bodyRoll = AccessTools.Method(typeof(GK2.FlowCanvasNodes.Flow_Body), "DoAction");
                if (bodyRoll != null && BodyRollFail != null)
                {
                    harmony.Patch(bodyRoll, prefix: new HarmonyMethod(typeof(CoopSceneShare), nameof(BodyRollPrefix)));
                }
                else
                {
                    log.LogWarning("Scene share: Flow_Body.DoAction not found; a joiner's donkey may deliver bodies too.");
                }
                log.LogInfo("Scene share: players can watch each other's story scenes.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Scene share disabled: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------- the player who triggers it

        private static void FireEventPrefix(string scriptName, string eventName)
        {
            if (Replaying)
            {
                return;
            }
            pendingScript = scriptName;
            pendingEvent = eventName;
            pendingAt = Time.unscaledTime;
            announcedPending = false;
        }

        private static void SceneShowing()
        {
            TryAnnounce();
        }

        private static void ControlTaken(TakenControlType t, bool isEnabled)
        {
            if (t == TakenControlType.ByFlow && !isEnabled)
            {
                TryAnnounce();
            }
        }

        /// <summary>A scene just took over this player's screen: offer it to the others.</summary>
        private static void TryAnnounce()
        {
            if (!Enabled || Replaying || announcedPending || pendingScript == null || Time.unscaledTime - pendingAt > StartWindow)
            {
                return;
            }
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsListening || netcode.ConnectedClientsIds.Count < 2 && netcode.IsHost)
            {
                return;
            }
            announcedPending = true;
            PlayerData player = MainGame.PlayerData;
            Vector3 position = MainGame.PlayerController != null ? ((Component)MainGame.PlayerController).transform.position : Vector3.zero;
            var start = new Offer
            {
                From = netcode.LocalClientId,
                Name = CoopSession.LocalName,
                Script = pendingScript,
                Event = pendingEvent,
                SceneId = player == null ? string.Empty : player.currentGameSceneId,
                Position = position,
                Sermon = SermonOf(player),
            };
            Send(netcode, start, null);
            Follow(start.Script);
            announced++;
            sharedScript = start.Script;
            sharedSince = Time.unscaledTime;
            sharedObject = FindRunning(start.Script);
            sharedObjectFound = sharedObject != null;
            sharedFreeSince = -1f;
            log.LogInfo("Scene share: offered " + start.Script + ":" + start.Event + " to the other players.");
        }

        private static void Send(NetworkManager netcode, Offer start, ulong? except)
        {
            using (var writer = new FastBufferWriter(512, Allocator.Temp))
            {
                writer.WriteValueSafe(KindOffer);
                writer.WriteValueSafe(start.From);
                writer.WriteValueSafe(new FixedString64Bytes(start.Name ?? string.Empty));
                writer.WriteValueSafe(new FixedString128Bytes(start.Script ?? string.Empty));
                writer.WriteValueSafe(new FixedString128Bytes(start.Event ?? string.Empty));
                writer.WriteValueSafe(new FixedString64Bytes(start.SceneId ?? string.Empty));
                writer.WriteValueSafe(start.Position);
                writer.WriteValueSafe(new FixedString64Bytes(start.Sermon ?? string.Empty));
                Deliver(netcode, writer, start.From, except);
            }
        }

        /// <summary>The scene this player offered has ended here; <paramref name="duration"/> is how long it ran.</summary>
        private static void SendEnd(NetworkManager netcode, ulong from, string script, float duration, ulong? except)
        {
            using (var writer = new FastBufferWriter(256, Allocator.Temp))
            {
                writer.WriteValueSafe(KindEnd);
                writer.WriteValueSafe(from);
                writer.WriteValueSafe(new FixedString128Bytes(script ?? string.Empty));
                writer.WriteValueSafe(duration);
                Deliver(netcode, writer, from, except);
            }
        }

        /// <summary>The triggering player chose an answer in the offered scene.</summary>
        private static void SendAnswer(NetworkManager netcode, ulong from, string script, string node, string answer, ulong? except)
        {
            using (var writer = new FastBufferWriter(512, Allocator.Temp))
            {
                writer.WriteValueSafe(KindAnswer);
                writer.WriteValueSafe(from);
                writer.WriteValueSafe(new FixedString128Bytes(script ?? string.Empty));
                writer.WriteValueSafe(new FixedString64Bytes(node ?? string.Empty));
                writer.WriteValueSafe(new FixedString128Bytes(answer ?? string.Empty));
                Deliver(netcode, writer, from, except);
            }
        }

        private static void Deliver(NetworkManager netcode, FastBufferWriter writer, ulong from, ulong? except)
        {
            if (netcode.IsHost)
            {
                foreach (ulong clientId in netcode.ConnectedClientsIds)
                {
                    if (clientId != netcode.LocalClientId && clientId != except && clientId != from)
                    {
                        netcode.CustomMessagingManager.SendNamedMessage(ShareMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
                    }
                }
            }
            else
            {
                netcode.CustomMessagingManager.SendNamedMessage(ShareMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
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
                if (kind == KindEnd)
                {
                    reader.ReadValueSafe(out ulong endFrom);
                    reader.ReadValueSafe(out FixedString128Bytes endScript);
                    reader.ReadValueSafe(out float duration);
                    if (netcode.IsHost)
                    {
                        endFrom = sender;
                        sharers.Remove(sender);
                        SendEnd(netcode, endFrom, endScript.ToString(), duration, sender);
                    }
                    Ended(endFrom, endScript.ToString(), duration);
                    return;
                }
                if (kind == KindAnswer)
                {
                    reader.ReadValueSafe(out ulong answerFrom);
                    reader.ReadValueSafe(out FixedString128Bytes answerScript);
                    reader.ReadValueSafe(out FixedString64Bytes node);
                    reader.ReadValueSafe(out FixedString128Bytes answer);
                    if (netcode.IsHost)
                    {
                        answerFrom = sender;
                        SendAnswer(netcode, answerFrom, answerScript.ToString(), node.ToString(), answer.ToString(), sender);
                    }
                    Answered(answerFrom, answerScript.ToString(), node.ToString(), answer.ToString());
                    return;
                }
                reader.ReadValueSafe(out ulong from);
                reader.ReadValueSafe(out FixedString64Bytes name);
                reader.ReadValueSafe(out FixedString128Bytes script);
                reader.ReadValueSafe(out FixedString128Bytes eventName);
                reader.ReadValueSafe(out FixedString64Bytes sceneId);
                reader.ReadValueSafe(out Vector3 position);
                reader.ReadValueSafe(out FixedString64Bytes sermon);
                var start = new Offer
                {
                    // The host vouches for who a joiner is.
                    From = netcode.IsHost ? sender : from,
                    Name = netcode.IsHost ? CoopSession.NameFor(sender) : name.ToString(),
                    Script = script.ToString(),
                    Event = eventName.ToString(),
                    SceneId = sceneId.ToString(),
                    Position = position,
                    Sermon = sermon.ToString(),
                };
                if (netcode.IsHost)
                {
                    sharers[sender] = start.Script;
                    Send(netcode, start, sender);
                }
                Consider(start);
            }
            catch (Exception ex)
            {
                log.LogWarning("Scene share: could not read an offer from " + sender + ": " + ex.Message);
            }
        }

        // ---------------------------------------------------------------- the others

        // Scenes running for other players (offered, not yet ended): they can still be watched later.
        private static readonly List<Offer> running = new List<Offer>();

        /// <summary>A scene another player is in right now, that this player is not watching.</summary>
        internal static bool CanWatchLate(out string name)
        {
            running.RemoveAll(r => r.Script == "Preview");
            Offer late = running.Count == 0 || watching != null || arriving != null || offer != null ? null : running[running.Count - 1];
            name = late == null ? null : late.Name;
            return late != null && (MainGame.PlayerController == null || MainGame.PlayerController.IsControlEnabledByType(TakenControlType.ByFlow));
        }

        /// <summary>Watch the scene another player is in, from its start (Pause > Co-op).</summary>
        internal static void WatchLate()
        {
            if (!CanWatchLate(out _))
            {
                return;
            }
            Offer late = running[running.Count - 1];
            log.LogInfo("Scene share: joining " + late.Name + "'s scene " + late.Script + " after it started.");
            lateJoins++;
            Watch(late);
        }

        private static int lateJoins;

        private static void Consider(Offer start)
        {
            running.RemoveAll(r => r.From == start.From);
            running.Add(start);
            // Busy with a scene of one's own, or already watching: the offer passes by.
            if (Replaying || offer != null || (MainGame.PlayerController != null && !MainGame.PlayerController.IsControlEnabledByType(TakenControlType.ByFlow)))
            {
                log.LogInfo("Scene share: " + start.Name + " started a scene while this player was busy; not offered.");
                return;
            }
            offer = start;
            offerUntil = Time.unscaledTime + PromptSeconds;
            acceptClicked = false;
            declineClicked = false;
            log.LogInfo("Scene share: " + start.Name + " offers " + start.Script + ":" + start.Event + ".");
        }

        /// <summary>
        /// The scene has ended for the player who triggered it. An offer still open closes; a
        /// replay here, which started a little later, gets as long as the scene took there plus a
        /// moment, then stops if it is still running (it may wait for something only that player
        /// can do).
        /// </summary>
        private static void Ended(ulong from, string script, float duration)
        {
            running.RemoveAll(r => r.From == from && r.Script == script);
            if (offer != null && offer.From == from && offer.Script == script)
            {
                log.LogInfo("Scene share: " + offer.Name + "'s scene ended before an answer.");
                offer = null;
                CoopScenePrompt.Withdraw();
            }
            if (watching == null || watching.From != from || watching.Script != script)
            {
                return;
            }
            if (arriving != null)
            {
                log.LogInfo("Scene share: " + watching.Name + "'s scene ended while this player was on the way.");
                arriving = null;
                watching = null;
                ReturnViewer();
                return;
            }
            stopAt = Mathf.Max(Time.unscaledTime + 1f, replayStartedAt + duration + 3f);
            log.LogInfo("Scene share: " + watching.Name + "'s scene ended there after " + duration.ToString("F1") + " s.");
        }

        /// <summary>The triggering player's choice: the replay here follows it, now or when it gets there.</summary>
        private static void Answered(ulong from, string script, string node, string answer)
        {
            foreach (Offer scene in running)
            {
                if (scene.From == from && scene.Script == script)
                {
                    scene.Answers[node] = answer;
                }
            }
            if (watching == null || watching.From != from || watching.Script != script)
            {
                return;
            }
            if (answersHeld.TryGetValue(node, out HeldAnswer held))
            {
                answersHeld.Remove(node);
                Choose(held, answer);
            }
            else
            {
                answersHeard[node] = answer;
            }
        }

        /// <summary>The replay reached a choice: wait for the triggering player's answer instead of asking here.</summary>
        private static void HoldAnswer(Flow_MultiAnswer node, Flow f)
        {
            var held = new HeldAnswer { Node = node, Flow = f };
            // The node's own "Out" goes on at once, as it does when the answers are shown.
            foreach (FlowOutput output in node.GetOutputFlowPorts())
            {
                if (output.name == "Out")
                {
                    output.Call(f);
                    break;
                }
            }
            if (answersHeard.TryGetValue(NodeKey(node), out string answer))
            {
                answersHeard.Remove(NodeKey(node));
                Choose(held, answer);
            }
            else
            {
                answersHeld[NodeKey(node)] = held;
                log.LogInfo("Scene share: waiting for " + watching?.Name + "'s answer.");
            }
        }

        private static void Choose(HeldAnswer held, string answer)
        {
            foreach (Flow_MultiAnswer.AnswerOption option in held.Node.AnswerOptions)
            {
                if (option.id == answer && option.output != null)
                {
                    answersFollowed++;
                    log.LogInfo("Scene share: following the answer \"" + answer + "\".");
                    option.output.Call(held.Flow);
                    return;
                }
            }
            log.LogWarning("Scene share: the answer \"" + answer + "\" is not in this scene here.");
        }

        /// <summary>From the plugin's Update: the offer's window, its answer, and the way back.</summary>
        internal static void Update()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            UpdateShared(netcode);
            CallLaterPorts();
            CoopScenePrompt.Update();
            if (watching != null)
            {
                // Watching ends when asked to, when the scene has ended for its player, or when
                // that player (or the session) is gone.
                bool gone = netcode == null || !netcode.IsListening || (netcode.IsHost && !IsConnected(netcode, watching.From));
                if (netcode == null || !netcode.IsListening)
                {
                    running.Clear();
                }
                if (CoopScenePrompt.TakeStop())
                {
                    stopClicked = true;
                }
                if (stopClicked || gone || (stopAt >= 0f && Time.unscaledTime >= stopAt))
                {
                    log.LogInfo("Scene share: stopped watching " + watching.Name + "'s scene (" + (stopClicked ? "asked to" : gone ? "they left" : "it ended there") + ").");
                    stopClicked = false;
                    StopWatching();
                    return;
                }
                CoopScenePrompt.Watching(watching.Name);
            }
            else if (previewWatching)
            {
                CoopScenePrompt.Watching("Host");
            }
            else
            {
                CoopScenePrompt.HidePanel();
            }
            if (stoppedGraphs.Count > 0 && Time.unscaledTime > stoppedUntil)
            {
                stoppedGraphs.Clear();
            }
            if (arriving != null)
            {
                // Arrived when the teleport has given control back, or after a while regardless.
                bool teleporting = MainGame.PlayerController != null && !MainGame.PlayerController.IsControlEnabledByType(TakenControlType.ByTeleport);
                float waited = Time.unscaledTime - arrivingSince;
                if ((!teleporting && waited > 0.5f) || waited > 15f)
                {
                    Offer start = arriving;
                    arriving = null;
                    StartReplay(start);
                }
                return;
            }
            if (returnPending && !Replaying)
            {
                returnPending = false;
                watching = null;
                stopAt = -1f;
                ReturnViewer();
            }
            if (offer == null)
            {
                return;
            }
            if (acceptClicked || CoopScenePrompt.TakeWatch())
            {
                Offer accepted = offer;
                offer = null;
                acceptClicked = false;
                CoopScenePrompt.Answered(true);
                if (accepted.Script != "Preview")
                {
                    Watch(accepted);
                }
                return;
            }
            if (declineClicked || CoopScenePrompt.TakeSkip() || Time.unscaledTime >= offerUntil)
            {
                log.LogInfo("Scene share: " + (Time.unscaledTime >= offerUntil ? "no answer; declined" : "declined") + " " + offer.Name + "'s scene.");
                offer = null;
                declineClicked = false;
                CoopScenePrompt.Answered(false);
                return;
            }
            CoopScenePrompt.Ask(offer.Name, offerUntil - Time.unscaledTime);
        }

        /// <summary>From the plugin's OnGUI: the plain look, when the game's parts are not used.</summary>
        internal static void DrawPlain()
        {
            CoopScenePrompt.DrawPlain(offer != null, watching != null || previewWatching, offerUntil - Time.unscaledTime);
        }

        /// <summary>Teleport to the player who started it, then play the same scene here.</summary>
        private static void Watch(Offer start)
        {
            try
            {
                PlayerData player = MainGame.PlayerData;
                returnScene = player == null ? null : player.currentGameSceneId;
                returnPosition = ((Component)MainGame.PlayerController).transform.position;
                log.LogInfo("Scene share: watching " + start.Name + "'s scene " + start.Script + ":" + start.Event + ".");
                GameSceneData target = MainGame.Instance.GameSave.worldData.GetGameSceneDataById(start.SceneId);
                if (target != null)
                {
                    PlayerController.Teleport(new PositionTeleport(target, start.Position, null));
                }
                // The scene starts once the teleport has finished (see Update).
                arriving = start;
                arrivingSince = Time.unscaledTime;
                watching = start;
                stopAt = -1f;
                stopClicked = false;
            }
            catch (Exception ex)
            {
                log.LogWarning("Scene share: could not join the scene: " + ex.Message);
            }
        }

        private static void StartReplay(Offer start)
        {
            try
            {
                GlobalFlowScript script = GlobalScriptsManager.RunFlowScript(start.Script, () => OnReplayFinished(start));
                if (script == null)
                {
                    log.LogWarning("Scene share: the scene " + start.Script + " is not available here.");
                    watching = null;
                    ReturnViewer();
                    return;
                }
                replays.Add(script.gameObject);
                Follow(start.Script);
                replaysStarted++;
                replayStartedAt = Time.unscaledTime;
                answersHeard.Clear();
                answersHeld.Clear();
                // Watching later: the answers already given are followed when the copy gets there.
                foreach (KeyValuePair<string, string> given in start.Answers)
                {
                    answersHeard[given.Key] = given.Value;
                }
                returnPending = true;
                LendSermon(start.Sermon);
                script.FireEvent(start.Event);
            }
            catch (Exception ex)
            {
                log.LogWarning("Scene share: could not play the scene: " + ex.Message);
                replays.Clear();
                watching = null;
                ReturnViewer();
            }
        }

        private static void OnReplayFinished(Offer start)
        {
            replays.RemoveWhere(go => go == null);
            // The finished script's object is destroyed right after this callback.
            replays.Clear();
            GiveSermonBack();
            replaysFinished++;
            log.LogInfo("Scene share: " + start.Name + "'s scene finished here.");
        }

        /// <summary>The triggering player's side: tell the others once the offered scene is over.</summary>
        private static void UpdateShared(NetworkManager netcode)
        {
            if (netcode != null && netcode.IsHost && sharers.Count > 0)
            {
                // A player who left in the middle of their scene: it ends for those watching.
                foreach (KeyValuePair<ulong, string> sharer in new List<KeyValuePair<ulong, string>>(sharers))
                {
                    if (!IsConnected(netcode, sharer.Key))
                    {
                        sharers.Remove(sharer.Key);
                        if (netcode.IsListening)
                        {
                            SendEnd(netcode, sharer.Key, sharer.Value, 0f, null);
                        }
                    }
                }
            }
            if (sharedScript == null)
            {
                return;
            }
            // Over when the script is gone, or when the player has had control and a clear screen
            // for a moment (some scenes keep their script waiting for later parts of the story).
            PlayerController controller = MainGame.PlayerController;
            bool free = controller != null && controller.IsControlEnabledByType(TakenControlType.ByFlow) && !GameUi.InCinematic;
            sharedFreeSince = free ? (sharedFreeSince < 0f ? Time.unscaledTime : sharedFreeSince) : -1f;
            bool over = (sharedObjectFound && sharedObject == null) || (sharedFreeSince >= 0f && Time.unscaledTime - sharedFreeSince > 1.5f) ||
                        Time.unscaledTime - sharedSince > 900f;
            if (!over)
            {
                return;
            }
            float duration = Time.unscaledTime - sharedSince - (sharedFreeSince >= 0f ? Time.unscaledTime - sharedFreeSince : 0f);
            if (netcode != null && netcode.IsListening)
            {
                SendEnd(netcode, netcode.LocalClientId, sharedScript, duration, null);
            }
            log.LogInfo("Scene share: " + sharedScript + " ended here after " + duration.ToString("F1") + " s.");
            sharedScript = null;
            sharedObject = null;
        }

        private static bool IsConnected(NetworkManager netcode, ulong clientId)
        {
            foreach (ulong id in netcode.ConnectedClientsIds)
            {
                if (id == clientId)
                {
                    return true;
                }
            }
            return false;
        }

        private static GameObject FindRunning(string script)
        {
            GlobalScriptsManager manager = GlobalScriptsManager.Instance;
            if (manager == null)
            {
                return null;
            }
            string objectName = "[g_fs] " + script;
            for (int i = manager.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = manager.transform.GetChild(i);
                if (child.name == objectName && !replays.Contains(child.gameObject))
                {
                    return child.gameObject;
                }
            }
            return null;
        }

        /// <summary>
        /// Ends the replay here, wherever it is: the script stops, its lines, fade and camera are
        /// cleared, and the viewer goes back.
        /// </summary>
        private static void StopWatching()
        {
            CoopScenePrompt.HidePanel();
            stopAt = -1f;
            watching = null;
            if (arriving != null)
            {
                arriving = null;
                ReturnViewer();
                return;
            }
            foreach (GameObject go in new List<GameObject>(replays))
            {
                if (go == null)
                {
                    continue;
                }
                var controller = go.GetComponent<FlowScriptController>();
                if (controller != null && controller.graph != null)
                {
                    stoppedGraphs.Add(controller.graph);
                }
                var script = go.GetComponent<GlobalFlowScript>();
                if (script != null)
                {
                    script.Terminate(true);
                }
            }
            replays.Clear();
            GiveSermonBack();
            answersHeld.Clear();
            answersHeard.Clear();
            stoppedUntil = Time.unscaledTime + 60f;
            replaysStopped++;
            try
            {
                AccessTools.Method(typeof(LazyBearTechnology.UISpeechBubble), "ForceHideAll")?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                log.LogWarning("Scene share: could not clear the scene's lines: " + ex.Message);
            }
            try
            {
                UIFade fade = LazyBearTechnology.LazyUI.Get<UIFade>();
                if (fade != null)
                {
                    fade.FadeOut(0.3f, null, FadeFlag.FlowScript);
                }
                CameraSystem.Instance.GetCameraController(CameraType.Main).SetTarget(MainGame.PlayerController.View.transform, 0.01f, null);
            }
            catch (Exception ex)
            {
                log.LogWarning("Scene share: could not reset the screen: " + ex.Message);
            }
            // The way back follows in Update (returnPending).
        }

        /// <summary>Back to where the viewer was, with control and the HUD restored.</summary>
        private static void ReturnViewer()
        {
            if (MainGame.PlayerController == null)
            {
                // Left the world (the session ended): nothing to go back to.
                return;
            }
            try
            {
                // A walk the scene started would carry on after the teleport.
                MainGame.PlayerController.MovementComponent?.ForceStop();
                MainGame.PlayerController.SetControlTakenType(TakenControlType.ByFlow, true);
                UICinematic bars = LazyBearTechnology.LazyUI.Get<UICinematic>();
                if (bars != null && bars.gameObject.activeSelf)
                {
                    bars.DisableCinematic();
                }
                GameSceneData home = returnScene == null ? null : MainGame.Instance.GameSave.worldData.GetGameSceneDataById(returnScene);
                if (home != null)
                {
                    PlayerController.Teleport(new PositionTeleport(home, returnPosition, null));
                }
                returned++;
                log.LogInfo("Scene share: back to where this player was.");
            }
            catch (Exception ex)
            {
                log.LogWarning("Scene share: could not return the viewer: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------- skipping steps in a replay

        /// <summary>
        /// Every step of every flow graph passes through here. In a replay, a step whose type is
        /// skipped is not run; its "out" is followed instead, so the scene carries on.
        /// </summary>
        private static bool gameLogicsNoted;

        /// <summary>
        /// The game's timed events (<c>GameLogicsSystem</c>: a visitor, Albert bringing three
        /// zombies in <c>Event_24_Doctor_Zombies</c>, the donkey's delivery) are rolled on every
        /// machine by its own clock and dice, and each one's zombies and items were then shared:
        /// three zombies twice (Workshop report, 5 October 2026). They are the host's: the host
        /// starts them, a joiner can watch the scene (scene share) and gets what it leaves through
        /// the other syncs.
        /// </summary>
        private static bool GameLogicsPrefix()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsListening || netcode.IsHost || !CoopDropSync.Enabled)
            {
                return true;
            }
            if (!gameLogicsNoted)
            {
                gameLogicsNoted = true;
                log.LogInfo("Scene share: timed events are the host's; none start here.");
            }
            return false;
        }

        private static readonly FieldInfo BodyRollFail = AccessTools.Field(typeof(GK2.FlowCanvasNodes.Flow_Body), "fail");
        private static int bodyRollsSkipped;

        /// <summary>
        /// The donkey's body delivery (<c>Flow_Body</c> in <c>npc_donkey</c>) rolls on every machine,
        /// each with its own dice, and each delivered body was then shared: two bodies, or a body
        /// only one player had asked for (Workshop report, 5 October 2026; seen in the day-58 trace).
        /// The host's roll decides; a joiner always takes the "no body" way, and the host's body
        /// reaches it through the drop sync.
        /// </summary>
        private static bool BodyRollPrefix(GK2.FlowCanvasNodes.Flow_Body __instance, Flow flow)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsListening || netcode.IsHost || !CoopDropSync.Enabled)
            {
                return true;
            }
            try
            {
                if (BodyRollFail.GetValue(__instance) is FlowOutput fail)
                {
                    if (bodyRollsSkipped++ < 5)
                    {
                        log.LogInfo("Scene share: the donkey's body roll is the host's; none here.");
                    }
                    fail.Call(flow);
                    return false;
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Scene share: could not skip the body roll: " + ex.Message);
            }
            return true;
        }

        private static bool FlowCallPrefix(FlowOutput __instance, Flow f)
        {
            if (CoopDiagnostics.Detailed)
            {
                TraceWorldSteps(__instance);
            }
            if (replays.Count == 0 && trailScript == null && stoppedGraphs.Count == 0 && sharedScript == null)
            {
                return true;
            }
            // "pointer" is an event; its delegate lives in the field behind it.
            var pointer = PointerField.GetValue(__instance) as FlowHandler;
            if (pointer == null)
            {
                return true;
            }
            Delegate[] targets = pointer.GetInvocationList();
            var nodes = new FlowNode[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                nodes[i] = NodeOf(targets[i]);
            }
            if (stoppedGraphs.Count > 0)
            {
                foreach (FlowNode node in nodes)
                {
                    if (node != null && stoppedGraphs.Contains(node.graph))
                    {
                        // A step of a replay that was stopped (a tween or timer finishing late).
                        return false;
                    }
                }
            }
            if (trailScript != null)
            {
                Trail(nodes);
            }
            if (sharedScript != null && __instance.parent is Flow_MultiAnswer asked)
            {
                TrySendAnswer(asked, __instance);
            }
            if (replays.Count == 0)
            {
                return true;
            }
            bool special = false;
            foreach (FlowNode node in nodes)
            {
                if (node != null && (node is Flow_MultiAnswer || Skipped.Contains(node.GetType().Name)) && InReplay(node))
                {
                    special = true;
                    break;
                }
            }
            if (!special)
            {
                return true;
            }
            for (int i = 0; i < targets.Length; i++)
            {
                FlowNode node = nodes[i];
                if (node != null && InReplay(node))
                {
                    if (node is Flow_MultiAnswer choice)
                    {
                        HoldAnswer(choice, f);
                        continue;
                    }
                    if (Skipped.Contains(node.GetType().Name))
                    {
                        skippedSteps++;
                        ContinueAfter(node, f);
                        continue;
                    }
                }
                ((FlowHandler)targets[i])(f);
            }
            return false;
        }

        /// <summary>The triggering player picked an answer in the scene they offered: tell the viewers.</summary>
        private static void TrySendAnswer(Flow_MultiAnswer asked, FlowOutput chosen)
        {
            Component agent = asked.graph == null ? null : asked.graph.agent;
            if (agent == null || sharedObject == null || agent.gameObject != sharedObject)
            {
                log.LogInfo("Scene share: an answer outside the offered scene (" + (agent == null ? "no agent" : agent.gameObject.name) + ", offered " + (sharedObject == null ? "none" : sharedObject.name) + ").");
                return;
            }
            foreach (Flow_MultiAnswer.AnswerOption option in asked.AnswerOptions)
            {
                if (option.output == chosen)
                {
                    NetworkManager netcode = NetworkManager.Singleton;
                    if (netcode != null && netcode.IsListening)
                    {
                        SendAnswer(netcode, netcode.LocalClientId, sharedScript, NodeKey(asked), option.id, null);
                        answersSent++;
                        log.LogInfo("Scene share: answered \"" + option.id + "\"; the viewers follow.");
                    }
                    return;
                }
            }
        }

        // Closure classes and the field in them that leads to the node
        private static readonly Dictionary<Type, FieldInfo> closureNode = new Dictionary<Type, FieldInfo>();

        /// <summary>
        /// The node a step's handler belongs to: the handler's target, or, for a lambda that
        /// captured locals, the node its closure refers to.
        /// </summary>
        // Detailed logs: which scripts run world steps on this machine outside a watched scene (each
        // machine runs the game's own scripts: a delivery or a quest's reward made on both, then
        // shared by the drop sync, is two).
        private static readonly HashSet<string> tracedWorldSteps = new HashSet<string>(StringComparer.Ordinal);

        private static void TraceWorldSteps(FlowOutput output)
        {
            try
            {
                NetworkManager netcode = NetworkManager.Singleton;
                if (netcode == null || !netcode.IsListening || tracedWorldSteps.Count > 400)
                {
                    return;
                }
                var pointer = PointerField.GetValue(output) as FlowHandler;
                if (pointer == null)
                {
                    return;
                }
                foreach (Delegate target in pointer.GetInvocationList())
                {
                    FlowNode node = NodeOf(target);
                    if (node == null || !Skipped.Contains(node.GetType().Name) || InReplay(node))
                    {
                        continue;
                    }
                    string graph = node.graph == null ? "?" : node.graph.name;
                    string key = graph + "|" + node.GetType().Name;
                    if (tracedWorldSteps.Add(key))
                    {
                        log.LogInfo("World step: " + node.GetType().Name + " in " + graph + " on the " + (netcode.IsHost ? "host" : "joiner") +
                                    (CoopQuestSync.ApplyingRemote ? " (during a mirrored quest change)" : string.Empty) + ".");
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private static FlowNode NodeOf(Delegate handler)
        {
            object target = handler.Target;
            for (int depth = 0; depth < 3 && target != null; depth++)
            {
                if (target is FlowNode node)
                {
                    return node;
                }
                Type type = target.GetType();
                if (!closureNode.TryGetValue(type, out FieldInfo next))
                {
                    next = null;
                    if (type.IsDefined(typeof(CompilerGeneratedAttribute), false))
                    {
                        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (typeof(FlowNode).IsAssignableFrom(field.FieldType))
                            {
                                next = field;
                                break;
                            }
                            if (next == null && field.FieldType.IsDefined(typeof(CompilerGeneratedAttribute), false))
                            {
                                next = field;
                            }
                        }
                    }
                    closureNode[type] = next;
                }
                if (next == null)
                {
                    return null;
                }
                target = next.GetValue(target);
            }
            return null;
        }

        /// <summary>
        /// Which step of the scene: its place in the graph, the same in every copy loaded from the
        /// game's asset (node UIDs are made anew on each load).
        /// </summary>
        private static string NodeKey(FlowNode node)
        {
            return node.graph == null ? "?" : node.graph.allNodes.IndexOf(node).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool InReplay(FlowNode node)
        {
            Component agent = node.graph == null ? null : node.graph.agent;
            if (agent == null)
            {
                return false;
            }
            for (Transform t = agent.transform; t != null; t = t.parent)
            {
                if (replays.Contains(t.gameObject))
                {
                    return true;
                }
            }
            return false;
        }

        private static void Follow(string script)
        {
            trailScript = "[g_fs] " + script;
            trailUntil = Time.unscaledTime + 300f;
            trail.Clear();
        }

        private static void Trail(FlowNode[] nodes)
        {
            if (Time.unscaledTime > trailUntil)
            {
                trailScript = null;
                return;
            }
            foreach (FlowNode node in nodes)
            {
                Component agent = node == null || node.graph == null ? null : node.graph.agent;
                if (agent != null && agent.gameObject.name.StartsWith(trailScript, StringComparison.Ordinal))
                {
                    trail.Enqueue(node.GetType().Name.Replace("Flow_", string.Empty));
                    while (trail.Count > 12)
                    {
                        trail.Dequeue();
                    }
                }
            }
        }

        private static readonly FieldInfo PointerField = AccessTools.Field(typeof(FlowOutput), "pointer");

        // ---------------------------------------------------------------- sermons

        private static bool sermonLent;
        private static SermonResultData sermonBefore;

        private static string SermonOf(PlayerData player)
        {
            SermonResultData sermon = pendingScript == "System_Pray" && player != null ? player.currentSermon : null;
            return sermon == null ? string.Empty : sermon.id + "|" + (sermon.success ? "1" : "0");
        }

        /// <summary>
        /// A sermon plays on the viewer as it went for the preacher: its steps read "the current
        /// sermon", which the viewer lends for the length of the copy (its rewards stay with the
        /// preacher, those steps are skipped here).
        /// </summary>
        private static void LendSermon(string sermon)
        {
            string[] parts = (sermon ?? string.Empty).Split('|');
            PlayerData player = MainGame.PlayerData;
            if (parts.Length != 2 || player == null)
            {
                return;
            }
            try
            {
                var lent = new SermonResultData(parts[0], string.Empty, 0, parts[1] == "1", 0, 0);
                sermonBefore = player.currentSermon;
                player.currentSermon = lent;
                sermonLent = true;
                if (MainGame.PlayerController != null && lent.Definition != null)
                {
                    MainGame.PlayerController.View.SetSermonIcon(lent.Definition.PrayIcon);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Scene share: could not follow the sermon: " + ex.Message);
            }
        }

        private static void GiveSermonBack()
        {
            if (!sermonLent)
            {
                return;
            }
            sermonLent = false;
            if (MainGame.PlayerData != null)
            {
                MainGame.PlayerData.currentSermon = sermonBefore;
            }
            sermonBefore = null;
        }

        /// <summary>
        /// Skipped steps whose scene also waits for a second, later port: the sermon's success or
        /// failure plays its animation for the player holding it, then says it is done. The copy
        /// goes on after about as long.
        /// </summary>
        private static readonly Dictionary<string, KeyValuePair<string, float>> LaterPorts = new Dictionary<string, KeyValuePair<string, float>>(StringComparer.Ordinal)
        {
            ["Flow_FinishSermon"] = new KeyValuePair<string, float>("OnFinish", 2.5f),
        };

        private struct LaterCall
        {
            internal FlowOutput Port;
            internal Flow Flow;
            internal float At;
        }

        private static readonly List<LaterCall> laterCalls = new List<LaterCall>();

        private static void CallLaterPorts()
        {
            if (laterCalls.Count == 0)
            {
                return;
            }
            if (replays.Count == 0)
            {
                laterCalls.Clear();
                return;
            }
            for (int i = laterCalls.Count - 1; i >= 0; i--)
            {
                LaterCall call = laterCalls[i];
                if (Time.unscaledTime >= call.At)
                {
                    laterCalls.RemoveAt(i);
                    try
                    {
                        call.Port.Call(call.Flow);
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning("Scene share: a later step of the scene failed here: " + ex.Message);
                    }
                }
            }
        }

        private static void ContinueAfter(FlowNode node, Flow f)
        {
            if (LaterPorts.TryGetValue(node.GetType().Name, out KeyValuePair<string, float> later))
            {
                foreach (FlowOutput output in node.GetOutputFlowPorts())
                {
                    if (string.Equals(output.name, later.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        laterCalls.Add(new LaterCall { Port = output, Flow = f, At = Time.unscaledTime + later.Value });
                    }
                }
            }
            FlowOutput next = null;
            foreach (FlowOutput output in node.GetOutputFlowPorts())
            {
                if (string.Equals(output.name, "Out", StringComparison.OrdinalIgnoreCase))
                {
                    next = output;
                    break;
                }
                if (next == null)
                {
                    next = output;
                }
            }
            if (next != null)
            {
                next.Call(f);
            }
        }

        // ---------------------------------------------------------------- tests

        /// <summary>Tests: answer the offer on screen ("watch" or "decline").</summary>
        internal static string AnswerForTest(string answer)
        {
            if (answer == "preview" || answer == "preview-off")
            {
                return PreviewForTest(answer == "preview");
            }
            if (answer == "preview-yes" || answer == "preview-no")
            {
                // The answer playing out, without going anywhere.
                offer = null;
                previewWatching = false;
                CoopScenePrompt.Answered(answer == "preview-yes");
                return "answer shown";
            }
            if (answer == "preview-watching")
            {
                previewWatching = true;
                return "watching panel";
            }

            if (answer == "stop")
            {
                return StopForTest();
            }
            if (answer.StartsWith("event:", StringComparison.Ordinal))
            {
                // One of the scene's own events, sent to the copy playing here.
                int sent = 0;
                foreach (GameObject go in replays)
                {
                    GlobalFlowScript script = go == null ? null : go.GetComponent<GlobalFlowScript>();
                    if (script != null)
                    {
                        script.FireEvent(answer.Substring(6));
                        sent++;
                    }
                }
                return "fired in " + sent + " replay(s)";
            }
            if (offer == null)
            {
                return "no offer";
            }
            if (answer == "watch") acceptClicked = true; else declineClicked = true;
            return "answered " + answer;
        }

        /// <summary>
        /// Tests: both windows on screen without a scene (the question with a long-lasting offer,
        /// and the watching panel), for checking every language fits; "off" takes them away.
        /// </summary>
        internal static string PreviewForTest(bool on)
        {
            if (!on)
            {
                previewWatching = false;
            }
            if (on)
            {
                offer = new Offer { From = ulong.MaxValue, Name = "Host", Script = "Preview", Event = "preview", SceneId = string.Empty };
                offerUntil = Time.unscaledTime + PromptSeconds;
                acceptClicked = false;
                declineClicked = false;
            }
            else if (offer != null && offer.Script == "Preview")
            {
                offer = null;
                CoopScenePrompt.Withdraw();
            }
            return on ? "previewing" : "preview off";
        }

        /// <summary>Tests: press "Stop watching".</summary>
        internal static string StopForTest()
        {
            if (watching == null)
            {
                return "not watching";
            }
            stopClicked = true;
            return "stopping";
        }

        /// <summary>Tests: what is on screen.</summary>
        internal static string DescribePrompt()
        {
            return CoopScenePrompt.Describe() + " watching=" + CoopScenePrompt.DescribePanel();
        }

        /// <summary>A teleport to a place in a scene (the game's own go to way points or objects).</summary>
        private sealed class PositionTeleport : TeleportDataBase
        {
            private readonly GameSceneData scene;
            private readonly Vector3 position;

            internal PositionTeleport(GameSceneData scene, Vector3 position, Action onLoaded)
                : base("outdoor", string.Empty, onLoaded, false, 0.3f)
            {
                this.scene = scene;
                this.position = position;
            }

            public override string GetDestinationId()
            {
                return scene.id;
            }

            public override GameSceneData GetDestinationSceneData()
            {
                return scene;
            }

            public override Vector3 GetPosition()
            {
                return position;
            }
        }
    }
}
