using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using MinecraftSkylines.Mod.SelfTest;
using Xunit;

namespace MinecraftSkylines.Mod.Tests
{
    public class ScenarioResultTests
    {
        [Fact]
        public void Verdict_methods_set_status_and_reason()
        {
            var r = new ScenarioResult();
            r.Pass("a"); Assert.Equal(ScenarioStatus.Pass, r.Status); Assert.Equal("a", r.Reason);
            r.Fail("b"); Assert.Equal(ScenarioStatus.Fail, r.Status); Assert.Equal("b", r.Reason);
            r.Skip("c"); Assert.Equal(ScenarioStatus.Skip, r.Status); Assert.Equal("c", r.Reason);
            r.Error("d"); Assert.Equal(ScenarioStatus.Error, r.Status); Assert.Equal("d", r.Reason);
        }

        [Fact]
        public void Null_reason_becomes_empty()
        {
            var r = new ScenarioResult();
            r.Pass(null);
            Assert.Equal("", r.Reason);
        }
    }

    public class MeasurementsTests
    {
        [Fact]
        public void Set_appends_in_insertion_order()
        {
            var m = new Measurements();
            m.Set("b", 1); m.Set("a", 2); m.Set("c", 3);
            Assert.Equal(3, m.Count);
            Assert.Equal(new[] { "b", "a", "c" }, m.Items.Select(kv => kv.Key).ToArray());
        }

        [Fact]
        public void Set_existing_key_replaces_in_place()
        {
            var m = new Measurements();
            m.Set("a", 1); m.Set("b", 2); m.Set("a", 9);
            Assert.Equal(2, m.Count);
            Assert.Equal(9, m.Get("a"));
            Assert.Equal(new[] { "a", "b" }, m.Items.Select(kv => kv.Key).ToArray());
        }

        [Fact]
        public void Get_missing_is_null()
        {
            Assert.Null(new Measurements().Get("nope"));
        }
    }

    public class JsonTests
    {
        [Fact] public void Null_writes_null() { Assert.Equal("null", Json.Write(null)); }
        [Fact] public void Bools() { Assert.Equal("true", Json.Write(true)); Assert.Equal("false", Json.Write(false)); }
        [Fact] public void Integers() { Assert.Equal("-42", Json.Write(-42)); Assert.Equal("9876543210", Json.Write(9876543210L)); }
        [Fact] public void Double_round_trips() { Assert.Equal("0.1", Json.Write(0.1)); Assert.Equal("1.5", Json.Write(1.5)); }
        [Fact] public void Float_is_written() { Assert.Equal("2.5", Json.Write(2.5f)); }

        [Fact]
        public void Non_finite_doubles_write_null()
        {
            Assert.Equal("null", Json.Write(double.NaN));
            Assert.Equal("null", Json.Write(double.PositiveInfinity));
            Assert.Equal("null", Json.Write(double.NegativeInfinity));
        }

        [Fact]
        public void Null_nullable_double_writes_null()
        {
            double? v = null;
            Assert.Equal("null", Json.Write(v));
        }

        [Fact]
        public void Doubles_ignore_current_culture()
        {
            CultureInfo old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.Equal("1.5", Json.Write(1.5));
                Assert.Equal("-1234567", Json.Write(-1234567));
            }
            finally { CultureInfo.CurrentCulture = old; }
        }

        [Fact]
        public void String_is_quoted_and_escaped()
        {
            Assert.Equal("\"a\\\"b\\\\c\\nd\\re\\tf\"", Json.Write("a\"b\\c\nd\re\tf"));
        }

        [Fact]
        public void Control_chars_use_lowercase_u_escapes()
        {
            Assert.Equal("\"\\u0001\\u001f\"", Json.Write("\u0001\u001f"));
        }

        [Fact]
        public void Non_ascii_is_kept()
        {
            Assert.Equal("\"café — 日\"", Json.Write("café — 日"));
        }

        [Fact]
        public void Arrays_have_no_whitespace()
        {
            Assert.Equal("[1.5,2,null]", Json.Write(new[] { 1.5, 2, double.NaN }));
            Assert.Equal("[1,2,3]", Json.Write(new[] { 1, 2, 3 }));
            Assert.Equal("[]", Json.Write(new int[0]));
        }

