/// <reference path="libs/js/action.js" />
/// <reference path="libs/js/stream-deck.js" />
/// <reference path="js/util.js" />
/// <reference path="js/api.js" />
/// <reference path="js/state.js" />
/// <reference path="js/images.js" />

const PLUGIN_UUID = "com.drewdus.kspflightcontrol";
const ACTION_UUID = {
	group: PLUGIN_UUID + ".action",
	staging: PLUGIN_UUID + ".staging",
	sas: PLUGIN_UUID + ".sas",
	thrust: PLUGIN_UUID + ".thrust",
	timewarp: PLUGIN_UUID + ".timewarp",
	telemetry: PLUGIN_UUID + ".telemetry",
	status: PLUGIN_UUID + ".status"
};

const THROTTLE_SEND_INTERVAL_MS = 80;
const THROTTLE_RELEASE_DELAY_MS = 600;
// How long a pressed group key keeps showing its own state before the poll wins again.
const OPTIMISTIC_GROUP_TTL_MS = 1500;
// How long a refused SAS mode stays written on its key.
const SAS_REFUSAL_TTL_MS = 3000;
// Hold an action group key this long to flip it without firing the group.
const LONG_PRESS_MS = 700;

const client = KspApi.createClient();
const hub = KspState.createHub({ client });
const renderer = KspImages.createRenderer($SD);

/** context -> { kind, context, settings, optimisticThrottle, releaseTimer } */
const contexts = new Map();
let hubSubscription = null;
let latestSnapshot = null;
let pendingThrottle = null;
let throttleTimer = null;
let throttleInFlight = false;

$SD.onConnected(function () {
	console.log("KSP Flight Control connected");
	$SD.getGlobalSettings();
});

$SD.onDidReceiveGlobalSettings(function (jsn) {
	const settings = (jsn && jsn.payload && jsn.payload.settings) || {};
	client.configure({ host: settings.host, port: settings.port });
	console.log("KSP Flight Control endpoint:", client.baseUrl());
	hub.refresh();
});

function entryFor(context) {
	return contexts.get(context) || null;
}

function ensurePolling() {
	if (!hubSubscription) {
		hubSubscription = hub.subscribe(function (snapshot, status, error) {
			latestSnapshot = KspUtil.normalizeSnapshot(snapshot);
			renderAll(latestSnapshot, status, error);
		});
	}
}

function snapshot() {
	return latestSnapshot;
}

function releasePollingWhenIdle() {
	if (contexts.size === 0 && hubSubscription) {
		hubSubscription();
		hubSubscription = null;
	}
}

function addContext(context, kind, payload) {
	const existing = contexts.get(context);
	const entry = existing || { kind: kind, context: context, settings: {}, optimisticThrottle: null, optimisticGroup: null, releaseTimer: null };
	entry.kind = kind;
	entry.settings = (payload && payload.settings) || entry.settings || {};
	entry.optimisticGroup = null;
	contexts.set(context, entry);
	renderer.forget(context);
	ensurePolling();
	renderEntry(entry, snapshot(), hub.getStatus(), hub.getError());

	if (!payload || !payload.settings) {
		$SD.getSettings(context);
	}
}

function removeContext(context) {
	const entry = contexts.get(context);
	if (entry && entry.releaseTimer) {
		clearTimeout(entry.releaseTimer);
	}
	contexts.delete(context);
	renderer.forget(context);
	releasePollingWhenIdle();
}

function renderAll(snapshot, status, error) {
	contexts.forEach(function (entry) {
		renderEntry(entry, snapshot, status, error);
	});
}

function renderEntry(entry, snapshot, status, error) {
	try {
		switch (entry.kind) {
			case "group":
				renderGroup(entry, snapshot, status);
				break;
			case "staging":
				renderStaging(entry, snapshot, status);
				break;
			case "sas":
				renderSas(entry, snapshot, status);
				break;
			case "telemetry":
				renderTelemetry(entry, snapshot, status);
				break;
			case "status":
				renderStatus(entry, snapshot, status, error);
				break;
			case "thrust":
				renderThrust(entry, snapshot, status);
				break;
			case "timewarp":
				renderTimeWarp(entry, snapshot, status);
				break;
			default:
				break;
		}
	} catch (cause) {
		console.error("KSP Flight Control: failed to render", entry.kind, cause);
	}
}

