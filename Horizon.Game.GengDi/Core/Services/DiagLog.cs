using System;
using System.IO;
using System.Threading;

namespace Horizon.Game.GengDi.Core.Services
{
    public static class DiagLog
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HundunWorld", "diag_merchant.log");

        private static readonly object _lock = new();

        public static void Log(string message)
        {
            try
            {
                var line = $"{DateTime.Now:HH:mm:ss.fff} [T{Thread.CurrentThread.ManagedThreadId}] {message}{Environment.NewLine}";
                lock (_lock)
                {
                    var dir = Path.GetDirectoryName(LogPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    File.AppendAllText(LogPath, line);
                }
            }
            catch { }
        }
    }
}
