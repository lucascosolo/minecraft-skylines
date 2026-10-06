using System;
using Skylines.Bridge;

namespace MinecraftSkylines.Protocol
{
    /// <summary>One part of an <see cref="EntityModel"/>: its parent and its quads.</summary>
    public sealed class EntityModelPart
    {
        /// <summary>Index of the parent part (always an earlier one) or <see cref="EntityModel.NoParent"/>.</summary>
        public ushort Parent;
        /// <summary>23 floats per quad: 4 x (x, y, z, u, v) in model units, then the normal nx, ny, nz.</summary>
        public float[] Quads = new float[0];
    }

    /// <summary>0x01E0 ENTITY_MODEL (guest to host, minor 14): a tree of parts with textured quads.</summary>
    public sealed class EntityModel
    {
        /// <summary>Most parts a model may have.</summary>
        public const int MaxParts = 1024;
        /// <summary>Most quads a part may have.</summary>
        public const int MaxQuads = 4096;
        /// <summary>Floats of one quad.</summary>
        public const int FloatsPerQuad = 23;
        /// <summary>Parent value of a root part.</summary>
        public const ushort NoParent = 0xFFFF;

        /// <summary>The guest's id for this model.</summary>
        public uint ModelId;
        /// <summary>Diagnostic name.</summary>
        public string Name = "";
        /// <summary>The parts; a parent comes before its children.</summary>
        public EntityModelPart[] Parts = new EntityModelPart[0];

        /// <summary>Encodes the payload; throws <see cref="ArgumentException"/> when a limit is exceeded.</summary>
        public byte[] Encode()
        {
            if (Parts.Length > MaxParts) throw new ArgumentException(Parts.Length + " parts, above " + MaxParts);
            PayloadWriter w = new PayloadWriter().U32(ModelId).String(Name).U16((ushort)Parts.Length);
            for (int i = 0; i < Parts.Length; i++)
            {
                EntityModelPart p = Parts[i];
                if (p.Quads.Length % FloatsPerQuad != 0) throw new ArgumentException("quads are not a multiple of " + FloatsPerQuad + " floats");
                int quads = p.Quads.Length / FloatsPerQuad;
                if (quads > MaxQuads) throw new ArgumentException(quads + " quads, above " + MaxQuads);
                w.U16(p.Parent).U16((ushort)quads);
                for (int j = 0; j < p.Quads.Length; j++) w.F32(p.Quads[j]);
            }
            return w.ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed or over a limit.</summary>
        public static EntityModel Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new EntityModel { ModelId = r.U32(), Name = r.String() };
            int n = r.U16();
            if (n > MaxParts) throw new ProtocolException("part count " + n + " is above " + MaxParts);
            m.Parts = new EntityModelPart[n];
            for (int i = 0; i < n; i++)
            {
                ushort parent = r.U16();
                if (parent != NoParent && parent >= i) throw new ProtocolException("part " + i + " has parent " + parent + ", which does not come before it");
                int quads = r.U16();
                if (quads > MaxQuads) throw new ProtocolException("quad count " + quads + " is above " + MaxQuads);
                var part = new EntityModelPart { Parent = parent, Quads = new float[quads * FloatsPerQuad] };
                for (int j = 0; j < part.Quads.Length; j++) part.Quads[j] = r.F32();
                m.Parts[i] = part;
            }
            return m;
        }
    }

    /// <summary>0x01E1 ENTITY_TEXTURE (guest to host, minor 14): an encoded image.</summary>
    public sealed class EntityTexture
    {
        /// <summary><see cref="Format"/> value of a PNG.</summary>
        public const byte FormatPng = 1;
        /// <summary>Largest image a message may carry.</summary>
        public const int MaxLength = 4194304;

        /// <summary>The guest's id for this texture.</summary>
        public uint TextureId;
        /// <summary>Pixels.</summary>
        public uint Width, Height;
        /// <summary>1 = PNG.</summary>
        public byte Format;
        /// <summary>The encoded image, top row first.</summary>
        public byte[] Data = new byte[0];

        /// <summary>Encodes the payload; throws <see cref="ArgumentException"/> above <see cref="MaxLength"/> bytes.</summary>
        public byte[] Encode()
        {
            if (Data.Length > MaxLength) throw new ArgumentException(Data.Length + " bytes, above " + MaxLength);
            return new PayloadWriter().U32(TextureId).U32(Width).U32(Height).U8(Format).U32((uint)Data.Length).Bytes(Data).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed or over a limit.</summary>
        public static EntityTexture Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var t = new EntityTexture { TextureId = r.U32(), Width = r.U32(), Height = r.U32(), Format = r.U8() };
            uint n = r.U32();
            if (n > MaxLength) throw new ProtocolException("texture length " + n + " is above " + MaxLength);
            t.Data = r.Bytes((int)n);
            return t;
        }
    }

    /// <summary>The pose of one model part in an <see cref="EntityDraw"/>.</summary>
    public struct EntityPartPose
    {
        /// <summary>Bit 0 of <see cref="Flags"/>: the part and its children are not drawn.</summary>
        public const byte FlagHidden = 1;
        /// <summary>Bit 1 of <see cref="Flags"/>: the part's own quads are not drawn.</summary>
        public const byte FlagSkip = 2;

        /// <summary>Offset in model units.</summary>
        public float Px, Py, Pz;
        /// <summary>Rotation in radians.</summary>
        public float XRot, YRot, ZRot;
        /// <summary>Scale.</summary>
        public float XScale, YScale, ZScale;
        /// <summary>See <see cref="FlagHidden"/> and <see cref="FlagSkip"/>.</summary>
        public byte Flags;
    }

