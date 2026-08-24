using System;
using System.Net;

namespace A2dpSink;

internal static class NetGuard
{
    public static bool IsPrivateIp(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
            return true;

        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
            return false;

        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 169 && bytes[1] == 254);
    }
}
