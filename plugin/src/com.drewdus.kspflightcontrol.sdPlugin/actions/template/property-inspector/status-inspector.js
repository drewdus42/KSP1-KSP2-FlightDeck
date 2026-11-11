/// <reference path="../../../libs/js/property-inspector.js" />
/// <reference path="../../../libs/js/utils.js" />
/// <reference path="../../../js/api.js" />

let globalSettings = { host: "127.0.0.1", port: 8080 };

$PI.onConnected(function () {
    $PI.getGlobalSettings();
});

$PI.onDidReceiveGlobalSettings(function (jsn) {
    const settings = (jsn && jsn.payload && jsn.payload.settings) || {};
    // Keep keys other inspectors own (the telemetry inspector stores known resource names).
    globalSettings = Object.assign({}, settings, {
        host: settings.host || "127.0.0.1",
        port: Number(settings.port) || 8080
    });

    const hostInput = document.getElementById("hostInput");
    const portInput = document.getElementById("portInput");
    if (hostInput) {
        hostInput.value = globalSettings.host;
    }
    if (portInput) {
        portInput.value = globalSettings.port;
    }
});

function saveEndpoint() {
    const hostInput = document.getElementById("hostInput");
    const portInput = document.getElementById("portInput");
    const host = (hostInput && hostInput.value.trim()) || "127.0.0.1";
    const parsedPort = Number(portInput && portInput.value);
    const port = Math.min(65535, Math.max(1, isFinite(parsedPort) && parsedPort > 0 ? Math.floor(parsedPort) : 8080));

    globalSettings = Object.assign({}, globalSettings, { host: host, port: port });
    $PI.setGlobalSettings(globalSettings);
    setResult("Saved. Press Test connection to verify.", false);
}

async function testConnection() {
    saveEndpoint();
    setResult("Looking for the mod on " + globalSettings.host + " (ports 8080 and 8081) ...", false);

    try {
        // Either game may be running, so probe both default ports instead of assuming one.
        const client = KspApi.createClient({ host: globalSettings.host, port: globalSettings.port });
        const found = await client.discover();

        if (!found) {
            setResult("No answer on " + globalSettings.host + ":8080 or :8081. Is the game running with the Flight Deck mod installed?", true);
            return;
        }

        const health = found.health;
        if (found.port !== globalSettings.port) {
            globalSettings = Object.assign({}, globalSettings, { port: found.port });
            $PI.setGlobalSettings(globalSettings);

            const portInput = document.getElementById("portInput");
            if (portInput) {
                portInput.value = found.port;
            }
        }

        const gameName = health.gameName || (health.game === "ksp2" ? "Kerbal Space Program 2" : "Kerbal Space Program");
        setResult(
            "Connected: " + gameName + " · " + (health.mod || "KSPFlightDeck") + " " + (health.version || "") +
            " · port " + found.port + (health.inFlight ? " · vessel in flight" : " · main menu"),
            false
        );
    } catch (cause) {
        setResult("No answer on " + globalSettings.host + ". Is the game running with the mod installed?", true);
    }
}

function setResult(message, isError) {
    const element = document.getElementById("testResult");
    if (!element) {
        return;
    }
    element.textContent = message;
    element.style.color = isError ? "#f87171" : "#a7b3c2";
}
