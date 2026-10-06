using System;
using Skylines.Core.Voxels;

namespace Skylines.Core.Saves
{
    /// <summary>
    /// A versioned, checksummed container for an opaque blob kept in a city's save:
    /// u8 version, u32 LE length, payload, u32 LE CRC-32 of everything before it.
    /// </summary>
    public static class BlobRecord
    {
        /// <summary>The record version written.</summary>
        public const byte Version = 1;
        private const int Overhead = 1 + 4 + 4;

        /// <summary>Wraps <paramref name="payload"/> in a record.</summary>
        public static byte[] Encode(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException("payload");
            var record = new byte[payload.Length + Overhead];
            record[0] = Version;
            PutU32(record, 1, (uint)payload.Length);
            Buffer.BlockCopy(payload, 0, record, 5, payload.Length);
            PutU32(record, record.Length - 4, VoxelEditRecord.Crc32(record, 0, record.Length - 4));
            return record;
        }

        /// <summary>Returns the payload of a record; throws <see cref="FormatException"/> if it is not a valid version-1 record.</summary>
        public static byte[] Decode(byte[] record)
        {
            if (record == null) throw new FormatException("record is null");
            if (record.Length < Overhead) throw new FormatException("record truncated (" + record.Length + " bytes)");
            if (record[0] != Version) throw new FormatException("unknown record version " + record[0]);
            uint length = GetU32(record, 1);
            if (length != (uint)(record.Length - Overhead)) throw new FormatException("record length " + length + " does not match its " + record.Length + " bytes");
            int body = record.Length - 4;
            if (VoxelEditRecord.Crc32(record, 0, body) != GetU32(record, body)) throw new FormatException("CRC mismatch");
            var payload = new byte[length];
            Buffer.BlockCopy(record, 5, payload, 0, payload.Length);
            return payload;
        }

        private static void PutU32(byte[] b, int at, uint v)
        {
            b[at] = (byte)v;
            b[at + 1] = (byte)(v >> 8);
            b[at + 2] = (byte)(v >> 16);
            b[at + 3] = (byte)(v >> 24);
        }

        private static uint GetU32(byte[] b, int at)
        {
            return (uint)(b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24);
        }
    }
}
