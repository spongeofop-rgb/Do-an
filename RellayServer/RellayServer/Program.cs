using WebRelay.Hubs;       // Namespace đã được sửa
using WebRelay.Services;    // Namespace đã được sửa
using Microsoft.AspNetCore.Components.WebAssembly.Server;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.ResponseCompression;

var builder = WebApplication.CreateBuilder(args);

// Thêm dịch vụ Razor, Server-Side Blazor và SignalR
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();

// Thêm dịch vụ TCP Listener chạy ngầm
builder.Services.AddHostedService<TcpListenerService>();

var app = builder.Build();

// Cấu hình Blazor WASM Hosted
app.UseBlazorFrameworkFiles(); // Khắc phục lỗi CS1061
app.UseStaticFiles();


if (app.Environment.IsDevelopment())
{
    // Lỗi CS1061 được giải quyết
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Cấu hình CORS
app.UseCors(policy => policy
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowAnyOrigin());


app.UseRouting();

app.MapRazorPages();
app.MapControllers();

// Ánh xạ SignalR Hub
app.MapHub<ControlHub>("/controlhub");

app.MapFallbackToFile("index.html"); // Cho Blazor WebAssembly

app.Run();