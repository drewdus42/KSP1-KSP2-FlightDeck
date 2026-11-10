/**
 * Pure helpers shared by the plugin and its tests. No Stream Deck or DOM access here.
 */
(function (root, factory) {
	const api = factory();
	if (typeof module === "object" && module.exports) {
		module.exports = api;
	} else {
		root.KspUtil = api;
	}
})(typeof self !== "undefined" ? self : this, function () {
	const DASH = "—";

	const TELEMETRY_FIELDS = [
		{ id: "altitude", label: "Altitude" },
		{ id: "radarAltitude", label: "Radar Altitude" },
		{ id: "apoapsis", label: "Apoapsis" },
		{ id: "periapsis", label: "Periapsis" },
		{ id: "orbitalSpeed", label: "Orbital Speed" },
		{ id: "surfaceSpeed", label: "Surface Speed" },
		{ id: "verticalSpeed", label: "Vertical Speed" },
		{ id: "inclination", label: "Inclination" },
		{ id: "period", label: "Orbital Period" },
		{ id: "timeToApoapsis", label: "Time to Apoapsis" },
		{ id: "timeToPeriapsis", label: "Time to Periapsis" },
		{ id: "deltaV", label: "Delta-V (vac)" },
		{ id: "throttle", label: "Throttle" },
		{ id: "warp", label: "Time Warp" },
		{ id: "stage", label: "Stage" }
	];

	const SAS_LABELS = {
		StabilityAssist: "STAB",
		Prograde: "PRGD",
		Retrograde: "RTGD",
		Normal: "NRML",
		Antinormal: "ANTI",
		RadialIn: "RAD-",
		RadialOut: "RAD+",
		Target: "TGT",
		AntiTarget: "ATGT",
		Maneuver: "MNVR"
	};

	const SAS_MODE_OPTIONS = [
		{ id: "StabilityAssist", label: "Stability Assist" },
		{ id: "Prograde", label: "Prograde" },
		{ id: "Retrograde", label: "Retrograde" },
		{ id: "Normal", label: "Normal" },
		{ id: "Antinormal", label: "Antinormal" },
		{ id: "RadialIn", label: "Radial In" },
		{ id: "RadialOut", label: "Radial Out" },
		{ id: "Target", label: "Target" },
		{ id: "AntiTarget", label: "Anti-Target" },
		{ id: "Maneuver", label: "Maneuver Node" }
	];

	function clamp(value, min, max) {
		const number = Number(value);
		if (!isFiniteNumber(number)) {
			return min;
		}
		return Math.min(max, Math.max(min, number));
	}

	function clamp01(value) {
		return clamp(value, 0, 1);
	}

	function isFiniteNumber(value) {
		return typeof value === "number" && isFinite(value);
	}

	function readNumber(value) {
		if (typeof value === "number") {
			return isFinite(value) ? value : null;
		}
		if (typeof value === "string" && value.trim().length > 0) {
			const parsed = Number(value);
			return isFinite(parsed) ? parsed : null;
		}
		return null;
	}

	function formatNumber(value, digits) {
		const number = readNumber(value);
		if (number === null) {
			return DASH;
		}
		const places = typeof digits === "number" ? digits : 1;
		return number.toFixed(places);
	}

	function formatDistance(meters, digits) {
		const value = readNumber(meters);
		if (value === null) {
			return DASH;
		}

		const abs = Math.abs(value);
		const places = typeof digits === "number" ? digits : 1;
		if (abs < 1000) {
			return formatNumber(value, 0) + " m";
		}
		if (abs < 1000000) {
			return formatNumber(value / 1000, places) + " km";
		}
		if (abs < 1000000000) {
			return formatNumber(value / 1000000, places) + " Mm";
		}
		return formatNumber(value / 1000000000, places) + " Gm";
	}

	function formatSpeed(metersPerSecond, digits) {
		const value = readNumber(metersPerSecond);
		if (value === null) {
			return DASH;
		}
		if (Math.abs(value) >= 10000) {
			return formatNumber(value / 1000, typeof digits === "number" ? digits : 1) + " km/s";
		}
		return formatNumber(value, typeof digits === "number" ? digits : 1) + " m/s";
	}

	function pad(value) {
		return String(value).padStart(2, "0");
	}

	function formatDuration(seconds) {
		const value = readNumber(seconds);
		if (value === null) {
			return DASH;
		}

		const sign = value < 0 ? "-" : "";
		let remaining = Math.floor(Math.abs(value));
		const days = Math.floor(remaining / 86400);
		remaining -= days * 86400;
		const hours = Math.floor(remaining / 3600);
		remaining -= hours * 3600;
		const minutes = Math.floor(remaining / 60);
		const secs = remaining - minutes * 60;

		if (days > 0) {
			return sign + days + "d " + pad(hours) + ":" + pad(minutes) + ":" + pad(secs);
		}
		return sign + pad(hours) + ":" + pad(minutes) + ":" + pad(secs);
	}

	function formatPercent(fraction) {
		const value = readNumber(fraction);
		if (value === null) {
			return DASH;
		}
		return Math.round(value * 100) + "%";
	}

	function prettyGroupName(name) {
		if (!name) {
			return "None";
		}

		const custom = /^Custom0?(\d)$/i.exec(name);
		if (custom) {
			return "Custom " + custom[1];
		}

		const spaced = {
			RCS: "RCS",
			SAS: "SAS",
			SolarPanels: "Solar Panels",
			RadiatorPanels: "Radiator Panels",
			Antennas: "Antennas",
			Science: "Science",
			Abort: "Abort",
			Brakes: "Brakes",
			Gear: "Gear",
			Lights: "Lights"
		};

		return spaced[name] || name;
	}

	function sasBadge(mode) {
		return SAS_LABELS[mode] || (mode ? mode.slice(0, 4).toUpperCase() : "SAS");
	}

	function sasModeLabel(mode) {
		const match = SAS_MODE_OPTIONS.filter(function (option) {
			return option.id === mode;
		})[0];
		return match ? match.label : mode || "Stability Assist";
	}

	function telemetryFields(snapshot) {
		const fields = TELEMETRY_FIELDS.slice();
		const resources = (snapshot && snapshot.resources) || {};
		Object.keys(resources)
			.sort()
			.forEach(function (name) {
				fields.push({ id: "resource:" + name, label: name.replace(/([a-z])([A-Z])/g, "$1 $2") });
			});
		return fields;
	}

	function telemetryFieldLabel(fieldId) {
		if (!fieldId) {
			return "Altitude";
		}
		if (fieldId.indexOf("resource:") === 0) {
			return fieldId.slice("resource:".length);
		}
		const match = TELEMETRY_FIELDS.filter(function (field) {
			return field.id === fieldId;
		})[0];
		return match ? match.label : fieldId;
	}

	/**
	 * Turns one field of a /api/state snapshot into a key label, value and unit.
	 */
	function formatTelemetry(fieldId, snapshot) {
		const label = telemetryFieldLabel(fieldId);
		const result = { label: label.toUpperCase(), value: DASH, unit: "" };

		if (!snapshot) {
			return result;
		}

		if (fieldId && fieldId.indexOf("resource:") === 0) {
			const name = fieldId.slice("resource:".length);
			const resource = (snapshot.resources || {})[name];
			if (!resource) {
				return result;
			}
			result.value = Math.round(Number(resource.amount) || 0).toLocaleString();
			result.unit = " / " + Math.round(Number(resource.max) || 0).toLocaleString();
			return result;
		}

		const orbit = snapshot.orbit || {};
		switch (fieldId) {
			case "altitude":
				result.value = formatDistance(orbit.altitude);
				break;
			case "radarAltitude":
				result.value = formatDistance(orbit.radarAltitude);
				break;
			case "apoapsis":
				result.value = formatDistance(orbit.apoapsis);
				break;
			case "periapsis":
				result.value = formatDistance(orbit.periapsis);
				break;
			case "orbitalSpeed":
				result.value = formatSpeed(orbit.orbitalSpeed);
				break;
			case "surfaceSpeed":
				result.value = formatSpeed(orbit.surfaceSpeed);
				break;
			case "verticalSpeed":
				result.value = formatSpeed(orbit.verticalSpeed);
				break;
			case "inclination":
				if (isFiniteNumber(orbit.inclination)) {
					result.value = formatNumber((orbit.inclination * 180) / Math.PI, 1);
					result.unit = "°";
				}
				break;
			case "period":
				result.value = formatDuration(orbit.period);
				break;
			case "timeToApoapsis":
				result.value = formatDuration(orbit.timeToApoapsis);
				break;
			case "timeToPeriapsis":
				result.value = formatDuration(orbit.timeToPeriapsis);
				break;
			case "deltaV":
				if (snapshot.deltaV && snapshot.deltaV.available) {
					result.value = Math.round(Number(snapshot.deltaV.vacuum) || 0).toLocaleString();
					result.unit = "m/s";
				}
				break;
			case "throttle":
				result.value = formatPercent(snapshot.throttle);
				break;
			case "warp":
				if (snapshot.timeWarp) {
					result.value = "×" + formatNumber(snapshot.timeWarp.rate, 0);
					if (snapshot.timeWarp.paused) {
						result.unit = "paused";
					}
				}
				break;
			case "stage":
				if (snapshot.staging) {
					result.value = String(snapshot.staging.currentStage) + " / " + String(snapshot.staging.stageCount);
				}
				break;
			default:
				break;
		}

		return result;
	}

	function actionGroupImage(group, state) {
		if (!group || group === "None") {
			return null;
		}
		return "Buttons/ActionGroup/" + (state ? "True" : "False") + "/" + group + ".png";
	}

	return {
		DASH: DASH,
		TELEMETRY_FIELDS: TELEMETRY_FIELDS,
		SAS_MODE_OPTIONS: SAS_MODE_OPTIONS,
		clamp: clamp,
		clamp01: clamp01,
		isFiniteNumber: isFiniteNumber,
		readNumber: readNumber,
		formatNumber: formatNumber,
		formatDistance: formatDistance,
		formatSpeed: formatSpeed,
		formatDuration: formatDuration,
		formatPercent: formatPercent,
		prettyGroupName: prettyGroupName,
		sasBadge: sasBadge,
		sasModeLabel: sasModeLabel,
		telemetryFields: telemetryFields,
		telemetryFieldLabel: telemetryFieldLabel,
		formatTelemetry: formatTelemetry,
		actionGroupImage: actionGroupImage
	};
});
