using System;
using System.Reflection;

namespace GK2Coop
{
    /// <summary>
    /// The serializer the game writes its save files with (Odin, binary), for state that must
    /// cross the network whole. <c>LazySerializer</c>, used before, skips fields that are only
    /// <c>[SerializeReference]</c> — among them every item's properties (a body's zombie skin and
    /// start items, a tool's wear) — so an item arrived as a plainer copy of itself.
    ///
    /// Output starts with a four-byte marker so a reader can tell it from older
    /// <c>LazySerializer</c> data (player profiles stored by earlier versions).
    /// </summary>
    internal static class CoopGameSerializer
    {
        private static readonly byte[] Marker = { (byte)'O', (byte)'D', (byte)'N', 1 };
        private static MethodInfo serializeValue;
        private static MethodInfo deserializeValue;
        private static object binaryFormat;

        internal static byte[] Serialize(object value)
        {
            Resolve();
            byte[] body = (byte[])serializeValue.MakeGenericMethod(value.GetType()).Invoke(null, new[] { value, binaryFormat, null });
            byte[] result = new byte[Marker.Length + body.Length];
            Buffer.BlockCopy(Marker, 0, result, 0, Marker.Length);
            Buffer.BlockCopy(body, 0, result, Marker.Length, body.Length);
            return result;
        }

        internal static bool IsGameFormat(byte[] raw)
        {
            if (raw == null || raw.Length < Marker.Length)
            {
                return false;
            }
            for (int i = 0; i < Marker.Length; i++)
            {
                if (raw[i] != Marker[i])
                {
                    return false;
                }
            }
            return true;
        }

        internal static object Deserialize(Type type, byte[] raw)
        {
            if (!IsGameFormat(raw))
            {
                throw new FormatException("not data written by CoopGameSerializer");
            }
            Resolve();
            byte[] body = new byte[raw.Length - Marker.Length];
            Buffer.BlockCopy(raw, Marker.Length, body, 0, body.Length);
            return deserializeValue.MakeGenericMethod(type).Invoke(null, new[] { body, binaryFormat, null });
        }

        private static void Resolve()
        {
            if (serializeValue != null && deserializeValue != null)
            {
                return;
            }
            Type utility = Plugin.FindGameType("Sirenix.Serialization.SerializationUtility");
            Type format = Plugin.FindGameType("Sirenix.Serialization.DataFormat");
            if (utility == null || format == null)
            {
                throw new MissingMemberException("Sirenix.Serialization.SerializationUtility");
            }
            binaryFormat = Enum.Parse(format, "Binary");
            foreach (MethodInfo method in utility.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (!method.IsGenericMethodDefinition)
                {
                    continue;
                }
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 3 || parameters[1].ParameterType != format)
                {
                    continue;
                }
                if (method.Name == "SerializeValue" && method.ReturnType == typeof(byte[]) && parameters[2].ParameterType.Name == "SerializationContext")
                {
                    serializeValue = method;
                }
                else if (method.Name == "DeserializeValue" && parameters[0].ParameterType == typeof(byte[]) && parameters[2].ParameterType.Name == "DeserializationContext")
                {
                    deserializeValue = method;
                }
            }
            if (serializeValue == null || deserializeValue == null)
            {
                throw new MissingMethodException("SerializationUtility.SerializeValue/DeserializeValue");
            }
        }
    }
}
