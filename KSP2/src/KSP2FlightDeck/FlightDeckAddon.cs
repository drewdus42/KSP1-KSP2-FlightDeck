using System;
using System.IO;
using System.Net.Sockets;
using FlightDeck.Core;
using KSP.Game;
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
        public const string Version = "1.3.0";

        private static FlightDeckAddon _instance;

        private QueueDispatcher _dispatcher;
        private HttpServer _server;
        private Ksp2Bridge _bridge;
        private ApiRouter _router;
        private Ksp2Settings _settings;
        private FlightDeckPanel _panel;
        private bool _started;
        private float _nextSceneCheck;
        private bool _sceneAllowsPanel;

        public static FlightDeckAddon Instance
        {
            get { return _instance; }
        }

        internal HttpServer Server
        {
            get { return _server; }
        }

        internal Ksp2Bridge Bridge
        {
            get { return _bridge; }
        }

        public bool PanelVisible
        {
            get { return _settings != null && _settings.ShowPanel; }
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
            _panel = new FlightDeckPanel();
        }

        public override void OnInitialized()
        {
            try
            {
                Ksp2Log.Initialize(Path.Combine(Ksp2Settings.ModDirectory, "Logs", "KSPFlightDeck2.log"));
                Ksp2Log.Info("KSP Flight Deck 2 " + Version + " starting (SpaceWarp).");
                _settings = Ksp2Settings.Load();
                StartServer();
                AppBarButtons.Register();
            }
            catch (Exception ex)
            {
                Ksp2Log.Error("Failed to start the Flight Deck HTTP server.", ex);
            }
        }

        /// <summary>Shows or hides the in-game status panel and persists the choice.</summary>
        public void SetPanelVisible(bool visible)
        {
            if (_settings == null)
            {
                return;
            }

            _settings.ShowPanel = visible;
            _settings.Save();
            Ksp2Log.Info(visible ? "Status panel shown." : "Status panel hidden.");
        }

        private void StartServer()
        {
            if (_started)
            {
                return;
            }

            Ksp2Settings settings = _settings ?? Ksp2Settings.Load();
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

                UpdatePanel();
            }
            catch (Exception ex)
            {
                Ksp2Log.Error("Flight Deck update loop failed.", ex);
            }
        }

        private void UpdatePanel()
        {
            if (_panel == null || _settings == null)
            {
                return;
            }

            _panel.SetVisible(this, _settings.ShowPanel && IsPanelScene());
            _panel.Refresh(this);
        }

        /// <summary>
        /// The panel is hidden in the main menu and while loading, but stays available in the KSC,
        /// editors and flight so the app bar button always gives immediate feedback.
        /// </summary>
        private bool IsPanelScene()
        {
            if (UnityEngine.Time.unscaledTime < _nextSceneCheck)
            {
                return _sceneAllowsPanel;
            }

            _nextSceneCheck = UnityEngine.Time.unscaledTime + 0.5f;

            try
            {
                GameInstance game = GameManager.Instance == null ? null : GameManager.Instance.Game;
                if (game == null || game.GlobalGameState == null)
                {
                    _sceneAllowsPanel = false;
                    return false;
                }

                GameState state = game.GlobalGameState.GetState();
                _sceneAllowsPanel = state != GameState.Invalid &&
                                    state != GameState.WarmUpLoading &&
                                    state != GameState.MainMenu &&
                                    state != GameState.Loading;
            }
            catch (Exception)
            {
                _sceneAllowsPanel = false;
            }

            return _sceneAllowsPanel;
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
