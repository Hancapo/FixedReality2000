using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace FixedReality2000;

internal sealed partial class RuntimeInspectorBridge : IDisposable
{
    private const int Port = 53987;
    private readonly ConcurrentQueue<PendingRequest> _requests = new();
    private TcpListener? _listener;
    private Thread? _listenerThread;
    private volatile bool _running;

    internal void Start()
    {
        if (_running)
        {
            return;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            _running = true;
            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "FixedReality2000 Runtime Inspector"
            };
            _listenerThread.Start();
            Plugin.Log.LogInfo(
                $"UnityExplorer MCP runtime bridge listening on 127.0.0.1:{Port}.");
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(
                $"Could not start the UnityExplorer MCP runtime bridge: {exception}");
            Dispose();
        }
    }

    internal void Tick()
    {
        int processed = 0;
        while (processed < 16 && _requests.TryDequeue(out PendingRequest? request))
        {
            try
            {
                request.Response = Execute(request.Path, request.Query);
                request.StatusCode = 200;
            }
            catch (Exception exception)
            {
                request.Response =
                    $"{{\"ok\":false,\"error\":{Json(exception.ToString())}}}";
                request.StatusCode = 500;
            }
            finally
            {
                request.Completed.Set();
            }

            processed++;
        }
    }

    public void Dispose()
    {
        _running = false;
        try
        {
            _listener?.Stop();
        }
        catch
        {
            // The listener may already be stopped during game shutdown.
        }

        while (_requests.TryDequeue(out PendingRequest? request))
        {
            request.Response =
                "{\"ok\":false,\"error\":\"The game bridge is shutting down.\"}";
            request.StatusCode = 503;
            request.Completed.Set();
        }

        _listener = null;
        _listenerThread = null;
    }

    private void ListenLoop()
    {
        while (_running && _listener != null)
        {
            try
            {
                TcpClient client = _listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
            catch (SocketException)
            {
                if (_running)
                {
                    Plugin.Log.LogWarning("The runtime inspector listener stopped unexpectedly.");
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning($"Runtime inspector listener error: {exception.Message}");
            }
        }
    }

    private void HandleClient(TcpClient client)
    {
        using (client)
        using (NetworkStream stream = client.GetStream())
        using (var reader = new StreamReader(
                   stream,
                   Encoding.ASCII,
                   detectEncodingFromByteOrderMarks: false,
                   bufferSize: 4096,
                   leaveOpen: true))
        {
            try
            {
                client.ReceiveTimeout = 12000;
                client.SendTimeout = 12000;
                string? requestLine = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    return;
                }

                string[] parts = requestLine.Split(' ');
                if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.Ordinal))
                {
                    WriteResponse(stream, 405, "{\"ok\":false,\"error\":\"GET only\"}");
                    return;
                }

                string? header;
                do
                {
                    header = reader.ReadLine();
                }
                while (!string.IsNullOrEmpty(header));

                ParseTarget(parts[1], out string path, out Dictionary<string, string> query);
                var pending = new PendingRequest(path, query);
                _requests.Enqueue(pending);
                if (!pending.Completed.WaitOne(10000))
                {
                    WriteResponse(
                        stream,
                        504,
                        "{\"ok\":false,\"error\":\"The Unity main thread did not answer in time.\"}");
                    return;
                }

                WriteResponse(stream, pending.StatusCode, pending.Response);
            }
            catch (Exception exception)
            {
                try
                {
                    WriteResponse(
                        stream,
                        500,
                        $"{{\"ok\":false,\"error\":{Json(exception.Message)}}}");
                }
                catch
                {
                    // The client disconnected.
                }
            }
        }
    }

    private static void WriteResponse(NetworkStream stream, int statusCode, string body)
    {
        byte[] payload = Encoding.UTF8.GetBytes(body);
        string status = statusCode == 200 ? "OK" : "Error";
        byte[] headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {status}\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            "Connection: close\r\n" +
            "Access-Control-Allow-Origin: *\r\n\r\n");
        stream.Write(headers, 0, headers.Length);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    private static void ParseTarget(
        string target,
        out string path,
        out Dictionary<string, string> query)
    {
        int separator = target.IndexOf('?');
        path = separator >= 0 ? target.Substring(0, separator) : target;
        query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (separator < 0 || separator + 1 >= target.Length)
        {
            return;
        }

        foreach (string pair in target.Substring(separator + 1).Split('&'))
        {
            if (string.IsNullOrEmpty(pair))
            {
                continue;
            }

            int equals = pair.IndexOf('=');
            string key = equals >= 0 ? pair.Substring(0, equals) : pair;
            string value = equals >= 0 ? pair.Substring(equals + 1) : string.Empty;
            query[Decode(key)] = Decode(value);
        }
    }

    private static string Decode(string value)
    {
        return Uri.UnescapeDataString(value.Replace('+', ' '));
    }


    private sealed class PendingRequest
    {
        internal PendingRequest(string path, Dictionary<string, string> query)
        {
            Path = path;
            Query = query;
        }

        internal string Path { get; }

        internal Dictionary<string, string> Query { get; }

        internal AutoResetEvent Completed { get; } = new(false);

        internal string Response { get; set; } =
            "{\"ok\":false,\"error\":\"No response.\"}";

        internal int StatusCode { get; set; } = 500;
    }
}
