using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System;

namespace ControllerMVC.Hubs
{
    public class ControlHub : Hub
    {
        // Danh sách lưu các kết nối TCP đang sống (Dùng chung cho cả Server)
        public static ConcurrentDictionary<string, TcpClient> ActiveClients = new ConcurrentDictionary<string, TcpClient>();

        // HÀM NÀY ĐƯỢC GỌI KHI BẠN BẤM NÚT TRÊN WEB
        public async Task SendCommandToClient(string targetIp, string command, string param)
        {
            // 1. Tìm xem Target có đang kết nối không
            if (ActiveClients.TryGetValue(targetIp, out TcpClient client))
            {
                if (client != null && client.Connected)
                {
                    try
                    {
                        var stream = client.GetStream();

                        // 2. Ghép lệnh (Ví dụ: "START|notepad" hoặc "WEBCAM_ON|")
                        string fullCommand = string.IsNullOrEmpty(param) ? command : $"{command}|{param}";

                        // 3. Chuyển sang byte
                        byte[] data = Encoding.UTF8.GetBytes(fullCommand);

                        // 4. Gửi sang TargetApp
                        await stream.WriteAsync(data, 0, data.Length);
                        await stream.FlushAsync();

                        // 5. Báo lại Web là đã gửi
                        // await Clients.Caller.SendAsync("ReceiveLog", $"[SERVER] -> [{targetIp}]: {fullCommand}");
                    }
                    catch (Exception ex)
                    {
                        await Clients.Caller.SendAsync("ReceiveLog", $"[LOI GUI LENH]: {ex.Message}");
                    }
                }
                else
                {
                    await Clients.Caller.SendAsync("ReceiveLog", $"[LOI]: Target {targetIp} da mat ket noi.");
                }
            }
            else
            {
                await Clients.Caller.SendAsync("ReceiveLog", $"[LOI]: Khong tim thay Target {targetIp}.");
            }
        }
    }
}