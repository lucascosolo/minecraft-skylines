"""Reference codec for the SKBR bridge protocol v1 (protocol/bridge-v1.md).

Pure standard library. Used to generate the golden vectors, as the scriptable fake peer in
conformance.py, and as a stand-in host or guest during development. The spec wins over this file.
"""
from __future__ import annotations

import socket
import struct
import time
import uuid
from dataclasses import dataclass, field

MAGIC = 0x52424B53
BRIDGE_VERSION = 1
HEADER = struct.Struct("<IHH")
MAX_PAYLOAD = 16 * 1024 * 1024

HELLO, WELCOME, HEARTBEAT, GOODBYE = 0x0001, 0x0002, 0x0003, 0x0004
APP_MIN = 0x0100

REJECT_BRIDGE_VERSION, REJECT_APP_PROTOCOL, REJECT_APP_MAJOR, REJECT_BUSY, REJECT_NOT_READY = 1, 2, 3, 4, 5
BYE_NORMAL, BYE_SHUTTING_DOWN, BYE_PROTOCOL_ERROR, BYE_TIMEOUT, BYE_BACKPRESSURE = 0, 1, 2, 3, 4


class ProtocolError(Exception):
    pass


class Writer:
    def __init__(self) -> None:
        self.buf = bytearray()

    def u8(self, v: int) -> "Writer":
        self.buf += struct.pack("<B", v); return self

    def u16(self, v: int) -> "Writer":
        self.buf += struct.pack("<H", v); return self

    def u32(self, v: int) -> "Writer":
        self.buf += struct.pack("<I", v); return self

    def u64(self, v: int) -> "Writer":
        self.buf += struct.pack("<Q", v); return self

    def i32(self, v: int) -> "Writer":
        self.buf += struct.pack("<i", v); return self

    def f32(self, v: float) -> "Writer":
        self.buf += struct.pack("<f", v); return self

    def f64(self, v: float) -> "Writer":
        self.buf += struct.pack("<d", v); return self

    def bool(self, v: bool) -> "Writer":
        return self.u8(1 if v else 0)

    def string(self, s: str) -> "Writer":
        b = s.encode("utf-8")
        if len(b) > 0xFFFF:
            raise ValueError("string longer than 65535 bytes")
        self.u16(len(b)); self.buf += b; return self

    def uuid(self, u: uuid.UUID) -> "Writer":
        self.buf += u.bytes; return self

    def bytes(self) -> bytes:
        return bytes(self.buf)


class Reader:
    def __init__(self, data: bytes) -> None:
        self.data, self.pos = data, 0

    def _take(self, n: int) -> bytes:
        if self.pos + n > len(self.data):
            raise ProtocolError("payload truncated")
        b = self.data[self.pos:self.pos + n]; self.pos += n; return b

    def u8(self) -> int: return self._take(1)[0]
    def u16(self) -> int: return struct.unpack("<H", self._take(2))[0]
    def u32(self) -> int: return struct.unpack("<I", self._take(4))[0]
    def u64(self) -> int: return struct.unpack("<Q", self._take(8))[0]
    def i32(self) -> int: return struct.unpack("<i", self._take(4))[0]
    def f32(self) -> float: return struct.unpack("<f", self._take(4))[0]
    def f64(self) -> float: return struct.unpack("<d", self._take(8))[0]

    def bool(self) -> bool:
        v = self.u8()
        if v > 1:
            raise ProtocolError("bool out of range")
        return v == 1

    def string(self) -> str:
        n = self.u16()
        try:
            return self._take(n).decode("utf-8")
        except UnicodeDecodeError as e:
            raise ProtocolError("invalid UTF-8") from e

    def uuid(self) -> uuid.UUID:
        return uuid.UUID(bytes=self._take(16))


def frame(type_: int, payload: bytes = b"", flags: int = 0) -> bytes:
    return HEADER.pack(len(payload), type_, flags) + payload


