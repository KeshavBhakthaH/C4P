using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace A2dpSink;

internal sealed class DiscoveryResponder
{
    public const int DefaultPort = CommandServer.DefaultPort;

    private const string QueryPrefix = "C4P_DISCOVER ";
    private const string AnnouncePrefix = "C4P_ANNOUNCE";

    private UdpClient? _udp;
    private byte[] _secretKey = [];

    public void Start(int port = DefaultPort)
    {
        if (_udp is not null)
            return;

        _secretKey = Encoding.ASCII.GetBytes(PairingKey.Secret);
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        _ = ReceiveLoopAsync(_udp);
    }

    public void Stop()
    {
        UdpClient? udp = _udp;
        _udp = null;

        if (udp is null)
            return;

        try
        {
            udp.Close();
            udp.Dispose();
        }
        catch
        {
        }
    }

    private async Task ReceiveLoopAsync(UdpClient udp)
    {
        while (true)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }
            catch
            {
                return;
            }

            HandleQuery(result, udp, _secretKey);
        }
    }

    private static void HandleQuery(UdpReceiveResult result, UdpClient udp, byte[] secretKey)
    {
        try
        {
            string message = Encoding.ASCII.GetString(result.Buffer).Trim();

            if (!message.StartsWith(QueryPrefix, StringComparison.Ordinal))
                return;

            string nonceHex = message[QueryPrefix.Length..].Trim();

            if (nonceHex.Length is < 16 or > 64 || nonceHex.Length % 2 != 0 || !IsHex(nonceHex))
                return;

            if (!NetGuard.IsPrivateIp(result.RemoteEndPoint.Address))
                return;

            byte[] nonce = Convert.FromHexString(nonceHex);

            byte[] prefixBytes = Encoding.ASCII.GetBytes(AnnouncePrefix);
            byte[] material = new byte[prefixBytes.Length + nonce.Length];
            prefixBytes.CopyTo(material, 0);
            nonce.CopyTo(material, prefixBytes.Length);

            string tag;
            using (var hmac = new HMACSHA256(secretKey))
            {
                tag = Convert.ToHexString(hmac.ComputeHash(material));
            }

            byte[] reply = Encoding.ASCII.GetBytes(
                AnnouncePrefix + " " + Environment.MachineName + "|" + tag + "\n");

            _ = udp.SendAsync(reply, reply.Length, result.RemoteEndPoint);
        }
        catch
        {
        }
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
}
