using System.Collections.Generic;
using FlightDeck.Core;
using Xunit;

namespace FlightDeck.Core.Tests
{
    public class SasPlanTests
    {
        private static string Describe(IList<SasOperation> operations)
        {
            var parts = new List<string>();
            foreach (SasOperation operation in operations)
            {
                parts.Add(operation.ToString());
            }

            return string.Join(",", parts);
        }

        [Fact]
        public void EnablingUsesTheRequestedModeInASingleCall()
        {
            // Regression: Enable() resets SAS to StabilityAssist, so the plan must call
            // Enable(mode) rather than SetMode(mode) + Enable().
            IList<SasOperation> operations = SasPlan.Build(false, "StabilityAssist", true, "Prograde");

            Assert.Equal("Enable(Prograde) group=on", Describe(operations));
        }

        [Fact]
        public void DisablingClearsTheSasActionGroup()
        {
            // Regression: KSP's VesselAutopilot.Update() re-enables SAS whenever the SAS action
            // group is still on, so a disable that only calls Autopilot.Disable() is undone on
            // the next frame and SAS looks stuck on (which is what happened with Stability Assist).
            IList<SasOperation> operations = SasPlan.Build(true, "StabilityAssist", false, "StabilityAssist");

            Assert.Equal("Disable group=off", Describe(operations));
            Assert.Equal(false, operations[0].ActionGroupState);
        }

        [Fact]
        public void EnablingSetsTheSasActionGroup()
        {
            IList<SasOperation> operations = SasPlan.Build(false, "Prograde", true, "StabilityAssist");

            Assert.Equal(true, operations[0].ActionGroupState);
        }

        [Fact]
        public void ModeOnlyRequestsDoNotTouchTheActionGroupWhileIdle()
        {
            IList<SasOperation> operations = SasPlan.Build(false, "Prograde", null, "Retrograde");

            Assert.Null(operations[0].ActionGroupState);
        }

        [Fact]
        public void EnablingWithoutAModeKeepsTheCurrentMode()
        {
            IList<SasOperation> operations = SasPlan.Build(false, "Maneuver", true, null);

            Assert.Equal("Enable(Maneuver) group=on", Describe(operations));
        }

        [Fact]
        public void EnablingFallsBackToStabilityWhenNothingIsKnown()
        {
            IList<SasOperation> operations = SasPlan.Build(false, null, true, null);

            Assert.Equal("Enable(StabilityAssist) group=on", Describe(operations));
        }

        [Fact]
        public void DisablingOnlyDisables()
        {
            Assert.Equal("Disable group=off", Describe(SasPlan.Build(true, "Prograde", false, "Prograde")));
            Assert.Equal("Disable group=off", Describe(SasPlan.Build(false, "Prograde", false, null)));
        }

        [Fact]
        public void ModeChangeWhileRunningReEnablesWithThatMode()
        {
            Assert.Equal("Enable(Retrograde) group=on", Describe(SasPlan.Build(true, "Prograde", null, "Retrograde")));
        }

        [Fact]
        public void ModeChangeWhileIdleOnlySetsTheMode()
        {
            Assert.Equal("SetMode(Retrograde)", Describe(SasPlan.Build(false, "Prograde", null, "Retrograde")));
        }

        [Fact]
        public void EmptyRequestDoesNothing()
        {
            Assert.Empty(SasPlan.Build(true, "Prograde", null, null));
        }

        [Fact]
        public void UnknownModeIsRejected()
        {
            ApiException failure = Assert.Throws<ApiException>(delegate { SasPlan.Build(false, "Prograde", true, "sideways"); });
            Assert.Equal("unknown_sas_mode", failure.Code);
        }

        [Fact]
        public void ModeAliasesAreAccepted()
        {
            Assert.Equal("Enable(Maneuver) group=on", Describe(SasPlan.Build(false, "Prograde", true, "node")));
            Assert.Equal("Enable(AntiTarget) group=on", Describe(SasPlan.Build(false, "Prograde", true, "anti-target")));
        }
    }
}
