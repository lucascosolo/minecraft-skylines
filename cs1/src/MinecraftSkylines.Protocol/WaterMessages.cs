using Skylines.Bridge;

namespace MinecraftSkylines.Protocol
{
    /// <summary>
    /// 0x01A0 WATER_SURFACE (host to guest, minor 10): the water surface and the ground under it (Minecraft y) over the
    /// block columns around the player; replaces the previous grid. Column (OriginX + dx, OriginZ + dz) is index dz * Size + dx.
    /// </summary>
    public sealed class WaterSurface
    {
        /// <summary>Largest grid side.</summary>
        public const ushort MaxSize = 128;

        /// <summary>Minecraft block coordinates of the grid's first column.</summary>
        public int OriginX, OriginZ;
        /// <summary>Columns per side, 0 to <see cref="MaxSize"/>.</summary>
        public ushort Size;
        /// <summary>Water surface per column; equal to <see cref="Bottom"/> where there is no water.</summary>
        public float[] Surface = new float[0];
        /// <summary>Ground under the water per column.</summary>
        public float[] Bottom = new float[0];

        /// <summary>Encodes the payload; both arrays must hold Size * Size values.</summary>
        public byte[] Encode()
        {
            var w = new PayloadWriter().I32(OriginX).I32(OriginZ).U16(Size);
            for (int i = 0; i < Size * Size; i++) w.F32(Surface[i]).F32(Bottom[i]);
            return w.ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is truncated or Size is above 128.</summary>
        public static WaterSurface Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new WaterSurface { OriginX = r.I32(), OriginZ = r.I32(), Size = r.U16() };
            if (m.Size > MaxSize) throw new ProtocolException("water grid size " + m.Size + " above " + MaxSize);
            int n = m.Size * m.Size;
            m.Surface = new float[n];
            m.Bottom = new float[n];
            for (int i = 0; i < n; i++)
            {
                m.Surface[i] = r.F32();
                m.Bottom[i] = r.F32();
            }
            return m;
        }
    }
}
