import os
import struct
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
sys.path.insert(0, HERE)

import cs1_meshes as m  # noqa: E402
from make_golden import ENTRIES, ENTRIES_V2, build_v2  # noqa: E402

GOLDEN = os.path.join(HERE, "data", "meshes-golden.bin")  # version 1, frozen
GOLDEN_V2 = os.path.join(HERE, "data", "meshes-golden-v2.bin")
CHANNELS = ("normals", "tangents", "colors", "uv", "uv2", "uv3", "uv4")


def golden_entries():
    return [m.MeshEntry(n, tuple(map(float, c)), tuple(map(float, e)),
                        [tuple(map(float, p)) for p in pos], list(idx))
            for n, c, e, pos, idx in ENTRIES]


def golden_v2_entries():
    return [m.MeshEntry(n, tuple(map(float, c)), tuple(map(float, e)),
                        [tuple(map(float, p)) for p in pos], list(idx),
                        **{k: [tuple(v) for v in vs] for k, vs in ch.items()})
            for n, c, e, pos, idx, ch in ENTRIES_V2]


def fresh_dir():
    return tempfile.mkdtemp(prefix="cs1meshes-")


def golden_bytes():
    with open(GOLDEN, "rb") as f:
        return f.read()


def write_raw(data):
    p = os.path.join(fresh_dir(), "bad.bin")
    with open(p, "wb") as f:
        f.write(data)
    return p


def hand_v2(entries):
    """entries: (name, centre, extent, positions, indices, mask, channel_bytes). Layout spelled out here."""
    out = b"CS1MESH\0" + struct.pack("<II", 2, len(entries))
    for name, c, e, pos, idx, mask, chan in entries:
        nb = name.encode("utf-8")
        out += struct.pack("<H", len(nb)) + nb + struct.pack("<I", len(pos)) + struct.pack("<6f", *c, *e)
        out += struct.pack("<II", len(idx), mask)
        out += b"".join(struct.pack("<3f", *p) for p in pos) + struct.pack("<%dH" % len(idx), *idx) + chan
    return out


TRI = ("T", (0, 0, 0), (1, 1, 1), [(0, 0, 0), (1, 0, 0), (0, 1, 0)], [0, 1, 2])


def hand_tri(mask=0, chan=b""):
    return hand_v2([TRI + (mask, chan)])


class WriteCache(unittest.TestCase):
    def test_constants(self):
        self.assertEqual(m.MAGIC, b"CS1MESH\0")
        self.assertEqual(m.VERSION, 2)
        self.assertEqual(tuple(m.READABLE_VERSIONS), (1, 2))
        self.assertEqual(m.MAX_VERTICES, 65535)
        self.assertEqual([m.CH_NORMALS, m.CH_TANGENTS, m.CH_COLORS, m.CH_UV, m.CH_UV2, m.CH_UV3, m.CH_UV4],
                         [1, 2, 4, 8, 16, 32, 64])

    def test_v1_golden_still_reads(self):
        got = list(m.read_cache(GOLDEN))
        self.assertEqual(got, golden_entries())
        for e in got:
            for ch in CHANNELS:
                self.assertIsNone(getattr(e, ch))

    def test_write_matches_independent_v2_build(self):
        p = os.path.join(fresh_dir(), "sub", "dir", "out.bin")
        self.assertEqual(m.write_cache(p, golden_v2_entries()), 3)
        with open(p, "rb") as f:
            self.assertEqual(f.read(), build_v2())

    @unittest.skipUnless(os.path.exists(GOLDEN_V2),
                         "tools/tests/data/meshes-golden-v2.bin missing: run python3 tools/tests/make_golden.py")
    def test_bytes_match_golden_v2(self):
        with open(GOLDEN_V2, "rb") as f:
            golden = f.read()
        self.assertEqual(golden, build_v2())
        p = os.path.join(fresh_dir(), "out.bin")
        m.write_cache(p, golden_v2_entries())
        with open(p, "rb") as f:
            self.assertEqual(f.read(), golden)
        self.assertEqual(list(m.read_cache(GOLDEN_V2)), golden_v2_entries())

    def test_entry_without_channels_written_as_mask_zero(self):
        p = os.path.join(fresh_dir(), "out.bin")
        m.write_cache(p, golden_entries())
        with open(p, "rb") as f:
            data = f.read()
        self.assertEqual(struct.unpack_from("<II", data, 8), (2, 3))
        self.assertEqual(data, hand_v2([(e.name, e.center, e.extent, e.positions, e.indices, 0, b"")
                                        for e in golden_entries()]))

    def test_overwrite_and_no_tmp_left(self):
        d = fresh_dir()
        p = os.path.join(d, "out.bin")
        with open(p, "wb") as f:
            f.write(b"old")
        m.write_cache(p, golden_v2_entries())
        with open(p, "rb") as f:
            self.assertEqual(f.read(), build_v2())
        self.assertEqual([n for n in os.listdir(d) if n.endswith(".tmp")], [])
        self.assertEqual(os.listdir(d), ["out.bin"])


