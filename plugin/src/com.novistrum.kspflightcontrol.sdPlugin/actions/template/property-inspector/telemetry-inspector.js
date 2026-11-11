/// <reference path="../../../libs/js/property-inspector.js" />
/// <reference path="../../../libs/js/utils.js" />
/// <reference path="../../../js/util.js" />
/// <reference path="../../../js/api.js" />

let currentSettings = { field: "altitude" };
let endpoint = { host: "127.0.0.1", port: null };

// Resource names are remembered across sessions, so a resource readout stays selectable while
// the game sits in the main menu (the mod only reports resources once a vessel is loaded).
let knownResources = [];
let capabilities = null;

$PI.onConnected(function (jsn) {
    const settings = (jsn && jsn.actionInfo && jsn.actionInfo.payload && jsn.actionInfo.payload.settings) || {};
    currentSettings = settings;
    buildOptions(null);
    $PI.getGlobalSettings();
});

$PI.onDidReceiveGlobalSettings(function (jsn) {
    const settings = (jsn && jsn.payload && jsn.payload.settings) || {};
    endpoint.host = settings.host || "127.0.0.1";
    endpoint.port = Number(settings.port) > 0 ? Number(settings.port) : null;

    knownResources = Array.isArray(settings.resourceFields) ? settings.resourceFields.slice() : [];
    buildOptions(null);
    loadSnapshot();
});

async function loadSnapshot() {
    let snapshot = null;
    const client = KspApi.createClient({ host: endpoint.host, port: endpoint.port || 8080 });

    try {
        const found = await client.discover();
        if (found) {
            endpoint.port = found.port;
            capabilities = found.health.capabilities || capabilities;

            const response = await fetch("http://" + endpoint.host + ":" + endpoint.port + "/api/state", { cache: "no-store" });
            const body = await response.json();
            snapshot = body && body.ok ? body.data : null;
            if (body && body.capabilities) {
                capabilities = body.capabilities;
            }
        }
    } catch (cause) {
        snapshot = null;
    }

    if (snapshot && snapshot.resources) {
        rememberResources(Object.keys(snapshot.resources));
    }

    buildOptions(snapshot);

    const hint = document.getElementById("telemetryHint");
    if (hint) {
        hint.textContent = snapshot
            ? "Live values from the active vessel."
            : "No vessel reported yet. Resource readouts stay available here, but only show values in flight.";
    }
}

function rememberResources(names) {
    let changed = false;
    names.forEach(function (name) {
        if (knownResources.indexOf(name) === -1) {
            knownResources.push(name);
            changed = true;
        }
    });

    if (changed) {
        knownResources.sort();
        $PI.setGlobalSettings({ host: endpoint.host, port: endpoint.port, resourceFields: knownResources });
    }
}

function buildOptions(snapshot) {
    const select = document.getElementById("telemetryField");
    if (!select) {
        return;
    }

    const fields = KspUtil.telemetryFields(snapshot, capabilities);
    const added = {};

    const flightGroup = document.createElement("optgroup");
    flightGroup.label = "Flight data";
    const resourceGroup = document.createElement("optgroup");
    resourceGroup.label = "Resources";

    function addOption(id, text, isResource) {
        if (added[id]) {
            return;
        }
        added[id] = true;
        const option = document.createElement("option");
        option.value = id;
        option.textContent = text;
        (isResource ? resourceGroup : flightGroup).appendChild(option);
    }

    fields.forEach(function (field) {
        addOption(field.id, field.label, field.id.indexOf("resource:") === 0);
    });

    knownResources.forEach(function (name) {
        const id = "resource:" + name;
        addOption(id, KspUtil.telemetryFieldLabel(id) + (snapshot ? "" : " (in flight only)"), true);
    });

    // Never drop the field that is already configured, even if the vessel lacks it.
    const desired = currentSettings.field || "altitude";
    addOption(desired, KspUtil.telemetryFieldLabel(desired), desired.indexOf("resource:") === 0);

    select.innerHTML = "";
    select.appendChild(flightGroup);
    if (resourceGroup.childElementCount > 0) {
        select.appendChild(resourceGroup);
    }

    select.value = desired;
}

function updateField() {
    const select = document.getElementById("telemetryField");
    currentSettings = Object.assign({}, currentSettings, { field: select.value });
    $PI.setSettings(currentSettings);
}
