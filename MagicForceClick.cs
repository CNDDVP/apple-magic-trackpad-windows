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

[assembly: System.Reflection.AssemblyVersion("1.1.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.1.0")]

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

            if (args.Length > 0 && args[0] == "--settings")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SettingsForm(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MagicForceClick.json")));
                return;
            }
            bool createdNew;
            using (var mutex = new Mutex(true, "Global\\MagicTrackpad2_ForceClick_Daemon", out createdNew))
            {
                if (!createdNew)
                {
                    return; // 已在后台运行
                }
                int? threshold = null;
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

        private volatile ForceClickOptions _options;
        private readonly int? _legacyThreshold;
        private readonly string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MagicForceClick.json");
        private readonly ScanTimeMotion _motion = new ScanTimeMotion();
        private readonly PressGesture _press = new PressGesture();
        private System.Windows.Forms.Timer _configTimer;
        private DateTime _configWriteTime;
        private IntPtr _inputDevice;
        private int _previewEpoch;
        private long _lastDiagnosticTick;
        private double _lastStatusTick;
        private bool _loggedReport, _diagnosticButtonDown;
        private int _diagnosticPeak;
        private bool _rawInputRegistered;
        private long _pointerSubmissionFailures;
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

        private POINT _lastSetPos;
        private double _remX, _remY;
        private static double NowMs { get { return Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency; } }

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

        public RawInputSinkForm(int? threshold)
        {
            _legacyThreshold = threshold;
            LoadConfiguration(true);
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
            dev[0].dwFlags = RIDEV_INPUTSINK | 0x2000; // RIDEV_DEVNOTIFY
            dev[0].hwndTarget = Handle;
            bool registered = RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            _rawInputRegistered = registered;
            Log("MagicForceClick 1.1.1; RawInput registration=" + registered + "; error=" + (registered ? 0 : Marshal.GetLastWin32Error()) + "; threshold=" + _options.ForceClickThreshold);
            RefreshMonitors();
            WriteStatus();
            _configTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _configTimer.Tick += delegate {
                LoadConfiguration(false);
                if (NowMs - _lastStatusTick >= 5000) WriteStatus();
            };
            _configTimer.Start();
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

        private void LoadConfiguration(bool first)
        {
            DateTime stamp = File.Exists(_configPath) ? File.GetLastWriteTimeUtc(_configPath) : DateTime.MinValue;
            if (!first && stamp == _configWriteTime) return;
            _configWriteTime = stamp;
            try
            {
                ForceClickOptions candidate = ForceClickOptions.Load(_configPath);
                if (_legacyThreshold.HasValue) candidate.ForceClickThreshold = _legacyThreshold.Value;
                candidate.Validate();
                _options = candidate;
                _motion.Reset(); _press.CancelHeld(); Interlocked.Increment(ref _previewEpoch);
                _remX = _remY = 0;
                Log("Configuration loaded; preview=" + candidate.EnableForceClick + "; acceleration=" + candidate.EnableAcceleration + "; monitor=" + candidate.EnableMonitorCompensation);
            }
            catch (Exception ex)
            {
                if (_options == null) _options = new ForceClickOptions();
                Log("Configuration rejected; keeping last valid options: " + ex.Message);
            }
        }

        [System.Runtime.Serialization.DataContract]
        public sealed class RuntimeStatus
        {
            [System.Runtime.Serialization.DataMember] public string Version = "1.1.1";
            [System.Runtime.Serialization.DataMember] public DateTime TimeUtc = DateTime.UtcNow;
            [System.Runtime.Serialization.DataMember] public long ValidScanFrames, DuplicateScanFrames, Discontinuities;
            [System.Runtime.Serialization.DataMember] public bool PressBlocked;
            [System.Runtime.Serialization.DataMember] public bool RawInputRegistered;
            [System.Runtime.Serialization.DataMember] public string PointerOutput = "SendInputAbsoluteVirtualDesktop";
            [System.Runtime.Serialization.DataMember] public long PointerSubmissionFailures;
            [System.Runtime.Serialization.DataMember] public ForceClickOptions Options;
        }
        private void WriteStatus()
        {
            _lastStatusTick = NowMs;
            try
            {
                var status = new RuntimeStatus { Options = _options, ValidScanFrames = _motion.ValidFrames, DuplicateScanFrames = _motion.DuplicateFrames, Discontinuities = _motion.Discontinuities, PressBlocked = _press.Blocked, RawInputRegistered = _rawInputRegistered, PointerSubmissionFailures = _pointerSubmissionFailures };
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MagicForceClick.status.json");
                string temp = path + ".tmp";
                using (var stream = File.Create(temp)) new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(RuntimeStatus)).WriteObject(stream, status);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            catch (Exception ex) { Log("Status write failed: " + ex.Message); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && _configTimer != null) _configTimer.Dispose();
            Interlocked.Increment(ref _previewEpoch);
            base.Dispose(disposing);
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
            else if (m.Msg == 0x00FE) // WM_INPUT_DEVICE_CHANGE
            {
                _motion.Reset(); _press.CancelHeld(); Interlocked.Increment(ref _previewEpoch);
                _inputDevice = IntPtr.Zero;
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
                if (dwSizeHid != 112 || dwCount == 0 || (ulong)headerSize + 8UL + (ulong)dwSizeHid * dwCount > dwSize)
                    return;

                IntPtr rawDataPtr = IntPtr.Add(hidPtr, 8);
                byte[] report = new byte[dwSizeHid];
                for (uint i = 0; i < dwCount; i++)
                {
                    Marshal.Copy(IntPtr.Add(rawDataPtr, (int)(i * dwSizeHid)), report, 0, (int)dwSizeHid);
                    HandleMt2Report(report, header.hDevice);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private void HandleMt2Report(byte[] report, IntPtr device)
        {
            TrackpadSample sample;
            if (!TrackpadSample.TryDecode(report, out sample)) return;
            if (_inputDevice != IntPtr.Zero && _inputDevice != device)
            {
                _motion.Reset(); _press.CancelHeld(); Interlocked.Increment(ref _previewEpoch);
            }
            _inputDevice = device;
            ForceClickOptions options = _options;
            POINT position;
            if (!GetCursorPos(out position)) return;
            double now = NowMs;
            MotionResult motion = _motion.Update(sample, now, options);
            if (motion.ContinuityLost) { _press.CancelHeld(); Interlocked.Increment(ref _previewEpoch); }
            bool wasBlocked = _press.Blocked;
            bool preview = _press.Update(sample, position.X, position.Y, now, options);
            if (!sample.Button || _press.Blocked) Interlocked.Increment(ref _previewEpoch);

            if (motion.Valid && motion.PadDistance > 0 && (options.EnableAcceleration || options.EnableMonitorCompensation))
            {
                int dx = position.X - _lastSetPos.X, dy = position.Y - _lastSetPos.Y;
                if ((dx != 0 || dy != 0) && Math.Abs(dx) < 800 && Math.Abs(dy) < 800)
                {
                    double scale = PointerGain.Combined(motion.Speed, GetMonitorScaleAt(position.X, position.Y), sample.Button, options);
                    double targetX = dx * scale + _remX, targetY = dy * scale + _remY;
                    int applyX = (int)Math.Round(targetX), applyY = (int)Math.Round(targetY);
                    _remX = targetX - applyX; _remY = targetY - applyY;
                    int x = _lastSetPos.X + applyX, y = _lastSetPos.Y + applyY;
                    if (x != position.X || y != position.Y) MovePointerThroughInput(x, y, ref position);
                }
            }
            else { _remX = _remY = 0; }
            _lastSetPos = position;

            if (sample.Button && !_diagnosticButtonDown) { _diagnosticPeak = sample.Pressure; Log("ButtonDown pressure=" + sample.Pressure); }
            if (sample.Button) _diagnosticPeak = Math.Max(_diagnosticPeak, sample.Pressure);
            if (!sample.Button && _diagnosticButtonDown) Log("ButtonUp peak=" + _diagnosticPeak + "; previewBlocked=" + wasBlocked);
            _diagnosticButtonDown = sample.Button;
            if (now - _lastDiagnosticTick >= 1000 && sample.ContactCount > 0)
            {
                _lastDiagnosticTick = (long)now;
                Log("ScanTime=" + sample.ScanTime + "; contacts=" + sample.ContactCount + "; pressure=" + sample.Pressure + "; validScanFrames=" + _motion.ValidFrames);
            }
            if (preview)
            {
                Log("Preview intent; pressure=" + sample.Pressure + "; threshold=" + options.ForceClickThreshold + "; rise=" + options.ForceClickPressureRise + "; dwellMs=" + options.ForceClickDwellMs);
                IntPtr hwnd = GetForegroundWindow();
                int token = Interlocked.Increment(ref _previewEpoch);
                ThreadPool.QueueUserWorkItem(delegate { TriggerStage2ForceClick(hwnd, token); });
            }
        }
        private bool PreviewCurrent(IntPtr hwnd, int token)
        {
            return token == Volatile.Read(ref _previewEpoch) && _options.EnableForceClick && hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd;
        }

        private void MovePointerThroughInput(int x, int y, ref POINT position)
        {
            // Keep the target on a real display, including gaps in staggered monitor layouts.
            double best = double.MaxValue;
            int targetX = x, targetY = y;
            foreach (var monitor in _monitors)
            {
                int cx = Math.Max(monitor.Left, Math.Min(monitor.Right - 1, x));
                int cy = Math.Max(monitor.Top, Math.Min(monitor.Bottom - 1, y));
                double distance = (double)(x - cx) * (x - cx) + (double)(y - cy) * (y - cy);
                if (distance < best) { best = distance; targetX = cx; targetY = cy; }
            }
            RECT clip;
            if (GetClipCursor(out clip) && clip.Right > clip.Left && clip.Bottom > clip.Top)
            {
                targetX = Math.Max(clip.Left, Math.Min(clip.Right - 1, targetX));
                targetY = Math.Max(clip.Top, Math.Min(clip.Bottom - 1, targetY));
            }
            int width = GetSystemMetrics(78), height = GetSystemMetrics(79);
            if (width <= 0 || height <= 0) return;
            var input = new INPUT[1];
            input[0].type = 0;
            input[0].mouse.dx = AbsolutePointer.Normalize(targetX, GetSystemMetrics(76), width);
            input[0].mouse.dy = AbsolutePointer.Normalize(targetY, GetSystemMetrics(77), height);
            input[0].mouse.dwFlags = 0x0001 | 0x8000 | 0x4000 | 0x2000;
            // Submit via the Windows input path, not SetCursorPos: subsequent PTP motion must
            // inherit the injected position. Track the submitted target, never a stale immediate read.
            if (SendInput(1, input, Marshal.SizeOf(typeof(INPUT))) == 1)
            { position.X = targetX; position.Y = targetY; }
            else
            {
                _pointerSubmissionFailures++;
                _motion.Reset(); _remX = _remY = 0;
                if (_pointerSubmissionFailures == 1) Log("Pointer input submission failed; error=" + Marshal.GetLastWin32Error());
            }
        }
        private void TriggerStage2ForceClick(IntPtr hwnd, int token)
        {
            if (!PreviewCurrent(hwnd, token)) return;
            PreviewContext context = PreviewContext.Capture(hwnd);
            if (context.Route == PreviewRoute.None)
            {
                Log("Stage2 ignored outside supported file view; process=" + context.ProcessName + "; windowClass=" + context.WindowClass + "; focusClass=" + context.FocusClass);
                return;
            }
            Log("Stage2 threshold reached; drag interlock passed; route=" + context.Route);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(25);
            if (!PreviewCurrent(hwnd, token) || !context.StillCurrent()) return;
            if (context.Route == PreviewRoute.FocusedSpace)
            {
                SendKey(VK_SPACE);
                Log("Directory Opus preview Space sent to focused file display");
                return;
            }
            if (!TryInvokeQuickLookPipe(context, token) && PreviewCurrent(hwnd, token) && context.StillCurrent()) SendKey(VK_SPACE);
        }

        private bool TryInvokeQuickLookPipe(PreviewContext context, int token)
        {
            try
            {
                string pipeName = "QuickLook.App.Pipe." + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
                {
                    pipe.Connect(250);
                    if (!PreviewCurrent(context.Window, token) || !context.StillCurrent()) return true;
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

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT
        { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr extraInfo; }
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public MOUSEINPUT mouse; }

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern bool GetClipCursor(out RECT clip);

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
