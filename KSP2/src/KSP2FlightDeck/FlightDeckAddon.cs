using System;
using System.IO;
using System.Net.Sockets;
using FlightDeck.Core;
using SpaceWarp2.API.Mods;

namespace FlightDeck.Ksp2
{
    /// <summary>
    /// SpaceWarp entry point. SpaceWarp instantiates this MonoBehaviour from the mod's main
    /// assembly (see swinfo.json) and calls the lifecycle hooks; the HTTP server then lives for
    /// the whole session, so the Stream Deck knows the mod is alive before a vessel exists.
    /// </summary>
    public sealed class FlightDeckAddon : MonoBehaviourMod
    {
        public const string Version = "1.2.0";

        private static FlightDeckAddon _instance;

        private QueueDispatcher _dispatcher;
        private HttpServer _server;
        private Ksp2Bridge _bridge;
        private ApiRouter _router;
        private bool _started;

        public static FlightDeckAddon Instance
        {
            get { return _instance; }
        }

        public bool ServerRunning
        {
            get { return _server != null && _server.IsRunning; }
        }

        public int Port
        {
            get { return _server != null ? _server.Port : 0; }
        }

        public override void OnPreInitialized()
        {
            _instance = this;
        }

        public override void OnInitialized()
        {
            try
            {
                Ksp2Log.Initialize(Path.Combine(Ksp2Settings.ModDirectory, "Logs", "KSPFlightDeck2.log"));
                Ksp2Log.Info("KSP Flight Deck 2 " + Version + " starting (SpaceWarp).");
                StartServer();
            }
            catch (Exception ex)
            {
                Ksp2Log.Error("Failed to start the Flight Deck HTTP server.", ex);
            }
        }

        private void StartServer()
        {
            if (_started)
            {
                return;
            }

            Ksp2Settings settings = Ksp2Settings.Load();
            if (!settings.Enabled)
            {
                Ksp2Log.Info("API disabled in settings.cfg (enabled = false).");
                return;
            }

            _bridge = new Ksp2Bridge();
            _dispatcher = new QueueDispatcher();
            _router = new ApiRouter(_bridge, _dispatcher, Version, BuildCapabilities(), Ksp2Log.Warn);
            _server = new HttpServer(_router.Handle, Ksp2Log.Warn);

            int port = BindFirstAvailablePort(_server, settings.Port);
            if (port <= 0)
            {
                Ksp2Log.Error("Could not bind any port starting at " + settings.Port + ". The Stream Deck plugin will not connect.");
                return;
            }

            if (port != settings.Port)
            {
                Ksp2Log.Warn("Port " + settings.Port + " was busy; listening on " + port + " instead. Update the plugin connection settings to match.");
            }

            _started = true;

            if (settings.ShowStartupMessage)
            {
                try
                {
                    UnityEngine.Debug.Log("[KSPFlightDeck2] API ready on http://127.0.0.1:" + port + "/api");
                }
                catch (Exception)
                {
                    // Early-load logging is best effort.
                }
            }
        }

        private static GameCapabilities BuildCapabilities()
        {
            var capabilities = new GameCapabilities("ksp2", "Kerbal Space Program 2");
            capabilities.ActionGroups = new System.Collections.Generic.List<string>(ActionGroupNames.WithResourceScan);

            // KSP 2 reports solar/radiator/science natively, so nothing is emulated.
            capabilities.VirtualActionGroups = new System.Collections.Generic.List<string>();

            capabilities.Resources = true;
            capabilities.DeltaV = true;
            capabilities.Orbit = true;
            capabilities.Staging = true;
            capabilities.TimeWarp = true;
            capabilities.Pause = true;
            return capabilities;
        }

        private static int BindFirstAvailablePort(HttpServer server, int preferredPort)
        {
            for (int offset = 0; offset < 10; offset++)
            {
                int candidate = preferredPort + offset;
                if (candidate > 65535)
                {
                    break;
                }

                try
                {
                    server.Start(candidate);
                    return candidate;
                }
                catch (SocketException ex)
                {
                    Ksp2Log.Warn("Port " + candidate + " is unavailable: " + ex.Message);
                }
            }

            return 0;
        }

        private void Update()
        {
            try
            {
                if (_bridge != null)
                {
                    _bridge.UpdateCachedState();
                }

                if (_dispatcher != null)
                {
                    _dispatcher.Pump();
                }
            }
            catch (Exception ex)
            {
                Ksp2Log.Error("Flight Deck update loop failed.", ex);
            }
        }

        private void OnDestroy()
        {
            try
            {
                if (_bridge != null)
                {
                    _bridge.MarkNotReady();
                }

                if (_server != null)
                {
                    _server.Dispose();
                    _server = null;
                }
            }
            catch (Exception ex)
            {
                Ksp2Log.Error("Failed to stop the Flight Deck HTTP server.", ex);
            }
            finally
            {
                _started = false;
                if (_instance == this)
                {
                    _instance = null;
                }

                Ksp2Log.Info("KSP Flight Deck 2 stopped.");
            }
        }
    }
}
