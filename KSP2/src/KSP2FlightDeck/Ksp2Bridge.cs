using System;
using System.Collections.Generic;
using FlightDeck.Core;
using KSP.Game;
using KSP.Sim;
using KSP.Sim.DeltaV;
using KSP.Sim.impl;
using KSP.Sim.ResourceSystem;
using KSP.Sim.State;

namespace FlightDeck.Ksp2
{
    /// <summary>
    /// Maps the Flight Deck API onto KSP 2's simulation. Everything here runs on the Unity main
    /// thread (the router guarantees it); <see cref="IsReady"/> is safe to read from any thread.
    /// </summary>
    internal sealed class Ksp2Bridge : IGameBridge
    {
        private static readonly Dictionary<string, KSPActionGroup> NativeGroups = BuildNativeGroups();
        private static readonly Dictionary<string, AutopilotMode> AutopilotModes = BuildAutopilotModes();

        private volatile bool _ready;
        private volatile string _notReadyReason = "Waiting for the game to load.";

        public bool IsReady
        {
            get { return _ready; }
        }

        public string NotReadyReason
        {
            get { return _notReadyReason; }
        }

        /// <summary>Called once per frame from the addon.</summary>
        public void UpdateCachedState()
        {
            try
            {
                GameInstance game = GameManager.Instance == null ? null : GameManager.Instance.Game;
                IViewController controller = game == null ? null : game.ViewController;
                if (controller == null)
                {
                    _ready = false;
                    _notReadyReason = "Waiting for the game to load.";
                    return;
                }

                VesselComponent vessel = null;
                bool hasVessel = controller.TryGetActiveSimVessel(out vessel, true);
                _ready = hasVessel && vessel != null && vessel.IsValidInSim;
                _notReadyReason = _ready ? null : "No vessel is loaded. Enter the flight scene or launch a craft.";
            }
            catch (Exception)
            {
                _ready = false;
                _notReadyReason = "The game is still loading.";
            }
        }

        public Dictionary<string, object> GetSnapshot()
        {
            GameInstance game = RequireGame();
            VesselComponent vessel = RequireVessel();

            return new Dictionary<string, object>
            {
                { "vessel", VesselInfo(vessel) },
                { "throttle", (double)vessel.flightCtrlState.mainThrottle },
                { "sas", SasState(vessel) },
                { "actionGroups", AllActionGroups(vessel) },
                { "staging", StagingState(vessel) },
                { "orbit", OrbitState(vessel) },
                { "resources", ResourceState(game, vessel) },
                { "timeWarp", TimeWarpState(game) },
                { "paused", Paused(game) },
                { "deltaV", DeltaVState(vessel) },
                { "timestamp", (double)PlanetariumTime(game) }
            };
        }

        public Dictionary<string, object> Execute(string command, Dictionary<string, object> arguments)
        {
            GameInstance game = RequireGame();
            VesselComponent vessel = RequireVessel();
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
                    return new Dictionary<string, object> { { "Throttle", (double)vessel.flightCtrlState.mainThrottle } };

                case "throttle.set":
                    return new Dictionary<string, object> { { "Throttle", SetThrottle(vessel, NumberArgument(arguments, "throttle")) } };

                case "sas.get":
                    return SasState(vessel);

                case "sas.set":
                    return SetSas(vessel, Json.GetBool(arguments, "enabled"), Json.GetString(arguments, "mode"));

                case "sas.mode":
                    return SetSas(vessel, null, Json.GetString(arguments, "mode"));

                case "sas.toggle":
                    return SetSas(vessel, !IsSasEnabled(vessel), null);

                case "stage.get":
                    return StagingState(vessel);

                case "stage.next":
                    vessel.ActivateNextStage();
                    return StagingState(vessel);

                case "timewarp.get":
                    return TimeWarpState(game);

                case "timewarp.set":
                {
                    int index = (int)NumberArgument(arguments, "index");
                    SetTimeWarpIndex(game, index);
                    return TimeWarpState(game);
                }

                case "pause.get":
                    return new Dictionary<string, object> { { "Paused", Paused(game) } };

                case "pause.set":
                    SetPaused(game, BoolArgument(arguments, "paused"));
                    return new Dictionary<string, object> { { "Paused", Paused(game) } };

                case "pause.toggle":
                    SetPaused(game, !Paused(game));
                    return new Dictionary<string, object> { { "Paused", Paused(game) } };

                case "resources.get":
                    return new Dictionary<string, object> { { "Resources", ResourceState(game, vessel) } };

                case "orbit.get":
                    return OrbitState(vessel);

                default:
                    throw new ApiException("unknown_command", "The mod does not implement the '" + command + "' command.");
            }
        }

        public void MarkNotReady()
        {
            _ready = false;
        }

