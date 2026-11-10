/// <reference path="libs/js/action.js" />
/// <reference path="libs/js/stream-deck.js" />
/// <reference path="js/util.js" />
/// <reference path="js/api.js" />
/// <reference path="js/state.js" />
/// <reference path="js/images.js" />

const PLUGIN_UUID = "com.novistrum.kspflightcontrol";
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

const client = KspApi.createClient();
const hub = KspState.createHub({ client });
const renderer = KspImages.createRenderer($SD);

/** context -> { kind, context, settings, optimisticThrottle, releaseTimer } */
const contexts = new Map();
let hubSubscription = null;
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
			renderAll(snapshot, status, error);
		});
	}
}

function releasePollingWhenIdle() {
	if (contexts.size === 0 && hubSubscription) {
		hubSubscription();
		hubSubscription = null;
	}
}

function addContext(context, kind, payload) {
	const existing = contexts.get(context);
	const entry = existing || { kind: kind, context: context, settings: {}, optimisticThrottle: null, releaseTimer: null };
	entry.kind = kind;
	entry.settings = (payload && payload.settings) || entry.settings || {};
	contexts.set(context, entry);
	renderer.forget(context);
	ensurePolling();
	renderEntry(entry, hub.getSnapshot(), hub.getStatus(), hub.getError());

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

function renderGroup(entry, snapshot, status) {
	const group = entry.settings.action;
	const online = status === "flight";
	const state = online && snapshot && snapshot.actionGroups ? Boolean(snapshot.actionGroups[group]) : false;
	const configured = group && group !== "None";

	if (!configured) {
		renderer.key(entry.context, {
			label: "ACTION GROUP",
			value: "SETUP",
			unit: "pick one in settings",
			accent: "#64748b"
		});
		return;
	}

	renderer.key(entry.context, {
		image: KspUtil.actionGroupImage(group, state),
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
	const label = prelaunch ? "LAUNCH" : "STAGE";
	const counter = staging ? staging.currentStage + " / " + staging.stageCount : KspUtil.DASH;

	renderer.key(entry.context, {
		image: prelaunch ? "Buttons/Other/Launch.jpg" : "Buttons/Other/Stage.jpg",
		title: counter,
		label: label,
		value: counter,
		unit: staging ? "" : offlineHint(status),
		accent: "#f59e0b"
	});
}

function renderSas(entry, snapshot, status) {
	const desired = entry.settings.mode || "StabilityAssist";
	const sas = status === "flight" && snapshot ? snapshot.sas : null;
	const enabled = Boolean(sas && sas.enabled);
	const activeMode = sas ? sas.mode : null;
	const matches = Boolean(enabled && activeMode === desired);
	const title = enabled ? KspUtil.sasBadge(activeMode) : KspUtil.sasBadge(desired);

	renderer.key(entry.context, {
		image: enabled ? "Buttons/Other/SASon.jpg" : "Buttons/Other/SASoff.jpg",
		title: title,
		label: KspUtil.sasModeLabel(desired),
		value: KspUtil.sasBadge(activeMode || desired),
		unit: sas ? (matches ? "engaged" : enabled ? "other mode" : "off") : offlineHint(status),
		accent: matches ? "#22c55e" : enabled ? "#f59e0b" : "#64748b",
		badge: enabled ? "ON" : null
	});
}

function renderTelemetry(entry, snapshot, status) {
	const field = entry.settings.field || "altitude";
	const online = status === "flight" && snapshot;
	const formatted = KspUtil.formatTelemetry(field, online ? snapshot : null);
	const value = online ? formatted.value : KspUtil.DASH;

	renderer.key(entry.context, {
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
			image: "Buttons/Other/ServerDown.png",
			title: "OFFLINE",
			label: "KSP FLIGHT DECK",
			value: "OFFLINE",
			unit: error === "unreachable" ? "mod not found" : "check the mod",
			accent: "#ef4444"
		});
		return;
	}

	if (status === "menu") {
		renderer.key(entry.context, {
			label: "KSP FLIGHT DECK",
			value: "READY",
			unit: "main menu",
			accent: "#f59e0b"
		});
		return;
	}

	const vessel = (snapshot && snapshot.vessel) || {};
	const name = vessel.name || "VESSEL";

	renderer.key(entry.context, {
		label: (vessel.body || "FLIGHT").toUpperCase(),
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

	renderer.feedback(entry.context, {
		title: status === "flight" ? "THRUST" : status === "menu" ? "NO VESSEL" : "OFFLINE",
		value: percent + "%",
		indicator: { value: percent }
	}, "thrust|" + percent + "|" + status);
}

function renderTimeWarp(entry, snapshot, status) {
	const warp = status === "flight" && snapshot ? snapshot.timeWarp : null;
	const rate = warp ? warp.rate : 1;
	const index = warp ? warp.index : 0;
	const maxIndex = warp ? Math.max(1, warp.maxIndex) : 1;
	const paused = warp ? Boolean(warp.paused) : false;
	const percent = Math.round((index / maxIndex) * 100);

	renderer.feedback(entry.context, {
		title: paused ? "PAUSED" : "TIME WARP",
		value: "×" + KspUtil.formatNumber(rate, 0),
		indicator: { value: percent }
	}, "warp|" + index + "|" + rate + "|" + paused + "|" + status);
}

async function safeSend(context, action, parameters) {
	try {
		const result = await client.send(action, parameters);
		if (!result.success) {
			console.warn("KSP Flight Control:", action, "failed:", result.error, result.message);
			$SD.showAlert(context);
			return null;
		}
		return result;
	} catch (cause) {
		console.warn("KSP Flight Control:", action, "failed:", cause);
		$SD.showAlert(context);
		hub.refresh();
		return null;
	}
}

function setThrottle(entry, value) {
	entry.optimisticThrottle = KspUtil.clamp01(value);
	renderThrust(entry, hub.getSnapshot(), hub.getStatus());

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
	renderEntry(entry, hub.getSnapshot(), hub.getStatus(), hub.getError());
});

ActionGroupToggle.onKeyUp(function (jsn) {
	const entry = entryFor(jsn.context);
	const group = entry && entry.settings.action;
	if (!group || group === "None") {
		$SD.showAlert(jsn.context);
		return;
	}

	safeSend(jsn.context, "setActionGroupState", { ID: group }).then(function (result) {
		if (result) {
			hub.refresh();
		}
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
	safeSend(jsn.context, "stage", {}).then(function (result) {
		if (result) {
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
	renderEntry(entry, hub.getSnapshot(), hub.getStatus(), hub.getError());
});

SasAction.onKeyUp(function (jsn) {
	const entry = entryFor(jsn.context);
	if (!entry) {
		return;
	}

	const desired = entry.settings.mode || "StabilityAssist";
	const snapshot = hub.getSnapshot();
	const sas = snapshot ? snapshot.sas : null;
	const engagedHere = Boolean(sas && sas.enabled && sas.mode === desired);
	const parameters = engagedHere ? { Enabled: false } : { Enabled: true, Mode: desired };

	safeSend(jsn.context, "setSas", parameters).then(function (result) {
		if (result) {
			hub.refresh();
		}
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
	renderEntry(entry, hub.getSnapshot(), hub.getStatus(), hub.getError());
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

	const snapshot = hub.getSnapshot();
	const current = entry.optimisticThrottle !== null && entry.optimisticThrottle !== undefined
		? entry.optimisticThrottle
		: snapshot
			? snapshot.throttle
			: 0;

	// Big flicks jump 5%, gentle nudges move 1% for fine trim.
	const step = Math.abs(ticks) > 1 ? 0.05 : 0.01;
	setThrottle(entry, Math.round(KspUtil.clamp01(current + ticks * step) * 100) / 100);
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

	const snapshot = hub.getSnapshot();
	const warp = snapshot ? snapshot.timeWarp : null;
	if (!warp) {
		$SD.showAlert(jsn.context);
		return;
	}

	const next = KspUtil.clamp(warp.index + ticks, 0, Math.max(0, warp.maxIndex));
	if (next === warp.index) {
		return;
	}

	safeSend(jsn.context, "setTimeWarp", { Index: next }).then(function (result) {
		if (result) {
			hub.refresh();
		}
	});
});

TimeWarpDial.onDialPress(function (jsn) {
	if (jsn.payload && jsn.payload.pressed) {
		return;
	}
	safeSend(jsn.context, "togglePause", {}).then(function (result) {
		if (result) {
			hub.refresh();
		}
	});
});

TimeWarpDial.onTouchTap(function (jsn) {
	if (jsn.payload && jsn.payload.pressed) {
		return;
	}
	safeSend(jsn.context, "setTimeWarp", { Index: 0 }).then(function (result) {
		if (result) {
			hub.refresh();
		}
	});
});
