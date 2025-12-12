using BlazorController.Server.Hubs;
using BlazorController.Server.Services;
using Microsoft.AspNetCore.ResponseCompression;

var builder = WebApplication.CreateBuilder(args);

// Thêm dịch vụ Razor, Server-Side Blazor và SignalR
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSignalR();
builder.Services.AddControllers();

// Thêm dịch vụ TCP Listener chạy ngầm
builder.Services.AddHostedService<TcpListenerService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseBlazorFrameworkFiles(); // Cần thiết cho Blazor WASM Hosted
app.UseStaticFiles();

app.UseRouting();

app.MapRazorPages();
app.MapControllers();
app.MapFallbackToFile("index.html"); // Cần thiết cho Blazor WASM Hosted

// Ánh xạ SignalR Hub
app.MapHub<ControlHub>("/controlhub");

app.Run();