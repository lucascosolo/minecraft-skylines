using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Unity-free part of the in-game self-test, compiled into MinecraftSkylines.Mod.Tests as well.
namespace MinecraftSkylines.Mod.SelfTest
{
    internal enum ScenarioStatus { Pass, Fail, Skip, Error }

    /// <summary>Outcome of one scenario. Measurements keep insertion order; values are null, bool, int, long, double, string, double[], int[] or a nested Measurements.</summary>
    internal sealed class ScenarioResult
    {
        public string Id = "";
        public string Name = "";
        public ScenarioStatus? Status;
        public string Reason = "";
        public double DurationMs;
        public readonly Measurements Measurements = new Measurements();

        public void Pass(string reason) { Set(ScenarioStatus.Pass, reason); }
        public void Fail(string reason) { Set(ScenarioStatus.Fail, reason); }
        public void Skip(string reason) { Set(ScenarioStatus.Skip, reason); }
        public void Error(string reason) { Set(ScenarioStatus.Error, reason); }

        private void Set(ScenarioStatus status, string reason)
        {
            Status = status;
            Reason = reason ?? "";
        }
    }

    /// <summary>Ordered key/value measurements; setting an existing key replaces its value in place.</summary>
    internal sealed class Measurements
    {
        private readonly List<KeyValuePair<string, object>> _items = new List<KeyValuePair<string, object>>();

        public void Set(string key, object value)
        {
            int i = _items.FindIndex(kv => kv.Key == key);
            if (i >= 0) _items[i] = new KeyValuePair<string, object>(key, value);
            else _items.Add(new KeyValuePair<string, object>(key, value));
        }

        public object Get(string key)
        {
            int i = _items.FindIndex(kv => kv.Key == key);
            return i >= 0 ? _items[i].Value : null;
        }

        public int Count { get { return _items.Count; } }

        public IEnumerable<KeyValuePair<string, object>> Items { get { return _items; } }

        public Measurements With(string key, object value)
        {
            Set(key, value);
            return this;
        }
    }

    internal sealed class SelfTestReport
    {
        public DateTime RunStartedUtc;
        public string GameVersion;
        public string ModVersion;
        public string CityName;
        public string MinecraftPeer;
        public bool Aborted;
        public string AbortReason;
        public readonly List<ScenarioResult> Scenarios = new List<ScenarioResult>();

        public int Count(ScenarioStatus s)
        {
            int n = 0;
            foreach (ScenarioResult r in Scenarios)
                if ((r.Status ?? ScenarioStatus.Error) == s) n++;
            return n;
        }

        public string SummaryLine(string reportPath)
        {
            return "Self-test: " + Count(ScenarioStatus.Pass) + " pass, " + Count(ScenarioStatus.Fail) + " fail, "
                + Count(ScenarioStatus.Skip) + " skip, " + Count(ScenarioStatus.Error) + " error"
                + (Aborted ? " (aborted: " + AbortReason + ")" : "") + " \u2014 report at " + reportPath;
        }

        public string ToJson()
        {
            var scenarios = new List<object>();
            foreach (ScenarioResult r in Scenarios)
            {
                scenarios.Add(new Measurements()
                    .With("id", r.Id).With("name", r.Name)
                    .With("status", (r.Status ?? ScenarioStatus.Error).ToString().ToLowerInvariant())
                    .With("reason", r.Reason).With("duration_ms", r.DurationMs).With("measurements", r.Measurements));
            }
            var summary = new Measurements()
                .With("pass", Count(ScenarioStatus.Pass)).With("fail", Count(ScenarioStatus.Fail))
                .With("skip", Count(ScenarioStatus.Skip)).With("error", Count(ScenarioStatus.Error));
            return Json.Write(new Measurements()
                .With("run_started_utc", Json.Iso(RunStartedUtc)).With("game_version", GameVersion).With("mod_version", ModVersion)
                .With("city_name", CityName).With("minecraft_peer", MinecraftPeer).With("aborted", Aborted)
                .With("abort_reason", AbortReason).With("scenarios", scenarios).With("summary", summary));
        }
    }

    internal static class Json
    {
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            Append(sb, value);
            return sb.ToString();
        }

