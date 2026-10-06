"""Extract Cities: Skylines 1 built-in mesh geometry into a local collision cache.

Built-in building and prop meshes are GPU-only at runtime (Mesh.isReadable false), so the mod cannot read their
vertices. The base game and DLC prefabs live in Unity scenes (Cities_Data/levelN with sharedassetsN.assets,
LoadingManager.LoadLevelComplete loading "<Env>Prefabs", "ExpansionNPrefabs", ...); this reads every Mesh in those
files with UnityPy and writes one cache file. Custom assets (.crp) are not read: PackageDeserializer.DeserializeMesh
builds them with `new Mesh()`, so they are CPU-readable in game already.

Cache format (little-endian, no padding):
    header: 8 bytes b"CS1MESH\\0", u32 version (2; version 1 files are still read), u32 entry count
    entry:  u16 name length, UTF-8 name, u32 vertex count, 6 x f32 bounds centre xyz and extent xyz (Mesh.bounds),
            u32 index count (multiple of 3), u32 channel mask (version 2 only), vertex count x 3 x f32 mesh-local
            positions, index count x u16 indices, then each channel in the mask in ascending bit order, per vertex:
            1 normals 3 x f32, 2 tangents 4 x f32, 4 colors 4 x u8 RGBA, 8/16/32/64 uv/uv2/uv3/uv4 2 x f32
The channels let the mod rebuild a GPU-only mesh for drawing (tunnel portals without their end wall).
Read by cs1/src/Skylines.Host/Geometry/MeshCache.cs.

Usage: cs1_meshes.py extract <Cities_Data dir> <output file>   (tools/extract-cs1-meshes.sh finds both)
"""
import os
import re
import struct
import sys
from array import array
from typing import Iterable, List, NamedTuple, Optional, Tuple

MAGIC = b"CS1MESH\0"
VERSION = 2
READABLE_VERSIONS = (1, 2)
CH_NORMALS, CH_TANGENTS, CH_COLORS, CH_UV, CH_UV2, CH_UV3, CH_UV4 = 1, 2, 4, 8, 16, 32, 64
# (field, bit, components, struct code) in storage order
_CHANNELS = (("normals", CH_NORMALS, 3, "f"), ("tangents", CH_TANGENTS, 4, "f"), ("colors", CH_COLORS, 4, "B"),
             ("uv", CH_UV, 2, "f"), ("uv2", CH_UV2, 2, "f"), ("uv3", CH_UV3, 2, "f"), ("uv4", CH_UV4, 2, "f"))
_KNOWN = sum(c[1] for c in _CHANNELS)
MAX_VERTICES = 65535

_HEADER = struct.Struct("<8sII")
_ENTRY = struct.Struct("<I6fI")
_MASK = struct.Struct("<I")
_ASSET_FILE = re.compile(r"^(level\d+|sharedassets\d+\.assets|resources\.assets)$")

Vec3 = Tuple[float, float, float]


class MeshEntry(NamedTuple):
    name: str
    center: Vec3
    extent: Vec3
    positions: List[Vec3]
    indices: List[int]
    normals: Optional[list] = None
    tangents: Optional[list] = None
    colors: Optional[list] = None
    uv: Optional[list] = None
    uv2: Optional[list] = None
    uv3: Optional[list] = None
    uv4: Optional[list] = None


def channel(values, n, dim):
    """The first `dim` components of each of n per-vertex values, or None when missing or of the wrong length."""
    if not values or len(values) != n:
        return None
    return [tuple(v[:dim]) for v in values]


def colors32(values, n):
    """RGBA bytes for n per-vertex colours given as 0..1 floats or as 0..255 values, or None when missing or wrong length."""
    if not values or len(values) != n:
        return None
    scale = 255 if all(c <= 1.0 for v in values for c in v[:4]) else 1
    return [tuple(min(255, max(0, int(round(c * scale)))) for c in v[:4]) for v in values]


