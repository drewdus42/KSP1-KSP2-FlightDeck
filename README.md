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

## Requirements

- **KSP 1:** 1.12.x (64-bit) on Windows
- **KSP 2:** with **SpaceWarp 2** — the Redux community build ships it, so a Redux install needs
  nothing extra; a stock KSP 2 install needs SpaceWarp 2 first
- **Stream Deck:** app 6.x or newer, any model (dials need a Stream Deck + / MK.2)
- **To build from source:** .NET SDK 8, PowerShell 5.1+, Node.js 18+ (plugin tests only)

## Install

Two pieces per game: the **mod**, which runs the API inside the game, and the **plugin**, which
lives in the Stream Deck app. You install the plugin once — the same plugin drives both games and
finds whichever one is running. Install the mod for each game you want to fly.

### 1. The Stream Deck plugin — double-click to install

1. Grab `plugin/Release/com.drewdus.kspflightcontrol.streamDeckPlugin`. (No file yet? Build it:
   `.\build.ps1 -Only Plugin`, or use the copy already committed under `plugin/Release`.)
2. **Double-click the file.** Stream Deck asks for confirmation — click **Install**. It copies the
   plugin to `%APPDATA%\Elgato\StreamDeck\Plugins\com.drewdus.kspflightcontrol.sdPlugin`, restarts
   it, and the *KSP Flight Control* category appears in the action list on the right.
3. No file association, or the app is already open and nothing happens? Run either of these:
   ```powershell
   Start-Process .\plugin\Release\com.drewdus.kspflightcontrol.streamDeckPlugin
   .\install.ps1 -Only Plugin      # also removes older copies of the plugin
   ```
4. Drag the keys you want from the *KSP Flight Control* category onto your deck.

Upgrading works the same way: double-click the newer `.streamDeckPlugin` and confirm the replace.
Close the Stream Deck app first if Windows complains that a file is in use.

> Keys placed with an older build (`com.novistrum.kspflightcontrol.*`) belong to a different plugin
> UUID and will show as missing after this change — re-add them once from the new category.

### 2. KSP 1 — install the mod

1. Copy the folder `KSP1/dist/GameData/KSPFlightDeck` into your KSP 1 `GameData` folder, so you end
   up with `Kerbal Space Program\GameData\KSPFlightDeck\Plugins\KSPFlightDeck.dll`.
2. Start KSP 1. The main menu pops **"Flight Deck API: http://127.0.0.1:8080/api"** in the top right
   (turn it off with `showStartupMessage = false` in `settings.cfg`). The log is written to
   `GameData\KSPFlightDeck\PluginData\`.
3. Check the link from a terminal — it should answer while the game sits in the main menu:
   ```powershell
   curl http://127.0.0.1:8080/api/health      # {"ok":true,...,"game":"ksp1",...}
   ```

One step instead of a manual copy:

```powershell
.\install.ps1 -Only KSP1                                        # finds the Steam install
.\install.ps1 -Only KSP1 -KspDir "D:\...\Kerbal Space Program"  # custom path
```

Re-running the install keeps your existing `settings.cfg` and `PluginData`. The API listens on
**port 8080**; change `port` in `GameData\KSPFlightDeck\settings.cfg` and set the same port on the
Connection Status key if you need to.

### 3. KSP 2 — install the mod

1. Copy `KSP2/dist/mods/KSPFlightDeck2` into `<Kerbal Space Program 2>\mods\`, giving
   `mods\KSPFlightDeck2\KSPFlightDeck2.dll` next to `swinfo.json`. There is no registration step:
   SpaceWarp 2 loads every folder in `mods\` that contains a `swinfo.json`.
2. Start KSP 2. `mods\KSPFlightDeck2\Logs\` should contain
   `[KSPFlightDeck2] Flight Deck listening on http://127.0.0.1:8081/api`.
3. Check the link:
   ```powershell
   curl http://127.0.0.1:8081/api/health      # {"ok":true,...,"game":"ksp2",...}
   ```

One step instead of a manual copy:

```powershell
.\install.ps1 -Only KSP2
.\install.ps1 -Only KSP2 -Ksp2Dir "D:\...\Kerbal Space Program 2"
```

The API listens on **port 8081**, one above KSP 1, so both games can run at the same time without
touching either settings file. `swinfo.json` declares `ksp2_version` `0.2.0.0`–`0.2.99.0`; widen
that range if you run a KSP 2 build outside it, otherwise SpaceWarp skips the mod.

### 4. First keys to add

1. **Connection Status** — put it on the deck first. It names the game, the port and the vessel, or
   says *no game*. If it stays dark, open its property inspector and press **Test connection**;
   that separates a port problem from a mod problem.
2. **Action Group** / **SAS Mode** — choose the group or mode per key in the property inspector. The
   list is whatever the connected game reports.