        [Fact]
        public void Measurements_write_as_object_in_order()
        {
            var m = new Measurements();
            m.Set("z", 1); m.Set("a", "x"); m.Set("n", null);
            Assert.Equal("{\"z\":1,\"a\":\"x\",\"n\":null}", Json.Write(m));
        }

        [Fact]
        public void Empty_measurements_write_empty_object()
        {
            Assert.Equal("{}", Json.Write(new Measurements()));
        }

        [Fact]
        public void Nested_measurements_and_arrays()
        {
            var inner = new Measurements(); inner.Set("k", true);
            var m = new Measurements(); m.Set("in", inner); m.Set("arr", new[] { 1, 2 });
            Assert.Equal("{\"in\":{\"k\":true},\"arr\":[1,2]}", Json.Write(m));
        }

        [Fact]
        public void Dictionary_writes_as_object()
        {
            IDictionary<string, object> d = new Dictionary<string, object> { { "a", 1 }, { "b", "x" } };
            Assert.Equal("{\"a\":1,\"b\":\"x\"}", Json.Write(d));
        }

        [Fact]
        public void Iso_format()
        {
            Assert.Equal("2026-10-05T14:03:09.120Z", Json.Iso(new DateTime(2026, 10, 5, 14, 3, 9, 120, DateTimeKind.Utc)));
        }

