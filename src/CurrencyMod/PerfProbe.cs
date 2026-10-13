using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace CurrencyMod
{
    /// <summary>
    /// Dev profiler ("perf &lt;seconds&gt;"): for a few seconds, times every method the mod patches (inclusive of the
    /// native body) and the Update/LateUpdate/OnGUI of the mod's components, plus the frame times. Built to find what
    /// makes a screen stutter (melhorias item 42). Everything is unpatched when the run ends.
    /// </summary>
    internal static class PerfProbe
    {
        private const string ProbeId = "lucas.humankind.currency.perf";

        private sealed class Stat
        {
            public long Ticks;
            public long MaxTicks;
            public int Calls;
        }

        private static readonly Dictionary<MethodBase, Stat> stats = new Dictionary<MethodBase, Stat>();
        private static readonly List<float> frames = new List<float>();
        private static Harmony probe;
        internal static bool Running => probe != null;

        /// <param name="bare">Only the frame times, without timing methods (patching ~150 methods takes seconds).</param>
        /// <param name="scroll">A UIScrollView to scroll through its rows during the run (simulates the player scrolling).</param>
        internal static IEnumerator Run(string coreHarmonyId, float seconds, bool bare, Amplitude.UI.Interactables.UIScrollView scroll, Action<string> done)
        {
            if (probe != null)
            {
                done("error: a perf run is already going");
                yield break;
            }
            stats.Clear();
            frames.Clear();
            probe = new Harmony(ProbeId);
            int patched = 0, failed = 0;
            HarmonyMethod prefix = new HarmonyMethod(typeof(PerfProbe), nameof(Prefix));
            HarmonyMethod postfix = new HarmonyMethod(typeof(PerfProbe), nameof(Postfix));
            foreach (MethodBase method in bare ? Enumerable.Empty<MethodBase>() : Targets(coreHarmonyId))
            {
                try
                {
                    probe.Patch(method, prefix: prefix, postfix: postfix);
                    patched++;
                }
                catch
                {
                    failed++;
                }
            }
            // Warm-up: patching and the first (JIT) calls of the patched methods would dominate the numbers.
            float warm = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < warm)
            {
                yield return null;
            }
            stats.Clear();
            yield return null;
            List<Amplitude.UI.UITransform> rows = scroll != null ? Rows(scroll) : null;
            int step = 0;
            Plugin.Log.LogInfo($"Perf: measuring for {seconds:0.#}s.");
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                if (rows != null && rows.Count > 0)
                {
                    // Down the list a row per frame, then back to the top.
                    scroll.ScrollTo(rows[step++ % rows.Count]);
                }
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
            }
            probe.UnpatchSelf();
            probe = null;
            done(Report(seconds, patched, failed));
        }

        private static List<Amplitude.UI.UITransform> Rows(Amplitude.UI.Interactables.UIScrollView scroll)
        {
            var rows = new List<Amplitude.UI.UITransform>();
            foreach (Amplitude.UI.UITransform ui in scroll.GetComponentsInChildren<Amplitude.UI.UITransform>())
            {
                if (ui.Height > 20f && ui.Height < 60f && ui.VisibleGlobally)
                {
                    rows.Add(ui);
                }
            }
            return rows.OrderByDescending(r => r.GlobalRect.y).ToList();
        }

        /// <summary>Methods patched by the mod's core plus the per-frame callbacks of its components.</summary>
        private static IEnumerable<MethodBase> Targets(string coreHarmonyId)
        {
            var seen = new HashSet<MethodBase>();
            foreach (MethodBase method in Harmony.GetAllPatchedMethods().ToList())
            {
                Patches info = Harmony.GetPatchInfo(method);
                bool ours = info != null && info.Owners.Contains(coreHarmonyId);
                if (ours && !(method is ConstructorInfo) && seen.Add(method))
                {
                    yield return method;
                }
            }
            string[] callbacks = { "Update", "LateUpdate", "OnGUI", "FixedUpdate" };
            foreach (Type type in typeof(PerfProbe).Assembly.GetTypes().Where(t => typeof(MonoBehaviour).IsAssignableFrom(t) && !t.IsAbstract && !t.ContainsGenericParameters))
            {
                foreach (string name in callbacks)
                {
                    MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                    if (method != null && seen.Add(method))
                    {
                        yield return method;
                    }
                }
            }
        }

        private static void Prefix(out long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        private static void Postfix(long __state, MethodBase __originalMethod)
        {
            long elapsed = Stopwatch.GetTimestamp() - __state;
            if (!stats.TryGetValue(__originalMethod, out Stat stat))
            {
                stat = new Stat();
                stats[__originalMethod] = stat;
            }
            stat.Ticks += elapsed;
            stat.Calls++;
            if (elapsed > stat.MaxTicks)
            {
                stat.MaxTicks = elapsed;
            }
        }

        private static string Report(float seconds, int patched, int failed)
        {
            double ms = 1000.0 / Stopwatch.Frequency;
            int count = Math.Max(1, frames.Count);
            var sorted = frames.OrderBy(f => f).ToList();
            float p95 = sorted.Count > 0 ? sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.95))] : 0f;
            var text = new StringBuilder();
            text.AppendLine($"perf {seconds:0.#}s · {patched} methods timed ({failed} not patchable) · focused {Application.isFocused} · runInBackground {Application.runInBackground} · vSync {QualitySettings.vSyncCount} · targetFps {Application.targetFrameRate}");
            text.AppendLine($"frames {frames.Count} · avg {(frames.Count > 0 ? frames.Average() : 0):0.0} ms · p95 {p95:0.0} ms · max {(frames.Count > 0 ? frames.Max() : 0):0.0} ms · over 50 ms: {frames.Count(f => f > 50f)}");
            text.AppendLine("inclusive time (nested calls count in both), top 30 by total:");
            text.AppendLine("total ms | ms/frame | calls | calls/frame | avg ms | max ms | method");
            foreach (var pair in stats.OrderByDescending(p => p.Value.Ticks).Take(30))
            {
                Stat s = pair.Value;
                text.AppendLine($"{s.Ticks * ms,8:0.0} | {s.Ticks * ms / count,7:0.00} | {s.Calls,6} | {s.Calls / (double)count,7:0.0} | {s.Ticks * ms / Math.Max(1, s.Calls),6:0.000} | {s.MaxTicks * ms,6:0.0} | {pair.Key.DeclaringType?.Name}.{pair.Key.Name}");
            }
            return text.ToString();
        }
    }
}
