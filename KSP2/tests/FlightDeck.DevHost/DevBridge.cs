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

    /// <summary>
    /// Simulated KSP 2 vessel. Mirrors the shapes Ksp2Bridge produces (native solar/radiator/
    /// science groups plus Resource Scan) so the Stream Deck plugin can be developed and tested
    /// without launching the game.
    /// </summary>
    internal sealed class DevBridge : IGameBridge
    {
        private readonly Dictionary<string, bool> _groups = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double[]> _resources = new Dictionary<string, double[]>(StringComparer.Ordinal);

        private double _throttle = 0.35;
        private string _sasMode = "Prograde";
        private bool _sasEnabled = true;
        private bool _sasGroup = true;
        private int _stage = 4;

        public DevBridge()
        {
            foreach (string name in ActionGroupNames.WithResourceScan)
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

            if (scenario == DevScenario.Prelaunch)
            {
                _stage = 6;
                _throttle = 0.0;
                _sasEnabled = false;
                _sasGroup = false;
            }
        }

        public void Tick()
        {
            if (!IsReady || Scenario == DevScenario.Paused)
            {
                return;
            }

            Burn("LiquidFuel", _throttle * 0.02);
            Burn("Oxidizer", _throttle * 0.024);
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
                { "name", "Dev Lander 2" },
                { "situation", Scenario == DevScenario.Prelaunch ? "PreLaunch" : "Orbiting" },
                { "situationString", Scenario == DevScenario.Prelaunch ? "Pre-launch" : "Orbiting Kerbin" },
                { "body", "Kerbin" },
                { "mass", 12.4 },
                { "crew", 3 },
                { "landed", Scenario == DevScenario.Prelaunch },
                { "splashed", false },
                { "latitude", -0.0972 },
                { "longitude", -74.5577 }
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
                { "paused", Scenario == DevScenario.Paused },
                { "deltaV", new Dictionary<string, object> { { "available", true }, { "vacuum", 3456.7 }, { "asl", 2980.1 }, { "burnTime", 212.4 }, { "calculating", false } } },
                { "timestamp", 123456.78 }
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
                        bool enable = (bool)arguments["enabled"];
                        _sasEnabled = enable;
                        _sasGroup = enable;
                    }

                    _groups["SAS"] = _sasGroup;
                    return Sas();

                case "sas.mode":
                    _sasMode = (string)arguments["mode"];
                    return Sas();

                case "sas.toggle":
                    _sasEnabled = !_sasEnabled;
                    _sasGroup = _sasEnabled;
                    _groups["SAS"] = _sasGroup;
                    return Sas();

                case "stage.get":
                    return Staging();

                case "stage.next":
                    _stage = Math.Max(0, _stage - 1);
                    return Staging();

                case "timewarp.get":
                    return TimeWarp();

                case "timewarp.set":
                    WarpIndex = Math.Max(0, Math.Min(8, (int)arguments["index"]));
                    return TimeWarp();

                case "pause.get":
                    return new Dictionary<string, object> { { "Paused", Scenario == DevScenario.Paused } };

                case "pause.set":
                    Scenario = (bool)arguments["paused"] ? DevScenario.Paused : DevScenario.Orbit;
                    return new Dictionary<string, object> { { "Paused", Scenario == DevScenario.Paused } };

                case "pause.toggle":
                    Scenario = Scenario == DevScenario.Paused ? DevScenario.Orbit : DevScenario.Paused;
                    return new Dictionary<string, object> { { "Paused", Scenario == DevScenario.Paused } };

                case "resources.get":
                    return new Dictionary<string, object> { { "Resources", GetSnapshot()["resources"] } };

                case "orbit.get":
                    return Orbit();

                default:
                    throw new ApiException("unknown_command", "The dev host does not implement '" + command + "'.");
            }
        }

        public int WarpIndex { get; private set; }

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
                { "Situation", prelaunch ? "PreLaunch" : "Orbiting" }
            };
        }

        private Dictionary<string, object> TimeWarp()
        {
            double[] rates = { 1, 2, 4, 10, 50, 100, 1000, 10000, 100000 };
            return new Dictionary<string, object>
            {
                { "Index", WarpIndex },
                { "Rate", rates[WarpIndex] },
                { "MaxIndex", rates.Length - 1 },
                { "Paused", Scenario == DevScenario.Paused }
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
                { "altitude", 84210.5 },
                { "radius", 684210.5 },
                { "inclination", Math.PI / 4 },
                { "eccentricity", 0.0123 },
                { "period", 3600.0 },
                { "timeToApoapsis", 900.0 },
                { "timeToPeriapsis", 1800.0 },
                { "orbitalSpeed", 2280.4 },
                { "surfaceSpeed", 2281.2 },
                { "verticalSpeed", -3.1 },
                { "radarAltitude", 83000.0 },
                { "semiMajorAxis", 684210.5 },
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
