using WebRelay.Hubs;
using WebRelay.Services;

var builder = WebApplication.CreateBuilder(args);

// --- 1. THÊM DỊCH VỤ ---
builder.Services.AddCors(); // Thêm dịch vụ CORS

// Tăng giới hạn gói tin SignalR lên 10MB để nhận được ảnh Webcam/Screenshot
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB
    options.EnableDetailedErrors = true;
});

// Chạy dịch vụ lắng nghe TCP (cổng 9999) ngầm
builder.Services.AddHostedService<TcpListenerService>();

var app = builder.Build();

// --- 2. CẤU HÌNH MIDDLEWARE ---

// Cấu hình CORS mở rộng (Quan trọng để tránh lỗi "Failed to fetch")
app.UseCors(x => x
    .AllowAnyMethod()
    .AllowAnyHeader()
    .SetIsOriginAllowed(origin => true) // Cho phép mọi nguồn
    .AllowCredentials());

app.UseRouting();
app.UseStaticFiles(); // (Tùy chọn, để đó cũng không sao)

// Ánh xạ đường dẫn SignalR
app.MapHub<ControlHub>("/controlhub");

// Chạy Server
app.Run();