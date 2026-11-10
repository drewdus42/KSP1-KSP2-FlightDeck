# KSP Flight Deck HTTP API

The mod exposes a small JSON API on the loopback interface only. It is designed so the
Stream Deck plugin (and anything else you want to script) can drive a vessel in flight.

- Base URL: `http://127.0.0.1:8080`
- Transport: HTTP/1.1, keep-alive supported, CORS `*` (needed because the plugin page is `file://`)
- Encoding: UTF-8 JSON
- Threading: requests are executed on Unity's main thread inside the addon's `Update()`; a
  request that cannot be served within 2 seconds returns `503 game_busy`
- No authentication. Bind is loopback-only, but any local process can still control your craft.

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
{ "ok": true, "mod": "KSPFlightDeck", "version": "1.0.0", "apiVersion": "1", "inFlight": false }
```

### `GET /api/state`

One request returns everything the UI needs. This is what the plugin polls at 4 Hz.

```json
{
  "ok": true,
  "mod": "KSPFlightDeck",
  "version": "1.0.0",
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
    "LastStage": 5,
    "CanStage": true,
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

Native KSP groups: `Abort`, `Brakes`, `Gear`, `Lights`, `RCS`, `SAS`, `Custom01`…`Custom10`.
Aliases such as `light`, `custom1` and `custom3` are normalised.

Virtual groups (KSP 1 has no equivalent):

| Group | Meaning |
| --- | --- |
| `SolarPanels` | all `ModuleDeployableSolarPanel` modules: state is "all extended", toggling extends or retracts every panel |
| `RadiatorPanels` | same for `ModuleDeployableRadiator` |
| `Antennas` | same for `ModuleDeployableAntenna` |
| `Science` | state is "any experiment available"; switching it on runs every ready `ModuleScienceExperiment` |

### Throttle

| Action | Parameters | `Data` |
| --- | --- | --- |
| `getShipThrottle` | – | `{ Throttle }` |
| `setShipThrottle` | `Throttle` (0–1, clamped) | `{ Throttle }` |

The mod sets both `FlightInputHandler.state.mainThrottle` and `vessel.ctrlState.mainThrottle`, so
the stock throttle indicator follows along.

### SAS

| Action | Parameters | `Data` |
| --- | --- | --- |
| `getSas` | – | `{ Enabled, Mode, CanSetMode }` |
| `setSas` | optional `Enabled`, optional `Mode` | `{ Enabled, Mode, CanSetMode }` |
| `setSasMode` | `Mode` | `{ Enabled, Mode, CanSetMode }` |
| `toggleSas` | – | `{ Enabled, Mode, CanSetMode }` |

Modes: `StabilityAssist`, `Prograde`, `Retrograde`, `Normal`, `Antinormal`, `RadialIn`,
`RadialOut`, `Target`, `AntiTarget`, `Maneuver` (aliases: `node`, `radial`, `kill`, …).

A mode that KSP refuses (no target, no maneuver node) returns `400 sas_mode_unavailable`.
`setSas` with `Enabled: true` always includes the mode in the same call, because KSP's
`Autopilot.Enable()` reverts to Stability Assist on its own.

### Staging

| Action | Parameters | `Data` |
| --- | --- | --- |
| `getStaging` | – | `{ CurrentStage, StageCount, LastStage, CanStage, Prelaunch, Label, Situation }` |
| `stage` | – | same, after activating the next stage |

`Prelaunch` (and `Label` = `Launch`) is true while nothing has been staged yet — on the pad, and
also just after lifting off before the first staging event, which is what the original plugin's
"Launch" button implied. After the first stage fires it becomes `Stage`.
`CurrentStage` counts down from `LastStage` to 0.

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
curl http://127.0.0.1:8080/api/health
curl http://127.0.0.1:8080/api/state

curl -Method POST http://127.0.0.1:8080/api `
  -ContentType "application/json" `
  -Body '{"ID":"t1","Action":"getActionGroupState","parameters":{"ID":"Gear"}}'
```
