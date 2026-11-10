/// <reference path="../../../libs/js/property-inspector.js" />
/// <reference path="../../../libs/js/utils.js" />

let currentSettings = {};

$PI.onConnected(function (jsn) {
    const settings = (jsn && jsn.actionInfo && jsn.actionInfo.payload && jsn.actionInfo.payload.settings) || {};
    currentSettings = settings;
    document.getElementById("agSelect").value = settings.action || "None";
});

function updateButton() {
    const group = document.getElementById("agSelect").value;
    currentSettings = Object.assign({}, currentSettings, { action: group });
    $PI.setSettings(currentSettings);
}
