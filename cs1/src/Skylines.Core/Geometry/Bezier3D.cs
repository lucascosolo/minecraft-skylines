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

        /// <summary>The same curve over parameters [t0, t1], reparameterised to [0, 1].</summary>
        public Bezier3D Cut(float t0, float t1)
        {
            float[] p0 = At(t0), p3 = At(t1), d0 = Derivative(t0), d1 = Derivative(t1);
            float k = (t1 - t0) / 3f;
            return new Bezier3D
            {
                Ax = p0[0], Ay = p0[1], Az = p0[2],
                Bx = p0[0] + d0[0] * k, By = p0[1] + d0[1] * k, Bz = p0[2] + d0[2] * k,
                Cx = p3[0] - d1[0] * k, Cy = p3[1] - d1[1] * k, Cz = p3[2] - d1[2] * k,
                Dx = p3[0], Dy = p3[1], Dz = p3[2],
            };
        }

        private float[] At(float t)
        {
            float u = 1 - t, a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
            return new[] { a * Ax + b * Bx + c * Cx + d * Dx, a * Ay + b * By + c * Cy + d * Dy, a * Az + b * Bz + c * Cz + d * Dz };
        }

        private float[] Derivative(float t)
        {
            float u = 1 - t, a = 3 * u * u, b = 6 * u * t, c = 3 * t * t;
            return new[]
            {
                a * (Bx - Ax) + b * (Cx - Bx) + c * (Dx - Cx),
                a * (By - Ay) + b * (Cy - By) + c * (Dy - Cy),
                a * (Bz - Az) + b * (Cz - Bz) + c * (Dz - Cz),
            };
        }
    }
}
