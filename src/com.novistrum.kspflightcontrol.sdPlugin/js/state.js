/**
 * Polls the mod once and fans the snapshot out to every visible key.
 * A single request per tick replaces the old plugin's request-per-button.
 */
(function (root, factory) {
	const api = factory();
	if (typeof module === "object" && module.exports) {
		module.exports = api;
	} else {
		root.KspState = api;
	}
})(typeof self !== "undefined" ? self : this, function () {
	const FLIGHT_INTERVAL_MS = 250;
	const MENU_INTERVAL_MS = 750;
	const OFFLINE_INTERVAL_MS = 1500;

	const STATUS = {
		FLIGHT: "flight",
		MENU: "menu",
		OFFLINE: "offline"
	};

	function createHub(options) {
		const optionsValue = options || {};
		let client = optionsValue.client || null;
		let snapshot = null;
		let status = STATUS.OFFLINE;
		let error = null;
		let lastSignature = "";
		let timer = null;
		let running = false;
		let inFlight = false;
		let pollNow = false;
		const listeners = new Set();

		function setClient(next) {
			client = next;
			refresh();
		}

		function statusInterval() {
			if (status === STATUS.FLIGHT) {
				return optionsValue.flightIntervalMs || FLIGHT_INTERVAL_MS;
			}
			if (status === STATUS.MENU) {
				return optionsValue.menuIntervalMs || MENU_INTERVAL_MS;
			}
			return optionsValue.offlineIntervalMs || OFFLINE_INTERVAL_MS;
		}

		function schedule(delay) {
			if (!running) {
				return;
			}
			if (timer) {
				clearTimeout(timer);
			}
			timer = setTimeout(tick, typeof delay === "number" ? delay : statusInterval());
		}

		async function tick() {
			timer = null;
			if (!running || inFlight) {
				return;
			}

			if (!client) {
				schedule(statusInterval());
				return;
			}

			inFlight = true;
			try {
				const body = await client.getState();
				const nextStatus = body && body.ok ? STATUS.FLIGHT : STATUS.MENU;
				const nextSnapshot = body && body.ok && body.data ? body.data : null;

				status = nextStatus;
				snapshot = nextSnapshot;
				error = body && body.ok ? null : body && body.error ? body.error : null;
				lastSignature = signature(nextSnapshot, status, error);
				emit();
			} catch (cause) {
				status = STATUS.OFFLINE;
				snapshot = null;
				error = cause && cause.code ? cause.code : "unreachable";
				const nextSignature = signature(null, status, error);
				if (nextSignature !== lastSignature) {
					lastSignature = nextSignature;
					emit();
				}
			} finally {
				inFlight = false;
				if (pollNow) {
					pollNow = false;
					schedule(0);
				} else {
					schedule(statusInterval());
				}
			}
		}

		function signature(nextSnapshot, nextStatus, nextError) {
			if (!nextSnapshot) {
				return nextStatus + ":" + (nextError || "");
			}
			try {
				return nextStatus + ":" + JSON.stringify(nextSnapshot);
			} catch (cause) {
				return nextStatus + ":" + Date.now();
			}
		}

		function emit() {
			listeners.forEach(function (listener) {
				try {
					listener(snapshot, status, error);
				} catch (cause) {
					console.error("State listener failed", cause);
				}
			});
		}

		function subscribe(listener) {
			listeners.add(listener);
			if (snapshot || status !== STATUS.OFFLINE) {
				listener(snapshot, status, error);
			}

			if (!running) {
				running = true;
				schedule(0);
			}

			return function unsubscribe() {
				listeners.delete(listener);
				if (listeners.size === 0) {
					stop();
				}
			};
		}

		function stop() {
			running = false;
			if (timer) {
				clearTimeout(timer);
				timer = null;
			}
		}

		function refresh() {
			if (!running) {
				return;
			}
			if (inFlight) {
				pollNow = true;
				return;
			}
			schedule(0);
		}

		return {
			STATUS: STATUS,
			setClient: setClient,
			subscribe: subscribe,
			refresh: refresh,
			stop: stop,
			getSnapshot: function () { return snapshot; },
			getStatus: function () { return status; },
			getError: function () { return error; },
			isRunning: function () { return running; }
		};
	}

	return {
		STATUS: STATUS,
		createHub: createHub
	};
});
