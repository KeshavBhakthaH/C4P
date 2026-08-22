using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace A2dpRemote;

public static class PcClient
{
    public static async Task<string> SendAsync(string ip, int port, string command, int timeoutMs = 8000)
    {
        try
        {
            using var client = new TcpClient();

            Task connectTask = client.ConnectAsync(ip, port);
            if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) != connectTask || !client.Connected)
                return "ERR NO_RESPONSE";

            await using var stream = client.GetStream();
            stream.ReadTimeout = timeoutMs;

            byte[] payload = Encoding.ASCII.GetBytes(command + "\n");
            await stream.WriteAsync(payload);

            var buffer = new byte[256];
            int read = await stream.ReadAsync(buffer);
            return Encoding.ASCII.GetString(buffer, 0, read).Trim();
        }
        catch (Exception ex)
        {
            return $"ERR {ex.Message}";
        }
    }
}