class ReadCache(unittest.TestCase):
    def test_roundtrip(self):
        p = os.path.join(fresh_dir(), "rt.bin")
        m.write_cache(p, golden_entries())
        self.assertEqual(list(m.read_cache(p)), golden_entries())

    def test_bad_magic(self):
        b = build_v2()
        with self.assertRaises(ValueError):
            m.read_cache(write_raw(b"XS1MESH\0" + b[8:]))

    def test_bad_version(self):
        b = golden_bytes()
        for v in (0, 3, 99):
            with self.assertRaises(ValueError, msg=str(v)):
                m.read_cache(write_raw(b[:8] + v.to_bytes(4, "little") + b[12:]))

    def test_truncated_one_byte(self):
        with self.assertRaises(ValueError):
            m.read_cache(write_raw(golden_bytes()[:-1]))

    def test_truncated_ten_bytes(self):
        with self.assertRaises(ValueError):
            m.read_cache(write_raw(golden_bytes()[:-10]))


class V2Format(unittest.TestCase):
    def test_hand_packed_all_channels(self):
        chan = (struct.pack("<9f", *([0, 0, 1] * 3))
                + struct.pack("<12f", *([1, 0, 0, 1] * 3))
                + bytes([255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 0])
                + struct.pack("<6f", 0, 0, 1, 0, 0, 1)
                + struct.pack("<6f", *([0.5] * 6))
                + struct.pack("<6f", *([2] * 6))
                + struct.pack("<6f", *([0.25] * 6)))
        (e,) = m.read_cache(write_raw(hand_tri(127, chan)))
        self.assertEqual(e.name, "T")
        self.assertEqual(e.positions, [(0, 0, 0), (1, 0, 0), (0, 1, 0)])
        self.assertEqual(e.indices, [0, 1, 2])
        self.assertEqual(e.normals, [(0, 0, 1)] * 3)
        self.assertEqual(e.tangents, [(1, 0, 0, 1)] * 3)
        self.assertEqual(e.colors, [(255, 0, 0, 255), (0, 255, 0, 128), (0, 0, 255, 0)])
        self.assertEqual(e.uv, [(0, 0), (1, 0), (0, 1)])
        self.assertEqual(e.uv2, [(0.5, 0.5)] * 3)
        self.assertEqual(e.uv3, [(2, 2)] * 3)
        self.assertEqual(e.uv4, [(0.25, 0.25)] * 3)

    def test_hand_packed_channels_in_ascending_bit_order(self):
        # mask 72 = uv (8) + uv4 (64); uv data comes first, uv4 second
        chan = struct.pack("<6f", 1, 2, 3, 4, 5, 6) + struct.pack("<6f", 7, 8, 9, 10, 11, 12)
        (e,) = m.read_cache(write_raw(hand_tri(72, chan)))
        self.assertEqual(e.uv, [(1, 2), (3, 4), (5, 6)])
        self.assertEqual(e.uv4, [(7, 8), (9, 10), (11, 12)])
        for ch in ("normals", "tangents", "colors", "uv2", "uv3"):
            self.assertIsNone(getattr(e, ch))

    def test_writer_output_equals_hand_packed(self):
        e = m.MeshEntry(*TRI, normals=[(0, 0, 1)] * 3, colors=[(1, 2, 3, 4)] * 3)
        p = os.path.join(fresh_dir(), "o.bin")
        m.write_cache(p, [e])
        chan = struct.pack("<9f", *([0, 0, 1] * 3)) + bytes([1, 2, 3, 4] * 3)
        with open(p, "rb") as f:
            self.assertEqual(f.read(), hand_tri(m.CH_NORMALS | m.CH_COLORS, chan))

    def test_roundtrip_all_channels(self):
        p = os.path.join(fresh_dir(), "rt.bin")
        entries = golden_v2_entries()
        m.write_cache(p, entries)
        got = m.read_cache(p)
        self.assertEqual(list(got), entries)
        self.assertIsInstance(got[0].normals[0], tuple)
        self.assertIsInstance(got[0].colors[0], tuple)
        self.assertIsNone(got[1].colors)
        self.assertIsNone(got[2].uv)

    def test_unknown_mask_bit(self):
        for mask in (128, 1 << 31, 128 | 1):
            with self.assertRaises(ValueError, msg=str(mask)):
                m.read_cache(write_raw(hand_tri(mask, b"\0" * 4096)))

    def test_truncated_inside_channel_data(self):
        chan = struct.pack("<9f", *([0, 0, 1] * 3)) + bytes([1, 2, 3, 4] * 3)
        good = hand_tri(m.CH_NORMALS | m.CH_COLORS, chan)
        self.assertEqual(len(m.read_cache(write_raw(good))), 1)
        for cut in (1, 5, 13):
            with self.assertRaises(ValueError, msg=str(cut)):
                m.read_cache(write_raw(good[:-cut]))

    def test_mask_promises_channel_but_data_absent(self):
        with self.assertRaises(ValueError):
            m.read_cache(write_raw(hand_tri(64, b"")))


