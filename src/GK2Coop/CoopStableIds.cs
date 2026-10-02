using System;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Gives a replaced world object the same id on every machine.
    ///
    /// When a craft or a death turns one object into another — a finished crop, a tree into a
    /// stump, a broken thing into a repaired one — the game calls <c>WorldData.ReplaceWgoData</c>,
    /// which builds the new object with <c>new WgoData(...)</c> and therefore a fresh random id.
    /// Each machine that performs the replacement invents a different id for the same object, and
    /// every later sync that names objects by id (deaths, drops, containers, stations) no longer
    /// finds it. Measured with the garden beds of the day-18 save
    /// (<c>artifacts/garden-local-0.28.0</c>).
    ///
    /// The replacement's id is instead derived from the replaced object's id and the new object
    /// type, so both machines arrive at the same id whenever they perform the same replacement.
    /// Object types that register a forced navigation hole in their constructor keep their random
    /// id: that registration is keyed by the id, and changing it afterwards would orphan it.
    /// Active only while a co-op session is running.
    /// </summary>
    internal static class CoopStableIds
    {
        private static ManualLogSource log;
        private static string pendingId;
        private static FieldInfo uniqueIdField;
        private static Type sguidType;
        private static int assigned;
        private static int skipped;
        private static int logsLeft = 5;

        internal static bool Enabled { get; set; } = true;

        internal static string Describe()
        {
            return $"stable ids: assigned={assigned}, kept random={skipped}";
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
                Type worldData = Plugin.FindGameType("WorldData");
                Type wgoData = Plugin.FindGameType("WgoData");
                sguidType = Plugin.FindGameType("SGuid");
                uniqueIdField = AccessTools.Field(wgoData, "uniqueId");
                MethodInfo replace = AccessTools.Method(worldData, "ReplaceWgoData", new[] { wgoData, typeof(string) });
                ConstructorInfo ctor = AccessTools.Constructor(wgoData, new[] { typeof(string), typeof(Vector3), typeof(string), typeof(string) });
                if (replace == null || ctor == null || uniqueIdField == null || sguidType == null)
                {
                    log.LogWarning("Stable ids: WorldData.ReplaceWgoData or the WgoData constructor not found; replaced objects keep random ids.");
                    Enabled = false;
                    return;
                }
                harmony.Patch(replace,
                    prefix: new HarmonyMethod(typeof(CoopStableIds).GetMethod(nameof(ReplacePrefix), BindingFlags.Static | BindingFlags.NonPublic)),
                    finalizer: new HarmonyMethod(typeof(CoopStableIds).GetMethod(nameof(ReplaceFinalizer), BindingFlags.Static | BindingFlags.NonPublic)));
                harmony.Patch(ctor,
                    postfix: new HarmonyMethod(typeof(CoopStableIds).GetMethod(nameof(ConstructorPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                log.LogInfo("Stable ids: replaced world objects get the same id on every machine.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Stable ids disabled: " + ex.Message);
            }
        }

        private static void ReplacePrefix(object wgoData, string newWgoId)
        {
            pendingId = null;
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening || wgoData == null)
            {
                return;
            }
            string oldId = Convert.ToString(CoopDiagnostics.GetMember(CoopDiagnostics.GetMember(wgoData, "UniqueId"), "Id"));
            if (!string.IsNullOrEmpty(oldId))
            {
                pendingId = Derive(oldId, newWgoId).ToString();
            }
        }

        private static Exception ReplaceFinalizer(Exception __exception)
        {
            pendingId = null;
            return __exception;
        }

        /// <summary>The first WgoData built during a replacement is the replacement.</summary>
        private static void ConstructorPostfix(object __instance)
        {
            string id = pendingId;
            if (id == null)
            {
                return;
            }
            pendingId = null;
            try
            {
                object definition = CoopDiagnostics.GetMember(__instance, "Definition");
                if (definition != null && Convert.ToString(CoopDiagnostics.GetMember(definition, "forceSetNavigationHoleType")) == "SpawnHoleForce")
                {
                    skipped++;
                    return;
                }
                uniqueIdField.SetValue(__instance, Activator.CreateInstance(sguidType, new Guid(id)));
                assigned++;
                if (logsLeft > 0)
                {
                    logsLeft--;
                    log.LogInfo("Stable ids: " + CoopDiagnostics.GetMember(__instance, "id") + " replacing an object got id " + id + ".");
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Stable ids: could not assign a replacement id: " + ex.Message);
            }
        }

        /// <summary>A name-based GUID: the same replaced object and new type always give the same id.</summary>
        internal static Guid Derive(string oldId, string newWgoId)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("GK2Coop.replace|" + oldId + "|" + newWgoId));
                // RFC 4122 version 3 and variant bits, so it reads as an ordinary GUID.
                hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
                hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
                return new Guid(hash);
            }
        }
    }
}
