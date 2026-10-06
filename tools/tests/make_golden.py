"""Golden cache fixtures built with struct only, independent of tools/cs1_meshes.py.

data/meshes-golden.bin is the frozen version-1 fixture (build() reproduces it; main() no longer rewrites it).
data/meshes-golden-v2.bin is created by:  python3 tools/tests/make_golden.py
That only creates or overwrites that one file; nothing is deleted."""
import os
import struct

ENTRIES = [
    ("Golden Box", (0.5, 1.0, -0.25), (1.5, 1.0, 2.0),
     [(-1, 0, -2.25), (2, 0, -2.25), (2, 2, 1.75), (-1, 2, 1.75)], [0, 1, 2, 0, 2, 3]),
    ("Golden Tri é", (0, 0.5, 0), (0.5, 0.5, 0.5),
     [(0, 0, 0), (0.5, 1, 0), (-0.5, 0, 0.5)], [0, 1, 2]),
    ("Golden Tri twin", (0, 0.5, 0), (0.5, 0.5, 0.5),
     [(0, 0, 0), (-0.5, 1, 0), (0.5, 0, 0.5)], [0, 2, 1]),
]


def build():
    """Version 1 bytes (compatibility fixture)."""
    out = bytearray(b"CS1MESH\0" + struct.pack("<II", 1, len(ENTRIES)))
    for name, c, e, pos, idx in ENTRIES:
        nb = name.encode("utf-8")
        out += struct.pack("<H", len(nb)) + nb
        out += struct.pack("<I", len(pos))
        out += struct.pack("<6f", *c, *e)
        out += struct.pack("<I", len(idx))
        for p in pos:
            out += struct.pack("<3f", *p)
        out += struct.pack("<%dH" % len(idx), *idx)
    return bytes(out)


# Version 2: geometry plus a channel dict. All values are exact in f32.
ENTRIES_V2 = [
    ("V2 All Channels", (0.5, 1.0, -0.25), (1.5, 1.0, 2.0),
     [(-1, 0, -2.25), (2, 0, -2.25), (2, 2, 1.75), (-1, 2, 1.75)], [0, 1, 2, 0, 2, 3],
     {"normals": [(0, 1, 0)] * 4,
      "tangents": [(1, 0, 0, 1), (1, 0, 0, 1), (1, 0, 0, -1), (1, 0, 0, -1)],
      "colors": [(255, 0, 0, 255), (0, 255, 0, 128), (0, 0, 255, 64), (1, 2, 3, 0)],
      "uv": [(0, 0), (1, 0), (1, 1), (0, 1)],
      "uv2": [(0.25, 0.5), (0.75, 0.5), (0.75, 0.125), (0.25, 0.125)],
      "uv3": [(2, 3)] * 4,
      "uv4": [(-1, -0.5)] * 4}),
    ("V2 Normals+UV é", (0, 0.5, 0), (0.5, 0.5, 0.5),
     [(0, 0, 0), (0.5, 1, 0), (-0.5, 0, 0.5)], [0, 1, 2],
     {"normals": [(0, 0, 1), (0, 0.5, 0.5), (0.5, 0.5, 0)],
      "uv": [(0, 0), (0.5, 1), (1, 0)]}),
    ("V2 Bare", (0, 0.5, 0), (0.5, 0.5, 0.5),
     [(0, 0, 0), (-0.5, 1, 0), (0.5, 0, 0.5)], [0, 2, 1], {}),
]
V2_ORDER = ("normals", "tangents", "colors", "uv", "uv2", "uv3", "uv4")  # ascending mask bit
V2_FORMAT = {"normals": "<3f", "tangents": "<4f", "colors": "<4B",
             "uv": "<2f", "uv2": "<2f", "uv3": "<2f", "uv4": "<2f"}


def build_v2():
    out = bytearray(b"CS1MESH\0" + struct.pack("<II", 2, len(ENTRIES_V2)))
    for name, c, e, pos, idx, ch in ENTRIES_V2:
        nb = name.encode("utf-8")
        mask = sum(1 << i for i, k in enumerate(V2_ORDER) if k in ch)
        out += struct.pack("<H", len(nb)) + nb
        out += struct.pack("<I", len(pos))
        out += struct.pack("<6f", *c, *e)
        out += struct.pack("<II", len(idx), mask)
        for p in pos:
            out += struct.pack("<3f", *p)
        out += struct.pack("<%dH" % len(idx), *idx)
        for k in V2_ORDER:
            for v in ch.get(k, ()):
                out += struct.pack(V2_FORMAT[k], *v)
    return bytes(out)


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    os.makedirs(os.path.join(here, "data"), exist_ok=True)
    with open(os.path.join(here, "data", "meshes-golden-v2.bin"), "wb") as f:
        f.write(build_v2())