        [Fact]
        public void Iso_ignores_current_culture()
        {
            CultureInfo old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.Equal("2026-01-02T03:04:05.006Z", Json.Iso(new DateTime(2026, 1, 2, 3, 4, 5, 6, DateTimeKind.Utc)));
            }
            finally { CultureInfo.CurrentCulture = old; }
        }
    }

    public class SelfTestReportTests
    {
        private static ScenarioResult R(string id, ScenarioStatus? st)
        {
            return new ScenarioResult { Id = id, Name = "n-" + id, Status = st, Reason = "why", DurationMs = 12.5 };
        }

        private static SelfTestReport Make()
        {
            var rep = new SelfTestReport
            {
                RunStartedUtc = new DateTime(2026, 10, 5, 14, 3, 9, 120, DateTimeKind.Utc),
                GameVersion = "1.0.0", ModVersion = "0.2", CityName = "Test City"
            };
            rep.Scenarios.Add(R("a", ScenarioStatus.Pass));
            rep.Scenarios.Add(R("b", ScenarioStatus.Fail));
            rep.Scenarios.Add(R("c", ScenarioStatus.Skip));
            rep.Scenarios.Add(R("d", ScenarioStatus.Error));
            rep.Scenarios.Add(R("e", null));
            rep.Scenarios[0].Measurements.Set("m", 1.5);
            return rep;
        }

        [Fact]
        public void Count_treats_null_status_as_error()
        {
            SelfTestReport rep = Make();
            Assert.Equal(1, rep.Count(ScenarioStatus.Pass));
            Assert.Equal(1, rep.Count(ScenarioStatus.Fail));
            Assert.Equal(1, rep.Count(ScenarioStatus.Skip));
            Assert.Equal(2, rep.Count(ScenarioStatus.Error));
        }

        [Fact]
        public void ToJson_top_level_keys_exact()
        {
            using (JsonDocument doc = JsonDocument.Parse(Make().ToJson()))
            {
                Assert.Equal(
                    new[] { "run_started_utc", "game_version", "mod_version", "city_name", "minecraft_peer", "aborted", "abort_reason", "scenarios", "summary" }.OrderBy(x => x).ToArray(),
                    doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(x => x).ToArray());
            }
        }

        [Fact]
        public void ToJson_header_values()
        {
            using (JsonDocument doc = JsonDocument.Parse(Make().ToJson()))
            {
                JsonElement root = doc.RootElement;
                Assert.Equal("2026-10-05T14:03:09.120Z", root.GetProperty("run_started_utc").GetString());
                Assert.Equal("1.0.0", root.GetProperty("game_version").GetString());
                Assert.Equal("0.2", root.GetProperty("mod_version").GetString());
                Assert.Equal("Test City", root.GetProperty("city_name").GetString());
                Assert.Equal(JsonValueKind.Null, root.GetProperty("minecraft_peer").ValueKind);
                Assert.Equal(JsonValueKind.False, root.GetProperty("aborted").ValueKind);
                Assert.Equal(JsonValueKind.Null, root.GetProperty("abort_reason").ValueKind);
            }
        }

        [Fact]
        public void ToJson_aborted_fields()
        {
            SelfTestReport rep = Make();
            rep.Aborted = true; rep.AbortReason = "city unloaded"; rep.MinecraftPeer = "mc 1.21";
            using (JsonDocument doc = JsonDocument.Parse(rep.ToJson()))
            {
                Assert.Equal(JsonValueKind.True, doc.RootElement.GetProperty("aborted").ValueKind);
                Assert.Equal("city unloaded", doc.RootElement.GetProperty("abort_reason").GetString());
                Assert.Equal("mc 1.21", doc.RootElement.GetProperty("minecraft_peer").GetString());
            }
        }

        [Fact]
        public void ToJson_scenario_shape_and_null_status_is_error()
        {
            using (JsonDocument doc = JsonDocument.Parse(Make().ToJson()))
            {
                JsonElement sc = doc.RootElement.GetProperty("scenarios");
                Assert.Equal(5, sc.GetArrayLength());
                JsonElement first = sc[0];
                Assert.Equal(
                    new[] { "id", "name", "status", "reason", "duration_ms", "measurements" }.OrderBy(x => x).ToArray(),
                    first.EnumerateObject().Select(p => p.Name).OrderBy(x => x).ToArray());
                Assert.Equal("a", first.GetProperty("id").GetString());
                Assert.Equal("n-a", first.GetProperty("name").GetString());
                Assert.Equal("pass", first.GetProperty("status").GetString());
                Assert.Equal("why", first.GetProperty("reason").GetString());
                Assert.Equal(12.5, first.GetProperty("duration_ms").GetDouble());
                Assert.Equal(1.5, first.GetProperty("measurements").GetProperty("m").GetDouble());
                Assert.Equal("fail", sc[1].GetProperty("status").GetString());
                Assert.Equal("skip", sc[2].GetProperty("status").GetString());
                Assert.Equal("error", sc[3].GetProperty("status").GetString());
                Assert.Equal("error", sc[4].GetProperty("status").GetString());
            }
        }

        [Fact]
        public void ToJson_summary_counts()
        {
            using (JsonDocument doc = JsonDocument.Parse(Make().ToJson()))
            {
                JsonElement s = doc.RootElement.GetProperty("summary");
                Assert.Equal(1, s.GetProperty("pass").GetInt32());
                Assert.Equal(1, s.GetProperty("fail").GetInt32());
                Assert.Equal(1, s.GetProperty("skip").GetInt32());
                Assert.Equal(2, s.GetProperty("error").GetInt32());
            }
        }

        [Fact]
        public void SummaryLine_plain()
        {
            Assert.Equal("Self-test: 1 pass, 1 fail, 1 skip, 2 error — report at /x/r.json", Make().SummaryLine("/x/r.json"));
        }

        [Fact]
        public void SummaryLine_aborted_inserts_reason_after_error_count()
        {
            SelfTestReport rep = Make();
            rep.Aborted = true; rep.AbortReason = "city unloaded";
            Assert.Equal("Self-test: 1 pass, 1 fail, 1 skip, 2 error (aborted: city unloaded) — report at /x/r.json", rep.SummaryLine("/x/r.json"));
        }
    }

    public class ScenarioRunnerTests
    {
        private static IEnumerator Steps(ScenarioResult r, int n, ScenarioStatus? verdictAtEnd, List<string> log = null)
        {
            for (int i = 0; i < n; i++)
            {
                if (log != null) log.Add("step" + i);
                yield return null;
            }
            if (verdictAtEnd == ScenarioStatus.Pass) r.Pass("ok");
        }

        private static IEnumerator Throws(ScenarioResult r)
        {
            yield return null;
            throw new InvalidOperationException("boom");
        }

        private static Scenario S(string id, Func<ScenarioResult, IEnumerator> body, double timeout = 100, Action cleanup = null)
        {
            return new Scenario { Id = id, Name = "N" + id, TimeoutSeconds = timeout, Body = body, Cleanup = cleanup };
        }

        [Fact]
        public void Empty_list_is_finished_immediately()
        {
            var run = new ScenarioRunner(new List<Scenario>(), (id, e) => { });
            Assert.True(run.Finished);
            Assert.Empty(run.Results);
            Assert.Null(run.Current);
        }

        [Fact]
        public void First_tick_starts_scenario_and_steps_once()
        {
            var log = new List<string>();
            var run = new ScenarioRunner(new List<Scenario> { S("a", r => Steps(r, 3, null, log)) }, (id, e) => { });
            Assert.False(run.Finished);
            Assert.Null(run.Current);
            run.Tick(10);
            Assert.Equal(new[] { "step0" }, log.ToArray());
            Assert.NotNull(run.Current);
            Assert.Equal("a", run.Current.Id);
            Assert.Equal("Na", run.Current.Name);
            run.Tick(10.1);
            Assert.Equal(new[] { "step0", "step1" }, log.ToArray());
        }

        [Fact]
        public void Body_is_called_on_first_tick_not_in_constructor()
        {
            int calls = 0;
            var run = new ScenarioRunner(new List<Scenario> { S("a", r => { calls++; return Steps(r, 1, null); }) }, (id, e) => { });
            Assert.Equal(0, calls);
            run.Tick(0);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void Scenario_ending_with_verdict_keeps_it_and_records_duration()
        {
            var run = new ScenarioRunner(new List<Scenario> { S("a", r => Steps(r, 1, ScenarioStatus.Pass)) }, (id, e) => { });
            run.Tick(10);      // step0
            Assert.False(run.Finished);
            run.Tick(12.5);    // MoveNext false, sets Pass
            Assert.True(run.Finished);
            Assert.Null(run.Current);
            ScenarioResult res = Assert.Single(run.Results);
            Assert.Equal(ScenarioStatus.Pass, res.Status);
            Assert.Equal("ok", res.Reason);
            Assert.Equal(2500, res.DurationMs, 6);
        }

        [Fact]
        public void Ending_without_verdict_is_error()
        {
            var run = new ScenarioRunner(new List<Scenario> { S("a", r => Steps(r, 0, null)) }, (id, e) => { });
            run.Tick(0);
            ScenarioResult res = Assert.Single(run.Results);
            Assert.Equal(ScenarioStatus.Error, res.Status);
            Assert.Equal("scenario ended without a verdict", res.Reason);
            Assert.True(run.Finished);
        }

        [Fact]
        public void Exception_in_movenext_is_error_and_reported()
        {
            string seenId = null; Exception seen = null;
            var run = new ScenarioRunner(new List<Scenario> { S("a", Throws) }, (id, e) => { seenId = id; seen = e; });
            run.Tick(0);
            Assert.Empty(run.Results);
            run.Tick(1);
            ScenarioResult res = Assert.Single(run.Results);
            Assert.Equal(ScenarioStatus.Error, res.Status);
            Assert.Equal("exception: boom", res.Reason);
            Assert.Equal("a", seenId);
            Assert.IsType<InvalidOperationException>(seen);
        }

        [Fact]
        public void Exception_in_body_is_error_and_reported()
        {
            int errors = 0;
            var run = new ScenarioRunner(new List<Scenario> { S("a", r => { throw new ArgumentException("bad"); }) }, (id, e) => errors++);
            run.Tick(0);
            ScenarioResult res = Assert.Single(run.Results);
            Assert.Equal(ScenarioStatus.Error, res.Status);
            Assert.Equal("exception: bad", res.Reason);
            Assert.Equal(1, errors);
        }

        [Fact]
        public void Timeout_fails_scenario_without_stepping_again()
        {
            var log = new List<string>();
            int cleanups = 0;
            var run = new ScenarioRunner(new List<Scenario> { S("a", r => Steps(r, 100, null, log), 5, () => cleanups++) }, (id, e) => { });
            run.Tick(0);
            run.Tick(5);       // exactly at the limit: not timed out
            Assert.Empty(run.Results);
            run.Tick(5.5);     // beyond
            ScenarioResult res = Assert.Single(run.Results);
            Assert.Equal(2, log.Count);
            Assert.Equal(ScenarioStatus.Fail, res.Status);
            Assert.Equal("timed out after 5 s", res.Reason);
            Assert.Equal(5500, res.DurationMs, 6);
            Assert.Equal(1, cleanups);
        }

        [Fact]
        public void Timeout_reason_uses_invariant_format()
        {
            CultureInfo old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var run = new ScenarioRunner(new List<Scenario> { S("a", r => Steps(r, 100, null), 2.5) }, (id, e) => { });
                run.Tick(0);
                run.Tick(3);
                Assert.Equal("timed out after 2.5 s", run.Results[0].Reason);
            }
            finally { CultureInfo.CurrentCulture = old; }
        }

        [Fact]
        public void Timeout_overrides_a_verdict_already_set()
        {
            var run = new ScenarioRunner(new List<Scenario> { S("a", r => { r.Pass("early"); return Steps(r, 100, null); }, 1) }, (id, e) => { });
            run.Tick(0);
            run.Tick(2);
            Assert.Equal(ScenarioStatus.Fail, run.Results[0].Status);
            Assert.Equal("timed out after 1 s", run.Results[0].Reason);
        }

        [Fact]
        public void Next_scenario_starts_on_the_next_tick()
        {
            var log = new List<string>();
            var run = new ScenarioRunner(new List<Scenario>
            {
                S("a", r => Steps(r, 0, ScenarioStatus.Pass)),
                S("b", r => { log.Add("b-start"); return Steps(r, 1, ScenarioStatus.Pass); })
            }, (id, e) => { });
            run.Tick(0);
            Assert.Single(run.Results);
            Assert.Empty(log);
            Assert.Null(run.Current);
            Assert.False(run.Finished);
            run.Tick(1);
            Assert.Equal(new[] { "b-start" }, log.ToArray());
            Assert.Equal("b", run.Current.Id);
        }

        [Fact]
        public void Results_follow_list_order_and_runner_finishes()
        {
            var run = new ScenarioRunner(new List<Scenario>
            {
                S("a", r => Steps(r, 0, ScenarioStatus.Pass)),
                S("b", r => Steps(r, 0, null))
            }, (id, e) => { });
            for (int t = 0; t < 4; t++) run.Tick(t);
            Assert.True(run.Finished);
            Assert.Equal(new[] { "a", "b" }, run.Results.Select(x => x.Id).ToArray());
        }

        [Fact]
        public void Cleanup_runs_exactly_once_per_scenario()
        {
            int cleanups = 0;
            var run = new ScenarioRunner(new List<Scenario> { S("a", r => Steps(r, 1, ScenarioStatus.Pass), 100, () => cleanups++) }, (id, e) => { });
            for (int t = 0; t < 6; t++) run.Tick(t);
            Assert.Equal(1, cleanups);
        }

        [Fact]
        public void Cleanup_exception_goes_to_onError_and_is_swallowed()
        {
            var errors = new List<string>();
            var run = new ScenarioRunner(new List<Scenario> { S("a", r => Steps(r, 0, ScenarioStatus.Pass), 100, () => { throw new Exception("cleanup"); }) },
                (id, e) => errors.Add(id + ":" + e.Message));
            run.Tick(0);
            Assert.Equal(new[] { "a:cleanup" }, errors.ToArray());
            Assert.Equal(ScenarioStatus.Pass, Assert.Single(run.Results).Status);
        }

        [Fact]
        public void Tick_after_finished_does_nothing()
        {
            int bodies = 0;
            var run = new ScenarioRunner(new List<Scenario> { S("a", r => { bodies++; return Steps(r, 0, ScenarioStatus.Pass); }) }, (id, e) => { });
            run.Tick(0);
            run.Tick(1);
            run.Tick(2);
            Assert.Equal(1, bodies);
            Assert.Single(run.Results);
        }

        [Fact]
        public void Abort_running_scenario_errors_and_rest_are_skipped()
        {
            int cleanups = 0;
            var run = new ScenarioRunner(new List<Scenario>
            {
                S("a", r => Steps(r, 0, ScenarioStatus.Pass)),
                S("b", r => Steps(r, 100, null), 100, () => cleanups++),
                S("c", r => Steps(r, 0, null)),
                S("d", r => Steps(r, 0, null))
            }, (id, e) => { });
            run.Tick(0);   // a ends
            run.Tick(1);   // b starts
            run.Abort("city unloaded", 4);
            Assert.True(run.Finished);
            Assert.True(run.Aborted);
            Assert.Equal("city unloaded", run.AbortReason);
            Assert.Null(run.Current);
            Assert.Equal(1, cleanups);
            Assert.Equal(new[] { "a", "b", "c", "d" }, run.Results.Select(x => x.Id).ToArray());
            Assert.Equal(ScenarioStatus.Error, run.Results[1].Status);
            Assert.Equal("aborted: city unloaded", run.Results[1].Reason);
            Assert.Equal(3000, run.Results[1].DurationMs, 6);
            foreach (int i in new[] { 2, 3 })
            {
                Assert.Equal(ScenarioStatus.Skip, run.Results[i].Status);
                Assert.Equal("aborted", run.Results[i].Reason);
                Assert.Equal(0, run.Results[i].DurationMs);
                Assert.Equal("N" + run.Results[i].Id, run.Results[i].Name);
            }
        }

        [Fact]
        public void Abort_before_any_start_skips_everything()
        {
            var run = new ScenarioRunner(new List<Scenario> { S("a", r => Steps(r, 0, null)), S("b", r => Steps(r, 0, null)) }, (id, e) => { });
            run.Abort("x", 0);
            Assert.True(run.Finished);
            Assert.All(run.Results, r => Assert.Equal(ScenarioStatus.Skip, r.Status));
            Assert.Equal(2, run.Results.Count);
        }

        [Fact]
        public void Abort_when_finished_does_nothing()
        {
            var run = new ScenarioRunner(new List<Scenario> { S("a", r => Steps(r, 0, ScenarioStatus.Pass)) }, (id, e) => { });
            run.Tick(0);
            run.Abort("late", 1);
            Assert.False(run.Aborted);
            Assert.Null(run.AbortReason);
            Assert.Equal(ScenarioStatus.Pass, Assert.Single(run.Results).Status);
        }

        [Fact]
        public void Not_aborted_by_default()
        {
            var run = new ScenarioRunner(new List<Scenario>(), (id, e) => { });
            Assert.False(run.Aborted);
        }
    }

    public class SelfTestMathTests
    {
        [Fact]
        public void PerSecondDividesCountBySeconds()
        {
            Assert.Equal(50.0, SelfTestMath.PerSecond(100, 2.0));
            Assert.Equal(0.5, SelfTestMath.PerSecond(1, 2.0));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-3.0)]
        public void PerSecondIsZeroForNonPositiveSeconds(double seconds)
        {
            Assert.Equal(0.0, SelfTestMath.PerSecond(100, seconds));
        }

        private const double Tol = 1e-9;

        // up-facing triangle: normal y = +1 (see contract)
        private static float[] Up(float y) { return new float[] { 0, y, 0, 0, y, 10, 10, y, 0 }; }
        private static float[] Down(float y) { return new float[] { 0, y, 0, 10, y, 0, 0, y, 10 }; }
        private static float[] Cat(params float[][] tris) { return tris.SelectMany(t => t).ToArray(); }

        [Fact]
        public void Summarize_empty_is_all_null()
        {
            Summary s = SelfTestMath.Summarize(new double[0]);
            Assert.Equal(0, s.Count);
            Assert.Null(s.Min); Assert.Null(s.Max); Assert.Null(s.Mean); Assert.Null(s.MaxAbs);
        }

        [Fact]
        public void Summarize_values()
        {
            Summary s = SelfTestMath.Summarize(new[] { -5.0, 1.0, 2.0, 4.0 });
            Assert.Equal(4, s.Count);
            Assert.Equal(-5.0, s.Min.Value, Tol);
            Assert.Equal(4.0, s.Max.Value, Tol);
            Assert.Equal(0.5, s.Mean.Value, Tol);
            Assert.Equal(5.0, s.MaxAbs.Value, Tol);
        }

        [Fact]
        public void Histogram_buckets()
        {
            int[] h = SelfTestMath.Histogram(new[] { -1.0, 0.0, 0.5, 1.0, 1.9, 2.0, 7.0 }, new[] { 0.0, 1.0, 2.0 });
            Assert.Equal(new[] { 1, 2, 2, 2 }, h);
        }

        [Fact]
        public void Histogram_empty_values_gives_zeros_of_k_plus_2()
        {
            Assert.Equal(new[] { 0, 0, 0 }, SelfTestMath.Histogram(new double[0], new[] { 0.0, 1.0 }));
        }

        [Fact]
        public void TopNear_up_facing_hit()
        {
            double? y = SelfTestMath.TopNear(Up(5), new ushort[] { 1 }, 1, 2, 2, 1, 5, 1);
            Assert.NotNull(y);
            Assert.Equal(5.0, y.Value, 5);
        }

        [Fact]
        public void TopNear_down_facing_ignored()
        {
            Assert.Null(SelfTestMath.TopNear(Down(5), new ushort[] { 1 }, 1, 2, 2, 1, 5, 1));
        }

        [Fact]
        public void TopNear_vertical_triangle_ignored()
        {
            var vertical = new float[] { 0, 0, 0, 0, 5, 0, 10, 0, 0 };
            Assert.Null(SelfTestMath.TopNear(vertical, new ushort[] { 1 }, 1, 2, 0, 1, 2, 10));
        }

        [Fact]
        public void TopNear_miss_returns_null()
        {
            Assert.Null(SelfTestMath.TopNear(Up(5), new ushort[] { 1 }, 1, 9, 9, 1, 5, 100));
            Assert.Null(SelfTestMath.TopNear(Up(5), new ushort[] { 1 }, 1, -3, 2, 1, 5, 100));
        }

        [Fact]
        public void TopNear_on_edge_counts_as_hit()
        {
            double? y = SelfTestMath.TopNear(Up(5), new ushort[] { 1 }, 1, 0, 3, 1, 5, 1);
            Assert.NotNull(y);
        }

        [Fact]
        public void TopNear_mask_filters()
        {
            Assert.Null(SelfTestMath.TopNear(Up(5), new ushort[] { 2 }, 1, 2, 2, 1, 5, 1));
            Assert.NotNull(SelfTestMath.TopNear(Up(5), new ushort[] { 3 }, 1, 2, 2, 1, 5, 1));
        }

        [Fact]
        public void TopNear_window_filters()
        {
            Assert.Null(SelfTestMath.TopNear(Up(5), new ushort[] { 1 }, 1, 2, 2, 1, 8, 2.5));
            Assert.NotNull(SelfTestMath.TopNear(Up(5), new ushort[] { 1 }, 1, 2, 2, 1, 7, 2.5));
        }

        [Fact]
        public void TopNear_picks_surface_nearest_to_ref()
        {
            float[] pos = Cat(Up(2), Up(6));
            ushort[] flags = { 1, 1 };
            Assert.Equal(6.0, SelfTestMath.TopNear(pos, flags, 2, 2, 2, 1, 5, 10).Value, 5);
            Assert.Equal(2.0, SelfTestMath.TopNear(pos, flags, 2, 2, 2, 1, 3, 10).Value, 5);
        }

        [Fact]
        public void TopNear_respects_count()
        {
            float[] pos = Cat(Up(2), Up(6));
            ushort[] flags = { 1, 1 };
            Assert.Equal(2.0, SelfTestMath.TopNear(pos, flags, 1, 2, 2, 1, 6, 10).Value, 5);
            Assert.Null(SelfTestMath.TopNear(pos, flags, 0, 2, 2, 1, 6, 10));
        }

        [Fact]
        public void TopNear_interpolates_on_slope()
        {
            var sloped = new float[] { 0, 0, 0, 0, 0, 10, 10, 10, 0 };   // y = x
            double? y = SelfTestMath.TopNear(sloped, new ushort[] { 1 }, 1, 4, 2, 1, 4, 1);
            Assert.NotNull(y);
            Assert.Equal(4.0, y.Value, 5);
        }

        [Fact]
        public void LateralDistance_to_segment_interior_and_ends()
        {
            double[] line = { 0, 0, 10, 0 };
            Assert.Equal(3.0, SelfTestMath.LateralDistance(line, 5, 3), Tol);
            Assert.Equal(5.0, SelfTestMath.LateralDistance(line, -3, 4), Tol);
            Assert.Equal(5.0, SelfTestMath.LateralDistance(line, 13, -4), Tol);
        }

        [Fact]
        public void LateralDistance_takes_min_over_segments()
        {
            double[] line = { 0, 0, 10, 0, 10, 10 };
            Assert.Equal(2.0, SelfTestMath.LateralDistance(line, 8, 5), Tol);
        }

        [Fact]
        public void LateralDistance_needs_two_points()
        {
            Assert.True(double.IsPositiveInfinity(SelfTestMath.LateralDistance(new double[0], 1, 1)));
            Assert.True(double.IsPositiveInfinity(SelfTestMath.LateralDistance(new double[] { 1, 1 }, 1, 1)));
        }

        [Theory]
        [InlineData(0, 1, 0)]
        [InlineData(1, 0, 90)]
        [InlineData(0, -1, 180)]
        [InlineData(-1, 0, 270)]
        [InlineData(1, 1, 45)]
        [InlineData(-1, 1, 315)]
        [InlineData(0, 0, 0)]
        public void YawTowards_is_unity_heading(double dx, double dz, double expected)
        {
            Assert.Equal(expected, SelfTestMath.YawTowards(dx, dz), 9);
        }

        [Fact]
        public void SlopeDegrees_flat_is_zero()
        {
            Assert.Equal(0.0, SelfTestMath.SlopeDegrees(3, 3, 3, 3, 1), Tol);
        }

        [Fact]
        public void SlopeDegrees_45_degree_plane()
        {
            Assert.Equal(45.0, SelfTestMath.SlopeDegrees(-1, 1, 0, 0, 1), 9);
            Assert.Equal(45.0, SelfTestMath.SlopeDegrees(0, 0, 4, 6, 1), 9);
        }

        [Fact]
        public void SlopeDegrees_combines_both_axes_and_scales_by_step()
        {
            // gx = gz = 1 per 2*step=4 m -> 0.5 ... (2 - -2)/(2*4)=0.5 each -> sqrt(0.5)
            double expected = Math.Atan(Math.Sqrt(0.5)) * 180.0 / Math.PI;
            Assert.Equal(expected, SelfTestMath.SlopeDegrees(-2, 2, -2, 2, 4), 9);
        }

        [Fact]
        public void Within_semantics()
        {
            Assert.False(SelfTestMath.Within(null, 1, 5));
            Assert.False(SelfTestMath.Within(1, null, 5));
            Assert.True(SelfTestMath.Within(1.0, 1.5, 0.5));
            Assert.True(SelfTestMath.Within(1.5, 1.0, 0.5));
            Assert.False(SelfTestMath.Within(1.0, 1.6, 0.5));
        }
    }

    public class AirborneTrackerTests
    {
        [Fact]
        public void Zero_before_any_airborne_sample()
        {
            var t = new AirborneTracker();
            Assert.Equal(0, t.LongestSeconds);
            t.Sample(0, true);
            t.Sample(1, true);
            Assert.Equal(0, t.LongestSeconds);
        }

        [Fact]
        public void Interval_ends_at_next_grounded_sample()
        {
            var t = new AirborneTracker();
            t.Sample(0, true);
            t.Sample(1, false);
            t.Sample(2, false);
            t.Sample(4, true);
            Assert.Equal(3.0, t.LongestSeconds, 9);
        }

        [Fact]
        public void Longest_of_several_intervals()
        {
            var t = new AirborneTracker();
            t.Sample(0, false); t.Sample(1, true);      // 1
            t.Sample(2, false); t.Sample(5, true);      // 3
            t.Sample(6, false); t.Sample(7, true);      // 1
            Assert.Equal(3.0, t.LongestSeconds, 9);
        }

        [Fact]
        public void Open_interval_measured_to_latest_sample()
        {
            var t = new AirborneTracker();
            t.Sample(0, true);
            t.Sample(1, false);
            Assert.Equal(0.0, t.LongestSeconds, 9);
            t.Sample(3.5, false);
            Assert.Equal(2.5, t.LongestSeconds, 9);
        }

        [Fact]
        public void Initial_airborne_state_starts_an_interval()
        {
            var t = new AirborneTracker();
            t.Sample(10, false);
            t.Sample(12, true);
            Assert.Equal(2.0, t.LongestSeconds, 9);
        }
    }
}