function offlineHint(status) {
	if (status === "menu") {
		return "no vessel";
	}
	return status === "offline" ? "offline" : "";
}

/**
 * A pressed group key shows the state it just asked for until the poll agrees with it,
 * so the artwork flips on the press instead of up to a poll later.
 */
function optimisticGroupState(entry, reported) {
	const pending = entry.optimisticGroup;
	if (!pending) {
		return reported;
	}
	if (reported === pending.state || Date.now() > pending.until) {
		entry.optimisticGroup = null;
		return reported;
	}
	return pending.state;
}

function renderGroup(entry, snapshot, status) {
	const group = entry.settings.action;
	const online = status === "flight";
	const reported = online && snapshot && snapshot.actionGroups ? Boolean(snapshot.actionGroups[group]) : false;
	const state = online ? optimisticGroupState(entry, reported) : false;
	const configured = group && group !== "None";

	if (!configured) {
		renderer.key(entry.context, {
			backgroundImage: KspUtil.ASSETS.panel,
			label: "ACTION GROUP",
			value: "SETUP",
			unit: "pick one in settings",
			accent: "#64748b",
			valueSize: 30
		});
		return;
	}

	renderer.key(entry.context, {
		image: KspUtil.actionGroupImage(group, state),
		// Groups without artwork (KSP 2's Resource Scan) get the shared panel instead of a bare tile.
		backgroundImage: KspUtil.actionGroupImage(group, state) ? null : KspUtil.ASSETS.panel,
		title: "",
		label: KspUtil.prettyGroupName(group),
		value: online ? (state ? "ON" : "OFF") : KspUtil.DASH,
		unit: online ? "" : offlineHint(status),
		accent: state ? "#22c55e" : "#64748b"
	});
}

function renderStaging(entry, snapshot, status) {
	const staging = status === "flight" && snapshot ? snapshot.staging : null;
	const prelaunch = staging ? Boolean(staging.prelaunch) : true;
	const exhausted = staging ? Boolean(staging.exhausted) : false;

	if (exhausted) {
		// Nothing left to fire: the grey blank instead of a Stage button that would do nothing.
		renderer.key(entry.context, {
			backgroundImage: KspUtil.ASSETS.stageIdle,
			bare: true,
			title: "",
			label: "STAGING",
			value: "DONE",
			unit: "",
			valueSize: 30
		});
		return;
	}

	// The artwork already says LAUNCH / NEXT STAGE, so the key carries no extra text.
	renderer.key(entry.context, {
		image: prelaunch ? KspUtil.ASSETS.launch : KspUtil.ASSETS.stage,
		title: "",
		label: prelaunch ? "Launch" : "Stage",
		value: "",
		unit: staging ? "" : offlineHint(status),
		accent: "#f59e0b"
	});
}

function renderSas(entry, snapshot, status) {
	const desired = entry.settings.mode || "StabilityAssist";
	const sas = status === "flight" && snapshot ? snapshot.sas : null;
	const enabled = Boolean(sas && sas.enabled);
	const activeMode = sas ? sas.mode : null;
	// The key lights up only when SAS is engaged in *this* key's mode.
	const engagedHere = Boolean(enabled && activeMode === desired);

	if (entry.refusal && Date.now() < entry.refusal.until) {
		renderer.key(entry.context, {
			backgroundImage: KspUtil.ASSETS.panel,
			bare: true,
			label: KspUtil.sasModeLabel(desired).toUpperCase(),
			value: "REFUSED",
			unit: entry.refusal.text,
			accent: "#ef4444",
			valueSize: 26
		});
		return;
	}

	const artwork = KspUtil.sasImage(desired, engagedHere);
	if (artwork) {
		// Pure artwork, no text: the filled/outline icon already shows the state.
		renderer.key(entry.context, {
			image: artwork,
			title: "",
			label: KspUtil.sasModeLabel(desired)
		});
		return;
	}

	// Maneuver (and anything else without artwork) gets a drawn tile.
	renderer.key(entry.context, {
		backgroundImage: KspUtil.ASSETS.panel,
		label: KspUtil.sasModeLabel(desired).toUpperCase(),
		value: engagedHere ? "ON" : "OFF",
		unit: sas ? (enabled && !engagedHere ? "SAS: " + KspUtil.sasBadge(activeMode) : "") : offlineHint(status),
		accent: engagedHere ? "#22c55e" : "#64748b",
		valueSize: 32
	});
}

