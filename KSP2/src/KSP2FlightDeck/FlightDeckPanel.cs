using System;
using System.Collections.Generic;
using System.Globalization;
using FlightDeck.Core;
using UitkForKsp2.API;
using UnityEngine;
using UnityEngine.UIElements;

namespace FlightDeck.Ksp2
{
    /// <summary>
    /// In-flight status panel: Stream Deck connectivity plus the telemetry the deck itself polls for.
    /// Built with the UITK SDK so it uses the game's theme, and opened from the app bar.
    /// </summary>
    internal sealed class FlightDeckPanel
    {
        private const float Width = 450f;
        private const float RefreshSeconds = 0.25f;
        private const double ConnectedThresholdSeconds = 2.5;

        private static readonly Color ConnectedColor = new Color(0.35f, 0.85f, 0.45f, 1f);
        private static readonly Color WaitingColor = new Color(0.95f, 0.75f, 0.25f, 1f);
        private static readonly Color OfflineColor = new Color(0.90f, 0.35f, 0.35f, 1f);
        private static readonly Color MutedColor = new Color(1f, 1f, 1f, 0.55f);

        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _statusDot;
        private Label _statusLabel;
        private Label _serverLabel;
        private Label _vesselLabel;
        private Label _flightLabel;
        private Label _navigationLabel;
        private Label _orbitLabel;
        private Label _deltaVLabel;
        private Label _resourcesLabel;
        private Label _actionGroupsLabel;

        private bool _created;
        private float _nextRefresh;
        private DateTime _rateWindowStart = DateTime.UtcNow;
        private int _rateWindowCount;
        private double _requestRate;

        public bool IsVisible { get; private set; }

        public void SetVisible(FlightDeckAddon addon, bool visible)
        {
            try
            {
                if (!_created)
                {
                    if (!visible)
                    {
                        return;
                    }

                    Create(addon);
                }

                IsVisible = visible;
                _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

                if (visible)
                {
                    _nextRefresh = 0f;
                }
            }
            catch (Exception ex)
            {
                Ksp2Log.Error("Could not create the Flight Deck panel.", ex);
                _created = false;
            }
        }

        /// <summary>Called from the addon's Update loop; throttled to 4 Hz.</summary>
        public void Refresh(FlightDeckAddon addon)
        {
            if (!_created || !IsVisible || Time.unscaledTime < _nextRefresh)
            {
                return;
            }

            _nextRefresh = Time.unscaledTime + RefreshSeconds;

            try
            {
                RefreshConnection(addon);
                RefreshTelemetry(addon);
            }
            catch (Exception ex)
            {
                Ksp2Log.Error("Flight Deck panel refresh failed.", ex);
            }
        }

        private void RefreshConnection(FlightDeckAddon addon)
        {
            HttpServer server = addon.Server;
            bool running = server != null && server.IsRunning;

            double age = double.MaxValue;
            if (running && server.LastRequestUtc != DateTime.MinValue)
            {
                age = (DateTime.UtcNow - server.LastRequestUtc).TotalSeconds;
            }

            if (!running)
            {
                SetStatus(OfflineColor, "Server offline");
                _serverLabel.text = "Enable the API in settings.cfg (enabled = true) and restart the game.";
                return;
            }

            if (age <= ConnectedThresholdSeconds)
            {
                SetStatus(ConnectedColor, "Connected — last poll " + FormatAge(age));
            }
            else
            {
                SetStatus(WaitingColor, "Waiting for Stream Deck");
            }

            UpdateRequestRate(server);

            var text = "127.0.0.1:" + server.Port.ToString(CultureInfo.InvariantCulture) +
                       "   ·   " + server.RequestCount.ToString("N0", CultureInfo.InvariantCulture) + " requests";

            if (_requestRate > 0.05)
            {
                text += "   ·   " + _requestRate.ToString("0.0", CultureInfo.InvariantCulture) + "/s";
            }

            _serverLabel.text = text;
        }

        private void UpdateRequestRate(HttpServer server)
        {
            int count = server.RequestCount;
            DateTime now = DateTime.UtcNow;
            double elapsed = (now - _rateWindowStart).TotalSeconds;

            if (elapsed < 1.0)
            {
                return;
            }

            int delta = count - _rateWindowCount;
            if (delta < 0)
            {
                delta = 0;
            }

            double instant = delta / elapsed;
            _requestRate = _requestRate <= 0.0 ? instant : (_requestRate * 0.6) + (instant * 0.4);
            _rateWindowStart = now;
            _rateWindowCount = count;
        }