@dataclass
class Hello:
    app_protocol: str
    app_major: int
    app_minor: int
    peer_name: str
    peer_version: str
    session_nonce: int
    magic: int = MAGIC
    bridge_version: int = BRIDGE_VERSION

    def encode(self) -> bytes:
        return (Writer().u32(self.magic).u16(self.bridge_version).string(self.app_protocol)
                .u16(self.app_major).u16(self.app_minor).string(self.peer_name)
                .string(self.peer_version).u64(self.session_nonce).bytes())

    @staticmethod
    def decode(p: bytes) -> "Hello":
        r = Reader(p)
        magic, bv = r.u32(), r.u16()
        return Hello(magic=magic, bridge_version=bv, app_protocol=r.string(), app_major=r.u16(),
                     app_minor=r.u16(), peer_name=r.string(), peer_version=r.string(),
                     session_nonce=r.u64())


@dataclass
class Welcome:
    accepted: bool
    reject_code: int
    reject_reason: str
    app_protocol: str
    app_major: int
    app_minor: int
    peer_name: str
    peer_version: str
    heartbeat_interval_ms: int
    peer_timeout_ms: int
    session_id: int
    bridge_version: int = BRIDGE_VERSION

    def encode(self) -> bytes:
        return (Writer().bool(self.accepted).u16(self.reject_code).string(self.reject_reason)
                .u16(self.bridge_version).string(self.app_protocol).u16(self.app_major)
                .u16(self.app_minor).string(self.peer_name).string(self.peer_version)
                .u32(self.heartbeat_interval_ms).u32(self.peer_timeout_ms).u64(self.session_id)
                .bytes())

    @staticmethod
    def decode(p: bytes) -> "Welcome":
        r = Reader(p)
        accepted, code, reason, bv = r.bool(), r.u16(), r.string(), r.u16()
        return Welcome(accepted=accepted, reject_code=code, reject_reason=reason, bridge_version=bv,
                       app_protocol=r.string(), app_major=r.u16(), app_minor=r.u16(),
                       peer_name=r.string(), peer_version=r.string(),
                       heartbeat_interval_ms=r.u32(), peer_timeout_ms=r.u32(), session_id=r.u64())


@dataclass
class Heartbeat:
    seq: int
    sender_uptime_ms: int

    def encode(self) -> bytes:
        return Writer().u32(self.seq).u64(self.sender_uptime_ms).bytes()

    @staticmethod
    def decode(p: bytes) -> "Heartbeat":
        r = Reader(p)
        return Heartbeat(seq=r.u32(), sender_uptime_ms=r.u64())


@dataclass
class Goodbye:
    code: int
    reason: str

    def encode(self) -> bytes:
        return Writer().u16(self.code).string(self.reason).bytes()

    @staticmethod
    def decode(p: bytes) -> "Goodbye":
        r = Reader(p)
        return Goodbye(code=r.u16(), reason=r.string())


# ---- minecraft-skylines app protocol 1.0 (protocol/minecraft-skylines-v1.md) ----------------
HOST_STATUS, GUEST_STATUS = 0x0100, 0x0101


@dataclass
class HostStatus:
    flags: int
    city_name: str
    save_id: uuid.UUID
    game_version: str

    def encode(self) -> bytes:
        return (Writer().u32(self.flags).string(self.city_name).uuid(self.save_id)
                .string(self.game_version).bytes())

    @staticmethod
    def decode(p: bytes) -> "HostStatus":
        r = Reader(p)
        return HostStatus(flags=r.u32(), city_name=r.string(), save_id=r.uuid(), game_version=r.string())


@dataclass
class GuestStatus:
    flags: int
    world_name: str
    paired_save_id: uuid.UUID

    def encode(self) -> bytes:
        return Writer().u32(self.flags).string(self.world_name).uuid(self.paired_save_id).bytes()

    @staticmethod
    def decode(p: bytes) -> "GuestStatus":
        r = Reader(p)
        return GuestStatus(flags=r.u32(), world_name=r.string(), paired_save_id=r.uuid())


