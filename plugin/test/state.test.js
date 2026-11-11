const test = require("node:test");
const assert = require("node:assert/strict");
const http = require("node:http");
const api = require("../src/com.drewdus.kspflightcontrol.sdPlugin/js/api.js");
const state = require("../src/com.drewdus.kspflightcontrol.sdPlugin/js/state.js");

function startFakeMod() {
	const fake = {
		inFlight: true,
		game: "ksp2",
		gameName: "Kerbal Space Program 2",
		capabilities: {
			actionGroups: ["Gear", "Lights", "ResourceScan"],
			sasModes: ["StabilityAssist", "Prograde"],
			virtualActionGroups: [],
			resources: true,
			deltaV: true,
			orbit: true,
			staging: true,
			timeWarp: true,
			pause: true
		},
		requests: [],
		snapshot: {
			vessel: { name: "Test Kerman", situation: "ORBITING", situationString: "Orbiting Kerbin", body: "Kerbin" },
			throttle: 0.3,
			sas: { enabled: true, mode: "Prograde" },
			actionGroups: { Gear: true, Lights: false },
			staging: { currentStage: 2, stageCount: 5, prelaunch: false },
			timeWarp: { index: 0, rate: 1, maxIndex: 8, paused: false },
			paused: false,
			timestamp: 1
		}
	};

	const server = http.createServer((request, response) => {
		const chunks = [];
		request.on("data", (chunk) => chunks.push(chunk));
		request.on("end", () => {
			const body = chunks.length > 0 ? Buffer.concat(chunks).toString("utf8") : "";
			fake.requests.push({ method: request.method, url: request.url, body: body });

			response.setHeader("Content-Type", "application/json");
			response.setHeader("Access-Control-Allow-Origin", "*");

			if (request.method === "GET" && request.url === "/api/health") {
				response.end(JSON.stringify({
					ok: true,
					mod: "KSPFlightDeck",
					version: "1.0.0",
					game: fake.game,
					gameName: fake.gameName,
					capabilities: fake.capabilities,
					inFlight: fake.inFlight
				}));
				return;
			}

			if (request.method === "GET" && request.url === "/api/state") {
				const state = fake.inFlight
					? { ok: true, game: fake.game, gameName: fake.gameName, capabilities: fake.capabilities, data: fake.snapshot }
					: { ok: false, error: "not_in_flight", game: fake.game, gameName: fake.gameName };
				response.end(JSON.stringify(state));
				return;
			}

			if (request.method === "POST" && request.url === "/api") {
				const parsed = body ? JSON.parse(body) : {};
				response.end(JSON.stringify({
					ID: parsed.ID,
					Action: parsed.Action,
					Success: true,
					Data: { Status: false, Group: parsed.parameters && parsed.parameters.ID }
				}));
				return;
			}

			response.statusCode = 404;
			response.end(JSON.stringify({ ok: false, error: "not_found" }));
		});
	});

	return new Promise((resolve) => {
		server.listen(0, "127.0.0.1", () => {
			resolve({ server: server, fake: fake, port: server.address().port });
		});
	});
}

test("client reads the state snapshot", async () => {
	const { server, port, fake } = await startFakeMod();
	try {
		const client = api.createClient({ host: "127.0.0.1", port: port, discoverPorts: [] });
		const body = await client.getState();

		assert.equal(body.ok, true);
		assert.equal(body.data.vessel.name, "Test Kerman");
		assert.equal(fake.requests[0].url, "/api/state");
	} finally {
		server.close();
	}
});

test("client sends actions in the legacy envelope", async () => {
	const { server, port, fake } = await startFakeMod();
	try {
		const client = api.createClient({ host: "127.0.0.1", port: port, discoverPorts: [] });
		const result = await client.send("setActionGroupState", { ID: "Gear" });

		assert.equal(result.success, true);
		assert.equal(result.data.Group, "Gear");
		assert.equal(result.data.Status, false);

		const posted = JSON.parse(fake.requests[0].body);
		assert.equal(posted.Action, "setActionGroupState");
		assert.deepEqual(posted.parameters, { ID: "Gear" });
		assert.equal(typeof posted.ID, "string");
	} finally {
		server.close();
	}
});

