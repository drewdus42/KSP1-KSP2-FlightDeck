# KSP Flight Deck HTTP API (KSP 2)

The mod exposes a small JSON API on the loopback interface only. It is designed so the
Stream Deck plugin (and anything else you want to script) can drive a vessel in flight.

This document describes the **KSP 2** build. The KSP 1 build speaks exactly the same protocol on
`http://127.0.0.1:8080`; the only differences are the action groups each game supports and where a
value comes from, which is why every response carries a `game` field and a `capabilities` object.

- Base URL: `http://127.0.0.1:8081`
- Transport: HTTP/1.1, keep-alive supported, CORS `*` (needed because the plugin page is `file://`)
- Encoding: UTF-8 JSON
- Threading: requests are executed on Unity's main thread inside the addon's `Update()`; a
  request that cannot be served within 2 seconds returns `503 game_busy`
- No authentication. Bind is loopback-only, but any local process can still control your craft.

## Game and capabilities

`GET /api/health`, `GET /api/state` and `GET /api` all include:

```json
{
  "game": "ksp2",
  "gameName": "Kerbal Space Program 2",
  "capabilities": {
    "actionGroups": ["Abort", "Gear", "ResourceScan", "Science", "SolarPanels", "..."],
    "sasModes": ["StabilityAssist", "Prograde", "..."],
    "virtualActionGroups": [],
    "resources": true,
    "deltaV": true,
    "orbit": true,
    "staging": true,
    "timeWarp": true,
    "pause": true
  }
}
```

Clients should treat `capabilities` as authoritative instead of assuming which groups exist.
`game` is `"ksp1"` or `"ksp2"`; `virtualActionGroups` lists the groups the mod has to emulate
(KSP 1 only), and is empty here because KSP 2 has real solar, radiator, science and resource-scan
groups.

## Envelopes

### `POST /api` (the Kerbal Web Program compatible envelope)

```json
{
  "ID": "abc123",
  "Action": "setActionGroupState",
  "parameters": { "ID": "Gear" }
}
```

Success:

```json
{
  "ID": "abc123",
  "Action": "setActionGroupState",
  "Success": true,
  "Data": { "Group": "Gear", "State": true, "Status": true }
}
```

Failure (HTTP 400 / 503 / 500):

```json
{
  "ID": "abc123",
  "Action": "setActionGroupState",
  "Success": false,
  "ok": false,
  "Error": "unknown_action_group",
  "Message": "Unknown action group 'Warp'."
}
```

`Data.Status` is a compatibility duplicate of `Data.State` so the original plugin's
`getActionGroupState` / `setActionGroupState` handling keeps working.

### `GET /api/health`

```json
{ "ok": true, "mod": "KSPFlightDeck", "version": "1.0.0", "apiVersion": "1", "game": "ksp2", "inFlight": false }
```

### `GET /api/state`

One request returns everything the UI needs. This is what the plugin polls at 4 Hz.

```json
{
  "ok": true,
  "mod": "KSPFlightDeck",
  "version": "1.0.1",
  "data": { "...": "see the snapshot schema below" }
}
```

When no vessel is loaded (main menu, editor, KSC) it returns HTTP 200 with:

```json
{ "ok": false, "error": "not_in_flight", "message": "No vessel is loaded. ..." }
```

### `GET /` or `GET /api/index`

Self-describing index of endpoints and action names.

### `OPTIONS *`

Returns `204` with the CORS preflight headers.

## Snapshot schema