3. **Telemetry** — pick a readout per key, including one key per resource.
4. **Thrust** / **Time Warp** — dials; rotate to change, press to act (see
   [Features](#features)).

### Update and uninstall

- **Update a mod:** copy the new folder over the old one (`install.ps1` also keeps `settings.cfg`).
- **Update the plugin:** double-click the newer `.streamDeckPlugin`.
- **Uninstall:** delete `GameData\KSPFlightDeck` (KSP 1) and/or `mods\KSPFlightDeck2` (KSP 2), then
  delete `%APPDATA%\Elgato\StreamDeck\Plugins\com.drewdus.kspflightcontrol.sdPlugin`. Nothing else
  on disk is touched.

## Features

One plugin action per kind of key, so a single profile works in both games. What each one does:

| Action | Controls | Notes |
| --- | --- | --- |
| **Connection Status** | connection state | Shows *flying / main menu / offline* plus the game (KSP 1 or KSP 2), the port and the vessel name, drawn straight on your blank panel art. Polls `/api/state` while any key is visible and stops when none are. Property inspector has **Test connection** and a host/port override. |
| **Action Group** | any action group | Your own artwork for on and off (the real group state, not a guess). Group chosen per key from what the connected game reports. KSP 2's custom groups report no state of their own, so the mod remembers what each press asked for and the key follows that. |
| **Staging** | launch, then next stage | Shows **Launch** artwork until the vessel has launched and **Stage** afterwards; one key covers the whole ascent. When nothing is left to fire the key turns into your grey blank (`BlankGray`) marked **STAGING DONE** and stops sending stage requests. |
| **SAS Mode** | SAS + one mode | Pure artwork: filled icon means SAS is holding this key's mode, outline means it isn't. Press engages the mode; press again while it holds that mode switches SAS off. Modes KSP refuses (no target, no node) flash red. |
| **Telemetry** | one live readout | Altitude, radar altitude, apoapsis, periapsis, orbital speed, surface speed, vertical speed, inclination, period, time to Ap, time to Pe, delta-v (vac), throttle, warp, stage — plus one option per resource the vessel carries. Drawn directly on your blank panel art: no scrim, no accent bar. |
| **Thrust** (dial) | throttle 0–100 % | Rotate: 1 % per tick, 5 % per tick when you spin fast. Press the dial to cut throttle, tap the touch strip for full throttle. Writes are coalesced to one request every 80 ms so spinning stays smooth. |
| **Time Warp** (dial) | warp level, pause | Rotate through the game's warp levels up to its ceiling. Press to pause/unpause, tap the touch strip to drop back to 1×. |

### Working

| Capability | KSP 1 | KSP 2 (Redux) |
| --- | --- | --- |
| Read/write throttle | ✅ | ✅ |
| Action groups: Abort, Brakes, Gear, Lights, RCS, SAS, Custom01–10 | ✅ native | ✅ native |
| Solar panels, radiators, science | ✅ emulated from part modules | ✅ native |
| Antennas group | ✅ emulated | ⚠️ listed but no effect (KSP 2 has no such group) |
| Resource scan | — | ✅ native, momentary (the key shows ON briefly) |
| SAS: stability, prograde, retrograde, normal, antinormal, radial in/out, target, anti-target, maneuver | ✅ | ✅ (mode is set and verified; targeting modes need a target) |
| Staging: launch → stage → grey "done" key | ✅ | ✅ (from KSP 2's staging stack) |
| Time warp levels + ceiling, pause | ✅ | ✅ |
| Orbit readout, delta-v | ✅ | ✅ |
| Per-resource levels | ✅ | ✅ |
| Auto-detected game and port | ✅ 8080 | ✅ 8081 |
| Legacy Kerbal Web Program calls (`getActionGroupState`, …) | ✅ | ✅ |
| Works without the game running (keys show offline, no errors) | ✅ | ✅ |

Both mods: loopback-only HTTP on the game's main thread, `503 game_busy` instead of blocking when a
request cannot be served within 2 s, and a `settings.cfg` that survives reinstalls.

## Todo

- [ ] **Per-key labels** — free-text override in the property inspector for action-group keys.
- [ ] **Delta-v per stage** on a dial, and a manoeuvre-node readout (prograde/radial/normal deltas).
- [ ] **Manoeuvre-node helpers** — the readout exists; creating/aligning nodes needs an orbit solver.
- [ ] **Read-only mode** in `settings.cfg` for streaming setups.
- [ ] **Editor and KSC support** — both mods currently address only the active vessel.
- [ ] **Science and antenna detail** — show science data volume / relay status, not just the toggle.
- [ ] **Staging depth on a telemetry key** — both mods now report remaining and total stages, so a
      "stage 3 / 6" readout is possible again if wanted (the staging key itself stays artwork-only).
- [ ] **Publish to the Stream Deck store** — needs author signing and preview images; keys placed
      with pre-`com.drewdus` builds have to be re-added once.
- [ ] **Snapshot push instead of polling** — a websocket would halve the round trips on big profiles.

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
  src/com.drewdus.kspflightcontrol.sdPlugin/
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
- Profiles built for older plugin UUIDs (`com.novistrum.*`) need rebuilding once; see
  [Install](#install).
