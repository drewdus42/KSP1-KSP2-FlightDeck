const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const util = require("../src/com.drewdus.kspflightcontrol.sdPlugin/js/util.js");

const pluginRoot = path.join(__dirname, "..", "src", "com.drewdus.kspflightcontrol.sdPlugin");
const manifest = JSON.parse(fs.readFileSync(path.join(pluginRoot, "manifest.json"), "utf8"));

const IMAGE_EXTENSIONS = [".svg", ".png", ".jpg", ".jpeg"];

function assetExists(relativePath) {
	const absolute = path.join(pluginRoot, relativePath);
	if (fs.existsSync(absolute) && fs.statSync(absolute).isFile()) {
		return true;
	}
	return IMAGE_EXTENSIONS.some((extension) => fs.existsSync(absolute + extension));
}

const GROUPS = [
	"Abort", "Brakes", "Gear", "Lights", "RCS", "SAS", "SolarPanels", "RadiatorPanels", "Science",
	"Custom01", "Custom02", "Custom03", "Custom04", "Custom05",
	"Custom06", "Custom07", "Custom08", "Custom09", "Custom10"
];

test("every action group has both On and Off artwork", () => {
	for (const group of GROUPS) {
		for (const state of [true, false]) {
			const imagePath = util.actionGroupImage(group, state);
			assert.ok(assetExists(imagePath), "missing " + imagePath);
		}
	}
});

test("every SAS mode with artwork resolves to a real On and Off image", () => {
	const modesWithArt = Object.keys(util.SAS_ART);
	assert.ok(modesWithArt.length >= 9);

	for (const mode of modesWithArt) {
		for (const on of [true, false]) {
			const imagePath = util.sasImage(mode, on);
			assert.ok(assetExists(imagePath), "missing " + imagePath);
		}
	}
});

test("SAS artwork covers every KSP mode except the ones without drawings", () => {
	const expected = ["StabilityAssist", "Prograde", "Retrograde", "Normal", "Antinormal", "RadialIn", "RadialOut", "Target", "AntiTarget"];
	assert.deepEqual(Object.keys(util.SAS_ART).sort(), expected.sort());
	assert.equal(util.sasImage("Maneuver", true), null, "Maneuver must fall back to a drawn tile");
	assert.equal(util.sasImage("Unknown", true), null);
});

test("SAS artwork files are the unlabelled set the user maintains", () => {
	const mode = util.sasImage("Prograde", true);
	assert.equal(mode, "Buttons/SAS/SAS Modes Unlabelled/SASpgOn.jpg");
	assert.equal(util.sasImage("StabilityAssist", false), "Buttons/SAS/SAS Modes Unlabelled/SASlockOff.jpg");
	assert.equal(util.sasImage("Target", true), "Buttons/SAS/SAS Modes Unlabelled/SASupOn.jpg");
	assert.equal(util.sasImage("AntiTarget", false), "Buttons/SAS/SAS Modes Unlabelled/SASdownOff.jpg");
	assert.ok(!mode.includes("Labelled"), "the plugin must caption unlabelled art, not use baked-in text");
});

test("shared panel and staging artwork exist", () => {
	assert.ok(assetExists(util.ASSETS.panel), util.ASSETS.panel);
	assert.ok(assetExists(util.ASSETS.launch), util.ASSETS.launch);
	assert.ok(assetExists(util.ASSETS.stage), util.ASSETS.stage);
	assert.ok(assetExists(util.ASSETS.stageIdle), util.ASSETS.stageIdle);
	assert.ok(assetExists(util.ASSETS.serverDown), util.ASSETS.serverDown);
});

test("dial layouts use the touch bar artwork", () => {
	const expected = {
		"thrustLayout.json": "Buttons/TouchBar/Thrust_Static.jpg",
		"timeWarpLayout.json": "Buttons/TouchBar/TimeWarp_Static.jpg"
	};

	for (const file of Object.keys(expected)) {
		const layout = JSON.parse(fs.readFileSync(path.join(pluginRoot, "Layouts", file), "utf8"));
		const pixmap = layout.items.filter((item) => item.type === "pixmap")[0];
		assert.ok(pixmap, file + " has no pixmap");
		assert.equal(pixmap.value, expected[file]);
		assert.ok(assetExists(pixmap.value), "missing " + pixmap.value);
		assert.ok(assetExists(pixmap.value.replace(/\.(jpg|png)$/i, "@2x.png")), "missing @2x for " + pixmap.value);
	}
});

test("manifest icons and encoder art point at existing files", () => {
	for (const action of manifest.Actions) {
		assert.ok(assetExists(action.Icon), "missing icon " + action.Icon);
		if (action.Encoder) {
			assert.ok(assetExists(action.Encoder.Icon), "missing encoder icon " + action.Encoder.Icon);
			const layout = JSON.parse(fs.readFileSync(path.join(pluginRoot, action.Encoder.layout), "utf8"));
			for (const item of layout.items.filter((entry) => entry.type === "pixmap")) {
				assert.ok(assetExists(item.value), "missing layout pixmap " + item.value);
			}
		}
	}
});
