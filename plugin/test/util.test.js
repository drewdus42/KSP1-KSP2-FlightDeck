const test = require("node:test");
const assert = require("node:assert/strict");
const util = require("../src/com.drewdus.kspflightcontrol.sdPlugin/js/util.js");

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

test("formatWarpRate keeps the dial value short", () => {
	assert.equal(util.formatWarpRate(1), "×1");
	assert.equal(util.formatWarpRate(4), "×4");
	assert.equal(util.formatWarpRate(500), "×500");
	assert.equal(util.formatWarpRate(1000), "×1k");
	assert.equal(util.formatWarpRate(10000), "×10k");
	assert.equal(util.formatWarpRate(100000), "×100k");
	assert.equal(util.formatWarpRate(null), util.DASH);
	assert.ok(util.formatWarpRate(100000).length <= 5);
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

test("groupPressParameters asks for the state the key should land on", () => {
	// Pressing an OFF group turns it on, and vice versa: the plugin never relies on a
	// blind toggle in the mod, so a repeated press cannot desync the artwork.
	assert.deepEqual(util.groupPressParameters({ actionGroups: { Lights: false } }, "Lights"), { Group: "Lights", State: true });
	assert.deepEqual(util.groupPressParameters({ actionGroups: { Lights: true } }, "Lights"), { Group: "Lights", State: false });
	assert.deepEqual(util.groupPressParameters({ actionGroups: {} }, "Lights"), { Group: "Lights", State: true });
	assert.deepEqual(util.groupPressParameters(null, "Gear"), { Group: "Gear", State: true });
	// PascalCase snapshots from an older mod build normalise first, but tolerate raw keys too.
	assert.deepEqual(util.groupPressParameters(util.normalizeSnapshot({ actionGroups: { Gear: true } }), "Gear"), { Group: "Gear", State: false });
});

test("groupInvertParameters flips the key without firing the group", () => {
	// A long press only changes what the mod reports, so a drifted one-shot group can be resynced.
	assert.deepEqual(util.groupInvertParameters({ actionGroups: { Custom01: true } }, "Custom01"), { Group: "Custom01", State: false, LatchOnly: true });
	assert.deepEqual(util.groupInvertParameters({ actionGroups: { Custom01: false } }, "Custom01"), { Group: "Custom01", State: true, LatchOnly: true });
	assert.deepEqual(util.groupInvertParameters(null, "Custom01"), { Group: "Custom01", State: true, LatchOnly: true });
});

test("action group options follow the connected game's capabilities", () => {
	const ksp1 = util.actionGroupOptions({ actionGroups: ["Gear", "Science", "Custom01"] });
	assert.deepEqual(ksp1.map((option) => option.id), ["Custom01", "Gear", "Science"]);
	assert.equal(ksp1[0].label, "Custom 1");

	// KSP 2 adds a native Resource Scan group.
	const ksp2 = util.actionGroupOptions({ actionGroups: ["Gear", "ResourceScan"] });
	assert.deepEqual(ksp2.map((option) => option.id), ["Gear", "ResourceScan"]);
	assert.equal(ksp2[1].label, "Resource Scan");
});

test("action group options fall back to the full list with no mod connected", () => {
	const options = util.actionGroupOptions(null).map((option) => option.id);
	assert.ok(options.includes("Gear"));
	assert.ok(options.includes("ResourceScan"));
	assert.equal(options.length, 21);
});

test("telemetry fields respect capabilities", () => {
	const withoutDelta = util.telemetryFields(null, { deltaV: false }).map((field) => field.id);
	assert.ok(!withoutDelta.includes("deltaV"));
	assert.ok(withoutDelta.includes("altitude"));

	const full = util.telemetryFields(null, { deltaV: true }).map((field) => field.id);
	assert.ok(full.includes("deltaV"));
});

test("gameLabel names the game for the status key", () => {
	assert.equal(util.gameLabel("ksp2", "Kerbal Space Program 2"), "Kerbal Space Program 2");
	assert.equal(util.gameLabel("ksp1", null), "Kerbal Space Program");
	assert.equal(util.gameLabel("ksp2", null), "Kerbal Space Program 2");
	assert.equal(util.gameLabel(undefined, undefined), "");
});

test("sasPressParameters toggles SAS off when it holds this key's mode", () => {
	const utilSas = { enabled: true, mode: "StabilityAssist" };

	// Regression: pressing a Stability Assist key while SAS is engaged must switch SAS off,
	// not re-engage it.
	assert.deepEqual(util.sasPressParameters(utilSas, "StabilityAssist"), { Enabled: false });
	assert.deepEqual(util.sasPressParameters({ enabled: true, mode: "Prograde" }, "Prograde"), { Enabled: false });
	assert.deepEqual(util.sasPressParameters({ enabled: true, mode: "AntiTarget" }, "AntiTarget"), { Enabled: false });
});

test("sasPressParameters engages the key's mode when SAS is idle or in another mode", () => {
	assert.deepEqual(util.sasPressParameters({ enabled: false, mode: "StabilityAssist" }, "StabilityAssist"), { Enabled: true, Mode: "StabilityAssist" });
	assert.deepEqual(util.sasPressParameters({ enabled: true, mode: "Prograde" }, "StabilityAssist"), { Enabled: true, Mode: "StabilityAssist" });
	assert.deepEqual(util.sasPressParameters(null, "Retrograde"), { Enabled: true, Mode: "Retrograde" });
	assert.deepEqual(util.sasPressParameters(undefined, undefined), { Enabled: true, Mode: "StabilityAssist" });
});

test("SAS options cover every KSP autopilot mode", () => {
	assert.deepEqual(
		util.SAS_MODE_OPTIONS.map((option) => option.id),
		["StabilityAssist", "Prograde", "Retrograde", "Normal", "Antinormal", "RadialIn", "RadialOut", "Target", "AntiTarget", "Maneuver"]
	);
});
