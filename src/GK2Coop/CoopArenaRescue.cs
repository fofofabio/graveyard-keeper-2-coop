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
        // Where the player was at the last check, and a fighting level they walked into (never
        // brought back from: what the player can walk into, they can walk out of).
        private static Vector3? lastAt;
        private static float lastAtTime;
        private static string walkedInto;
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

        /// <summary>
        /// The fighting level whose zone the point is in: inside one of the level's own zone boxes,
        /// in the box's own frame, height included (3 units above and below for the player's feet).
        /// Until 0.65.6 the check was one rectangle around all of a level's boxes, flat: in a late
        /// world it took in ordinary ground next to an island, and players walking there were
        /// "brought back from the fight area" (Workshop report, 5 October 2026).
        /// </summary>
        private static float Flat(Vector3 a, Vector3 b)
        {
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }

        private static string FindArea(Vector3 at, out string returnPoint)
        {
            returnPoint = null;
            if (levelType == null)
            {
                return null;
            }
            FieldInfo collidersField = AccessTools.Field(levelType, "zoneDefineColliders");
            Type boxType = Type.GetType("UnityEngine.BoxCollider, UnityEngine.PhysicsModule");
            if (collidersField == null || boxType == null)
            {
                return null;
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
                foreach (object entry in colliders)
                {
                    if (!(entry is Component box) || box == null || !boxType.IsInstanceOfType(box))
                    {
                        continue;
                    }
                    Vector3 local = box.transform.InverseTransformPoint(at) - (Vector3)centerOf.GetValue(box, null);
                    Vector3 half = (Vector3)sizeOf.GetValue(box, null) * 0.5f;
                    Vector3 scale = box.transform.lossyScale;
                    float margin = Mathf.Abs(scale.y) > 0.0001f ? 3f / Mathf.Abs(scale.y) : 3f;
                    if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z && Mathf.Abs(local.y) <= half.y + margin)
                    {
                        returnPoint = returnProperty == null ? null : returnProperty.GetValue(level, null) as string;
                        return Convert.ToString(CoopDiagnostics.GetMember(level, "id"));
                    }
                }
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
                // A fight or no session in between: the next place is not reached on foot.
                lastAt = null;
                walkedInto = null;
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
                Vector3? before = lastAt;
                float elapsed = Time.unscaledTime - lastAtTime;
                lastAt = at;
                lastAtTime = Time.unscaledTime;
                string level = FindArea(at, out string returnPoint);
                if (level == null)
                {
                    insideLevel = null;
                    walkedInto = null;
                    return;
                }
                if (walkedInto == level)
                {
                    return;
                }
                // Arrived on foot from just outside (a walk covers a few units a second; a teleport
                // onto an island is a jump): an area a late world lets the player walk into. The
                // checks are 2 s apart, more when the game is slow, so the reach grows with the time.
                // Across the ground only: a body that drops or is lifted at a check (seen 21 units
                // below the ground for a moment) has not jumped across the map.
                if (insideLevel == null && before.HasValue && FindArea(before.Value, out _) == null &&
                    Flat(before.Value, at) < Mathf.Clamp(7f * elapsed, 8f, 20f))
                {
                    walkedInto = level;
                    log.LogInfo("Arena rescue: this player walked into " + level + "; not brought back from it.");
                    return;
                }
                // Inside at two checks in a row: not the instant of being teleported in to start a fight.
                if (insideLevel != level)
                {
                    if (CoopDiagnostics.Detailed)
                    {
                        log.LogInfo("Arena rescue: in " + level + " at " + at.ToString("F1") + "; the check before: " +
                                    (before.HasValue ? before.Value.ToString("F1") + " in " + (FindArea(before.Value, out _) ?? "none") +
                                     ", " + Flat(before.Value, at).ToString("F1") + " units across" : "none") +
                                    ", " + elapsed.ToString("F1") + " s ago, inside before=" + (insideLevel ?? "none") + ".");
                    }
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
