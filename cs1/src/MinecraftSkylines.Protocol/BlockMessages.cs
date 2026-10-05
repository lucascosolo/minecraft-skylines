using System;
using Skylines.Bridge;

namespace MinecraftSkylines.Protocol
{
    /// <summary>0x0130 BLOCK_ATLAS (guest to host): the encoded block texture atlas.</summary>
    public sealed class BlockAtlas
    {
        /// <summary>The only format in 1.2.</summary>
        public const byte FormatPng = 1;

        /// <summary>Pixels.</summary>
        public uint Width, Height;
        /// <summary>One of the format constants.</summary>
        public byte Format = FormatPng;
        /// <summary>The encoded image, top row first.</summary>
        public byte[] Data = new byte[0];

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().U32(Width).U32(Height).U8(Format).U32((uint)Data.Length).Bytes(Data).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static BlockAtlas Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new BlockAtlas();
            m.Width = r.U32();
            m.Height = r.U32();
            m.Format = r.U8();
            uint n = r.U32();
            if (n > (uint)r.Remaining) throw new ProtocolException("payload truncated");
            m.Data = r.Bytes((int)n);
            return m;
        }
    }

    /// <summary>0x0131 ATLAS_REGION (guest to host): one animated sprite frame.</summary>
    public sealed class AtlasRegion
    {
        /// <summary>Pixels in the atlas.</summary>
        public uint X, Y, Width, Height;
        /// <summary>RGBA8, top row first, Width x Height x 4 bytes.</summary>
        public byte[] Rgba = new byte[0];

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            if ((ulong)Rgba.Length != (ulong)Width * Height * 4) throw new InvalidOperationException("Rgba must hold Width x Height x 4 bytes");
            return new PayloadWriter().U32(X).U32(Y).U32(Width).U32(Height).Bytes(Rgba).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static AtlasRegion Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new AtlasRegion();
            m.X = r.U32();
            m.Y = r.U32();
            m.Width = r.U32();
            m.Height = r.U32();
            ulong n = (ulong)m.Width * m.Height * 4;
            if (n > (ulong)r.Remaining) throw new ProtocolException("payload truncated");
            m.Rgba = r.Bytes((int)n);
            return m;
        }
    }

    /// <summary>
    /// 0x0132 SECTION_MESH (guest to host): the triangle list of one 16x16x16 section. Vertices stay in their
    /// 32-byte wire form (LE f32 x y z u v, u32 color light flags) in <see cref="VertexData"/>.
    /// </summary>
    public sealed class SectionMesh
    {
        /// <summary>Bytes per vertex on the wire.</summary>
        public const int BytesPerVertex = 32;

        /// <summary>Section coordinates, floor(block / 16).</summary>
        public int Sx, Sy, Sz;
        /// <summary>Raw vertices, <see cref="BytesPerVertex"/> each.</summary>
        public byte[] VertexData = new byte[0];

        /// <summary>Number of vertices (a multiple of 3; 0 = the section is empty).</summary>
        public int VertexCount { get { return VertexData.Length / BytesPerVertex; } }

        /// <summary>Position relative to the section origin, Minecraft coordinates.</summary>
        public float X(int i) { return F(i, 0); }
        /// <summary>See <see cref="X"/>.</summary>
        public float Y(int i) { return F(i, 4); }
        /// <summary>See <see cref="X"/>.</summary>
        public float Z(int i) { return F(i, 8); }
        /// <summary>Atlas UV, origin top-left.</summary>
        public float U(int i) { return F(i, 12); }
        /// <summary>See <see cref="U"/>.</summary>
        public float V(int i) { return F(i, 16); }
        /// <summary>RGBA8 as bytes R,G,B,A (R in the low byte).</summary>
        public uint Color(int i) { return Le(VertexData, i * BytesPerVertex + 20); }
        /// <summary>Low byte block light, next byte sky light.</summary>
        public uint Light(int i) { return Le(VertexData, i * BytesPerVertex + 24); }
        /// <summary>Bit 0 cutout, bit 1 translucent.</summary>
        public uint Flags(int i) { return Le(VertexData, i * BytesPerVertex + 28); }

        /// <summary>Writes vertex <paramref name="i"/> into <paramref name="dst"/> in wire form.</summary>
        public static void WriteVertex(byte[] dst, int i, float x, float y, float z, float u, float v, uint color, uint light, uint flags)
        {
            int o = i * BytesPerVertex;
            PutLe(dst, o, FloatBits(x));
            PutLe(dst, o + 4, FloatBits(y));
            PutLe(dst, o + 8, FloatBits(z));
            PutLe(dst, o + 12, FloatBits(u));
            PutLe(dst, o + 16, FloatBits(v));
            PutLe(dst, o + 20, color);
            PutLe(dst, o + 24, light);
            PutLe(dst, o + 28, flags);
        }

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            if (VertexData.Length % (3 * BytesPerVertex) != 0) throw new InvalidOperationException("VertexData must hold whole triangles");
            return new PayloadWriter().I32(Sx).I32(Sy).I32(Sz).U32((uint)VertexCount).Bytes(VertexData).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static SectionMesh Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new SectionMesh();
            m.Sx = r.I32();
            m.Sy = r.I32();
            m.Sz = r.I32();
            uint n = r.U32();
            if (n % 3 != 0) throw new ProtocolException("vertex count not a multiple of 3");
            if (n > (uint)(r.Remaining / BytesPerVertex)) throw new ProtocolException("payload truncated");
            m.VertexData = r.Bytes((int)n * BytesPerVertex);
            return m;
        }

        private float F(int i, int offset)
        {
            return BitConverter.ToSingle(BitConverter.GetBytes(Le(VertexData, i * BytesPerVertex + offset)), 0);
        }

        private static uint FloatBits(float f)
        {
            return BitConverter.ToUInt32(BitConverter.GetBytes(f), 0);
        }

        private static uint Le(byte[] b, int o)
        {
            return (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
        }

        private static void PutLe(byte[] b, int o, uint v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
        }
    }

    /// <summary>0x01F0 DEBUG_COMMAND (host to guest): a server command for automated in-game tests (dev world only).</summary>
    public sealed class DebugCommand
    {
        /// <summary>The command without the leading slash.</summary>
        public string Command = "";

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().String(Command).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static DebugCommand Decode(byte[] payload)
        {
            var m = new DebugCommand();
            m.Command = new PayloadReader(payload).String();
            return m;
        }
    }
}
