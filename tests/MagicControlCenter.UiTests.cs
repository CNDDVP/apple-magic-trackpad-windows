using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using MagicForceClick;

namespace MagicControlCenter.Tests
{
    internal static class WindowSmokeTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int _passed, _failed;

        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] private static extern int GetDpiForSystem();
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr window, int id);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr window);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int count);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("kernel32.dll")] private static extern bool AllocConsole();
        [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        private static void ShowConsoleOnce()
        {
            if (!AllocConsole()) return;
            IntPtr console = GetConsoleWindow();
            if (console != IntPtr.Zero) ShowWindow(console, 0);
        }

        private static string resultPath;
        private static void UncaughtException(object sender, ThreadExceptionEventArgs e)
        {
            Log("UNCAUGHT " + e.Exception);
            using (var writer = new StreamWriter(resultPath, false, new System.Text.UTF8Encoding(false))) { writer.WriteLine("control_center_ui_crashed=1"); writer.WriteLine("failed=1"); writer.WriteLine(e.Exception); }
            Environment.Exit(2);
        }
        [STAThread]
        public static int Main(string[] args)
        {
            bool dpiAware = false;
            try { dpiAware = SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += UncaughtException;
            ShowConsoleOnce();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string output = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "MagicControlCenter-ui-test");
            string directory = Path.Combine(output, "isolated-config");
            Directory.CreateDirectory(output);
            string result = Path.Combine(output, "UiResults.txt");
            resultPath = result;
            if (File.Exists(result)) File.Delete(result);
            File.WriteAllText(result, "control_center_ui_running=1" + Environment.NewLine + "failed=0" + Environment.NewLine);
            try
            {
                Directory.CreateDirectory(output);
                File.WriteAllLines(result, new[] { "control_center_ui_running=1", "failed=0" });
                foreach (string oldLog in Directory.GetFiles(output, "UiTestLog*.txt")) File.Delete(oldLog);
                Directory.CreateDirectory(directory);
                string config = Path.Combine(directory, "MagicForceClick.json");
                string child = Path.Combine(output, "parent-layout", "MagicControlCenter");
                Directory.CreateDirectory(child);
                string parentConfig = Path.Combine(output, "parent-layout", "MagicForceClick.json");
                new ForceClickOptions().Save(parentConfig);
                string located = ControlCenterProgram.ResolveConfigurationDirectory(child);
                if (Path.GetFullPath(located) != Path.GetFullPath(Path.GetDirectoryName(parentConfig)))
                    throw new Exception("Child-folder launch did not locate the parent service configuration.");
                Pass("child-folder launch locates the actual parent configuration");
                ForceClickOptions original = new ForceClickOptions();
                original.ApplyLightCrossScreenPreset();
                original.Save(config);
                using (var window = new ControlCenterForm(directory, true))
                {
                    window.StartPosition = FormStartPosition.Manual;
                    var workArea = Screen.PrimaryScreen.WorkingArea;
                    window.Left = workArea.Left + 20; window.Top = workArea.Top + 20;
                    window.Show();
                    ShowWindow(window.Handle, 5);
                    Application.DoEvents();
                    Log("DPI_CONTEXT_SET=" + dpiAware + " ERROR=" + Marshal.GetLastWin32Error());
                    Log("SYSTEM_DPI=" + GetDpiForSystem() + " WINDOW_DPI=" + GetDpiForWindow(window.Handle));
                    using (var graphics = window.CreateGraphics()) Log("GRAPHICS_DPI=" + graphics.DpiX.ToString("0.##"));
                    Log("FORM_DEVICE_DPI=" + window.DeviceDpi + " CLIENT=" + window.ClientSize.Width + "x" + window.ClientSize.Height);
                    if (GetDpiForWindow(window.Handle) != 144 || window.DeviceDpi != 144)
                        throw new Exception("Acceptance window is not on the 144 DPI primary display.");
                    if (Math.Abs(window.LayoutScale - 1.5f) > 0.01f)
                        throw new Exception("Layout scale does not match the actual 144 DPI window.");
                    using (var graphics = window.CreateGraphics())
                        if (Math.Abs(graphics.DpiX - window.DeviceDpi) > 0.1) throw new Exception("Graphics DPI differs from Form DPI.");
                    Pass("all DPI sources and layout agree at 144 DPI");
                    var runtimeLabel = (Label)typeof(ControlCenterForm).GetField("_runtimeLabel", PrivateInstance).GetValue(window);
                    if (runtimeLabel.IsDisposed) throw new ObjectDisposedException("Label", "Shared runtime label disposed at startup.");
                    window.Navigate("feel");
                    Application.DoEvents();
                    window.Navigate("overview");
                    Application.DoEvents();
                    if (runtimeLabel.IsDisposed) throw new ObjectDisposedException("Label", "Shared runtime label disposed after leaving overview.");
                    if (!runtimeLabel.IsHandleCreated) throw new Exception("Shared runtime label has no live window handle after returning to overview.");
                    Pass("leave overview and return without disposed Label");
                    if (window.CanSave) throw new Exception("Save must be disabled before the first edit.");
                    foreach (string page in new[] { "overview", "feel", "press", "gestures", "diagnostics" })
                    {
                        window.Navigate(page);
                        Invoke(window, "RefreshRuntime");
                        if (page == "diagnostics")
                        {
                            var diagnostics = (TextBox)typeof(ControlCenterForm).GetField("_diagnosticsText", PrivateInstance).GetValue(window);
                            if (string.IsNullOrWhiteSpace(diagnostics.Text) || !diagnostics.Text.Contains("Raw Input")) throw new Exception("Diagnostics did not populate the status text.");
                        }
                        window.Left = 60; window.Top = 60;
                        Application.DoEvents();
                        var canvas = (Panel)typeof(ControlCenterForm).GetField("_pageCanvas", PrivateInstance).GetValue(window);
                        if (Math.Abs(canvas.Width - 820 * window.LayoutScale) > 2) throw new Exception("Page scaled more than once: " + page + " width=" + canvas.Width);
                        for (int a = 0; a < canvas.Controls.Count; a++)
                            for (int b = a + 1; b < canvas.Controls.Count; b++)
                                if (canvas.Controls[a].Bounds.IntersectsWith(canvas.Controls[b].Bounds)) throw new Exception("Cards overlap on " + page);
                        AssertContentGeometry(window);
                        Capture(window, Path.Combine(output, page + ".png"));
                        if (window.CanSave) throw new Exception("Navigating to " + page + " incorrectly enabled Save.");
                        Pass("navigate " + page);
                    }
                    window.Navigate("feel");
                    var acceleration = (CheckBox)typeof(ControlCenterForm).GetField("_acceleration", PrivateInstance).GetValue(window);
                    acceleration.Checked = !original.EnableAcceleration;
                    if (!window.CanSave) throw new Exception("Feature switch did not immediately enable Save.");
                    Invoke(window, "SaveChanges");
                    if (ForceClickOptions.Load(config).EnableAcceleration == original.EnableAcceleration) throw new Exception("Feature switch was not saved.");
                    acceleration = (CheckBox)typeof(ControlCenterForm).GetField("_acceleration", PrivateInstance).GetValue(window);
                    acceleration.Checked = original.EnableAcceleration;
                    Invoke(window, "SaveChanges");
                    Pass("feature switches mark edits immediately and round-trip");
                    Invoke(window, "ApplyPreset", "classic");
                    var preset = (ForceClickOptions)typeof(ControlCenterForm).GetField("_draft", PrivateInstance).GetValue(window);
                    if (preset.ForceClickThreshold != 180 || Math.Abs(preset.MaxGain - 3.4) > 0.001) throw new Exception("Preset values were overwritten by old controls.");
                    Invoke(window, "LoadConfiguration", true);
                    if (window.CanSave) throw new Exception("Discarding a preset retained old draft values.");
                    Pass("presets and discard do not recapture stale controls");
                    foreach (var screen in Screen.AllScreens)
                    {
                        window.Location = new System.Drawing.Point(screen.WorkingArea.Left + 20, screen.WorkingArea.Top + 20);
                        for (int tick = 0; tick < 20; tick++) { Application.DoEvents(); Thread.Sleep(10); }
                        uint dpi = GetDpiForWindow(window.Handle);
                        if (window.DeviceDpi != dpi || Math.Abs(window.LayoutScale - dpi / 96f) > 0.01f) throw new Exception("DPI did not follow monitor " + screen.DeviceName);
                        window.Navigate("overview"); Application.DoEvents();
                        var canvas = (Panel)typeof(ControlCenterForm).GetField("_pageCanvas", PrivateInstance).GetValue(window);
                        if (Math.Abs(canvas.Width - 820 * window.LayoutScale) > 2) throw new Exception("Page size drifted after changing monitors.");
                        Log("MONITOR " + screen.DeviceName + " DPI=" + dpi + " FORM=" + window.DeviceDpi);
                        foreach (string page in new[] { "overview", "feel", "press", "gestures", "diagnostics" })
                        {
                            window.Navigate(page); Application.DoEvents();
                            Capture(window, Path.Combine(output, "monitor-" + dpi + "-" + page + ".png"));
                            AssertContentGeometry(window);
                        }
                        using (var startup = new ControlCenterForm(directory, true)) {
                            startup.StartPosition = FormStartPosition.Manual;
                            startup.Location = new System.Drawing.Point(screen.WorkingArea.Left + 20, screen.WorkingArea.Top + 20);
                            startup.Show(); ShowWindow(startup.Handle, 5); Application.DoEvents();
                            AssertContentGeometry(startup);
                            var startupCanvas = (Panel)typeof(ControlCenterForm).GetField("_pageCanvas", PrivateInstance).GetValue(startup);
                            if (Math.Abs(startupCanvas.Width - 820 * startup.LayoutScale) > 2) throw new Exception("Fresh window page scale is incorrect on " + screen.DeviceName);
                            foreach (string page in new[] { "feel", "press", "gestures", "diagnostics" }) { startup.Navigate(page); Application.DoEvents(); AssertContentGeometry(startup); }
                            startup.Close();
                        }
                    }
                    window.Location = new System.Drawing.Point(workArea.Left + 20, workArea.Top + 20);
                    for (int tick = 0; tick < 20; tick++) { Application.DoEvents(); Thread.Sleep(10); }
                    Pass("all attached monitors preserve DPI and a single page scale");
                    window.Navigate("press");
                    Application.DoEvents();
                    var threshold = (NumericUpDown)typeof(ControlCenterForm).GetField("_threshold", PrivateInstance).GetValue(window);
                    var draft = (ForceClickOptions)typeof(ControlCenterForm).GetField("_draft", PrivateInstance).GetValue(window);
                    int originalThreshold = original.ForceClickThreshold;
                    int expected = originalThreshold == 90 ? 100 : 90;
                    threshold.Value = expected;
                    Application.DoEvents();
                    window.Navigate("diagnostics");
                    Application.DoEvents();
                    if (!window.CanSave) throw new Exception("An actual numeric-input edit did not enable the primary save action.");
                    Invoke(window, "SaveChanges");
                    int saved = ForceClickOptions.Load(config).ForceClickThreshold;
                    if (saved != expected) throw new Exception("Saved JSON did not match edited threshold: " + saved);
                    if (Directory.GetFiles(Path.Combine(directory, "control-center-backups"), "*.json").Length < 1)
                        throw new Exception("The real UI save did not create a backup.");
                    Pass("actual edit, save, backup and file readback");
                    Invoke(window, "LoadConfiguration", true);
                    window.Navigate("press");
                    Application.DoEvents();
                    threshold = (NumericUpDown)typeof(ControlCenterForm).GetField("_threshold", PrivateInstance).GetValue(window);
                    threshold.Value = originalThreshold;
                    Application.DoEvents();
                    Invoke(window, "SaveChanges");
                    if (ForceClickOptions.Load(config).ForceClickThreshold != original.ForceClickThreshold)
                        throw new Exception("The isolated configuration was not restored.");
                    Pass("test copy restored without touching production settings");
                    ForceClickOptions external = ForceClickOptions.Load(config);
                    external.MaxGain = 4.25;
                    external.Save(config);
                    window.Navigate("press");
                    threshold = (NumericUpDown)typeof(ControlCenterForm).GetField("_threshold", PrivateInstance).GetValue(window);
                    threshold.Value = originalThreshold == 90 ? 100 : 90;
                    bool warningSeen = DriveDialog(window, delegate { Log("DRIVE save-with-external-edit"); Invoke(window, "SaveChanges"); }, "保存未完成", 1, Path.Combine(output, "save-conflict.png"));
                    if (!warningSeen) throw new Exception("External edit was not rejected by the UI save warning.");
                    var stateLabel = (Label)typeof(ControlCenterForm).GetField("_stateLabel", PrivateInstance).GetValue(window);
                    if (stateLabel.Text != "保存未完成") throw new Exception("Save state did not remain visibly incomplete: " + stateLabel.Text);
                    if (ForceClickOptions.Load(config).MaxGain != 4.25) throw new Exception("External edit was overwritten.");
                    Pass("external configuration edit is not overwritten");
                    Invoke(window, "LoadConfiguration", true);
                    window.Navigate("press");
                    Application.DoEvents();
                    threshold = (NumericUpDown)typeof(ControlCenterForm).GetField("_threshold", PrivateInstance).GetValue(window);
                    threshold.Value = originalThreshold == 90 ? 100 : 90;
                    Application.DoEvents();
                    if (!window.CanSave) throw new Exception("Fresh threshold edit did not produce unsaved changes.");
                    bool closeConfirmationSeen = DriveDialog(window, delegate { Log("DRIVE close-with-unsaved-edit"); window.Close(); }, "Magic Trackpad 控制中心", 7, Path.Combine(output, "close-confirmation.png"));
                    if (!closeConfirmationSeen) throw new Exception("Closing did not show the expected confirmation dialog.");
                    if (window.IsDisposed) throw new Exception("Closing cancelled did not keep the window alive.");
                    if (!((System.Windows.Forms.Timer)typeof(ControlCenterForm).GetField("_timer", PrivateInstance).GetValue(window)).Enabled)
                        throw new Exception("Cancelling close stopped runtime refresh.");
                    if (ForceClickOptions.Load(config).ForceClickThreshold != originalThreshold) throw new Exception("Cancelled close wrote production/test configuration.");
                    Pass("closing with unsaved changes requires confirmation");
                    Invoke(window, "LoadConfiguration", true);
                    if (window.CanSave) throw new Exception("Discard did not restore a clean window.");
                    window.Navigate("feel");
                    Application.DoEvents();
                    window.PageHost.AutoScrollPosition = new System.Drawing.Point(0, 10000);
                    Application.DoEvents();
                    if (window.PageHost.AutoScrollPosition.Y >= 0) throw new Exception("Long pointer page cannot scroll.");
                    Capture(window, Path.Combine(output, "feel-scrolled.png"));
                    Pass("long pointer page scrolls to its final settings");
                    window.Navigate("press");
                    threshold = (NumericUpDown)typeof(ControlCenterForm).GetField("_threshold", PrivateInstance).GetValue(window);
                    threshold.Value = originalThreshold == 90 ? 100 : 90;
                    if (!DriveDialog(window, delegate { window.Close(); }, "Magic Trackpad 控制中心", 6, Path.Combine(output, "close-discard.png")))
                        throw new Exception("Discard-and-close confirmation was not driven.");
                    if (!window.IsDisposed) throw new Exception("Confirmed close did not dispose the window.");
                    Pass("confirmed close cleans up the real window");
                }
            }
            catch (Exception ex) { _failed++; Log("FAIL " + ex); }
            if (_failed == 0 && _passed != 17) { _failed++; Log("FAIL expected 17 completed UI checks, got " + _passed); }
            Log("RESULT control_center_ui_passed=" + _passed + " failed=" + _failed);
            File.WriteAllLines(result, new[] { "control_center_ui_passed=" + _passed, "expected=17", "completed=" + (_failed == 0 ? "true" : "false"), "failed=" + _failed });
            return _failed == 0 ? 0 : 1;
        }

        private static void Invoke(object target, string name, params object[] args)
        {
            try
            {
                var types = new Type[args.Length];
                for (int i = 0; i < args.Length; i++) types[i] = args[i] == null ? typeof(object) : args[i].GetType();
                var method = target.GetType().GetMethod(name, PrivateInstance, null, types, null);
                if (method == null) throw new MissingMethodException(target.GetType().FullName, name);
                method.Invoke(target, args);
            }
            catch (TargetInvocationException ex) { throw ex.InnerException; }
        }

        private static void Capture(Form window, string path)
        {
            // DrawToBitmap reverses sibling paint order and can hide overlap
            // defects. Capture the native window instead; geometry assertions
            // additionally verify every label and sibling input rectangle.
            CaptureHandle(window.Handle, path);
        }

        private static bool IsContent(Control control)
        {
            return control.Name == "divider" || control is FlowLayoutPanel || (!string.IsNullOrEmpty(control.Text) && (control is Label || control is ButtonBase || control is TextBox));
        }

        private static void AssertContentGeometry(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                // A one-pixel logical line may round to two physical pixels
                // after a monitor transition; it must never become a text row.
                if (control.Name == "divider" && control.Height > Math.Max(2, Math.Ceiling(GetDpiForWindow(control.Handle) / 96.0)))
                    throw new Exception("A decorative divider was expanded into a text-height block: " + control.Height);
                if (IsContent(control))
                    foreach (Control sibling in parent.Controls)
                        if (control != sibling && IsContent(sibling) && control.Bounds.IntersectsWith(sibling.Bounds))
                            throw new Exception("Text/control collision: [" + control.Text + "] " + control.Bounds + " covers [" + sibling.Text + "] " + sibling.Bounds);
                Label label = control as Label;
                if (label != null && !string.IsNullOrEmpty(label.Text) && label.GetPreferredSize(new System.Drawing.Size(label.Width, 0)).Height > label.Height + 1)
                {
                    using (var graphics = label.CreateGraphics()) {
                        var measured = TextRenderer.MeasureText(graphics, label.Text, label.Font, new System.Drawing.Size(label.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                        Log("TEXT_METRICS text=" + label.Text + " device=" + label.DeviceDpi + " window=" + GetDpiForWindow(label.Handle) + " graphics=" + graphics.DpiX + " font=" + label.Font.SizeInPoints + " bounds=" + label.Bounds + " actual=" + measured);
                    }
                    throw new Exception("Text height is clipped: " + label.Text + " allocated=" + label.Height + " preferred=" + label.GetPreferredSize(new System.Drawing.Size(label.Width, 0)).Height);
                }
                AssertContentGeometry(control);
            }
        }

        private static bool DriveDialog(Form owner, Action operation, string title, int buttonId, string screenshot)
        {
            uint ownerPid; GetWindowThreadProcessId(owner.Handle, out ownerPid);
            Log("DIALOG_OWNER current=" + System.Diagnostics.Process.GetCurrentProcess().Id + " native=" + ownerPid + " visible=" + IsWindowVisible(owner.Handle));
            using (var driver = new System.Windows.Forms.Timer())
            {
                driver.Interval = 100;
                int ticks = 0;
                bool seen = false;
                bool completed = false;
                Exception operationError = null;
                driver.Tick += delegate
                {
                    ticks++;
                    IntPtr dialog = FindOwnedDialog(owner, title);
                    if (ticks % 10 == 0) Log("DIALOG_PROBE title=" + title + " handle=" + dialog + " window=" + owner.Handle);
                    if (dialog != IntPtr.Zero)
                    {
                        IntPtr button = GetDlgItem(dialog, buttonId);
                        if (button == IntPtr.Zero)
                        {
                            EnumChildWindows(dialog, delegate(IntPtr child, IntPtr parameter) {
                                string text = WindowTitle(child).Replace("&", "");
                                Log("DIALOG_CHILD id=" + GetDlgCtrlID(child) + " text=" + text);
                                bool matches = GetDlgCtrlID(child) == buttonId ||
                                    (buttonId == 1 && (text.StartsWith("确定") || text.Equals("OK", StringComparison.OrdinalIgnoreCase))) ||
                                    (buttonId == 6 && (text.StartsWith("是") || text.StartsWith("Yes", StringComparison.OrdinalIgnoreCase))) ||
                                    (buttonId == 7 && (text.StartsWith("否") || text.StartsWith("No", StringComparison.OrdinalIgnoreCase)));
                                if (matches) button = child;
                                return true;
                            }, IntPtr.Zero);
                        }
                        Log("DIALOG_FOUND title=" + WindowTitle(dialog) + " handle=" + dialog + " button=" + button);
                        if (button == IntPtr.Zero) throw new Exception("Expected dialog button was not found.");
                        CaptureHandle(dialog, screenshot);
                        SendMessage(button, 0x00F5, IntPtr.Zero, IntPtr.Zero);
                        seen = true;
                        driver.Stop();
                        return;
                    }
                    if (ticks > 100)
                    {
                        driver.Stop();
                        Log("DIALOG_TIMEOUT " + DescribeOwnedWindows(owner));
                        throw new Exception("Timed out waiting for dialog: " + title);
                    }
                };
                driver.Start();
                // Post the operation outside the timer callback. MessageBox runs
                // its own modal loop; the timer must remain free to click it.
                owner.BeginInvoke(new MethodInvoker(delegate {
                    try { operation(); } catch (Exception ex) { operationError = ex; }
                    finally { completed = true; }
                }));
                DateTime deadline = DateTime.UtcNow.AddSeconds(12);
                while ((!seen || !completed) && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
                driver.Stop();
                if (operationError != null) throw operationError;
                if (!completed) throw new Exception("Dialog operation did not complete.");
                return seen;
            }
        }

        private static readonly System.Collections.Generic.List<IntPtr> Windows = new System.Collections.Generic.List<IntPtr>();
        private static bool CollectWindow(IntPtr window, IntPtr parameter) { Windows.Add(window); return true; }
        private static void RefreshWindows()
        {
            Windows.Clear();
            if (!EnumWindows(CollectWindow, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        private static string DescribeOwnedWindows(Form owner)
        {
            var description = new System.Text.StringBuilder();
            RefreshWindows();
            foreach (IntPtr handle in Windows)
            {
                uint processId;
                GetWindowThreadProcessId(handle, out processId);
                if (processId == System.Diagnostics.Process.GetCurrentProcess().Id)
                    description.Append(handle).Append("=[").Append(WindowTitle(handle)).Append("] visible=").Append(IsWindowVisible(handle)).Append(" ");
            }
            return description.ToString();
        }

        private static IntPtr FindConfirmationDialog(Form owner)
        {
            return FindOwnedDialog(owner, "Magic Trackpad 控制中心");
        }

        private static IntPtr FindOwnedDialog(Form owner, string title)
        {
            IntPtr found = IntPtr.Zero;
            RefreshWindows();
            foreach (IntPtr handle in Windows)
            {
                uint processId;
                GetWindowThreadProcessId(handle, out processId);
                if (processId == System.Diagnostics.Process.GetCurrentProcess().Id && handle != owner.Handle && IsWindowVisible(handle) && WindowTitle(handle) == title) { found = handle; break; }
            }
            return found;
        }

        private static string WindowTitle(IntPtr handle)
        {
            var title = new System.Text.StringBuilder(256);
            GetWindowText(handle, title, title.Capacity);
            return title.ToString();
        }

        private static void CaptureHandle(IntPtr handle, string path)
        {
            RECT rect;
            if (!GetWindowRect(handle, out rect)) throw new Exception("Could not read native window bounds.");
            using (var image = new System.Drawing.Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top))
            using (var graphics = System.Drawing.Graphics.FromImage(image))
            {
                IntPtr dc = graphics.GetHdc();
                try
                {
                    if (!PrintWindow(handle, dc, 0)) throw new Exception("Native confirmation screenshot failed.");
                }
                finally { graphics.ReleaseHdc(dc); }
                image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private static void Pass(string name) { _passed++; Log("PASS " + name); }
        private static void Log(string text)
        {
            Console.WriteLine(text);
            using (var stream = new FileStream(Path.Combine(Path.GetDirectoryName(resultPath), "UiTestLog.txt"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.WriteLine(text);
                writer.Flush();
            }
        }
    }
}
