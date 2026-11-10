using System;
using System.Collections.Generic;
using FlightDeck.Core;

namespace FlightDeck.DevHost
{
    internal enum DevScenario
    {
        MainMenu,
        Prelaunch,
        Orbit,
        Paused,
        Offline
    }

    /// <summary>Simulated vessel used by the dev host: the same shapes the real GameBridge returns.</summary>
    internal sealed class DevBridge : IGameBridge
    {
        private readonly Dictionary<string, bool> _groups = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double[]> _resources = new Dictionary<string, double[]>(StringComparer.Ordinal);

        private double _throttle = 0.35;
        private string _sasMode = "Prograde";
        private bool _sasEnabled = true;
        private int _stage = 4;
        private int _warpIndex;
        private bool _paused;
        private double _ut = 123456.0;
        private double _altitude = 84210.5;

        public DevBridge()
        {
            foreach (string name in ActionGroupNames.All)
            {
                _groups[name] = false;
            }

            _groups["Gear"] = true;
            _groups["SAS"] = true;
            _groups["SolarPanels"] = true;
            _groups["Science"] = true;

            _resources["LiquidFuel"] = new[] { 320.0, 800.0 };
            _resources["Oxidizer"] = new[] { 390.0, 980.0 };
            _resources["ElectricCharge"] = new[] { 512.4, 1000.0 };
            _resources["MonoPropellant"] = new[] { 40.0, 120.0 };
        }

        public DevScenario Scenario { get; private set; } = DevScenario.Orbit;

        public bool QuitRequested { get; set; }

        public bool IsReady
        {
            get
            {
                return Scenario == DevScenario.Prelaunch
                    || Scenario == DevScenario.Orbit
                    || Scenario == DevScenario.Paused;
            }
        }

        public void SetScenario(DevScenario scenario)
        {
            Scenario = scenario;
            _paused = scenario == DevScenario.Paused;

            if (scenario == DevScenario.Prelaunch)
            {
                _stage = 6;
                _throttle = 0.0;
                _sasEnabled = false;
            }
        }

        public void Tick()
        {
            if (!IsReady || _paused)
            {
                return;
            }

            _ut += 0.05;
            _altitude += 8.5;

            double burn = _throttle * 0.02;
            Burn("LiquidFuel", burn * 2);
            Burn("Oxidizer", burn * 2.4);
            Burn("ElectricCharge", -0.01);
        }

        private void Burn(string resource, double amount)
        {
            double[] pair = _resources[resource];
            pair[0] = Math.Max(0, pair[0] - amount);
        }

        public Dictionary<string, object> GetSnapshot()
        {
            if (!IsReady)
            {
                throw new ApiException("not_in_flight", "The dev host is showing the main menu.");
            }

            var vessel = new Dictionary<string, object>
            {
                { "name", "Dev Lander" },
                { "situation", Scenario == DevScenario.Prelaunch ? "PRELAUNCH" : "ORBITING" },
                { "situationString", Scenario == DevScenario.Prelaunch ? "Pre-launch" : "Orbiting Kerbin" },
                { "body", "Kerbin" },
                { "mass", 12.4 },
                { "crew", 3 },
                { "landed", Scenario == DevScenario.Prelaunch },
                { "splashed", false }
            };

            var groups = new Dictionary<string, object>();
            foreach (var pair in _groups)
            {
                groups[pair.Key] = pair.Value;
            }

            var resources = new Dictionary<string, object>();
            foreach (var pair in _resources)
            {
                resources[pair.Key] = new Dictionary<string, object>
                {
                    { "amount", pair.Value[0] },
                    { "max", pair.Value[1] },
                    { "fraction", pair.Value[1] > 0 ? pair.Value[0] / pair.Value[1] : 0d }
                };
            }

            return new Dictionary<string, object>
            {
                { "vessel", vessel },
                { "throttle", _throttle },
                { "sas", Sas() },
                { "actionGroups", groups },
                { "staging", Staging() },
                { "orbit", Orbit() },
                { "resources", resources },
                { "timeWarp", TimeWarp() },
                { "paused", _paused },
                { "deltaV", new Dictionary<string, object> { { "available", true }, { "vacuum", 3456.7 }, { "asl", 2980.1 }, { "burnTime", 212.4 } } },
                { "timestamp", _ut }
            };
        }

