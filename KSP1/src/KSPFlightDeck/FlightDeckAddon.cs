using System;
using System.IO;
using System.Net.Sockets;
using FlightDeck.Core;
using UnityEngine;

namespace FlightDeck
{
    /// <summary>
    /// Owns the HTTP server for the lifetime of the game. Started in the main menu (once) so the
    /// Stream Deck knows the mod is alive before a craft is loaded.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class FlightDeckAddon : MonoBehaviour
    {
        public const string Version = "1.0.1";

        private QueueDispatcher _dispatcher;
        private HttpServer _server;
        private GameBridge _bridge;
        private ApiRouter _router;
        private bool _started;

        public static FlightDeckAddon Instance { get; private set; }

        public bool ServerRunning
        {
            get { return _server != null && _server.IsRunning; }
        }

        public int Port
        {
            get { return _server != null ? _server.Port : 0; }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            try
            {
                PluginLog.Initialize(Path.Combine(ModSettings.SettingsDirectory, "PluginData", "KSPFlightDeck.log"));
                PluginLog.Info("KSP Flight Deck " + Version + " starting.");
                StartServer();
            }
            catch (Exception ex)
            {
                PluginLog.Error("Failed to start the Flight Deck HTTP server.", ex);
            }
        }

        private void StartServer()
        {
            if (_started)
            {
                return;
            }

            var settings = ModSettings.Load();
            if (!settings.Enabled)
            {
                PluginLog.Info("API disabled in settings.cfg (enabled = false).");
                return;
            }

            _bridge = new GameBridge();
            _dispatcher = new QueueDispatcher();
            _router = new ApiRouter(_bridge, _dispatcher, Version, BuildCapabilities(), PluginLog.Warn);
            _server = new HttpServer(_router.Handle, PluginLog.Warn);

            int port = BindFirstAvailablePort(_server, settings.Port);
            if (port <= 0)
            {
                PluginLog.Error("Could not bind any port starting at " + settings.Port + ". The Stream Deck plugin will not connect.");
                return;
            }

            if (port != settings.Port)
            {
                PluginLog.Warn("Port " + settings.Port + " was busy; listening on " + port + " instead. Update the plugin connection settings to match.");
            }

            _started = true;

            if (settings.ShowStartupMessage)
            {
                try
                {
                    ScreenMessages.PostScreenMessage(
                        "Flight Deck API: http://127.0.0.1:" + port + "/api",
                        4f,
                        ScreenMessageStyle.UPPER_RIGHT);
                }
                catch (Exception)
                {
                    // Screen messages are unavailable during the very first frames.
                }
            }
        }

        private static GameCapabilities BuildCapabilities()
        {
            var capabilities = new GameCapabilities("ksp1", "Kerbal Space Program");
            capabilities.ActionGroups = new System.Collections.Generic.List<string>(ActionGroupNames.All);

            // KSP 1 has no native solar/radiator/antenna/science groups, so the mod emulates them
            // by walking part modules.
            capabilities.VirtualActionGroups = new System.Collections.Generic.List<string>
            {
                ActionGroupNames.SolarPanels, ActionGroupNames.RadiatorPanels, ActionGroupNames.Antennas, ActionGroupNames.Science
            };

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
                    PluginLog.Warn("Port " + candidate + " is unavailable: " + ex.Message);
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
                PluginLog.Error("Flight Deck update loop failed.", ex);
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
                PluginLog.Error("Failed to stop the Flight Deck HTTP server.", ex);
            }
            finally
            {
                _started = false;
                if (Instance == this)
                {
                    Instance = null;
                }

                PluginLog.Info("KSP Flight Deck stopped.");
            }
        }
    }
}
