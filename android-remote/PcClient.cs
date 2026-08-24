using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace A2dpRemote;

public static class PcClient
{
    public static async Task<string> SendAsync(string ip, int port, string command, string sharedKey, int timeoutMs = 8000)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sharedKey))
                return "ERR NO_KEY";

            using var client = new TcpClient();

            Task connectTask = client.ConnectAsync(ip, port);
            if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) != connectTask || !client.Connected)
                return "ERR NO_RESPONSE";

            await using var stream = client.GetStream();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
            CancellationToken ct = timeoutCts.Token;

            string challenge = await ReadLineAsync(stream, ct);
            if (!challenge.StartsWith("CHALLENGE ", StringComparison.Ordinal))
                return $"ERR UNEXPECTED_REPLY \"{challenge}\"";

            string nonceHex = challenge["CHALLENGE ".Length..].Trim();

            if (nonceHex.Length is < 16 or > 128 || nonceHex.Length % 2 != 0 || !IsHex(nonceHex))
                return "ERR MALFORMED_CHALLENGE";

            byte[] nonce = Convert.FromHexString(nonceHex);
            byte[] keyBytes = Encoding.ASCII.GetBytes(sharedKey.Trim());

            string authMac;
            using (var authHmac = new HMACSHA256(keyBytes))
            {
                authMac = Convert.ToHexString(authHmac.ComputeHash(nonce));
            }

            byte[] sessionKey;
            using (var deriveHmac = new HMACSHA256(keyBytes))
            {
                byte[] material = new byte[nonce.Length + 1];
                nonce.CopyTo(material, 0);
                material[^1] = 0x01;
                sessionKey = deriveHmac.ComputeHash(material);
            }

            await WriteLineAsync(stream, $"AUTH {authMac}", ct);

            string authReply = await ReadLineAsync(stream, ct);
            if (!authReply.StartsWith("OK", StringComparison.Ordinal))
                return $"ERR AUTH_FAILED \"{authReply}\"";

            string payload = command.Trim();
            string signedLine;
            using (var sessionHmac = new HMACSHA256(sessionKey))
            {
                string tag = Convert.ToHexString(sessionHmac.ComputeHash(Encoding.ASCII.GetBytes(payload)));
                signedLine = payload + "|" + tag;
            }

            await WriteLineAsync(stream, signedLine, ct);

            string reply = await ReadLineAsync(stream, ct);

            int separator = reply.LastIndexOf('|');
            if (separator <= 0)
                return $"ERR UNEXPECTED_REPLY \"{reply}\"";

            string body = reply[..separator];
            string replyTag = reply[(separator + 1)..].Trim();

            if (!VerifyMac(body, replyTag, sessionKey))
                return "ERR TAMPERED";

            return body;
        }
        catch (Exception ex)
        {
            return $"ERR {ex.Message}";
        }
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

        return expected.Length == provided.Length && CryptographicOperations.FixedTimeEquals(expected, provided);
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

    private static async Task WriteLineAsync(NetworkStream stream, string text, CancellationToken ct)
    {
        byte[] payload = Encoding.ASCII.GetBytes(text + "\n");
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new List<byte>(64);

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