# ---- minecraft-skylines app protocol 1.1: milestone 2 -----------------------------------------
ENTER_PLAYER_MODE, EXIT_PLAYER_MODE, INPUT, COLLISION_REGION, COLLISION_RESET = 0x0110, 0x0111, 0x0112, 0x0113, 0x0114
PLAYER_STATE = 0x0120
IN_KEY, IN_BUTTON, IN_SCROLL, IN_TEXT, IN_RELEASE_ALL = 1, 2, 3, 4, 5


@dataclass
class EnterPlayerMode:
    teleport_seq: int
    x: float
    y: float
    z: float
    yaw: float
    pitch: float
    collision_epoch: int

    def encode(self) -> bytes:
        return (Writer().u32(self.teleport_seq).f64(self.x).f64(self.y).f64(self.z)
                .f32(self.yaw).f32(self.pitch).u32(self.collision_epoch).bytes())

    @staticmethod
    def decode(p: bytes) -> "EnterPlayerMode":
        r = Reader(p)
        return EnterPlayerMode(r.u32(), r.f64(), r.f64(), r.f64(), r.f32(), r.f32(), r.u32())


@dataclass
class ExitPlayerMode:
    reason: str

    def encode(self) -> bytes:
        return Writer().string(self.reason).bytes()

    @staticmethod
    def decode(p: bytes) -> "ExitPlayerMode":
        return ExitPlayerMode(Reader(p).string())


@dataclass
class InputEvent:
    kind: int
    action: int
    code: int


@dataclass
class Input:
    yaw: float
    pitch: float
    events: list

    def encode(self) -> bytes:
        w = Writer().f32(self.yaw).f32(self.pitch).u16(len(self.events))
        for e in self.events:
            w.u8(e.kind).u8(e.action).i32(e.code)
        return w.bytes()

    @staticmethod
    def decode(p: bytes) -> "Input":
        r = Reader(p)
        yaw, pitch, n = r.f32(), r.f32(), r.u16()
        return Input(yaw, pitch, [InputEvent(r.u8(), r.u8(), r.i32()) for _ in range(n)])


@dataclass
class CollisionRegion:
    epoch: int
    region_x: int
    region_z: int
    tris: list  # each: (9 floats, flags)

    def encode(self) -> bytes:
        w = Writer().u32(self.epoch).i32(self.region_x).i32(self.region_z).u32(len(self.tris))
        for verts, flags in self.tris:
            for v in verts:
                w.f32(v)
            w.u16(flags)
        return w.bytes()

    @staticmethod
    def decode(p: bytes) -> "CollisionRegion":
        r = Reader(p)
        epoch, rx, rz, n = r.u32(), r.i32(), r.i32(), r.u32()
        tris = []
        for _ in range(n):
            verts = [r.f32() for _ in range(9)]
            tris.append((verts, r.u16()))
        return CollisionRegion(epoch, rx, rz, tris)


@dataclass
class CollisionReset:
    epoch: int

    def encode(self) -> bytes:
        return Writer().u32(self.epoch).bytes()

    @staticmethod
    def decode(p: bytes) -> "CollisionReset":
        return CollisionReset(Reader(p).u32())


PLAYER_STATE_FIELDS = [
    ("flags", "u32"), ("teleportAck", "u32"),
    ("x", "f64"), ("y", "f64"), ("z", "f64"),
    ("eyeX", "f64"), ("eyeY", "f64"), ("eyeZ", "f64"),
    ("yaw", "f32"), ("pitch", "f32"), ("fovDeg", "f32"),
    ("tickSeq", "u32"),
    ("prevX", "f64"), ("prevY", "f64"), ("prevZ", "f64"),
    ("curX", "f64"), ("curY", "f64"), ("curZ", "f64"),
    ("prevEyeHeight", "f32"), ("curEyeHeight", "f32"),
    ("partialTick", "f32"), ("tickMs", "f32"),
]


