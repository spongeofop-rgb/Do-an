using Microsoft.AspNetCore.SignalR;
using System.Net.Sockets;

namespace BlazorController.Server.Hubs // Đổi tên namespace cho khớp với project Server của bạn
{
    public class ControlHub : Hub
    {
        // Dictionary để lưu trữ các kết nối TargetApp đang hoạt động
        public static Dictionary<string, TcpClient> ActiveClients { get; } = new Dictionary<string, TcpClient>();

        // Phương thức nhận lệnh từ Frontend (Giao diện Web)
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

                        // Gửi Log xác nhận tới người điều khiển
                        await Clients.Caller.SendAsync("ReceiveLog", $"[SERVER] Gửi lệnh '{command}' tới {targetId}");
                    }
                    catch (Exception ex)
                    {
                        await Clients.Caller.SendAsync("ReceiveLog", $"[LỖI] Không gửi được lệnh tới {targetId}: {ex.Message}");
                    }
                }
                else
                {
                    // Loại bỏ Client đã mất kết nối
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