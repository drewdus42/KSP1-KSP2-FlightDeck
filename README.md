# KSP Flight Deck

Stream Deck flight controls for **Kerbal Space Program 1.12** and **Kerbal Space Program 2**
(Redux / SpaceWarp 2), built from three cooperating pieces:

| Part | What it is |
| --- | --- |
| [`KSP1/`](KSP1/) | **KSP Flight Deck** – a KSP 1 addon that hosts a local HTTP API on `127.0.0.1:8080` |
| [`KSP2/`](KSP2/) | **KSP Flight Deck 2** – the same API for KSP 2, on `127.0.0.1:8081` |
| [`plugin/`](plugin/) | **KSP Flight Control** – one Stream Deck plugin that drives whichever game is running |

The plugin used to talk to Kerbal Web Program, a KSP 2 mod that is no longer maintained. Both mods
here recreate that bridge for their game, keep the request/response format the plugin was written
against, and add the features the old plugin had on its wish list (staging, SAS modes, telemetry
readouts). The plugin finds the first mod that answers, then lights up only the keys that game
supports, so the same profile works in either game.

Full protocol reference: [`KSP1/docs/API.md`](KSP1/docs/API.md) (identical for
[`KSP2/docs/API.md`](KSP2/docs/API.md), plus the KSP 2 differences noted there).

## How it fits together

```
┌─────────────┐   key / dial    ┌──────────────┐   HTTP    ┌────────────────┐   main thread   ┌─────────┐
│ Stream Deck │ ──────────────▶ │  plugin      │ ────────▶ │ KSP Flight     │ ──────────────▶ │ KSP 1   │
│  hardware   │ ◀────────────── │  app.js      │ ◀──────── │ Deck addon     │                 │ 1.12    │
└─────────────┘  /api/state 4Hz └──────┬───────┘  JSON     └────────────────┘                 └─────────┘
                                       │        or
                                       │                  ┌────────────────┐                 ┌─────────┐
                                       └────────────────▶ │ KSP Flight     │ ──────────────▶ │ KSP 2   │
                                                          │ Deck 2 addon   │                 │ Redux   │
                                                          └────────────────┘                 └─────────┘
```

- Both addons start in the main menu and survive scene changes, so the deck knows the mod is alive
  before a craft exists.
- All game calls are marshalled to the game's main thread, because neither KSP nor Unity is
  thread-safe.
- The plugin polls a single snapshot endpoint instead of one request per button, and only polls
  while at least one key is visible.
- `GET /api/health` reports `game`, `gameName` and `capabilities`, which is how the plugin decides
  which actions and readouts to offer.

## Quick start

1. **Install a mod**
   - KSP 1: copy `KSP1/dist/GameData/KSPFlightDeck` into your KSP `GameData` folder.
   - KSP 2: copy `KSP2/dist/mods/KSPFlightDeck2` into your KSP 2 `mods` folder.
   The main menu then shows which port the API is listening on.
2. **Install the plugin**
   Double-click `plugin/Release/com.novistrum.kspflightcontrol.streamDeckPlugin`.
3. **Add keys** from the *KSP Flight Control* category. Start with **Connection Status** and press
   *Test connection* to confirm the link.

Or do both with `.\install.ps1 -Plugin` after a build.

## Requirements

- KSP 1.12.x on Windows, and/or KSP 2 with SpaceWarp 2 (the Redux community build ships it)
- Stream Deck 6.x or newer
- To build: .NET SDK 8, PowerShell 5.1+, Node.js 18+ (plugin tests only)

## Repository layout

```
KSP1/                    KSP 1 addon (C#)
  src/FlightDeck.Core/     JSON, HTTP server, dispatcher, API router — no KSP references, unit tested
  src/KSPFlightDeck/       KSPAddon, GameBridge (the only KSP-facing code), settings, logging
  tests/FlightDeck.Core.Tests/
  tests/FlightDeck.Smoke/  net472 host that loads the mod next to the real KSP assemblies
  tests/FlightDeck.DevHost/ simulated vessel + real API, for plugin development
  dist/GameData/KSPFlightDeck/
  build.ps1, install.ps1   build/install just this mod
KSP2/                    KSP 2 addon (C#)
  src/FlightDeck.Core/     the same protocol layer (kept identical to KSP1's copy)
  src/KSP2FlightDeck/      SpaceWarp 2 mod entry point + Ksp2Bridge (the only KSP 2-facing code)
  tests/FlightDeck.Core.Tests/
  tests/FlightDeck.DevHost/
  dist/mods/KSPFlightDeck2/
  build.ps1, install.ps1   build/install just this mod
plugin/                  Stream Deck plugin (HTML/JS, shared by both games)
  src/com.novistrum.kspflightcontrol.sdPlugin/
  test/                    node:test suites (helpers, HTTP client/polling, manifest, artwork)
  Release/                 built .streamDeckPlugin
build.ps1                builds, tests and packages everything
install.ps1              installs either or both mods (+ -Plugin)
```