@dataclass
class PlayerState:
    values: dict  # keyed by PLAYER_STATE_FIELDS names

    def encode(self) -> bytes:
        w = Writer()
        for name, kind in PLAYER_STATE_FIELDS:
            getattr(w, kind)(self.values[name])
        return w.bytes()

    @staticmethod
    def decode(p: bytes) -> "PlayerState":
        r = Reader(p)
        return PlayerState({name: getattr(r, kind)() for name, kind in PLAYER_STATE_FIELDS})


# ---- minecraft-skylines app protocol 1.2: milestone 3 -----------------------------------------
BLOCK_ATLAS, ATLAS_REGION, SECTION_MESH, SECTIONS_CLEAR, DEBUG_COMMAND = 0x0130, 0x0131, 0x0132, 0x0133, 0x01F0
ATLAS_PNG = 1
VERTEX = struct.Struct("<5f3I")  # x y z u v color light flags = 32 bytes


@dataclass
class BlockAtlas:
    width: int
    height: int
    fmt: int
    data: bytes

    def encode(self) -> bytes:
        return Writer().u32(self.width).u32(self.height).u8(self.fmt).u32(len(self.data)).bytes() + self.data

    @staticmethod
    def decode(p: bytes) -> "BlockAtlas":
        r = Reader(p)
        w, h, fmt, n = r.u32(), r.u32(), r.u8(), r.u32()
        return BlockAtlas(w, h, fmt, r._take(n))


@dataclass
class AtlasRegion:
    x: int
    y: int
    width: int
    height: int
    rgba: bytes

    def encode(self) -> bytes:
        if len(self.rgba) != self.width * self.height * 4:
            raise ValueError("rgba size mismatch")
        return Writer().u32(self.x).u32(self.y).u32(self.width).u32(self.height).bytes() + self.rgba

    @staticmethod
    def decode(p: bytes) -> "AtlasRegion":
        r = Reader(p)
        x, y, w, h = r.u32(), r.u32(), r.u32(), r.u32()
        return AtlasRegion(x, y, w, h, r._take(w * h * 4))


@dataclass
class SectionMesh:
    sx: int
    sy: int
    sz: int
    vertices: list  # each: (x, y, z, u, v, color, light, flags)

    def encode(self) -> bytes:
        if len(self.vertices) % 3:
            raise ValueError("vertex count must be a multiple of 3")
        w = Writer().i32(self.sx).i32(self.sy).i32(self.sz).u32(len(self.vertices))
        return w.bytes() + b"".join(VERTEX.pack(*v) for v in self.vertices)

    @staticmethod
    def decode(p: bytes) -> "SectionMesh":
        r = Reader(p)
        sx, sy, sz, n = r.i32(), r.i32(), r.i32(), r.u32()
        if n % 3:
            raise ProtocolError("vertex count not a multiple of 3")
        raw = r._take(n * VERTEX.size)
        return SectionMesh(sx, sy, sz, [VERTEX.unpack_from(raw, i * VERTEX.size) for i in range(n)])


@dataclass
class DebugCommand:
    command: str

    def encode(self) -> bytes:
        return Writer().string(self.command).bytes()

    @staticmethod
    def decode(p: bytes) -> "DebugCommand":
        return DebugCommand(Reader(p).string())


# ---- minecraft-skylines app protocol 1.3: GUI overlay ----------------------------------------
VIEWPORT, OVERLAY_OFFER, OVERLAY_STOP = 0x0140, 0x0141, 0x0142
IN_CURSOR = 6
OVERLAY_MAGIC, OVERLAY_DIRTY = 0x564F534D, 1 << 2


@dataclass
class Viewport:
    width: int
    height: int
    ui_scale: float

    def encode(self) -> bytes:
        return Writer().u32(self.width).u32(self.height).f32(self.ui_scale).bytes()

    @staticmethod
    def decode(p: bytes) -> "Viewport":
        r = Reader(p)
        return Viewport(r.u32(), r.u32(), r.f32())