function renderTelemetry(entry, snapshot, status) {
	const field = entry.settings.field || "altitude";
	const online = status === "flight" && snapshot;
	const formatted = KspUtil.formatTelemetry(field, online ? snapshot : null);
	const value = online ? formatted.value : KspUtil.DASH;

	renderer.key(entry.context, {
		backgroundImage: KspUtil.ASSETS.panel,
		// The user's blank panel art is the face of the key: no scrim, no accent bar.
		bare: true,
		label: formatted.label,
		value: value,
		unit: online ? formatted.unit : offlineHint(status),
		accent: "#38bdf8",
		valueSize: value.length > 7 ? 26 : 38
	});
}

function renderStatus(entry, snapshot, status, error) {
	if (status === "offline") {
		renderer.key(entry.context, {
			backgroundImage: KspUtil.ASSETS.panel,
			bare: true,
			label: "KSP FLIGHT DECK",
			value: "OFFLINE",
			unit: error === "unreachable" ? "no mod found" : "check the mod",
			accent: "#ef4444",
			valueSize: 30
		});
		return;
	}

	const gameLabel = KspUtil.gameLabel(hub.getGame(), hub.getGameName());

	if (status === "menu") {
		renderer.key(entry.context, {
			backgroundImage: KspUtil.ASSETS.panel,
			bare: true,
			label: gameLabel || "KSP FLIGHT DECK",
			value: "READY",
			unit: "main menu",
			accent: "#f59e0b",
			valueSize: 30
		});
		return;
	}

	const vessel = (snapshot && snapshot.vessel) || {};
	const name = vessel.name || "VESSEL";

	renderer.key(entry.context, {
		backgroundImage: KspUtil.ASSETS.panel,
		bare: true,
		label: (vessel.body || gameLabel || "FLIGHT").toUpperCase(),
		value: name.length > 12 ? name.slice(0, 12) : name,
		unit: vessel.situationString || vessel.situation || "",
		accent: snapshot && snapshot.paused ? "#f59e0b" : "#22c55e",
		valueSize: 26,
		badge: snapshot && snapshot.paused ? "II" : null
	});
}

function renderThrust(entry, snapshot, status) {
	const throttle = entry.optimisticThrottle !== null && entry.optimisticThrottle !== undefined
		? entry.optimisticThrottle
		: status === "flight" && snapshot
			? snapshot.throttle
			: 0;
	const percent = Math.round(KspUtil.clamp01(throttle) * 100);
	const value = status !== "flight" ? KspUtil.DASH : percent + "%";

	// The touch strip artwork (Thrust_Static) is the whole tile, with the percentage and a slim
	// gauge sitting in the blank band it leaves in the middle.
	renderer.feedback(entry.context, {
		title: "",
		value: value,
		indicator: { value: status === "flight" ? percent : 0 }
	}, "thrust|" + value + "|" + status);
}

