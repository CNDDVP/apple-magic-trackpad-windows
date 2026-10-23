using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace MagicForceClick
{
    internal static class Program
    {
        // v2.2 PTP_REPORT_MT2 精确字节偏移 (112 bytes total)
        // ReportID = [0] (0x05)
        // Contact[0]: Flags=[1], ContactID=[2..5], X=[6..7], Y=[8..9], Width=[10..11], Height=[12..13], Pressure=[14..15]
        // ScanTime=[106..107], ContactCount=[108], MechanicalForce=[109..110], IsButtonClicked=[111]
        private const int REPORT_LEN_MT2 = 112;
        private const int STAGE2_PRESSURE_THRESHOLD = 180; // 苹果压力范围 0..255，一段点击约 80~110，二段深压设为 180
        private const int STAGE2_RESET_THRESHOLD = 110;
        private const int MAX_DRAG_DELTA = 220; // 若手指已明显滑动拖拽窗口，则不误触二段预览

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private static void EnablePerMonitorDpiAwareness()
        {
            try
            {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
                if (SetProcessDpiAwarenessContext(new IntPtr(-4)))
                    return;
            }
            catch { }
            try
            {
                // PROCESS_PER_MONITOR_DPI_AWARE = 2
                if (SetProcessDpiAwareness(2) == 0)
                    return;
            }
            catch { }
            try
            {
                SetProcessDPIAware();
            }
            catch { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            EnablePerMonitorDpiAwareness();

            bool createdNew;
            using (var mutex = new Mutex(true, "Global\\MagicTrackpad2_ForceClick_Daemon", out createdNew))
            {
                if (!createdNew)
                {
                    return; // 已在后台运行
                }
                int threshold = STAGE2_PRESSURE_THRESHOLD;
                int custom;
                if (args.Length > 0 && int.TryParse(args[0], out custom) && custom >= 120 && custom <= 250)
                {
                    threshold = custom;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new RawInputSinkForm(threshold));
            }
        }
    }

    internal sealed class RawInputSinkForm : Form
    {
        private const int WM_INPUT = 0x00FF;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIM_TYPEHID = 2;
        private const uint RIDEV_INPUTSINK = 0x00000100;

        private readonly int _threshold;
        private bool _stage1Active;
        private bool _stage2Fired;
        private int _anchorX;
        private int _anchorY;
        private long _lastFireTick;
        private long _lastDiagnosticTick;
        private bool _loggedReport;
        private bool _diagnosticButtonDown;
        private int _diagnosticPeak;

        // === 多屏 PPI 自动等速补偿 + 类 macOS S型动态加速曲线 状态 ===
        private struct MonitorScaleInfo
        {
            public string DeviceName;
            public int Left, Top, Right, Bottom;
            public int PhysWidthPx, PhysHeightPx;
            public double PhysWidthCm;
            public double PpiPxPerCm;
            public double ScaleFactor;
        }

        private MonitorScaleInfo[] _monitors = new MonitorScaleInfo[0];

        // S型加速曲线参数（针对妙控板 ~46 units/mm 硬件分辨率调优 - 提速版）
        // v 单位：HID units / ms（1 unit/ms ≈ 2.17 cm/s）
        private double _curveMinGain = 0.95;   // 极慢速微操保持接近 1:1 精准度，不发飘也不拖泥带水
        private double _curveMaxGain = 3.40;   // 快速甩动跨屏最大增益（一指轻甩即可横跨 8320px 三联屏）
        private double _curveMidSpeed = 4.5;   // S曲线拐点速度前移（units/ms），中常速移动即明显提速
        private double _curvePower = 1.85;     // S曲线指数，提速介入更早更顺滑

        private bool _wasSingleFinger;
        private uint _prevContactId;
        private int _prevPadX;
        private int _prevPadY;
        private long _prevTimestamp;
        private double _smoothedSpeed;
        private POINT _lastSetPos;
        private double _remX;
        private double _remY;

        private static void Log(string message)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MagicForceClick.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1048576)
                    File.WriteAllText(path, "");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch { }
        }

        public RawInputSinkForm(int threshold)
        {
            _threshold = threshold;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Minimized;
            Opacity = 0;
            Width = 1;
            Height = 1;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var dev = new RAWINPUTDEVICE[1];
            // HID Usage Page 0x0D (Digitizer), Usage 0x05 (Touch Pad)
            dev[0].usUsagePage = 0x000D;
            dev[0].usUsage = 0x0005;
            dev[0].dwFlags = RIDEV_INPUTSINK;
            dev[0].hwndTarget = Handle;
            bool registered = RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            Log("RawInput registration=" + registered + "; error=" + (registered ? 0 : Marshal.GetLastWin32Error()) + "; threshold=" + _threshold);
            RefreshMonitors();
        }

        private void RefreshMonitors()
        {
            try
            {
                Screen[] screens = Screen.AllScreens;
                List<MonitorScaleInfo> list = new List<MonitorScaleInfo>();
                double primaryPpi = 0;

                foreach (Screen scr in screens)
                {
                    DEVMODE dm = new DEVMODE();
                    dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                    int left = scr.Bounds.Left;
                    int top = scr.Bounds.Top;
                    int physW = scr.Bounds.Width;
                    int physH = scr.Bounds.Height;
                    if (EnumDisplaySettings(scr.DeviceName, -1, ref dm) && dm.dmPelsWidth > 0)
                    {
                        left = dm.dmPositionX;
                        top = dm.dmPositionY;
                        physW = dm.dmPelsWidth;
                        physH = dm.dmPelsHeight;
                    }

                    // 直接通过 GDI GetDeviceCaps(HORZSIZE=4) 从显示器 EDID 读取真实物理宽度（mm -> cm），支持任意新电脑多屏自适应
                    double widthCm = (physW >= 3400) ? 71.0 : 53.0;
                    IntPtr hdc = CreateDC(null, scr.DeviceName, null, IntPtr.Zero);
                    if (hdc != IntPtr.Zero)
                    {
                        try
                        {
                            int horzMm = GetDeviceCaps(hdc, 4); // HORZSIZE (mm)
                            if (horzMm >= 150 && horzMm <= 2000)
                                widthCm = horzMm / 10.0;
                        }
                        finally
                        {
                            DeleteDC(hdc);
                        }
                    }
                    double ppi = physW / widthCm;

                    MonitorScaleInfo info = new MonitorScaleInfo();
                    info.DeviceName = scr.DeviceName;
                    info.Left = left;
                    info.Top = top;
                    info.Right = left + physW;
                    info.Bottom = top + physH;
                    info.PhysWidthPx = physW;
                    info.PhysHeightPx = physH;
                    info.PhysWidthCm = widthCm;
                    info.PpiPxPerCm = ppi;
                    info.ScaleFactor = 1.0;

                    if (scr.Primary || primaryPpi <= 0)
                        primaryPpi = ppi;

                    list.Add(info);
                }

                if (primaryPpi <= 0)
                    primaryPpi = 54.08;

                for (int i = 0; i < list.Count; i++)
                {
                    MonitorScaleInfo m = list[i];
                    m.ScaleFactor = m.PpiPxPerCm / primaryPpi;
                    if (m.ScaleFactor < 0.40) m.ScaleFactor = 0.40;
                    if (m.ScaleFactor > 1.60) m.ScaleFactor = 1.60;
                    list[i] = m;
                    Log(string.Format("Monitor {0}: PhysBounds=({1},{2})-({3},{4}) Phys={5}x{6} WidthCm={7:F1} PPI={8:F2}px/cm ScaleFactor={9:F3}x",
                        m.DeviceName, m.Left, m.Top, m.Right, m.Bottom, m.PhysWidthPx, m.PhysHeightPx, m.PhysWidthCm, m.PpiPxPerCm, m.ScaleFactor));
                }

                _monitors = list.ToArray();
            }
            catch (Exception ex)
            {
                Log("RefreshMonitors error: " + ex.Message);
            }
        }

        private double GetMonitorScaleAt(int x, int y)
        {
            MonitorScaleInfo[] mons = _monitors;
            for (int i = 0; i < mons.Length; i++)
            {
                if (x >= mons[i].Left && x < mons[i].Right && y >= mons[i].Top && y < mons[i].Bottom)
                    return mons[i].ScaleFactor;
            }
            double bestDist = double.MaxValue;
            double bestScale = 1.0;
            for (int i = 0; i < mons.Length; i++)
            {
                int cx = (mons[i].Left + mons[i].Right) / 2;
                int cy = (mons[i].Top + mons[i].Bottom) / 2;
                double d = (double)(x - cx) * (x - cx) + (double)(y - cy) * (y - cy);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestScale = mons[i].ScaleFactor;
                }
            }
            return bestScale;
        }

        private double ComputeMacSCurveGain(double speedUnitsPerMs)
        {
            if (speedUnitsPerMs <= 0.001)
                return _curveMinGain;
            double ratio = speedUnitsPerMs / _curveMidSpeed;
            double pow = Math.Pow(ratio, _curvePower);
            double s = pow / (1.0 + pow);
            return _curveMinGain + (_curveMaxGain - _curveMinGain) * s;
        }

        protected override void SetVisibleCore(bool value)
        {
            // A hidden Form still needs a real HWND to receive WM_INPUT.
            if (!IsHandleCreated)
                CreateHandle();
            base.SetVisibleCore(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT)
            {
                ProcessRawInput(m.LParam);
            }
            else if (m.Msg == WM_DISPLAYCHANGE)
            {
                RefreshMonitors();
            }
            base.WndProc(ref m);
        }

        private void ProcessRawInput(IntPtr hRawInput)
        {
            uint dwSize = 0;
            uint headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            if (GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref dwSize, headerSize) != 0 || dwSize == 0)
                return;

            IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
            try
            {
                if (GetRawInputData(hRawInput, RID_INPUT, buffer, ref dwSize, headerSize) != dwSize)
                    return;

                var header = (RAWINPUTHEADER)Marshal.PtrToStructure(buffer, typeof(RAWINPUTHEADER));
                if (header.dwType != RIM_TYPEHID)
                    return;

                IntPtr hidPtr = IntPtr.Add(buffer, (int)headerSize);
                uint dwSizeHid = (uint)Marshal.ReadInt32(hidPtr, 0);
                uint dwCount = (uint)Marshal.ReadInt32(hidPtr, 4);
                if (!_loggedReport)
                {
                    _loggedReport = true;
                    Log("First HID report length=" + dwSizeHid + "; count=" + dwCount);
                }
                if (dwSizeHid < 112 || dwCount == 0)
                    return;

                IntPtr rawDataPtr = IntPtr.Add(hidPtr, 8);
                byte[] report = new byte[dwSizeHid];
                for (uint i = 0; i < dwCount; i++)
                {
                    Marshal.Copy(IntPtr.Add(rawDataPtr, (int)(i * dwSizeHid)), report, 0, (int)dwSizeHid);
                    HandleMt2Report(report);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private void HandleMt2Report(byte[] r)
        {
            // Both v2.2 miniports use ReportID 0x05 for the 112-byte MT2 report.
            if (r.Length < 112 || r[0] != 0x05)
                return;

            byte c0Flags = r[1];
            bool tipSwitch = (c0Flags & 0x02) != 0;
            uint c0Id = BitConverter.ToUInt32(r, 2);
            byte contactCount = r[108];
            ushort c0X = (ushort)(r[6] | (r[7] << 8));
            ushort c0Y = (ushort)(r[8] | (r[9] << 8));
            ushort c0Pressure = (ushort)(r[14] | (r[15] << 8));
            ushort mechForce = (ushort)(r[109] | (r[110] << 8));
            byte isButtonClicked = r[111];

            // === 1. 单指移动：三屏 PPI 等速补偿 + 类 macOS S型动态加速曲线 ===
            if (contactCount == 1 && tipSwitch)
            {
                long nowTicks = Stopwatch.GetTimestamp();
                POINT curPos;
                GetCursorPos(out curPos);

                if (_wasSingleFinger && c0Id == _prevContactId)
                {
                    double dtMs = (nowTicks - _prevTimestamp) * 1000.0 / Stopwatch.Frequency;
                    if (dtMs >= 0.5 && dtMs <= 65.0)
                    {
                        int dPadX = (int)c0X - _prevPadX;
                        int dPadY = (int)c0Y - _prevPadY;
                        double padDist = Math.Sqrt((double)dPadX * dPadX + (double)dPadY * dPadY);
                        double instSpeed = padDist / dtMs; // HID units / ms
                        _smoothedSpeed = 0.55 * instSpeed + 0.45 * _smoothedSpeed;

                        int winDx = curPos.X - _lastSetPos.X;
                        int winDy = curPos.Y - _lastSetPos.Y;

                        if ((winDx != 0 || winDy != 0) && Math.Abs(winDx) < 800 && Math.Abs(winDy) < 800)
                        {
                            double monScale = GetMonitorScaleAt(curPos.X, curPos.Y);
                            double curveGain = ComputeMacSCurveGain(_smoothedSpeed);
                            double totalScale = monScale * curveGain;

                            double targetDx = winDx * totalScale + _remX;
                            double targetDy = winDy * totalScale + _remY;
                            int applyDx = (int)Math.Round(targetDx);
                            int applyDy = (int)Math.Round(targetDy);
                            _remX = targetDx - applyDx;
                            _remY = targetDy - applyDy;

                            int newX = _lastSetPos.X + applyDx;
                            int newY = _lastSetPos.Y + applyDy;

                            if (newX != curPos.X || newY != curPos.Y)
                            {
                                SetCursorPos(newX, newY);
                                GetCursorPos(out curPos);
                            }
                        }
                    }
                    else
                    {
                        _smoothedSpeed = 0;
                        _remX = 0;
                        _remY = 0;
                    }
                }
                else
                {
                    _smoothedSpeed = 0;
                    _remX = 0;
                    _remY = 0;
                }

                _wasSingleFinger = true;
                _prevContactId = c0Id;
                _prevPadX = c0X;
                _prevPadY = c0Y;
                _prevTimestamp = nowTicks;
                _lastSetPos = curPos;
            }
            else
            {
                _wasSingleFinger = false;
                _smoothedSpeed = 0;
                _remX = 0;
                _remY = 0;
            }

            // === 2. 二段式重压预览（Force Click） ===
            int pressure = Math.Max((int)c0Pressure, (int)mechForce);
            bool diagnosticDown = (isButtonClicked & 1) != 0;
            if (diagnosticDown && !_diagnosticButtonDown)
            {
                _diagnosticPeak = pressure;
                Log("ButtonDown pressure=" + pressure + "; contacts=" + contactCount);
            }
            if (diagnosticDown)
                _diagnosticPeak = Math.Max(_diagnosticPeak, pressure);
            if (!diagnosticDown && _diagnosticButtonDown)
                Log("ButtonUp peak=" + _diagnosticPeak);
            _diagnosticButtonDown = diagnosticDown;
            long diagnosticNow = (long)(uint)Environment.TickCount;
            if (contactCount > 0 && diagnosticNow - _lastDiagnosticTick >= 1000)
            {
                _lastDiagnosticTick = diagnosticNow;
                Log("ReportID=" + r[0] + "; Contact count=" + contactCount + "; pressure=" + pressure + "; clicked=" + isButtonClicked);
            }

            if (isButtonClicked == 0 || contactCount == 0 || pressure < 110)
            {
                _stage1Active = false;
                if (pressure < 110 && isButtonClicked == 0)
                {
                    _stage2Fired = false;
                }
                return;
            }

            // 仅对单指重按触发二段深压预览（避免双指/三指手势误触）
            if (contactCount != 1)
                return;

            if (!_stage1Active)
            {
                _stage1Active = true;
                _anchorX = c0X;
                _anchorY = c0Y;
            }

            if (_stage2Fired)
                return;

            // 如果手指已经大幅滑动（正在拖拽窗口/框选），则锁住不触发二段深压
            if (Math.Abs(c0X - _anchorX) > 220 || Math.Abs(c0Y - _anchorY) > 220)
            {
                _stage2Fired = true;
                return;
            }

            if (pressure >= _threshold)
            {
                long now = (long)(uint)Environment.TickCount;
                if (now - _lastFireTick < 450)
                    return;

                _stage2Fired = true;
                _lastFireTick = now;
                ThreadPool.QueueUserWorkItem(_ => TriggerStage2ForceClick());
            }
        }

        private static void TriggerStage2ForceClick()
        {
            string cls = GetForegroundWindowClass();
            Log("Stage2 threshold reached; foreground=" + cls);
            if (!IsFileManagerOrPreviewWindow(cls))
            {
                Log("Preview skipped outside Explorer/desktop/QuickLook");
                return;
            }
            // The installed signed driver has no second-stage actuation command.
            FireSecondStageHaptic();

            // 2. 先释放左键按住状态，防止资源管理器处于“准备拖拽”态而吞掉预览热键
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(25);

            // 3. 判断当前窗口类型，触发对应二段深压动作
            if (IsFileManagerOrPreviewWindow(cls))
            {
                // 在桌面 / 资源管理器 / QuickLook 预览窗：直接触发 QuickLook 空格秒预览（或 PowerToys Peek Ctrl+Space）
                if (!TryInvokeQuickLookPipe())
                {
                    SendKey(VK_SPACE);
                }
            }
        }

        private static bool IsFileManagerOrPreviewWindow(string cls)
        {
            if (string.IsNullOrEmpty(cls)) return false;
            return cls == "CabinetWClass"
                || cls == "ExploreWClass"
                || cls == "Progman"
                || cls == "WorkerW"
                || cls.Contains("QuickLook")
                || cls.StartsWith("HwndWrapper[QuickLook");
        }

        private static bool TryInvokeQuickLookPipe()
        {
            try
            {
                string pipeName = "QuickLook.App.Pipe." + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
                {
                    pipe.Connect(250);
                    using (var writer = new StreamWriter(pipe))
                    {
                        writer.WriteLine("QuickLook.App.PipeMessages.Toggle||");
                        writer.Flush();
                    }
                    Log("QuickLook toggle sent");
                    return true;
                }
            }
            catch
            {
                Log("QuickLook pipe unavailable; falling back to Space");
                return false;
            }
        }

        private static void FireSecondStageHaptic()
        {
            Log("Second-stage haptic unsupported by the installed driver; no haptic command sent");
        }

        private static string GetForegroundWindowClass()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static void SendKey(byte vk)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            Thread.Sleep(15);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static void SendKeyCombo(byte modVk, byte vk)
        {
            keybd_event(modVk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            Thread.Sleep(15);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(modVk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_SPACE = 0x20;
        private const byte VK_CONTROL = 0x11;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public char[] dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public char[] dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string lpszOutput, IntPtr lpInitData);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);
    }
}
