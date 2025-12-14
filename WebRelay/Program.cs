using WebRelay.Hubs;
using WebRelay.Services;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 1. THÊM DỊCH VỤ (SERVICES)
// ==========================================

// [QUAN TRỌNG] Thêm dịch vụ MVC để chạy được Giao diện (Views) & Controller
builder.Services.AddControllersWithViews();

builder.Services.AddCors(); // Thêm dịch vụ CORS

// Tăng giới hạn gói tin SignalR lên 10MB để nhận được ảnh Webcam/Screenshot
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB
    options.EnableDetailedErrors = true;
});

// Chạy dịch vụ lắng nghe TCP (cổng 9999) ngầm để nhận kết nối từ TargetApp
builder.Services.AddHostedService<TcpListenerService>();

var app = builder.Build();

// ==========================================
// 2. CẤU HÌNH MIDDLEWARE (PIPELINE)
// ==========================================

// Cấu hình CORS mở rộng (Quan trọng để tránh lỗi kết nối từ nguồn khác)
app.UseCors(x => x
    .AllowAnyMethod()
    .AllowAnyHeader()
    .SetIsOriginAllowed(origin => true) // Cho phép mọi nguồn
    .AllowCredentials());

// [BẮT BUỘC] Để Server đọc được file CSS/JS/Hình ảnh trong thư mục wwwroot
// Nếu thiếu dòng này -> Web sẽ bị vỡ giao diện (trắng trơn)
app.UseStaticFiles();

app.UseRouting();

// [QUAN TRỌNG] Cấu hình đường dẫn mặc định cho Web
// Khi vào trang chủ (/) -> Nó sẽ tìm HomeController -> Hàm Index
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Ánh xạ đường dẫn SignalR (WebSocket)
app.MapHub<ControlHub>("/controlhub");

// Chạy Server
app.Run();