        private void RefreshTelemetry(FlightDeckAddon addon)
        {
            Dictionary<string, object> snapshot = null;
            if (addon.Bridge != null && addon.Bridge.IsReady)
            {
                try
                {
                    snapshot = addon.Bridge.GetSnapshot();
                }
                catch (Exception)
                {
                    snapshot = null;
                }
            }

            if (snapshot == null)
            {
                _vesselLabel.text = "No vessel loaded";
                _flightLabel.text = "Launch a vessel or enter flight to see telemetry.";
                _navigationLabel.text = string.Empty;
                _orbitLabel.text = string.Empty;
                _deltaVLabel.text = string.Empty;
                _resourcesLabel.text = string.Empty;
                _actionGroupsLabel.text = string.Empty;
                return;
            }

            Dictionary<string, object> vessel = Sub(snapshot, "vessel");
            Dictionary<string, object> sas = Sub(snapshot, "sas");
            Dictionary<string, object> staging = Sub(snapshot, "staging");
            Dictionary<string, object> timeWarp = Sub(snapshot, "timeWarp");
            Dictionary<string, object> orbit = Sub(snapshot, "orbit");
            Dictionary<string, object> deltaV = Sub(snapshot, "deltaV");
            Dictionary<string, object> resources = Sub(snapshot, "resources");
            Dictionary<string, object> actionGroups = Sub(snapshot, "actionGroups");

            string vesselName = String(vessel, "name");
            string situation = String(vessel, "situation");
            _vesselLabel.text = string.IsNullOrEmpty(vesselName)
                ? "Vessel"
                : situation.Length > 0 ? vesselName + " — " + situation : vesselName;

            int throttlePercent = (int)Math.Round(Number(snapshot, "throttle") * 100.0);
            string sasText = sas != null && Bool(sas, "Enabled")
                ? "SAS " + String(sas, "Mode")
                : "SAS off";

            string stageText = "Stage " + Number(staging, "CurrentStage").ToString("0", CultureInfo.InvariantCulture) +
                               " / " + Number(staging, "StageCount").ToString("0", CultureInfo.InvariantCulture);
            if (staging != null && Bool(staging, "Prelaunch"))
            {
                stageText = "Prelaunch";
            }

            _flightLabel.text = "Throttle " + throttlePercent.ToString(CultureInfo.InvariantCulture) + "%" +
                                "   ·   " + sasText +
                                "   ·   " + stageText;

            string altitudeText = "Alt " + Distance(Number(orbit, "altitude")) +
                                  "   ·   Radar " + Distance(Number(orbit, "radarAltitude")) +
                                  "   ·   Surf " + Speed(Number(orbit, "surfaceSpeed")) +
                                  "   ·   Vert " + Speed(Number(orbit, "verticalSpeed"));
            _navigationLabel.text = altitudeText;

            if (orbit != null && Bool(orbit, "available"))
            {
                _orbitLabel.text = "Ap " + Distance(Number(orbit, "apoapsis")) +
                                   "   ·   Pe " + Distance(Number(orbit, "periapsis")) +
                                   "   ·   Inc " + Number(orbit, "inclination").ToString("0.0", CultureInfo.InvariantCulture) + "°";
            }
            else if (vessel != null && Bool(vessel, "splashed"))
            {
                _orbitLabel.text = WithBody("Splashed", vessel);
            }
            else if (vessel != null && Bool(vessel, "landed"))
            {
                _orbitLabel.text = WithBody("Landed", vessel);
            }
            else
            {
                _orbitLabel.text = "Suborbital";
            }

            if (deltaV != null && Bool(deltaV, "available"))
            {
                _deltaVLabel.text = "ΔV " + Number(deltaV, "vacuum").ToString("N0", CultureInfo.InvariantCulture) + " m/s vac" +
                                    "   ·   burn " + Duration(Number(deltaV, "burnTime"));
            }
            else
            {
                _deltaVLabel.text = "ΔV unavailable";
            }

            if (timeWarp != null)
            {
                bool paused = Bool(timeWarp, "Paused");
                double rate = Number(timeWarp, "Rate");
                string warpText = paused ? "Paused" : "Warp " + rate.ToString("0.#", CultureInfo.InvariantCulture) + "x";
                _deltaVLabel.text += "   ·   " + warpText;
            }

            _resourcesLabel.text = FormatResources(resources);
            _actionGroupsLabel.text = FormatActionGroups(actionGroups);
        }

