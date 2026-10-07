using Skylines.Bridge;

namespace MinecraftSkylines.Protocol
{
    /// <summary>One moving obstacle of <see cref="DynamicObstacles"/>: an upright box in Minecraft coordinates.</summary>
    public struct MovingObstacle
    {
        /// <summary><see cref="DynamicObstacles.Vehicle"/> or <see cref="DynamicObstacles.Citizen"/>.</summary>
        public byte Kind;
        /// <summary>The host's id of the object (CS1 vehicle or citizen instance index).</summary>
        public uint Id;
        /// <summary>Box centre, Minecraft coordinates.</summary>
        public float X, Y, Z;
        /// <summary>Minecraft yaw of the length axis, degrees.</summary>
        public float Yaw;
        /// <summary>Half extents along the width, vertical and length axes, metres.</summary>
        public float HalfWidth, HalfHeight, HalfLength;
        /// <summary>Velocity, m/s, Minecraft frame.</summary>
        public float VX, VY, VZ;
        /// <summary>Turn rate, Minecraft yaw degrees per second (SHAPED_OBSTACLES only).</summary>
        public float YawRate;
        /// <summary>
        /// Height along the length (SHAPED_OBSTACLES only): equal slices from the -length end, each its top above the box
        /// bottom in 1/255 of the box height; null or empty for none. At most 255.
        /// </summary>
        public byte[] Profile;
    }

    /// <summary>0x0170 DYNAMIC_OBSTACLES (host to guest, minor 7): every vehicle and citizen near the player; replaces the previous set.</summary>
    public sealed class DynamicObstacles
    {
        /// <summary>Kind of a vehicle (a trailer is its own vehicle).</summary>
        public const byte Vehicle = 1;
        /// <summary>Kind of a walking citizen.</summary>
        public const byte Citizen = 2;
        /// <summary>Kind of a parked vehicle (id indexes CS1's parked-vehicle table, a separate numbering).</summary>
        public const byte ParkedVehicle = 3;

        /// <summary>The obstacles; at most 65535.</summary>
        public MovingObstacle[] Obstacles = new MovingObstacle[0];

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            var w = new PayloadWriter().U16((ushort)Obstacles.Length);
            foreach (MovingObstacle o in Obstacles)
                w.U8(o.Kind).U32(o.Id).F32(o.X).F32(o.Y).F32(o.Z).F32(o.Yaw)
                    .F32(o.HalfWidth).F32(o.HalfHeight).F32(o.HalfLength).F32(o.VX).F32(o.VY).F32(o.VZ);
            return w.ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static DynamicObstacles Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var list = new MovingObstacle[r.U16()];
            for (int i = 0; i < list.Length; i++)
                list[i] = new MovingObstacle
                {
                    Kind = r.U8(), Id = r.U32(), X = r.F32(), Y = r.F32(), Z = r.F32(), Yaw = r.F32(),
                    HalfWidth = r.F32(), HalfHeight = r.F32(), HalfLength = r.F32(), VX = r.F32(), VY = r.F32(), VZ = r.F32(),
                };
            return new DynamicObstacles { Obstacles = list };
        }

        /// <summary>Encodes the SHAPED_OBSTACLES (0x0171, minor 17) payload: each obstacle as in 0x0170, then yaw rate and profile.</summary>
        public byte[] EncodeShaped()
        {
            var w = new PayloadWriter().U16((ushort)Obstacles.Length);
            foreach (MovingObstacle o in Obstacles)
            {
                int steps = o.Profile == null ? 0 : System.Math.Min(o.Profile.Length, 255);
                w.U8(o.Kind).U32(o.Id).F32(o.X).F32(o.Y).F32(o.Z).F32(o.Yaw)
                    .F32(o.HalfWidth).F32(o.HalfHeight).F32(o.HalfLength).F32(o.VX).F32(o.VY).F32(o.VZ).F32(o.YawRate).U8((byte)steps);
                for (int i = 0; i < steps; i++) w.U8(o.Profile[i]);
            }
            return w.ToArray();
        }

        /// <summary>Decodes a SHAPED_OBSTACLES payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static DynamicObstacles DecodeShaped(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var list = new MovingObstacle[r.U16()];
            for (int i = 0; i < list.Length; i++)
            {
                list[i] = new MovingObstacle
                {
                    Kind = r.U8(), Id = r.U32(), X = r.F32(), Y = r.F32(), Z = r.F32(), Yaw = r.F32(),
                    HalfWidth = r.F32(), HalfHeight = r.F32(), HalfLength = r.F32(), VX = r.F32(), VY = r.F32(), VZ = r.F32(),
                    YawRate = r.F32(),
                };
                list[i].Profile = r.Bytes(r.U8());
            }
            return new DynamicObstacles { Obstacles = list };
        }
    }
}
