using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SmsTunnel.Tests;

/// <summary>A real HTTP/1.1 server on 127.0.0.1 with a random port, for tests that need actual sockets.</summary>
public sealed class TinyHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<string, string> _respond;
    private readonly Task _loop;

    public TinyHttpServer(Func<string, string> respond)
    {
        _respond = respond;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _loop = Task.Run(AcceptAsync);
    }

    public ConcurrentQueue<string> Requests { get; } = new();

    public Uri Url => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");

    public static string Response(int status, string reason, string body = "", string? extraHeaders = null)
    {
        int length = Encoding.UTF8.GetByteCount(body);
        return $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {length}\r\nConnection: close\r\n{extraHeaders}\r\n{body}";
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private async Task AcceptAsync()
    {
        while (true)
        {
            TcpClient connection;
            try
            {
                connection = await _listener.AcceptTcpClientAsync();
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(() => AnswerAsync(connection));
        }
    }

    private async Task AnswerAsync(TcpClient connection)
    {
        using (connection)
        {
            NetworkStream stream = connection.GetStream();
            var received = new MemoryStream();
            var buffer = new byte[8192];
            int headerEnd;
            while ((headerEnd = IndexOfBlankLine(received)) < 0)
            {
                int read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    return;
                }

                received.Write(buffer, 0, read);
            }

            string head = Encoding.ASCII.GetString(received.GetBuffer(), 0, headerEnd);
            int contentLength = 0;
            foreach (string line in head.Split("\r\n"))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    contentLength = int.Parse(line.AsSpan("Content-Length:".Length).Trim(), CultureInfo.InvariantCulture);
                }
            }

            while (received.Length < headerEnd + 4 + contentLength)
            {
                int read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                received.Write(buffer, 0, read);
            }

            string request = Encoding.UTF8.GetString(received.ToArray());
            Requests.Enqueue(request);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(_respond(request)));
        }
    }

    private static int IndexOfBlankLine(MemoryStream stream)
    {
        byte[] bytes = stream.GetBuffer();
        for (int i = 0; i + 3 < stream.Length; i++)
        {
            if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
            {
                return i;
            }
        }

        return -1;
    }
}
