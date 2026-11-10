const test = require("node:test");
const assert = require("node:assert/strict");
const util = require("../src/com.novistrum.kspflightcontrol.sdPlugin/js/util.js");

test("clamp keeps values inside the range", () => {
	assert.equal(util.clamp(0.5, 0, 1), 0.5);
	assert.equal(util.clamp(-3, 0, 1), 0);
	assert.equal(util.clamp(9, 0, 1), 1);
	assert.equal(util.clamp("0.25", 0, 1), 0.25);
	assert.equal(util.clamp(NaN, 0, 1), 0);
});

test("formatDistance scales units", () => {
	assert.equal(util.formatDistance(850), "850 m");
	assert.equal(util.formatDistance(12345), "12.3 km");
	assert.equal(util.formatDistance(2500000), "2.5 Mm");
	assert.equal(util.formatDistance(null), util.DASH);
	assert.equal(util.formatDistance(NaN), util.DASH);
});

test("formatDuration renders clock strings", () => {
	assert.equal(util.formatDuration(0), "00:00:00");
	assert.equal(util.formatDuration(3725), "01:02:05");
	assert.equal(util.formatDuration(90061), "1d 01:01:01");
	assert.equal(util.formatDuration(-15), "-00:00:15");
	assert.equal(util.formatDuration(undefined), util.DASH);
});

test("formatSpeed switches to km/s for high speeds", () => {
	assert.equal(util.formatSpeed(120.4), "120.4 m/s");
	assert.equal(util.formatSpeed(2350), "2350.0 m/s");
	assert.equal(util.formatSpeed(22500), "22.5 km/s");
});

test("telemetry understands the mod snapshot shape", () => {
	const snapshot = {
		throttle: 0.42,
		staging: { currentStage: 4, stageCount: 7 },
		timeWarp: { index: 2, rate: 10, paused: true },
		deltaV: { available: true, vacuum: 3456.7 },
		orbit: {
			altitude: 84210.5,
			radarAltitude: 83000,
			apoapsis: 120000,
			periapsis: 79000,
			orbitalSpeed: 2280.4,
			inclination: Math.PI / 4,
			period: 3600,
			timeToApoapsis: 900,
			timeToPeriapsis: -45
		},
		resources: {
			ElectricCharge: { amount: 512.4, max: 1000, fraction: 0.5124 }
		}
	};

	assert.equal(util.formatTelemetry("altitude", snapshot).value, "84.2 km");
	assert.equal(util.formatTelemetry("apoapsis", snapshot).value, "120.0 km");
	assert.equal(util.formatTelemetry("inclination", snapshot).value, "45.0");
	assert.equal(util.formatTelemetry("inclination", snapshot).unit, "°");
	assert.equal(util.formatTelemetry("period", snapshot).value, "01:00:00");
	assert.equal(util.formatTelemetry("timeToPeriapsis", snapshot).value, "-00:00:45");
	assert.equal(util.formatTelemetry("deltaV", snapshot).value, "3,457");
	assert.equal(util.formatTelemetry("throttle", snapshot).value, "42%");
	assert.equal(util.formatTelemetry("warp", snapshot).value, "×10");
	assert.equal(util.formatTelemetry("warp", snapshot).unit, "paused");
	assert.equal(util.formatTelemetry("stage", snapshot).value, "4 / 7");
	assert.equal(util.formatTelemetry("resource:ElectricCharge", snapshot).value, "512");
});

test("telemetry copes with a missing snapshot", () => {
	const result = util.formatTelemetry("apoapsis", null);
	assert.equal(result.value, util.DASH);
	assert.equal(result.label, "APOAPSIS");
});

test("resource fields appear in the property inspector list", () => {
	const fields = util.telemetryFields({ resources: { LiquidFuel: { amount: 1, max: 2 } } });
	const ids = fields.map((field) => field.id);
	assert.ok(ids.includes("altitude"));
	assert.ok(ids.includes("resource:LiquidFuel"));
});

test("pretty group names and SAS badges", () => {
	assert.equal(util.prettyGroupName("Custom03"), "Custom 3");
	assert.equal(util.prettyGroupName("SolarPanels"), "Solar Panels");
	assert.equal(util.prettyGroupName("None"), "None");
	assert.equal(util.sasBadge("Prograde"), "PRGD");
	assert.equal(util.sasBadge("WeirdMode"), "WEIR");
	assert.equal(util.sasModeLabel("AntiTarget"), "Anti-Target");
});

test("action group image paths follow the asset layout", () => {
	assert.equal(util.actionGroupImage("Gear", true), "Buttons/ActionGroup/True/Gear.png");
	assert.equal(util.actionGroupImage("Gear", false), "Buttons/ActionGroup/False/Gear.png");
	assert.equal(util.actionGroupImage("None", true), null);
});

test("SAS options cover every KSP autopilot mode", () => {
	assert.deepEqual(
		util.SAS_MODE_OPTIONS.map((option) => option.id),
		["StabilityAssist", "Prograde", "Retrograde", "Normal", "Antinormal", "RadialIn", "RadialOut", "Target", "AntiTarget", "Maneuver"]
	);
});
