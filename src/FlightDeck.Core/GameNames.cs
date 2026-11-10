using System;
using System.Collections.Generic;

namespace FlightDeck.Core
{
    /// <summary>
    /// Canonical action group names. The first nine are native KSP action groups; the rest are
    /// virtual groups the mod emulates by walking part modules (KSP 1 has no built-in group
    /// for panels, antennas or science).
    /// </summary>
    public static class ActionGroupNames
    {
        public const string Abort = "Abort";
        public const string Brakes = "Brakes";
        public const string Gear = "Gear";
        public const string Lights = "Lights";
        public const string Rcs = "RCS";
        public const string Sas = "SAS";
        public const string SolarPanels = "SolarPanels";
        public const string RadiatorPanels = "RadiatorPanels";
        public const string Antennas = "Antennas";
        public const string Science = "Science";

        public static readonly string[] CustomGroups =
        {
            "Custom01", "Custom02", "Custom03", "Custom04", "Custom05",
            "Custom06", "Custom07", "Custom08", "Custom09", "Custom10"
        };

        public static readonly string[] All = BuildAll();

        private static readonly Dictionary<string, string> Lookup = BuildLookup();

        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            string value;
            return Lookup.TryGetValue(name.Trim(), out value) ? value : null;
        }

        public static bool IsNativeKspGroup(string canonicalName)
        {
            if (string.IsNullOrEmpty(canonicalName))
            {
                return false;
            }

            switch (canonicalName)
            {
                case Abort:
                case Brakes:
                case Gear:
                case Lights:
                case Rcs:
                case Sas:
                    return true;
                default:
                    return canonicalName.StartsWith("Custom", StringComparison.Ordinal);
            }
        }

        private static string[] BuildAll()
        {
            var result = new List<string>
            {
                Abort, Brakes, Gear, Lights, Rcs, Sas, SolarPanels, RadiatorPanels, Antennas, Science
            };
            result.AddRange(CustomGroups);
            result.Sort(StringComparer.Ordinal);
            return result.ToArray();
        }

        private static Dictionary<string, string> BuildLookup()
        {
            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string name in All)
            {
                lookup[name] = name;
            }

            lookup["light"] = Lights;
            lookup["rcs"] = Rcs;
            lookup["sas"] = Sas;
            lookup["solar"] = SolarPanels;
            lookup["solarpanel"] = SolarPanels;
            lookup["panels"] = SolarPanels;
            lookup["radiator"] = RadiatorPanels;
            lookup["radiators"] = RadiatorPanels;
            lookup["radiatorpanel"] = RadiatorPanels;
            lookup["antenna"] = Antennas;
            lookup["antennae"] = Antennas;
            lookup["comm"] = Antennas;
            lookup["comms"] = Antennas;
            lookup["experiment"] = Science;
            lookup["experiments"] = Science;

            for (int i = 0; i < CustomGroups.Length; i++)
            {
                lookup["Custom" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)] = CustomGroups[i];
                lookup["Custom" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(2, '0')] = CustomGroups[i];
            }

            return lookup;
        }
    }

    /// <summary>SAS autopilot modes, named after KSP's VesselAutopilot.AutopilotMode values.</summary>
    public static class SasModes
    {
        public static readonly string[] All =
        {
            "StabilityAssist", "Prograde", "Retrograde", "Normal", "Antinormal",
            "RadialIn", "RadialOut", "Target", "AntiTarget", "Maneuver"
        };

        private static readonly Dictionary<string, string> Lookup = BuildLookup();

        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            string value;
            return Lookup.TryGetValue(name.Trim(), out value) ? value : null;
        }

        private static Dictionary<string, string> BuildLookup()
        {
            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string mode in All)
            {
                lookup[mode] = mode;
            }

            lookup["stability"] = "StabilityAssist";
            lookup["stabilityassist"] = "StabilityAssist";
            lookup["kill"] = "StabilityAssist";
            lookup["antinormal"] = "Antinormal";
            lookup["anti-normal"] = "Antinormal";
            lookup["antiNormal"] = "Antinormal";
            lookup["radial"] = "RadialOut";
            lookup["radialin"] = "RadialIn";
            lookup["radial-in"] = "RadialIn";
            lookup["radialout"] = "RadialOut";
            lookup["radial-out"] = "RadialOut";
            lookup["antitarget"] = "AntiTarget";
            lookup["anti-target"] = "AntiTarget";
            lookup["anti"] = "AntiTarget";
            lookup["node"] = "Maneuver";
            lookup["maneuver"] = "Maneuver";
            return lookup;
        }
    }
}