@dataclass
class OverlayOffer:
    path: str
    max_width: int
    max_height: int
    slot_count: int
    generation: int

    def encode(self) -> bytes:
        return (Writer().string(self.path).u32(self.max_width).u32(self.max_height)
                .u32(self.slot_count).u64(self.generation).bytes())

    @staticmethod
    def decode(p: bytes) -> "OverlayOffer":
        r = Reader(p)
        return OverlayOffer(r.string(), r.u32(), r.u32(), r.u32(), r.u64())


def cursor_code(x: int, y: int) -> int:
    v = ((x & 0xFFFF) << 16) | (y & 0xFFFF)
    return v - (1 << 32) if v >= (1 << 31) else v  # carried in an i32


# ---- minecraft-skylines app protocol 1.4: block selection ------------------------------------
BLOCK_SELECTION = 0x0134


@dataclass
class BlockSelection:
    visible: bool
    box: tuple  # minX minY minZ maxX maxY maxZ
    kind: int

    def encode(self) -> bytes:
        w = Writer().bool(self.visible)
        for v in self.box:
            w.f32(v)
        return w.u8(self.kind).bytes()

    @staticmethod
    def decode(p: bytes) -> "BlockSelection":
        r = Reader(p)
        vis = r.bool()
        box = tuple(r.f32() for _ in range(6))
        return BlockSelection(vis, box, r.u8())


# ---- minecraft-skylines app protocol 1.5: per-city block edits -------------------------------
CITY_OPEN, BLOCK_EDITS, CITY_CLOSE, EDIT_SYNC, EDIT_SYNC_ACK, CITY_STATE = 0x0150, 0x0151, 0x0152, 0x0153, 0x0154, 0x0155
EDITS_LAST = 1
MAX_EDITS_PER_BATCH = 65536
CITY_APPLYING, CITY_READY, CITY_CLOSED = 0, 1, 2


@dataclass
class CityOpen:
    open_seq: int
    save_id: uuid.UUID
    city_name: str
    edit_count: int

    def encode(self) -> bytes:
        return Writer().u32(self.open_seq).uuid(self.save_id).string(self.city_name).u32(self.edit_count).bytes()

    @staticmethod
    def decode(p: bytes) -> "CityOpen":
        r = Reader(p)
        return CityOpen(r.u32(), r.uuid(), r.string(), r.u32())


@dataclass
class BlockEdits:
    open_seq: int
    flags: int
    palette: list  # of str
    edits: list  # each: (x, y, z, state_index)

    def encode(self) -> bytes:
        if len(set(self.palette)) != len(self.palette):
            raise ValueError("palette has duplicates")
        if len(self.edits) > MAX_EDITS_PER_BATCH:
            raise ValueError("too many edits in one batch")
        w = Writer().u32(self.open_seq).u8(self.flags).u16(len(self.palette))
        for st in self.palette:
            w.string(st)
        w.u32(len(self.edits))
        for x, y, z, i in self.edits:
            if not 0 <= i < len(self.palette):
                raise ValueError("state index out of range")
            w.i32(x).i32(y).i32(z).u16(i)
        return w.bytes()

    @staticmethod
    def decode(p: bytes) -> "BlockEdits":
        r = Reader(p)
        seq, flags, n = r.u32(), r.u8(), r.u16()
        palette = [r.string() for _ in range(n)]
        if len(set(palette)) != len(palette):
            raise ProtocolError("palette has duplicates")
        count = r.u32()
        if count > MAX_EDITS_PER_BATCH:
            raise ProtocolError("too many edits in one batch")
        edits = []
        for _ in range(count):
            e = (r.i32(), r.i32(), r.i32(), r.u16())
            if e[3] >= n:
                raise ProtocolError("state index out of range")
            edits.append(e)
        return BlockEdits(seq, flags, palette, edits)


@dataclass
class CityClose:
    open_seq: int

    def encode(self) -> bytes:
        return Writer().u32(self.open_seq).bytes()

    @staticmethod
    def decode(p: bytes) -> "CityClose":
        return CityClose(Reader(p).u32())