function renderTimeWarp(entry, snapshot, status) {
	const warp = status === "flight" && snapshot ? snapshot.timeWarp : null;
	const rate = warp ? warp.rate : 1;
	const index = warp ? warp.index : 0;
	const maxIndex = warp ? Math.max(1, warp.maxIndex) : 1;
	const paused = warp ? Boolean(warp.paused) : false;
	const percent = Math.round((index / maxIndex) * 100);
	const value = status !== "flight" ? KspUtil.DASH : paused ? "II" : KspUtil.formatWarpRate(rate);

	renderer.feedback(entry.context, {
		title: paused ? "PAUSED" : "",
		value: value,
		indicator: { value: status === "flight" ? percent : 0 }
	}, "warp|" + value + "|" + index + "|" + rate + "|" + paused + "|" + status);
}

async function safeSend(context, action, parameters) {
	try {
		const result = await client.send(action, parameters);
		if (!result.success) {
			console.warn("KSP Flight Control:", action, "failed:", result.error, result.message);
			$SD.showAlert(context);
			return { ok: false, error: result.error || "refused", message: result.message || "" };
		}
		return { ok: true, data: result.data };
	} catch (cause) {
		console.warn("KSP Flight Control:", action, "failed:", cause);
		$SD.showAlert(context);
		hub.refresh();
		return { ok: false, error: "unreachable", message: String(cause) };
	}
}

/** Short text for a refused SAS press, shown on the key instead of a silent red flash. */
function sasRefusalText(error, mode, sas, vessel) {
	if (error === "sas_unavailable") {
		return "no SAS";
	}
	if (error === "unreachable") {
		return "offline";
	}
	// KSP 2 has no prograde/normal/radial direction to hold while the vessel sits on the ground.
	if (vessel && (vessel.landed || vessel.splashed)) {
		return "on the ground";
	}
	if (mode === "Target" || mode === "AntiTarget") {
		return "needs target";
	}
	if (mode === "Maneuver") {
		return "needs node";
	}
	if (sas && sas.canSetModes && sas.canSetModes[mode] === false) {
		return "game says no";
	}
	return "refused";
}

function setThrottle(entry, value) {
	entry.optimisticThrottle = KspUtil.clamp01(value);
	renderThrust(entry, snapshot(), hub.getStatus());

	pendingThrottle = entry.optimisticThrottle;
	if (!throttleTimer && !throttleInFlight) {
		throttleTimer = setTimeout(flushThrottle, THROTTLE_SEND_INTERVAL_MS);
	}

	if (entry.releaseTimer) {
		clearTimeout(entry.releaseTimer);
	}
	entry.releaseTimer = setTimeout(function () {
		entry.optimisticThrottle = null;
		hub.refresh();
	}, THROTTLE_RELEASE_DELAY_MS);
}

async function flushThrottle() {
	throttleTimer = null;
	const value = pendingThrottle;
	pendingThrottle = null;

	if (value === null) {
		return;
	}

	throttleInFlight = true;
	try {
		await client.send("setShipThrottle", { Throttle: value });
	} catch (cause) {
		console.warn("KSP Flight Control: throttle update failed", cause);
	} finally {
		throttleInFlight = false;
		if (pendingThrottle !== null) {
			throttleTimer = setTimeout(flushThrottle, THROTTLE_SEND_INTERVAL_MS);
		}
	}
}

/// ACTION GROUP TOGGLE

const ActionGroupToggle = new Action(ACTION_UUID.group);

ActionGroupToggle.onWillAppear(function (jsn) {
	addContext(jsn.context, "group", jsn.payload);
});

ActionGroupToggle.onWillDisappear(function (jsn) {
	removeContext(jsn.context);
});

ActionGroupToggle.onDidReceiveSettings(function (jsn) {
	const entry = entryFor(jsn.context);
	if (!entry) {
		return;
	}
	entry.settings = (jsn.payload && jsn.payload.settings) || {};
	entry.optimisticGroup = null;
	renderEntry(entry, snapshot(), hub.getStatus(), hub.getError());
});

ActionGroupToggle.onKeyDown(function (jsn) {
	const entry = entryFor(jsn.context);
	if (entry) {
		entry.pressedAt = Date.now();
	}
});

