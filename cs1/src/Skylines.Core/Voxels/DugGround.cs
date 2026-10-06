using System;
using System.Collections.Generic;

namespace Skylines.Core.Voxels
{
    /// <summary>What a <see cref="DugQuad"/> is: a face of a solid cell, or a skirt band of an open column.</summary>
    public enum DugQuadKind : byte
    {
        /// <summary>Top face of a solid cell under a dug cell (normal +y).</summary>
        Floor,
        /// <summary>Bottom face of a solid cell over a dug cell (normal -y).</summary>
        Ceiling,
        /// <summary>Side face of a solid cell next to a dug cell.</summary>
        Wall,
        /// <summary>Vertical band on an open column's edge, from the neighbour's solid top up to the terrain surface.</summary>
        Skirt,
    }

    /// <summary>
    /// One quad in Minecraft coordinates. Corners A, B, C, D are counter-clockwise seen from the open side, so
    /// (B - A) x (C - A) points out of the solid into the cavity; triangles are (A, B, C) and (A, C, D).
    /// </summary>
    public struct DugQuad
    {
        /// <summary>Corners.</summary>
        public float Ax, Ay, Az, Bx, By, Bz, Cx, Cy, Cz, Dx, Dy, Dz;
        /// <summary>What it is.</summary>
        public DugQuadKind Kind;
    }

    /// <summary>
    /// The ground the player dug, in Minecraft coordinates (cell (x, y, z) spans [x, x+1] x [y, y+1] x [z, z+1]).
    /// A column's solid top is the highest cell whose centre lies below the terrain surface at the column centre
    /// (the shadow world's rule). A cell is dug when it has an edit (any state) and lies at or below its column's
    /// solid top; a column is open when its solid top cell is dug, so the terrain surface over it is removed.
    /// Not thread-safe.
    /// </summary>
    public sealed class DugGround
    {
        private readonly Func<int, int, float> _surface;
        private readonly Dictionary<long, HashSet<int>> _edits = new Dictionary<long, HashSet<int>>();
        private readonly Dictionary<long, int> _tops = new Dictionary<long, int>();

        /// <summary><paramref name="surfaceAtColumnCentre"/>(x, z): terrain surface height at (x + 0.5, z + 0.5).</summary>
        public DugGround(Func<int, int, float> surfaceAtColumnCentre)
        {
            if (surfaceAtColumnCentre == null) throw new ArgumentNullException("surfaceAtColumnCentre");
            _surface = surfaceAtColumnCentre;
        }

        /// <summary>Highest cell whose centre lies strictly below <paramref name="surface"/>: ceil(surface - 0.5) - 1.</summary>
        public static int SolidTop(float surface)
        {
            return (int)Math.Ceiling(surface - 0.5) - 1;
        }

        private static long Key(int x, int z) { return ((long)x << 32) | (uint)z; }

        /// <summary>Number of dug cells.</summary>
        public int DugCount
        {
            get
            {
                int n = 0;
                foreach (KeyValuePair<long, HashSet<int>> c in _edits)
                {
                    int top = TopOf((int)(c.Key >> 32), (int)c.Key);
                    foreach (int y in c.Value) if (y <= top) n++;
                }
                return n;
            }
        }

        /// <summary>Records whether the position has an edit.</summary>
        public void Set(int x, int y, int z, bool edited)
        {
            long k = Key(x, z);
            HashSet<int> ys;
            if (edited)
            {
                if (!_edits.TryGetValue(k, out ys)) _edits[k] = ys = new HashSet<int>();
                ys.Add(y);
            }
            else if (_edits.TryGetValue(k, out ys) && ys.Remove(y) && ys.Count == 0)
            {
                _edits.Remove(k);
            }
        }

        /// <summary>Forgets every edit and every cached column surface.</summary>
        public void Clear()
        {
            _edits.Clear();
            _tops.Clear();
        }

        /// <summary>Forgets the cached column surfaces (the terrain changed); edits are kept.</summary>
        public void ForgetSurface()
        {
            _tops.Clear();
        }

        /// <summary>The column's solid top (cached).</summary>
        public int TopOf(int x, int z)
        {
            long k = Key(x, z);
            int top;
            if (!_tops.TryGetValue(k, out top)) _tops[k] = top = SolidTop(_surface(x, z));
            return top;
        }

        /// <summary>Edited and at or below the column's solid top.</summary>
        public bool IsDug(int x, int y, int z)
        {
            HashSet<int> ys;
            return _edits.TryGetValue(Key(x, z), out ys) && ys.Contains(y) && y <= TopOf(x, z);
        }

        /// <summary>At or below the column's solid top and not dug.</summary>
        public bool IsSolid(int x, int y, int z)
        {
            return y <= TopOf(x, z) && !IsDug(x, y, z);
        }

        /// <summary>The column's solid top cell is dug.</summary>
        public bool IsOpen(int x, int z)
        {
            return _edits.ContainsKey(Key(x, z)) && IsDug(x, TopOf(x, z), z);
        }