def check_mesh(positions, indices) -> Optional[str]:
    """None when the mesh can be cached, else why it is skipped."""
    n = len(positions)
    if n == 0:
        return "no vertices"
    if n > MAX_VERTICES:
        return "over 65535 vertices"
    if len(indices) == 0:
        return "no triangles"
    if len(indices) % 3:
        return "index count not a multiple of 3"
    if min(indices) < 0 or max(indices) >= n:
        return "index out of range"
    return None


def _pack(e: MeshEntry) -> bytes:
    name = e.name.encode("utf-8")
    pos = array("f", (c for p in e.positions for c in p[:3]))
    idx = array("H", e.indices)
    mask = 0
    extra = b""
    for field, bit, dim, code in _CHANNELS:
        values = getattr(e, field)
        if values is None:
            continue
        mask |= bit
        a = array(code, (c for v in values for c in v[:dim]))
        if sys.byteorder != "little" and code == "f":
            a.byteswap()
        extra += a.tobytes()
    if sys.byteorder != "little":
        pos.byteswap()
        idx.byteswap()
    return (struct.pack("<H", len(name)) + name
            + _ENTRY.pack(len(e.positions), *e.center, *e.extent, len(e.indices)) + _MASK.pack(mask)
            + pos.tobytes() + idx.tobytes() + extra)


def write_cache(path, entries: Iterable[MeshEntry]) -> int:
    """Writes entries to path through a temporary sibling renamed over it; returns the count written."""
    path = os.fspath(path)
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    tmp = path + ".tmp"
    count = 0
    with open(tmp, "wb") as f:
        f.write(_HEADER.pack(MAGIC, VERSION, 0))
        for e in entries:
            f.write(_pack(e))
            count += 1
        f.seek(0)
        f.write(_HEADER.pack(MAGIC, VERSION, count))
    os.replace(tmp, path)
    return count


def read_cache(path) -> List[MeshEntry]:
    with open(path, "rb") as f:
        data = f.read()
    if len(data) < _HEADER.size:
        raise ValueError("truncated header")
    magic, version, count = _HEADER.unpack_from(data, 0)
    if magic != MAGIC:
        raise ValueError("bad magic")
    if version not in READABLE_VERSIONS:
        raise ValueError("unsupported version %d" % version)
    off = _HEADER.size
    out = []
    try:
        for _ in range(count):
            (nlen,) = struct.unpack_from("<H", data, off)
            off += 2
            if off + nlen > len(data):
                raise ValueError("truncated name")
            name = data[off:off + nlen].decode("utf-8")
            off += nlen
            vc, cx, cy, cz, ex, ey, ez, ic = _ENTRY.unpack_from(data, off)
            off += _ENTRY.size
            mask = 0
            if version >= 2:
                (mask,) = _MASK.unpack_from(data, off)
                off += _MASK.size
                if mask & ~_KNOWN:
                    raise ValueError("unknown channel mask bits 0x%x" % (mask & ~_KNOWN))
            end = off + 12 * vc + 2 * ic
            if end > len(data):
                raise ValueError("truncated geometry")
            pos = struct.unpack_from("<%df" % (3 * vc), data, off)
            idx = struct.unpack_from("<%dH" % ic, data, off + 12 * vc)
            off = end
            channels = {}
            for field, bit, dim, code in _CHANNELS:
                if not mask & bit:
                    continue
                size = struct.calcsize("<%d%s" % (dim * vc, code))
                if off + size > len(data):
                    raise ValueError("truncated channel " + field)
                flat = struct.unpack_from("<%d%s" % (dim * vc, code), data, off)
                off += size
                channels[field] = [tuple(flat[i:i + dim]) for i in range(0, len(flat), dim)]
            out.append(MeshEntry(name, (cx, cy, cz), (ex, ey, ez),
                                 [tuple(pos[i:i + 3]) for i in range(0, len(pos), 3)], list(idx), **channels))
    except struct.error as e:
        raise ValueError("truncated entry: %s" % e)
    return out


