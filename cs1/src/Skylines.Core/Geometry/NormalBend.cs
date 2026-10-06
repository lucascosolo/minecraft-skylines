using System;

namespace Skylines.Core.Geometry
{
    /// <summary>
    /// Softens directional lighting on a mesh by bending its normals toward +Y: n' = normalize((1 - k) n + k up).
    /// k = 0 keeps the true normal (hard sun-lit / shaded sides); k = 1 makes every face light like a floor.
    /// Faces keep their orientation toward the sun, so the sunny side stays brighter (owner's preference).
    /// </summary>
    public static class NormalBend
    {
        /// <summary>Bends (nx, ny, nz) toward up by <paramref name="k"/> (clamped to 0..1) and renormalises.</summary>
        public static void Apply(float nx, float ny, float nz, float k, out float ox, out float oy, out float oz)
        {
            if (k <= 0f) { ox = nx; oy = ny; oz = nz; return; }
            if (k > 1f) k = 1f;
            float x = (1f - k) * nx, y = (1f - k) * ny + k, z = (1f - k) * nz;
            float len = (float)Math.Sqrt(x * x + y * y + z * z);
            if (len < 1e-6f) { ox = 0f; oy = 1f; oz = 0f; return; }
            ox = x / len; oy = y / len; oz = z / len;
        }
    }
}
