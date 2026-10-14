using System;
using System.Diagnostics;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace CurrencyMod.Support
{
    /// <summary>
    /// A copy of the BepInEx log that survives the next launch. BepInEx truncates LogOutput.log as soon as the game
    /// starts, so after a freeze or a crash the session that went wrong is gone by the time the player can send a report.
    /// Here every log line also goes to Realpolitik\session.log; on the first start of a new game process the previous
    /// file becomes session-prev.log (hot reloads of the core keep appending to the same file).
    /// </summary>
    internal sealed class SessionLog : ILogListener
    {
        private const long MaxBytes = 16L * 1024 * 1024;
        private const string HeaderPrefix = "# Realpolitik session, process ";

        private static SessionLog current;

        private readonly object gate = new object();
        private StreamWriter writer;
        private long written;
        private bool capped;

        internal static string Folder => Path.Combine(Paths.BepInExRootPath, "Realpolitik");
        internal static string CurrentPath => Path.Combine(Folder, "session.log");
        internal static string PreviousPath => Path.Combine(Folder, "session-prev.log");

        internal static void Start()
        {
            if (current != null)
            {
                return;
            }
            try
            {
                Directory.CreateDirectory(Folder);
                int pid = Process.GetCurrentProcess().Id;
                string header = HeaderPrefix + pid;
                bool sameProcess = File.Exists(CurrentPath) && FirstLine(CurrentPath) == header;
                if (!sameProcess && File.Exists(CurrentPath))
                {
                    File.Copy(CurrentPath, PreviousPath, overwrite: true);
                    File.Delete(CurrentPath);
                }
                var log = new SessionLog();
                var stream = new FileStream(CurrentPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                log.written = stream.Length;
                log.writer = new StreamWriter(stream) { AutoFlush = true };
                if (!sameProcess)
                {
                    log.writer.WriteLine(header);
                    log.writer.WriteLine($"# Started {DateTime.Now:yyyy-MM-dd HH:mm:ss}, mod {Plugin.ProductVersion}, game {Application.version}");
                }
                BepInEx.Logging.Logger.Listeners.Add(log);
                Application.quitting += log.OnQuitting;
                current = log;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"Session log not started: {ex.Message}");
            }
        }

        internal static void Stop()
        {
            SessionLog log = current;
            current = null;
            if (log == null)
            {
                return;
            }
            Application.quitting -= log.OnQuitting;
            BepInEx.Logging.Logger.Listeners.Remove(log);
            log.Dispose();
        }

        /// <summary>Writes a marked line (quit steps, report events) even when nothing else logs.</summary>
        internal static void Mark(string text) => current?.Write($"[{DateTime.Now:HH:mm:ss.fff}] # {text}");

        private void OnQuitting() => Write($"[{DateTime.Now:HH:mm:ss.fff}] # Quit requested (Application.quitting); memory {GC.GetTotalMemory(false) / (1024 * 1024)} MB managed");

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            Write($"[{DateTime.Now:HH:mm:ss.fff}] [{eventArgs.Level,-7}:{eventArgs.Source.SourceName}] {eventArgs.Data}");
        }

        private void Write(string line)
        {
            lock (gate)
            {
                if (writer == null || capped)
                {
                    return;
                }
                try
                {
                    if (written > MaxBytes)
                    {
                        capped = true;
                        writer.WriteLine("# Size cap reached; the rest of this session is only in LogOutput.log.");
                        return;
                    }
                    writer.WriteLine(line);
                    written += line.Length + 2;
                }
                catch (Exception)
                {
                    capped = true;
                }
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                writer?.Dispose();
                writer = null;
            }
        }

        private static string FirstLine(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadLine();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
