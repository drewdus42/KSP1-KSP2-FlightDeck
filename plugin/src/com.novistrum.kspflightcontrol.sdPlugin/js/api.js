/**
 * Thin client for the KSP Flight Deck HTTP API. Works in the plugin page and in Node (tests).
 */
(function (root, factory) {
	const api = factory();
	if (typeof module === "object" && module.exports) {
		module.exports = api;
	} else {
		root.KspApi = api;
	}
})(typeof self !== "undefined" ? self : this, function () {
	const DEFAULT_TIMEOUT_MS = 4000;

	// The two mods listen on different ports by default (KSP 1 took 8080 before KSP 2 existed).
	const DEFAULT_PORTS = [8080, 8081];

	function randomId(length) {
		const characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
		const size = length || 6;
		let value = "";
		for (let i = 0; i < size; i++) {
			value += characters.charAt(Math.floor(Math.random() * characters.length));
		}
		return value;
	}

	function createClient(options) {
		const settings = {
			host: "127.0.0.1",
			port: 8080,
			timeoutMs: DEFAULT_TIMEOUT_MS,
			discoverPorts: DEFAULT_PORTS.slice()
		};

		configure(options);

		function configure(next) {
			if (!next) {
				return settings;
			}
			if (typeof next.host === "string" && next.host.trim().length > 0) {
				settings.host = next.host.trim();
			}
			if (Number(next.port) > 0) {
				settings.port = Math.floor(Number(next.port));
			}
			if (Number(next.timeoutMs) > 0) {
				settings.timeoutMs = Math.floor(Number(next.timeoutMs));
			}
			if (Array.isArray(next.discoverPorts)) {
				settings.discoverPorts = next.discoverPorts.slice();
			}
			return settings;
		}

		function baseUrl() {
			return "http://" + settings.host + ":" + settings.port;
		}

		async function requestJson(path, init) {
			const controller = typeof AbortController !== "undefined" ? new AbortController() : null;
			const timer = controller ? setTimeout(function () { controller.abort(); }, settings.timeoutMs) : null;

			let response;
			try {
				response = await fetch(baseUrl() + path, {
					method: (init && init.method) || "GET",
					headers: (init && init.headers) || { "Content-Type": "application/json" },
					body: init && init.body,
					signal: controller ? controller.signal : undefined,
					cache: "no-store"
				});
			} catch (cause) {
				const error = new Error("KSP mod is not reachable at " + baseUrl());
				error.code = "unreachable";
				error.cause = cause;
				throw error;
			} finally {
				if (timer) {
					clearTimeout(timer);
				}
			}

			const text = await response.text();
			let body = null;
			if (text && text.length > 0) {
				try {
					body = JSON.parse(text);
				} catch (cause) {
					const error = new Error("The KSP mod sent an unreadable response.");
					error.code = "invalid_response";
					error.cause = cause;
					throw error;
				}
			}

			return { status: response.status, ok: response.ok, body: body };
		}

		/** Full state document; throws only when the mod cannot be reached. */
		async function getState() {
			const result = await requestJson("/api/state");
			if (!result.body) {
				const error = new Error("Empty response from " + baseUrl());
				error.code = "invalid_response";
				throw error;
			}
			return result.body;
		}

		async function health() {
			const result = await requestJson("/api/health");
			return result.body || { ok: false, error: "invalid_response" };
		}

		/**
		 * Tries the configured port first, then the other default ports. Returns the health
		 * document of the first mod that answers, with the port it answered on.
		 */
		async function discover(ports) {
			const candidates = [];
			const list = ports && ports.length ? ports : settings.discoverPorts || [];

			candidates.push(settings.port);
			list.forEach(function (port) {
				if (candidates.indexOf(port) === -1) {
					candidates.push(port);
				}
			});

			if (!list.length) {
				return null;
			}

			for (const port of candidates) {
				if (!(Number(port) > 0)) {
					continue;
				}

				const saved = settings.port;
				settings.port = Math.floor(Number(port));
				try {
					const body = await health();
					if (body && body.ok) {
						return { port: settings.port, health: body };
					}
				} catch (cause) {
					// Not this port; keep looking.
				} finally {
					if (settings.port !== saved) {
						settings.port = saved;
					}
				}
			}

			return null;
		}

		/**
		 * Legacy-compatible action call: { ID, Action, parameters }.
		 * Network problems throw; API problems come back as { Success: false, Error }.
		 */
		async function send(action, parameters) {
			const payload = {
				ID: randomId(6),
				Action: action,
				parameters: parameters || {}
			};

			const result = await requestJson("/api", {
				method: "POST",
				body: JSON.stringify(payload)
			});

			const body = result.body || {};
			return {
				status: result.status,
				success: body.Success === true,
				data: body.Data || null,
				error: body.Error || (result.ok ? null : "http_" + result.status),
				message: body.Message || null
			};
		}

		return {
			configure: configure,
			settings: function () { return { host: settings.host, port: settings.port }; },
			baseUrl: baseUrl,
			getState: getState,
			health: health,
			discover: discover,
			send: send
		};
	}

	return {
		createClient: createClient,
		randomId: randomId,
		DEFAULT_PORTS: DEFAULT_PORTS
	};
});