        public static string Iso(DateTime utc)
        {
            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        private static void Append(StringBuilder sb, object v)
        {
            if (v == null) sb.Append("null");
            else if (v is bool) sb.Append((bool)v ? "true" : "false");
            else if (v is string) Quote(sb, (string)v);
            else if (v is double || v is float)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
                else sb.Append(v is float ? ((float)v).ToString("R", CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture));
            }
            else if (v is int || v is long || v is short || v is ushort || v is uint || v is byte)
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
            else if (v is Measurements) Object(sb, ((Measurements)v).Items);
            else if (v is IDictionary<string, object>) Object(sb, (IDictionary<string, object>)v);
            else if (v is IEnumerable)
            {
                sb.Append('[');
                bool first = true;
                foreach (object o in (IEnumerable)v)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Append(sb, o);
                }
                sb.Append(']');
            }
            else Quote(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        private static void Object(StringBuilder sb, IEnumerable<KeyValuePair<string, object>> items)
        {
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> kv in items)
            {
                if (!first) sb.Append(',');
                first = false;
                Quote(sb, kv.Key);
                sb.Append(':');
                Append(sb, kv.Value);
            }
            sb.Append('}');
        }

        private static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    /// <summary>A scenario: a coroutine run one step per frame, with a timeout and a cleanup that always runs.</summary>
    internal sealed class Scenario
    {
        public string Id;
        public string Name;
        public double TimeoutSeconds;
        public Func<ScenarioResult, IEnumerator> Body;
        public Action Cleanup;
    }

    /// <summary>Runs scenarios one after another on an injected clock (seconds).</summary>
    internal sealed class ScenarioRunner
    {
        private readonly IList<Scenario> _scenarios;
        private readonly Action<string, Exception> _onError;
        private readonly List<ScenarioResult> _results = new List<ScenarioResult>();
        private int _next;
        private Scenario _scenario;
        private IEnumerator _steps;
        private double _start;

        public ScenarioRunner(IList<Scenario> scenarios, Action<string, Exception> onError)
        {
            _scenarios = scenarios;
            _onError = onError;
            Finished = scenarios.Count == 0;
        }

        public bool Finished { get; private set; }
        public bool Aborted { get; private set; }
        public string AbortReason { get; private set; }
        public ScenarioResult Current { get; private set; }
        public List<ScenarioResult> Results { get { return _results; } }

        public void Tick(double nowSeconds)
        {
            if (Finished) return;
            if (Current == null)
            {
                _scenario = _scenarios[_next++];
                Current = new ScenarioResult { Id = _scenario.Id, Name = _scenario.Name };
                _start = nowSeconds;
                _steps = null;
                Step(nowSeconds, true);
                return;
            }
            if (nowSeconds - _start > _scenario.TimeoutSeconds)
            {
                Current.Fail("timed out after " + _scenario.TimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " s");
                End(nowSeconds);
                return;
            }
            Step(nowSeconds, false);
        }

        public void Abort(string reason, double nowSeconds)
        {
            if (Finished) return;
            if (Current != null)
            {
                Current.Error("aborted: " + reason);
                End(nowSeconds);
            }
            while (_next < _scenarios.Count)
            {
                Scenario s = _scenarios[_next++];
                var r = new ScenarioResult { Id = s.Id, Name = s.Name };
                r.Skip("aborted");
                _results.Add(r);
            }
            Aborted = true;
            AbortReason = reason;
            Finished = true;
        }

        private void Step(double nowSeconds, bool first)
        {
            try
            {
                if (first) _steps = _scenario.Body(Current);
                if (_steps != null && _steps.MoveNext()) return;
                if (Current.Status == null) Current.Error("scenario ended without a verdict");
            }
            catch (Exception e)
            {
                Current.Error("exception: " + e.Message);
                Report(e);
            }
            End(nowSeconds);
        }

        private void End(double nowSeconds)
        {
            ScenarioResult r = Current;
            r.DurationMs = (nowSeconds - _start) * 1000.0;
            Current = null;
            _steps = null;
            if (_scenario.Cleanup != null)
            {
                try { _scenario.Cleanup(); }
                catch (Exception e) { Report(e); }
            }
            _results.Add(r);
            if (_next >= _scenarios.Count) Finished = true;
        }