        private static string FormatResources(Dictionary<string, object> resources)
        {
            if (resources == null || resources.Count == 0)
            {
                return "No resource data";
            }

            var lines = new List<string>();
            foreach (KeyValuePair<string, object> entry in resources)
            {
                var data = entry.Value as Dictionary<string, object>;
                if (data == null)
                {
                    continue;
                }

                double amount = Number(data, "amount");
                double max = Number(data, "max");
                double fraction = Number(data, "fraction");

                if (max <= 0d)
                {
                    continue;
                }

                lines.Add(entry.Key + "  " +
                          amount.ToString("N0", CultureInfo.InvariantCulture) + " / " +
                          max.ToString("N0", CultureInfo.InvariantCulture) +
                          "  ·  " + (fraction * 100f).ToString("0", CultureInfo.InvariantCulture) + "%");

                if (lines.Count >= 5)
                {
                    break;
                }
            }

            return lines.Count == 0 ? "No resource data" : string.Join("\n", lines.ToArray());
        }

        private static string FormatActionGroups(Dictionary<string, object> actionGroups)
        {
            if (actionGroups == null)
            {
                return string.Empty;
            }

            var active = new List<string>();
            foreach (KeyValuePair<string, object> entry in actionGroups)
            {
                if (entry.Value is bool && (bool)entry.Value)
                {
                    active.Add(entry.Key);
                }
            }

            return active.Count == 0 ? "Active groups: none" : "Active: " + string.Join(" · ", active.ToArray());
        }

        private void SetStatus(Color color, string text)
        {
            _statusDot.style.backgroundColor = color;
            _statusLabel.text = text;
        }

        private void Create(FlightDeckAddon addon)
        {
            var options = WindowOptions.Default;
            options.WindowId = "KSPFlightDeck2.StatusPanel";
            options.BringToFrontOnPointerDown = true;

            _root = Element.VisualElement("flight-deck-window", "flight-deck-window", null);
            _root.style.width = Width;
            _root.style.paddingLeft = 16;
            _root.style.paddingRight = 16;
            _root.style.paddingTop = 12;
            _root.style.paddingBottom = 14;
            _root.style.backgroundColor = new Color(0.075f, 0.098f, 0.137f, 0.96f);
            _root.style.borderTopLeftRadius = 6;
            _root.style.borderTopRightRadius = 6;
            _root.style.borderBottomLeftRadius = 6;
            _root.style.borderBottomRightRadius = 6;
            _root.style.borderLeftWidth = 1;
            _root.style.borderRightWidth = 1;
            _root.style.borderTopWidth = 1;
            _root.style.borderBottomWidth = 1;
            _root.style.borderLeftColor = new Color(1f, 1f, 1f, 0.10f);
            _root.style.borderRightColor = new Color(1f, 1f, 1f, 0.10f);
            _root.style.borderTopColor = new Color(1f, 1f, 1f, 0.10f);
            _root.style.borderBottomColor = new Color(1f, 1f, 1f, 0.10f);

            var titleRow = Element.VisualElement("flight-deck-title-row", null, null);
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.justifyContent = Justify.SpaceBetween;
            titleRow.style.marginBottom = 8;

            var title = Element.Label("flight-deck-title", "KSP Flight Deck", "flight-deck-title", null);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleRow.Add(title);

            var version = Element.Label("flight-deck-version", "v" + FlightDeckAddon.Version, null, null);
            version.style.color = MutedColor;
            titleRow.Add(version);
            _root.Add(titleRow);

            title.MakeDraggable(_root, true);

            AddSection("STREAM DECK");

            var statusRow = Element.VisualElement("flight-deck-status-row", null, null);
            statusRow.style.flexDirection = FlexDirection.Row;
            statusRow.style.alignItems = Align.Center;
            statusRow.style.marginBottom = 4;

            _statusDot = Element.VisualElement("flight-deck-status-dot", null, null);
            _statusDot.style.width = 9;
            _statusDot.style.height = 9;
            _statusDot.style.borderTopLeftRadius = 5;
            _statusDot.style.borderTopRightRadius = 5;
            _statusDot.style.borderBottomLeftRadius = 5;
            _statusDot.style.borderBottomRightRadius = 5;
            _statusDot.style.marginRight = 8;
            statusRow.Add(_statusDot);

            _statusLabel = Element.Label("flight-deck-status", "Starting…", null, null);
            statusRow.Add(_statusLabel);
            _root.Add(statusRow);

            _serverLabel = Element.Label("flight-deck-server", string.Empty, null, null);
            _serverLabel.style.color = MutedColor;
            _serverLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _serverLabel.style.marginBottom = 12;
            _root.Add(_serverLabel);

            AddSection("FLIGHT");
            _vesselLabel = AddLine(string.Empty, false);
            _flightLabel = AddLine(string.Empty, true);
            _navigationLabel = AddLine(string.Empty, true);
            _orbitLabel = AddLine(string.Empty, true);
            _deltaVLabel = AddLine(string.Empty, true);

            AddSection("RESOURCES");
            _resourcesLabel = AddLine(string.Empty, true);
            _actionGroupsLabel = AddLine(string.Empty, true);

            var hideButton = Element.Button("flight-deck-hide", "Hide panel", null, null);
            hideButton.style.marginTop = 10;
            hideButton.clicked += () => addon.SetPanelVisible(false);
            _root.Add(hideButton);

            _document = Window.Create(options, _root);
            _created = true;
        }