## Build and verify

```powershell
.\build.ps1                                   # both mods + plugin
.\build.ps1 -Only KSP1                        # one target (Also: KSP2, Plugin)
.\build.ps1 -SkipTests                        # just package
.\build.ps1 -KspDir "D:\...\Kerbal Space Program" -Ksp2Dir "D:\...\Kerbal Space Program 2"
```

What runs:

| Step | Command | Covers |
| --- | --- | --- |
| KSP 1 mod build | `dotnet build KSP1\src\KSPFlightDeck -c Release` | compiles against the real KSP assemblies |
| KSP 2 mod build | `dotnet build KSP2\src\KSP2FlightDeck -c Release` | compiles against the real KSP 2 + SpaceWarp assemblies |
| Protocol tests | `dotnet test KSP1\tests\FlightDeck.Core.Tests` (and `KSP2\...`) | JSON, HTTP semantics (CORS, keep-alive, chunked body), dispatcher timeouts, SAS call planning, legacy API compatibility, validation — 80 tests per repo |
| KSP 1 smoke test | `KSP1\tests\FlightDeck.Smoke\...\FlightDeck.Smoke.exe` | loads `KSPFlightDeck.dll` against KSP's DLLs, checks the `KSPAddon` contract, drives the real server over a socket |
| Plugin tests | `node --test plugin\test\*.test.js` | formatters, HTTP client against a fake mod, polling state machine, manifest/asset integrity, artwork coverage — 48 tests |
| Packaging | `DistributionTool.exe`, `Compress-Archive` | `.streamDeckPlugin`, `KSPFlightDeck-1.0.1.zip`, `KSPFlightDeck2-1.0.0.zip` |

There is also a dev host (a simulated vessel serving the real API) for each game and a live check
for plugin work without the game:

```powershell
cd KSP1; dotnet run --project tests\FlightDeck.DevHost -- --port 8080
cd KSP2; dotnet run --project tests\FlightDeck.DevHost -- --port 8081
cd plugin; node test\live-check.js     # drives the real client against the real API
```

The protocol layer (`FlightDeck.Core`) deliberately has no KSP or Unity references, which is why the
whole API can be tested without launching either game. The smoke test closes the remaining gap for
KSP 1 by loading the game-facing assembly on .NET Framework with KSP's DLLs present.

## API compatibility

The old Kerbal Web Program protocol is preserved in both games:

```json
POST /api   { "ID": "abc123", "Action": "getActionGroupState", "parameters": { "ID": "Gear" } }
         →  { "ID": "abc123", "Action": "getActionGroupState", "Success": true, "Data": { "Status": true } }
```

`getActionGroupState`, `setActionGroupState`, `getShipThrottle` and `setShipThrottle` behave exactly
as before, so any other client written for Kerbal Web Program keeps working. New actions
(`getState`, `getSas`, `setSas`, `getStaging`, `stage`, `getTimeWarp`, `setTimeWarp`,
`togglePause`, `getResources`, `getOrbit`, …) are documented in
[`KSP1/docs/API.md`](KSP1/docs/API.md) and [`KSP2/docs/API.md`](KSP2/docs/API.md).

## Notes and limits

- Loopback only and unauthenticated: any local app can control your craft. Do not expose the ports.
- The mods address the active vessel only; there is no editor or KSC support.
- KSP 1 has no solar/radiator/antenna/science action groups, so the mod emulates them by walking
  the matching part modules. KSP 2 has them natively, plus a Resource Scan group.
- KSP 2 Redux is a moving target: the mod is built against the SpaceWarp 2 API that ships with
  Redux 0.2.8.5, and a game update can move that API.
- Profiles built for the original KSP 2 plugin need rebuilding once because the plugin UUID changed.

## Ideas for the next pass

- Custom labels per action-group key (property inspector free text).
- Delta-v per stage on a dial, and a manoeuvre-node readout.
- Transfer/encounter helpers that need a real orbit solver (MechJeb-style) rather than stock data.
- Optional read-only mode in `settings.cfg` for streaming setups.
