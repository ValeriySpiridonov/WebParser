using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WebParser.Tests;

internal sealed class StubHttpServer : IDisposable
{
    public sealed class Response
    {
        public int StatusCode { get; init; } = 200;
        public string? ContentType { get; init; }
        public byte[]? Body { get; init; }
        public int DelayMs { get; init; }

        public static Response Html(string html, int statusCode = 200) => new()
        {
            StatusCode = statusCode,
            ContentType = "text/html",
            Body = Encoding.UTF8.GetBytes(html)
        };

        public static Response Raw(byte[] body, string? contentType, int statusCode = 200) => new()
        {
            StatusCode = statusCode,
            ContentType = contentType,
            Body = body
        };
    }

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<string, Response?> _handler;
    private readonly ConcurrentDictionary<string, int> _hits = new();
    private readonly Task _acceptLoop;
    private int _inflight;
    private int _peak;

    public StubHttpServer(Func<string, Response?> handler)
    {
        _handler = handler;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUrl = $"http://127.0.0.1:{Port}/";
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }
    public string BaseUrl { get; }

    public int Hits(string path) => _hits.TryGetValue(path, out int count) ? count : 0;

    public int PeakConcurrency => Volatile.Read(ref _peak);

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch
        {
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using (client)
            {
                int now = Interlocked.Increment(ref _inflight);
                UpdatePeak(now);
                try
                {
                    using NetworkStream stream = client.GetStream();
                    string? requestLine = await ReadLineAsync(stream, _cts.Token);
                    if (requestLine is null)
                    {
                        return;
                    }

                    string[] parts = requestLine.Split(' ');
                    string path = parts.Length > 1 ? parts[1] : "/";

                    while (!(await ReadLineAsync(stream, _cts.Token) is { Length: 0 }))
                    {
                    }

                    _hits.AddOrUpdate(path, 1, (_, count) => count + 1);
                    Response? response = _handler(path) ?? new Response { StatusCode = 404 };

                    if (response.DelayMs > 0)
                    {
                        await Task.Delay(response.DelayMs, _cts.Token);
                    }

                    await WriteResponseAsync(stream, response, _cts.Token);
                }
                finally
                {
                    Interlocked.Decrement(ref _inflight);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken token)
    {
        var line = new StringBuilder();
        var buffer = new byte[1];
        int prev = -1;

        while (true)
        {
            int read = await stream.ReadAsync(buffer, 0, 1, token);
            if (read == 0)
            {
                return line.Length == 0 ? null : line.ToString();
            }

            if (buffer[0] == '\n')
            {
                if (prev == '\r')
                {
                    line.Length--;
                }
                return line.ToString();
            }

            line.Append((char)buffer[0]);
            prev = buffer[0];
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, Response response, CancellationToken token)
    {
        byte[] body = response.Body ?? Array.Empty<byte>();
        var head = new StringBuilder();
        head.Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ').Append(StatusText(response.StatusCode)).Append("\r\n");
        head.Append("Content-Type: ").Append(response.ContentType ?? "application/octet-stream").Append("\r\n");
        head.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        head.Append("Connection: close\r\n\r\n");

        byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
        await stream.WriteAsync(headBytes, 0, headBytes.Length, token);
        if (body.Length > 0)
        {
            await stream.WriteAsync(body, 0, body.Length, token);
        }
        await stream.FlushAsync(token);
    }

    private static string StatusText(int code) => code switch
    {
        200 => "OK",
        404 => "Not Found",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        _ => "Unknown"
    };

    private void UpdatePeak(int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref _peak);
            if (value <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _peak, value, current) != current);
    }
}