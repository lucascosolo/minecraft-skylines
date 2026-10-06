namespace Skylines.Core.Geometry
{
    /// <summary>The window a raised pavement's height above its road edge must fall in to be emitted.</summary>
    public static class PavementStep
    {
        /// <summary>Lowest and highest kept step, metres.</summary>
        public const float Min = 0.05f, Max = 1.5f;

        /// <summary><paramref name="raw"/> when it lies in [Min, Max], else 0 (NaN, meaning no pedestrian lane, gives 0).</summary>
        public static float Keep(float raw)
        {
            return raw >= Min && raw <= Max ? raw : 0f;
        }

        /// <summary>Why <see cref="Keep"/> kept or dropped <paramref name="raw"/>, for the log.</summary>
        public static string Verdict(float raw)
        {
            if (float.IsNaN(raw)) return "dropped: no pedestrian lane beside this edge";
            string m = raw.ToString("0.000") + " m";
            if (raw < Min) return "dropped: " + m + " is below the " + Min + " m window";
            if (raw > Max) return "dropped: " + m + " is above the " + Max + " m window";
            return "kept: " + m;
        }
    }
}
