using System;
using System.Collections.Generic;

namespace FlightDeck.Core
{
    public enum SasOperationKind
    {
        Enable,
        Disable,
        SetMode
    }

    public sealed class SasOperation
    {
        public SasOperation(SasOperationKind kind, string mode, bool? actionGroupState = null)
        {
            Kind = kind;
            Mode = mode;
            ActionGroupState = actionGroupState;
        }

        public SasOperationKind Kind { get; private set; }
        public string Mode { get; private set; }

        /// <summary>
        /// State the SAS action group must end up in. KSP's VesselAutopilot.Update() mirrors the
        /// action group every frame (group on -> Enable(), group off -> Disable()), so changing
        /// only the autopilot gets reverted on the next frame and SAS appears stuck on.
        /// </summary>
        public bool? ActionGroupState { get; private set; }

        public override string ToString()
        {
            string text = Mode == null ? Kind.ToString() : Kind + "(" + Mode + ")";
            return ActionGroupState.HasValue ? text + " group=" + (ActionGroupState.Value ? "on" : "off") : text;
        }
    }

    /// <summary>
    /// Works out the VesselAutopilot calls needed for a SAS request.
    /// This exists as a pure, testable step because KSP's Enable() overload is a trap:
    /// Enable() immediately calls Enable(AutopilotMode.StabilityAssist), so calling
    /// Enable() *after* SetMode() silently throws the requested mode away.
    /// </summary>
    public static class SasPlan
    {
        public static IList<SasOperation> Build(bool currentEnabled, string currentMode, bool? enabled, string mode)
        {
            var operations = new List<SasOperation>();
            string canonicalCurrent = SasModes.Normalize(currentMode) ?? SasModes.All[0];
            string canonicalRequested = string.IsNullOrEmpty(mode) ? null : SasModes.Normalize(mode);

            if (canonicalRequested == null && !string.IsNullOrEmpty(mode))
            {
                throw new ApiException("unknown_sas_mode", "Unknown SAS mode '" + mode + "'.");
            }

            if (enabled.HasValue && !enabled.Value)
            {
                operations.Add(new SasOperation(SasOperationKind.Disable, null, false));
                return operations;
            }

            string targetMode = canonicalRequested ?? canonicalCurrent;

            if (enabled.HasValue && enabled.Value)
            {
                // One call: Enable(mode) sets the mode and turns SAS on.
                operations.Add(new SasOperation(SasOperationKind.Enable, targetMode, true));
                return operations;
            }

            if (canonicalRequested != null)
            {
                // Mode-only request: re-enable when SAS is already running (SetMode does that
                // internally anyway), otherwise just record the mode.
                operations.Add(currentEnabled
                    ? new SasOperation(SasOperationKind.Enable, canonicalRequested, true)
                    : new SasOperation(SasOperationKind.SetMode, canonicalRequested));
            }

            return operations;
        }
    }
}
