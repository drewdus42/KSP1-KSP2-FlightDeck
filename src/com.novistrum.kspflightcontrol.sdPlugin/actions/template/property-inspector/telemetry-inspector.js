/// <reference path="../../../libs/js/property-inspector.js" />
/// <reference path="../../../libs/js/utils.js" />
/// <reference path="../../../js/util.js" />

let currentSettings = { field: "altitude" };
let endpoint = { host: "127.0.0.1", port: 8080 };

$PI.onConnected(function (jsn) {
    const settings = (jsn && jsn.actionInfo && jsn.actionInfo.payload && jsn.actionInfo.payload.settings) || {};
    currentSettings = settings;
    buildOptions(null);
    $PI.getGlobalSettings();
});

$PI.onDidReceiveGlobalSettings(function (jsn) {
    const settings = (jsn && jsn.payload && jsn.payload.settings) || {};
    endpoint.host = settings.host || "127.0.0.1";
    endpoint.port = Number(settings.port) || 8080;
    loadSnapshot();
});

async function loadSnapshot() {
    let snapshot = null;
    try {
        const response = await fetch("http://" + endpoint.host + ":" + endpoint.port + "/api/state", { cache: "no-store" });
        const body = await response.json();
        snapshot = body && body.ok ? body.data : null;
    } catch (cause) {
        snapshot = null;
    }

    buildOptions(snapshot);

    const hint = document.getElementById("telemetryHint");
    if (hint) {
        hint.textContent = snapshot
            ? "Live values from the active vessel."
            : "No vessel reported yet — resource readouts appear once you are in flight.";
    }
}

function buildOptions(snapshot) {
    const select = document.getElementById("telemetryField");
    if (!select) {
        return;
    }

    const fields = KspUtil.telemetryFields(snapshot);
    const flightGroup = document.createElement("optgroup");
    flightGroup.label = "Flight data";
    const resourceGroup = document.createElement("optgroup");
    resourceGroup.label = "Resources";

    fields.forEach(function (field) {
        const option = document.createElement("option");
        option.value = field.id;
        option.textContent = field.label;
        if (field.id.indexOf("resource:") === 0) {
            resourceGroup.appendChild(option);
        } else {
            flightGroup.appendChild(option);
        }
    });

    select.innerHTML = "";
    select.appendChild(flightGroup);
    if (resourceGroup.childElementCount > 0) {
        select.appendChild(resourceGroup);
    }

    const desired = currentSettings.field || "altitude";
    select.value = desired;
    if (select.value !== desired) {
        select.value = "altitude";
    }
}

function updateField() {
    const select = document.getElementById("telemetryField");
    currentSettings = Object.assign({}, currentSettings, { field: select.value });
    $PI.setSettings(currentSettings);
}
