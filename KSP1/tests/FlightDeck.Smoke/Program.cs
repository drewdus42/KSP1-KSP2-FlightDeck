using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using FlightDeck.Core;

namespace FlightDeck.Smoke
{
    /// <summary>
    /// Verifies the built mod on the same runtime family KSP uses: it loads KSPFlightDeck.dll
    /// next to the real KSP assemblies, checks the addon contract, and drives the real
    /// ApiRouter/HttpServer over a socket.
    /// </summary>
    internal static class Program
    {
        private static string _managedDir;
        private static int _failures;

        private static int Main()
        {
            string kspDir = Environment.GetEnvironmentVariable("KSPDIR");
            if (string.IsNullOrEmpty(kspDir))
            {
                kspDir = @"C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program";
            }

            _managedDir = Path.Combine(kspDir, "KSP_x64_Data", "Managed");
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromKsp;

            if (!Check("KSP managed folder exists", Directory.Exists(_managedDir)))
            {
                return 1;
            }

            CheckAddonContract();
            CheckBridgeType();
            CheckHttpRoundTrip();

            Console.WriteLine();
            Console.WriteLine(_failures == 0 ? "SMOKE TEST PASSED" : "SMOKE TEST FAILED (" + _failures + " checks)");
            return _failures == 0 ? 0 : 1;
        }

        private static void CheckAddonContract()
        {
            Assembly mod = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KSPFlightDeck.dll"));

            Type addon = mod.GetType("FlightDeck.FlightDeckAddon", false);
            if (!Check("FlightDeck.FlightDeckAddon exists", addon != null))
            {
                return;
            }

            Check("Addon derives from UnityEngine.MonoBehaviour", typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(addon));
            Check("Addon is public", addon.IsPublic);

            object[] attributes = addon.GetCustomAttributes(false);
            object kspAddon = attributes.FirstOrDefault(delegate(object attribute)
            {
                return attribute.GetType().Name == "KSPAddon";
            });

            Check("Addon carries a KSPAddon attribute", kspAddon != null);

            if (kspAddon != null)
            {
                FieldInfo startup = kspAddon.GetType().GetField("startup");
                FieldInfo once = kspAddon.GetType().GetField("once");
                Check("KSPAddon startup is MainMenu", startup != null && startup.GetValue(kspAddon).ToString() == "MainMenu");
                Check("KSPAddon is a once addon (survives scene changes)", once != null && (bool)once.GetValue(kspAddon));
            }

            FieldInfo version = addon.GetField("Version");
            Check("Version constant is 1.0.2", version != null && (string)version.GetValue(null) == "1.0.2");
        }

        private static void CheckBridgeType()
        {
            Assembly mod = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KSPFlightDeck.dll"));
            Type bridgeType = mod.GetType("FlightDeck.GameBridge", false);

            if (!Check("FlightDeck.GameBridge exists", bridgeType != null))
            {
                return;
            }

            Check("GameBridge implements IGameBridge", typeof(IGameBridge).IsAssignableFrom(bridgeType));

            try
            {
                var bridge = (IGameBridge)Activator.CreateInstance(bridgeType, true);
                Check("GameBridge can be constructed", bridge != null);
                Check("GameBridge reports not ready outside flight", bridge != null && !bridge.IsReady);
            }
            catch (Exception ex)
            {
                Check("GameBridge can be constructed (" + ex.GetType().Name + ": " + ex.Message + ")", false);
            }
        }

        private static void CheckHttpRoundTrip()
        {
            var bridge = new SmokeBridge();
            var dispatcher = new QueueDispatcher();
            var router = new ApiRouter(bridge, dispatcher, "smoke", new GameCapabilities("ksp1", "Kerbal Space Program"));

            // Stand-in for the addon's Update() loop, which pumps game-thread work every frame.
            bool running = true;
            var pump = new System.Threading.Thread(delegate()
            {
                while (running)
                {
                    dispatcher.Pump();
                    System.Threading.Thread.Sleep(2);
                }
            });
            pump.IsBackground = true;
            pump.Start();

            try
            {
                using (var server = new HttpServer(router.Handle))
                {
                    try
                    {
                        server.Start(0);
                    }
                    catch (Exception ex)
                    {
                        Check("HTTP server starts (" + ex.Message + ")", false);
                        return;
                    }

                    Check("HTTP server binds a port", server.Port > 0);

                    string health = Get("http://127.0.0.1:" + server.Port + "/api/health");
                    Check("GET /api/health returns ok", health.Contains("\"ok\":true"));

                    string state = Get("http://127.0.0.1:" + server.Port + "/api/state");
                    Check("GET /api/state returns the snapshot", state.Contains("\"throttle\":0.5"));

                    string legacy = Post(
                        "http://127.0.0.1:" + server.Port + "/api",
                        "{\"ID\":\"sm1\",\"Action\":\"setActionGroupState\",\"parameters\":{\"ID\":\"Gear\"}}");
                    Check("Legacy POST returns Success", legacy.Contains("\"Success\":true"));
                    Check("Legacy POST returns Status", legacy.Contains("\"Status\":true"));
                }
            }
            finally
            {
                running = false;
                pump.Join(2000);
            }
        }

        private static string Get(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = 5000;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                return reader.ReadToEnd();
            }
        }

        private static string Post(string url, string body)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Timeout = 5000;

            byte[] payload = Encoding.UTF8.GetBytes(body);
            request.ContentLength = payload.Length;
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(payload, 0, payload.Length);
            }

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                return reader.ReadToEnd();
            }
        }

        private static Assembly ResolveFromKsp(object sender, ResolveEventArgs args)
        {
            string simpleName = new AssemblyName(args.Name).Name;
            string candidate = Path.Combine(_managedDir, simpleName + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }

        private static bool Check(string description, bool condition)
        {
            Console.WriteLine((condition ? "  PASS  " : "  FAIL  ") + description);
            if (!condition)
            {
                _failures++;
            }

            return condition;
        }

        private sealed class SmokeBridge : IGameBridge
        {
            public bool IsReady
            {
                get { return true; }
            }

            public Dictionary<string, object> GetSnapshot()
            {
                return new Dictionary<string, object>
                {
                    { "throttle", 0.5d },
                    { "vessel", new Dictionary<string, object> { { "name", "Smoke" } } }
                };
            }

            public Dictionary<string, object> Execute(string command, Dictionary<string, object> arguments)
            {
                if (command == "actiongroup.toggle")
                {
                    return new Dictionary<string, object> { { "Group", "Gear" }, { "State", true } };
                }

                return new Dictionary<string, object>();
            }
        }
    }
}
