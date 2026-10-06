using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Skylines.Bridge;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class ProtocolTests
    {
        private static JsonDocument Load(string name)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string p = Path.Combine(dir.FullName, "protocol", "vectors", name);
                if (File.Exists(p)) return JsonDocument.Parse(File.ReadAllText(p));
                dir = dir.Parent;
            }
            throw new FileNotFoundException("protocol/vectors/" + name);
        }

        private static byte[] Hex(string h)
        {
            var b = new byte[h.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(2 * i, 2), 16);
            return b;
        }

        public static IEnumerable<object[]> AppVectors()
        {
            using (JsonDocument d = Load("frames.json"))
                foreach (JsonElement v in d.RootElement.GetProperty("valid").EnumerateArray())
                    if (v.GetProperty("type").GetInt32() >= 0x100)
                        yield return new object[] { v.GetProperty("name").GetString() };
        }

        [Theory]
        [MemberData(nameof(AppVectors))]
        public void StatusVectorsDecodeAndEncode(string name)
        {
            JsonElement v = default;
            using (JsonDocument d = Load("frames.json"))
                foreach (JsonElement e in d.RootElement.GetProperty("valid").EnumerateArray())
                    if (e.GetProperty("name").GetString() == name) v = e.Clone();
            string hex = v.GetProperty("hex").GetString();
            JsonElement f = v.GetProperty("fields");
            Frame frame = FrameCodec.Decode(Hex(hex));
            byte[] payload;
            if (frame.Type == AppProtocol.HostStatusType)
            {
                HostStatus s = HostStatus.Decode(frame.Payload);
                Assert.Equal(f.GetProperty("flags").GetUInt32(), s.Flags);
                Assert.Equal(f.GetProperty("cityName").GetString(), s.CityName);
                Assert.Equal(new Guid(f.GetProperty("saveId").GetString()), s.SaveId);
                Assert.Equal(f.GetProperty("gameVersion").GetString(), s.GameVersion);
                payload = s.Encode();
            }
            else if (frame.Type == AppProtocol.GuestStatusType)
            {
                GuestStatus s = GuestStatus.Decode(frame.Payload);
                Assert.Equal(f.GetProperty("flags").GetUInt32(), s.Flags);
                Assert.Equal(f.GetProperty("worldName").GetString(), s.WorldName);
                Assert.Equal(new Guid(f.GetProperty("pairedSaveId").GetString()), s.PairedSaveId);
                payload = s.Encode();
            }
            else
            {
                payload = CheckPlayerMessage(frame, f);
            }
            Assert.Equal(hex, Convert.ToHexString(FrameCodec.Encode(frame.Type, payload)).ToLowerInvariant());
        }

        private static byte[] CheckPlayerMessage(Frame frame, JsonElement f)
        {
            switch (frame.Type)
            {
                case AppProtocol.EnterPlayerModeType:
                    {
                        EnterPlayerMode m = EnterPlayerMode.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("teleportSeq").GetUInt32(), m.TeleportSeq);
                        Assert.Equal(f.GetProperty("x").GetDouble(), m.X);
                        Assert.Equal(f.GetProperty("y").GetDouble(), m.Y);
                        Assert.Equal(f.GetProperty("z").GetDouble(), m.Z);
                        Assert.Equal(f.GetProperty("yaw").GetSingle(), m.Yaw);
                        Assert.Equal(f.GetProperty("pitch").GetSingle(), m.Pitch);
                        Assert.Equal(f.GetProperty("collisionEpoch").GetUInt32(), m.CollisionEpoch);
                        return m.Encode();
                    }
                case AppProtocol.ExitPlayerModeType:
                    {
                        ExitPlayerMode m = ExitPlayerMode.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("reason").GetString(), m.Reason);
                        return m.Encode();
                    }
                case AppProtocol.InputType:
                    {
                        Input m = Input.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("yaw").GetSingle(), m.Yaw);
                        Assert.Equal(f.GetProperty("pitch").GetSingle(), m.Pitch);
                        JsonElement events = f.GetProperty("events");
                        Assert.Equal(events.GetArrayLength(), m.Events.Length);
                        int i = 0;
                        foreach (JsonElement e in events.EnumerateArray())
                        {
                            Assert.Equal(e.GetProperty("kind").GetByte(), m.Events[i].Kind);
                            Assert.Equal(e.GetProperty("action").GetByte(), m.Events[i].Action);
                            Assert.Equal(e.GetProperty("code").GetInt32(), m.Events[i].Code);
                            if (e.GetProperty("kind").GetByte() == InputKind.Cursor)
                            {
                                int cx = e.GetProperty("cursorX").GetInt32(), cy = e.GetProperty("cursorY").GetInt32();
                                int code = e.GetProperty("code").GetInt32();
                                Assert.Equal(code, InputEvent.CursorCode(cx, cy));
                                Assert.Equal(cx & 0xFFFF, InputEvent.CursorX(code));
                                Assert.Equal(cy & 0xFFFF, InputEvent.CursorY(code));
                            }
                            i++;
                        }
                        return m.Encode();
                    }
                case AppProtocol.CollisionRegionType:
                    {
                        CollisionRegion m = CollisionRegion.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("epoch").GetUInt32(), m.Epoch);
                        Assert.Equal(f.GetProperty("regionX").GetInt32(), m.RegionX);
                        Assert.Equal(f.GetProperty("regionZ").GetInt32(), m.RegionZ);
                        JsonElement tris = f.GetProperty("tris");
                        Assert.Equal(tris.GetArrayLength(), m.TriangleCount);
                        Assert.Equal(9 * m.TriangleCount, m.Vertices.Length);
                        int t = 0;
                        foreach (JsonElement tri in tris.EnumerateArray())
                        {
                            int k = 0;
                            foreach (JsonElement v in tri.GetProperty("v").EnumerateArray())
                                Assert.Equal(v.GetSingle(), m.Vertices[9 * t + k++]);
                            Assert.Equal(9, k);
                            Assert.Equal(tri.GetProperty("flags").GetUInt16(), m.Flags[t]);
                            t++;
                        }
                        return m.Encode();
                    }
                case AppProtocol.CollisionResetType:
                    {
                        CollisionReset m = CollisionReset.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("epoch").GetUInt32(), m.Epoch);
                        return m.Encode();
                    }
                case AppProtocol.BlockAtlasType:
                    {
                        BlockAtlas m = BlockAtlas.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("width").GetUInt32(), m.Width);
                        Assert.Equal(f.GetProperty("height").GetUInt32(), m.Height);
                        Assert.Equal(f.GetProperty("format").GetByte(), m.Format);
                        Assert.Equal(BlockAtlas.FormatPng, m.Format);
                        Assert.Equal(Hex(f.GetProperty("dataHex").GetString()), m.Data);
                        return m.Encode();
                    }
                case AppProtocol.AtlasRegionType:
                    {
                        AtlasRegion m = AtlasRegion.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("x").GetUInt32(), m.X);
                        Assert.Equal(f.GetProperty("y").GetUInt32(), m.Y);
                        Assert.Equal(f.GetProperty("width").GetUInt32(), m.Width);
                        Assert.Equal(f.GetProperty("height").GetUInt32(), m.Height);
                        Assert.Equal(Hex(f.GetProperty("rgbaHex").GetString()), m.Rgba);
                        return m.Encode();
                    }
                case AppProtocol.SectionMeshType:
                    {
                        SectionMesh m = SectionMesh.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("sx").GetInt32(), m.Sx);
                        Assert.Equal(f.GetProperty("sy").GetInt32(), m.Sy);
                        Assert.Equal(f.GetProperty("sz").GetInt32(), m.Sz);
                        JsonElement verts = f.GetProperty("vertices");
                        Assert.Equal(verts.GetArrayLength(), m.VertexCount);
                        Assert.Equal(SectionMesh.BytesPerVertex * m.VertexCount, m.VertexData.Length);
                        int i = 0;
                        foreach (JsonElement v in verts.EnumerateArray())
                        {
                            Assert.Equal(v.GetProperty("x").GetSingle(), m.X(i));
                            Assert.Equal(v.GetProperty("y").GetSingle(), m.Y(i));
                            Assert.Equal(v.GetProperty("z").GetSingle(), m.Z(i));
                            Assert.Equal(v.GetProperty("u").GetSingle(), m.U(i));
                            Assert.Equal(v.GetProperty("v").GetSingle(), m.V(i));
                            Assert.Equal(v.GetProperty("color").GetUInt32(), m.Color(i));
                            Assert.Equal(v.GetProperty("light").GetUInt32(), m.Light(i));
                            Assert.Equal(v.GetProperty("flags").GetUInt32(), m.Flags(i));
                            i++;
                        }
                        return m.Encode();
                    }
                case AppProtocol.SectionsClearType:
                    Assert.Empty(frame.Payload);
                    return new byte[0];
                case AppProtocol.OverlayStopType:
                    Assert.Empty(frame.Payload);
                    return new byte[0];
                case AppProtocol.ViewportType:
                    {
                        Viewport m = Viewport.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("width").GetUInt32(), m.Width);
                        Assert.Equal(f.GetProperty("height").GetUInt32(), m.Height);
                        Assert.Equal(f.GetProperty("uiScale").GetSingle(), m.UiScale);
                        return m.Encode();
                    }
                case AppProtocol.OverlayOfferType:
                    {
                        OverlayOffer m = OverlayOffer.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("path").GetString(), m.Path);
                        Assert.Equal(f.GetProperty("maxWidth").GetUInt32(), m.MaxWidth);
                        Assert.Equal(f.GetProperty("maxHeight").GetUInt32(), m.MaxHeight);
                        Assert.Equal(f.GetProperty("slotCount").GetUInt32(), m.SlotCount);
                        Assert.Equal(ulong.Parse(f.GetProperty("generation").GetString()), m.Generation);
                        return m.Encode();
                    }
                case AppProtocol.BlockSelectionType:
                    {
                        BlockSelection m = BlockSelection.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("visible").GetBoolean(), m.Visible);
                        Assert.Equal(f.GetProperty("minX").GetSingle(), m.MinX);
                        Assert.Equal(f.GetProperty("minY").GetSingle(), m.MinY);
                        Assert.Equal(f.GetProperty("minZ").GetSingle(), m.MinZ);
                        Assert.Equal(f.GetProperty("maxX").GetSingle(), m.MaxX);
                        Assert.Equal(f.GetProperty("maxY").GetSingle(), m.MaxY);
                        Assert.Equal(f.GetProperty("maxZ").GetSingle(), m.MaxZ);
                        Assert.Equal(f.GetProperty("kind").GetByte(), m.Kind);
                        return m.Encode();
                    }
                case AppProtocol.DebugCommandType:
                    {
                        DebugCommand m = DebugCommand.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("command").GetString(), m.Command);
                        return m.Encode();
                    }
                case AppProtocol.CityOpenType:
                    {
                        CityOpen m = CityOpen.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("openSeq").GetUInt32(), m.OpenSeq);
                        Assert.Equal(new Guid(f.GetProperty("saveId").GetString()), m.SaveId);
                        Assert.Equal(f.GetProperty("cityName").GetString(), m.CityName);
                        Assert.Equal(f.GetProperty("editCount").GetUInt32(), m.EditCount);
                        return m.Encode();
                    }
                case AppProtocol.BlockEditsType:
                    {
                        BlockEdits m = BlockEdits.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("openSeq").GetUInt32(), m.OpenSeq);
                        Assert.Equal(f.GetProperty("flags").GetByte(), m.Flags);
                        JsonElement pal = f.GetProperty("palette");
                        Assert.Equal(pal.GetArrayLength(), m.Palette.Length);
                        int pi = 0;
                        foreach (JsonElement e in pal.EnumerateArray()) Assert.Equal(e.GetString(), m.Palette[pi++]);
                        JsonElement edits = f.GetProperty("edits");
                        Assert.Equal(edits.GetArrayLength(), m.Edits.Length);
                        int ei = 0;
                        foreach (JsonElement e in edits.EnumerateArray())
                        {
                            Assert.Equal(e.GetProperty("x").GetInt32(), m.Edits[ei].X);
                            Assert.Equal(e.GetProperty("y").GetInt32(), m.Edits[ei].Y);
                            Assert.Equal(e.GetProperty("z").GetInt32(), m.Edits[ei].Z);
                            Assert.Equal(e.GetProperty("state").GetUInt16(), m.Edits[ei].State);
                            ei++;
                        }
                        return m.Encode();
                    }
                case AppProtocol.WorldTimeType:
                    {
                        WorldTime m = WorldTime.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("hour").GetSingle(), m.Hour);
                        Assert.Equal(f.GetProperty("day").GetUInt32(), m.Day);
                        Assert.Equal(f.GetProperty("flags").GetByte(), m.Flags);
                        Assert.Equal(f.GetProperty("minecraftDayTicks").GetInt32(), WorldTime.MinecraftDayTicks(m.Hour));
                        return m.Encode();
                    }
                case AppProtocol.DynamicObstaclesType:
                    {
                        DynamicObstacles m = DynamicObstacles.Decode(frame.Payload);
                        JsonElement obs = f.GetProperty("obstacles");
                        Assert.Equal(obs.GetArrayLength(), m.Obstacles.Length);
                        int oi = 0;
                        foreach (JsonElement o in obs.EnumerateArray())
                        {
                            MovingObstacle a = m.Obstacles[oi++];
                            Assert.Equal(o.GetProperty("kind").GetByte(), a.Kind);
                            Assert.Equal(o.GetProperty("id").GetUInt32(), a.Id);
                            Assert.Equal(o.GetProperty("x").GetSingle(), a.X);
                            Assert.Equal(o.GetProperty("y").GetSingle(), a.Y);
                            Assert.Equal(o.GetProperty("z").GetSingle(), a.Z);
                            Assert.Equal(o.GetProperty("yaw").GetSingle(), a.Yaw);
                            Assert.Equal(o.GetProperty("halfWidth").GetSingle(), a.HalfWidth);
                            Assert.Equal(o.GetProperty("halfHeight").GetSingle(), a.HalfHeight);
                            Assert.Equal(o.GetProperty("halfLength").GetSingle(), a.HalfLength);
                            Assert.Equal(o.GetProperty("vx").GetSingle(), a.VX);
                            Assert.Equal(o.GetProperty("vy").GetSingle(), a.VY);
                            Assert.Equal(o.GetProperty("vz").GetSingle(), a.VZ);
                        }
                        return m.Encode();
                    }
                case AppProtocol.LightSourcesType:
                    {
                        LightSources m = LightSources.Decode(frame.Payload);
                        JsonElement lights = f.GetProperty("lights");
                        Assert.Equal(lights.GetArrayLength(), m.Lights.Length);
                        int li = 0;
                        foreach (JsonElement l in lights.EnumerateArray())
                        {
                            LightSource a = m.Lights[li++];
                            Assert.Equal(l.GetProperty("x").GetInt32(), a.X);
                            Assert.Equal(l.GetProperty("y").GetInt32(), a.Y);
                            Assert.Equal(l.GetProperty("z").GetInt32(), a.Z);
                            Assert.Equal(l.GetProperty("level").GetByte(), a.Level);
                        }
                        return m.Encode();
                    }
                case AppProtocol.SkyStateType:
                    {
                        SkyState m = SkyState.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("flags").GetByte(), m.Flags);
                        AssertFloats(f.GetProperty("skyColor"), m.SkyColor);
                        AssertFloats(f.GetProperty("fogColor"), m.FogColor);
                        AssertFloats(f.GetProperty("sunriseColor"), m.SunriseColor);
                        Assert.Equal(f.GetProperty("starBrightness").GetSingle(), m.StarBrightness);
                        Assert.Equal(f.GetProperty("rainLevel").GetSingle(), m.RainLevel);
                        Assert.Equal(f.GetProperty("moonPhase").GetByte(), m.MoonPhase);
                        AssertFloats(f.GetProperty("cloudColor"), m.CloudColor);
                        Assert.Equal(f.GetProperty("cloudHeight").GetSingle(), m.CloudHeight);
                        Assert.Equal(f.GetProperty("cloudOffset").GetSingle(), m.CloudOffset);
                        Assert.Equal(f.GetProperty("cloudSpeed").GetSingle(), m.CloudSpeed);
                        return m.Encode();
                    }
                case AppProtocol.SkyTexturesType:
                    {
                        SkyTextures m = SkyTextures.Decode(frame.Payload);
                        JsonElement textures = f.GetProperty("textures");
                        Assert.Equal(textures.GetArrayLength(), m.Textures.Length);
                        int ti = 0;
                        foreach (JsonElement t in textures.EnumerateArray())
                        {
                            SkyTexture a = m.Textures[ti++];
                            Assert.Equal(t.GetProperty("kind").GetByte(), a.Kind);
                            Assert.Equal(t.GetProperty("phase").GetByte(), a.Phase);
                            Assert.Equal(t.GetProperty("format").GetByte(), a.Format);
                            Assert.Equal(Hex(t.GetProperty("dataHex").GetString()), a.Data);
                        }
                        return m.Encode();
                    }
                case AppProtocol.WaterSurfaceType:
                    {
                        WaterSurface m = WaterSurface.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("originX").GetInt32(), m.OriginX);
                        Assert.Equal(f.GetProperty("originZ").GetInt32(), m.OriginZ);
                        Assert.Equal(f.GetProperty("size").GetUInt16(), m.Size);
                        AssertFloats(f.GetProperty("surface"), m.Surface);
                        AssertFloats(f.GetProperty("bottom"), m.Bottom);
                        return m.Encode();
                    }
                case AppProtocol.CityCloseType:
                    {
                        CityClose m = CityClose.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("openSeq").GetUInt32(), m.OpenSeq);
                        return m.Encode();
                    }
                case AppProtocol.EditSyncType:
                case AppProtocol.EditSyncAckType:
                    {
                        EditSync m = EditSync.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("openSeq").GetUInt32(), m.OpenSeq);
                        Assert.Equal(f.GetProperty("token").GetUInt32(), m.Token);
                        return m.Encode();
                    }
                case AppProtocol.CityStateType:
                    {
                        CityStateUpdate m = CityStateUpdate.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("openSeq").GetUInt32(), m.OpenSeq);
                        Assert.Equal(f.GetProperty("state").GetByte(), m.State);
                        Assert.Equal(f.GetProperty("appliedCount").GetUInt32(), m.AppliedCount);
                        return m.Encode();
                    }
                default:
                    {
                        Assert.Equal(AppProtocol.PlayerStateType, frame.Type);
                        PlayerState m = PlayerState.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("flags").GetUInt32(), m.Flags);
                        Assert.Equal(f.GetProperty("teleportAck").GetUInt32(), m.TeleportAck);
                        Assert.Equal(f.GetProperty("x").GetDouble(), m.X);
                        Assert.Equal(f.GetProperty("y").GetDouble(), m.Y);
                        Assert.Equal(f.GetProperty("z").GetDouble(), m.Z);
                        Assert.Equal(f.GetProperty("eyeX").GetDouble(), m.EyeX);
                        Assert.Equal(f.GetProperty("eyeY").GetDouble(), m.EyeY);
                        Assert.Equal(f.GetProperty("eyeZ").GetDouble(), m.EyeZ);
                        Assert.Equal(f.GetProperty("yaw").GetSingle(), m.Yaw);
                        Assert.Equal(f.GetProperty("pitch").GetSingle(), m.Pitch);
                        Assert.Equal(f.GetProperty("fovDeg").GetSingle(), m.FovDeg);
                        Assert.Equal(f.GetProperty("tickSeq").GetUInt32(), m.TickSeq);
                        Assert.Equal(f.GetProperty("prevX").GetDouble(), m.PrevX);
                        Assert.Equal(f.GetProperty("prevY").GetDouble(), m.PrevY);
                        Assert.Equal(f.GetProperty("prevZ").GetDouble(), m.PrevZ);
                        Assert.Equal(f.GetProperty("curX").GetDouble(), m.CurX);
                        Assert.Equal(f.GetProperty("curY").GetDouble(), m.CurY);
                        Assert.Equal(f.GetProperty("curZ").GetDouble(), m.CurZ);
                        Assert.Equal(f.GetProperty("prevEyeHeight").GetSingle(), m.PrevEyeHeight);
                        Assert.Equal(f.GetProperty("curEyeHeight").GetSingle(), m.CurEyeHeight);
                        Assert.Equal(f.GetProperty("partialTick").GetSingle(), m.PartialTick);
                        Assert.Equal(f.GetProperty("tickMs").GetSingle(), m.TickMs);
                        return m.Encode();
                    }
            }
        }

        [Fact]
        public void TruncatedPlayerMessagesAreProtocolErrors()
        {
            Assert.Throws<ProtocolException>(() => EnterPlayerMode.Decode(new byte[10]));
            Assert.Throws<ProtocolException>(() => Input.Decode(new byte[] { 0, 0, 0, 0, 0, 0, 1, 0, 1, 1 }));
            Assert.Throws<ProtocolException>(() => PlayerState.Decode(new byte[20]));
            // triCount claims 2^32-1 triangles with no data behind it: must fail without allocating.
            Assert.Throws<ProtocolException>(() => CollisionRegion.Decode(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255, 255, 255, 255 }));
        }

        private static byte[] U32(uint v) { return BitConverter.GetBytes(v); }

        private static byte[] Cat(params byte[][] parts)
        {
            var l = new List<byte>();
            foreach (byte[] p in parts) l.AddRange(p);
            return l.ToArray();
        }

        [Fact]
        public void TruncatedBlockMessagesAreProtocolErrors()
        {
            Assert.Throws<ProtocolException>(() => BlockAtlas.Decode(new byte[5]));
            // byteLength past the end of the payload
            Assert.Throws<ProtocolException>(() => BlockAtlas.Decode(Cat(U32(1), U32(1), new byte[] { 1 }, U32(100), new byte[3])));
            Assert.Throws<ProtocolException>(() => BlockAtlas.Decode(Cat(U32(1), U32(1), new byte[] { 1 }, U32(0xFFFFFFFF))));
            Assert.Throws<ProtocolException>(() => AtlasRegion.Decode(new byte[10]));
            // 2x1 region needs 8 bytes of pixels, only 4 given
            Assert.Throws<ProtocolException>(() => AtlasRegion.Decode(Cat(U32(0), U32(0), U32(2), U32(1), new byte[4])));
            Assert.Throws<ProtocolException>(() => SectionMesh.Decode(new byte[7]));
            // header only, count says 3 vertices
            Assert.Throws<ProtocolException>(() => SectionMesh.Decode(Cat(U32(0), U32(0), U32(0), U32(3))));
            // 3 vertices claimed, 2 present
            Assert.Throws<ProtocolException>(() => SectionMesh.Decode(Cat(U32(0), U32(0), U32(0), U32(3), new byte[64])));
            Assert.Throws<ProtocolException>(() => DebugCommand.Decode(new byte[] { 5, 0, 1 }));
        }

        [Fact]
        public void HugeCountsFailWithoutAllocating()
        {
            Assert.Throws<ProtocolException>(() => SectionMesh.Decode(Cat(U32(0), U32(0), U32(0), U32(0xFFFFFFFF))));
            // count % 3 == 0 yet far beyond the data: 0xFFFFFFFF is divisible by 3
            Assert.Throws<ProtocolException>(() => SectionMesh.Decode(Cat(U32(0), U32(0), U32(0), U32(0xFFFFFFFD))));
            // width*height*4 overflows 32 bits
            Assert.Throws<ProtocolException>(() => AtlasRegion.Decode(Cat(U32(0), U32(0), U32(0xFFFFFFFF), U32(0xFFFFFFFF))));
            Assert.Throws<ProtocolException>(() => AtlasRegion.Decode(Cat(U32(0), U32(0), U32(0x40000000), U32(1))));
        }

        [Fact]
        public void SectionMeshVertexCountMustBeMultipleOfThree()
        {
            Assert.Throws<ProtocolException>(() => SectionMesh.Decode(Cat(U32(0), U32(0), U32(0), U32(2), new byte[64])));
            Assert.Throws<ProtocolException>(() => SectionMesh.Decode(Cat(U32(0), U32(0), U32(0), U32(4), new byte[128])));
        }

        [Fact]
        public void EncodeRejectsInconsistentBuffers()
        {
            Assert.Throws<InvalidOperationException>(() => new AtlasRegion { Width = 2, Height = 1, Rgba = new byte[7] }.Encode());
            Assert.Throws<InvalidOperationException>(() => new SectionMesh { VertexData = new byte[32] }.Encode());
            Assert.Throws<InvalidOperationException>(() => new SectionMesh { VertexData = new byte[96 + 32] }.Encode());
        }

        [Fact]
        public void WriteVertexRoundTripsThroughAccessors()
        {
            var data = new byte[2 * SectionMesh.BytesPerVertex];
            SectionMesh.WriteVertex(data, 0, 1.5f, -2.25f, 3f, 0.125f, 0.875f, 0x44332211u, 0xF0u, 7u);
            SectionMesh.WriteVertex(data, 1, -0.5f, 16f, 8f, 1f, 0f, 0xFFFFFFFFu, 0u, 1u);
            var m = new SectionMesh { Sx = 1, Sy = -2, Sz = 3, VertexData = data };
            Assert.Equal(2, m.VertexCount);
            Assert.Equal(1.5f, m.X(0)); Assert.Equal(-2.25f, m.Y(0)); Assert.Equal(3f, m.Z(0));
            Assert.Equal(0.125f, m.U(0)); Assert.Equal(0.875f, m.V(0));
            Assert.Equal(0x44332211u, m.Color(0)); Assert.Equal(0xF0u, m.Light(0)); Assert.Equal(7u, m.Flags(0));
            Assert.Equal(-0.5f, m.X(1)); Assert.Equal(16f, m.Y(1)); Assert.Equal(8f, m.Z(1));
            Assert.Equal(1f, m.U(1)); Assert.Equal(0f, m.V(1));
            Assert.Equal(0xFFFFFFFFu, m.Color(1)); Assert.Equal(0u, m.Light(1)); Assert.Equal(1u, m.Flags(1));
            // little endian on the wire: x = 1.5f = 0x3FC00000
            Assert.Equal(new byte[] { 0x00, 0x00, 0xC0, 0x3F }, new[] { data[0], data[1], data[2], data[3] });
            // color at byte offset 20
            Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 }, new[] { data[20], data[21], data[22], data[23] });
        }

        [Fact]
        public void TruncatedOverlayAndViewportAreProtocolErrors()
        {
            Assert.Throws<ProtocolException>(() => OverlayOffer.Decode(new byte[] { 5, 0, 1 }));
            Assert.Throws<ProtocolException>(() => OverlayOffer.Decode(Cat(new byte[] { 1, 0, (byte)'a' }, U32(1), U32(1), U32(1))));
            Assert.Throws<ProtocolException>(() => Viewport.Decode(new byte[7]));
        }

        [Fact]
        public void CursorCodePacksAndWraps()
        {
            Assert.Equal(125764663, InputEvent.CursorCode(1919, 1079));
            Assert.Equal(-1673527291, InputEvent.CursorCode(40000, 5));
            InputEvent e = InputEvent.Cursor(7, 9);
            Assert.Equal(InputKind.Cursor, e.Kind);
            Assert.Equal(0, e.Action);
            Assert.Equal(InputEvent.CursorCode(7, 9), e.Code);
            Assert.Equal(7, InputEvent.CursorX(e.Code));
            Assert.Equal(9, InputEvent.CursorY(e.Code));
            Assert.Equal(65535, InputEvent.CursorX(InputEvent.CursorCode(-1, 0)));
        }

        [Fact]
        public void TruncatedStatusIsProtocolError()
        {
            Assert.Throws<ProtocolException>(() => HostStatus.Decode(new byte[] { 1, 0, 0 }));
            Assert.Throws<ProtocolException>(() => GuestStatus.Decode(new byte[] { 1, 0, 0, 0, 5, 0, 1 }));
        }

        public static IEnumerable<object[]> InvalidBlockEdits()
        {
            yield return new object[] { "block_edits_index_out_of_range" };
            yield return new object[] { "block_edits_duplicate_palette" };
            yield return new object[] { "block_edits_too_many" };
        }

        [Theory]
        [MemberData(nameof(InvalidBlockEdits))]
        public void InvalidBlockEditsVectorRaisesProtocolException(string name)
        {
            string hex = null;
            using (JsonDocument d = Load("frames.json"))
                foreach (JsonElement v in d.RootElement.GetProperty("invalid").EnumerateArray())
                    if (v.GetProperty("name").GetString() == name) hex = v.GetProperty("hex").GetString();
            Assert.NotNull(hex);
            Assert.Throws<ProtocolException>(() => BlockEdits.Decode(FrameCodec.Decode(Hex(hex)).Payload));
        }

        [Fact]
        public void InvalidLightSourcesVectorRaisesProtocolException()
        {
            string hex = null;
            using (JsonDocument d = Load("frames.json"))
                foreach (JsonElement v in d.RootElement.GetProperty("invalid").EnumerateArray())
                    if (v.GetProperty("name").GetString() == "light_sources_level_zero") hex = v.GetProperty("hex").GetString();
            Assert.NotNull(hex);
            Assert.Throws<ProtocolException>(() => LightSources.Decode(FrameCodec.Decode(Hex(hex)).Payload));
        }

        private static void AssertFloats(JsonElement expected, float[] actual)
        {
            Assert.Equal(expected.GetArrayLength(), actual.Length);
            int i = 0;
            foreach (JsonElement e in expected.EnumerateArray()) Assert.Equal(e.GetSingle(), actual[i++]);
        }

        private static byte[] InvalidVectorPayload(string name)
        {
            string hex = null;
            using (JsonDocument d = Load("frames.json"))
                foreach (JsonElement v in d.RootElement.GetProperty("invalid").EnumerateArray())
                    if (v.GetProperty("name").GetString() == name) hex = v.GetProperty("hex").GetString();
            Assert.NotNull(hex);
            return FrameCodec.Decode(Hex(hex)).Payload;
        }

        [Fact]
        public void InvalidSkyStateVectorRaisesProtocolException()
        {
            byte[] p = InvalidVectorPayload("sky_state_moon_phase_8");
            Assert.Throws<ProtocolException>(() => SkyState.Decode(p));
        }

        [Fact]
        public void InvalidSkyTexturesVectorRaisesProtocolException()
        {
            byte[] p = InvalidVectorPayload("sky_textures_moon_phase_8");
            Assert.Throws<ProtocolException>(() => SkyTextures.Decode(p));
        }

        private static byte[] SnapshotPayload()
        {
            return new BlockEdits
            {
                OpenSeq = 3,
                Flags = BlockEdits.FlagLast,
                Palette = new[] { "minecraft:stone", "minecraft:dirt" },
                Edits = new[] { new BlockEdit(1, 2, 3, 0), new BlockEdit(-4, 5, -6, 1) },
            }.Encode();
        }

        [Fact]
        public void BlockEditsRoundTripsAndEveryTruncationThrows()
        {
            byte[] full = SnapshotPayload();
            BlockEdits m = BlockEdits.Decode(full);
            Assert.Equal(2, m.Edits.Length);
            Assert.Equal(-6, m.Edits[1].Z);
            for (int len = 0; len < full.Length; len++)
            {
                byte[] cut = new byte[len];
                Array.Copy(full, cut, len);
                Assert.Throws<ProtocolException>(() => BlockEdits.Decode(cut));
            }
        }

        [Fact]
        public void BlockEditsHugeCountsFailWithoutAllocating()
        {
            // openSeq=1, flags=0, paletteCount=0, editCount=0xFFFFFFFF, no edit bytes.
            Assert.Throws<ProtocolException>(() => BlockEdits.Decode(Hex("01000000" + "00" + "0000" + "ffffffff")));
            // editCount exactly at the limit but no data present.
            Assert.Throws<ProtocolException>(() => BlockEdits.Decode(Hex("01000000" + "00" + "0000" + "00000100")));
            // paletteCount=0xFFFF with no entries.
            Assert.Throws<ProtocolException>(() => BlockEdits.Decode(Hex("01000000" + "00" + "ffff")));
        }

        [Fact]
        public void BlockEditsEncodeRejectsInvalidContent()
        {
            Assert.Throws<InvalidOperationException>(() => new BlockEdits
            {
                Palette = new[] { "a", "a" },
                Edits = new BlockEdit[0],
            }.Encode());
            Assert.Throws<InvalidOperationException>(() => new BlockEdits
            {
                Palette = new[] { "a" },
                Edits = new BlockEdit[BlockEdits.MaxEdits + 1],
            }.Encode());
            Assert.Throws<InvalidOperationException>(() => new BlockEdits
            {
                Palette = new[] { "a" },
                Edits = new[] { new BlockEdit(0, 0, 0, 1) },
            }.Encode());
        }

        [Fact]
        public void BlockEditsEncodeAcceptsMaxEdits()
        {
            var edits = new BlockEdit[BlockEdits.MaxEdits];
            byte[] p = new BlockEdits { Palette = new[] { "a" }, Edits = edits }.Encode();
            Assert.Equal(BlockEdits.MaxEdits, BlockEdits.Decode(p).Edits.Length);
        }

        [Fact]
        public void SmallCityMessagesRejectTruncation()
        {
            Assert.Throws<ProtocolException>(() => CityClose.Decode(new byte[3]));
            Assert.Throws<ProtocolException>(() => WorldTime.Decode(new byte[8]));
            Assert.Throws<ProtocolException>(() => EditSync.Decode(new byte[7]));
            Assert.Throws<ProtocolException>(() => CityStateUpdate.Decode(new byte[8]));
            byte[] open = new CityOpen { OpenSeq = 1, SaveId = Guid.NewGuid(), CityName = "x", EditCount = 1 }.Encode();
            for (int len = 0; len < open.Length; len++)
            {
                byte[] cut = new byte[len];
                Array.Copy(open, cut, len);
                Assert.Throws<ProtocolException>(() => CityOpen.Decode(cut));
            }
        }

        [Fact]
        public void DynamicObstaclesRejectEveryTruncation()
        {
            byte[] full = new DynamicObstacles
            {
                Obstacles = new[] { new MovingObstacle { Kind = DynamicObstacles.Vehicle, Id = 7, X = 1f }, new MovingObstacle { Kind = DynamicObstacles.Citizen, Id = 8 } },
            }.Encode();
            Assert.Equal(2, DynamicObstacles.Decode(full).Obstacles.Length);
            for (int len = 0; len < full.Length; len++)
            {
                byte[] cut = new byte[len];
                Array.Copy(full, cut, len);
                Assert.Throws<ProtocolException>(() => DynamicObstacles.Decode(cut));
            }
            Assert.Empty(DynamicObstacles.Decode(new DynamicObstacles { Obstacles = new MovingObstacle[0] }.Encode()).Obstacles);
        }

        [Fact]
        public void ConstantsMatchSpec()
        {
            Assert.Equal("minecraft-skylines", AppProtocol.Name);
            Assert.Equal(1, AppProtocol.Major);
            Assert.Equal(10, AppProtocol.Minor);
            Assert.Equal(0x01A0, AppProtocol.WaterSurfaceType);
            Assert.Equal(0x0190, AppProtocol.SkyStateType);
            Assert.Equal(0x0191, AppProtocol.SkyTexturesType);
            Assert.Equal(0x0180, AppProtocol.LightSourcesType);
            Assert.Equal(0x0170, AppProtocol.DynamicObstaclesType);
            Assert.Equal(0x0140, AppProtocol.ViewportType);
            Assert.Equal(0x0141, AppProtocol.OverlayOfferType);
            Assert.Equal(0x0142, AppProtocol.OverlayStopType);
            Assert.Equal(6, InputKind.Cursor);
            Assert.Equal(0x0130, AppProtocol.BlockAtlasType);
            Assert.Equal(0x0131, AppProtocol.AtlasRegionType);
            Assert.Equal(0x0132, AppProtocol.SectionMeshType);
            Assert.Equal(0x0133, AppProtocol.SectionsClearType);
            Assert.Equal(0x0134, AppProtocol.BlockSelectionType);
            Assert.Equal(0x0150, AppProtocol.CityOpenType);
            Assert.Equal(0x0151, AppProtocol.BlockEditsType);
            Assert.Equal(0x0152, AppProtocol.CityCloseType);
            Assert.Equal(0x0153, AppProtocol.EditSyncType);
            Assert.Equal(0x0154, AppProtocol.EditSyncAckType);
            Assert.Equal(0x0155, AppProtocol.CityStateType);
            Assert.Equal(1, BlockEdits.FlagLast);
            Assert.Equal(65536, BlockEdits.MaxEdits);
            Assert.Equal(0, CityStateUpdate.Applying);
            Assert.Equal(1, CityStateUpdate.Ready);
            Assert.Equal(2, CityStateUpdate.Closed);
            Assert.Equal(0x01F0, AppProtocol.DebugCommandType);
            Assert.Equal(8u, HostStatusFlags.PlayerMode);
            Assert.Equal(2u, GuestStatusFlags.ScreenOpen);
        }

        private static double AngleDiff(double a, double b)
        {
            return Math.Abs(MinecraftFrame.Wrap180(a - b));
        }

        [Fact]
        public void CoordinateVectorsConvertBothWays()
        {
            using (JsonDocument d = Load("coords.json"))
            {
                double tol = d.RootElement.GetProperty("tolerance").GetDouble();
                Assert.Equal(MinecraftFrame.YOffset, d.RootElement.GetProperty("yOffset").GetDouble());
                foreach (JsonElement c in d.RootElement.GetProperty("cases").EnumerateArray())
                {
                    JsonElement cs = c.GetProperty("cs"), mc = c.GetProperty("mc");
                    var csPos = new Vec3d(cs.GetProperty("x").GetDouble(), cs.GetProperty("y").GetDouble(), cs.GetProperty("z").GetDouble());
                    var mcPos = new Vec3d(mc.GetProperty("x").GetDouble(), mc.GetProperty("y").GetDouble(), mc.GetProperty("z").GetDouble());
                    double ex = cs.GetProperty("eulerX").GetDouble(), ey = cs.GetProperty("eulerY").GetDouble();
                    double yaw = mc.GetProperty("yaw").GetDouble(), pitch = mc.GetProperty("pitch").GetDouble();

                    Vec3d m = MinecraftFrame.CsToMc(csPos);
                    Assert.InRange(Math.Abs(m.X - mcPos.X), 0, tol);
                    Assert.InRange(Math.Abs(m.Y - mcPos.Y), 0, tol);
                    Assert.InRange(Math.Abs(m.Z - mcPos.Z), 0, tol);
                    McLook look = MinecraftFrame.UnityEulerToMc(ex, ey);
                    Assert.InRange(AngleDiff(look.Yaw, yaw), 0, tol);
                    Assert.InRange(AngleDiff(look.Pitch, pitch), 0, tol);

                    Vec3d back = MinecraftFrame.McToCs(mcPos);
                    Assert.InRange(Math.Abs(back.X - csPos.X), 0, tol);
                    Assert.InRange(Math.Abs(back.Y - csPos.Y), 0, tol);
                    Assert.InRange(Math.Abs(back.Z - csPos.Z), 0, tol);
                    double bx, by;
                    MinecraftFrame.McToUnityEuler(new McLook(yaw, pitch), out bx, out by);
                    Assert.InRange(AngleDiff(bx, ex), 0, tol);
                    Assert.InRange(AngleDiff(by, ey), 0, tol);
                }
            }
        }
    }
}
