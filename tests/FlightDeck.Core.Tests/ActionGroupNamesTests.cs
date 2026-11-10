using System.Collections.Generic;
using System.Linq;
using FlightDeck.Core;
using Xunit;

namespace FlightDeck.Core.Tests
{
    public class ActionGroupNamesTests
    {
        [Fact]
        public void AllGroupsAreExposed()
        {
            Assert.Contains("Gear", ActionGroupNames.All);
            Assert.Contains("SAS", ActionGroupNames.All);
            Assert.Contains("SolarPanels", ActionGroupNames.All);
            Assert.Contains("RadiatorPanels", ActionGroupNames.All);
            Assert.Contains("Antennas", ActionGroupNames.All);
            Assert.Contains("Science", ActionGroupNames.All);
            Assert.Contains("Custom10", ActionGroupNames.All);
            Assert.Equal(20, ActionGroupNames.All.Length);
        }

        [Theory]
        [InlineData("gear", "Gear")]
        [InlineData("LIGHT", "Lights")]
        [InlineData("Lights", "Lights")]
        [InlineData("custom1", "Custom01")]
        [InlineData("Custom 1", null)]
        [InlineData("Custom01", "Custom01")]
        [InlineData("solar panel", null)]
        [InlineData("solarpanel", "SolarPanels")]
        [InlineData("radiators", "RadiatorPanels")]
        [InlineData("antennae", "Antennas")]
        [InlineData("experiments", "Science")]
        [InlineData("Custom11", null)]
        [InlineData("", null)]
        public void NormalizesAliases(string input, string expected)
        {
            Assert.Equal(expected, ActionGroupNames.Normalize(input));
        }

        [Fact]
        public void DistinguishesNativeGroups()
        {
            Assert.True(ActionGroupNames.IsNativeKspGroup("Gear"));
            Assert.True(ActionGroupNames.IsNativeKspGroup("Custom07"));
            Assert.False(ActionGroupNames.IsNativeKspGroup("Science"));
            Assert.False(ActionGroupNames.IsNativeKspGroup("Antennas"));
        }

        [Fact]
        public void SasModesMatchKspEnumNames()
        {
            Assert.Equal(
                new[]
                {
                    "StabilityAssist", "Prograde", "Retrograde", "Normal", "Antinormal",
                    "RadialIn", "RadialOut", "Target", "AntiTarget", "Maneuver"
                },
                SasModes.All);

            Assert.Equal("Maneuver", SasModes.Normalize("node"));
            Assert.Equal("AntiTarget", SasModes.Normalize("anti-target"));
            Assert.Equal("RadialIn", SasModes.Normalize("radial-in"));
            Assert.Null(SasModes.Normalize("sideways"));
        }

        [Fact]
        public void EverySasModeRoundTrips()
        {
            Assert.All(SasModes.All, mode => Assert.Equal(mode, SasModes.Normalize(mode)));
        }
    }
}