class ChannelHelper(unittest.TestCase):
    def test_missing_or_empty_or_wrong_length(self):
        self.assertIsNone(m.channel(None, 3, 3))
        self.assertIsNone(m.channel([], 3, 3))
        self.assertIsNone(m.channel([(0, 0, 1)] * 2, 3, 3))
        self.assertIsNone(m.channel([(0, 0, 1)] * 4, 3, 3))

    def test_exact_dim(self):
        self.assertEqual(m.channel([(0, 0, 1), (1, 0, 0)], 2, 3), [(0, 0, 1), (1, 0, 0)])

    def test_truncates_to_dim(self):
        self.assertEqual(m.channel([(0, 0, 1, 1)] * 2, 2, 3), [(0, 0, 1)] * 2)
        self.assertEqual(m.channel([(1, 2, 3)] * 2, 2, 2), [(1, 2)] * 2)

    def test_returns_list_of_tuples(self):
        r = m.channel([[0, 1], [2, 3]], 2, 2)
        self.assertEqual(r, [(0, 1), (2, 3)])
        self.assertIsInstance(r, list)
        self.assertIsInstance(r[0], tuple)


class Colors32(unittest.TestCase):
    def test_missing_or_wrong_length(self):
        self.assertIsNone(m.colors32(None, 2))
        self.assertIsNone(m.colors32([], 2))
        self.assertIsNone(m.colors32([(1, 1, 1, 1)], 2))

    def test_float_colours_scaled(self):
        self.assertEqual(m.colors32([(1.0, 0.0, 0.25, 1.0), (0.0, 0.2, 1.0, 0.0)], 2),
                         [(255, 0, 64, 255), (0, 51, 255, 0)])

    def test_all_components_at_most_one_counts_as_float(self):
        self.assertEqual(m.colors32([(1, 0, 0, 1)], 1), [(255, 0, 0, 255)])

    def test_int_like_colours_rounded(self):
        self.assertEqual(m.colors32([(255, 128, 0, 255), (10.4, 20.6, 2, 3)], 2),
                         [(255, 128, 0, 255), (10, 21, 2, 3)])

    def test_one_component_above_one_makes_all_int_like(self):
        self.assertEqual(m.colors32([(1.0, 1.0, 2.0, 1.0)], 1), [(1, 1, 2, 1)])

    def test_clamped(self):
        self.assertEqual(m.colors32([(300, -5, 0, 255)], 1), [(255, 0, 0, 255)])
        self.assertEqual(m.colors32([(-0.5, 0.25, 1.0, 0.0)], 1), [(0, 64, 255, 0)])

    def test_result_is_list_of_int_tuples(self):
        r = m.colors32([(10, 20, 30, 40)], 1)
        self.assertIsInstance(r, list)
        self.assertIsInstance(r[0], tuple)
        self.assertTrue(all(isinstance(c, int) for c in r[0]))


class CheckMesh(unittest.TestCase):
    def test_ok(self):
        self.assertIsNone(m.check_mesh([(0, 0, 0)] * 3, [0, 1, 2]))

    def test_no_vertices(self):
        self.assertEqual(m.check_mesh([], [0, 1, 2]), "no vertices")

    def test_vertex_limit_boundary(self):
        self.assertIsNone(m.check_mesh([(0, 0, 0)] * 65535, [0, 1, 2]))
        self.assertEqual(m.check_mesh([(0, 0, 0)] * 65536, [0, 1, 2]), "over 65535 vertices")

    def test_no_triangles(self):
        self.assertEqual(m.check_mesh([(0, 0, 0)] * 3, []), "no triangles")

    def test_index_count_not_multiple(self):
        self.assertEqual(m.check_mesh([(0, 0, 0)] * 3, [0, 1, 2, 0]),
                         "index count not a multiple of 3")

    def test_index_out_of_range(self):
        self.assertEqual(m.check_mesh([(0, 0, 0)] * 3, [0, 1, 3]), "index out of range")
        self.assertEqual(m.check_mesh([(0, 0, 0)] * 3, [0, 1, -1]), "index out of range")


if __name__ == "__main__":
    unittest.main()
