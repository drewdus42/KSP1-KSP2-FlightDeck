# KSP Flight Deck (mod)

Local HTTP control API for **Kerbal Space Program 1.12.x**. It replaces the KSP 2 Kerbal Web
Program mod that the Stream Deck plugin used to talk to, and keeps its request/response shape so
existing client code keeps working.

- Listens on `http://127.0.0.1:8080` (loopback only, no admin rights, no URL ACL)
- Runs a single poll-friendly endpoint (`GET /api/state`) plus the legacy `POST /api` actions
- Everything executes on the main thread through a queue pumped from `Update()`
- No dependencies beyond KSP itself (no Harmony, no bundled JSON library)

Full protocol reference: [docs/API.md](docs/API.md)

## Install

1. Copy `GameData/KSPFlightDeck` into your KSP `GameData` folder.
2. Start KSP. You should see `Flight Deck API: http://127.0.0.1:8080/api` in the top right of the
   main menu, and `[KSPFlightDeck] Flight Deck listening ...` in `KSP.log`.
3. Verify with `curl http://127.0.0.1:8080/api/health`.

The mod starts in the main menu and stays alive across scene changes, so the Stream Deck knows it
is connected before you launch a craft.

## Settings

`GameData/KSPFlightDeck/settings.cfg` (created on first run):

```cfg
KSPFlightDeck
{
	enabled = true
	port = 8080
	showStartupMessage = true
}
```

If the port is busy the mod tries the next nine ports and logs which one it used; update the
plugin's connection settings to match.

Diagnostics go to `GameData/KSPFlightDeck/PluginData/KSPFlightDeck.log` (rotated at 2 MB) and to
`KSP.log`.

## What maps to what

| API group | KSP 1 implementation |
| --- | --- |
| `Abort`, `Brakes`, `Gear`, `Lights`, `RCS`, `SAS`, `Custom01`–`Custom10` | `vessel.ActionGroups` / `KSPActionGroup` |
| `SolarPanels` | every `ModuleDeployableSolarPanel` on the vessel (all extended / all retracted) |
| `RadiatorPanels` | every `ModuleDeployableRadiator` |
| `Antennas` | every `ModuleDeployableAntenna` |
| `Science` | state = an experiment is ready; switching on runs every available `ModuleScienceExperiment` |

SAS requests go through `SasPlan`, a tested planner: KSP's `Autopilot.Enable()` internally calls
`Enable(StabilityAssist)`, so engaging must be a single `Enable(mode)` call. `VesselAutopilot.Update()`
also mirrors the SAS action group every frame (group on → `Enable()`, group off → `Disable()`), so
every SAS change updates the group as well — otherwise disabling is undone immediately and SAS
appears stuck on. The mod reports `sas_unavailable` rather than silently doing nothing when a vessel
has no SAS capability.

Staging uses `KSP.UI.Screens.StageManager`; SAS uses `VesselAutopilot.SetMode`/`Enable`; time warp
uses `TimeWarp.SetRate`; pause uses `FlightDriver.SetPause`; delta-v comes from the stock
`VesselDeltaV` simulation.

## Build

Requirements: .NET SDK 8 (only to build), PowerShell, and KSP 1.12 with its managed assemblies.

```powershell
dotnet build src\KSPFlightDeck\KSPFlightDeck.csproj -c Release
```

(Or `dotnet build KSPFlightDeck.sln` to compile every project, including the tests and dev host.)

The output goes straight to `dist\GameData\KSPFlightDeck\Plugins\` (`KSPFlightDeck.dll` plus
`FlightDeck.Core.dll`). If KSP is not in the default Steam location:

```powershell
dotnet build src\KSPFlightDeck\KSPFlightDeck.csproj -c Release -p:KspDir="D:\Steam\steamapps\common\Kerbal Space Program"
```

## Test

```powershell
dotnet test tests\FlightDeck.Core.Tests\FlightDeck.Core.Tests.csproj   # 75 tests, no KSP needed
dotnet build tests\FlightDeck.Smoke\FlightDeck.Smoke.csproj -c Release
tests\FlightDeck.Smoke\bin\Release\net472\FlightDeck.Smoke.exe        # loads the DLL next to KSP's assemblies
```

| Project | What it covers |
| --- | --- |
| `src/FlightDeck.Core` | JSON, HTTP server, main-thread dispatcher, API router — no KSP or Unity references |
| `src/KSPFlightDeck` | KSP addon and `GameBridge` (the only code that touches KSP) |
| `tests/FlightDeck.Core.Tests` | xunit tests including a real socket round trip, CORS, keep-alive, legacy envelope compatibility |
| `tests/FlightDeck.Smoke` | net472 executable that loads `KSPFlightDeck.dll` against the real KSP assemblies, verifies the `KSPAddon` contract, and drives the HTTP API |
| `tests/FlightDeck.DevHost` | runs the real API with a simulated vessel for plugin work |

## Develop without launching KSP

```powershell
dotnet run --project tests\FlightDeck.DevHost -- --port 8080
```

The dev host serves the same endpoints from a simulated orbiting vessel and animates altitude,
fuel and electric charge. Press `1`–`5` in its console to switch between main menu, prelaunch,
orbit, paused and offline (the API stops answering), which is a quick way to check the plugin's
connection states. `q` quits.

From the plugin folder you can then run `node test\live-check.js` to exercise the plugin's real
client modules against it.

## Layout

```
src/FlightDeck.Core/       transport + protocol (net472 + net8.0, unit tested)
src/KSPFlightDeck/         KSPAddon, GameBridge, settings, logging
tests/FlightDeck.Core.Tests/
tests/FlightDeck.Smoke/
dist/GameData/KSPFlightDeck/
```

## Notes and limits

- The API exposes whatever KSP exposes; it does not add new gameplay (no autopilot beyond SAS).
- Only one vessel is addressable: the active vessel (`FlightGlobals.ActiveVessel`).
- Requests time out after 2 seconds and return `503 game_busy`. This is deliberate: KSP cannot
  answer while loading or while the game is doing heavy physics work.
- Loopback only. If you want remote control you would need to bind another address and add
  authentication yourself — the mod intentionally never listens on the network.
