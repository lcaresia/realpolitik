using System;
using System.IO;

// The bare minimum of BepInEx and Plugin needed to compile the mod's Credentials.cs and ApiKey.cs outside the game.
namespace BepInEx
{
    internal static class Paths
    {
        internal static string ConfigPath => Path.Combine(AiBench.Bench.GameDir, "BepInEx", "config");
    }
}

namespace CurrencyMod
{
    internal static class Plugin
    {
        internal static readonly ShimLog Log = new ShimLog();
    }

    internal sealed class ShimLog
    {
        public void LogInfo(object message) => Console.Error.WriteLine("[mod] " + message);
        public void LogWarning(object message) => Console.Error.WriteLine("[mod warning] " + message);
    }
}
