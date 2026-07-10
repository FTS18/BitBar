using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Linq;

namespace BitBar
{
    public partial class Form1 : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool GlobalMemoryStatusEx([In, Out] ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        private System.Windows.Forms.Timer timer;
        private Label speedLabel;
        private long previousReceived = 0;
        private long previousSent = 0;
        private NetworkInterface activeInterface;
        
        private ContextMenuStrip contextMenu;
        private NotifyIcon trayIcon;
        private bool cycleMode = false;
        private int cycleCounter = 0;
        
        public string selectedAdapterId = "";
        public bool useBits = false;
        public int refreshInterval = 1000;

        private FlyoutForm flyout;
        private NetworkConsumerTracker consumerTracker;
        private Queue<long> downHistory = new Queue<long>();
        private Queue<long> upHistory = new Queue<long>();
        private const int MaxHistory = 60;

        private ulong prevIdleTime, prevKernelTime, prevUserTime;
        private const string RegKey = @"Software\BitBar";
        private bool isHovered = false;
        private System.Windows.Forms.Timer hoverTimer;

        public Form1()
        {
            InitializeComponent();
            LoadPreferences();
            SetupUI();
            EmbedInTaskbar();
            SetupNetworkMonitoring();
            GetCpuUsage(); // initialize counters
        }

        private void LoadPreferences()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegKey))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("CycleMode");
                        if (val != null) cycleMode = (int)val == 1;

                        var bitsVal = key.GetValue("UseBits");
                        if (bitsVal != null) useBits = (int)bitsVal == 1;

                        var intervalVal = key.GetValue("RefreshInterval");
                        if (intervalVal != null) refreshInterval = (int)intervalVal;

                        var adapterVal = key.GetValue("SelectedAdapterId");
                        if (adapterVal != null) selectedAdapterId = (string)adapterVal;
                    }
                }
            }
            catch {}
        }

        private void SavePreferences()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegKey))
                {
                    key.SetValue("CycleMode", cycleMode ? 1 : 0);
                    key.SetValue("UseBits", useBits ? 1 : 0);
                    key.SetValue("RefreshInterval", refreshInterval);
                    key.SetValue("SelectedAdapterId", selectedAdapterId);
                }
            }
            catch {}
        }

        public void ApplySettings()
        {
            SavePreferences();
            timer.Interval = refreshInterval;
            activeInterface = GetActiveNetworkInterface();
            if (activeInterface != null)
            {
                var stats = activeInterface.GetIPv4Statistics();
                previousReceived = stats.BytesReceived;
                previousSent = stats.BytesSent;
            }
            UpdatePosition();
        }

        private void SetupUI()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.BackColor = Color.Black; 
            this.TransparencyKey = Color.Black; 
            
            speedLabel = new Label
            {
                ForeColor = Color.White,
                Font = new Font("Segoe UI Variable Text", 9, FontStyle.Regular), 
                UseCompatibleTextRendering = true, 
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(0, 0),
                BackColor = Color.Transparent
            };
            
            this.Controls.Add(speedLabel);

            // Native Windows 11 Hover Effect & Custom Flyout
            flyout = new FlyoutForm();
            consumerTracker = new NetworkConsumerTracker();

            hoverTimer = new System.Windows.Forms.Timer();
            hoverTimer.Interval = 50;
            hoverTimer.Tick += HoverTimer_Tick;
            hoverTimer.Start();

            // Double Click Action
            speedLabel.DoubleClick += (s, e) => {
                System.Diagnostics.Process.Start("taskmgr.exe");
            };

            // Context Menu
            contextMenu = new ContextMenuStrip();
            if (!IsLightTheme())
            {
                contextMenu.BackColor = Color.FromArgb(43, 43, 43);
                contextMenu.ForeColor = Color.White;
                contextMenu.ShowImageMargin = false;
            }

            var autoStartItem = new ToolStripMenuItem("Auto-Start on Boot", null, (s, e) => { 
                ToggleAutoStart(); 
                ((ToolStripMenuItem)s).Checked = IsAutoStartEnabled(); 
            });
            autoStartItem.Checked = IsAutoStartEnabled();
            contextMenu.Items.Add(autoStartItem);

            var cycleModeItem = new ToolStripMenuItem("Cycle CPU/RAM", null, (s, e) => { 
                cycleMode = !cycleMode; 
                SavePreferences();
                ((ToolStripMenuItem)s).Checked = cycleMode; 
            });
            cycleModeItem.Checked = cycleMode;
            contextMenu.Items.Add(cycleModeItem);

            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Settings", null, (s, e) => {
                new SettingsForm(this).ShowDialog();
            });
            var exitItem = new ToolStripMenuItem("Exit BitBar", null, (s, e) => Application.Exit());
            contextMenu.Items.Add(exitItem);

            trayIcon = new NotifyIcon();
            trayIcon.Icon = SystemIcons.Information; // Or custom icon
            try { trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            trayIcon.Text = "BitBar";
            trayIcon.ContextMenuStrip = contextMenu;
            trayIcon.Visible = true;

            // Manually handle Right-Click to force it to show over the Taskbar
            speedLabel.MouseClick += (s, e) => {
                if (e.Button == MouseButtons.Right)
                {
                    SetForegroundWindow(this.Handle);
                    contextMenu.Show(Cursor.Position);
                }
            };
        }

        protected override bool ShowWithoutActivation => true;

        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEACTIVATE = 0x0021;
            const int MA_NOACTIVATE = 3; // Do not activate, but process the click (prevents beep)
            
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }
            
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (isHovered)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color bgHover = IsLightTheme() ? Color.FromArgb(255, 235, 235, 235) : Color.FromArgb(255, 55, 55, 55);
                using (SolidBrush brush = new SolidBrush(bgHover))
                {
                    // Match native taskbar icon margins (4px radius, small padding)
                    Rectangle rect = new Rectangle(2, 4, this.Width - 4, this.Height - 8);
                    e.Graphics.FillRoundedRectangle(brush, rect, 4);
                }
            }
        }

        private void HoverTimer_Tick(object sender, EventArgs e)
        {
            bool currentlyHovered = false;
            if (this.Visible)
            {
                Rectangle screenBounds = this.RectangleToScreen(this.ClientRectangle);
                currentlyHovered = screenBounds.Contains(Cursor.Position);
            }

            if (currentlyHovered != isHovered)
            {
                isHovered = currentlyHovered;
                this.Invalidate(); // Repaint background

                if (isHovered)
                {
                    ShowFlyout();
                }
                else
                {
                    HideFlyout();
                }
            }
        }

        private void ShowFlyout()
        {
            if (flyout.Visible && flyout.Opacity >= 0.99) return;
            UpdateFlyoutData();
            
            // Calculate location (above the taskbar widget)
            int flyoutX = this.Location.X - (flyout.Width - this.Width) / 2;
            int flyoutY = this.Location.Y - flyout.Height - 10; // 10px padding
            
            SetForegroundWindow(flyout.Handle);
            flyout.ShowAnimated(flyoutX, flyoutY);
        }

        private void HideFlyout()
        {
            flyout.Hide();
        }

        private void UpdateFlyoutData()
        {
            string topConsumer = consumerTracker.GetTopConsumer();
            flyout.UpdateData(downHistory, upHistory, downHistory.LastOrDefault(), upHistory.LastOrDefault(), topConsumer, IsLightTheme());
        }

        private void EmbedInTaskbar()
        {
            IntPtr taskbarHandle = FindWindow("Shell_TrayWnd", null);
            if (taskbarHandle != IntPtr.Zero)
            {
                SetParent(this.Handle, taskbarHandle);
                UpdatePosition();
            }
        }

        private void UpdatePosition()
        {
            IntPtr taskbarHandle = FindWindow("Shell_TrayWnd", null);
            IntPtr trayNotifyHandle = FindWindowEx(taskbarHandle, IntPtr.Zero, "TrayNotifyWnd", null);

            if (taskbarHandle != IntPtr.Zero && trayNotifyHandle != IntPtr.Zero)
            {
                GetWindowRect(taskbarHandle, out RECT taskbarRect);
                GetWindowRect(trayNotifyHandle, out RECT notifyRect);

                int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
                
                // Measure a standard "wide" text template so the width is fixed.
                // This prevents the widget from jittering every second and fixes the hover glitch.
                string templateText = useBits ? "\u2191 999.9 Mbps\n\u2193 999.9 Mbps" : "\u2191 999.9 MB/s\n\u2193 999.9 MB/s";
                Size textSize = TextRenderer.MeasureText(templateText, speedLabel.Font);
                
                int targetWidth = textSize.Width + 12; // 6px padding left/right
                int dynamicX = (notifyRect.Left - taskbarRect.Left) - targetWidth; 
                
                if (this.Size.Width != targetWidth || this.Size.Height != taskbarHeight)
                {
                    this.Size = new Size(targetWidth, taskbarHeight);
                    this.Invalidate();
                }
                
                if (this.Location.X != dynamicX || this.Location.Y != 0)
                {
                    this.Location = new Point(dynamicX, 0); 
                }
                
                speedLabel.Location = new Point(0, 0);
                speedLabel.Size = new Size(targetWidth - 10, taskbarHeight);
            }
        }

        private void SetupNetworkMonitoring()
        {
            activeInterface = GetActiveNetworkInterface();

            if (activeInterface != null)
            {
                var stats = activeInterface.GetIPv4Statistics();
                previousReceived = stats.BytesReceived;
                previousSent = stats.BytesSent;
            }

            timer = new System.Windows.Forms.Timer();
            timer.Interval = refreshInterval;
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            cycleCounter++;

            if (activeInterface == null || activeInterface.OperationalStatus != OperationalStatus.Up)
            {
                activeInterface = GetActiveNetworkInterface();
            }

            if (activeInterface != null)
            {
                var stats = activeInterface.GetIPv4Statistics();
                long received = stats.BytesReceived;
                long sent = stats.BytesSent;

                long downSpeed = received - previousReceived;
                long upSpeed = sent - previousSent;

                previousReceived = received;
                previousSent = sent;

                downHistory.Enqueue(downSpeed);
                upHistory.Enqueue(upSpeed);
                
                if (downHistory.Count > MaxHistory) downHistory.Dequeue();
                if (upHistory.Count > MaxHistory) upHistory.Dequeue();

                if (flyout.Visible)
                {
                    UpdateFlyoutData();
                }

                if (cycleMode && (cycleCounter % 10) >= 5) 
                {
                    // Show CPU/RAM for 5 seconds
                    double cpu = GetCpuUsage();
                    double ram = GetRamUsage();
                    speedLabel.Text = $"CPU {cpu:F0}%\nRAM {ram:F0}%";
                    speedLabel.ForeColor = IsLightTheme() ? Color.Black : Color.White;
                }
                else
                {
                    // Show Network for 5 seconds
                    string downStr = FormatSpeed(downSpeed);
                    string upStr = FormatSpeed(upSpeed);
                    speedLabel.Text = $"U: {upStr}\nD: {downStr}";

                    // Ghost Mode: Auto-Fade on zero traffic
                    if (downSpeed == 0 && upSpeed == 0)
                    {
                        speedLabel.ForeColor = Color.DimGray;
                    }
                    else
                    {
                        speedLabel.ForeColor = IsLightTheme() ? Color.Black : Color.White;
                    }
                }
            }
            else
            {
                speedLabel.Text = "Offline";
            }
            UpdatePosition();
        }

        private double GetCpuUsage()
        {
            if (GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime))
            {
                ulong curIdle = ((ulong)idleTime.dwHighDateTime << 32) | idleTime.dwLowDateTime;
                ulong curKernel = ((ulong)kernelTime.dwHighDateTime << 32) | kernelTime.dwLowDateTime;
                ulong curUser = ((ulong)userTime.dwHighDateTime << 32) | userTime.dwLowDateTime;

                ulong sys = (curKernel - prevKernelTime) + (curUser - prevUserTime);
                ulong idle = curIdle - prevIdleTime;

                prevIdleTime = curIdle;
                prevKernelTime = curKernel;
                prevUserTime = curUser;

                if (sys > 0)
                {
                    return (sys - idle) * 100.0 / sys;
                }
            }
            return 0;
        }

        private double GetRamUsage()
        {
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                return memStatus.dwMemoryLoad;
            }
            return 0;
        }

        public bool IsLightTheme()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("SystemUsesLightTheme");
                        if (val != null && (int)val == 1)
                            return true;
                    }
                }
            }
            catch {}
            return false;
        }

        private void ToggleAutoStart()
        {
            string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, true))
                {
                    if (key.GetValue("NativeNetMonitor") == null)
                        key.SetValue("NativeNetMonitor", Application.ExecutablePath);
                    else
                        key.DeleteValue("NativeNetMonitor");
                }
            }
            catch {}
        }

        private bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    return key?.GetValue("NativeNetMonitor") != null;
                }
            }
            catch {}
            return false;
        }

        private string FormatSpeed(long bytes)
        {
            if (useBits)
            {
                long bits = bytes * 8;
                if (bits < 1000) return $"{bits} bps";
                if (bits < 1000 * 1000) return $"{bits / 1000.0:F1} Kbps";
                return $"{bits / (1000.0 * 1000.0):F1} Mbps";
            }
            else
            {
                if (bytes < 1024) return $"{bytes} B/s";
                if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB/s";
                return $"{bytes / (1024.0 * 1024.0):F1} MB/s";
            }
        }

        private string FormatData(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        private NetworkInterface GetActiveNetworkInterface()
        {
            if (!string.IsNullOrEmpty(selectedAdapterId) && selectedAdapterId != "Auto")
            {
                var specific = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(ni => ni.Id == selectedAdapterId);
                if (specific != null && specific.OperationalStatus == OperationalStatus.Up)
                    return specific;
            }

            return NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(ni => 
                    ni.OperationalStatus == OperationalStatus.Up && 
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    ni.GetIPv4Statistics().BytesReceived > 0);
        }
    }
}