class Summary:
    def __init__(self):
        self.files = 0
        self.found = 0
        self.written = 0
        self.skipped = {}

    def skip(self, reason):
        self.skipped[reason] = self.skipped.get(reason, 0) + 1


def asset_files(data_dir) -> List[str]:
    names = sorted(n for n in os.listdir(data_dir) if _ASSET_FILE.match(n))
    return [os.path.join(data_dir, n) for n in names]


def meshes_in(path, summary: Summary):
    """Yields a MeshEntry for every usable Mesh in one Unity serialized file."""
    import UnityPy
    from UnityPy.helpers.MeshHelper import MeshHandler

    UnityPy.config.FALLBACK_UNITY_VERSION = "5.6.7f1"  # CS1's engine, used if a file's version string is stripped
    env = UnityPy.load(path)
    for obj in env.objects:
        if obj.type.name != "Mesh":
            continue
        summary.found += 1
        try:
            mesh = obj.read(check_read=False)
            h = MeshHandler(mesh)
            h.process()
            positions = [tuple(v[:3]) for v in (h.m_Vertices or [])]
            indices = [i for sub in h.get_triangles() for tri in sub for i in tri]
            aabb = mesh.m_LocalAABB
            c, x = aabb.m_Center, aabb.m_Extent
            n = len(positions)
            channels = dict(normals=channel(h.m_Normals, n, 3), tangents=channel(h.m_Tangents, n, 4),
                            colors=colors32(h.m_Colors, n), uv=channel(h.m_UV0, n, 2), uv2=channel(h.m_UV1, n, 2),
                            uv3=channel(h.m_UV2, n, 2), uv4=channel(h.m_UV3, n, 2))
        except Exception as e:  # one bad mesh must not end the run
            summary.skip("read failed: " + type(e).__name__)
            continue
        why = check_mesh(positions, indices)
        if why:
            summary.skip(why)
            continue
        yield MeshEntry(mesh.m_Name, (c.x, c.y, c.z), (x.x, x.y, x.z), positions, indices, **channels)


def extract(data_dir, out_path, log=print) -> Summary:
    summary = Summary()
    seen = set()

    def entries():
        files = asset_files(data_dir)
        for i, path in enumerate(files, 1):
            summary.files += 1
            before = summary.written
            try:
                for e in meshes_in(path, summary):
                    key = (e.name, len(e.positions), e.center, e.extent)
                    if key in seen:
                        summary.skip("duplicate")
                        continue
                    seen.add(key)
                    summary.written += 1
                    yield e
            except Exception as e:
                summary.skip("file unreadable: " + type(e).__name__)
                log("  %s: unreadable (%s: %s)" % (os.path.basename(path), type(e).__name__, e))
            log("[%d/%d] %s: %d new meshes" % (i, len(files), os.path.basename(path), summary.written - before))

    staged = out_path + ".new"
    write_cache(staged, entries())
    if summary.written:  # never replace a good cache with an empty one
        os.replace(staged, out_path)
    return summary


def main(argv) -> int:
    if len(argv) != 4 or argv[1] != "extract":
        print(__doc__.strip().splitlines()[-1], file=sys.stderr)
        return 2
    data_dir, out_path = argv[2], argv[3]
    if not os.path.isdir(data_dir):
        print("not a directory: " + data_dir, file=sys.stderr)
        return 1
    s = extract(data_dir, out_path)
    print("files scanned: %d" % s.files)
    print("meshes found: %d" % s.found)
    if s.written:
        print("written: %d -> %s (%d bytes)" % (s.written, out_path, os.path.getsize(out_path)))
    else:
        print("written: 0 (no usable meshes; %s left unchanged)" % out_path)
    print("skipped: %d" % sum(s.skipped.values()))
    for reason, n in sorted(s.skipped.items(), key=lambda kv: -kv[1]):
        print("  %6d  %s" % (n, reason))
    return 0 if s.written else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
