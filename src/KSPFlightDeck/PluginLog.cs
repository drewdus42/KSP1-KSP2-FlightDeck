using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FlightDeck
{
    internal static class PluginLog
    {
        private static readonly object Sync = new object();
        private static string _path;
        private const long MaxLogBytes = 2 * 1024 * 1024;

        public static void Initialize(string path)
        {
            _path = path;
            try
            {
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    if (info.Length > MaxLogBytes)
                    {
                        File.Delete(path);
                    }
                }
            }
            catch (Exception)
            {
                // A log file we cannot rotate must never stop the mod from loading.
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public static void Error(string message, Exception exception = null)
        {
            Write("ERROR", message, exception);
        }

        private static void Write(string level, string message, Exception exception)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message;
            if (exception != null)
            {
                line += Environment.NewLine + exception;
            }

            try
            {
                if (level == "ERROR")
                {
                    Debug.LogWarning("[KSPFlightDeck] " + message);
                }
                else
                {
                    Debug.Log("[KSPFlightDeck] " + message);
                }
            }
            catch (Exception)
            {
                // UnityEngine may not be available during early load.
            }

            if (string.IsNullOrEmpty(_path))
            {
                return;
            }

            lock (Sync)
            {
                try
                {
                    File.AppendAllText(_path, line + Environment.NewLine);
                }
                catch (Exception)
                {
                    // Ignore logging failures.
                }
            }
        }
    }
}