    /// <summary>One model drawn for an entity.</summary>
    public sealed class EntityDraw
    {
        /// <summary>From ENTITY_MODEL.</summary>
        public uint ModelId;
        /// <summary>From ENTITY_TEXTURE.</summary>
        public uint TextureId;
        /// <summary>RGBA8 as bytes R, G, B, A, multiplied with the texture.</summary>
        public uint Color;
        /// <summary>Row-major 3 x 4 affine transform, model space (metres) to the Minecraft frame relative to the entity.</summary>
        public float[] Matrix = new float[12];
        /// <summary>One pose per model part.</summary>
        public EntityPartPose[] Parts = new EntityPartPose[0];
    }

    /// <summary>One entity of <see cref="EntityStates"/>.</summary>
    public sealed class EntityState
    {
        /// <summary>The guest's entity id.</summary>
        public uint EntityId;
        /// <summary>Position, Minecraft frame.</summary>
        public float X, Y, Z;
        /// <summary>Degrees, Minecraft convention; informational.</summary>
        public float BodyYaw, HeadYaw, Pitch;
        /// <summary>The models drawn for it.</summary>
        public EntityDraw[] Draws = new EntityDraw[0];
    }

    /// <summary>0x01E2 ENTITY_STATES (guest to host, minor 14): the complete set of entities with their pose.</summary>
    public sealed class EntityStates
    {
        /// <summary>Most entities a message may carry.</summary>
        public const int MaxEntities = 2048;
        /// <summary>Most draws an entity may have.</summary>
        public const int MaxDraws = 16;
        /// <summary>Most part poses a draw may have.</summary>
        public const int MaxParts = 1024;

        /// <summary>Increases by one per message.</summary>
        public uint Seq;
        /// <summary>The entities.</summary>
        public EntityState[] Entities = new EntityState[0];

        /// <summary>Encodes the payload; throws <see cref="ArgumentException"/> when a limit is exceeded or a matrix is not 12 floats.</summary>
        public byte[] Encode()
        {
            if (Entities.Length > MaxEntities) throw new ArgumentException(Entities.Length + " entities, above " + MaxEntities);
            PayloadWriter w = new PayloadWriter().U32(Seq).U16((ushort)Entities.Length);
            for (int i = 0; i < Entities.Length; i++)
            {
                EntityState e = Entities[i];
                if (e.Draws.Length > MaxDraws) throw new ArgumentException(e.Draws.Length + " draws, above " + MaxDraws);
                w.U32(e.EntityId).F32(e.X).F32(e.Y).F32(e.Z).F32(e.BodyYaw).F32(e.HeadYaw).F32(e.Pitch).U8((byte)e.Draws.Length);
                for (int j = 0; j < e.Draws.Length; j++)
                {
                    EntityDraw d = e.Draws[j];
                    if (d.Matrix.Length != 12) throw new ArgumentException("matrix has " + d.Matrix.Length + " floats, expected 12");
                    if (d.Parts.Length > MaxParts) throw new ArgumentException(d.Parts.Length + " parts, above " + MaxParts);
                    w.U32(d.ModelId).U32(d.TextureId).U32(d.Color);
                    for (int k = 0; k < 12; k++) w.F32(d.Matrix[k]);
                    w.U16((ushort)d.Parts.Length);
                    for (int k = 0; k < d.Parts.Length; k++)
                    {
                        EntityPartPose p = d.Parts[k];
                        w.F32(p.Px).F32(p.Py).F32(p.Pz).F32(p.XRot).F32(p.YRot).F32(p.ZRot).F32(p.XScale).F32(p.YScale).F32(p.ZScale).U8(p.Flags);
                    }
                }
            }
            return w.ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed or over a limit.</summary>
        public static EntityStates Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new EntityStates { Seq = r.U32() };
            int n = r.U16();
            if (n > MaxEntities) throw new ProtocolException("entity count " + n + " is above " + MaxEntities);
            m.Entities = new EntityState[n];
            for (int i = 0; i < n; i++)
            {
                var e = new EntityState { EntityId = r.U32(), X = r.F32(), Y = r.F32(), Z = r.F32(), BodyYaw = r.F32(), HeadYaw = r.F32(), Pitch = r.F32() };
                int dc = r.U8();
                if (dc > MaxDraws) throw new ProtocolException("draw count " + dc + " is above " + MaxDraws);
                e.Draws = new EntityDraw[dc];
                for (int j = 0; j < dc; j++)
                {
                    var d = new EntityDraw { ModelId = r.U32(), TextureId = r.U32(), Color = r.U32() };
                    for (int k = 0; k < 12; k++) d.Matrix[k] = r.F32();
                    int pc = r.U16();
                    if (pc > MaxParts) throw new ProtocolException("part count " + pc + " is above " + MaxParts);
                    d.Parts = new EntityPartPose[pc];
                    for (int k = 0; k < pc; k++)
                        d.Parts[k] = new EntityPartPose { Px = r.F32(), Py = r.F32(), Pz = r.F32(), XRot = r.F32(), YRot = r.F32(), ZRot = r.F32(), XScale = r.F32(), YScale = r.F32(), ZScale = r.F32(), Flags = r.U8() };
                    e.Draws[j] = d;
                }
                m.Entities[i] = e;
            }
            return m;
        }
    }
}
