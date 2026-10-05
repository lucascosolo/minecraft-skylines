namespace Skylines.Core.Geometry
{
    /// <summary>Cubic bezier in 3D: control points a, b, c, d.</summary>
    public struct Bezier3D
    {
        /// <summary>Control point a (start).</summary>
        public float Ax, Ay, Az;
        /// <summary>Control point b.</summary>
        public float Bx, By, Bz;
        /// <summary>Control point c.</summary>
        public float Cx, Cy, Cz;
        /// <summary>Control point d (end).</summary>
        public float Dx, Dy, Dz;
    }
}
