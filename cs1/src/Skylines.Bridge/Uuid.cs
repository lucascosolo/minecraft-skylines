using System;

namespace Skylines.Bridge
{
    /// <summary>Conversion between <see cref="Guid"/> and the wire's RFC 4122 byte order.</summary>
    /// <remarks>
    /// <c>Guid.ToByteArray()</c> stores the first three groups little-endian; the wire uses the textual
    /// (big-endian) order, so those three groups are reversed. The swap is its own inverse.
    /// </remarks>
    public static class Uuid
    {
        /// <summary>Returns the 16 bytes of <paramref name="guid"/> in RFC 4122 order.</summary>
        public static byte[] ToBytes(Guid guid)
        {
            return Swap(guid.ToByteArray());
        }

        /// <summary>Builds a <see cref="Guid"/> from 16 bytes in RFC 4122 order.</summary>
        public static Guid FromBytes(byte[] rfc4122)
        {
            if (rfc4122 == null || rfc4122.Length != 16) throw new ArgumentException("uuid needs 16 bytes");
            return new Guid(Swap(rfc4122));
        }

        private static byte[] Swap(byte[] b)
        {
            var r = (byte[])b.Clone();
            Array.Reverse(r, 0, 4);
            Array.Reverse(r, 4, 2);
            Array.Reverse(r, 6, 2);
            return r;
        }
    }
}