        private void Report(Exception e)
        {
            if (_onError == null) return;
            try { _onError(_scenario.Id, e); }
            catch (Exception) { }
        }
    }

    internal struct Summary
    {
        public int Count;
        public double? Min, Max, Mean, MaxAbs;
    }

    internal static class SelfTestMath
    {
        private const double BaryTolerance = 1e-6;

        public static Summary Summarize(IList<double> values)
        {
            var s = new Summary { Count = values.Count };
            if (values.Count == 0) return s;
            double min = double.MaxValue, max = double.MinValue, sum = 0, maxAbs = 0;
            foreach (double v in values)
            {
                min = Math.Min(min, v);
                max = Math.Max(max, v);
                maxAbs = Math.Max(maxAbs, Math.Abs(v));
                sum += v;
            }
            s.Min = min; s.Max = max; s.Mean = sum / values.Count; s.MaxAbs = maxAbs;
            return s;
        }

        public static int[] Histogram(IList<double> values, double[] edges)
        {
            var h = new int[edges.Length + 1];
            foreach (double v in values)
            {
                int i = 0;
                while (i < edges.Length && v >= edges[i]) i++;
                h[i]++;
            }
            return h;
        }

        public static double? TopNear(float[] positions, ushort[] flags, int count, double x, double z, ushort mask, double refY, double window)
        {
            double? best = null;
            for (int t = 0; t < count; t++)
            {
                if ((flags[t] & mask) == 0) continue;
                int o = 9 * t;
                double ax = positions[o], ay = positions[o + 1], az = positions[o + 2];
                double ux = positions[o + 3] - ax, uy = positions[o + 4] - ay, uz = positions[o + 5] - az;
                double vx = positions[o + 6] - ax, vy = positions[o + 7] - ay, vz = positions[o + 8] - az;
                double ny = uz * vx - ux * vz;
                if (ny <= 0) continue;
                // Solve (x - ax, z - az) = s * (ux, uz) + w * (vx, vz) in the xz plane.
                double det = ux * vz - uz * vx;
                if (Math.Abs(det) < 1e-12) continue;
                double px = x - ax, pz = z - az;
                double s = (px * vz - pz * vx) / det, w = (ux * pz - uz * px) / det;
                if (s < -BaryTolerance || w < -BaryTolerance || s + w > 1 + BaryTolerance) continue;
                double y = ay + s * uy + w * vy;
                if (Math.Abs(y - refY) > window) continue;
                if (best == null || Math.Abs(y - refY) < Math.Abs(best.Value - refY)) best = y;
            }
            return best;
        }

        public static double LateralDistance(double[] polylineXZ, double x, double z)
        {
            int n = polylineXZ.Length / 2;
            if (n < 2) return double.PositiveInfinity;
            double best = double.PositiveInfinity;
            for (int i = 0; i + 1 < n; i++)
            {
                double ax = polylineXZ[2 * i], az = polylineXZ[2 * i + 1];
                double dx = polylineXZ[2 * i + 2] - ax, dz = polylineXZ[2 * i + 3] - az;
                double len2 = dx * dx + dz * dz;
                double t = len2 > 0 ? Math.Max(0, Math.Min(1, ((x - ax) * dx + (z - az) * dz) / len2)) : 0;
                double ex = ax + t * dx - x, ez = az + t * dz - z;
                best = Math.Min(best, Math.Sqrt(ex * ex + ez * ez));
            }
            return best;
        }

        public static double YawTowards(double dx, double dz)
        {
            if (dx == 0 && dz == 0) return 0;
            double deg = Math.Atan2(dx, dz) * 180.0 / Math.PI;
            if (deg < 0) deg += 360.0;
            return deg >= 360.0 ? 0 : deg;
        }

        public static double SlopeDegrees(double hxMinus, double hxPlus, double hzMinus, double hzPlus, double step)
        {
            double gx = (hxPlus - hxMinus) / (2 * step), gz = (hzPlus - hzMinus) / (2 * step);
            return Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180.0 / Math.PI;
        }

        public static double PerSecond(long count, double seconds)
        {
            return seconds > 0 ? count / seconds : 0;
        }

        public static bool Within(double? a, double? b, double tolerance)
        {
            return a.HasValue && b.HasValue && Math.Abs(a.Value - b.Value) <= tolerance;
        }
    }

