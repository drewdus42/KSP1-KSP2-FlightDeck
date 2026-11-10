using System.Collections.Generic;
using System.Text;
using FlightDeck.Core;
using Xunit;

namespace FlightDeck.Core.Tests
{
    public class ApiRouterTests
    {
        private static ApiRouter CreateRouter(FakeBridge bridge)
        {
            // Constructed on the test thread, so QueueDispatcher treats this thread as the
            // main thread and runs commands inline.
            return new ApiRouter(bridge, new QueueDispatcher(), "1.0.0-test");
        }

        private static HttpResponse Post(ApiRouter router, string action, string parametersJson = "{}")
        {
            var request = new HttpRequest
            {
                Method = "POST",
                Path = "/api",
                Body = "{\"ID\":\"abc123\",\"Action\":\"" + action + "\",\"parameters\":" + parametersJson + "}"
            };

            return router.Handle(request);
        }

        private static Dictionary<string, object> Body(HttpResponse response)
        {
            return Json.AsObject(Json.Parse(Encoding.UTF8.GetString(response.Body)));
        }

        [Fact]
        public void HealthReportsFlightState()
        {
            var bridge = new FakeBridge { IsReady = false };
            var body = Body(CreateRouter(bridge).Handle(new HttpRequest { Method = "GET", Path = "/api/health" }));

            Assert.True((bool)body["ok"]);
            Assert.Equal("KSPFlightDeck", body["mod"]);
            Assert.False((bool)body["inFlight"]);
        }

        [Fact]
        public void StateExplainsWhyItCannotServeRequests()
        {
            var bridge = new FakeBridge { IsReady = false };
            HttpResponse response = CreateRouter(bridge).Handle(new HttpRequest { Method = "GET", Path = "/api/state" });
            var body = Body(response);

            Assert.Equal(200, response.StatusCode);
            Assert.False((bool)body["ok"]);
            Assert.Equal("not_in_flight", body["error"]);
        }

        [Fact]
        public void StateReturnsSnapshot()
        {
            var bridge = new FakeBridge { Throttle = 0.5d };
            var body = Body(CreateRouter(bridge).Handle(new HttpRequest { Method = "GET", Path = "/api/state" }));
            var data = Json.AsObject(body["data"]);

            Assert.True((bool)body["ok"]);
            Assert.Equal(0.5d, data["throttle"]);
        }

        [Fact]
        public void LegacyGetActionGroupStateUsesStatusField()
        {
            var bridge = new FakeBridge();
            var body = Body(Post(CreateRouter(bridge), "getActionGroupState", "{\"ID\":\"Gear\"}"));
            var data = Json.AsObject(body["Data"]);

            Assert.True((bool)body["Success"]);
            Assert.Equal("abc123", body["ID"]);
            Assert.True((bool)data["Status"]);
        }

        [Fact]
        public void LegacySetActionGroupStateTogglesWithoutStateParameter()
        {
            var bridge = new FakeBridge();
            ApiRouter router = CreateRouter(bridge);

            var first = Json.AsObject(Body(Post(router, "setActionGroupState", "{\"ID\":\"Gear\"}"))["Data"]);
            var second = Json.AsObject(Body(Post(router, "setActionGroupState", "{\"ID\":\"Gear\"}"))["Data"]);

            Assert.False((bool)first["Status"]);
            Assert.True((bool)second["Status"]);
            Assert.Contains("actiongroup.toggle", bridge.Calls);
        }

        [Fact]
        public void SetActionGroupHonoursExplicitState()
        {
            var bridge = new FakeBridge();
            var data = Json.AsObject(Body(Post(CreateRouter(bridge), "setActionGroup", "{\"Group\":\"Lights\",\"State\":false}"))["Data"]);

            Assert.False((bool)data["Status"]);
            Assert.False((bool)bridge.Groups["Lights"]);
        }

        [Fact]
        public void LegacyThrottleNamesAreSupported()
        {
            var bridge = new FakeBridge();
            ApiRouter router = CreateRouter(bridge);

            var current = Json.AsObject(Body(Post(router, "getShipThrottle"))["Data"]);
            Assert.Equal(0.25d, current["Throttle"]);

            var updated = Json.AsObject(Body(Post(router, "setShipThrottle", "{\"Throttle\":0.75}"))["Data"]);
            Assert.Equal(0.75d, updated["Throttle"]);
            Assert.Equal(0.75d, bridge.Throttle);
        }

        [Theory]
        [InlineData("-1", 0d)]
        [InlineData("5", 1d)]
        public void ThrottleIsClamped(string requested, double expected)
        {
            var bridge = new FakeBridge();
            var data = Json.AsObject(Body(Post(CreateRouter(bridge), "setShipThrottle", "{\"Throttle\":" + requested + "}"))["Data"]);

            Assert.Equal(expected, System.Convert.ToDouble(data["Throttle"]));
        }

        [Fact]
        public void MissingThrottleIsRejected()
        {
            HttpResponse response = Post(CreateRouter(new FakeBridge()), "setShipThrottle", "{}");
            var body = Body(response);

            Assert.Equal(400, response.StatusCode);
            Assert.False((bool)body["Success"]);
            Assert.Equal("missing_parameter", body["Error"]);
        }