@dataclass
class EditSync:
    """EDIT_SYNC and EDIT_SYNC_ACK share this layout."""
    open_seq: int
    token: int

    def encode(self) -> bytes:
        return Writer().u32(self.open_seq).u32(self.token).bytes()

    @staticmethod
    def decode(p: bytes) -> "EditSync":
        r = Reader(p)
        return EditSync(r.u32(), r.u32())


@dataclass
class CityState:
    open_seq: int
    state: int
    applied_count: int

    def encode(self) -> bytes:
        return Writer().u32(self.open_seq).u8(self.state).u32(self.applied_count).bytes()

    @staticmethod
    def decode(p: bytes) -> "CityState":
        r = Reader(p)
        return CityState(r.u32(), r.u8(), r.u32())


# ---- minecraft-skylines app protocol 1.6: the city's clock ---------------------------------------
WORLD_TIME = 0x0160
TIME_DAY_NIGHT = 1


@dataclass
class WorldTime:
    hour: float
    day: int
    flags: int

    def encode(self) -> bytes:
        return Writer().f32(self.hour).u32(self.day).u8(self.flags).bytes()

    @staticmethod
    def decode(p: bytes) -> "WorldTime":
        r = Reader(p)
        return WorldTime(r.f32(), r.u32(), r.u8())


def minecraft_day_ticks(hour: float) -> int:
    """Ticks into Minecraft's day (0 = 06:00) for a city hour."""
    return int(((hour - 6.0) % 24.0) * 1000.0) % 24000


# ---- minecraft-skylines app protocol 1.7: moving obstacles --------------------------------------
DYNAMIC_OBSTACLES = 0x0170
OBSTACLE_VEHICLE = 1
OBSTACLE_CITIZEN = 2


@dataclass
class Obstacle:
    """An oriented box in Minecraft coordinates: centre, yaw of the length axis, half extents, velocity (m/s)."""
    kind: int
    id: int
    x: float
    y: float
    z: float
    yaw: float
    half_width: float
    half_height: float
    half_length: float
    vx: float
    vy: float
    vz: float


@dataclass
class DynamicObstacles:
    obstacles: list

    def encode(self) -> bytes:
        w = Writer().u16(len(self.obstacles))
        for o in self.obstacles:
            w.u8(o.kind).u32(o.id)
            for v in (o.x, o.y, o.z, o.yaw, o.half_width, o.half_height, o.half_length, o.vx, o.vy, o.vz):
                w.f32(v)
        return w.bytes()

    @staticmethod
    def decode(p: bytes) -> "DynamicObstacles":
        r = Reader(p)
        n = r.u16()
        return DynamicObstacles([Obstacle(r.u8(), r.u32(), *[r.f32() for _ in range(10)]) for _ in range(n)])


# ---- minecraft-skylines app protocol 1.8: lamp light ------------------------------------------
LIGHT_SOURCES = 0x0180
LIGHT_MAX_LEVEL = 15


@dataclass
class LightSource:
    """A lit city light: the Minecraft block containing it and its Minecraft light level (1-15)."""
    x: int
    y: int
    z: int
    level: int


@dataclass
class LightSources:
    lights: list

    def encode(self) -> bytes:
        w = Writer().u16(len(self.lights))
        for s in self.lights:
            w.i32(s.x).i32(s.y).i32(s.z).u8(s.level)
        return w.bytes()

    @staticmethod
    def decode(p: bytes) -> "LightSources":
        r = Reader(p)
        out = []
        for _ in range(r.u16()):
            s = LightSource(r.i32(), r.i32(), r.i32(), r.u8())
            if not 1 <= s.level <= LIGHT_MAX_LEVEL:
                raise ProtocolError(f"light level {s.level} outside 1..15")
            out.append(s)
        return LightSources(out)


