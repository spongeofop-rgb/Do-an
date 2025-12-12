using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System;

namespace WebRelay.Hubs
{
    public class ControlHub : Hub
    {
        // Danh sách lưu trữ các Target đang kết nối
        public static ConcurrentDictionary<string, TcpClient> ActiveClients = new ConcurrentDictionary<string, TcpClient>();

        // --- QUAN TRỌNG: HÀM NÀY CHẠY KHI WEB VỪA KẾT NỐI ---
        public override async Task OnConnectedAsync()
        {
            // Duyệt qua danh sách Target đang có
            foreach (var clientIp in ActiveClients.Keys)
            {
                // Báo ngay cho Web biết: "Này, đang có Target này kết nối sẵn rồi nhé!"
                await Clients.Caller.SendAsync("ClientConnected", clientIp);
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }

        // Hàm gửi lệnh từ Web xuống Target
        public async Task SendCommand(string targetIp, string command)
        {
            if (ActiveClients.TryGetValue(targetIp, out TcpClient client))
            {
                if (client.Connected)
                {
                    try
                    {
                        NetworkStream stream = client.GetStream();
                        byte[] msg = Encoding.UTF8.GetBytes(command);
                        await stream.WriteAsync(msg, 0, msg.Length);
                        await Clients.Caller.SendAsync("ReceiveLog", $"[GỬI LỆNH] Đã gửi '{command}' tới {targetIp}");
                    }
                    catch (Exception ex)
                    {
                        await Clients.Caller.SendAsync("ReceiveLog", $"[LỖI] Không gửi được: {ex.Message}");
                    }
                }
                else
                {
                    ActiveClients.TryRemove(targetIp, out _);
                    await Clients.Caller.SendAsync("ClientDisconnected", targetIp);
                }
            }
            else
            {
                await Clients.Caller.SendAsync("ReceiveLog", $"[LỖI] Không tìm thấy Target {targetIp}");
            }
        }
    }
}