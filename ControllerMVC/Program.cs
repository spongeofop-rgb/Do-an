var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// ******* KHẮC PHỤC SỰ CỐ GIAO THỨC *******
// BỎ DÒNG UseHttpsRedirection() ĐỂ TRÁNH XUNG ĐỘT KHI KẾT NỐI ĐẾN BACKEND HTTP
// app.UseHttpsRedirection(); // <--- DÒNG NÀY ĐÃ BỊ LOẠI BỎ/COMMENT

app.UseStaticFiles();

app.UseRouting();

// Thêm CORS nếu bạn gặp lỗi kết nối (chỉ cần thiết nếu Frontend gọi Hub trong cùng Project)
// app.UseCors(policy => policy
//    .AllowAnyHeader()
//    .AllowAnyMethod()
//    .AllowAnyOrigin()); 

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();