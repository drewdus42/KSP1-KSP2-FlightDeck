using System;
using System.Collections.Generic;
using System.Linq;
using FlightDeck.Core;

namespace FlightDeck.Core.Tests
{
    internal sealed class FakeBridge : IGameBridge
    {
        public FakeBridge()
        {
            foreach (string group in ActionGroupNames.All)
            {
                Groups[group] = false;
            }

            Groups["Gear"] = true;
        }

        public bool IsReady { get; set; } = true;

        public Dictionary<string, bool> Groups { get; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public List<string> Calls { get; } = new List<string>();

        public double Throttle { get; set; } = 0.25d;

        public string SasMode { get; set; } = "StabilityAssist";

        public bool SasEnabled { get; set; }

        public int StageNumber { get; set; } = 3;

        public int WarpIndex { get; set; }

        public bool Paused { get; set; }

        public Dictionary<string, object> GetSnapshot()
        {
            return new Dictionary<string, object>
            {
                { "throttle", Throttle },
                { "sas", SasState() },
                { "actionGroups", Groups.ToDictionary(pair => pair.Key, pair => (object)pair.Value) },
                { "staging", new Dictionary<string, object> { { "CurrentStage", StageNumber } } },
                { "timeWarp", new Dictionary<string, object> { { "Index", WarpIndex }, { "Paused", Paused } } },
                { "paused", Paused }
            };
        }

        public Dictionary<string, object> Execute(string command, Dictionary<string, object> arguments)
        {
            Calls.Add(command);
            arguments = arguments ?? new Dictionary<string, object>();

            switch (command)
            {
                case "actiongroup.get":
                {
                    string group = (string)arguments["group"];
                    return Group(group, Groups[group]);
                }

                case "actiongroup.set":
                {
                    string group = (string)arguments["group"];
                    Groups[group] = (bool)arguments["state"];
                    return Group(group, Groups[group]);
                }

                case "actiongroup.toggle":
                {
                    string group = (string)arguments["group"];
                    Groups[group] = !Groups[group];
                    return Group(group, Groups[group]);
                }

                case "actiongroup.all":
                    return new Dictionary<string, object>
                    {
                        { "Groups", Groups.ToDictionary(pair => pair.Key, pair => (object)pair.Value) }
                    };

                case "throttle.get":
                    return new Dictionary<string, object> { { "Throttle", Throttle } };

                case "throttle.set":
                    Throttle = (double)arguments["throttle"];
                    return new Dictionary<string, object> { { "Throttle", Throttle } };

                case "sas.get":
                    return SasState();

                case "sas.set":
                    if (arguments.ContainsKey("mode"))
                    {
                        SasMode = (string)arguments["mode"];
                    }

                    if (arguments.ContainsKey("enabled"))
                    {
                        SasEnabled = (bool)arguments["enabled"];
                    }

                    return SasState();

                case "sas.mode":
                    SasMode = (string)arguments["mode"];
                    return SasState();

                case "sas.toggle":
                    SasEnabled = !SasEnabled;
                    return SasState();

                case "stage.get":
                    return new Dictionary<string, object> { { "CurrentStage", StageNumber } };

                case "stage.next":
                    StageNumber = Math.Max(0, StageNumber - 1);
                    return new Dictionary<string, object> { { "CurrentStage", StageNumber } };

                case "timewarp.get":
                    return new Dictionary<string, object> { { "Index", WarpIndex }, { "Paused", Paused } };

                case "timewarp.set":
                    WarpIndex = (int)arguments["index"];
                    return new Dictionary<string, object> { { "Index", WarpIndex }, { "Paused", Paused } };

                case "pause.get":
                    return new Dictionary<string, object> { { "Paused", Paused } };

                case "pause.set":
                    Paused = (bool)arguments["paused"];
                    return new Dictionary<string, object> { { "Paused", Paused } };

                case "pause.toggle":
                    Paused = !Paused;
                    return new Dictionary<string, object> { { "Paused", Paused } };

                case "resources.get":
                    return new Dictionary<string, object>
                    {
                        { "Resources", new Dictionary<string, object> { { "LiquidFuel", 12.5d } } }
                    };

                case "orbit.get":
                    return new Dictionary<string, object> { { "apoapsis", 80000d } };

                default:
                    throw new ApiException("unknown_command", "fake bridge does not know " + command);
            }
        }

        private Dictionary<string, object> SasState()
        {
            return new Dictionary<string, object>
            {
                { "Enabled", SasEnabled },
                { "Mode", SasMode },
                { "CanSetMode", true }
            };
        }

        private static Dictionary<string, object> Group(string group, bool state)
        {
            return new Dictionary<string, object> { { "Group", group }, { "State", state } };
        }
    }

    internal sealed class SlowDispatcher : IMainThreadDispatcher
    {
        public bool IsMainThread
        {
            get { return false; }
        }

        public T Invoke<T>(Func<T> action, int timeoutMilliseconds)
        {
            throw new TimeoutException("The game did not answer in time.");
        }
    }

    /// <summary>Runs work inline, which is what the router sees when called from the game thread.</summary>
    internal sealed class InlineDispatcher : IMainThreadDispatcher
    {
        public bool IsMainThread
        {
            get { return true; }
        }

        public T Invoke<T>(Func<T> action, int timeoutMilliseconds)
        {
            return action();
        }
    }
}