test("client reports an unreachable mod", async () => {
	const { server, port } = await startFakeMod();
	await new Promise((resolve) => server.close(resolve));

	const client = api.createClient({ host: "127.0.0.1", port: port, timeoutMs: 500, discoverPorts: [] });
	await assert.rejects(client.getState(), (error) => error.code === "unreachable");
});

test("hub reports flight, menu and offline states", async () => {
	const { server, port, fake } = await startFakeMod();
	const client = api.createClient({ host: "127.0.0.1", port: port, discoverPorts: [] });
	const hub = state.createHub({ client: client, flightIntervalMs: 20, menuIntervalMs: 20, offlineIntervalMs: 20 });

	const seen = [];
	const unsubscribe = hub.subscribe((snapshot, status) => {
		seen.push(status);
	});

	try {
		await waitFor(() => hub.getStatus() === state.STATUS.FLIGHT);
		assert.equal(hub.getSnapshot().vessel.name, "Test Kerman");

		fake.inFlight = false;
		await waitFor(() => seen.includes(state.STATUS.MENU));

		server.close();
		await waitFor(() => hub.getStatus() === state.STATUS.OFFLINE);
	} finally {
		unsubscribe();
		server.close();
	}

	assert.equal(hub.isRunning(), false);
});

test("hub stops polling when the last key disappears", async () => {
	const { server, port, fake } = await startFakeMod();
	try {
		const client = api.createClient({ host: "127.0.0.1", port: port, discoverPorts: [] });
		const hub = state.createHub({ client: client, flightIntervalMs: 10, menuIntervalMs: 10, offlineIntervalMs: 10 });

		const unsubscribe = hub.subscribe(() => {});
		await waitFor(() => hub.getStatus() === state.STATUS.FLIGHT);
		unsubscribe();

		const requestsAfterStop = fake.requests.length;
		await new Promise((resolve) => setTimeout(resolve, 60));
		assert.equal(hub.isRunning(), false);
		assert.equal(fake.requests.length, requestsAfterStop);
	} finally {
		server.close();
	}
});

test("hub exposes the connected game and its capabilities", async () => {
	const { server, port } = await startFakeMod();
	try {
		const client = api.createClient({ host: "127.0.0.1", port: port, discoverPorts: [] });
		const hub = state.createHub({ client: client, flightIntervalMs: 20, menuIntervalMs: 20, offlineIntervalMs: 20 });

		const unsubscribe = hub.subscribe(() => {});
		await waitFor(() => hub.getStatus() === state.STATUS.FLIGHT);
		unsubscribe();

		assert.equal(hub.getGame(), "ksp2");
		assert.equal(hub.getGameName(), "Kerbal Space Program 2");
		assert.deepEqual(hub.getCapabilities().actionGroups, ["Gear", "Lights", "ResourceScan"]);
	} finally {
		server.close();
	}
});

test("discover finds a mod on one of the candidate ports", async () => {
	const { server, port } = await startFakeMod();
	try {
		const client = api.createClient({ host: "127.0.0.1", port: 1, discoverPorts: [] });
		const found = await client.discover([port]);

		assert.ok(found, "expected to find the fake mod");
		assert.equal(found.port, port);
		assert.equal(found.health.game, "ksp2");
	} finally {
		server.close();
	}
});

test("discover reports nothing when no mod is listening", async () => {
	const { server, port } = await startFakeMod();
	await new Promise((resolve) => server.close(resolve));

	const client = api.createClient({ host: "127.0.0.1", port: port, timeoutMs: 400, discoverPorts: [] });
	assert.equal(await client.discover([port]), null);
});

async function waitFor(predicate, timeoutMs) {
	const limit = Date.now() + (timeoutMs || 3000);
	while (Date.now() < limit) {
		if (predicate()) {
			return;
		}
		await new Promise((resolve) => setTimeout(resolve, 10));
	}
	throw new Error("Timed out waiting for condition");
}
