using System.Net;
using System.Net.Sockets;

namespace A2dpRemote;

public static class PcPairing
{
    public const string Prefix = "C4P2";

    public static bool TryParse(string? payload, out string[] ips, out int port, out string key, out string? mac)
    {
        ips = [];
        port = 0;
        key = string.Empty;
        mac = null;

        if (string.IsNullOrWhiteSpace(payload))
            return false;

        string[] parts = payload.Trim().Split('|');

        if (parts.Length != 5 || parts[0] != Prefix)
            return false;

        var list = new List<string>();

        foreach (string candidate in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPAddress.TryParse(candidate, out IPAddress? ip)
                && ip.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(ip)
                && !list.Contains(candidate))
            {
                list.Add(candidate);
            }
        }

        if (list.Count == 0 || !int.TryParse(parts[2], out port) || port is < 1 or > 65535)
            return false;

        key = parts[3].Trim();

        if (key.Length != 64 || !key.All(c => Uri.IsHexDigit(c)))
            return false;

        mac = parts[4].Trim();

        if (mac.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(mac, @"^([0-9A-F]{2}:){5}[0-9A-F]{2}$"))
            return false;

        if (mac.Length == 0)
            mac = null;

        ips = list.ToArray();
        return true;
    }
}
