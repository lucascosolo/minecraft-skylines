import os
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
sys.path.insert(0, HERE)

import cs1_meshes as m  # noqa: E402
from make_golden import ENTRIES  # noqa: E402

GOLDEN = os.path.join(HERE, "data", "meshes-golden.bin")


def golden_entries():
    return [m.MeshEntry(n, tuple(map(float, c)), tuple(map(float, e)),
                        [tuple(map(float, p)) for p in pos], list(idx))
            for n, c, e, pos, idx in ENTRIES]


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


class WriteCache(unittest.TestCase):
    def test_constants(self):
        self.assertEqual(m.MAGIC, b"CS1MESH\0")
        self.assertEqual(m.VERSION, 1)
        self.assertEqual(m.MAX_VERTICES, 65535)

    def test_bytes_match_golden(self):
        p = os.path.join(fresh_dir(), "sub", "dir", "out.bin")
        self.assertEqual(m.write_cache(p, golden_entries()), 3)
        with open(p, "rb") as f:
            self.assertEqual(f.read(), golden_bytes())

    def test_overwrite_and_no_tmp_left(self):
        d = fresh_dir()
        p = os.path.join(d, "out.bin")
        with open(p, "wb") as f:
            f.write(b"old")
        m.write_cache(p, golden_entries())
        with open(p, "rb") as f:
            self.assertEqual(f.read(), golden_bytes())
        self.assertEqual([n for n in os.listdir(d) if n.endswith(".tmp")], [])
        self.assertEqual(os.listdir(d), ["out.bin"])


class ReadCache(unittest.TestCase):
    def test_reads_golden(self):
        self.assertEqual(list(m.read_cache(GOLDEN)), golden_entries())

    def test_roundtrip(self):
        p = os.path.join(fresh_dir(), "rt.bin")
        m.write_cache(p, golden_entries())
        self.assertEqual(list(m.read_cache(p)), golden_entries())

    def test_bad_magic(self):
        b = golden_bytes()
        with self.assertRaises(ValueError):
            m.read_cache(write_raw(b"XS1MESH\0" + b[8:]))

    def test_bad_version(self):
        b = golden_bytes()
        with self.assertRaises(ValueError):
            m.read_cache(write_raw(b[:8] + (2).to_bytes(4, "little") + b[12:]))

    def test_truncated_one_byte(self):
        with self.assertRaises(ValueError):
            m.read_cache(write_raw(golden_bytes()[:-1]))

    def test_truncated_ten_bytes(self):
        with self.assertRaises(ValueError):
            m.read_cache(write_raw(golden_bytes()[:-10]))


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
