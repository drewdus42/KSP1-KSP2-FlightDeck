using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FlightDeck.Core;
using Xunit;

namespace FlightDeck.Core.Tests
{
    public class HttpServerTests
    {
        private static HttpClient CreateClient(HttpServer server)
        {
            return new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + server.Port) };
        }

        [Fact]
        public async Task ServesJsonAndCorsHeaders()
        {
            using var server = new HttpServer(request => HttpResponse.Json(new Dictionary<string, object>
            {
                { "ok", true },
                { "path", request.Path },
                { "query", request.QueryParameters.ContainsKey("a") ? request.QueryParameters["a"] : null }
            }));

            server.Start(0);
            using HttpClient client = CreateClient(server);

            HttpResponseMessage response = await client.GetAsync("/api/health?a=1");
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("*", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.Contains("\"path\":\"/api/health\"", body);
            Assert.Contains("\"query\":\"1\"", body);
        }

        [Fact]
        public async Task PostBodyReachesHandler()
        {
            using var server = new HttpServer(request => HttpResponse.Json(new Dictionary<string, object>
            {
                { "method", request.Method },
                { "body", request.Body },
                { "contentType", request.Header("Content-Type") }
            }));

            server.Start(0);
            using HttpClient client = CreateClient(server);

            HttpResponseMessage response = await client.PostAsync(
                "/api",
                new StringContent("{\"Action\":\"getState\"}", Encoding.UTF8, "application/json"));

            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("\"method\":\"POST\"", body);
            Assert.Contains("getState", body);
            Assert.Contains("application/json", body);
        }

        [Fact]
        public async Task KeepsConnectionAliveForRepeatedRequests()
        {
            int handled = 0;
            using var server = new HttpServer(request =>
            {
                Interlocked.Increment(ref handled);
                return HttpResponse.Json(new Dictionary<string, object> { { "ok", true } });
            });

            server.Start(0);

            using var socket = new TcpClient();
            socket.Connect(IPAddress.Loopback, server.Port);
            using NetworkStream stream = socket.GetStream();

            byte[] pipeline = Encoding.ASCII.GetBytes(
                "GET /api/state HTTP/1.1\r\nHost: localhost\r\n\r\n" +
                "GET /api/health HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(pipeline, 0, pipeline.Length);

            RawResponse first = await RawResponse.ReadAsync(stream);
            RawResponse second = await RawResponse.ReadAsync(stream);

            Assert.Contains("200 OK", first.StatusLine);
            Assert.Equal("keep-alive", first.Headers["Connection"]);
            Assert.Contains("200 OK", second.StatusLine);
            Assert.Equal("close", second.Headers["Connection"]);
            Assert.Equal(2, handled);
        }

        [Fact]
        public async Task WaitsForChunkedArrivalOfBody()
        {
            string seen = null;
            using var server = new HttpServer(request =>
            {
                seen = request.Body;
                return HttpResponse.Json(new Dictionary<string, object> { { "ok", true } });
            });

            server.Start(0);

            using var socket = new TcpClient();
            socket.Connect(IPAddress.Loopback, server.Port);
            using NetworkStream stream = socket.GetStream();

            const string body = "{\"Action\":\"getShipThrottle\"}";
            byte[] headers = Encoding.ASCII.GetBytes(
                "POST /api HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\nContent-Length: "
                + Encoding.UTF8.GetByteCount(body) + "\r\nConnection: close\r\n\r\n");

            await stream.WriteAsync(headers, 0, headers.Length);
            await stream.FlushAsync();
            await Task.Delay(120);
            byte[] payload = Encoding.UTF8.GetBytes(body);
            await stream.WriteAsync(payload, 0, payload.Length);
            await stream.FlushAsync();

            RawResponse response = await RawResponse.ReadAsync(stream);

            Assert.Contains("200 OK", response.StatusLine);
            Assert.Equal(body, seen);
        }

        [Fact]
        public async Task RejectsMalformedRequestLine()
        {
            using var server = new HttpServer(request => HttpResponse.Json(new Dictionary<string, object>()));
            server.Start(0);

            using var socket = new TcpClient();
            socket.Connect(IPAddress.Loopback, server.Port);
            using NetworkStream stream = socket.GetStream();

            byte[] garbage = Encoding.ASCII.GetBytes("this is not http\r\n\r\n");
            await stream.WriteAsync(garbage, 0, garbage.Length);

            RawResponse response = await RawResponse.ReadAsync(stream);

            Assert.Contains("400", response.StatusLine);
        }

        [Fact]
        public async Task ReportsHandlerFailuresAs500()
        {
            using var server = new HttpServer(request => { throw new InvalidOperationException("boom"); });
            server.Start(0);
            using HttpClient client = CreateClient(server);

            HttpResponseMessage response = await client.GetAsync("/api/state");
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains("internal_error", body);
        }

        [Fact]
        public async Task HandlerCanReturnNullBodyForNoContent()
        {
            using var server = new HttpServer(request => HttpResponse.Empty(204));
            server.Start(0);
            using HttpClient client = CreateClient(server);

            HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/api"));

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public void SecondBindOnSamePortFails()
        {
            using var first = new HttpServer(request => HttpResponse.Empty(204));
            first.Start(0);

            using var second = new HttpServer(request => HttpResponse.Empty(204));
            Assert.Throws<SocketException>(delegate { second.Start(first.Port); });
        }

        [Fact]
        public async Task RouterIsServedEndToEnd()
        {
            var bridge = new FakeBridge { Throttle = 0.4d };
            var router = new ApiRouter(bridge, new InlineDispatcher(), "1.0.0-test", new GameCapabilities("ksp1", "Kerbal Space Program"));

            using var server = new HttpServer(router.Handle);
            server.Start(0);
            using HttpClient client = CreateClient(server);

            string stateBody = await client.GetStringAsync("/api/state");
            Assert.Contains("\"ok\":true", stateBody);
            Assert.Contains("\"throttle\":0.4", stateBody);

            HttpResponseMessage legacy = await client.PostAsync(
                "/api",
                new StringContent("{\"ID\":\"x1\",\"Action\":\"setActionGroupState\",\"parameters\":{\"ID\":\"Gear\"}}", Encoding.UTF8, "application/json"));
            string legacyBody = await legacy.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
            Assert.Contains("\"Success\":true", legacyBody);
            Assert.Contains("\"Status\":false", legacyBody);
        }
    }

    internal sealed class RawResponse
    {
        public string StatusLine { get; private set; }
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Body { get; private set; }

        public static async Task<RawResponse> ReadAsync(Stream stream)
        {
            var headerBytes = new List<byte>();
            var single = new byte[1];

            while (true)
            {
                int read = await stream.ReadAsync(single, 0, 1);
                if (read == 0)
                {
                    throw new IOException("Connection closed before headers completed.");
                }

                headerBytes.Add(single[0]);
                int count = headerBytes.Count;
                if (count >= 4
                    && headerBytes[count - 4] == (byte)'\r'
                    && headerBytes[count - 3] == (byte)'\n'
                    && headerBytes[count - 2] == (byte)'\r'
                    && headerBytes[count - 1] == (byte)'\n')
                {
                    break;
                }
            }

            string headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
            string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            var response = new RawResponse { StatusLine = lines[0] };
            for (int i = 1; i < lines.Length; i++)
            {
                int separator = lines[i].IndexOf(':');
                if (separator > 0)
                {
                    response.Headers[lines[i].Substring(0, separator).Trim()] = lines[i].Substring(separator + 1).Trim();
                }
            }

            int contentLength = 0;
            string header;
            if (response.Headers.TryGetValue("Content-Length", out header))
            {
                contentLength = int.Parse(header);
            }

            var body = new byte[contentLength];
            int filled = 0;
            while (filled < contentLength)
            {
                int read = await stream.ReadAsync(body, filled, contentLength - filled);
                if (read == 0)
                {
                    break;
                }

                filled += read;
            }

            response.Body = Encoding.UTF8.GetString(body, 0, filled);
            return response;
        }
    }
}