        /// <summary>Every open column with minX &lt;= x &lt; maxX and minZ &lt;= z &lt; maxZ, as (x, z) pairs sorted by x then z.</summary>
        public List<KeyValuePair<int, int>> OpenColumns(int minX, int minZ, int maxX, int maxZ)
        {
            var list = new List<KeyValuePair<int, int>>();
            foreach (long k in _edits.Keys)
            {
                int x = (int)(k >> 32), z = (int)k;
                if (x >= minX && x < maxX && z >= minZ && z < maxZ && IsOpen(x, z)) list.Add(new KeyValuePair<int, int>(x, z));
            }
            list.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.CompareTo(b.Value));
            return list;
        }

        /// <summary>
        /// For every dug cell with minX &lt;= x &lt; maxX and minZ &lt;= z &lt; maxZ, one quad on each of its six faces whose
        /// neighbour cell is solid (<see cref="IsSolid"/>), lying on the shared face and facing into the dug cell.
        /// </summary>
        public void Faces(int minX, int minZ, int maxX, int maxZ, List<DugQuad> into)
        {
            foreach (KeyValuePair<long, HashSet<int>> c in _edits)
            {
                int x = (int)(c.Key >> 32), z = (int)c.Key;
                if (x < minX || x >= maxX || z < minZ || z >= maxZ) continue;
                int top = TopOf(x, z);
                foreach (int y in c.Value)
                {
                    if (y > top) continue;
                    // Quad(o, u, v) faces u x v.
                    if (IsSolid(x, y - 1, z)) into.Add(Quad(x, y, z, 0, 0, 1, 1, 0, 0, DugQuadKind.Floor));
                    if (IsSolid(x, y + 1, z)) into.Add(Quad(x, y + 1, z, 1, 0, 0, 0, 0, 1, DugQuadKind.Ceiling));
                    if (IsSolid(x - 1, y, z)) into.Add(Quad(x, y, z, 0, 1, 0, 0, 0, 1, DugQuadKind.Wall));
                    if (IsSolid(x + 1, y, z)) into.Add(Quad(x + 1, y, z, 0, 0, 1, 0, 1, 0, DugQuadKind.Wall));
                    if (IsSolid(x, y, z - 1)) into.Add(Quad(x, y, z, 1, 0, 0, 0, 1, 0, DugQuadKind.Wall));
                    if (IsSolid(x, y, z + 1)) into.Add(Quad(x, y, z + 1, 0, 1, 0, 1, 0, 0, DugQuadKind.Wall));
                }
            }
        }

        private static DugQuad Quad(float ox, float oy, float oz, float ux, float uy, float uz, float vx, float vy, float vz, DugQuadKind kind)
        {
            return new DugQuad
            {
                Ax = ox, Ay = oy, Az = oz,
                Bx = ox + ux, By = oy + uy, Bz = oz + uz,
                Cx = ox + ux + vx, Cy = oy + uy + vy, Cz = oz + uz + vz,
                Dx = ox + vx, Dy = oy + vy, Dz = oz + vz,
                Kind = kind,
            };
        }

        /// <summary>
        /// For every open column O in the range and each of its four side neighbours N that is not open: a vertical quad
        /// on their shared edge, facing into O, from lo = min(TopOf(N), TopOf(O)) + 1 up to
        /// max(lo, <paramref name="surfaceAt"/>(corner x, corner z)) at each of the edge's two corners; none when both
        /// corners' surfaces are at or below lo.
        /// </summary>
        public void Skirts(int minX, int minZ, int maxX, int maxZ, Func<float, float, float> surfaceAt, List<DugQuad> into)
        {
            foreach (KeyValuePair<int, int> o in OpenColumns(minX, minZ, maxX, maxZ))
            {
                int x = o.Key, z = o.Value;
                // Edge p1 -> p2 runs so that (p2 - p1) x up points into O.
                Skirt(x, z, x - 1, z, x, z + 1, x, z, surfaceAt, into);
                Skirt(x, z, x + 1, z, x + 1, z, x + 1, z + 1, surfaceAt, into);
                Skirt(x, z, x, z - 1, x, z, x + 1, z, surfaceAt, into);
                Skirt(x, z, x, z + 1, x + 1, z + 1, x, z + 1, surfaceAt, into);
            }
        }

        private void Skirt(int x, int z, int nx, int nz, float p1x, float p1z, float p2x, float p2z, Func<float, float, float> surfaceAt, List<DugQuad> into)
        {
            if (IsOpen(nx, nz)) return;
            float lo = Math.Min(TopOf(nx, nz), TopOf(x, z)) + 1;
            float t1 = surfaceAt(p1x, p1z), t2 = surfaceAt(p2x, p2z);
            if (t1 <= lo && t2 <= lo) return;
            into.Add(new DugQuad
            {
                Ax = p1x, Ay = lo, Az = p1z,
                Bx = p2x, By = lo, Bz = p2z,
                Cx = p2x, Cy = Math.Max(lo, t2), Cz = p2z,
                Dx = p1x, Dy = Math.Max(lo, t1), Dz = p1z,
                Kind = DugQuadKind.Skirt,
            });
        }
    }
}
