/// <reference path="../../../libs/js/property-inspector.js" />
/// <reference path="../../../libs/js/utils.js" />

// The thrust dial needs no per-key settings; the layout lives in Layouts/thrustLayout.json.
$PI.onConnected(function () {
    console.log("KSP Flight Control: thrust inspector connected");
});
