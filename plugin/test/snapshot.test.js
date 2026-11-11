const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const util = require("../src/com.drewdus.kspflightcontrol.sdPlugin/js/util.js");

// Captured from the mod (KSPFlightDeck 1.0.0) so the plugin is always tested against the
// exact wire format the game produces, not a hand written approximation.
const snapshot = JSON.parse(fs.readFileSync(path.join(__dirname, "fixtures", "snapshot.json"), "utf8"));

test("snapshot fixture is the mod's real shape", () => {
	assert.ok(snapshot.vessel && snapshot.vessel.name);
	assert.ok(snapshot.staging.CurrentStage !== undefined, "mod sends PascalCase staging keys");
	assert.ok(snapshot.timeWarp.Rate !== undefined, "mod sends PascalCase timeWarp keys");
	assert.ok(snapshot.sas.Enabled !== undefined, "mod sends PascalCase sas keys");
	assert.ok(snapshot.orbit.altitude !== undefined);
	assert.ok(snapshot.actionGroups.Gear !== undefined);
});

test("normalizer exposes one spelling for every section", () => {
	const normalized = util.normalizeSnapshot(snapshot);

	assert.equal(normalized.staging.currentStage, snapshot.staging.CurrentStage);
	assert.equal(normalized.staging.stageCount, snapshot.staging.StageCount);
	assert.equal(normalized.staging.prelaunch, snapshot.staging.Prelaunch);
	assert.equal(normalized.timeWarp.rate, snapshot.timeWarp.Rate);
	assert.equal(normalized.timeWarp.maxIndex, snapshot.timeWarp.MaxIndex);
	assert.equal(normalized.sas.enabled, snapshot.sas.Enabled);
	assert.equal(normalized.sas.mode, snapshot.sas.Mode);
	assert.equal(normalized.vessel.name, snapshot.vessel.name);
	assert.equal(normalized.orbit.apoapsis, snapshot.orbit.apoapsis);
	assert.equal(normalized.throttle, snapshot.throttle);
});

test("normalizer is idempotent and tolerates missing sections", () => {
	const once = util.normalizeSnapshot(snapshot);
	const twice = util.normalizeSnapshot(once);
	assert.deepEqual(twice, once);

	const empty = util.normalizeSnapshot({});
	assert.equal(empty.staging.currentStage, null);
	assert.equal(empty.timeWarp.rate, null);
	assert.equal(empty.sas.enabled, false);
	assert.deepEqual(empty.actionGroups, {});
	assert.equal(util.normalizeSnapshot(null), null);
});

test("every telemetry field renders real values from the fixture", () => {
	const expectations = {
		altitude: /km$/,
		radarAltitude: /km$/,
		apoapsis: /km$/,
		periapsis: /km$/,
		orbitalSpeed: /m\/s$/,
		surfaceSpeed: /m\/s$/,
		verticalSpeed: /m\/s$/,
		inclination: /^\d+\.\d$/,
		period: /^\d\d:\d\d:\d\d$/,
		timeToApoapsis: /^\d\d:\d\d:\d\d$/,
		timeToPeriapsis: /^\d\d:\d\d:\d\d$/,
		deltaV: /^\d[\d,]*$/,
		throttle: /^\d+%$/,
		warp: /^×\d/,
		stage: /^\d+ \/ \d+$/
	};

	Object.keys(expectations).forEach((field) => {
		const result = util.formatTelemetry(field, snapshot);
		assert.match(result.value, expectations[field], field + " -> " + JSON.stringify(result));
		assert.notEqual(result.value, util.DASH, field + " must not be empty");
	});

	assert.equal(util.formatTelemetry("stage", snapshot).value, snapshot.staging.CurrentStage + " / " + snapshot.staging.StageCount);
	assert.equal(util.formatTelemetry("warp", snapshot).value, "×" + snapshot.timeWarp.Rate);
});

test("staging label from the mod wins over the prelaunch flag", () => {
	// The mod reports Label so the key can say Launch on the pad and Stage afterwards,
	// including the "already launched but not yet staged" case.
	const normalized = util.normalizeSnapshot({ staging: { CurrentStage: 2, StageCount: 2, LastStage: 2, Prelaunch: true, Label: "Launch" } });
	assert.equal(normalized.staging.label, "Launch");
	assert.equal(normalized.staging.lastStage, 2);

	const staged = util.normalizeSnapshot({ staging: { CurrentStage: 1, StageCount: 2, LastStage: 2, Prelaunch: false, Label: "Stage" } });
	assert.equal(staged.staging.label, "Stage");
});

test("landed vessels report no usable orbit", () => {
	// Captured shape when KSP has no real orbit: Pe is negative and Ap equals altitude.
	const landed = util.normalizeSnapshot({
		orbit: { available: false, landed: true, altitude: 71, periapsis: -598387, apoapsis: 71 }
	});
	assert.equal(landed.orbit.available, false);
	assert.equal(util.formatTelemetry("altitude", landed).value, "71 m");
});

test("resource readouts use the fixture", () => {
	Object.keys(snapshot.resources).forEach((name) => {
		const result = util.formatTelemetry("resource:" + name, snapshot);
		assert.notEqual(result.value, util.DASH, name);
		assert.ok(result.unit.indexOf("/") > 0, name);
	});
});