```jsonc
{
  "vessel": {
    "name": "Mun Lander",
    "situation": "ORBITING",
    "situationString": "Orbiting Kerbin",
    "body": "Kerbin",
    "mass": 12.4,
    "crew": 3,
    "landed": false,
    "splashed": false
  },
  "throttle": 0.35,
  "sas": { "Enabled": true, "Mode": "Prograde", "CanSetMode": true },
  "actionGroups": {
    "Abort": false, "Antennas": false, "Brakes": false, "Custom01": false,
    "Custom02": false, "Custom03": false, "Custom04": false, "Custom05": false,
    "Custom06": false, "Custom07": false, "Custom08": false, "Custom09": false,
    "Custom10": false, "Gear": true, "Lights": false, "RCS": false,
    "RadiatorPanels": false, "SAS": true, "Science": true, "SolarPanels": true
  },
  "staging": {
    "CurrentStage": 3,
    "StageCount": 6,
    "RemainingStages": 3,
    "CanStage": true,
    "Exhausted": false,
    "Prelaunch": false,
    "Label": "Stage",
    "Situation": "ORBITING"
  },
  "orbit": {
    "available": true,
    "body": "Kerbin",
    "apoapsis": 120000.0,
    "periapsis": 79000.0,
    "altitude": 84210.5,
    "radius": 684210.5,
    "inclination": 0.7853981,
    "eccentricity": 0.0123,
    "period": 3600.0,
    "timeToApoapsis": 900.0,
    "timeToPeriapsis": 1800.0,
    "orbitalSpeed": 2280.4,
    "surfaceSpeed": 2281.2,
    "verticalSpeed": -3.1,
    "radarAltitude": 83000.0,
    "semiMajorAxis": 684210.5,
    "atmosphereDepth": 70000.0
  },
  "resources": {
    "ElectricCharge": { "amount": 512.4, "max": 1000.0, "fraction": 0.5124 },
    "LiquidFuel": { "amount": 320.0, "max": 800.0, "fraction": 0.4 }
  },
  "timeWarp": { "Index": 0, "Rate": 1.0, "MaxIndex": 8, "Paused": false },
  "paused": false,
  "deltaV": { "available": true, "vacuum": 3456.7, "asl": 2980.1, "burnTime": 212.4 },
  "timestamp": 123456.78
}
```

Units: metres for distances/altitudes, m/s for speeds, seconds for times, radians for
inclination, `0.0–1.0` for throttle. Values that KSP cannot compute are `null` or
`available: false`.

`orbit.available` is `false` while the vessel is landed or splashed: KSP still exposes an `Orbit`
then, but its apoapsis/periapsis/period are degenerate (a resting vessel reports a negative
periapsis), so only altitude and speed fields are sent.

## Actions

All action names are case-insensitive. Parameters accept the legacy `ID` key as well as
`Group` / `name`.

### Action groups

| Action | Parameters | `Data` |
| --- | --- | --- |
| `getActionGroupState` | `ID` | `{ Group, State, Status }` |
| `setActionGroupState` | `ID`, optional `State` | `{ Group, State, Status }` — toggles when `State` is absent (legacy behaviour) |
| `setActionGroup` | `Group`, `State` | `{ Group, State, Status }` |
| `getActionGroups` | – | `{ Groups: { name: bool } }` |

Native KSP 2 groups: `Abort`, `Brakes`, `Gear`, `Lights`, `RCS`, `SAS`, `SolarPanels`,
`RadiatorPanels`, `Science`, `ResourceScan`, `Custom01`…`Custom10`.
Aliases such as `light`, `custom1` and `scan` are normalised.

KSP 2 reports group state as `None`/`True`/`False`/`Mixed`; the API maps `True` and `Mixed` to
`State: true` (a partly deployed panel group reads as on).

`Antennas` is accepted by the name normaliser for compatibility with the KSP 1 API, but KSP 2 has
no antenna action group: reads report `false` and writes return `400 unknown_action_group`.

### Throttle

| Action | Parameters | `Data` |
| --- | --- | --- |
| `getShipThrottle` | – | `{ Throttle }` |
| `setShipThrottle` | `Throttle` (0–1, clamped) | `{ Throttle }` |

The mod copies the vessel's `FlightCtrlState`, sets `mainThrottle` and pushes it back with
`SetFlightControlState`, so the stock throttle indicator follows along.

### SAS

| Action | Parameters | `Data` |
| --- | --- | --- |
| `getSas` | – | `{ Enabled, Mode, CanSetMode }` |
| `setSas` | optional `Enabled`, optional `Mode` | `{ Enabled, Mode, CanSetMode }` |
| `setSasMode` | `Mode` | `{ Enabled, Mode, CanSetMode }` |
| `toggleSas` | – | `{ Enabled, Mode, CanSetMode }` |

