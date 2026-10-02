using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Tools for testing the co-op features by hand, in Pause > Co-op > Test tools, only when
    /// <c>[Testing] TestTools = true</c> (off by default; for test copies, not for players). One
    /// press builds a test yard around the player — a chest, crafting stations, an autopsy table
    /// and graves with bodies, a garden bed, a zombie at work — hands out a kit, starts story
    /// scenes and sermons, takes the player to the places that cannot be moved, and sets the
    /// weather. Objects are placed through <see cref="CoopBuildSync"/>, so the other players get
    /// them as they would a building. Developer tooling: its labels are English only.
    /// </summary>
    internal static class CoopTestTools
    {
        private enum Page
        {
            Main,
            Scenes,
            Places
        }

        private sealed class Scheduled
        {
            internal float At;
            internal string Label;
            internal Func<string> Run;
        }

        /// <summary>Story scenes that are known to play through from a day-18 world (see the scene-share tests).</summary>
        private static readonly string[][] Scenes =
        {
            new[] { "Village money (at Jeffry)", "npc_jeffry", "Event_124_Village_Money", "124_village_money_chest_1" },
            new[] { "The attic (asks an answer)", string.Empty, "Event_116_Base_Attic", "116_base_attic_1" },
            new[] { "Resurrection room (new NPC)", string.Empty, "Event_137_Base_Resurrection", "137_base_resurection_room_1" },
        };

        private static readonly string[][] Places =
        {
            new[] { "Home", "teleport_milestone_6_home" },
            new[] { "Graveyard", "teleport_milestone_4_graveyard" },
            new[] { "Village", "teleport_milestone_5_village" },
            new[] { "Church", "church_tribune" },
            new[] { "Well", "teleport_milestone_3_well" },
            new[] { "Port", "teleport_milestone_2_port_area" },
        };

        private static ManualLogSource log;
        private static Page page;
        private static string lastResult = string.Empty;
        private static readonly List<Scheduled> scheduled = new List<Scheduled>();

        internal static bool Enabled { get; set; }

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static string Title => page == Page.Scenes ? "Test: story scenes" : page == Page.Places ? "Test: go to" : "Test tools";

        /// <summary>The page when it is shown again.</summary>
        internal static void Reset()
        {
            page = Page.Main;
        }

        /// <summary>
        /// Draws the current page into the pause menu's co-op window. <paramref name="close"/>: the
        /// window should close (an action that needs the world); <paramref name="back"/>: back to
        /// the co-op page.
        /// </summary>
        internal static void Draw(GameUiPanel panel, out bool close, out bool back)
        {
            close = false;
            back = false;
            if (lastResult.Length > 0)
            {
                panel.Label(lastResult, GameUi.TextKind.Hint);
            }
            if (page == Page.Scenes)
            {
                foreach (string[] scene in Scenes)
                {
                    if (panel.Button(scene[0]))
                    {
                        close = true;
                        StartScene(scene);
                    }
                }
                if (panel.Button("Start a sermon"))
                {
                    close = true;
                    Report(StartSermon());
                }
                if (panel.Button("Back"))
                {
                    page = Page.Main;
                }
                return;
            }
            if (page == Page.Places)
            {
                foreach (string[] place in Places)
                {
                    if (panel.Button(place[0]))
                    {
                        close = true;
                        Report(TeleportTo(place[1]));
                    }
                }
                if (panel.Button("Back"))
                {
                    page = Page.Main;
                }
                return;
            }
            if (panel.Button("Build the test yard here"))
            {
                close = true;
                Report(BuildYard());
            }
            if (panel.Button("Give me the kit"))
            {
                Report(GiveKit());
            }
            if (panel.Button("Drop a body here"))
            {
                Report(DropBody());
            }
            if (panel.Button("Story scenes and sermon"))
            {
                page = Page.Scenes;
            }
            if (panel.Button("Go to..."))
            {
                page = Page.Places;
            }
            if (panel.Button("Rain"))
            {
                Report(SetWeather("RainDayThunder"));
            }
            if (panel.Button("Clear weather"))
            {
                Report(SetWeather("CleanWeather"));
            }
            if (panel.Button("Sleep now"))
            {
                close = true;
                Report(Sleep());
            }
            if (panel.Button("Full energy"))
            {
                Report(Refill());
            }
            if (panel.Button("Back"))
            {
                back = true;
            }
        }

        /// <summary>From the plugin's Update: actions waiting for a teleport to finish.</summary>
        internal static void Update()
        {
            for (int i = scheduled.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < scheduled[i].At)
                {
                    continue;
                }
                Scheduled due = scheduled[i];
                scheduled.RemoveAt(i);
                try
                {
                    Report(due.Label + ": " + due.Run());
                }
                catch (Exception ex)
                {
                    Report(due.Label + " failed: " + ex.Message);
                }
            }
        }

        private static void Report(string result)
        {
            lastResult = result ?? string.Empty;
            log.LogInfo("Test tools: " + lastResult);
        }

        // ---------------------------------------------------------------- the test yard

        /// <summary>
        /// Objects in a grid next to the player, one step apart: storage, the stations the host
        /// runs, the autopsy table and graves (one player at a time), a garden bed, and a zombie
        /// working the sawhorse.
        /// </summary>
        internal static string BuildYard()
        {
            PlayerData player = MainGame.PlayerData;
            object gameScene = CoopDiagnostics.GetMember(MainGame.PlayerController, "CurrentGameScene");
            if (player == null || gameScene == null)
            {
                return "not in a world";
            }
            string scene = player.currentGameSceneId;
            Vector3 origin = player.position.Value + new Vector3(2.5f, 0f, -2.5f);
            string[] layout =
            {
                "chest", "sawhorse", "iron_anvil",
                "furnace_1", "autopsy_table_1", "embalm_table_1",
                "grave_empty", "grave_ground", "garden_empty",
            };
            var placed = new List<string>();
            var objects = new Dictionary<string, WgoData>();
            for (int i = 0; i < layout.Length; i++)
            {
                string id = layout[i];
                if (GameBalance.Me.GetDataOrNull<WGODef>(id) == null)
                {
                    placed.Add(id + "?");
                    continue;
                }
                Vector3 at = origin + new Vector3((i % 3) * 2.6f, 0f, -(i / 3) * 2.6f);
                var data = new WgoData(id, at, scene);
                CoopBuildSync.TestPlace(gameScene, data);
                objects[id] = data;
                placed.Add(id);
            }
            // Contents: things to share in the chest, a body on the table and in the filled grave.
            if (objects.TryGetValue("chest", out WgoData chest) && chest.Inventory != null)
            {
                foreach (string item in new[] { "berry", "wood", "stone" })
                {
                    if (GameBalance.Me.GetDataOrNull<ItemDef>(item) != null)
                    {
                        chest.Inventory.AddItemToInventory(new Item(item, 10));
                    }
                }
            }
            foreach (string holder in new[] { "autopsy_table_1", "grave_ground" })
            {
                if (objects.TryGetValue(holder, out WgoData data) && data.Inventory != null)
                {
                    Item body = NewBody("body_0_2");
                    if (body != null)
                    {
                        data.Inventory.AddItemToInventory(body);
                    }
                }
            }
            string zombie = objects.TryGetValue("sawhorse", out WgoData sawhorse) ? MakeZombieFor(sawhorse, scene) : "no sawhorse";
            return "yard: " + string.Join(", ", placed.ToArray()) + "; zombie: " + zombie;
        }

        private static Item NewBody(string id)
        {
            BodyDef body = GameBalance.Me.GetDataOrNull<BodyDef>(id) ?? GameBalance.Me.bodyDefs.FirstOrDefault();
            return body == null ? null : body.GenerateItem();
        }

        /// <summary>A zombie made from a test body and assigned to a station, as the game's zombie tools do.</summary>
        private static string MakeZombieFor(WgoData station, string scene)
        {
            try
            {
                Item bodyItem = NewBody("body_zombie_test_5");
                object zombies = MainGame.ZombieSystemData;
                Vector3 near = station.Position + new Vector3(1.2f, 0f, -1.2f);
                MethodInfo create = zombies.GetType().GetMethod("CreateZombieDrop");
                object[] createArgs = create.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
                createArgs[0] = "zombie";
                createArgs[1] = near;
                createArgs[2] = scene;
                createArgs[3] = bodyItem;
                object zombie = create.Invoke(zombies, createArgs);
                MethodInfo put = zombies.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .First(m => m.Name == "PutZombieFromStoreToGameScene" && m.GetParameters().Length == 4);
                put.Invoke(zombies, new[] { CoopDiagnostics.GetMember(zombie, "UniqueId"), scene, near, Enum.ToObject(put.GetParameters()[3].ParameterType, 0) });
                MethodInfo attach = zombie.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .First(m => m.Name == "AttachToCraftWgoData" && m.GetParameters().Length == 3);
                attach.Invoke(zombie, new[] { (object)station.UniqueId, CoopDiagnostics.GetMember(zombie, "ZombieItem"), null });
                return "at the sawhorse";
            }
            catch (Exception ex)
            {
                return "not made (" + (ex.InnerException ?? ex).Message + ")";
            }
        }

        // ---------------------------------------------------------------- the player

        /// <summary>Money, a tool of every kind the co-op features need, seeds and materials.</summary>
        internal static string GiveKit()
        {
            PlayerData player = MainGame.PlayerData;
            if (player == null)
            {
                return "not in a world";
            }
            player.SetRes("money", player.GetResInt("money") + 1000);
            var given = new List<string> { "money +1000" };
            foreach (ItemType type in new[] { ItemType.Axe, ItemType.Shovel, ItemType.Pickaxe, ItemType.Hammer, ItemType.SurgicalKit, ItemType.Sword })
            {
                ItemDef tool = GameBalance.Me.itemDefs.FirstOrDefault(d => d.type == type);
                if (tool != null && player.inventory.AddItemToInventory(new Item(tool.id, 1)))
                {
                    given.Add(tool.id);
                }
            }
            foreach (string item in new[] { "wood", "stone", "berry" })
            {
                if (GameBalance.Me.GetDataOrNull<ItemDef>(item) != null && player.inventory.AddItemToInventory(new Item(item, 20)))
                {
                    given.Add(item + " x20");
                }
            }
            ItemDef seed = GameBalance.Me.itemDefs.FirstOrDefault(d => d.id.StartsWith("seed", StringComparison.Ordinal));
            if (seed != null && player.inventory.AddItemToInventory(new Item(seed.id, 10)))
            {
                given.Add(seed.id + " x10");
            }
            return "kit: " + string.Join(", ", given.ToArray());
        }

        internal static string DropBody()
        {
            PlayerData player = MainGame.PlayerData;
            Item body = NewBody("body_0_2");
            if (player == null || body == null)
            {
                return "no body";
            }
            MainGame.Instance.dropSystem.DropItem(body, player.currentGameSceneId, player.position.Value + new Vector3(1f, 0f, -1f));
            return "a body lies next to you";
        }

        internal static string Refill()
        {
            PlayerData player = MainGame.PlayerData;
            if (player == null)
            {
                return "not in a world";
            }
            // To its maximum (the player has energy, no health of their own).
            object system = player.GetResSystem("energy");
            object max = system == null ? null : CoopDiagnostics.GetMember(system, "Max");
            player.SetRes("energy", max == null ? 10000f : Convert.ToSingle(max));
            return "energy " + player.GetResInt("energy");
        }

        internal static string Sleep()
        {
            // The game's own sleep, without saving at the end.
            MainGame.PlayerData.energySystem.StartSleeping(null, null, true, true, SleepAnimType.None, 40f);
            return "sleeping";
        }

        internal static string SetWeather(string state)
        {
            WeatherSystem.Instance.SetWeatherState(state, false);
            return "weather " + state;
        }

        // ---------------------------------------------------------------- places and scenes

        internal static string TeleportTo(string wgoId)
        {
            bool started = PlayerController.Teleport(new WgoTeleportData(wgoId, "outdoor", string.Empty, false, null, true, 0.3f));
            return (started ? "going to " : "cannot go to ") + wgoId;
        }

        private static void StartScene(string[] scene)
        {
            string label = scene[0];
            if (scene[1].Length > 0)
            {
                // Where the scene happens first; its steps walk people around that place.
                Report(TeleportTo(scene[1]));
                scheduled.Add(new Scheduled { At = Time.unscaledTime + 8f, Label = label, Run = () => FireScene(scene) });
                return;
            }
            Report(label + ": " + FireScene(scene));
        }

        private static string FireScene(string[] scene)
        {
            GlobalScriptsManager.FireEvent(scene[2], scene[3], null);
            return "started " + scene[2];
        }

        /// <summary>A sermon, as the pray window starts one: the sermon chosen, then the script's event.</summary>
        internal static string StartSermon()
        {
            SermonDef sermon = GameBalance.Me.sermonDefs.FirstOrDefault();
            if (sermon == null || MainGame.PlayerData == null)
            {
                return "no sermon";
            }
            MainGame.PlayerData.currentSermon = new SermonResultData(sermon.id, string.Empty, 3, true, 1, 1);
            GlobalScriptsManager.FireEvent("System_Pray", "sermon_start", null);
            return "sermon " + sermon.id;
        }

        // ---------------------------------------------------------------- tests

        /// <summary>Tests: run one action by name, as its button would.</summary>
        internal static string RunForTest(string action)
        {
            switch (action)
            {
                case "yard": return BuildYard();
                case "kit": return GiveKit();
                case "body": return DropBody();
                case "refill": return Refill();
                case "sermon": return StartSermon();
                case "rain": return SetWeather("RainDayThunder");
                case "clear": return SetWeather("CleanWeather");
                default:
                    foreach (string[] place in Places)
                    {
                        if (string.Equals(place[0], action, StringComparison.OrdinalIgnoreCase)) return TeleportTo(place[1]);
                    }
                    return "no test action " + action;
            }
        }

        internal static string Describe()
        {
            return "test tools: enabled=" + Enabled + ", page=" + page + ", last=" + lastResult;
        }
    }
}
