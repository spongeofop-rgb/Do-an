using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Sockets;
using System.Text;

namespace WebController.Controllers
{
    public class HomeController : Controller
    {
        const string IP = "127.0.0.1";
        const int PORT = 9999;

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public JsonResult Send(string cmd)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    var result = client.BeginConnect(IP, PORT, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));

                    if (!success) return Json(new { status = "error", data = "Không tìm thấy Server! Hãy chạy TargetApp.exe (Admin) trước." });

                    client.EndConnect(result);
                    NetworkStream stream = client.GetStream();
                    byte[] data = Encoding.UTF8.GetBytes(cmd);
                    stream.Write(data, 0, data.Length);
                    byte[] buffer = new byte[1024 * 10000]; 
                    int bytes = stream.Read(buffer, 0, buffer.Length);
                    string response = Encoding.UTF8.GetString(buffer, 0, bytes);

                    return Json(new { status = "ok", data = response });
                }
            }
            catch (Exception ex)
            {
                return Json(new { status = "error", data = "Lỗi kết nối: " + ex.Message });
            }
        }
    }
}