        private void AddSection(string title)
        {
            var label = Element.Label(null, title, null, null);
            label.style.color = MutedColor;
            label.style.marginTop = 8;
            label.style.marginBottom = 4;
            _root.Add(label);
        }

        private Label AddLine(string text, bool muted)
        {
            var label = Element.Label(null, text, null, null);
            if (muted)
            {
                label.style.color = new Color(1f, 1f, 1f, 0.85f);
            }

            label.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(label);
            return label;
        }

        private static string WithBody(string prefix, Dictionary<string, object> vessel)
        {
            string body = String(vessel, "body");
            return body.Length == 0 ? prefix : prefix + " · " + body;
        }

        private static Dictionary<string, object> Sub(Dictionary<string, object> parent, string key)
        {
            object raw;
            if (parent != null && parent.TryGetValue(key, out raw))
            {
                return raw as Dictionary<string, object>;
            }

            return null;
        }

        private static double Number(Dictionary<string, object> data, string key)
        {
            object raw;
            if (data == null || !data.TryGetValue(key, out raw) || raw == null)
            {
                return 0d;
            }

            try
            {
                return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0d;
            }
        }

        private static string String(Dictionary<string, object> data, string key)
        {
            object raw;
            if (data != null && data.TryGetValue(key, out raw))
            {
                return raw as string ?? string.Empty;
            }

            return string.Empty;
        }

        private static bool Bool(Dictionary<string, object> data, string key)
        {
            object raw;
            return data != null && data.TryGetValue(key, out raw) && raw is bool && (bool)raw;
        }

        private static string FormatAge(double seconds)
        {
            if (seconds < 1.0)
            {
                return (seconds * 1000.0).ToString("0", CultureInfo.InvariantCulture) + " ms ago";
            }

            return seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s ago";
        }

        private static string Distance(double meters)
        {
            double abs = Math.Abs(meters);
            if (abs >= 100000.0)
            {
                return (meters / 1000.0).ToString("N0", CultureInfo.InvariantCulture) + " km";
            }

            if (abs >= 1000.0)
            {
                return (meters / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " km";
            }

            return meters.ToString("N0", CultureInfo.InvariantCulture) + " m";
        }

        private static string Speed(double metersPerSecond)
        {
            return metersPerSecond.ToString("N0", CultureInfo.InvariantCulture) + " m/s";
        }

        private static string Duration(double seconds)
        {
            if (seconds <= 0.0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                return "--";
            }

            var span = TimeSpan.FromSeconds(seconds);
            return span.TotalHours >= 1.0
                ? span.TotalHours.ToString("0", CultureInfo.InvariantCulture) + "h " + span.Minutes.ToString("00", CultureInfo.InvariantCulture) + "m"
                : span.Minutes.ToString("0", CultureInfo.InvariantCulture) + "m " + span.Seconds.ToString("00", CultureInfo.InvariantCulture) + "s";
        }
    }
}
