# KSP Flight Control (Stream Deck plugin)

Control a Kerbal Space Program 1.12 vessel from a Stream Deck. This is the successor to the KSP 2
plugin of the same name: it now talks to the **KSP Flight Deck** mod, a local HTTP API for the
original game (see the mod repository's `docs/API.md` for the protocol).

Unlike the KSP 2 version, keys stay in sync with the game (the mod reports real in-game state),
and the plugin makes **one request for all keys** instead of one request per button.

## Install

1. Install the mod first: copy `GameData/KSPFlightDeck` into your KSP folder and start the game once.
2. Double-click `Release/com.novistrum.kspflightcontrol.streamDeckPlugin`, or install it from the
   Stream Deck app (the `Release` build is produced by `DistributionTool.exe`).
3. Drop the **Connection Status** key on your deck and press *Test connection* in its settings. It
   should report `Connected: KSPFlightDeck 1.0.0 (main menu)` or `(vessel in flight)`.

The default endpoint is `127.0.0.1:8080`. Change it in the Connection Status property inspector if
you edited `settings.cfg` in the mod.

## Actions

| Action | Controls | Notes |
| --- | --- | --- |
| **Action Group** | any KSP action group | Icon reflects the real group state. Includes virtual groups: solar panels, radiators, antennas and science |
| **Staging** | launch / next stage | Shows `Launch` on the pad and `Stage` afterwards, with `current / total` stages as the title |
| **SAS Mode** | SAS + mode | Press enables SAS in the configured mode; press again while that mode is active to switch SAS off. Modes that need a target or maneuver node flash red when KSP refuses |
| **Telemetry** | one live readout | Altitude, radar altitude, apoapsis, periapsis, orbital/surface/vertical speed, inclination, period, time to Ap/Pe, delta-v, throttle, warp, stage, or any resource (fuel, oxidizer, monoprop, EC, …) |
| **Connection Status** | endpoint settings | Shows `OFFLINE`, `READY` (main menu) or the active vessel, body and situation |
| **Thrust** (Stream Deck + dial) | throttle | Rotate 1% per tick (5% per fast flick), press → 0%, tap → 100% |
| **Time Warp** (Stream Deck + dial) | warp + pause | Rotate changes warp index, press toggles pause, tap returns to 1× |

Every key falls back to a canvas-drawn tile when an asset is missing (for example there is no
antenna artwork), so new action groups still look intentional.

## Property inspector settings

| Inspector | Setting |
| --- | --- |
| Action Group | which group the key toggles |
| SAS Mode | which autopilot mode the key engages |
| Telemetry | which readout to show (resource entries appear once the mod reports a vessel) |
| Connection Status | global mod host/port plus a *Test connection* button |
| Thrust | static help text; the dial needs no settings |

Host and port are **global settings**, so they apply to every key at once.

## How it works

```
Stream Deck key ──▶ app.js ──▶ KspApi client ──HTTP──▶ KSP Flight Deck mod ──▶ KSP API
                     ▲                                   (main-thread queue)
                     └──────── /api/state snapshot (4 Hz) ───────┘
```

- `js/util.js` – pure formatting/normalisation helpers (unit tested under Node)
- `js/api.js` – HTTP client: `GET /api/state`, `POST /api` with the legacy `{ID, Action, parameters}` envelope
- `js/state.js` – one polling loop for the whole plugin: 250 ms in flight, 750 ms in the menu,
  1500 ms while offline; stops when no keys are visible
- `js/images.js` – asset loading with caching plus canvas-drawn fallbacks; identical frames are not re-uploaded
- `app.js` – action wiring and rendering

Dial input is optimistic: the dial updates instantly and throttle writes are coalesced to at most
one request every 80 ms (with a trailing write), which keeps the mod responsive while you spin.

## Migrating a profile from the KSP 2 plugin

The plugin UUID changed from `com.novistrum.ksp2flightcontrol` to `com.novistrum.kspflightcontrol`,
so old keys will not light up. Re-add the keys from the new "KSP Flight Control" category
(profiles must be rebuilt once). All original artwork is still used.

## Development

```powershell
node --test test\util.test.js test\state.test.js test\manifest.test.js   # 23 tests
.\DistributionTool.exe -b -i "src\com.novistrum.kspflightcontrol.sdPlugin" -o "Release"
```

To exercise the plugin against a live API without launching KSP, start the mod's dev host and run
the end-to-end check (it uses the same `js/api.js`, `js/state.js` and `js/util.js` the plugin runs):

```powershell
cd ..\mod
dotnet run --project tests\FlightDeck.DevHost -- --port 8080
# in another terminal:
cd ..\plugin
node test\live-check.js
```

`test/manifest.test.js` validates the manifest against the files on disk: every action UUID is
namespaced and unique, every icon/state/layout/inspector exists, layouts contain the keys the code
expects, and `app.html` loads scripts in dependency order. `test/state.test.js` starts a fake mod
server and exercises the real HTTP client and polling hub.

Source layout:

```
src/com.novistrum.kspflightcontrol.sdPlugin/
  manifest.json            actions, encoders, property inspectors
  app.html / app.js        plugin entry point
  js/*.js                  client, polling hub, rendering, helpers
  actions/template/        icons and property inspectors
  Buttons/, Layouts/       key artwork and Stream Deck + layouts
```

## Known limits

- The mod only knows the active vessel; there is no editor or tracking-station support.
- No throttle-hold or autopilot beyond KSP's own SAS modes.
- If KSP is loading or hitching, keys briefly show alerts; they recover on the next poll.
