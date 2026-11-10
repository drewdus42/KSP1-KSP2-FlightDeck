using System;
using System.Collections.Generic;

namespace FlightDeck.Core
{
    /// <summary>
    /// HTTP API surface. Kept free of KSP types so the whole protocol (including the legacy
    /// Kerbal Web Program envelope the plugin was originally written against) can be tested.
    /// </summary>
    public sealed class ApiRouter
    {
        public const string ModName = "KSPFlightDeck";
        public const string ApiVersion = "1";

        private readonly IGameBridge _bridge;
        private readonly IMainThreadDispatcher _dispatcher;
        private readonly int _dispatchTimeoutMs;
        private readonly Action<string> _log;

        public ApiRouter(IGameBridge bridge, IMainThreadDispatcher dispatcher, string modVersion, Action<string> log = null, int dispatchTimeoutMs = 2000)
        {
            if (bridge == null) throw new ArgumentNullException("bridge");
            if (dispatcher == null) throw new ArgumentNullException("dispatcher");

            _bridge = bridge;
            _dispatcher = dispatcher;
            _dispatchTimeoutMs = dispatchTimeoutMs;
            _log = log ?? delegate { };
            ModVersion = modVersion ?? "0.0.0";
        }

        public string ModVersion { get; private set; }

        public HttpResponse Handle(HttpRequest request)
        {
            if (request == null)
            {
                return HttpResponse.Text("Empty request.", 400);
            }

            string path = NormalizePath(request.Path);
            string method = (request.Method ?? "GET").ToUpperInvariant();

            if (method == "OPTIONS")
            {
                return HttpResponse.Empty(204);
            }

            if (path == "/api/health")
            {
                return method == "GET" || method == "HEAD" ? Health() : MethodNotAllowed(method, path);
            }

            if (path == "/api/state")
            {
                return method == "GET" || method == "HEAD" ? State() : MethodNotAllowed(method, path);
            }

            if (path == "/api")
            {
                return method == "POST" ? ActionCall(request) : MethodNotAllowed(method, path);
            }

            if (path == "/" || path == "/api/index")
            {
                return method == "GET" ? Index() : MethodNotAllowed(method, path);
            }

            return HttpResponse.Json(new Dictionary<string, object>
            {
                { "ok", false },
                { "error", "not_found" },
                { "message", "Unknown endpoint '" + path + "'." }
            }, 404);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "/";
            }

            if (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal))
            {
                path = path.Substring(0, path.Length - 1);
            }

