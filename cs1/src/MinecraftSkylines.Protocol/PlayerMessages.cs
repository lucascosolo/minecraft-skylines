using System;
using Skylines.Bridge;

namespace MinecraftSkylines.Protocol
{
    /// <summary>0x0110 ENTER_PLAYER_MODE (host to guest).</summary>
    public sealed class EnterPlayerMode
    {
        /// <summary>Increments on every teleport the host requests.</summary>
        public uint TeleportSeq;
        /// <summary>Feet position, Minecraft coordinates.</summary>
        public double X, Y, Z;
        /// <summary>Initial look, degrees.</summary>
        public float Yaw, Pitch;
        /// <summary>The epoch the regions for this position carry.</summary>
        public uint CollisionEpoch;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().U32(TeleportSeq).F64(X).F64(Y).F64(Z).F32(Yaw).F32(Pitch).U32(CollisionEpoch).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static EnterPlayerMode Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new EnterPlayerMode();
            m.TeleportSeq = r.U32();
            m.X = r.F64();
            m.Y = r.F64();
            m.Z = r.F64();
            m.Yaw = r.F32();
            m.Pitch = r.F32();
            m.CollisionEpoch = r.U32();
            return m;
        }
    }

    /// <summary>0x0111 EXIT_PLAYER_MODE (host to guest).</summary>
    public sealed class ExitPlayerMode
    {
        /// <summary>Human-readable reason, e.g. <c>player pressed Esc</c>.</summary>
        public string Reason = "";

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().String(Reason).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static ExitPlayerMode Decode(byte[] payload)
        {
            var m = new ExitPlayerMode();
            m.Reason = new PayloadReader(payload).String();
            return m;
        }
    }

    /// <summary>Kinds of <see cref="InputEvent.Kind"/>.</summary>
    public static class InputKind
    {
        /// <summary>Key press or release; code is the GLFW key.</summary>
        public const byte Key = 1;
        /// <summary>Mouse button press or release; code is the GLFW button.</summary>
        public const byte MouseButton = 2;
        /// <summary>Scroll; code is wheel notches times 120.</summary>
        public const byte Scroll = 3;
        /// <summary>Text; code is a Unicode code point.</summary>
        public const byte Text = 4;
        /// <summary>Release every held key and button.</summary>
        public const byte ReleaseAll = 5;
        /// <summary>Cursor position (minor 3); code is <see cref="InputEvent.CursorCode"/>.</summary>
        public const byte Cursor = 6;
    }

    /// <summary>One input event of <see cref="Input"/>.</summary>
    public struct InputEvent
    {
        /// <summary>One of <see cref="InputKind"/>.</summary>
        public byte Kind;
        /// <summary>1 press, 0 release (0 for scroll, text, release all).</summary>
        public byte Action;
        /// <summary>Key, button, scroll amount or code point.</summary>
        public int Code;

        /// <summary>Creates an event.</summary>
        public InputEvent(byte kind, byte action, int code)
        {
            Kind = kind;
            Action = action;
            Code = code;
        }

        /// <summary>A cursor event at host pixel (x, y), origin top-left.</summary>
        public static InputEvent Cursor(int x, int y)
        {
            return new InputEvent(InputKind.Cursor, 0, CursorCode(x, y));
        }

        /// <summary>(x &lt;&lt; 16) | y, each masked to 16 bits, carried in an i32.</summary>
        public static int CursorCode(int x, int y)
        {
            return unchecked((int)(((uint)(x & 0xFFFF) << 16) | (uint)(y & 0xFFFF)));
        }

        /// <summary>The x of a cursor code.</summary>
        public static int CursorX(int code)
        {
            return (code >> 16) & 0xFFFF;
        }

        /// <summary>The y of a cursor code.</summary>
        public static int CursorY(int code)
        {
            return code & 0xFFFF;
        }
    }

    /// <summary>0x0112 INPUT (host to guest), once per host frame in player mode.</summary>
    public sealed class Input
    {
        /// <summary>Authoritative look, degrees.</summary>
        public float Yaw, Pitch;
        /// <summary>Events since the previous frame.</summary>
        public InputEvent[] Events = new InputEvent[0];

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            var w = new PayloadWriter().F32(Yaw).F32(Pitch).U16((ushort)Events.Length);
            for (int i = 0; i < Events.Length; i++)
                w.U8(Events[i].Kind).U8(Events[i].Action).I32(Events[i].Code);
            return w.ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static Input Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new Input();
            m.Yaw = r.F32();
            m.Pitch = r.F32();
            int n = r.U16();
            m.Events = new InputEvent[n];
            for (int i = 0; i < n; i++)
            {
                m.Events[i].Kind = r.U8();
                m.Events[i].Action = r.U8();
                m.Events[i].Code = r.I32();
            }
            return m;
        }
    }

    /// <summary>0x0113 COLLISION_REGION (host to guest): all collision triangles of one 16 x 16 column.</summary>
    public sealed class CollisionRegion
    {
        /// <summary>Bit 0 of a triangle's flags.</summary>
        public const ushort Terrain = 1 << 0;
        /// <summary>Bit 1 of a triangle's flags.</summary>
        public const ushort RoadSurface = 1 << 1;
        /// <summary>Bit 2 of a triangle's flags.</summary>
        public const ushort BridgeDeck = 1 << 2;
        /// <summary>Bit 3 of a triangle's flags.</summary>
        public const ushort Building = 1 << 3;
        /// <summary>Bit 9 (minor 13): terrain surface cut away over dug columns; never solid, sent so the guest's shadow ground keeps its height.</summary>
        public const ushort DugSurface = 1 << 9;

        private const int BytesPerTriangle = 9 * 4 + 2;

        /// <summary>Regions older than the latest reset are dropped.</summary>
        public uint Epoch;
        /// <summary>floor(x / 16).</summary>
        public int RegionX;
        /// <summary>floor(z / 16).</summary>
        public int RegionZ;
        /// <summary>ax ay az bx by bz cx cy cz per triangle, 9 floats each.</summary>
        public float[] Vertices = new float[0];
        /// <summary>One flags value per triangle.</summary>
        public ushort[] Flags = new ushort[0];

        /// <summary>Number of triangles.</summary>
        public int TriangleCount { get { return Flags.Length; } }

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            if (Vertices.Length != 9 * Flags.Length) throw new InvalidOperationException("Vertices must hold 9 floats per flags entry");
            var w = new PayloadWriter().U32(Epoch).I32(RegionX).I32(RegionZ).U32((uint)Flags.Length);
            for (int t = 0; t < Flags.Length; t++)
            {
                for (int i = 0; i < 9; i++) w.F32(Vertices[9 * t + i]);
                w.U16(Flags[t]);
            }
            return w.ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static CollisionRegion Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new CollisionRegion();
            m.Epoch = r.U32();
            m.RegionX = r.I32();
            m.RegionZ = r.I32();
            uint n = r.U32();
            if (n > (uint)(r.Remaining / BytesPerTriangle)) throw new ProtocolException("payload truncated");
            m.Vertices = new float[9 * (int)n];
            m.Flags = new ushort[n];
            for (int t = 0; t < (int)n; t++)
            {
                for (int i = 0; i < 9; i++) m.Vertices[9 * t + i] = r.F32();
                m.Flags[t] = r.U16();
            }
            return m;
        }
    }

    /// <summary>0x0114 COLLISION_RESET (host to guest).</summary>
    public sealed class CollisionReset
    {
        /// <summary>The guest drops all regions and accepts only regions with epoch at least this.</summary>
        public uint Epoch;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().U32(Epoch).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static CollisionReset Decode(byte[] payload)
        {
            var m = new CollisionReset();
            m.Epoch = new PayloadReader(payload).U32();
            return m;
        }
    }

    /// <summary>Bits of <see cref="PlayerState.Flags"/>.</summary>
    public static class PlayerStateFlags
    {
        /// <summary>A world is loaded.</summary>
        public const uint InWorld = 1u << 0;
        /// <summary>The player stands on ground.</summary>
        public const uint OnGround = 1u << 1;
        /// <summary>Sneaking.</summary>
        public const uint Sneaking = 1u << 2;
        /// <summary>Sprinting.</summary>
        public const uint Sprinting = 1u << 3;
        /// <summary>Swimming.</summary>
        public const uint Swimming = 1u << 4;
        /// <summary>Flying.</summary>
        public const uint Flying = 1u << 5;
        /// <summary>Dead.</summary>
        public const uint Dead = 1u << 6;
        /// <summary>Held, waiting for collision.</summary>
        public const uint Held = 1u << 7;
    }

    /// <summary>0x0120 PLAYER_STATE (guest to host), once per guest render frame.</summary>
    public sealed class PlayerState
    {
        /// <summary>Bits from <see cref="PlayerStateFlags"/>.</summary>
        public uint Flags;
        /// <summary>The last teleportSeq applied and released from hold; 0 before any.</summary>
        public uint TeleportAck;
        /// <summary>Feet, interpolated to this frame.</summary>
        public double X, Y, Z;
        /// <summary>Camera position.</summary>
        public double EyeX, EyeY, EyeZ;
        /// <summary>Look as applied by the guest, degrees.</summary>
        public float Yaw, Pitch;
        /// <summary>Effective vertical field of view, degrees.</summary>
        public float FovDeg;
        /// <summary>Increments every guest physics tick.</summary>
        public uint TickSeq;
        /// <summary>Feet at the previous tick.</summary>
        public double PrevX, PrevY, PrevZ;
        /// <summary>Feet at the latest tick.</summary>
        public double CurX, CurY, CurZ;
        /// <summary>Eye height above feet at the previous and latest tick.</summary>
        public float PrevEyeHeight, CurEyeHeight;
        /// <summary>0..1, how far this frame is between prev and cur.</summary>
        public float PartialTick;
        /// <summary>Milliseconds per tick.</summary>
        public float TickMs;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().U32(Flags).U32(TeleportAck)
                .F64(X).F64(Y).F64(Z).F64(EyeX).F64(EyeY).F64(EyeZ)
                .F32(Yaw).F32(Pitch).F32(FovDeg).U32(TickSeq)
                .F64(PrevX).F64(PrevY).F64(PrevZ).F64(CurX).F64(CurY).F64(CurZ)
                .F32(PrevEyeHeight).F32(CurEyeHeight).F32(PartialTick).F32(TickMs).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static PlayerState Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new PlayerState();
            m.Flags = r.U32();
            m.TeleportAck = r.U32();
            m.X = r.F64(); m.Y = r.F64(); m.Z = r.F64();
            m.EyeX = r.F64(); m.EyeY = r.F64(); m.EyeZ = r.F64();
            m.Yaw = r.F32(); m.Pitch = r.F32(); m.FovDeg = r.F32();
            m.TickSeq = r.U32();
            m.PrevX = r.F64(); m.PrevY = r.F64(); m.PrevZ = r.F64();
            m.CurX = r.F64(); m.CurY = r.F64(); m.CurZ = r.F64();
            m.PrevEyeHeight = r.F32(); m.CurEyeHeight = r.F32();
            m.PartialTick = r.F32(); m.TickMs = r.F32();
            return m;
        }
    }
}
