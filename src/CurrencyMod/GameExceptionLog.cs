using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;

namespace CurrencyMod
{
    /// <summary>
    /// The game catches exceptions in its UI loop (UIUpdatingManager.Render and others) and sends them to its own
    /// Diagnostics, which writes nowhere we can read. A UI that stops redrawing (melhorias-jogando item 28) leaves no
    /// trace. This copies each distinct exception to the BepInEx log once and counts the repeats; `exceptions` in the
    /// dev channel lists them.
    /// </summary>
    [HarmonyPatch(typeof(Amplitude.Diagnostics), nameof(Amplitude.Diagnostics.LogException), new[] { typeof(Exception), typeof(int) })]
    internal static class GameExceptionLog
    {
        private const int MaxDistinct = 50;

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, (int Count, DateTime First, DateTime Last, string Text)> seen =
            new Dictionary<string, (int, DateTime, DateTime, string)>();

        private static void Prefix(Exception exception, int innerExceptionLevel)
        {
            if (exception == null || innerExceptionLevel > 0)
            {
                return;
            }
            try
            {
                string stack = exception.StackTrace ?? string.Empty;
                string firstFrame = stack.Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
                string key = exception.GetType().Name + "|" + exception.Message + "|" + firstFrame;
                lock (Gate)
                {
                    DateTime now = DateTime.Now;
                    if (seen.TryGetValue(key, out var entry))
                    {
                        seen[key] = (entry.Count + 1, entry.First, now, entry.Text);
                        return;
                    }
                    if (seen.Count >= MaxDistinct)
                    {
                        return;
                    }
                    string text = exception.ToString();
                    if (text.Length > 4000)
                    {
                        text = text.Substring(0, 4000);
                    }
                    seen[key] = (1, now, now, text);
                    Plugin.Log.LogWarning($"Game exception (caught by the game): {text}");
                }
            }
            catch
            {
                // Never break the game's own error path.
            }
        }

        /// <summary>Distinct exceptions seen since the game started, most frequent first.</summary>
        internal static string Report()
        {
            lock (Gate)
            {
                if (seen.Count == 0)
                {
                    return "ok: no game exceptions caught since start.";
                }
                var text = new StringBuilder($"ok: {seen.Count} distinct game exception(s)\n");
                foreach (var pair in seen.OrderByDescending(p => p.Value.Count))
                {
                    text.AppendLine($"--- x{pair.Value.Count}  first {pair.Value.First:HH:mm:ss}  last {pair.Value.Last:HH:mm:ss}");
                    text.AppendLine(pair.Value.Text);
                }
                return text.ToString();
            }
        }
    }
}
