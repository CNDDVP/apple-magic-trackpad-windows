using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MagicForceClick
{
    // Read only Win32 context. No window activation, selection changes or file access.
    internal sealed class PreviewContext
    {
        public IntPtr Window, Focus;
        public string ProcessName, WindowClass, FocusClass;
        public PreviewRoute Route;
        public static PreviewContext Capture(IntPtr window)
        {
            var result = new PreviewContext { Window = window };
            try
            {
                uint pid;
                uint thread = GetWindowThreadProcessId(window, out pid);
                var info = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
                if (window == IntPtr.Zero || thread == 0 || !GetGUIThreadInfo(thread, ref info)
                    || info.hwndFocus == IntPtr.Zero || GetAncestor(info.hwndFocus, 2) != window)
                    return result;
                uint focusPid;
                GetWindowThreadProcessId(info.hwndFocus, out focusPid);
                if (focusPid != pid) return result;
                using (var process = Process.GetProcessById((int)pid)) result.ProcessName = process.ProcessName;
                result.WindowClass = ClassName(window);
                result.Focus = info.hwndFocus;
                result.FocusClass = ClassName(result.Focus);
                result.Route = PreviewPolicy.Resolve(result.ProcessName, result.WindowClass, result.FocusClass, ModifiersHeld());
            }
            catch { result.Route = PreviewRoute.None; }
            return result;
        }
        public bool StillCurrent()
        {
            if (Window != GetForegroundWindow() || Route == PreviewRoute.None) return false;
            var current = Capture(Window);
            return current.Route == Route && current.Focus == Focus;
        }
        private static string ClassName(IntPtr hwnd)
        { var name = new StringBuilder(256); GetClassName(hwnd, name, name.Capacity); return name.ToString(); }
        private static bool ModifiersHeld()
        {
            foreach (int key in new int[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
                if ((GetAsyncKeyState(key) & 0x8000) != 0) return true;
            return false;
        }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct GUITHREADINFO
        {
            public int cbSize, flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public RECT rcCaret;
        }
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int size);
    }
}