ActionGroupToggle.onKeyUp(function (jsn) {
	const entry = entryFor(jsn.context);
	const group = entry && entry.settings.action;
	if (!group || group === "None") {
		$SD.showAlert(jsn.context);
		return;
	}

	const heldMs = entry.pressedAt ? Date.now() - entry.pressedAt : 0;
	entry.pressedAt = 0;

	// A long press only flips what the key reports, so a group that has drifted out of sync can be
	// put back in step without firing it; the next short press then fires it the right way.
	const longPress = entry.settings.longPressInvert !== false && heldMs >= LONG_PRESS_MS;
	const parameters = longPress
		? KspUtil.groupInvertParameters(snapshot(), group)
		: KspUtil.groupPressParameters(snapshot(), group);

	// Send the state to land on rather than a blind toggle, then paint it immediately:
	// waiting for the next poll made presses look like they did nothing.
	entry.optimisticGroup = { state: parameters.State, until: Date.now() + OPTIMISTIC_GROUP_TTL_MS };
	renderEntry(entry, snapshot(), hub.getStatus(), hub.getError());
	if (longPress) {
		$SD.showOk(jsn.context);
	}

	safeSend(jsn.context, "setActionGroup", parameters).then(function (result) {
		if (!result.ok && entry.optimisticGroup) {
			entry.optimisticGroup = null;
		}
		hub.refresh();
	});
});

/// STAGING

const StagingAction = new Action(ACTION_UUID.staging);

StagingAction.onWillAppear(function (jsn) {
	addContext(jsn.context, "staging", jsn.payload);
});

StagingAction.onWillDisappear(function (jsn) {
	removeContext(jsn.context);
});

StagingAction.onKeyUp(function (jsn) {
	const state = snapshot();
	if (state && state.staging && state.staging.exhausted) {
		// Nothing left to fire: the grey key already says so, so no request and no false hope.
		$SD.showAlert(jsn.context);
		return;
	}

	safeSend(jsn.context, "stage", {}).then(function (result) {
		if (result.ok) {
			hub.refresh();
		}
	});
});

/// SAS MODE

const SasAction = new Action(ACTION_UUID.sas);

SasAction.onWillAppear(function (jsn) {
	addContext(jsn.context, "sas", jsn.payload);
});

SasAction.onWillDisappear(function (jsn) {
	removeContext(jsn.context);
});

SasAction.onDidReceiveSettings(function (jsn) {
	const entry = entryFor(jsn.context);
	if (!entry) {
		return;
	}
	entry.settings = (jsn.payload && jsn.payload.settings) || {};
	renderEntry(entry, snapshot(), hub.getStatus(), hub.getError());
});

SasAction.onKeyUp(function (jsn) {
	const entry = entryFor(jsn.context);
	if (!entry) {
		return;
	}

	const desired = entry.settings.mode || "StabilityAssist";
	const state = snapshot();
	const parameters = KspUtil.sasPressParameters(state ? state.sas : null, desired);

	safeSend(jsn.context, "setSas", parameters).then(function (result) {
		if (result.ok) {
			hub.refresh();
			return;
		}

		// Say why on the key: a refused mode used to look like a press that did nothing.
		entry.refusal = {
			text: sasRefusalText(result.error, desired, state ? state.sas : null, state ? state.vessel : null),
			until: Date.now() + SAS_REFUSAL_TTL_MS
		};
		renderEntry(entry, snapshot(), hub.getStatus(), hub.getError());
		setTimeout(function () {
			if (entry.refusal && Date.now() >= entry.refusal.until) {
				entry.refusal = null;
				renderEntry(entry, snapshot(), hub.getStatus(), hub.getError());
			}
		}, SAS_REFUSAL_TTL_MS + 50);
	});
});

/// TELEMETRY

const TelemetryAction = new Action(ACTION_UUID.telemetry);

TelemetryAction.onWillAppear(function (jsn) {
	addContext(jsn.context, "telemetry", jsn.payload);
});

TelemetryAction.onWillDisappear(function (jsn) {
	removeContext(jsn.context);
});

