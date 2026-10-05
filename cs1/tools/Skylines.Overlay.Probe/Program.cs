using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Skylines.Host.Overlay;

namespace Skylines.Overlay.Probe
{
    /// <summary>
    /// Consumes an overlay file the way CS1 does (host side of the triple buffer) for a cross-language test:
    /// one line per second with frames seen, latest frameId, size and the first pixel's bytes. Exit code 0
    /// when at least one frame was seen, 1 when none, 2 on usage or open errors.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            double seconds;
            if (args.Length != 2 || !double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) || seconds <= 0)
            {
                Console.Error.WriteLine("usage: probe <path> <seconds>");
                return 2;
            }
            SharedOverlayReader r;
            try { r = SharedOverlayReader.Open(args[0]); }
            catch (Exception e)
            {
                Console.Error.WriteLine("open failed: " + e.Message);
                return 2;
            }
            using (r)
            {
                Console.WriteLine("mapped {0}: max {1}x{2}, generation {3}, published {4}", r.Path, r.MaxWidth, r.MaxHeight, r.Generation, r.FramesPublished);
                var clock = Stopwatch.StartNew();
                double nextReport = 1;
                long lastReported = 0;
                while (clock.Elapsed.TotalSeconds < seconds)
                {
                    r.Acquire();
                    if (clock.Elapsed.TotalSeconds >= nextReport)
                    {
                        Report(r, r.FramesAcquired - lastReported);
                        lastReported = r.FramesAcquired;
                        nextReport += 1;
                    }
                    Thread.Sleep(2);
                }
                Report(r, r.FramesAcquired - lastReported);
                Console.WriteLine("total frames {0}, rejected {1}, published by guest {2}", r.FramesAcquired, r.FramesRejected, r.FramesPublished);
                return r.FramesAcquired > 0 ? 0 : 1;
            }
        }

        private static void Report(SharedOverlayReader r, long framesThisInterval)
        {
            if (!r.HasFrame)
            {
                Console.WriteLine("frames {0} (+{1}), no frame yet", r.FramesAcquired, framesThisInterval);
                return;
            }
            OverlayFrame f = r.Front;
            var px = new byte[4];
            Marshal.Copy(f.Pixels, px, 0, 4);
            Console.WriteLine("frames {0} (+{1}), frameId {2}, {3}x{4}{5}, first pixel {6:x2} {7:x2} {8:x2} {9:x2}",
                r.FramesAcquired, framesThisInterval, f.FrameId, f.Width, f.Height, f.RowsBottomUp ? " bottom-up" : "", px[0], px[1], px[2], px[3]);
        }
    }
}