# ---- minecraft-skylines app protocol 1.9: Minecraft's sky -------------------------------------
SKY_STATE = 0x0190
SKY_TEXTURES = 0x0191
SKY_FLAG_SKY = 1
SKY_FLAG_CLOUDS = 2
SKY_TEX_SUN = 0
SKY_TEX_MOON = 1
SKY_TEX_CLOUDS = 2
SKY_TEX_PNG = 1
MOON_PHASES = 8


@dataclass
class SkyState:
    flags: int
    sky_color: tuple
    fog_color: tuple
    sunrise_color: tuple
    star_brightness: float
    rain_level: float
    moon_phase: int
    cloud_color: tuple
    cloud_height: float
    cloud_offset: float
    cloud_speed: float

    def encode(self) -> bytes:
        w = Writer().u8(self.flags)
        for v in (*self.sky_color, *self.fog_color, *self.sunrise_color, self.star_brightness, self.rain_level):
            w.f32(v)
        w.u8(self.moon_phase)
        for v in (*self.cloud_color, self.cloud_height, self.cloud_offset, self.cloud_speed):
            w.f32(v)
        return w.bytes()

    @staticmethod
    def decode(p: bytes) -> "SkyState":
        r = Reader(p)
        flags = r.u8()
        sky = tuple(r.f32() for _ in range(3))
        fog = tuple(r.f32() for _ in range(3))
        sunrise = tuple(r.f32() for _ in range(4))
        stars, rain, phase = r.f32(), r.f32(), r.u8()
        if phase >= MOON_PHASES:
            raise ProtocolError(f"moon phase {phase} outside 0..7")
        cloud = tuple(r.f32() for _ in range(4))
        return SkyState(flags, sky, fog, sunrise, stars, rain, phase, cloud, r.f32(), r.f32(), r.f32())


@dataclass
class SkyTexture:
    kind: int
    phase: int
    fmt: int
    data: bytes


@dataclass
class SkyTextures:
    textures: list

    def encode(self) -> bytes:
        out = Writer().u8(len(self.textures)).bytes()
        for t in self.textures:
            out += Writer().u8(t.kind).u8(t.phase).u8(t.fmt).u32(len(t.data)).bytes() + t.data
        return out

    @staticmethod
    def decode(p: bytes) -> "SkyTextures":
        r = Reader(p)
        out = []
        for _ in range(r.u8()):
            kind, phase, fmt, n = r.u8(), r.u8(), r.u8(), r.u32()
            if kind == SKY_TEX_MOON and phase >= MOON_PHASES:
                raise ProtocolError(f"moon phase {phase} outside 0..7")
            out.append(SkyTexture(kind, phase, fmt, r._take(n)))
        return SkyTextures(out)


# ---- minecraft-skylines app protocol 1.10: the city's water ------------------------------------
WATER_SURFACE = 0x01A0
WATER_MAX_SIZE = 128


@dataclass
class WaterSurface:
    """Water surface and ground (Minecraft y) per block column, index dz * size + dx."""
    origin_x: int
    origin_z: int
    size: int
    surface: list
    bottom: list

    def encode(self) -> bytes:
        w = Writer().i32(self.origin_x).i32(self.origin_z).u16(self.size)
        for s, b in zip(self.surface, self.bottom):
            w.f32(s).f32(b)
        return w.bytes()

    @staticmethod
    def decode(p: bytes) -> "WaterSurface":
        r = Reader(p)
        ox, oz, size = r.i32(), r.i32(), r.u16()
        if size > WATER_MAX_SIZE:
            raise ProtocolError(f"water grid size {size} above {WATER_MAX_SIZE}")
        surface, bottom = [], []
        for _ in range(size * size):
            surface.append(r.f32())
            bottom.append(r.f32())
        return WaterSurface(ox, oz, size, surface, bottom)


# ---- minecraft-skylines app protocol 1.11: the city's player -----------------------------------
PLAYER_DATA = 0x01B0
RESPAWN_REQUEST = 0x01B1
PLAYER_DATA_MAX = 4 * 1024 * 1024


