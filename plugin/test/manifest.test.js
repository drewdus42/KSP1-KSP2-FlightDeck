const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const pluginRoot = path.join(__dirname, "..", "src", "com.drewdus.kspflightcontrol.sdPlugin");
const manifest = JSON.parse(fs.readFileSync(path.join(pluginRoot, "manifest.json"), "utf8"));

const IMAGE_EXTENSIONS = [".svg", ".png", ".jpg", ".jpeg"];

function existsWithOptionalExtension(relativePath) {
	const base = path.join(pluginRoot, relativePath);
	if (fs.existsSync(base) && fs.statSync(base).isFile()) {
		return true;
	}
	return IMAGE_EXTENSIONS.some((extension) => fs.existsSync(base + extension));
}

test("manifest is well formed", () => {
	assert.equal(manifest.SDKVersion, 2);
	assert.equal(manifest.Name, "KSP Flight Control");
	assert.ok(manifest.Version.match(/^\d+\.\d+\.\d+$/));
	assert.equal(manifest.Author, "Drewdus");
	assert.equal(manifest.Category, "KSP Flight Control");
	assert.ok(fs.existsSync(path.join(pluginRoot, manifest.CodePath)));
});

test("every action has a namespaced unique UUID", () => {
	const uuids = manifest.Actions.map((action) => action.UUID);
	assert.equal(new Set(uuids).size, uuids.length);

	for (const uuid of uuids) {
		assert.ok(uuid.startsWith("com.drewdus.kspflightcontrol."), uuid);
	}
});

test("all manifest icons and state images exist", () => {
	for (const action of manifest.Actions) {
		assert.ok(existsWithOptionalExtension(action.Icon), "missing icon: " + action.Icon);

		for (const state of action.States || []) {
			assert.ok(existsWithOptionalExtension(state.Image), "missing state image: " + state.Image);
		}

		if (action.Encoder && action.Encoder.layout) {
			assert.ok(fs.existsSync(path.join(pluginRoot, action.Encoder.layout)), "missing layout: " + action.Encoder.layout);
		}

		if (action.PropertyInspectorPath) {
			assert.ok(fs.existsSync(path.join(pluginRoot, action.PropertyInspectorPath)), "missing inspector: " + action.PropertyInspectorPath);
		}
	}

	assert.ok(existsWithOptionalExtension(manifest.Icon));
	assert.ok(existsWithOptionalExtension(manifest.CategoryIcon));
});

test("layouts reference existing assets and keys used by app.js", () => {
	const action = manifest.Actions.find((entry) => entry.Encoder);
	assert.ok(action);

	for (const encoderAction of manifest.Actions.filter((entry) => entry.Encoder)) {
		const layoutPath = path.join(pluginRoot, encoderAction.Encoder.layout);
		const layout = JSON.parse(fs.readFileSync(layoutPath, "utf8"));
		assert.equal(layout.id, path.basename(encoderAction.Encoder.layout, ".json"));

		for (const item of layout.items) {
			assert.ok(["text", "pixmap", "gbar"].includes(item.type), item.type);
			assert.ok(Array.isArray(item.rect) && item.rect.length === 4, "rect for " + item.key);
			if (item.type === "pixmap") {
				assert.ok(existsWithOptionalExtension(item.value), "missing pixmap: " + item.value);
			}
		}

		const keys = layout.items.map((item) => item.key);
		assert.ok(keys.includes("title"));
		assert.ok(keys.includes("value"));
		assert.ok(keys.includes("indicator"));
	}
});

test("action names are localized in en.json and de.json", () => {
	const languages = ["en.json", "de.json"];
	for (const language of languages) {
		const strings = JSON.parse(fs.readFileSync(path.join(pluginRoot, language), "utf8"));
		for (const action of manifest.Actions) {
			assert.ok(strings[action.UUID], language + " is missing " + action.UUID);
			assert.ok(strings[action.UUID].Name, language + " is missing the name of " + action.UUID);
		}
	}
});

test("app.html loads every script it needs in order", () => {
	const html = fs.readFileSync(path.join(pluginRoot, "app.html"), "utf8");
	const scripts = [...html.matchAll(/<script src="([^"]+)"><\/script>/g)].map((match) => match[1]);

	for (const script of scripts) {
		assert.ok(fs.existsSync(path.join(pluginRoot, script)), "missing script: " + script);
	}

	const order = ["libs/js/stream-deck.js", "libs/js/action.js", "js/util.js", "js/api.js", "js/state.js", "js/images.js", "app.js"];
	let lastIndex = -1;
	for (const required of order) {
		const index = scripts.indexOf(required);
		assert.ok(index > lastIndex, required + " is missing or out of order");
		lastIndex = index;
	}
});

test("property inspectors reference existing scripts and styles", () => {
	const inspectorDir = path.join(pluginRoot, "actions", "template", "property-inspector");
	const pages = fs.readdirSync(inspectorDir).filter((file) => file.endsWith(".html"));
	assert.ok(pages.length >= 5);

	for (const page of pages) {
		const html = fs.readFileSync(path.join(inspectorDir, page), "utf8");
		const references = [
			...[...html.matchAll(/<script src="([^"]+)"><\/script>/g)].map((match) => match[1]),
			...[...html.matchAll(/<link rel="stylesheet" href="([^"]+)"/g)].map((match) => match[1])
		];

		for (const reference of references) {
			assert.ok(fs.existsSync(path.resolve(inspectorDir, reference)), page + " -> " + reference);
		}
	}
});

test("manifest declares the actions app.js implements", () => {
	const source = fs.readFileSync(path.join(pluginRoot, "app.js"), "utf8");
	for (const action of manifest.Actions) {
		const suffix = action.UUID.split(".").pop();
		assert.ok(source.includes("PLUGIN_UUID + \"." + suffix + "\""), "app.js has no wiring for " + action.UUID);
	}
});