    /// <summary>Longest continuous time not on the ground, fed samples in time order.</summary>
    internal sealed class AirborneTracker
    {
        private double? _airSince;

        public void Sample(double nowSeconds, bool onGround)
        {
            if (!onGround && _airSince == null) _airSince = nowSeconds;
            if (_airSince != null) LongestSeconds = Math.Max(LongestSeconds, nowSeconds - _airSince.Value);
            if (onGround) _airSince = null;
        }

        public double LongestSeconds { get; private set; }
    }
    /// <summary>S8: a row of test blocks a few metres in front of the player, axis-aligned, in Minecraft coordinates.</summary>
    internal sealed class BlockRowPlan
    {
        public static readonly string[] Blocks =
        {
            "minecraft:stone", "minecraft:grass_block", "minecraft:oak_planks", "minecraft:glass",
            "minecraft:oak_leaves[persistent=true]", "minecraft:water",
        };

        public int FeetBlockX, FeetBlockY, FeetBlockZ;
        public int ForwardX, ForwardZ;
        public int[] BlockX, BlockZ;

        public static double SnapYaw(double unityYawDeg)
        {
            double s = Math.Round(unityYawDeg / 90.0) * 90.0 % 360.0;
            return s < 0 ? s + 360.0 : s;
        }

        public static BlockRowPlan Create(double mcX, double mcY, double mcZ, double unityYawDeg, int distance)
        {
            int q = (int)(SnapYaw(unityYawDeg) / 90.0);
            var p = new BlockRowPlan
            {
                FeetBlockX = (int)Math.Floor(mcX),
                FeetBlockY = (int)Math.Floor(mcY + 0.001),
                FeetBlockZ = (int)Math.Floor(mcZ),
                ForwardX = new[] { 0, 1, 0, -1 }[q],
                ForwardZ = new[] { -1, 0, 1, 0 }[q],
                BlockX = new int[Blocks.Length],
                BlockZ = new int[Blocks.Length],
            };
            int cx = p.FeetBlockX + distance * p.ForwardX, cz = p.FeetBlockZ + distance * p.ForwardZ;
            int lx = -p.ForwardZ, lz = p.ForwardX;
            for (int i = 0; i < Blocks.Length; i++)
            {
                p.BlockX[i] = cx + (i - 2) * lx;
                p.BlockZ[i] = cz + (i - 2) * lz;
            }
            return p;
        }

        public string[] BuildCommands()
        {
            var c = new string[Blocks.Length];
            for (int i = 0; i < Blocks.Length; i++)
                c[i] = Fill(BlockX[i], BlockZ[i], BlockX[i], BlockZ[i], Blocks[i]);
            return c;
        }

        public string ClearCommand()
        {
            int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
            for (int i = 0; i < Blocks.Length; i++)
            {
                minX = Math.Min(minX, BlockX[i]); maxX = Math.Max(maxX, BlockX[i]);
                minZ = Math.Min(minZ, BlockZ[i]); maxZ = Math.Max(maxZ, BlockZ[i]);
            }
            return Fill(minX, minZ, maxX, maxZ, "minecraft:air");
        }

        public double GapToRow(double mcX, double mcZ)
        {
            bool alongX = ForwardX != 0;
            int f = alongX ? ForwardX : ForwardZ;
            int c = alongX ? BlockX[0] : BlockZ[0];
            double p = alongX ? mcX : mcZ;
            double nearFace = f > 0 ? c : c + 1;
            return f * (nearFace - p) - 0.3;
        }

        public bool Covers(int sx, int sy, int sz)
        {
            for (int i = 0; i < Blocks.Length; i++)
                for (int y = FeetBlockY; y <= FeetBlockY + 1; y++)
                    if (Section(BlockX[i]) == sx && Section(y) == sy && Section(BlockZ[i]) == sz) return true;
            return false;
        }

        private static int Section(int b) { return b >> 4; }

        private string Fill(int x0, int z0, int x1, int z1, string block)
        {
            return string.Format(CultureInfo.InvariantCulture, "fill {0} {1} {2} {3} {4} {5} {6}",
                x0, FeetBlockY, z0, x1, FeetBlockY + 1, z1, block);
        }
    }
}
