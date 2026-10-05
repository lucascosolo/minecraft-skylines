using System.Diagnostics;

namespace Skylines.Bridge
{
    /// <summary>Monotonic millisecond clock (Stopwatch based; net35 has no TickCount64).</summary>
    internal static class Clock
    {
        private static readonly long Start = Stopwatch.GetTimestamp();

        /// <summary>Monotonic milliseconds since an arbitrary origin, always at least 1.</summary>
        public static long NowMs()
        {
            return (Stopwatch.GetTimestamp() - Start) * 1000 / Stopwatch.Frequency + 1;
        }
    }
}
