using System;
using System.Reflection;
using BepInEx.Logging;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Keeps the host able to accept a new connection after a client vanished without saying
    /// goodbye (crash, kill, power loss, network cut).
    ///
    /// The defect is in the shipped Unity Transport, not in this mod. The host keeps sending to a
    /// vanished client until its 30-second timeout, and every datagram to the dead port comes back
    /// as an ICMP "port unreachable". Windows reports that as a failed receive completion, and
    /// <c>UDPNetworkInterface.ReceiveJob</c> skips failed completions without returning their
    /// buffer to the free list. Each one permanently removes a receive buffer from the 512-slot
    /// pool; once they are gone no receive is scheduled and the host is deaf. It still shows
    /// <c>0.0.0.0:8889</c> bound, which is why a relaunched client "could not reach" a host that
    /// looked healthy, while a clean quit (which stops the host sending at once) reconnected fine.
    ///
    /// <c>NetworkDriver.Bind</c> on the same endpoint is the transport's own recovery path: it
    /// closes the socket (cancelling every outstanding request), resets the free list and
    /// schedules a full set of receives. Connections are keyed by remote endpoint, so they survive
    /// it. The rebind runs from <c>Update</c>, a frame after the departure was seen, because the
    /// transport completes its jobs synchronously in EarlyUpdate/PostLateUpdate and has finished
    /// reading every event by then — rebinding inside the disconnect callback could free buffers
    /// that later events in the same batch still point into.
    /// </summary>
    internal static class CoopTransportGuard
    {
        private const float RebindDelaySeconds = 1f;

        private static ManualLogSource log;
        private static int lastRemoteCount = -1;
        private static float rebindAt = -1f;
        private static int rebinds;
        private static bool unsupportedLogged;

        internal static bool Enabled { get; set; } = true;

        internal static int Rebinds => rebinds;

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static void Tick()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsServer || !netcode.IsListening)
            {
                lastRemoteCount = -1;
                rebindAt = -1f;
                return;
            }

            int remote = 0;
            foreach (ulong id in netcode.ConnectedClientsIds)
            {
                if (id != netcode.LocalClientId)
                {
                    remote++;
                }
            }
            if (lastRemoteCount >= 0 && remote < lastRemoteCount)
            {
                rebindAt = Time.unscaledTime + RebindDelaySeconds;
            }
            lastRemoteCount = remote;

            if (rebindAt >= 0f && Time.unscaledTime >= rebindAt)
            {
                rebindAt = -1f;
                Rebind(netcode, "a client left");
            }
        }

        private static void Rebind(NetworkManager netcode, string reason)
        {
            try
            {
                object transport = netcode.NetworkConfig?.NetworkTransport;
                if (transport is SteamSocketsTransport)
                {
                    // Steam's sockets do not have Unity Transport's receive leak this works around.
                    return;
                }
                FieldInfo driverField = FindField(transport?.GetType(), "m_Driver");
                object driver = driverField?.GetValue(transport);
                Type driverType = driver?.GetType();
                MethodInfo getEndpoint = driverType?.GetMethod("GetLocalEndpoint", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                PropertyInfo created = driverType?.GetProperty("IsCreated", BindingFlags.Instance | BindingFlags.Public);
                if (getEndpoint == null || created == null)
                {
                    if (!unsupportedLogged)
                    {
                        unsupportedLogged = true;
                        log?.LogWarning("Transport guard: this Unity Transport does not expose the driver as expected; a crashed client may block reconnection until the host restarts hosting.");
                    }
                    return;
                }
                if (!(bool)created.GetValue(driver))
                {
                    return;
                }
                object endpoint = getEndpoint.Invoke(driver, null);
                MethodInfo bind = driverType.GetMethod("Bind", BindingFlags.Instance | BindingFlags.Public, null, new[] { endpoint.GetType() }, null);
                if (bind == null)
                {
                    return;
                }
                // NetworkDriver is a struct: the boxed copy shares every native container with the
                // transport's field, and Bind changes nothing that lives only in the copy.
                int result = Convert.ToInt32(bind.Invoke(driver, new[] { endpoint }));
                rebinds++;
                if (result == 0)
                {
                    log?.LogInfo($"Transport guard: re-bound the host socket to {endpoint} after {reason} (rebind {rebinds}).");
                }
                else
                {
                    log?.LogWarning($"Transport guard: re-binding {endpoint} after {reason} returned {result}.");
                }
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                log?.LogWarning("Transport guard: could not re-bind the host socket: " + inner.Message);
            }
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
    }
}
