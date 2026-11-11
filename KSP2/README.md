# KSP Flight Deck 2 (mod)

Local HTTP control API for **Kerbal Space Program 2**, so the Stream Deck plugin (and any other
local tool) can read flight data and drive the active vessel. It is the KSP 2 half of the same
protocol the KSP 1 mod speaks: one plugin, two games.

- Talks to the game through **SpaceWarp 2**, which ships with KSP 2 0.2.x and with
  [KSP2 Redux](https://github.com/KSP2Redux/...) builds
- Listens on `http://127.0.0.1:8081` (loopback only, no admin rights, no URL ACL)
- Same endpoints and action names as the KSP 1 mod, plus native KSP 2 action groups
- Everything runs on Unity's main thread through a queue pumped from `Update()`

Protocol reference: [docs/API.md](docs/API.md)

## Install

1. Copy `mods\KSPFlightDeck2` into `<Kerbal Space Program 2>\mods\`
   (or run `.\install.ps1` after building).
2. Start KSP 2. The SpaceWarp log line `Registered plugin: KSPFlightDeck2` and
   `[KSPFlightDeck2] Flight Deck listening on http://127.0.0.1:8081/api` confirm the load.
3. Verify with `curl http://127.0.0.1:8081/api/health`.

The API starts as soon as SpaceWarp initialises the plugin, so it answers in the main menu with
`inFlight: false` — exactly what the Stream Deck needs to show `READY`.

## Redux notes

KSP2 Redux changes engine internals and not every mod survives it. This mod only uses public
SpaceWarp/KSP 2 API surface, and the parts that Redux is most likely to disturb (part and resource
enumeration) are wrapped in try/catch: if enumeration fails, the API falls back to the vessel-wide
fuel and electric-charge percentages instead of failing the request. Anything unusual is logged to
`mods\KSPFlightDeck2\Logs\KSPFlightDeck2.log`.

## In-game status panel

The mod adds a **Flight Deck** button to the KSC and in-flight app bars. It opens a UITK panel,
styled with the game's theme, that shows:

- Stream Deck connection state: listening endpoint, last poll age, request count and rate, open
  connections (green = polling, amber = waiting for the deck, red = server offline)
- Vessel telemetry: name and situation, throttle, SAS mode, stage, altitude/radar altitude,
  surface and vertical speed, orbit (Ap/Pe/inclination), delta-v and burn time, time warp
- Resource levels (top five by capacity) and the currently active action groups

`showPanel = false` hides it at startup; the app bar button and the panel's own **Hide panel**
button toggle it at any time.

## Settings

`mods\KSPFlightDeck2\settings.cfg` (created on first run):

```cfg
enabled = true
port = 8081
showStartupMessage = true
showPanel = true
```

If the port is busy the mod tries the next nine ports and logs which one it used.

## What maps to what

| API group | KSP 2 implementation |
| --- | --- |
| `Abort`, `Brakes`, `Gear`, `Lights`, `RCS`, `SAS`, `Custom01`–`Custom10` | native `KSPActionGroup` values via `VesselComponent.SetActionGroup` |
| `SolarPanels`, `RadiatorPanels`, `Science`, `ResourceScan` | native KSP 2 action groups (KSP 1 has to emulate the first three) |
| `Antennas` | accepted for compatibility; KSP 2 has no antenna action group, so reads return `false` and writes throw `unknown_action_group` |

Staging uses `ActivateNextStage()`; SAS uses `VesselAutopilot.Activate(mode)` / `Deactivate()` (one
call, so the mode cannot be lost the way KSP 1's `Enable()` does); time warp and pause come from
`ViewController.TimeWarp` / `ViewController.SetPause`; delta-v from `VesselDeltaVComponent`.

KSP 2 exposes `HasLaunched`, which is exactly the "Launch first, then Stage" behaviour the deck
uses — no stage counting required.

## Build

Requirements: .NET SDK 8 (only to build) and KSP 2 0.2.x with `SpaceWarp2.dll` in
`KSP2_x64_Data\Managed`.

```powershell
dotnet build src\KSP2FlightDeck\KSP2FlightDeck.csproj -c Release
```

If KSP 2 is not in the default Steam location:

```powershell
dotnet build src\KSP2FlightDeck\KSP2FlightDeck.csproj -c Release -p:Ksp2Dir="D:\Steam\steamapps\common\Kerbal Space Program 2"
```

The mod is a **single assembly**: the shared protocol layer is compiled into `KSPFlightDeck2.dll`
rather than shipped as a second dll, because SpaceWarp resolves mod assemblies from the game's
Managed folder and a sibling dependency fails to load there.

## Test

```powershell
dotnet test tests\FlightDeck.Core.Tests\FlightDeck.Core.Tests.csproj   # 80 tests, no game needed
dotnet run --project tests\FlightDeck.DevHost -- --port 8095            # simulated KSP 2 vessel
```

| Project | What it covers |
| --- | --- |
| `src/FlightDeck.Core` | JSON, HTTP server, dispatcher, router, capabilities — no game references |
| `src/KSP2FlightDeck` | SpaceWarp plugin + `Ksp2Bridge` (the only KSP 2 facing code) |
| `tests/FlightDeck.Core.Tests` | protocol tests, including the game/capabilities contract |
| `tests/FlightDeck.DevHost` | real API with a simulated KSP 2 vessel, for plugin work |

## Layout

```
KSP2/
  src/FlightDeck.Core/     shared protocol (same source as the KSP 1 mod)
  src/KSP2FlightDeck/      SpaceWarp entry point, bridge, settings, logging
  tests/                   protocol tests + dev host
  dist/mods/KSPFlightDeck2 swinfo.json, KSPFlightDeck2.dll
  build.ps1, install.ps1   build/install just this mod (or use ..\build.ps1 for everything)
```

## Limits

- Only the active vessel is addressable, same as the KSP 1 mod.
- KSP 2 has no public current-stage index, so the API reports `StageCount` derived from part
  activation stages and uses `HasLaunched` for the Launch/Stage label.
- Loopback only and unauthenticated: any local process can control your craft.
