using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace A2dpSink;

internal sealed class CommandServer
{
    public const int DefaultPort = 8080;

    private readonly AudioSinkService _sink;
    private TcpListener? _listener;

    public CommandServer(AudioSinkService sink)
    {
        _sink = sink;
    }

    public void Start(int port = DefaultPort)
    {
        if (_listener is not null)
            return;

        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start();
        _ = AcceptLoopAsync(_listener);
    }

    public async Task StopAsync()
    {
        var listener = _listener;
        _listener = null;

        if (listener is null)
            return;

        try
        {
            listener.Stop();
        }
        catch
        {
        }

        await Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync();
            }
            catch (SocketException)
            {
                continue;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                NetworkStream stream = client.GetStream();

                string request = await ReadLineAsync(stream, timeoutCts.Token);
                string response = await DispatchAsync(request, CancellationToken.None);

                byte[] payload = Encoding.ASCII.GetBytes(response + "\n");
                await stream.WriteAsync(payload, timeoutCts.Token);
                await stream.FlushAsync(timeoutCts.Token);
            }
            catch
            {
            }
        }
    }

    private async Task<string> DispatchAsync(string request, CancellationToken ct)
    {
        string command = request.Trim().ToUpperInvariant();

        OpResult result;
        switch (command)
        {
            case "CONNECT":
                result = await _sink.ConnectAsync(ct);
                return Line(result);

            case "DISCONNECT":
                result = await _sink.DisconnectAsync(ct);
                return Line(result);

            case "PAUSE_FORWARD":
                result = await _sink.PauseForwardingAsync();
                return Line(result);

            case "RESUME_FORWARD":
                result = await _sink.ResumeForwardingAsync();
                return Line(result);

            case "STATUS":
                return "STATUS " + _sink.CurrentStatus.ToProtocolText();

            default:
                return $"ERR UNKNOWN_COMMAND \"{command}\"";
        }
    }

    private static string Line(OpResult result) => (result.Success ? "OK " : "ERR ") + result.Detail;

    private static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new List<byte>(32);

        while (buffer.Count < 128)
        {
            byte[] one = new byte[1];
            int read = await stream.ReadAsync(one.AsMemory(0, 1), ct);

            if (read == 0)
                break;

            if (one[0] == (byte)'\n')
                break;

            if (one[0] != (byte)'\r')
                buffer.Add(one[0]);
        }

        return Encoding.ASCII.GetString(buffer.ToArray());
    }
}
