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
        public SasOperation(SasOperationKind kind, string mode)
        {
            Kind = kind;
            Mode = mode;
        }

        public SasOperationKind Kind { get; private set; }
        public string Mode { get; private set; }

        public override string ToString()
        {
            return Mode == null ? Kind.ToString() : Kind + "(" + Mode + ")";
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
                operations.Add(new SasOperation(SasOperationKind.Disable, null));
                return operations;
            }

            string targetMode = canonicalRequested ?? canonicalCurrent;

            if (enabled.HasValue && enabled.Value)
            {
                // One call: Enable(mode) sets the mode and turns SAS on.
                operations.Add(new SasOperation(SasOperationKind.Enable, targetMode));
                return operations;
            }

            if (canonicalRequested != null)
            {
                // Mode-only request: re-enable when SAS is already running (SetMode does that
                // internally anyway), otherwise just record the mode.
                operations.Add(currentEnabled
                    ? new SasOperation(SasOperationKind.Enable, canonicalRequested)
                    : new SasOperation(SasOperationKind.SetMode, canonicalRequested));
            }

            return operations;
        }
    }
}
