using System;
using System.Text;

namespace Skylines.Bridge
{
    /// <summary>
    /// Reads SKBR primitives from a payload. Reading past the end, a bool other than 0 or 1,
    /// or a string that is not valid UTF-8 throws <see cref="ProtocolException"/>.
    /// Bytes left after the last field are ignored by design (fields may be appended in a major version).
    /// </summary>
    public sealed class PayloadReader
    {
        private readonly byte[] _data;
        private int _pos;

        /// <summary>Creates a reader over <paramref name="data"/>.</summary>
        public PayloadReader(byte[] data)
        {
            if (data == null) throw new ArgumentNullException("data");
            _data = data;
        }

        /// <summary>Number of unread bytes.</summary>
        public int Remaining { get { return _data.Length - _pos; } }

        /// <summary>Reads a u8.</summary>
        public byte U8() { return Take(1)[0]; }

        /// <summary>Reads a u16.</summary>
        public ushort U16() { return (ushort)Le(2); }

        /// <summary>Reads a u32.</summary>
        public uint U32() { return (uint)Le(4); }

        /// <summary>Reads a u64.</summary>
        public ulong U64() { return Le(8); }

        /// <summary>Reads an i32.</summary>
        public int I32() { return unchecked((int)(uint)Le(4)); }

        /// <summary>Reads an i64.</summary>
        public long I64() { return unchecked((long)Le(8)); }

        /// <summary>Reads an IEEE 754 f32.</summary>
        public float F32() { return BitConverter.ToSingle(Ordered(4), 0); }

        /// <summary>Reads an IEEE 754 f64.</summary>
        public double F64() { return BitConverter.ToDouble(Ordered(8), 0); }

        /// <summary>Reads a bool; anything but 0 or 1 is a protocol error.</summary>
        public bool Bool()
        {
            byte v = U8();
            if (v > 1) throw new ProtocolException("bool out of range: " + v);
            return v == 1;
        }

        /// <summary>Reads a u16-length-prefixed UTF-8 string.</summary>
        public string String()
        {
            int n = U16();
            if (n > Remaining) throw new ProtocolException("string length " + n + " past payload end");
            try
            {
                string s = PayloadWriter.StrictUtf8.GetString(_data, _pos, n);
                _pos += n;
                return s;
            }
            catch (DecoderFallbackException)
            {
                throw new ProtocolException("string is not valid UTF-8");
            }
        }

        /// <summary>Reads a uuid in RFC 4122 byte order.</summary>
        public Guid Uuid() { return Bridge.Uuid.FromBytes(Take(16)); }

        private ulong Le(int n)
        {
            byte[] b = Take(n);
            ulong v = 0;
            for (int i = n - 1; i >= 0; i--) v = (v << 8) | b[i];
            return v;
        }

        private byte[] Ordered(int n)
        {
            byte[] b = Take(n);
            if (!BitConverter.IsLittleEndian) Array.Reverse(b);
            return b;
        }

        private byte[] Take(int n)
        {
            if (n > Remaining) throw new ProtocolException("payload ends before its last field");
            var r = new byte[n];
            Array.Copy(_data, _pos, r, 0, n);
            _pos += n;
            return r;
        }
    }
}