@dataclass
class PlayerData:
    """The player's own data as the guest serializes it; opaque to the host. Empty: a fresh player (host -> guest)."""
    open_seq: int
    data: bytes

    def encode(self) -> bytes:
        if len(self.data) > PLAYER_DATA_MAX:
            raise ValueError("player data too long")
        return Writer().u32(self.open_seq).u32(len(self.data)).bytes() + self.data

    @staticmethod
    def decode(p: bytes) -> "PlayerData":
        r = Reader(p)
        seq, n = r.u32(), r.u32()
        if n > PLAYER_DATA_MAX:
            raise ProtocolError(f"player data length {n} above {PLAYER_DATA_MAX}")
        return PlayerData(seq, r._take(n))


@dataclass
class RespawnRequest:
    """The player respawned without a spawn block of its own; the host teleports it to the city's entry spot."""
    open_seq: int

    def encode(self) -> bytes:
        return Writer().u32(self.open_seq).bytes()

    @staticmethod
    def decode(p: bytes) -> "RespawnRequest":
        return RespawnRequest(Reader(p).u32())


# ---- socket helpers ---------------------------------------------------------------------------
class Conn:
    """A blocking connection with a receive deadline, for scripted tests."""

    def __init__(self, sock: socket.socket) -> None:
        self.sock = sock
        sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self.buf = bytearray()

    @staticmethod
    def connect(port: int, timeout_s: float = 5.0) -> "Conn":
        deadline = time.monotonic() + timeout_s
        while True:
            try:
                return Conn(socket.create_connection(("127.0.0.1", port), timeout=1.0))
            except OSError:
                if time.monotonic() > deadline:
                    raise
                time.sleep(0.05)

    def send(self, data: bytes) -> None:
        self.sock.sendall(data)

    def send_frame(self, type_: int, payload: bytes = b"", flags: int = 0) -> None:
        self.send(frame(type_, payload, flags))

    def recv_frame(self, timeout_s: float) -> tuple[int, int, bytes] | None:
        """Return (type, flags, payload), or None if the peer closed. Raises TimeoutError."""
        deadline = time.monotonic() + timeout_s
        while True:
            if len(self.buf) >= HEADER.size:
                length, type_, flags = HEADER.unpack_from(self.buf)
                if length > MAX_PAYLOAD:
                    raise ProtocolError("oversize frame")
                if len(self.buf) >= HEADER.size + length:
                    payload = bytes(self.buf[HEADER.size:HEADER.size + length])
                    del self.buf[:HEADER.size + length]
                    return type_, flags, payload
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise TimeoutError("no frame before deadline")
            self.sock.settimeout(remaining)
            try:
                chunk = self.sock.recv(65536)
            except socket.timeout as e:
                raise TimeoutError("no frame before deadline") from e
            except ConnectionResetError:
                return None
            if not chunk:
                return None
            self.buf += chunk

    def recv_until(self, wanted: set[int], timeout_s: float) -> tuple[int, int, bytes] | None:
        """Skip frames (heartbeats etc.) until one of `wanted` types arrives."""
        deadline = time.monotonic() + timeout_s
        while True:
            f = self.recv_frame(max(0.0, deadline - time.monotonic()))
            if f is None or f[0] in wanted:
                return f

    def wait_closed(self, timeout_s: float) -> list[tuple[int, int, bytes]]:
        """Read until EOF; return the frames seen. Raises TimeoutError if still open."""
        seen = []
        deadline = time.monotonic() + timeout_s
        while True:
            f = self.recv_frame(max(0.0, deadline - time.monotonic()))
            if f is None:
                return seen
            seen.append(f)

    def close(self) -> None:
        try:
            self.sock.close()
        except OSError:
            pass


@dataclass
class HostConfig:
    app_protocol: str = "skbr-conformance"
    app_major: int = 1
    app_minor: int = 0
    heartbeat_interval_ms: int = 200
    peer_timeout_ms: int = 1000
    name: str = "python-reference-host"
    version: str = "0"
    extra: dict = field(default_factory=dict)
