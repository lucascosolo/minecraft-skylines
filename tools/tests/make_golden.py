"""Builds data/meshes-golden.bin with struct only, independent of tools/cs1_meshes.py."""
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


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    os.makedirs(os.path.join(here, "data"), exist_ok=True)
    with open(os.path.join(here, "data", "meshes-golden.bin"), "wb") as f:
        f.write(build())
