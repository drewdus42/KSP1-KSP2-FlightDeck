using System;
using System.Collections.Generic;
using FlightDeck.Core;
using KSP.UI.Screens;

namespace FlightDeck
{
    /// <summary>
    /// Translates Flight Deck API commands into KSP calls. Everything here must run on the
    /// main thread (the router guarantees that); <see cref="IsReady"/> is safe from any thread.
    /// </summary>
    internal sealed class GameBridge : IGameBridge
    {
        private static readonly Dictionary<string, KSPActionGroup> NativeGroups = BuildNativeGroups();
        private static readonly Dictionary<string, VesselAutopilot.AutopilotMode> AutopilotModes = BuildAutopilotModes();

        private volatile bool _ready;

        public bool IsReady
        {
            get { return _ready; }
        }

        public void UpdateCachedState()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            _ready = HighLogic.LoadedSceneIsFlight && vessel != null && vessel.loaded;
        }

        public Dictionary<string, object> GetSnapshot()
        {
            Vessel vessel = RequireVessel();

            return new Dictionary<string, object>
            {
                { "vessel", VesselInfo(vessel) },
                { "throttle", (double)vessel.ctrlState.mainThrottle },
                { "sas", SasState(vessel) },
                { "actionGroups", AllActionGroups(vessel) },
                { "staging", StagingState(vessel) },
                { "orbit", OrbitState(vessel) },
                { "resources", ResourceState(vessel) },
                { "timeWarp", TimeWarpState() },
                { "paused", FlightDriver.Pause },
                { "deltaV", DeltaVState(vessel) },
                { "timestamp", (double)Planetarium.GetUniversalTime() }
            };
        }

        public Dictionary<string, object> Execute(string command, Dictionary<string, object> arguments)
        {
            Vessel vessel = RequireVessel();
            arguments = arguments ?? new Dictionary<string, object>();

            switch (command)
            {
                case "actiongroup.get":
                {
                    string group = GroupArgument(arguments);
                    return GroupResult(group, GetActionGroupState(vessel, group));
                }

                case "actiongroup.set":
                {
                    string group = GroupArgument(arguments);
                    bool state = BoolArgument(arguments, "state");
                    return GroupResult(group, SetActionGroupState(vessel, group, state));
                }

                case "actiongroup.toggle":
                {
                    string group = GroupArgument(arguments);
                    return GroupResult(group, SetActionGroupState(vessel, group, !GetActionGroupState(vessel, group)));
                }

                case "actiongroup.all":
                    return new Dictionary<string, object> { { "Groups", AllActionGroups(vessel) } };

                case "throttle.get":
                    return new Dictionary<string, object> { { "Throttle", (double)vessel.ctrlState.mainThrottle } };

                case "throttle.set":
                    return new Dictionary<string, object> { { "Throttle", SetThrottle(vessel, NumberArgument(arguments, "throttle")) } };

                case "sas.get":
                    return SasState(vessel);

                case "sas.set":
                {
                    bool? enabled = Json.GetBool(arguments, "enabled");
                    string mode = Json.GetString(arguments, "mode");
                    return SetSas(vessel, enabled, mode);
                }

                case "sas.mode":
                    return SetSas(vessel, null, Convert.ToString(arguments["mode"]));

                case "sas.toggle":
                    return SetSas(vessel, !IsSasEnabled(vessel), null);

                case "stage.get":
                    return StagingState(vessel);

                case "stage.next":
                    StageManager.ActivateNextStage();
                    return StagingState(vessel);

                case "timewarp.get":
                    return TimeWarpState();

                case "timewarp.set":
                {
                    int index = (int)NumberArgument(arguments, "index");
                    TimeWarp.SetRate(index, false);
                    return TimeWarpState();
                }

                case "pause.get":
                    return new Dictionary<string, object> { { "Paused", FlightDriver.Pause } };

                case "pause.set":
                    FlightDriver.SetPause(BoolArgument(arguments, "paused"), true);
                    return new Dictionary<string, object> { { "Paused", FlightDriver.Pause } };

                case "pause.toggle":
                    FlightDriver.SetPause(!FlightDriver.Pause, true);
                    return new Dictionary<string, object> { { "Paused", FlightDriver.Pause } };

                case "resources.get":
                    return new Dictionary<string, object> { { "Resources", ResourceState(vessel) } };

                case "orbit.get":
                    return OrbitState(vessel);

                default:
                    throw new ApiException("unknown_command", "The mod does not implement the '" + command + "' command.");
            }
        }

