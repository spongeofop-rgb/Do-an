using Microsoft.AspNetCore.SignalR;
using System.Net.Sockets;
using System.Net;
using WebRelay.Hubs; // Namespace đã được sửa
using System.Text;
using Microsoft.Extensions.Hosting;

namespace WebRelay.Services // Namespace đã được sửa
{
    public class TcpListenerService : BackgroundService
    {
        private TcpListener _listener;
        private readonly IHubContext<ControlHub> _hubContext;
        private const int TCP_PORT = 9999;

        public TcpListenerService(IHubContext<ControlHub> hubContext)
        {
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, TCP_PORT);
                _listener.Start();

                await _hubContext.Clients.All.SendAsync("ReceiveLog", $"=== SERVER TCP ĐANG CHỜ KẾT NỐI (PORT {TCP_PORT}) ===");

                while (!stoppingToken.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync();

                    var clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();

                    ControlHub.ActiveClients[clientIp] = client;
                    await _hubContext.Clients.All.SendAsync("ReceiveLog", $"\n>>> KẾT NỐI THÀNH CÔNG! IP: {clientIp} <<<");
                    await _hubContext.Clients.All.SendAsync("ClientConnected", clientIp);

                    _ = Task.Run(() => HandleClient(client, clientIp, stoppingToken));
                }
            }
            catch (Exception ex)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveLog", $"LỖI LẮNG NGHE TCP: {ex.Message}");
            }
        }

        private async Task HandleClient(TcpClient client, string clientId, CancellationToken stoppingToken)
        {
            try
            {
                var stream = client.GetStream();
                byte[] buffer = new byte[1024 * 10000];

                while (client.Connected && !stoppingToken.IsCancellationRequested)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, stoppingToken);
                    if (bytesRead > 0)
                    {
                        string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        if (response.Contains("PID:"))
                        {
                            await _hubContext.Clients.All.SendAsync("ReceiveLog", $"[DS APP] Từ {clientId}");
                            await _hubContext.Clients.All.SendAsync("ReceiveLog", response);
                        }
                        else if (response.Length > 1000) // Ảnh Webcam
                        {
                            await _hubContext.Clients.All.SendAsync("ReceiveWebcam", response);
                        }
                        else if (!response.Contains("NO_IMAGE"))
                        {
                            await _hubContext.Clients.All.SendAsync("ReceiveLog", $"[{clientId}] {response}");
                        }
                    }
                }
            }
            catch (Exception)
            {
                ControlHub.ActiveClients.Remove(clientId);
                await _hubContext.Clients.All.SendAsync("ReceiveLog", $"[KẾT NỐI MẤT] Client {clientId} đã ngắt kết nối.");
                await _hubContext.Clients.All.SendAsync("ClientDisconnected", clientId);
            }
        }
    }
}