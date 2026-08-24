using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace A2dpSink;

public static class PairingQr
{
    public const string Prefix = "C4P2";

    private static readonly Task<string?> BluetoothMacTask = LoadBluetoothMacAsync();

    public static Task<string> BuildPayloadAsync()
    {
        return BuildPayloadInternalAsync();
    }

    public static bool TryParsePayload(
        string? payload,
        out IReadOnlyList<string> addresses,
        out int port,
        out string key,
        out string bluetoothMac)
    {
        addresses = [];
        port = 0;
        key = string.Empty;
        bluetoothMac = string.Empty;

        if (payload is null)
            return false;

        string[] parts = payload.Trim().Split('|');

        if (parts.Length != 5 || parts[0] != Prefix)
            return false;

        List<string> parsed = parts[1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(ip => IPAddress.TryParse(ip, out IPAddress? value)
                && value.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(value))
            .Distinct()
            .ToList();

        if (parsed.Count == 0 || !int.TryParse(parts[2], out port) || port is < 1 or > 65535)
            return false;

        key = parts[3].Trim();

        if (key.Length != 64 || key.Any(c => !Uri.IsHexDigit(c)))
            return false;

        bluetoothMac = parts[4].Trim();

        if (bluetoothMac.Length > 0 &&
            !System.Text.RegularExpressions.Regex.IsMatch(bluetoothMac, @"^([0-9A-F]{2}:){5}[0-9A-F]{2}$"))
        {
            return false;
        }

        addresses = parsed;
        return true;
    }

    public static List<string> GetPrivateIpv4Addresses()
    {
        var result = new SortedSet<string>();

        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    byte[] bytes = info.Address.GetAddressBytes();

                    if (IPAddress.IsLoopback(info.Address) ||
                        !(bytes[0] == 10 ||
                          (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                          (bytes[0] == 192 && bytes[1] == 168)))
                    {
                        continue;
                    }

                    result.Add(info.Address.ToString());
                }
            }
        }
        catch
        {
        }

        return result.ToList();
    }

    private static async Task<string> BuildPayloadInternalAsync()
    {
        List<string> addresses = GetPrivateIpv4Addresses();
        string? mac = await BluetoothMacTask.ConfigureAwait(false);

        return Prefix + "|" +
               string.Join(",", addresses) + "|" +
               CommandServer.DefaultPort + "|" +
               PairingKey.Secret + "|" +
               (string.IsNullOrWhiteSpace(mac) ? string.Empty : mac);
    }

    private static async Task<string?> LoadBluetoothMacAsync()
    {
        try
        {
            Windows.Devices.Bluetooth.BluetoothAdapter? adapter =
                await Windows.Devices.Bluetooth.BluetoothAdapter.GetDefaultAsync();

            if (adapter is null)
                return null;

            string hex = adapter.BluetoothAddress.ToString("X12");
            return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
        }
        catch
        {
            return null;
        }
    }
}