            return path;
        }

        private HttpResponse Index()
        {
            return HttpResponse.Json(new Dictionary<string, object>
            {
                { "ok", true },
                { "mod", ModName },
                { "version", ModVersion },
                { "apiVersion", ApiVersion },
                { "inFlight", _bridge.IsReady },
                {
                    "endpoints", new List<object>
                    {
                        "GET  /api/health",
                        "GET  /api/state",
                        "POST /api  { ID, Action, parameters }"
                    }
                },
                {
                    "actions", new List<object>
                    {
                        "getState", "getActionGroupState", "setActionGroupState", "setActionGroup", "getActionGroups",
                        "getShipThrottle", "setShipThrottle", "getSas", "setSas", "setSasMode", "toggleSas",
                        "getStaging", "stage", "getTimeWarp", "setTimeWarp", "getPaused", "setPaused", "togglePause",
                        "getResources", "getOrbit"
                    }
                }
            });
        }

        private HttpResponse Health()
        {
            return HttpResponse.Json(new Dictionary<string, object>
            {
                { "ok", true },
                { "mod", ModName },
                { "version", ModVersion },
                { "apiVersion", ApiVersion },
                { "inFlight", _bridge.IsReady }
            });
        }

        private HttpResponse State()
        {
            if (!_bridge.IsReady)
            {
                return HttpResponse.Json(new Dictionary<string, object>
                {
                    { "ok", false },
                    { "error", "not_in_flight" },
                    { "message", "No vessel is loaded. Enter the flight scene or launch a craft." },
                    { "mod", ModName },
                    { "version", ModVersion }
                });
            }

            return ExecuteOnGameThread(delegate
            {
                return HttpResponse.Json(new Dictionary<string, object>
                {
                    { "ok", true },
                    { "mod", ModName },
                    { "version", ModVersion },
                    { "data", _bridge.GetSnapshot() }
                });
            });
        }

        private HttpResponse ActionCall(HttpRequest request)
        {
            Dictionary<string, object> payload;
            try
            {
                payload = Json.AsObject(Json.Parse(request.Body ?? string.Empty));
            }
            catch (JsonException ex)
            {
                return Failure(null, null, "invalid_json", ex.Message, 400);
            }

            if (payload == null)
            {
                return Failure(null, null, "invalid_request", "The request body must be a JSON object.", 400);
            }

            string id = Json.GetString(payload, "ID", "id");
            string action = Json.GetString(payload, "Action", "action");
            if (string.IsNullOrEmpty(action))
            {
                return Failure(id, null, "missing_action", "The 'Action' property is required.", 400);
            }

            var arguments = Json.AsObject(Json.Get(payload, "parameters", "Parameters")) ?? new Dictionary<string, object>();

            if (!_bridge.IsReady)
            {
                return Failure(id, action, "not_in_flight", "No vessel is loaded. Enter the flight scene or launch a craft.", 503);
            }

            return ExecuteOnGameThread(delegate { return Dispatch(id, action, arguments); });
        }

        private HttpResponse Dispatch(string id, string action, Dictionary<string, object> arguments)
        {
            string command = action.Trim().ToLowerInvariant();
            try
            {
                switch (command)
                {
                    case "getstate":
                        return Success(id, action, _bridge.GetSnapshot());

                    case "getactiongroupstate":
                    {
                        var result = _bridge.Execute("actiongroup.get", Args("group", RequireGroup(arguments)));
                        return Success(id, action, WithStatus(result));
                    }

                    case "setactiongroupstate":
                    {
                        // Legacy Kerbal Web Program semantics: setting without a value toggles.
                        string group = RequireGroup(arguments);
                        bool? state = Json.GetBool(arguments, "State", "state", "Enabled", "enabled");
                        string cmd = state.HasValue ? "actiongroup.set" : "actiongroup.toggle";
                        var args = Args("group", group);
                        if (state.HasValue)
                        {
                            args["state"] = state.Value;
                        }

                        return Success(id, action, WithStatus(_bridge.Execute(cmd, args)));
                    }

                    case "setactiongroup":
                    {
                        string group = RequireGroup(arguments);
                        bool? state = Json.GetBool(arguments, "State", "state", "Enabled", "enabled");
                        if (!state.HasValue)
                        {
                            throw new ApiException("missing_parameter", "setActionGroup requires a boolean 'State'.");
                        }

                        var args = Args("group", group);
                        args["state"] = state.Value;
                        return Success(id, action, WithStatus(_bridge.Execute("actiongroup.set", args)));
                    }

                    case "getactiongroups":
                        return Success(id, action, _bridge.Execute("actiongroup.all", new Dictionary<string, object>()));

                    case "getshipthrottle":
                        return Success(id, action, _bridge.Execute("throttle.get", new Dictionary<string, object>()));

                    case "setshipthrottle":
                    {
                        var value = Json.GetDouble(arguments, "Throttle", "throttle", "Value", "value");
                        if (!value.HasValue)
                        {
                            throw new ApiException("missing_parameter", "setShipThrottle requires a numeric 'Throttle' between 0 and 1.");
                        }

                        var args = new Dictionary<string, object> { { "throttle", Clamp01(value.Value) } };
                        return Success(id, action, _bridge.Execute("throttle.set", args));
                    }

                    case "getsas":
                        return Success(id, action, _bridge.Execute("sas.get", new Dictionary<string, object>()));

                    case "setsas":
                    {
                        var args = new Dictionary<string, object>();
                        bool? enabled = Json.GetBool(arguments, "Enabled", "enabled");
                        string mode = Json.GetString(arguments, "Mode", "mode");
                        if (enabled.HasValue)
                        {
                            args["enabled"] = enabled.Value;
                        }

                        if (!string.IsNullOrEmpty(mode))
                        {
                            args["mode"] = RequireSasMode(mode);
                        }

                        if (args.Count == 0)
                        {
                            throw new ApiException("missing_parameter", "setSas requires 'Enabled' and/or 'Mode'.");
                        }

                        return Success(id, action, _bridge.Execute("sas.set", args));
                    }

                    case "setsasmode":
                    {
                        var args = new Dictionary<string, object> { { "mode", RequireSasMode(RequireString(arguments, "Mode", "mode", "ID", "id")) } };
                        return Success(id, action, _bridge.Execute("sas.mode", args));
                    }

                    case "togglesas":
                        return Success(id, action, _bridge.Execute("sas.toggle", new Dictionary<string, object>()));

                    case "getstaging":
                        return Success(id, action, _bridge.Execute("stage.get", new Dictionary<string, object>()));

                    case "stage":
                        return Success(id, action, _bridge.Execute("stage.next", new Dictionary<string, object>()));

                    case "gettimewarp":
                        return Success(id, action, _bridge.Execute("timewarp.get", new Dictionary<string, object>()));

                    case "settimewarp":
                    {
                        int? index = Json.GetInt(arguments, "Index", "index", "Value", "value");
                        if (!index.HasValue)
                        {
                            throw new ApiException("missing_parameter", "setTimeWarp requires an integer 'Index'.");
                        }

                        var args = new Dictionary<string, object> { { "index", index.Value } };
                        return Success(id, action, _bridge.Execute("timewarp.set", args));
                    }

                    case "getpaused":
                        return Success(id, action, _bridge.Execute("pause.get", new Dictionary<string, object>()));

                    case "setpaused":
                    {
                        bool? paused = Json.GetBool(arguments, "Paused", "paused", "Value", "value");
                        if (!paused.HasValue)
                        {
                            throw new ApiException("missing_parameter", "setPaused requires a boolean 'Paused'.");
                        }

                        var args = new Dictionary<string, object> { { "paused", paused.Value } };
                        return Success(id, action, _bridge.Execute("pause.set", args));
                    }

                    case "togglepause":
                        return Success(id, action, _bridge.Execute("pause.toggle", new Dictionary<string, object>()));

                    case "getresources":
                        return Success(id, action, _bridge.Execute("resources.get", new Dictionary<string, object>()));

                    case "getorbit":
                        return Success(id, action, _bridge.Execute("orbit.get", new Dictionary<string, object>()));

                    default:
                        return Failure(id, action, "unknown_action", "Unknown action '" + action + "'.", 400);
                }
            }
            catch (ApiException ex)
            {
                return Failure(id, action, ex.Code, ex.Message, 400);
            }
        }

        private HttpResponse ExecuteOnGameThread(Func<HttpResponse> work)
        {
            try
            {
                return _dispatcher.Invoke(work, _dispatchTimeoutMs);
            }
            catch (TimeoutException ex)
            {
                _log("Dispatch timeout: " + ex.Message);
                return HttpResponse.Json(new Dictionary<string, object>
                {
                    { "ok", false },
                    { "error", "game_busy" },
                    { "message", ex.Message }
                }, 503);
            }
            catch (ApiException ex)
            {
                return HttpResponse.Json(new Dictionary<string, object>
                {
                    { "ok", false },
                    { "error", ex.Code },
                    { "message", ex.Message }
                }, 400);
            }
            catch (Exception ex)
            {
                _log("Dispatch failed: " + ex);
                return HttpResponse.Json(new Dictionary<string, object>
                {
                    { "ok", false },
                    { "error", "internal_error" },
                    { "message", ex.Message }
                }, 500);
            }
        }

        private HttpResponse MethodNotAllowed(string method, string path)
        {
            return HttpResponse.Json(new Dictionary<string, object>
            {
                { "ok", false },
                { "error", "method_not_allowed" },
                { "message", method + " is not supported for " + path + "." }
            }, 405);
        }

        private static HttpResponse Success(string id, string action, Dictionary<string, object> data)
        {
            return HttpResponse.Json(new Dictionary<string, object>
            {
                { "ID", id ?? string.Empty },
                { "Action", action ?? string.Empty },
                { "Success", true },
                { "Data", data ?? new Dictionary<string, object>() }
            });
        }

        private static HttpResponse Failure(string id, string action, string code, string message, int statusCode)
        {
            var body = new Dictionary<string, object>
            {
                { "Success", false },
                { "ok", false },
                { "Error", code },
                { "Message", message }
            };

            if (id != null)
            {
                body["ID"] = id;
            }

            if (action != null)
            {
                body["Action"] = action;
            }

            return HttpResponse.Json(body, statusCode);
        }

        private static Dictionary<string, object> WithStatus(Dictionary<string, object> result)
        {
            var data = result ?? new Dictionary<string, object>();
            object state;
            if (data.TryGetValue("State", out state))
            {
                data["Status"] = state;
            }

            return data;
        }

        private static Dictionary<string, object> Args(string key, object value)
        {
            return new Dictionary<string, object> { { key, value } };
        }

        private static string RequireString(Dictionary<string, object> arguments, params string[] keys)
        {
            string value = Json.GetString(arguments, keys);
            if (string.IsNullOrEmpty(value))
            {
                throw new ApiException("missing_parameter", "The '" + keys[0] + "' parameter is required.");
            }

            return value;
        }

        private static string RequireGroup(Dictionary<string, object> arguments)
        {
            string raw = RequireString(arguments, "ID", "id", "Group", "group", "Name", "name");
            string canonical = ActionGroupNames.Normalize(raw);
            if (canonical == null)
            {
                throw new ApiException("unknown_action_group", "Unknown action group '" + raw + "'.");
            }

            return canonical;
        }

        private static string RequireSasMode(string raw)
        {
            string canonical = SasModes.Normalize(raw);
            if (canonical == null)
            {
                throw new ApiException("unknown_sas_mode", "Unknown SAS mode '" + raw + "'.");
            }

            return canonical;
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value))
            {
                return 0d;
            }

            if (value < 0d)
            {
                return 0d;
            }

            return value > 1d ? 1d : value;
        }
    }
}
