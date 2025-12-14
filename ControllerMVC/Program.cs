using ControllerMVC.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using System.Diagnostics; // Thư viện để mở trình duyệt

var builder = WebApplication.CreateBuilder(args);

// --- 1. THÊM DỊCH VỤ (CÓ CẤU HÌNH TĂNG GIỚI HẠN SIGNALR) ---
builder.Services.AddControllersWithViews();

// [QUAN TRỌNG] Tăng giới hạn gói tin SignalR lên 100MB để không chặn ảnh Camera
builder.Services.AddSignalR(e => {
    e.MaximumReceiveMessageSize = 100 * 1024 * 1024; // 100MB
    e.EnableDetailedErrors = true;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapHub<ControlHub>("/controlhub");

// --- 2. TỰ ĐỘNG MỞ TRÌNH DUYỆT KHI CHẠY ---
var url = "http://localhost:5000";
Task.Run(async () =>
{
    await Task.Delay(2000); // Đợi 2 giây cho Server khởi động xong
    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        Console.WriteLine($"-> DA TU DONG MO WEB TAI: {url}");
    }
    catch
    {
        Console.WriteLine($"-> VUI LONG MO WEB THU CONG: {url}");
    }
});

// --- 3. KHỞI CHẠY SERVER TCP ---
Task.Run(() => StartTcpServer(app.Services));

app.Run();

// --- LOGIC XỬ LÝ TCP (GIỮ NGUYÊN NHƯ CŨ) ---
void StartTcpServer(IServiceProvider services)
{
    TcpListener listener = new TcpListener(IPAddress.Any, 9999);
    listener.Start();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("=============================================");
    Console.WriteLine("SERVER TCP DANG LANG NGHE TAI CONG 9999");
    Console.WriteLine("=============================================");
    Console.ResetColor();

    while (true)
    {
        try
        {
            var client = listener.AcceptTcpClient();
            Task.Run(() => HandleClient(client, services));
        }
        catch { }
    }
}

async Task HandleClient(TcpClient client, IServiceProvider services)
{
    string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
    Console.WriteLine($"[!!!] KET NOI MOI TU: {clientIp}");
    ControlHub.ActiveClients[clientIp] = client;

    using (var scope = services.CreateScope())
    {
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ControlHub>>();
        await hub.Clients.All.SendAsync("ClientConnected", clientIp);

        NetworkStream stream = client.GetStream();
        try
        {
            while (client.Connected)
            {
                // Đọc 4 byte độ dài
                byte[] lengthBuffer = new byte[4];
                int bytesRead = await ReadExactAsync(stream, lengthBuffer, 4);
                if (bytesRead == 0) break;

                int packetSize = BitConverter.ToInt32(lengthBuffer, 0);

                // Đọc nội dung gói tin
                byte[] buffer = new byte[packetSize];
                await ReadExactAsync(stream, buffer, packetSize);

                string message = Encoding.UTF8.GetString(buffer);

                // Xử lý dữ liệu
                if (message.StartsWith("IMG|"))
                {
                    // [DEBUG] Báo log để biết Server đã nhận được ảnh
                    Console.WriteLine($"-> DA NHAN ANH TU {clientIp} ({packetSize} bytes)");

                    string base64 = message.Substring(4);
                    await hub.Clients.All.SendAsync("ReceiveWebcam", base64);
                }
                else
                {
                    await hub.Clients.All.SendAsync("ReceiveLog", $"[{DateTime.Now:T}] [{clientIp}] {message}");
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"LOI ({clientIp}): " + ex.Message); }
        finally
        {
            ControlHub.ActiveClients.TryRemove(clientIp, out _);
            client.Close();
            await hub.Clients.All.SendAsync("ReceiveLog", $"[DISCONNECTED] {clientIp} da ngat ket noi.");
        }
    }
}

async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int size)
{
    int totalRead = 0;
    while (totalRead < size)
    {
        int read = await stream.ReadAsync(buffer, totalRead, size - totalRead);
        if (read == 0) return 0;
        totalRead += read;
    }
    return totalRead;
}