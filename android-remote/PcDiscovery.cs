using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Net;
using Android.Net.Wifi;

namespace A2dpRemote;

public static class PcDiscovery
{
    public const int DiscoveryPort = 8080;

    public static async Task<List<(string Ip, string Name)>> DiscoverAsync(string? sharedKey = null, int timeoutMs = 3000)
    {
        var found = new List<(string Ip, string Name)>();
        WifiManager.MulticastLock? multicastLock = AcquireMulticastLock();

        try
        {
            using var udp = new UdpClient();
            using var timeoutCts = new CancellationTokenSource(timeoutMs);

            string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            byte[] query = Encoding.ASCII.GetBytes("C4P_DISCOVER " + nonce + "\n");

            foreach (IPEndPoint target in GetBroadcastTargets())
            {
                try
                {
                    await udp.SendAsync(query, query.Length, target);
                }
                catch
                {
                }
            }

            while (!timeoutCts.IsCancellationRequested)
            {
                UdpReceiveResult reply;
                try
                {
                    reply = await udp.ReceiveAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    continue;
                }

                (string ip, string name)? entry = ParseAnnounce(reply, nonce, sharedKey);

                if (entry is { } value && !string.IsNullOrEmpty(value.name) && !found.Exists(item => item.Ip == value.ip))
                    found.Add(value);
            }
        }
        finally
        {
            multicastLock?.Release();
        }

        return found;
    }

    private static (string Ip, string Name)? ParseAnnounce(UdpReceiveResult reply, string nonce, string? sharedKey)
    {
        try
        {
            string text = Encoding.ASCII.GetString(reply.Buffer).Trim();
            if (!text.StartsWith("C4P_ANNOUNCE ", StringComparison.Ordinal))
                return null;

            string rest = text["C4P_ANNOUNCE ".Length..];

            int separator = rest.LastIndexOf('|');
            if (separator <= 0)
            {
                if (!string.IsNullOrEmpty(sharedKey))
                    return null;

                return (reply.RemoteEndPoint.Address.ToString(), rest.Trim());
            }

            string name = rest[..separator].Trim();
            string tag = rest[(separator + 1)..].Trim();

            if (string.IsNullOrEmpty(name))
                return null;

            if (string.IsNullOrEmpty(sharedKey))
                return (reply.RemoteEndPoint.Address.ToString(), name);

            byte[] expected = ComputeAnnounceMac(nonce, sharedKey);
            byte[] provided;

            try
            {
                provided = Convert.FromHexString(tag);
            }
            catch (FormatException)
            {
                return null;
            }

            if (expected.Length != provided.Length || !CryptographicOperations.FixedTimeEquals(expected, provided))
                return null;

            return (reply.RemoteEndPoint.Address.ToString(), name);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] ComputeAnnounceMac(string nonceHex, string sharedKey)
    {
        byte[] prefixBytes = Encoding.ASCII.GetBytes("C4P_ANNOUNCE");
        byte[] nonceBytes = Convert.FromHexString(nonceHex);
        byte[] material = new byte[prefixBytes.Length + nonceBytes.Length];
        prefixBytes.CopyTo(material, 0);
        nonceBytes.CopyTo(material, prefixBytes.Length);

        using var hmac = new HMACSHA256(Encoding.ASCII.GetBytes(sharedKey.Trim()));
        return hmac.ComputeHash(material);
    }

    private static WifiManager.MulticastLock? AcquireMulticastLock()
    {
        try
        {
            var wifi = (WifiManager?)Application.Context.GetSystemService(Context.WifiService);
            WifiManager.MulticastLock? acquired = wifi?.CreateMulticastLock("c4p_discovery");
            acquired?.SetReferenceCounted(false);
            acquired?.Acquire();
            return acquired;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<IPEndPoint> GetBroadcastTargets()
    {
        var targets = new List<IPEndPoint> { new(IPAddress.Broadcast, DiscoveryPort) };

        try
        {
            var wifi = (WifiManager?)Application.Context.GetSystemService(Context.WifiService);
            DhcpInfo? dhcp = wifi?.DhcpInfo;

            if (dhcp is not null && dhcp.IpAddress != 0 && dhcp.Netmask != 0)
            {
                int broadcast = (dhcp.IpAddress & dhcp.Netmask) | ~dhcp.Netmask;
                string address = FormatIpAddress(broadcast);

                if (!string.IsNullOrEmpty(address))
                    targets.Add(new IPEndPoint(IPAddress.Parse(address), DiscoveryPort));
            }
        }
        catch
        {
        }

        return targets;
    }

    private static string FormatIpAddress(int value)
    {
        return $"{value & 0xFF}.{(value >> 8) & 0xFF}.{(value >> 16) & 0xFF}.{(value >> 24) & 0xFF}";
    }
}