        [Fact]
        public void UnknownGroupIsRejected()
        {
            HttpResponse response = Post(CreateRouter(new FakeBridge()), "getActionGroupState", "{\"ID\":\"Warp\"}");
            var body = Body(response);

            Assert.Equal(400, response.StatusCode);
            Assert.Equal("unknown_action_group", body["Error"]);
        }

        [Fact]
        public void UnknownActionIsRejected()
        {
            HttpResponse response = Post(CreateRouter(new FakeBridge()), "selfDestruct");
            var body = Body(response);

            Assert.Equal(400, response.StatusCode);
            Assert.Equal("unknown_action", body["Error"]);
        }

        [Fact]
        public void InvalidJsonIsRejected()
        {
            var router = CreateRouter(new FakeBridge());
            HttpResponse response = router.Handle(new HttpRequest { Method = "POST", Path = "/api", Body = "{oops" });

            Assert.Equal(400, response.StatusCode);
            Assert.Equal("invalid_json", Body(response)["Error"]);
        }

        [Fact]
        public void SasModesAreValidatedAndApplied()
        {
            var bridge = new FakeBridge();
            ApiRouter router = CreateRouter(bridge);

            var ok = Json.AsObject(Body(Post(router, "setSasMode", "{\"Mode\":\"prograde\"}"))["Data"]);
            Assert.Equal("Prograde", ok["Mode"]);

            HttpResponse bad = Post(router, "setSasMode", "{\"Mode\":\"sideways\"}");
            Assert.Equal(400, bad.StatusCode);
            Assert.Equal("unknown_sas_mode", Body(bad)["Error"]);
        }

        [Fact]
        public void SasEnableAndToggleWork()
        {
            var bridge = new FakeBridge();
            ApiRouter router = CreateRouter(bridge);

            var enabled = Json.AsObject(Body(Post(router, "setSas", "{\"Enabled\":true,\"Mode\":\"Maneuver\"}"))["Data"]);
            Assert.True((bool)enabled["Enabled"]);
            Assert.Equal("Maneuver", enabled["Mode"]);

            var toggled = Json.AsObject(Body(Post(router, "toggleSas"))["Data"]);
            Assert.False((bool)toggled["Enabled"]);
        }

        [Fact]
        public void StagingCommandsRoundTrip()
        {
            var bridge = new FakeBridge { StageNumber = 4 };
            ApiRouter router = CreateRouter(bridge);

            var before = Json.AsObject(Body(Post(router, "getStaging"))["Data"]);
            Assert.Equal(4L, before["CurrentStage"]);

            var after = Json.AsObject(Body(Post(router, "stage"))["Data"]);
            Assert.Equal(3L, after["CurrentStage"]);
        }

        [Fact]
        public void TimeWarpAndPauseCommandsRoundTrip()
        {
            var bridge = new FakeBridge();
            ApiRouter router = CreateRouter(bridge);

            var warp = Json.AsObject(Body(Post(router, "setTimeWarp", "{\"Index\":3}"))["Data"]);
            Assert.Equal(3L, warp["Index"]);
            Assert.Equal(3, bridge.WarpIndex);

            var paused = Json.AsObject(Body(Post(router, "togglePause"))["Data"]);
            Assert.True((bool)paused["Paused"]);
            Assert.True(bridge.Paused);
        }

        [Fact]
        public void NotInFlightReturns503()
        {
            var bridge = new FakeBridge { IsReady = false };
            HttpResponse response = Post(CreateRouter(bridge), "getShipThrottle");
            var body = Body(response);

            Assert.Equal(503, response.StatusCode);
            Assert.False((bool)body["Success"]);
            Assert.Equal("not_in_flight", body["Error"]);
            Assert.Empty(bridge.Calls);
        }

        [Fact]
        public void GameThreadTimeoutReturns503()
        {
            var router = new ApiRouter(new FakeBridge(), new SlowDispatcher(), "1.0.0-test", null, 50);
            HttpResponse response = Post(router, "getShipThrottle");
            var body = Body(response);

            Assert.Equal(503, response.StatusCode);
            Assert.Equal("game_busy", body["error"]);
        }

        [Fact]
        public void PreflightAndUnknownRoutesAreHandled()
        {
            var router = CreateRouter(new FakeBridge());

            Assert.Equal(204, router.Handle(new HttpRequest { Method = "OPTIONS", Path = "/api" }).StatusCode);
            Assert.Equal(404, router.Handle(new HttpRequest { Method = "GET", Path = "/api/nope" }).StatusCode);
            Assert.Equal(405, router.Handle(new HttpRequest { Method = "GET", Path = "/api" }).StatusCode);
        }

        [Fact]
        public void ActionNamesAreCaseInsensitive()
        {
            var bridge = new FakeBridge();
            var body = Body(Post(CreateRouter(bridge), "GETSHIPTHROTTLE"));

            Assert.True((bool)body["Success"]);
        }
    }
}
