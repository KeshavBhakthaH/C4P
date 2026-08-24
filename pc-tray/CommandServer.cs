using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace A2dpSink;

internal sealed class CommandServer
{
    public const int DefaultPort = 8080;

    private const int MaxConcurrentClients = 8;
    private const int MaxFailuresPerIp = 5;
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(60);

    private static readonly SemaphoreSlim ConnectionSlots = new(MaxConcurrentClients, MaxConcurrentClients);

    private readonly AudioSinkService _sink;
    private readonly ConcurrentDictionary<string, FailureRecord> _failures = new();
    private TcpListener? _listener;
    private byte[] _secretKey = [];

    private sealed class FailureRecord
    {
        public int Count;
        public DateTime BlockedUntilUtc;
    }

    public CommandServer(AudioSinkService sink)
    {
        _sink = sink;
    }

    public void Start(int port = DefaultPort)
    {
        if (_listener is not null)
            return;

        _secretKey = Encoding.ASCII.GetBytes(PairingKey.Secret);

        _listener = new TcpListener(IPAddress.Any, port);
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

            if (!ConnectionSlots.Wait(0))
            {
                try
                {
                    client.Dispose();
                }
                catch
                {
                }

                continue;
            }

            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            await HandleClientCoreAsync(client);
        }
        finally
        {
            ConnectionSlots.Release();
        }
    }

    private async Task HandleClientCoreAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                if (client.Client.RemoteEndPoint is not IPEndPoint remote || !NetGuard.IsPrivateIp(remote.Address))
                    return;

                string remoteIp = remote.Address.ToString();

                if (IsBlocked(remoteIp))
                    return;

                client.NoDelay = true;

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                NetworkStream stream = client.GetStream();

                byte[] nonce = RandomNumberGenerator.GetBytes(32);
                await WriteLineAsync(stream, "CHALLENGE " + Convert.ToHexString(nonce), timeoutCts.Token);

                string authLine = await ReadLineAsync(stream, timeoutCts.Token);

                if (!IsAuthorized(authLine, nonce))
                {
                    RegisterFailure(remoteIp);
                    await WriteLineAsync(stream, "ERR AUTH_FAILED", timeoutCts.Token);
                    return;
                }

                ClearFailures(remoteIp);

                await WriteLineAsync(stream, "OK READY", timeoutCts.Token);

                byte[] sessionKey = DeriveSessionKey(nonce);

                string request = await ReadLineAsync(stream, timeoutCts.Token);
                string response = await DispatchVerifiedAsync(request, sessionKey, CancellationToken.None);

                await WriteLineAsync(stream, MacLine(response, sessionKey), timeoutCts.Token);
            }
            catch
            {
            }
        }
    }

    private bool IsAuthorized(string line, byte[] nonce)
    {
        string prefix = "AUTH ";

        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        string provided = line[prefix.Length..].Trim();

        if (provided.Length != 64)
            return false;

        byte[] providedBytes;
        try
        {
            providedBytes = Convert.FromHexString(provided);
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(_secretKey);
        byte[] expected = hmac.ComputeHash(nonce);

        return CryptographicOperations.FixedTimeEquals(expected, providedBytes);
    }

    private byte[] DeriveSessionKey(byte[] nonce)
    {
        byte[] material = new byte[nonce.Length + 1];
        nonce.CopyTo(material, 0);
        material[^1] = 0x01;

        using var hmac = new HMACSHA256(_secretKey);
        return hmac.ComputeHash(material);
    }

    private async Task<string> DispatchVerifiedAsync(string request, byte[] sessionKey, CancellationToken ct)
    {
        int separator = request.LastIndexOf('|');

        if (separator <= 0)
            return "ERR BAD_REQUEST";

        string command = request[..separator].Trim();
        string tag = request[(separator + 1)..].Trim();

        if (!VerifyMac(command, tag, sessionKey))
            return "ERR TAMPERED";

        return await DispatchAsync(command, ct);
    }

    private static bool VerifyMac(string message, string providedHex, byte[] macKey)
    {
        if (providedHex.Length != 64 || !IsHex(providedHex))
            return false;

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(providedHex);
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(macKey);
        byte[] expected = hmac.ComputeHash(Encoding.ASCII.GetBytes(message));

        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    private static string MacLine(string line, byte[] macKey)
    {
        using var hmac = new HMACSHA256(macKey);
        string tag = Convert.ToHexString(hmac.ComputeHash(Encoding.ASCII.GetBytes(line)));
        return line + "|" + tag;
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }

        return true;
    }

    private bool IsBlocked(string ip)
    {
        if (!_failures.TryGetValue(ip, out FailureRecord? record))
            return false;

        lock (record)
        {
            if (record.BlockedUntilUtc > DateTime.UtcNow)
                return true;

            if (record.BlockedUntilUtc != default)
                _failures.TryRemove(ip, out _);

            return false;
        }
    }

    private void RegisterFailure(string ip)
    {
        FailureRecord record = _failures.GetOrAdd(ip, _ => new FailureRecord());

        lock (record)
        {
            record.Count++;

            if (record.Count >= MaxFailuresPerIp)
            {
                record.BlockedUntilUtc = DateTime.UtcNow + FailureCooldown;
                record.Count = 0;
            }
        }

        if (_failures.Count > 512)
        {
            foreach (KeyValuePair<string, FailureRecord> entry in _failures)
            {
                lock (entry.Value)
                {
                    if (entry.Value.BlockedUntilUtc != default && entry.Value.BlockedUntilUtc < DateTime.UtcNow)
                        _failures.TryRemove(entry.Key, out _);
                }
            }
        }
    }

    private void ClearFailures(string ip)
    {
        _failures.TryRemove(ip, out _);
    }

    private static async Task WriteLineAsync(NetworkStream stream, string text, CancellationToken ct)
    {
        byte[] payload = Encoding.ASCII.GetBytes(text + "\n");
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
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