        /// <summary>
        /// Called when the addon is shutting down so in-flight requests fail fast instead of
        /// waiting for a game thread that will never pump again.
        /// </summary>
        public void MarkNotReady()
        {
            _ready = false;
        }

        private static Vessel RequireVessel()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded)
            {
                throw new ApiException("not_in_flight", "No vessel is loaded. Enter the flight scene or launch a craft.");
            }

            return vessel;
        }

        private static Dictionary<string, object> VesselInfo(Vessel vessel)
        {
            return new Dictionary<string, object>
            {
                { "name", vessel.vesselName },
                { "situation", vessel.situation.ToString() },
                { "situationString", vessel.SituationString },
                { "body", vessel.mainBody != null ? vessel.mainBody.bodyName : null },
                { "mass", (double)vessel.GetTotalMass() },
                { "crew", vessel.GetCrewCount() },
                { "landed", vessel.Landed },
                { "splashed", vessel.Splashed }
            };
        }

        private static Dictionary<string, object> GroupResult(string group, bool state)
        {
            return new Dictionary<string, object>
            {
                { "Group", group },
                { "State", state }
            };
        }

        private static bool GetActionGroupState(Vessel vessel, string group)
        {
            KSPActionGroup native;
            if (NativeGroups.TryGetValue(group, out native))
            {
                return vessel.ActionGroups[native];
            }

            switch (group)
            {
                case ActionGroupNames.SolarPanels:
                    return AllDeployed<ModuleDeployableSolarPanel>(vessel);
                case ActionGroupNames.RadiatorPanels:
                    return AllDeployed<ModuleDeployableRadiator>(vessel);
                case ActionGroupNames.Antennas:
                    return AllDeployed<ModuleDeployableAntenna>(vessel);
                case ActionGroupNames.Science:
                    return AnyScienceAvailable(vessel);
                default:
                    throw new ApiException("unknown_action_group", "Unknown action group '" + group + "'.");
            }
        }

        private static bool SetActionGroupState(Vessel vessel, string group, bool state)
        {
            KSPActionGroup native;
            if (NativeGroups.TryGetValue(group, out native))
            {
                vessel.ActionGroups.SetGroup(native, state);
                return vessel.ActionGroups[native];
            }

            switch (group)
            {
                case ActionGroupNames.SolarPanels:
                    SetDeployed<ModuleDeployableSolarPanel>(vessel, state);
                    break;
                case ActionGroupNames.RadiatorPanels:
                    SetDeployed<ModuleDeployableRadiator>(vessel, state);
                    break;
                case ActionGroupNames.Antennas:
                    SetDeployed<ModuleDeployableAntenna>(vessel, state);
                    break;
                case ActionGroupNames.Science:
                    if (state)
                    {
                        RunScience(vessel);
                    }

                    break;
                default:
                    throw new ApiException("unknown_action_group", "Unknown action group '" + group + "'.");
            }

            return GetActionGroupState(vessel, group);
        }

        private static Dictionary<string, object> AllActionGroups(Vessel vessel)
        {
            var groups = new Dictionary<string, object>();
            foreach (string name in ActionGroupNames.All)
            {
                try
                {
                    groups[name] = GetActionGroupState(vessel, name);
                }
                catch (Exception)
                {
                    groups[name] = false;
                }
            }

            return groups;
        }

        private static bool AllDeployed<T>(Vessel vessel) where T : ModuleDeployablePart
        {
            List<T> modules = vessel.FindPartModulesImplementing<T>();
            if (modules == null || modules.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] == null || modules[i].deployState != ModuleDeployablePart.DeployState.EXTENDED)
                {
                    return false;
                }
            }

            return true;
        }

        private static void SetDeployed<T>(Vessel vessel, bool extended) where T : ModuleDeployablePart
        {
            List<T> modules = vessel.FindPartModulesImplementing<T>();
            if (modules == null)
            {
                return;
            }

            for (int i = 0; i < modules.Count; i++)
            {
                T module = modules[i];
                if (module == null)
                {
                    continue;
                }

                if (extended)
                {
                    module.Extend();
                }
                else
                {
                    module.Retract();
                }
            }
        }

        private static bool AnyScienceAvailable(Vessel vessel)
        {
            List<ModuleScienceExperiment> experiments = vessel.FindPartModulesImplementing<ModuleScienceExperiment>();
            if (experiments == null)
            {
                return false;
            }

            for (int i = 0; i < experiments.Count; i++)
            {
                ModuleScienceExperiment experiment = experiments[i];
                if (experiment != null && !experiment.Inoperable && !experiment.Deployed)
                {
                    return true;
                }
            }

            return false;
        }

        private static void RunScience(Vessel vessel)
        {
            List<ModuleScienceExperiment> experiments = vessel.FindPartModulesImplementing<ModuleScienceExperiment>();
            if (experiments == null)
            {
                return;
            }

            for (int i = 0; i < experiments.Count; i++)
            {
                ModuleScienceExperiment experiment = experiments[i];
                if (experiment == null || experiment.Inoperable || experiment.Deployed)
                {
                    continue;
                }

                try
                {
                    experiment.DeployExperiment();
                }
                catch (Exception ex)
                {
                    PluginLog.Warn("Science experiment " + experiment.experimentID + " failed: " + ex.Message);
                }
            }
        }

        private static bool IsSasEnabled(Vessel vessel)
        {
            VesselAutopilot autopilot = vessel.Autopilot;

            // VesselAutopilot.Update() mirrors the SAS action group every frame: group on with
            // the autopilot off calls Enable(), group off with the autopilot on calls Disable().
            // The group is therefore the state the player sees, and the only one worth trusting.
            bool groupOn = vessel.ActionGroups[KSPActionGroup.SAS];
            if (groupOn)
            {
                return true;
            }

            return autopilot != null && autopilot.Enabled;
        }

        private static void SyncSasActionGroup(Vessel vessel, bool active)
        {
            try
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, active);

                bool actual = vessel.ActionGroups[KSPActionGroup.SAS];
                if (actual != active)
                {
                    // SetGroup respects the action group cooldown and can silently do nothing.
                    PluginLog.Warn(
                        "SAS action group is still " + (actual ? "on" : "off") + " after requesting " +
                        (active ? "on" : "off") + " (" +
                        vessel.ActionGroups.CooldownTimeRemaining(KSPActionGroup.SAS).ToString("0.0") +
                        "s cooldown).");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warn("Could not set the SAS action group to " + active + ": " + ex.Message);
            }
        }

        private static Dictionary<string, object> SasState(Vessel vessel)
        {
            VesselAutopilot autopilot = vessel.Autopilot;
            string mode = autopilot != null ? autopilot.Mode.ToString() : "StabilityAssist";
            bool canSetMode = autopilot != null && autopilot.CanSetMode(autopilot.Mode);

            return new Dictionary<string, object>
            {
                { "Enabled", IsSasEnabled(vessel) },
                { "Mode", mode },
                { "CanSetMode", canSetMode }
            };
        }

        private static Dictionary<string, object> SetSas(Vessel vessel, bool? enabled, string mode)
        {
            VesselAutopilot autopilot = vessel.Autopilot;
            if (autopilot == null)
            {
                throw new ApiException("sas_unavailable", "This vessel has no SAS: add a pilot or a probe core.");
            }

            IList<SasOperation> operations = SasPlan.Build(IsSasEnabled(vessel), autopilot.Mode.ToString(), enabled, mode);

            foreach (SasOperation operation in operations)
            {
                switch (operation.Kind)
                {
                    case SasOperationKind.Disable:
                        // Clear the action group first: it is what KSP's Update() reads, so
                        // disabling the autopilot alone would be undone on the next frame.
                        SyncSasActionGroup(vessel, false);
                        autopilot.Disable();
                        break;

                    case SasOperationKind.Enable:
                    {
                        VesselAutopilot.AutopilotMode target;
                        if (!AutopilotModes.TryGetValue(operation.Mode, out target))
                        {
                            throw new ApiException("unknown_sas_mode", "Unknown SAS mode '" + operation.Mode + "'.");
                        }

                        if (!autopilot.CanSetMode(target))
                        {
                            throw new ApiException(
                                "sas_mode_unavailable",
                                "SAS cannot enter " + operation.Mode + " right now. Target and maneuver modes need a target or a maneuver node.");
                        }

                        // Enable(mode) is the only overload that keeps the mode: Enable() forces
                        // StabilityAssist, which used to silently undo a preceding SetMode().
                        if (!autopilot.Enable(target))
                        {
                            throw new ApiException(
                                "sas_unavailable",
                                "KSP refused to engage SAS. The vessel needs a pilot or a probe core with SAS.");
                        }

                        // Then mirror it into the action group so Update() keeps SAS engaged.
                        SyncSasActionGroup(vessel, operation.ActionGroupState == true);
                        break;
                    }

                    case SasOperationKind.SetMode:
                    {
                        VesselAutopilot.AutopilotMode target;
                        if (!AutopilotModes.TryGetValue(operation.Mode, out target))
                        {
                            throw new ApiException("unknown_sas_mode", "Unknown SAS mode '" + operation.Mode + "'.");
                        }

                        if (!autopilot.SetMode(target) || autopilot.Mode != target)
                        {
                            throw new ApiException(
                                "sas_mode_unavailable",
                                "SAS cannot enter " + operation.Mode + " right now. Target and maneuver modes need a target or a maneuver node.");
                        }

                        break;
                    }
                }
            }

            return SasState(vessel);
        }

        private static double SetThrottle(Vessel vessel, double value)
        {
            float throttle = (float)Math.Max(0d, Math.Min(1d, value));

            // KSP reads the human input state when the vessel is not fly-by-wire, so both are set.
            if (FlightInputHandler.state != null)
            {
                FlightInputHandler.state.mainThrottle = throttle;
            }

            vessel.ctrlState.mainThrottle = throttle;
            return vessel.ctrlState.mainThrottle;
        }

        private static Dictionary<string, object> StagingState(Vessel vessel)
        {
            // On the pad (or with nothing staged yet) the key should read "Launch", matching
            // the original plugin's behaviour of showing Launch on stage 1 only.
            bool onPad = vessel.situation == Vessel.Situations.PRELAUNCH;
            int currentStage = StageManager.CurrentStage;
            int lastStage = StageManager.LastStage;
            bool prelaunch = onPad || currentStage >= lastStage;

            return new Dictionary<string, object>
            {
                { "CurrentStage", currentStage },
                { "StageCount", StageManager.StageCount },
                { "LastStage", lastStage },
                { "CanStage", StageManager.CanSeparate },
                { "Prelaunch", prelaunch },
                { "Label", prelaunch ? "Launch" : "Stage" },
                { "Situation", vessel.situation.ToString() }
            };
        }

        private static Dictionary<string, object> TimeWarpState()
        {
            return new Dictionary<string, object>
            {
                { "Index", TimeWarp.CurrentRateIndex },
                { "Rate", (double)TimeWarp.CurrentRate },
                { "MaxIndex", MaxTimeWarpIndex() },
                { "Paused", FlightDriver.Pause }
            };
        }

        private static int MaxTimeWarpIndex()
        {
            var fetch = TimeWarp.fetch;
            if (fetch == null)
            {
                return 0;
            }

            int physical = fetch.physicsWarpRates != null ? fetch.physicsWarpRates.Length : 1;
            int rails = fetch.warpRates != null ? fetch.warpRates.Length : 1;
            return Math.Max(0, Math.Max(physical, rails) - 1);
        }

        private static Dictionary<string, object> OrbitState(Vessel vessel)
        {
            Orbit orbit = vessel.orbit;
            double atmosphere = vessel.mainBody != null ? vessel.mainBody.atmosphereDepth : 0d;

            // While landed or splashed KSP still exposes an Orbit, but Ap/Pe/period are
            // degenerate (often negative periapsis), so report only what is meaningful.
            bool hasOrbit = orbit != null && !vessel.Landed && !vessel.Splashed;

            if (!hasOrbit)
            {
                return new Dictionary<string, object>
                {
                    { "available", false },
                    { "landed", vessel.Landed },
                    { "splashed", vessel.Splashed },
                    { "altitude", vessel.altitude },
                    { "surfaceSpeed", vessel.srfSpeed },
                    { "verticalSpeed", vessel.verticalSpeed },
                    { "radarAltitude", vessel.radarAltitude },
                    { "atmosphereDepth", atmosphere }
                };
            }

            return new Dictionary<string, object>
            {
                { "available", true },
                { "body", vessel.mainBody != null ? vessel.mainBody.bodyName : null },
                { "apoapsis", orbit.ApA },
                { "periapsis", orbit.PeA },
                { "altitude", vessel.altitude },
                { "radius", orbit.radius },
                { "inclination", orbit.inclination },
                { "eccentricity", orbit.eccentricity },
                { "period", orbit.period },
                { "timeToApoapsis", orbit.timeToAp },
                { "timeToPeriapsis", orbit.timeToPe },
                { "orbitalSpeed", orbit.orbitalSpeed },
                { "surfaceSpeed", vessel.srfSpeed },
                { "verticalSpeed", vessel.verticalSpeed },
                { "radarAltitude", vessel.radarAltitude },
                { "semiMajorAxis", orbit.semiMajorAxis },
                { "atmosphereDepth", atmosphere }
            };
        }

        private static Dictionary<string, object> ResourceState(Vessel vessel)
        {
            var totals = new Dictionary<string, double[]>(StringComparer.Ordinal);

            List<Part> parts = vessel.parts;
            if (parts != null)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    Part part = parts[i];
                    if (part == null || part.Resources == null)
                    {
                        continue;
                    }

                    foreach (PartResource resource in part.Resources)
                    {
                        if (resource == null || string.IsNullOrEmpty(resource.resourceName))
                        {
                            continue;
                        }

                        double[] pair;
                        if (!totals.TryGetValue(resource.resourceName, out pair))
                        {
                            pair = new double[2];
                            totals[resource.resourceName] = pair;
                        }

                        pair[0] += resource.amount;
                        pair[1] += resource.maxAmount;
                    }
                }
            }

            // Longest bar first: the plugin renders the top entries on a single key.
            var ordered = new List<KeyValuePair<string, double[]>>(totals);
            ordered.Sort(delegate(KeyValuePair<string, double[]> left, KeyValuePair<string, double[]> right)
            {
                return right.Value[1].CompareTo(left.Value[1]);
            });

            var result = new Dictionary<string, object>();
            int limit = Math.Min(ordered.Count, 24);
            for (int i = 0; i < limit; i++)
            {
                double amount = ordered[i].Value[0];
                double max = ordered[i].Value[1];
                result[ordered[i].Key] = new Dictionary<string, object>
                {
                    { "amount", amount },
                    { "max", max },
                    { "fraction", max > 0d ? amount / max : 0d }
                };
            }

            return result;
        }

        private static Dictionary<string, object> DeltaVState(Vessel vessel)
        {
            var deltaV = vessel.VesselDeltaV;
            if (deltaV == null || !deltaV.IsReady)
            {
                return new Dictionary<string, object> { { "available", false } };
            }

            return new Dictionary<string, object>
            {
                { "available", true },
                { "vacuum", (double)deltaV.TotalDeltaVVac },
                { "asl", (double)deltaV.TotalDeltaVASL },
                { "burnTime", (double)deltaV.TotalBurnTime }
            };
        }

        private static string GroupArgument(Dictionary<string, object> arguments)
        {
            string group = Json.GetString(arguments, "group", "Group", "id", "ID", "name", "Name");
            if (string.IsNullOrEmpty(group))
            {
                throw new ApiException("missing_parameter", "An action group name is required.");
            }

            string canonical = ActionGroupNames.Normalize(group);
            if (canonical == null)
            {
                throw new ApiException("unknown_action_group", "Unknown action group '" + group + "'.");
            }

            return canonical;
        }

        private static bool BoolArgument(Dictionary<string, object> arguments, string key)
        {
            bool? value = Json.GetBool(arguments, key);
            if (!value.HasValue)
            {
                throw new ApiException("missing_parameter", "The '" + key + "' boolean parameter is required.");
            }

            return value.Value;
        }

        private static double NumberArgument(Dictionary<string, object> arguments, string key)
        {
            double? value = Json.GetDouble(arguments, key);
            if (!value.HasValue)
            {
                throw new ApiException("missing_parameter", "The '" + key + "' numeric parameter is required.");
            }

            return value.Value;
        }

        private static Dictionary<string, KSPActionGroup> BuildNativeGroups()
        {
            var groups = new Dictionary<string, KSPActionGroup>(StringComparer.OrdinalIgnoreCase)
            {
                { ActionGroupNames.Abort, KSPActionGroup.Abort },
                { ActionGroupNames.Brakes, KSPActionGroup.Brakes },
                { ActionGroupNames.Gear, KSPActionGroup.Gear },
                { ActionGroupNames.Lights, KSPActionGroup.Light },
                { ActionGroupNames.Rcs, KSPActionGroup.RCS },
                { ActionGroupNames.Sas, KSPActionGroup.SAS },
                { "Custom01", KSPActionGroup.Custom01 },
                { "Custom02", KSPActionGroup.Custom02 },
                { "Custom03", KSPActionGroup.Custom03 },
                { "Custom04", KSPActionGroup.Custom04 },
                { "Custom05", KSPActionGroup.Custom05 },
                { "Custom06", KSPActionGroup.Custom06 },
                { "Custom07", KSPActionGroup.Custom07 },
                { "Custom08", KSPActionGroup.Custom08 },
                { "Custom09", KSPActionGroup.Custom09 },
                { "Custom10", KSPActionGroup.Custom10 }
            };

            return groups;
        }

        private static Dictionary<string, VesselAutopilot.AutopilotMode> BuildAutopilotModes()
        {
            var modes = new Dictionary<string, VesselAutopilot.AutopilotMode>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in SasModes.All)
            {
                modes[name] = (VesselAutopilot.AutopilotMode)Enum.Parse(typeof(VesselAutopilot.AutopilotMode), name);
            }

            return modes;
        }
    }
}
