/// <reference path="../../../libs/js/property-inspector.js" />
/// <reference path="../../../libs/js/utils.js" />
/// <reference path="../../../js/util.js" />
/// <reference path="../../../js/api.js" />

let currentSettings = {};
let endpoint = { host: "127.0.0.1", port: null };

$PI.onConnected(function (jsn) {
    const settings = (jsn && jsn.actionInfo && jsn.actionInfo.payload && jsn.actionInfo.payload.settings) || {};
    currentSettings = settings;
    document.getElementById("agSelect").value = settings.action || "None";
    document.getElementById("agLongPress").checked = settings.longPressInvert !== false;
    $PI.getGlobalSettings();
});

$PI.onDidReceiveGlobalSettings(function (jsn) {
    const settings = (jsn && jsn.payload && jsn.payload.settings) || {};
    endpoint.host = settings.host || "127.0.0.1";
    endpoint.port = Number(settings.port) > 0 ? Number(settings.port) : null;
    loadCapabilities();
});

/**
 * Asks whichever mod is running (KSP 1 on 8080, KSP 2 on 8081) which action groups it supports,
 * then rebuilds the list. Falls back to the full set when nothing is connected.
 */
async function loadCapabilities() {
    const client = KspApi.createClient({ host: endpoint.host, port: endpoint.port || 8080 });
    let capabilities = null;

    try {
        const found = await client.discover();
        capabilities = found && found.health ? found.health.capabilities : null;
    } catch (cause) {
        capabilities = null;
    }

    updateHint(capabilities);

    const select = document.getElementById("agSelect");
    if (!select) {
        return;
    }

    const options = KspUtil.actionGroupOptions(capabilities);
    const previous = currentSettings.action || select.value || "None";

    select.innerHTML = "";

    const none = document.createElement("option");
    none.value = "None";
    none.textContent = "None";
    select.appendChild(none);

    options.forEach(function (option) {
        const element = document.createElement("option");
        element.value = option.id;
        element.textContent = option.label;
        select.appendChild(element);
    });

    const hasPrevious = options.some(function (option) { return option.id === previous; });
    select.value = hasPrevious || previous === "None" ? previous : "None";
}

function updateHint(capabilities) {
    const hint = document.getElementById("agHint");
    if (!hint) {
        return;
    }

    if (!capabilities) {
        hint.textContent = "No mod detected. Start Kerbal Space Program (1 or 2) with the Flight Deck mod installed.";
        return;
    }

    if (capabilities.virtualActionGroups && capabilities.virtualActionGroups.length > 0) {
        hint.textContent = "Solar panels, radiators, antennas and science are emulated by the mod: it switches every matching part module on the vessel.";
        return;
    }

    hint.textContent = "This game exposes these groups natively.";
}

function updateButton() {
    const group = document.getElementById("agSelect").value;
    const longPress = document.getElementById("agLongPress").checked;
    currentSettings = Object.assign({}, currentSettings, { action: group, longPressInvert: longPress });
    $PI.setSettings(currentSettings);
}
