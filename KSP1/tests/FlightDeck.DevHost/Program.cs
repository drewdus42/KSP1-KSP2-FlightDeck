using System;
using System.Collections.Generic;
using FlightDeck.Core;

namespace FlightDeck.DevHost
{
    /// <summary>
    /// Hosts the real API with a simulated vessel. Handy for building the Stream Deck UI
    /// (or any other client) without launching KSP:
    ///     dotnet run --project tests/FlightDeck.DevHost -- --port 8080
    /// Press 1-5 at runtime to simulate main menu, prelaunch, orbit, pause and offline states.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            int port = 8080;
            int portIndex = Array.IndexOf(args, "--port");
            if (portIndex >= 0 && portIndex + 1 < args.Length)
            {
                int.TryParse(args[portIndex + 1], out port);
            }

            var bridge = new DevBridge();
            var dispatcher = new QueueDispatcher();
            var router = new ApiRouter(bridge, dispatcher, "dev", new GameCapabilities("ksp1", "Kerbal Space Program (dev host)"));
            using var server = new HttpServer(router.Handle, message => Console.WriteLine("  " + message));

            try
            {
                server.Start(port);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not bind port " + port + ": " + ex.Message);
                return 1;
            }

            Console.WriteLine("KSP Flight Deck dev host -> http://127.0.0.1:" + server.Port + "/api");
            Console.WriteLine("1 = main menu, 2 = prelaunch, 3 = orbit, 4 = paused orbit, 5 = offline, q = quit");
            StartConsoleWatcher(bridge);

            bool offlineApplied = false;
            while (!bridge.QuitRequested)
            {
                if (bridge.Scenario == DevScenario.Offline && !offlineApplied)
                {
                    server.Stop();
                    offlineApplied = true;
                }
                else if (bridge.Scenario != DevScenario.Offline && offlineApplied)
                {
                    try
                    {
                        server.Start(port);
                        Console.WriteLine("API listening again on http://127.0.0.1:" + server.Port + "/api");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Could not restart on port " + port + ": " + ex.Message);
                    }

                    offlineApplied = false;
                }

                bridge.Tick();
                dispatcher.Pump();
                System.Threading.Thread.Sleep(20);
            }

            return 0;
        }

        private static void StartConsoleWatcher(DevBridge bridge)
        {
            if (Console.IsInputRedirected)
            {
                // Running under a background host (CI, script): no keyboard scenarios available.
                return;
            }

            var thread = new System.Threading.Thread(delegate()
            {
                try
                {
                    while (!bridge.QuitRequested)
                    {
                        if (!Console.KeyAvailable)
                        {
                            System.Threading.Thread.Sleep(50);
                            continue;
                        }

                        ConsoleKeyInfo key = Console.ReadKey(true);
                        switch (key.KeyChar)
                        {
                            case '1':
                                bridge.SetScenario(DevScenario.MainMenu);
                                Console.WriteLine("scenario: main menu");
                                break;
                            case '2':
                                bridge.SetScenario(DevScenario.Prelaunch);
                                Console.WriteLine("scenario: prelaunch");
                                break;
                            case '3':
                                bridge.SetScenario(DevScenario.Orbit);
                                Console.WriteLine("scenario: orbit");
                                break;
                            case '4':
                                bridge.SetScenario(DevScenario.Paused);
                                Console.WriteLine("scenario: paused");
                                break;
                            case '5':
                                bridge.SetScenario(DevScenario.Offline);
                                Console.WriteLine("scenario: offline (no API responses)");
                                break;
                            case 'q':
                            case 'Q':
                                bridge.QuitRequested = true;
                                break;
                            default:
                                break;
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // Console was detached; scenarios simply stop working.
                }
            });

            thread.IsBackground = true;
            thread.Start();
        }
    }
}
