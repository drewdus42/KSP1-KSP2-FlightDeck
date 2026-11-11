using System;
using System.Collections.Generic;

namespace FlightDeck.Core
{
    /// <summary>Raised by the game layer for client mistakes (unknown group, missing parameter, ...).</summary>
    public sealed class ApiException : Exception
    {
        public ApiException(string code, string message) : base(message)
        {
            Code = code;
        }

        public string Code { get; private set; }
    }

    /// <summary>
    /// The game-facing surface used by <see cref="ApiRouter"/>. Implemented by the KSP addon and
    /// by test fakes, which keeps all protocol logic testable without the game.
    /// </summary>
    public interface IGameBridge
    {
        /// <summary>True when a vessel is loaded and the flight scene is active. Safe to read from any thread.</summary>
        bool IsReady { get; }

        /// <summary>Complete state document used by the plugin's single polling request.</summary>
        Dictionary<string, object> GetSnapshot();

        /// <summary>
        /// Executes a command. Always called on the main thread by the router.
        /// Supported commands: actiongroup.get / actiongroup.set / actiongroup.toggle / actiongroup.all,
        /// throttle.get / throttle.set, sas.get / sas.set / sas.mode / sas.toggle,
        /// stage.get / stage.next, timewarp.get / timewarp.set, pause.get / pause.set / pause.toggle,
        /// resources.get, orbit.get.
        /// </summary>
        Dictionary<string, object> Execute(string command, Dictionary<string, object> arguments);
    }
}
