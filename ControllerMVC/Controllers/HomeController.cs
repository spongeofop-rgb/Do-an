using Microsoft.AspNetCore.Mvc;

namespace ControllerMVC.Controllers // Đảm bảo namespace này khớp với Project MVC của bạn
{
    public class HomeController : Controller
    {
        // Action này sẽ xử lý yêu cầu GET khi người dùng truy cập /Home/Index hoặc /
        public IActionResult Index()
        {
            // Trả về View tương ứng: Views/Home/Index.cshtml
            return View();
        }

        /* * LƯU Ý: Không cần Action 'Send' cũ ở đây,
         * vì logic gửi lệnh sẽ được xử lý hoàn toàn bằng JavaScript/SignalR 
        */
    }
}