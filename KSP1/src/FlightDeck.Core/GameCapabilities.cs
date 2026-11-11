using System;
using System.Collections.Generic;

namespace FlightDeck.Core
{
    /// <summary>
    /// Describes what the connected game can do, so the Stream Deck plugin can adapt one UI to
    /// Kerbal Space Program 1 and 2 without hard-coded assumptions.
    /// </summary>
    public sealed class GameCapabilities
    {
        public GameCapabilities(string gameId, string gameName)
        {
            GameId = gameId ?? "unknown";
            GameName = gameName ?? "Kerbal Space Program";
            ActionGroups = new List<string>(ActionGroupNames.All);
            SasModes = new List<string>(Core.SasModes.All);
            VirtualActionGroups = new List<string>();
        }

        /// <summary>Stable identifier: "ksp1" or "ksp2".</summary>
        public string GameId { get; private set; }

        /// <summary>Human readable game name for the Connection Status key.</summary>
        public string GameName { get; private set; }

        /// <summary>Action groups this game can drive.</summary>
        public IList<string> ActionGroups { get; set; }

        /// <summary>SAS modes this game accepts.</summary>
        public IList<string> SasModes { get; set; }

        /// <summary>
        /// Groups the mod synthesises itself (KSP 1 has no solar/radiator/antenna/science groups).
        /// Empty for KSP 2, which has them natively.
        /// </summary>
        public IList<string> VirtualActionGroups { get; set; }

        public bool Resources { get; set; }
        public bool DeltaV { get; set; }
        public bool Orbit { get; set; }
        public bool Staging { get; set; }
        public bool TimeWarp { get; set; }
        public bool Pause { get; set; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                { "actionGroups", new List<object>(ToStringList(ActionGroups)) },
                { "sasModes", new List<object>(ToStringList(SasModes)) },
                { "virtualActionGroups", new List<object>(ToStringList(VirtualActionGroups)) },
                { "resources", Resources },
                { "deltaV", DeltaV },
                { "orbit", Orbit },
                { "staging", Staging },
                { "timeWarp", TimeWarp },
                { "pause", Pause }
            };
        }

        private static IEnumerable<object> ToStringList(IEnumerable<string> values)
        {
            foreach (string value in values)
            {
                yield return value;
            }
        }
    }
}
