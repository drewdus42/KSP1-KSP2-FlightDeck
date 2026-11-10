using System;
using System.Globalization;
using System.IO;

namespace FlightDeck
{
    internal sealed class ModSettings
    {
        public bool Enabled = true;
        public int Port = 8080;
        public bool ShowStartupMessage = true;

        public static string SettingsDirectory
        {
            get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPFlightDeck"); }
        }

        public static string FilePath
        {
            get { return Path.Combine(SettingsDirectory, "settings.cfg"); }
        }

        public static ModSettings Load()
        {
            var settings = new ModSettings();
            string path = FilePath;

            try
            {
                if (!File.Exists(path))
                {
                    settings.Save();
                    return settings;
                }

                ConfigNode node = ConfigNode.Load(path);
                if (node == null)
                {
                    return settings;
                }

                settings.Enabled = ParseBool(node.GetValue("enabled"), true);
                settings.Port = ParseInt(node.GetValue("port"), 8080);
                settings.ShowStartupMessage = ParseBool(node.GetValue("showStartupMessage"), true);
            }
            catch (Exception ex)
            {
                PluginLog.Error("Could not read " + path + "; using defaults.", ex);
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsDirectory))
                {
                    Directory.CreateDirectory(SettingsDirectory);
                }

                var node = new ConfigNode("KSPFlightDeck");
                node.AddValue("enabled", Enabled ? "true" : "false");
                node.AddValue("port", Port.ToString(CultureInfo.InvariantCulture));
                node.AddValue("showStartupMessage", ShowStartupMessage ? "true" : "false");
                node.Save(FilePath);
            }
            catch (Exception ex)
            {
                PluginLog.Error("Could not write " + FilePath, ex);
            }
        }

        private static bool ParseBool(string value, bool fallback)
        {
            if (string.IsNullOrEmpty(value))
            {
                return fallback;
            }

            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            if (!string.IsNullOrEmpty(value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            return fallback;
        }
    }
}
