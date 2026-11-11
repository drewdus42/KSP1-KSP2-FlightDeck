using System;
using System.IO;

namespace FlightDeck.Ksp2
{
    internal static class Ksp2Log
    {
        private static readonly object Sync = new object();
        private static string _path;
        private const long MaxLogBytes = 2 * 1024 * 1024;

        public static void Initialize(string path)
        {
            _path = path;
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Logging problems must never stop the mod from loading.
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        public static void Error(string message, Exception exception)
        {
            Write("ERROR", exception == null ? message : message + Environment.NewLine + exception);
        }

        private static void Write(string level, string message)
        {
            try
            {
                UnityEngine.Debug.Log("[KSPFlightDeck2] " + message);
            }
            catch (Exception)
            {
                // Unity logging is unavailable during very early load.
            }

            if (string.IsNullOrEmpty(_path))
            {
                return;
            }

            lock (Sync)
            {
                try
                {
                    File.AppendAllText(_path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message + Environment.NewLine);
                }
                catch (Exception)
                {
                    // Ignored on purpose.
                }
            }
        }
    }
}
