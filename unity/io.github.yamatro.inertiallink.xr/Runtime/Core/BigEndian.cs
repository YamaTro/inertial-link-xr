using System;
using System.Runtime.InteropServices;

namespace YamaTro.InertialLink.Core
{
    internal static class BigEndian
    {
        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] public float Float;
            [FieldOffset(0)] public int Int;
        }

        public static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        public static uint ReadUInt32(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
                   ((uint)data[offset + 2] << 8) | data[offset + 3];
        }

        public static ulong ReadUInt64(byte[] data, int offset)
        {
            return ((ulong)ReadUInt32(data, offset) << 32) | ReadUInt32(data, offset + 4);
        }

        public static long ReadInt64(byte[] data, int offset) { return unchecked((long)ReadUInt64(data, offset)); }

        public static float ReadSingle(byte[] data, int offset)
        {
            var bits = new FloatBits { Int = unchecked((int)ReadUInt32(data, offset)) };
            return bits.Float;
        }

        public static void WriteUInt16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)(value >> 8);
            data[offset + 1] = (byte)value;
        }

        public static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }

        public static void WriteUInt64(byte[] data, int offset, ulong value)
        {
            WriteUInt32(data, offset, (uint)(value >> 32));
            WriteUInt32(data, offset + 4, (uint)value);
        }

        public static void WriteInt64(byte[] data, int offset, long value) { WriteUInt64(data, offset, unchecked((ulong)value)); }

        public static void WriteSingle(byte[] data, int offset, float value)
        {
            var bits = new FloatBits { Float = value };
            WriteUInt32(data, offset, unchecked((uint)bits.Int));
        }
    }
}
