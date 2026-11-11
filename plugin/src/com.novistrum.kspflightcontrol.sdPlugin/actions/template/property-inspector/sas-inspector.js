/// <reference path="../../../libs/js/property-inspector.js" />
/// <reference path="../../../libs/js/utils.js" />

let currentSettings = {};

$PI.onConnected(function (jsn) {
    const settings = (jsn && jsn.actionInfo && jsn.actionInfo.payload && jsn.actionInfo.payload.settings) || {};
    currentSettings = settings;
    document.getElementById("sasMode").value = settings.mode || "StabilityAssist";
});

function updateSasMode() {
    const mode = document.getElementById("sasMode").value;
    currentSettings = Object.assign({}, currentSettings, { mode: mode });
    $PI.setSettings(currentSettings);
}
