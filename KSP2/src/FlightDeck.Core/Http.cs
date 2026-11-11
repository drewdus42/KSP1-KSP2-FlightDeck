using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace FlightDeck.Core
{
    public sealed class HttpRequest
    {
        private readonly Dictionary<string, string> _headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Method { get; set; }
        public string Target { get; set; }
        public string Path { get; set; }
        public string Query { get; set; }
        public string HttpVersion { get; set; }
        public string Body { get; set; }
        public byte[] BodyBytes { get; set; }

        public IDictionary<string, string> Headers
        {
            get { return _headers; }
        }

        public IDictionary<string, string> QueryParameters
        {
            get { return _query; }
        }

        public string Header(string name)
        {
            string value;
            return _headers.TryGetValue(name, out value) ? value : null;
        }

        internal void ParseQuery()
        {
            if (string.IsNullOrEmpty(Query))
            {
                return;
            }

            foreach (string pair in Query.Split('&'))
            {
                if (pair.Length == 0)
                {
                    continue;
                }

                int separator = pair.IndexOf('=');
                string key = separator < 0 ? pair : pair.Substring(0, separator);
                string value = separator < 0 ? string.Empty : pair.Substring(separator + 1);

                try
                {
                    _query[UrlDecode(key)] = UrlDecode(value);
                }
                catch (Exception)
                {
                    // A malformed escape sequence must not take the whole request down.
                }
            }
        }

        private static string UrlDecode(string value)
        {
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
    }

    public sealed class HttpResponse
    {
        private readonly Dictionary<string, string> _headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int StatusCode { get; set; }
        public string ReasonPhrase { get; set; }
        public string ContentType { get; set; }
        public byte[] Body { get; set; }

        public IDictionary<string, string> Headers
        {
            get { return _headers; }
        }

        public HttpResponse()
        {
            StatusCode = 200;
            ContentType = "application/json; charset=utf-8";
            Body = new byte[0];
        }

        public static HttpResponse Json(object payload, int statusCode = 200)
        {
            var response = new HttpResponse { StatusCode = statusCode };
            try
            {
                response.Body = Encoding.UTF8.GetBytes(FlightDeck.Core.Json.Serialize(payload));
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.Body = Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"serialization_failed\",\"message\":" + Quote(ex.Message) + "}");
            }

            return response;
        }

        public static HttpResponse Text(string text, int statusCode = 200)
        {
            return new HttpResponse
            {
                StatusCode = statusCode,
                ContentType = "text/plain; charset=utf-8",
                Body = Encoding.UTF8.GetBytes(text ?? string.Empty)
            };
        }

        public static HttpResponse Empty(int statusCode)
        {
            return new HttpResponse { StatusCode = statusCode, Body = new byte[0] };
        }

        private static string Quote(string value)
        {
            return FlightDeck.Core.Json.Serialize(value ?? string.Empty);
        }
    }

    /// <summary>
    /// Small HTTP/1.1 server built directly on a loopback TcpListener.
    /// HttpListener is avoided on purpose: on Windows it needs an URL ACL reservation
    /// unless the game runs elevated, while a plain socket needs no privileges.
    /// </summary>
    public sealed class HttpServer : IDisposable
    {
        private const int MaxHeaderBytes = 64 * 1024;
        private const int MaxBodyBytes = 4 * 1024 * 1024;
        private const int ConnectionTimeoutMs = 60000;

        private readonly Func<HttpRequest, HttpResponse> _handler;
        private readonly Action<string> _log;
        private readonly SemaphoreSlim _connectionSlots = new SemaphoreSlim(16, 16);

        private Socket _socket;
        private Thread _acceptThread;
        private volatile bool _running;
        private long _lastRequestTicks;
        private int _requestCount;
        private int _activeConnections;

        public HttpServer(Func<HttpRequest, HttpResponse> handler, Action<string> log = null)
        {
            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }

            _handler = handler;
            _log = log ?? delegate { };
        }

        public int Port { get; private set; }

        public bool IsRunning
        {
            get { return _running; }
        }

        /// <summary>UTC time the server last accepted a request. Read by the in-flight status panel.</summary>
        public DateTime LastRequestUtc
        {
            get
            {
                long ticks = Interlocked.Read(ref _lastRequestTicks);
                return ticks == 0L ? DateTime.MinValue : new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        /// <summary>Total requests served since the server started.</summary>
        public int RequestCount
        {
            get { return Volatile.Read(ref _requestCount); }
        }

        /// <summary>Client connections currently open (a polling plugin holds a keep-alive one).</summary>
        public int ActiveConnections
        {
            get { return Volatile.Read(ref _activeConnections); }
        }

        public void Start(int port)
        {
            if (_running)
            {
                throw new InvalidOperationException("The server is already running.");
            }

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            socket.Listen(32);

            _socket = socket;
            Port = ((IPEndPoint)socket.LocalEndPoint).Port;
            _running = true;

            _acceptThread = new Thread(() => AcceptLoop(socket))
            {
                IsBackground = true,
                Name = "FlightDeck HTTP accept"
            };
            _acceptThread.Start();

            Log("Flight Deck listening on http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture) + "/api");
        }

        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;

            Socket socket = _socket;
            _socket = null;

            try
            {
                if (socket != null)
                {
                    socket.Close();
                }
            }
            catch (Exception)
            {
                // Socket already torn down.
            }

            Log("Flight Deck HTTP server stopped.");
        }

        public void Dispose()
        {
            Stop();
            _connectionSlots.Dispose();
        }

        private void AcceptLoop(Socket listener)
        {
            while (_running && listener == _socket)
            {
                Socket client = null;
                try
                {
                    client = listener.Accept();
                }
                catch (Exception)
                {
                    if (!_running || listener != _socket)
                    {
                        return;
                    }

                    Log("Accept failed: transient error");
                    continue;
                }

                var accepted = client;
                _connectionSlots.Wait();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        HandleConnection(accepted);
                    }
                    finally
                    {
                        _connectionSlots.Release();
                    }
                });
            }
        }

        private void HandleConnection(Socket client)
        {
            Interlocked.Increment(ref _activeConnections);
            try
            {
                client.NoDelay = true;
                client.ReceiveTimeout = ConnectionTimeoutMs;
                client.SendTimeout = ConnectionTimeoutMs;

                using (var stream = new NetworkStream(client, false))
                {
                    byte[] pending = null;

                    while (_running)
                    {
                        HttpRequest request;
                        try
                        {
                            request = ReadRequest(stream, ref pending);
                        }
                        catch (HttpProtocolException ex)
                        {
                            WriteResponse(stream, null, HttpResponse.Text(ex.Message, ex.StatusCode), false);
                            return;
                        }

                        if (request == null)
                        {
                            return;
                        }

                        Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks);
                        Interlocked.Increment(ref _requestCount);

                        HttpResponse response;
                        try
                        {
                            response = _handler(request) ?? HttpResponse.Text("No response produced by handler.", 500);
                        }
                        catch (Exception ex)
                        {
                            Log("Request handling failed: " + ex);
                            response = HttpResponse.Json(new Dictionary<string, object>
                            {
                                { "ok", false },
                                { "error", "internal_error" },
                                { "message", ex.Message }
                            }, 500);
                        }

                        bool keepAlive = ShouldKeepAlive(request);
                        WriteResponse(stream, request, response, keepAlive);
                        if (!keepAlive)
                        {
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // The plugin polls continuously and Stream Deck reloads pages often, so clients
                // aborting a keep-alive connection is routine. Only real faults are worth a log line.
                if (!IsClientDisconnect(ex))
                {
                    Log("Connection error: " + ex.Message);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeConnections);

                try
                {
                    client.Close();
                }
                catch (Exception)
                {
                    // Ignored.
                }
            }
        }

        private static bool ShouldKeepAlive(HttpRequest request)
        {
            string connection = request.Header("Connection");
            bool closeRequested = !string.IsNullOrEmpty(connection)
                && connection.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0;

            if (closeRequested)
            {
                return false;
            }

            if (string.Equals(request.HttpVersion, "HTTP/1.0", StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrEmpty(connection)
                    && connection.IndexOf("keep-alive", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return true;
        }

        private static HttpRequest ReadRequest(NetworkStream stream, ref byte[] pending)
        {
            var buffer = new byte[8192];
            byte[] data = pending ?? new byte[0];
            pending = null;

            int headerEnd = FindHeaderTerminator(data, data.Length);
            if (headerEnd < 0)
            {
                using (var accumulated = new MemoryStream())
                {
                    if (data.Length > 0)
                    {
                        accumulated.Write(data, 0, data.Length);
                    }

                    while (headerEnd < 0)
                    {
                        int read = ReadFromStream(stream, buffer);
                        if (read <= 0)
                        {
                            if (accumulated.Length == 0)
                            {
                                return null;
                            }

                            throw new HttpProtocolException(400, "Truncated HTTP request.");
                        }

                        accumulated.Write(buffer, 0, read);
                        data = accumulated.ToArray();
                        headerEnd = FindHeaderTerminator(data, data.Length);
                        if (headerEnd < 0 && data.Length > MaxHeaderBytes)
                        {
                            throw new HttpProtocolException(431, "Request headers are too large.");
                        }
                    }
                }
            }

            string headerBlock = Encoding.ASCII.GetString(data, 0, headerEnd);
            var request = ParseHeaders(headerBlock);

            int declaredLength = 0;
            string contentLength = request.Header("Content-Length");
            if (!string.IsNullOrEmpty(contentLength)
                && !int.TryParse(contentLength, NumberStyles.Integer, CultureInfo.InvariantCulture, out declaredLength))
            {
                throw new HttpProtocolException(400, "Invalid Content-Length header.");
            }

            if (declaredLength > MaxBodyBytes)
            {
                throw new HttpProtocolException(413, "Request body is too large.");
            }

            int bodyStart = headerEnd + 4;
            int required = bodyStart + declaredLength;
            int available = Math.Max(0, data.Length - bodyStart);
            if (data.Length < required)
            {
                var expanded = new byte[required];
                Buffer.BlockCopy(data, 0, expanded, 0, data.Length);
                data = expanded;
            }

            int filled = Math.Min(available, declaredLength);
            while (filled < declaredLength)
            {
                int read = stream.Read(data, bodyStart + filled, declaredLength - filled);
                if (read <= 0)
                {
                    throw new HttpProtocolException(400, "Truncated request body.");
                }

                filled += read;
            }

            var body = new byte[declaredLength];
            if (declaredLength > 0)
            {
                Buffer.BlockCopy(data, bodyStart, body, 0, declaredLength);
            }

            // Anything past this request belongs to the next one on the same connection.
            int leftoverStart = bodyStart + declaredLength;
            int leftoverCount = data.Length - leftoverStart;
            if (leftoverCount > 0)
            {
                var leftover = new byte[leftoverCount];
                Buffer.BlockCopy(data, leftoverStart, leftover, 0, leftoverCount);
                pending = leftover;
            }

            request.BodyBytes = body;
            request.Body = body.Length == 0 ? string.Empty : Encoding.UTF8.GetString(body);
            return request;
        }

        private static int ReadFromStream(NetworkStream stream, byte[] buffer)
        {
            try
            {
                return stream.Read(buffer, 0, buffer.Length);
            }
            catch (IOException ex)
            {
                var socketError = ex.InnerException as SocketException;
                if (socketError != null && socketError.SocketErrorCode == SocketError.TimedOut)
                {
                    return 0;
                }

                throw;
            }
        }

        private static int FindHeaderTerminator(byte[] buffer, int length)
        {
            for (int i = 0; i + 3 < length; i++)
            {
                if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                {
                    return i;
                }
            }

            return -1;
        }

        private static HttpRequest ParseHeaders(string headerBlock)
        {
            string[] lines = headerBlock.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0 || lines[0].Length == 0)
            {
                throw new HttpProtocolException(400, "Empty request line.");
            }

            string[] parts = lines[0].Split(' ');
            if (parts.Length < 2)
            {
                throw new HttpProtocolException(400, "Malformed request line.");
            }

            string version = parts.Length > 2 ? parts[2] : "HTTP/1.0";
            if (!version.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase)
                || version.Length < 8
                || !char.IsDigit(version[5])
                || version[6] != '.')
            {
                throw new HttpProtocolException(400, "Unsupported or malformed HTTP version.");
            }

            var request = new HttpRequest
            {
                Method = parts[0].ToUpperInvariant(),
                Target = parts[1],
                HttpVersion = version
            };

            int queryIndex = request.Target.IndexOf('?');
            if (queryIndex >= 0)
            {
                request.Path = request.Target.Substring(0, queryIndex);
                request.Query = request.Target.Substring(queryIndex + 1);
            }
            else
            {
                request.Path = request.Target;
                request.Query = string.Empty;
            }

            request.ParseQuery();

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0)
                {
                    continue;
                }

                int separator = line.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                string name = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                request.Headers[name] = value;
            }

            return request;
        }

        private static void WriteResponse(NetworkStream stream, HttpRequest request, HttpResponse response, bool keepAlive)
        {
            byte[] body = response.Body ?? new byte[0];
            bool suppressBody = response.StatusCode == 204 || response.StatusCode == 304
                || (request != null && string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase));

            var builder = new StringBuilder();
            builder.Append("HTTP/1.1 ")
                .Append(response.StatusCode.ToString(CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(string.IsNullOrEmpty(response.ReasonPhrase) ? GetReason(response.StatusCode) : response.ReasonPhrase)
                .Append("\r\n");

            if (!string.IsNullOrEmpty(response.ContentType) && body.Length > 0)
            {
                builder.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
            }

            builder.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            builder.Append("Date: ").Append(DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture)).Append("\r\n");
            builder.Append("Connection: ").Append(keepAlive ? "keep-alive" : "close").Append("\r\n");

            // The Stream Deck plugin runs from file://, so every response is cross origin.
            if (!response.Headers.ContainsKey("Access-Control-Allow-Origin"))
            {
                builder.Append("Access-Control-Allow-Origin: *\r\n");
            }

            if (!response.Headers.ContainsKey("Access-Control-Allow-Methods"))
            {
                builder.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
            }

            if (!response.Headers.ContainsKey("Access-Control-Allow-Headers"))
            {
                builder.Append("Access-Control-Allow-Headers: Content-Type\r\n");
            }

            if (!response.Headers.ContainsKey("Access-Control-Max-Age"))
            {
                builder.Append("Access-Control-Max-Age: 86400\r\n");
            }

            if (!response.Headers.ContainsKey("Cache-Control"))
            {
                builder.Append("Cache-Control: no-store\r\n");
            }

            foreach (var header in response.Headers)
            {
                builder.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
            }

            builder.Append("\r\n");

            byte[] headers = Encoding.ASCII.GetBytes(builder.ToString());
            stream.Write(headers, 0, headers.Length);
            if (!suppressBody && body.Length > 0)
            {
                stream.Write(body, 0, body.Length);
            }

            stream.Flush();
        }

        private static bool IsClientDisconnect(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (current is SocketException)
                {
                    var socketError = (SocketException)current;
                    switch (socketError.SocketErrorCode)
                    {
                        case SocketError.ConnectionReset:
                        case SocketError.ConnectionAborted:
                        case SocketError.Shutdown:
                        case SocketError.TimedOut:
                        case SocketError.Disconnecting:
                            return true;
                    }
                }

                if (current is ObjectDisposedException || current is System.IO.EndOfStreamException)
                {
                    return true;
                }

                if (current is IOException && current.InnerException == null)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetReason(int statusCode)
        {
            switch (statusCode)
            {
                case 200: return "OK";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 413: return "Payload Too Large";
                case 431: return "Request Header Fields Too Large";
                case 500: return "Internal Server Error";
                case 503: return "Service Unavailable";
                default: return "Status";
            }
        }

        private void Log(string message)
        {
            try
            {
                _log(message);
            }
            catch (Exception)
            {
                // Logging must never break the server.
            }
        }

        private sealed class HttpProtocolException : Exception
        {
            public HttpProtocolException(int statusCode, string message) : base(message)
            {
                StatusCode = statusCode;
            }

            public int StatusCode { get; private set; }
        }
    }
}