        private static GameInstance RequireGame()
        {
            GameInstance game = GameManager.Instance == null ? null : GameManager.Instance.Game;
            if (game == null || game.ViewController == null)
            {
                throw new ApiException("not_in_flight", "The game is still loading.");
            }

            return game;
        }

        private static VesselComponent RequireVessel()
        {
            GameInstance game = RequireGame();
            VesselComponent vessel;
            if (!game.ViewController.TryGetActiveSimVessel(out vessel, true) || vessel == null)
            {
                throw new ApiException("not_in_flight", "No vessel is loaded. Enter the flight scene or launch a craft.");
            }

            return vessel;
        }

        private static Dictionary<string, object> VesselInfo(VesselComponent vessel)
        {
            return new Dictionary<string, object>
            {
                { "name", vessel.DisplayName ?? vessel.Name },
                { "situation", vessel.Situation.ToString() },
                { "situationString", vessel.Situation.ToString() },
                { "body", vessel.mainBody != null ? vessel.mainBody.bodyName : null },
                { "mass", vessel.totalMass },
                { "crew", vessel.TotalCommandCrewCapacity },
                { "landed", vessel.Landed },
                { "splashed", vessel.Splashed },
                { "latitude", vessel.Latitude },
                { "longitude", vessel.Longitude }
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

        private static bool GetActionGroupState(VesselComponent vessel, string group)
        {
            KSPActionGroup native;
            if (!NativeGroups.TryGetValue(group, out native))
            {
                throw new ApiException("unknown_action_group", "KSP 2 has no '" + group + "' action group.");
            }

            return ToBool(vessel.GetActionGroupState(native));
        }

        private static bool SetActionGroupState(VesselComponent vessel, string group, bool state)
        {
            KSPActionGroup native;
            if (!NativeGroups.TryGetValue(group, out native))
            {
                throw new ApiException("unknown_action_group", "KSP 2 has no '" + group + "' action group.");
            }

            vessel.SetActionGroup(native, state);
            return ToBool(vessel.GetActionGroupState(native));
        }

        /// <summary>KSP 2 reports None/True/False/Mixed; treat Mixed as "at least partly on".</summary>
        private static bool ToBool(KSPActionGroupState state)
        {
            return state == KSPActionGroupState.True || state == KSPActionGroupState.Mixed;
        }

        private static Dictionary<string, object> AllActionGroups(VesselComponent vessel)
        {
            var groups = new Dictionary<string, object>();
            foreach (string name in ActionGroupNames.WithResourceScan)
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

        private static bool IsSasEnabled(VesselComponent vessel)
        {
            VesselAutopilot autopilot = vessel.Autopilot;
            return autopilot != null && autopilot.Enabled;
        }

        private static Dictionary<string, object> SasState(VesselComponent vessel)
        {
            VesselAutopilot autopilot = vessel.Autopilot;
            string mode = autopilot != null ? autopilot.AutopilotMode.ToString() : "StabilityAssist";
            bool canSetMode = autopilot != null && autopilot.CanSetMode(autopilot.AutopilotMode);

            return new Dictionary<string, object>
            {
                { "Enabled", IsSasEnabled(vessel) },
                { "Mode", mode },
                { "CanSetMode", canSetMode }
            };
        }

        private static Dictionary<string, object> SetSas(VesselComponent vessel, bool? enabled, string mode)
        {
            VesselAutopilot autopilot = vessel.Autopilot;
            if (autopilot == null)
            {
                throw new ApiException("sas_unavailable", "This vessel has no SAS: add a pilot or a probe core.");
            }

            AutopilotMode target;
            bool hasMode = !string.IsNullOrEmpty(mode);
            if (hasMode && !AutopilotModes.TryGetValue(mode, out target))
            {
                throw new ApiException("unknown_sas_mode", "Unknown SAS mode '" + mode + "'.");
            }

            if (enabled.HasValue && !enabled.Value)
            {
                autopilot.Deactivate();
                return SasState(vessel);
            }

            if (enabled.HasValue && enabled.Value)
            {
                AutopilotMode engageMode = hasMode ? AutopilotModes[mode] : autopilot.AutopilotMode;
                if (!autopilot.CanSetMode(engageMode))
                {
                    throw new ApiException(
                        "sas_mode_unavailable",
                        "SAS cannot enter " + engageMode + " right now. Target and maneuver modes need a target or a maneuver node.");
                }

                // KSP 2's Activate(mode) sets the mode and engages in one call.
                if (!autopilot.Activate(engageMode))
                {
                    throw new ApiException("sas_unavailable", "KSP refused to engage SAS.");
                }

                return SasState(vessel);
            }

            if (hasMode)
            {
                AutopilotMode requested;
                AutopilotModes.TryGetValue(mode, out requested);
                if (!autopilot.CanSetMode(requested))
                {
                    throw new ApiException(
                        "sas_mode_unavailable",
                        "SAS cannot enter " + mode + " right now. Target and maneuver modes need a target or a maneuver node.");
                }

                if (!autopilot.SetMode(requested) || autopilot.AutopilotMode != requested)
                {
                    throw new ApiException("sas_mode_unavailable", "SAS cannot enter " + mode + " right now.");
                }
            }

            return SasState(vessel);
        }

        private static double SetThrottle(VesselComponent vessel, double value)
        {
            float throttle = (float)Math.Max(0d, Math.Min(1d, value));
            FlightCtrlState state = vessel.flightCtrlState;
            state.mainThrottle = throttle;
            vessel.SetFlightControlState(state, false);
            return vessel.flightCtrlState.mainThrottle;
        }

        private static Dictionary<string, object> StagingState(VesselComponent vessel)
        {
            // KSP 2 exposes HasLaunched, which is exactly the "press Launch first, then Stage"
            // behaviour the deck uses.
            bool prelaunch = !vessel.HasLaunched || vessel.Situation == VesselSituations.PreLaunch;
            int stageCount = StageCount(vessel);
            int currentStage = prelaunch ? Math.Max(0, stageCount - 1) : 0;

            return new Dictionary<string, object>
            {
                { "CurrentStage", currentStage },
                { "StageCount", stageCount },
                { "CanStage", vessel.IsControllable },
                { "Prelaunch", prelaunch },
                { "Label", prelaunch ? "Launch" : "Stage" },
                { "Situation", vessel.Situation.ToString() }
            };
        }

        /// <summary>
        /// KSP 2 has no public "current stage index"; the staging stack only survives on the parts
        /// as activation stages, so the count is derived from those.
        /// </summary>
        private static int StageCount(VesselComponent vessel)
        {
            try
            {
                PartOwnerComponent owner = vessel.SimulationObject.PartOwner;
                if (owner == null || owner.Parts == null)
                {
                    return 0;
                }

                int highest = 0;
                foreach (PartComponent part in owner.Parts)
                {
                    if (part != null && part.ActivationStage > highest)
                    {
                        highest = part.ActivationStage;
                    }
                }

                return highest + 1;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static Dictionary<string, object> TimeWarpState(GameInstance game)
        {
            TimeWarp warp = game.ViewController.TimeWarp;
            bool physicsWarp;
            int maxIndex = warp.GetMaxRateIndex(false, out physicsWarp);

            return new Dictionary<string, object>
            {
                { "Index", warp.CurrentRateIndex },
                { "Rate", (double)warp.CurrentRate },
                { "MaxIndex", maxIndex },
                { "Paused", game.ViewController.IsPaused }
            };
        }

        private static void SetTimeWarpIndex(GameInstance game, int index)
        {
            TimeWarp warp = game.ViewController.TimeWarp;
            bool physicsWarp;
            int maxIndex = warp.GetMaxRateIndex(false, out physicsWarp);
            int clamped = Math.Max(0, Math.Min(maxIndex, index));
            warp.SetRateIndex(clamped, true);
        }

        private static bool Paused(GameInstance game)
        {
            return game.ViewController.IsPaused;
        }

        private static void SetPaused(GameInstance game, bool paused)
        {
            game.ViewController.SetPause(paused);
        }

        private static double PlanetariumTime(GameInstance game)
        {
            try
            {
                return game.ViewController.universalTime;
            }
            catch (Exception)
            {
                return 0d;
            }
        }

        private static Dictionary<string, object> OrbitState(VesselComponent vessel)
        {
            PatchedConicsOrbit orbit = vessel.Orbit;
            double atmosphere = vessel.mainBody != null ? vessel.mainBody.atmosphereDepth : 0d;
            bool usable = orbit != null && !vessel.Landed && !vessel.Splashed;

            if (!usable)
            {
                return new Dictionary<string, object>
                {
                    { "available", false },
                    { "landed", vessel.Landed },
                    { "splashed", vessel.Splashed },
                    { "altitude", vessel.AltitudeFromSeaLevel },
                    { "surfaceSpeed", vessel.SrfSpeedMagnitude },
                    { "verticalSpeed", vessel.VerticalSrfSpeed },
                    { "radarAltitude", vessel.AltitudeFromSurface },
                    { "atmosphereDepth", atmosphere }
                };
            }

            return new Dictionary<string, object>
            {
                { "available", true },
                { "body", vessel.mainBody != null ? vessel.mainBody.bodyName : null },
                { "apoapsis", orbit.Apoapsis },
                { "periapsis", orbit.Periapsis },
                { "altitude", vessel.AltitudeFromSeaLevel },
                { "radius", orbit.radius },
                { "inclination", orbit.inclination },
                { "eccentricity", orbit.eccentricity },
                { "period", orbit.period },
                { "timeToApoapsis", orbit.TimeToAp },
                { "timeToPeriapsis", orbit.TimeToPe },
                { "orbitalSpeed", vessel.OrbitalSpeed },
                { "surfaceSpeed", vessel.SrfSpeedMagnitude },
                { "verticalSpeed", vessel.VerticalSrfSpeed },
                { "radarAltitude", vessel.AltitudeFromSurface },
                { "semiMajorAxis", orbit.semiMajorAxis },
                { "atmosphereDepth", atmosphere }
            };
        }

        private static Dictionary<string, object> DeltaVState(VesselComponent vessel)
        {
            VesselDeltaVComponent deltaV = vessel.VesselDeltaV;
            if (deltaV == null)
            {
                return new Dictionary<string, object> { { "available", false } };
            }

            return new Dictionary<string, object>
            {
                { "available", true },
                { "vacuum", deltaV.TotalDeltaVVac },
                { "asl", deltaV.TotalDeltaVASL },
                { "burnTime", deltaV.TotalBurnTime },
                { "calculating", deltaV.IsCalculationRunning }
            };
        }

        /// <summary>
        /// Totals every resource container on the vessel. Part and resource enumeration differs
        /// between KSP 2 builds (Redux refactors some internals), so failures degrade to whatever
        /// the vessel-wide percentages expose instead of breaking the snapshot.
        /// </summary>
        private static Dictionary<string, object> ResourceState(GameInstance game, VesselComponent vessel)
        {
            var totals = new Dictionary<string, double[]>(StringComparer.Ordinal);

            try
            {
                PartOwnerComponent owner = vessel.SimulationObject.PartOwner;
                if (owner != null && owner.Parts != null)
                {
                    foreach (PartComponent part in owner.Parts)
                    {
                        if (part == null || part.PartResourceContainer == null)
                        {
                            continue;
                        }

                        foreach (ContainedResourceData data in part.PartResourceContainer.GetAllResourcesContainedData())
                        {
                            string name = ResourceName(game, data.ResourceID);
                            if (string.IsNullOrEmpty(name))
                            {
                                continue;
                            }

                            double[] pair;
                            if (!totals.TryGetValue(name, out pair))
                            {
                                pair = new double[2];
                                totals[name] = pair;
                            }

                            pair[0] += data.StoredUnits;
                            pair[1] += data.CapacityUnits;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Ksp2Log.Warn("Resource enumeration failed, falling back to vessel percentages: " + ex.Message);
                totals.Clear();
            }

            var result = new Dictionary<string, object>();

            if (totals.Count > 0)
            {
                var ordered = new List<KeyValuePair<string, double[]>>(totals);
                ordered.Sort(delegate(KeyValuePair<string, double[]> left, KeyValuePair<string, double[]> right)
                {
                    return right.Value[1].CompareTo(left.Value[1]);
                });

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

            AddPercentage(result, "LiquidFuel", SafeFuel(vessel));
            AddPercentage(result, "ElectricCharge", SafeElectricity(vessel));
            return result;
        }

        private static string ResourceName(GameInstance game, ResourceDefinitionID id)
        {
            try
            {
                return game.ResourceDefinitionDatabase.GetResourceNameFromID(id);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static double SafeFuel(VesselComponent vessel)
        {
            try
            {
                return vessel.FuelPercentage;
            }
            catch (Exception)
            {
                return 0d;
            }
        }

        private static double SafeElectricity(VesselComponent vessel)
        {
            try
            {
                return vessel.StoredElectricityPercentage;
            }
            catch (Exception)
            {
                return 0d;
            }
        }

        private static void AddPercentage(Dictionary<string, object> target, string name, double fraction)
        {
            if (double.IsNaN(fraction) || double.IsInfinity(fraction))
            {
                fraction = 0d;
            }

            target[name] = new Dictionary<string, object>
            {
                { "amount", fraction },
                { "max", 1d },
                { "fraction", fraction },
                { "percentageOnly", true }
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
                { ActionGroupNames.Lights, KSPActionGroup.Lights },
                { ActionGroupNames.Rcs, KSPActionGroup.RCS },
                { ActionGroupNames.Sas, KSPActionGroup.SAS },
                { ActionGroupNames.SolarPanels, KSPActionGroup.SolarPanels },
                { ActionGroupNames.RadiatorPanels, KSPActionGroup.RadiatorPanels },
                { ActionGroupNames.Science, KSPActionGroup.Science },
                { ActionGroupNames.ResourceScan, KSPActionGroup.ResourceScan },
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

        private static Dictionary<string, AutopilotMode> BuildAutopilotModes()
        {
            var modes = new Dictionary<string, AutopilotMode>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in SasModes.All)
            {
                modes[name] = (AutopilotMode)Enum.Parse(typeof(AutopilotMode), name);
            }

            return modes;
        }
    }
}
