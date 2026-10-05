using System;
using System.IO;
using System.Text;

namespace Skylines.Bridge
{
    /// <summary>Builds a little-endian payload from the SKBR primitive encodings.</summary>
    public sealed class PayloadWriter
    {
        internal static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly MemoryStream _ms = new MemoryStream();

        /// <summary>Appends a u8.</summary>
        public PayloadWriter U8(byte v) { _ms.WriteByte(v); return this; }

        /// <summary>Appends a u16.</summary>
        public PayloadWriter U16(ushort v) { return Raw(v, 2); }

        /// <summary>Appends a u32.</summary>
        public PayloadWriter U32(uint v) { return Raw(v, 4); }

        /// <summary>Appends a u64.</summary>
        public PayloadWriter U64(ulong v) { return Raw(v, 8); }

        /// <summary>Appends an i32.</summary>
        public PayloadWriter I32(int v) { return Raw(unchecked((uint)v), 4); }

        /// <summary>Appends an i64.</summary>
        public PayloadWriter I64(long v) { return Raw(unchecked((ulong)v), 8); }

        /// <summary>Appends an IEEE 754 f32.</summary>
        public PayloadWriter F32(float v) { return Ordered(BitConverter.GetBytes(v)); }

        /// <summary>Appends an IEEE 754 f64.</summary>
        public PayloadWriter F64(double v) { return Ordered(BitConverter.GetBytes(v)); }

        /// <summary>Appends a bool as 0 or 1.</summary>
        public PayloadWriter Bool(bool v) { return U8(v ? (byte)1 : (byte)0); }

        /// <summary>Appends a string: u16 byte length then UTF-8. A null string is written empty.</summary>
        public PayloadWriter String(string s)
        {
            byte[] b = StrictUtf8.GetBytes(s ?? "");
            if (b.Length > ushort.MaxValue) throw new ArgumentException("string longer than 65535 UTF-8 bytes");
            U16((ushort)b.Length);
            _ms.Write(b, 0, b.Length);
            return this;
        }

        /// <summary>Appends a uuid in RFC 4122 byte order.</summary>
        public PayloadWriter Uuid(Guid g)
        {
            byte[] b = Bridge.Uuid.ToBytes(g);
            _ms.Write(b, 0, 16);
            return this;
        }

        /// <summary>Appends raw bytes (no length prefix).</summary>
        public PayloadWriter Bytes(byte[] b)
        {
            _ms.Write(b, 0, b.Length);
            return this;
        }

        /// <summary>Returns the bytes written so far.</summary>
        public byte[] ToArray() { return _ms.ToArray(); }

        private PayloadWriter Raw(ulong v, int n)
        {
            for (int i = 0; i < n; i++) _ms.WriteByte((byte)(v >> (8 * i)));
            return this;
        }

        private PayloadWriter Ordered(byte[] b)
        {
            if (!BitConverter.IsLittleEndian) Array.Reverse(b);
            _ms.Write(b, 0, b.Length);
            return this;
        }
    }
}
