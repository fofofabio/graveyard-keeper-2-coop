using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using LazyBearTechnology;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// A fighting level is an island: the game teleports the fighter in when the fight starts and
    /// back out to the level's return point when it ends, and there is no way to walk out. Someone
    /// who is inside without fighting — a joiner who fought, dropped out and was put back where they
    /// were; a player placed there any other way — was shut in. In a co-op session, whoever stands in
    /// a fighting level's area without a fight of their own is taken to that level's return point
    /// (the one the game uses after a fight), and told why.
    /// </summary>
    internal static class CoopArenaRescue
    {
        private const string DefaultReturnPoint = "RT_fightback_player_spawn";

        private static ManualLogSource log;
        private static Type levelType;
        private static PropertyInfo boundsProperty;
        private static PropertyInfo returnProperty;
        private static float nextCheck;
        private static UnityEngine.Object[] levels;
        private static bool levelsDue = true;
        private static string insideLevel;
        private static int rescued;

        internal static bool Enabled { get; set; } = true;

        internal static string Describe()
        {
            string areas = string.Empty;
            try
            {
                foreach (KeyValuePair<string, Bounds> area in Areas())
                {
                    areas += " " + area.Key + "=" + area.Value.center.ToString("F0") + "±" + area.Value.extents.ToString("F0");
                }
            }
            catch (Exception ex)
            {
                areas = " (" + ex.Message + ")";
            }
            return "arena rescue: rescued=" + rescued + (insideLevel != null ? ", inside " + insideLevel : string.Empty) + ", areas:" + (areas.Length == 0 ? " none" : areas);
        }

        /// <summary>
        /// Each fighting level's island: the boxes of its own zone colliders (FightingLevel's
        /// zoneDefineColliders, the same the game builds the fight's navigation from). They are off
        /// between fights, when a collider reports no bounds, so the box is computed from its stored
        /// center and size. (The level's world zone "wz_…" is the approach in town, not the island.)
        /// </summary>
        private static IEnumerable<KeyValuePair<string, Bounds>> Areas()
        {
            if (levelType == null)
            {
                yield break;
            }
            FieldInfo collidersField = AccessTools.Field(levelType, "zoneDefineColliders");
            Type boxType = Type.GetType("UnityEngine.BoxCollider, UnityEngine.PhysicsModule");
            if (collidersField == null || boxType == null)
            {
                yield break;
            }
            PropertyInfo centerOf = boxType.GetProperty("center");
            PropertyInfo sizeOf = boxType.GetProperty("size");
            foreach (UnityEngine.Object found in Levels())
            {
                if (!(found is Component level) || level == null || !level.gameObject.scene.IsValid() ||
                    !(collidersField.GetValue(level) is System.Collections.IEnumerable colliders))
                {
                    continue;
                }
                bool any = false;
                Bounds area = default(Bounds);
                foreach (object entry in colliders)
                {
                    if (!(entry is Component box) || box == null || !boxType.IsInstanceOfType(box))
                    {
                        continue;
                    }
                    Vector3 center = (Vector3)centerOf.GetValue(box, null);
                    Vector3 half = (Vector3)sizeOf.GetValue(box, null) * 0.5f;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 local = center + new Vector3((corner & 1) == 0 ? -half.x : half.x, (corner & 2) == 0 ? -half.y : half.y, (corner & 4) == 0 ? -half.z : half.z);
                        Vector3 world = box.transform.TransformPoint(local);
                        if (!any)
                        {
                            area = new Bounds(world, Vector3.zero);
                            any = true;
                        }
                        else
                        {
                            area.Encapsulate(world);
                        }
                    }
                }
                if (any && area.size.sqrMagnitude > 0.01f)
                {
                    yield return new KeyValuePair<string, Bounds>(Convert.ToString(CoopDiagnostics.GetMember(level, "id")), area);
                }
            }
        }

        private static string FindArea(Vector3 at, out string returnPoint)
        {
            returnPoint = null;
            foreach (KeyValuePair<string, Bounds> area in Areas())
            {
                Bounds b = area.Value;
                if (at.x < b.min.x || at.x > b.max.x || at.z < b.min.z || at.z > b.max.z)
                {
                    continue;
                }
                foreach (UnityEngine.Object found in Levels())
                {
                    if (found is Component level && level != null && level.gameObject.scene.IsValid() && Convert.ToString(CoopDiagnostics.GetMember(level, "id")) == area.Key)
                    {
                        returnPoint = returnProperty == null ? null : returnProperty.GetValue(level, null) as string;
                        break;
                    }
                }
                return area.Key;
            }
            return null;
        }

        /// <summary>
        /// The fighting levels, found again after each scene load: a search of all memory, 10 ms
        /// in a day-18 world, which this check made every 2 s.
        /// </summary>
        private static UnityEngine.Object[] Levels()
        {
            if (levelsDue || levels == null)
            {
                levelsDue = false;
                levels = Resources.FindObjectsOfTypeAll(levelType);
            }
            return levels;
        }

        internal static void Init(ManualLogSource source)
        {
            log = source;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => levelsDue = true;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += scene => levelsDue = true;
            levelType = Plugin.FindGameType("FightingLevel");
            boundsProperty = levelType == null ? null : AccessTools.Property(levelType, "LevelBounds");
            returnProperty = levelType == null ? null : AccessTools.Property(levelType, "FightbackGdPointId");

        }

        /// <summary>From the plugin's Update.</summary>
        internal static void Tick()
        {
            if (!Enabled || Time.unscaledTime < nextCheck)
            {
                return;
            }
            nextCheck = Time.unscaledTime + 2f;
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsListening || MainGame.PlayerController == null ||
                LazySingleton<FightingGameController>.Instance.CurrentFightState != FightState.Disabled)
            {
                insideLevel = null;
                return;
            }
            try
            {
                Component body = MainGame.PlayerController.PhysicalBody as Component;
                if (body == null)
                {
                    return;
                }
                Vector3 at = body.transform.position;
                string level = FindArea(at, out string returnPoint);
                if (level == null)
                {
                    insideLevel = null;
                    return;
                }
                // Inside at two checks in a row: not the instant of being teleported in to start a fight.
                if (insideLevel != level)
                {
                    insideLevel = level;
                    return;
                }
                insideLevel = null;
                string point = string.IsNullOrEmpty(returnPoint) ? DefaultReturnPoint : returnPoint;
                PlayerController.Teleport(new GDPointTeleportData(point, isTag: false, "outdoor", "", null, donNotFade: false));
                rescued++;
                CoopStatus.Announce(L.T("You were brought back from the fight area."), false, 6f);
                log.LogInfo("Arena rescue: this player stood in " + level + " without a fight of their own; brought back to " + point + ".");
            }
            catch (Exception ex)
            {
                log.LogWarning("Arena rescue: " + ex.Message);
                nextCheck = Time.unscaledTime + 10f;
            }
        }
    }
}
