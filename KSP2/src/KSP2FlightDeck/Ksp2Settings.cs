using System;
using System.Globalization;
using System.IO;

namespace FlightDeck.Ksp2
{
    /// <summary>
    /// Plain key=value settings file. KSP 2 has no ConfigNode, and keeping this dumb makes the
    /// options easy to edit and impossible to break.
    /// </summary>
    internal sealed class Ksp2Settings
    {
        public bool Enabled = true;
        public int Port = 8081;
        public bool ShowStartupMessage = true;
        public bool ShowPanel = true;

        /// <summary>Folder the plugin dll lives in (…\Kerbal Space Program 2\mods\KSPFlightDeck2).</summary>
        public static string ModDirectory
        {
            get
            {
                try
                {
                    string location = typeof(Ksp2Settings).Assembly.Location;
                    if (!string.IsNullOrEmpty(location))
                    {
                        return Path.GetDirectoryName(location);
                    }
                }
                catch (Exception)
                {
                    // Fall through to the working directory.
                }

                return Directory.GetCurrentDirectory();
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(ModDirectory, "settings.cfg"); }
        }

        public static Ksp2Settings Load()
        {
            var settings = new Ksp2Settings();
            try
            {
                if (!File.Exists(FilePath))
                {
                    settings.Save();
                    return settings;
                }

                foreach (string rawLine in File.ReadAllLines(FilePath))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim().ToLowerInvariant();
                    string value = line.Substring(separator + 1).Trim();

                    switch (key)
                    {
                        case "enabled":
                            settings.Enabled = ParseBool(value, settings.Enabled);
                            break;
                        case "port":
                            int port;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
                            {
                                settings.Port = port;
                            }

                            break;
                        case "showstartupmessage":
                            settings.ShowStartupMessage = ParseBool(value, settings.ShowStartupMessage);
                            break;
                        case "showpanel":
                            settings.ShowPanel = ParseBool(value, settings.ShowPanel);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Ksp2Log.Error("Could not read " + FilePath + "; using defaults.", ex);
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllLines(FilePath, new[]
                {
                    "# KSP Flight Deck 2 settings",
                    "enabled = " + (Enabled ? "true" : "false"),
                    "port = " + Port.ToString(CultureInfo.InvariantCulture),
                    "showStartupMessage = " + (ShowStartupMessage ? "true" : "false"),
                    "showPanel = " + (ShowPanel ? "true" : "false")
                });
            }
            catch (Exception ex)
            {
                Ksp2Log.Error("Could not write " + FilePath, ex);
            }
        }

        private static bool ParseBool(string value, bool fallback)
        {
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }
    }
}
