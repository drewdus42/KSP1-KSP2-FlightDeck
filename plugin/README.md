# KSP Flight Control (Stream Deck plugin)

Control a Kerbal Space Program 1.12 vessel from a Stream Deck. This is the successor to the KSP 2
plugin of the same name: it now talks to the **KSP Flight Deck** mod, a local HTTP API for the
original game (see the mod repository's `docs/API.md` for the protocol).

Unlike the KSP 2 version, keys stay in sync with the game (the mod reports real in-game state),
and the plugin makes **one request for all keys** instead of one request per button.

## Install

1. Install the mod first: copy `GameData/KSPFlightDeck` into your KSP folder and start the game once.
2. Double-click `Release/com.drewdus.kspflightcontrol.streamDeckPlugin`, or install it from the
   Stream Deck app (the `Release` build is produced by `DistributionTool.exe`).
3. Drop the **Connection Status** key on your deck and press *Test connection* in its settings. It
   should report `Connected: KSPFlightDeck 1.0.1 (main menu)` or `(vessel in flight)`.

The default endpoint is `127.0.0.1:8080`. Change it in the Connection Status property inspector if
you edited `settings.cfg` in the mod.

## Actions

| Action | Controls | Notes |
| --- | --- | --- |
| **Action Group** | any KSP action group | Icon reflects the real group state. Includes virtual groups: solar panels, radiators, antennas and science |
| **Staging** | launch / next stage | Uses the `Launch` artwork while nothing has been staged yet and `Next Stage` afterwards, with no extra text on the key |
| **SAS Mode** | SAS + mode | Your per-mode SAS artwork only (filled icon = SAS is holding that mode, outline = idle). Press engages SAS in the configured mode, press again while it is active switches SAS off. Modes that need a target or maneuver node flash red when KSP refuses |

Each SAS key is a toggle: pressing it while SAS holds that mode switches SAS off, otherwise it
engages that mode. Stability Assist behaves the same as every other mode.
| **Telemetry** | one live readout | Altitude, radar altitude, apoapsis, periapsis, orbital/surface/vertical speed, inclination, period, time to Ap/Pe, delta-v, throttle, warp, stage, or any resource (fuel, oxidizer, monoprop, EC, …) |
| **Connection Status** | endpoint settings | Shows `OFFLINE`, `READY` (main menu) or the active vessel, body and situation |
| **Thrust** (Stream Deck + dial) | throttle | Your `Thrust_Static` touch-bar art with the percentage and level bar beside it. Rotate 1% per tick (5% per fast flick), press → 0%, tap → 100% |
| **Time Warp** (Stream Deck + dial) | warp + pause | Your `TimeWarp_Static` touch-bar art with the multiplier (`×1` … `×100k`) and warp bar. Rotate changes warp index, press toggles pause, tap returns to 1× |

Every key falls back to a canvas-drawn tile when an asset is missing (for example there is no
maneuver-node SAS drawing), so new action groups still look intentional.

Artwork comes from the original plugin: `Buttons/ActionGroup/{True,False}` for action groups,
`Buttons/SAS/SAS Modes Unlabelled` for SAS modes (shown as pure artwork — no text is added to those
keys), `Buttons/Other` for staging and the shared panel (`Blank3`), and `Buttons/TouchBar` for the
two dials. `@2x` PNG companions are generated for the raster images the manifest references,
because the packager requires them.

## Property inspector settings

| Inspector | Setting |
| --- | --- |
| Action Group | which group the key toggles |
| SAS Mode | which autopilot mode the key engages |
| Telemetry | which readout to show; resource names are remembered across sessions, so a fuel/EC readout stays selectable even while KSP is in the main menu |
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

## Migrating a profile from an older build

The plugin UUID has changed twice during development: first from `com.novistrum.ksp2flightcontrol`
(the original KSP 2 plugin) to `com.novistrum.kspflightcontrol`, and now to
`com.drewdus.kspflightcontrol` to match the author name. Keys placed with an older UUID will not
light up, so re-add them from the new "KSP Flight Control" category (profiles must be rebuilt
once). All original artwork is still used.

## Development

```powershell
node --test test\util.test.js test\snapshot.test.js test\state.test.js test\manifest.test.js test\art.test.js   # 48 tests
.\DistributionTool.exe -b -i "src\com.drewdus.kspflightcontrol.sdPlugin" -o "Release"
```

To exercise the plugin against a live API without launching KSP, start the mod's dev host and run
the end-to-end check (it uses the same `js/api.js`, `js/state.js` and `js/util.js` the plugin runs):

```powershell
cd ..\KSP1
dotnet run --project tests\FlightDeck.DevHost -- --port 8080
# in another terminal:
cd ..\plugin
node test\live-check.js
```

`test/manifest.test.js` validates the manifest against the files on disk: every action UUID is
namespaced and unique, every icon/state/layout/inspector exists, layouts contain the keys the code
expects, and `app.html` loads scripts in dependency order. `test/state.test.js` starts a fake mod
server and exercises the real HTTP client and polling hub. `test/snapshot.test.js` runs against
`test/fixtures/snapshot.json`, which is captured from the real mod, so a change in the mod's wire
format (for example `CurrentStage` vs `currentStage`) fails the suite instead of showing up as a
blank key.

Source layout:

```
src/com.drewdus.kspflightcontrol.sdPlugin/
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
