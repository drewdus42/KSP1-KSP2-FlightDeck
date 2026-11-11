#!/usr/bin/env node
/**
 * End-to-end check of the plugin's client against a running mod (or the dev host).
 *
 *   cd mod && dotnet run --project tests/FlightDeck.DevHost -- --port 8080
 *   node plugin/test/live-check.js
 *
 * It uses the real js/api.js, js/state.js and js/util.js modules, so it validates the
 * exact code path the Stream Deck plugin runs.
 */
const api = require("../src/com.drewdus.kspflightcontrol.sdPlugin/js/api.js");
const state = require("../src/com.drewdus.kspflightcontrol.sdPlugin/js/state.js");
const util = require("../src/com.drewdus.kspflightcontrol.sdPlugin/js/util.js");

const host = process.env.KSP_HOST || "127.0.0.1";
const port = Number(process.env.KSP_PORT || 8080);

let failures = 0;

function check(name, condition, detail) {
	if (condition) {
		console.log("  PASS  " + name);
	} else {
		failures++;
		console.log("  FAIL  " + name + (detail === undefined ? "" : "  -> " + JSON.stringify(detail)));
	}
}

(async () => {
	const client = api.createClient({ host, port });
	const hub = state.createHub({ client, flightIntervalMs: 30, menuIntervalMs: 30, offlineIntervalMs: 30 });

	const health = await client.health();
	check("health ok", health.ok === true, health);
	check("health reports a loaded vessel", health.inFlight === true, health);

	const raw = await fetch(client.baseUrl() + "/api/state", { cache: "no-store" });
	check("state response allows CORS", raw.headers.get("access-control-allow-origin") === "*");

	const preflight = await fetch(client.baseUrl() + "/api", { method: "OPTIONS" });
	check("OPTIONS preflight is 204", preflight.status === 204, preflight.status);
	check("preflight allows POST", (preflight.headers.get("access-control-allow-methods") || "").includes("POST"));

	const body = await client.getState();
	const snapshot = body.data;
	check("snapshot has a vessel", snapshot.vessel && typeof snapshot.vessel.name === "string", snapshot.vessel);
	check("snapshot has resources", Object.keys(snapshot.resources || {}).length > 0);
	check("telemetry renders altitude", util.formatTelemetry("altitude", snapshot).value.endsWith("km"));

	let status = null;
	const unsubscribe = hub.subscribe((value, nextStatus) => { status = nextStatus; });
	await new Promise((resolve) => setTimeout(resolve, 250));
	check("polling hub sees the flight scene", status === state.STATUS.FLIGHT, status);
	unsubscribe();

	const toggled = await client.send("setActionGroupState", { ID: "Gear" });
	check("action group toggle succeeds", toggled.success === true, toggled);
	check("action group reports new state", typeof toggled.data.Status === "boolean", toggled.data);

	const queried = await client.send("getActionGroupState", { ID: "Gear" });
	check("action group state round-trips", queried.data.Status === toggled.data.Status, queried.data);

	const throttle = await client.send("setShipThrottle", { Throttle: 0.8 });
	check("throttle is applied", Math.abs(throttle.data.Throttle - 0.8) < 1e-6, throttle.data);

	const sas = await client.send("setSas", { Enabled: true, Mode: "Maneuver" });
	check("SAS mode is applied", sas.data.Mode === "Maneuver" && sas.data.Enabled === true, sas.data);

	const badGroup = await client.send("getActionGroupState", { ID: "Warp" });
	check("unknown action group is rejected", badGroup.success === false && badGroup.error === "unknown_action_group", badGroup);
	check("rejection uses HTTP 400", badGroup.status === 400, badGroup.status);

	const badMode = await client.send("setSasMode", { Mode: "sideways" });
	check("unknown SAS mode is rejected", badMode.error === "unknown_sas_mode", badMode);

	const badJson = await fetch(client.baseUrl() + "/api", {
		method: "POST",
		headers: { "Content-Type": "application/json" },
		body: "{oops"
	});
	check("malformed JSON is 400", badJson.status === 400, badJson.status);

	const unknownRoute = await fetch(client.baseUrl() + "/api/nope");
	check("unknown route is 404", unknownRoute.status === 404, unknownRoute.status);

	console.log(failures === 0 ? "\nLIVE CHECK PASSED" : "\nLIVE CHECK FAILED (" + failures + " failed)");
	process.exit(failures === 0 ? 0 : 1);
})().catch((error) => {
	console.error("\nLIVE CHECK FAILED: " + error.message);
	console.error("Is the mod (or the dev host) listening on " + host + ":" + port + "?");
	process.exit(1);
});