TelemetryAction.onDidReceiveSettings(function (jsn) {
	const entry = entryFor(jsn.context);
	if (!entry) {
		return;
	}
	entry.settings = (jsn.payload && jsn.payload.settings) || {};
	renderEntry(entry, snapshot(), hub.getStatus(), hub.getError());
});

TelemetryAction.onKeyUp(function (jsn) {
	hub.refresh();
	$SD.showOk(jsn.context);
});

/// CONNECTION STATUS

const StatusAction = new Action(ACTION_UUID.status);

StatusAction.onWillAppear(function (jsn) {
	addContext(jsn.context, "status", jsn.payload);
});

StatusAction.onWillDisappear(function (jsn) {
	removeContext(jsn.context);
});

StatusAction.onKeyUp(function (jsn) {
	hub.refresh();
	if (hub.getStatus() === "offline") {
		$SD.showAlert(jsn.context);
	} else {
		$SD.showOk(jsn.context);
	}
});

/// THRUST DIAL

const ThrustDial = new Action(ACTION_UUID.thrust);

ThrustDial.onWillAppear(function (jsn) {
	addContext(jsn.context, "thrust", jsn.payload);
});

ThrustDial.onWillDisappear(function (jsn) {
	removeContext(jsn.context);
});

ThrustDial.onDialRotate(function (jsn) {
	const entry = entryFor(jsn.context);
	if (!entry) {
		return;
	}

	const ticks = Number(jsn.payload && jsn.payload.ticks) || 0;
	if (ticks === 0) {
		return;
	}

	const state = snapshot();
	const currentThrottle = entry.optimisticThrottle !== null && entry.optimisticThrottle !== undefined
		? entry.optimisticThrottle
		: state
			? state.throttle
			: 0;

	// Big flicks jump 5%, gentle nudges move 1% for fine trim.
	const step = Math.abs(ticks) > 1 ? 0.05 : 0.01;
	setThrottle(entry, Math.round(KspUtil.clamp01(currentThrottle + ticks * step) * 100) / 100);
});

ThrustDial.onDialPress(function (jsn) {
	const entry = entryFor(jsn.context);
	if (!entry || (jsn.payload && jsn.payload.pressed)) {
		return;
	}
	setThrottle(entry, 0);
});

ThrustDial.onTouchTap(function (jsn) {
	const entry = entryFor(jsn.context);
	if (!entry || (jsn.payload && jsn.payload.pressed)) {
		return;
	}
	setThrottle(entry, 1);
});

/// TIME WARP DIAL

const TimeWarpDial = new Action(ACTION_UUID.timewarp);

TimeWarpDial.onWillAppear(function (jsn) {
	addContext(jsn.context, "timewarp", jsn.payload);
});

TimeWarpDial.onWillDisappear(function (jsn) {
	removeContext(jsn.context);
});

TimeWarpDial.onDialRotate(function (jsn) {
	const ticks = Number(jsn.payload && jsn.payload.ticks) || 0;
	if (ticks === 0) {
		return;
	}

	const state = snapshot();
	const warp = state ? state.timeWarp : null;
	if (!warp || warp.index === null) {
		$SD.showAlert(jsn.context);
		return;
	}

	const next = KspUtil.clamp(warp.index + ticks, 0, Math.max(0, warp.maxIndex));
	if (next === warp.index) {
		return;
	}

	safeSend(jsn.context, "setTimeWarp", { Index: next }).then(function (result) {
		if (result.ok) {
			hub.refresh();
		}
	});
});

TimeWarpDial.onDialPress(function (jsn) {
	if (jsn.payload && jsn.payload.pressed) {
		return;
	}
	safeSend(jsn.context, "togglePause", {}).then(function (result) {
		if (result.ok) {
			hub.refresh();
		}
	});
});

TimeWarpDial.onTouchTap(function (jsn) {
	if (jsn.payload && jsn.payload.pressed) {
		return;
	}
	safeSend(jsn.context, "setTimeWarp", { Index: 0 }).then(function (result) {
		if (result.ok) {
			hub.refresh();
		}
	});
});
