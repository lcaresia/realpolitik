using System;
using System.IO;

// O mínimo do BepInEx e do Plugin para compilar Credentials.cs e ApiKey.cs do mod fora do jogo.
namespace BepInEx
{
    internal static class Paths
    {
        internal static string ConfigPath => Path.Combine(IaBench.Bench.GameDir, "BepInEx", "config");
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
        public void LogWarning(object message) => Console.Error.WriteLine("[mod aviso] " + message);
    }
}