Modes: `StabilityAssist`, `Prograde`, `Retrograde`, `Normal`, `Antinormal`, `RadialIn`,
`RadialOut`, `Target`, `AntiTarget`, `Maneuver` (aliases: `node`, `radial`, `kill`, …).

A mode that KSP 2 refuses (no target, no maneuver node) returns `400 sas_mode_unavailable`.
`setSas` with `Enabled: true` sets the mode and engages in the same call, then verifies the mode the
game ended up in (see below) so a key can never claim a mode SAS is not actually holding.

### Staging

| Action | Parameters | `Data` |
| --- | --- | --- |
| `getStaging` | – | `{ CurrentStage, StageCount, RemainingStages, CanStage, Exhausted, Prelaunch, Label, Situation }` |
| `stage` | – | same, after activating the next stage |

`Prelaunch` (and `Label` = `Launch`) follows `VesselComponent.HasLaunched`, which is exactly the
"press Launch first, then Stage" behaviour the deck uses.

Staging comes from `SimulationObject.Staging` (`StageCount` remaining, `TotalStageCount`,
`HaveStages`). When the staging stack is exhausted, `Exhausted` is true, `CanStage` is false and
`Label` is `Done`, which the plugin renders as its grey key instead of a Stage button.

### SAS

KSP 2's `VesselAutopilot.Activate()` does not always carry the mode through, so the mod sets the mode
first, activates, then verifies `AutopilotMode`; a mode that still did not take returns
`400 sas_mode_unavailable` naming the mode the game actually stayed in.

### Action group state on KSP 2

KSP 2 tracks state for the built-in groups (`Gear`, `RCS`, `SolarPanels`, …) but its custom groups
(`Custom01`…`Custom10`) only fire their parts and report no state back. The mod remembers the state
the last write asked for and reports that for those groups, so a Stream Deck key can show a stable
on/off; the latch is cleared whenever the active vessel changes.

### Time warp and pause

| Action | Parameters | `Data` |
| --- | --- | --- |
| `getTimeWarp` | – | `{ Index, Rate, MaxIndex, Paused }` |
| `setTimeWarp` | `Index` | `{ Index, Rate, MaxIndex, Paused }` |
| `getPaused` | – | `{ Paused }` |
| `setPaused` | `Paused` | `{ Paused }` |
| `togglePause` | – | `{ Paused }` |

Index 0 is 1×; indices 1–3 are the physics warp rates, higher indices are on-rails warp.

### Readouts

| Action | Parameters | `Data` |
| --- | --- | --- |
| `getState` | – | the full snapshot |
| `getResources` | – | `{ Resources: { name: { amount, max, fraction } } }` |

Resources are totalled across every part container. If a KSP 2 build (for example a Redux
refactor) makes part enumeration fail, the mod falls back to vessel-wide percentages and marks
those entries with `percentageOnly: true`, where `amount`/`fraction` are `0–1` and `max` is `1`.
| `getOrbit` | – | the `orbit` object from the snapshot |

## Error codes

| Code | HTTP | Meaning |
| --- | --- | --- |
| `invalid_json` | 400 | request body is not JSON |
| `invalid_request` | 400 | body is not a JSON object |
| `missing_action` | 400 | no `Action` property |
| `missing_parameter` | 400 | a required parameter is absent or of the wrong type |
| `unknown_action` | 400 | action name is not implemented |
| `unknown_action_group` | 400 | group name is not recognised |
| `unknown_sas_mode` | 400 | SAS mode is not recognised |
| `sas_unavailable` | 400 | vessel has no probe core or pilot, or KSP refused to engage SAS |
| `sas_mode_unavailable` | 400 | KSP refused the mode (no target/node) |
| `not_in_flight` | 503 | no loaded vessel |
| `game_busy` | 503 | the game did not answer within 2 seconds |
| `internal_error` | 500 | unexpected exception; see `KSP.log` |

## Quick manual test

```powershell
curl http://127.0.0.1:8081/api/health
curl http://127.0.0.1:8081/api/state

curl -Method POST http://127.0.0.1:8081/api `
  -ContentType "application/json" `
  -Body '{"ID":"t1","Action":"getActionGroupState","parameters":{"ID":"Gear"}}'
```