        public Dictionary<string, object> Execute(string command, Dictionary<string, object> arguments)
        {
            if (!IsReady)
            {
                throw new ApiException("not_in_flight", "The dev host is showing the main menu.");
            }

            arguments = arguments ?? new Dictionary<string, object>();

            switch (command)
            {
                case "actiongroup.get":
                {
                    string group = (string)arguments["group"];
                    return Group(group, _groups[group]);
                }

                case "actiongroup.set":
                {
                    string group = (string)arguments["group"];
                    _groups[group] = (bool)arguments["state"];
                    return Group(group, _groups[group]);
                }

                case "actiongroup.toggle":
                {
                    string group = (string)arguments["group"];
                    _groups[group] = !_groups[group];
                    return Group(group, _groups[group]);
                }

                case "actiongroup.all":
                {
                    var groups = new Dictionary<string, object>();
                    foreach (var pair in _groups)
                    {
                        groups[pair.Key] = pair.Value;
                    }

                    return new Dictionary<string, object> { { "Groups", groups } };
                }

                case "throttle.get":
                    return new Dictionary<string, object> { { "Throttle", _throttle } };

                case "throttle.set":
                    _throttle = (double)arguments["throttle"];
                    return new Dictionary<string, object> { { "Throttle", _throttle } };

                case "sas.get":
                    return Sas();

                case "sas.set":
                    if (arguments.ContainsKey("mode"))
                    {
                        _sasMode = (string)arguments["mode"];
                    }

                    if (arguments.ContainsKey("enabled"))
                    {
                        _sasEnabled = (bool)arguments["enabled"];
                    }

                    _groups["SAS"] = _sasEnabled;
                    return Sas();

                case "sas.mode":
                    _sasMode = (string)arguments["mode"];
                    _groups["SAS"] = _sasEnabled;
                    return Sas();

                case "sas.toggle":
                    _sasEnabled = !_sasEnabled;
                    _groups["SAS"] = _sasEnabled;
                    return Sas();

                case "stage.get":
                    return Staging();

                case "stage.next":
                    _stage = Math.Max(0, _stage - 1);
                    return Staging();

                case "timewarp.get":
                    return TimeWarp();

                case "timewarp.set":
                    _warpIndex = Math.Max(0, Math.Min(8, (int)arguments["index"]));
                    return TimeWarp();

                case "pause.get":
                    return new Dictionary<string, object> { { "Paused", _paused } };

                case "pause.set":
                    _paused = (bool)arguments["paused"];
                    return new Dictionary<string, object> { { "Paused", _paused } };

                case "pause.toggle":
                    _paused = !_paused;
                    return new Dictionary<string, object> { { "Paused", _paused } };

                case "resources.get":
                    return new Dictionary<string, object> { { "Resources", GetSnapshot()["resources"] } };

                case "orbit.get":
                    return Orbit();

                default:
                    throw new ApiException("unknown_command", "The dev host does not implement '" + command + "'.");
            }
        }

        private Dictionary<string, object> Sas()
        {
            return new Dictionary<string, object>
            {
                { "Enabled", _sasEnabled },
                { "Mode", _sasMode },
                { "CanSetMode", true }
            };
        }

        private Dictionary<string, object> Staging()
        {
            bool prelaunch = Scenario == DevScenario.Prelaunch;
            return new Dictionary<string, object>
            {
                { "CurrentStage", _stage },
                { "StageCount", 6 },
                { "CanStage", true },
                { "Prelaunch", prelaunch },
                { "Label", prelaunch ? "Launch" : "Stage" },
                { "Situation", prelaunch ? "PRELAUNCH" : "ORBITING" }
            };
        }

        private Dictionary<string, object> TimeWarp()
        {
            double[] rates = { 1, 2, 4, 10, 50, 100, 1000, 10000, 100000 };
            return new Dictionary<string, object>
            {
                { "Index", _warpIndex },
                { "Rate", rates[_warpIndex] },
                { "MaxIndex", rates.Length - 1 },
                { "Paused", _paused }
            };
        }

        private Dictionary<string, object> Orbit()
        {
            return new Dictionary<string, object>
            {
                { "available", true },
                { "body", "Kerbin" },
                { "apoapsis", 120000.0 },
                { "periapsis", 79000.0 },
                { "altitude", _altitude },
                { "radius", 600000.0 + _altitude },
                { "inclination", Math.PI / 4 },
                { "eccentricity", 0.0123 },
                { "period", 3600.0 },
                { "timeToApoapsis", 900.0 },
                { "timeToPeriapsis", 1800.0 },
                { "orbitalSpeed", 2280.4 },
                { "surfaceSpeed", 2281.2 },
                { "verticalSpeed", -3.1 },
                { "radarAltitude", _altitude - 1210 },
                { "semiMajorAxis", 600000.0 + _altitude },
                { "atmosphereDepth", 70000.0 }
            };
        }

        private static Dictionary<string, object> Group(string group, bool state)
        {
            return new Dictionary<string, object>
            {
                { "Group", group },
                { "State", state }
            };
        }
    }
}
