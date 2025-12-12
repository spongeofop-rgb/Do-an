using Microsoft.AspNetCore.SignalR;
using System.Net.Sockets;

namespace WebRelay.Hubs // Namespace đã được sửa
{
    public class ControlHub : Hub
    {
        public static Dictionary<string, TcpClient> ActiveClients { get; } = new Dictionary<string, TcpClient>();

        public async Task SendCommand(string targetId, string command)
        {
            if (ActiveClients.TryGetValue(targetId, out TcpClient? client))
            {
                if (client.Connected)
                {
                    try
                    {
                        var stream = client.GetStream();
                        var data = System.Text.Encoding.UTF8.GetBytes(command);
                        await stream.WriteAsync(data, 0, data.Length);

                        await Clients.Caller.SendAsync("ReceiveLog", $"[SERVER] Gửi lệnh '{command}' tới {targetId}");
                    }
                    catch (Exception ex)
                    {
                        await Clients.Caller.SendAsync("ReceiveLog", $"[LỖI] Không gửi được lệnh tới {targetId}: {ex.Message}");
                    }
                }
                else
                {
                    ActiveClients.Remove(targetId);
                    await Clients.All.SendAsync("ClientDisconnected", targetId);
                }
            }
            else
            {
                await Clients.Caller.SendAsync("ReceiveLog", $"[LỖI] Không tìm thấy Target ID: {targetId}");
            }
        }
    }
}