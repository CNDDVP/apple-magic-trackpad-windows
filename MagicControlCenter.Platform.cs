using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace MagicForceClick
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct TouchpadParameters
    {
        internal uint versionNumber, maxSupportedContacts, legacyTouchpadFeatures, systemFlags;
        internal uint userFlags, sensitivityLevel, cursorSpeed, feedbackIntensity, clickForceSensitivity;
        internal uint rightClickZoneWidth, rightClickZoneHeight;

        internal static TouchpadParameters CreateForTest() { return new TouchpadParameters { versionNumber = 1 }; }
        internal bool HasFlag(uint mask) { return (userFlags & mask) != 0; }
        internal TouchpadParameters SetFlag(uint mask, bool value)
        {
            if (value) userFlags |= mask; else userFlags &= ~mask;
            return this;
        }
    }

    internal static class NativeTouchpadApi
    {
        internal static TouchpadParameters Read()
        {
            return Read(NativeMethods.TryRead);
        }

        internal static TouchpadParameters Read(NativeMethods.ReadParameters read)
        {
            TouchpadParameters value;
            if (!read(out value)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 Windows 原生触控板参数。");
            return value;
        }

        internal static void Save(NativeTouchpadSnapshot edited, NativeMethods.WriteParameters write, NativeMethods.ReadParameters read)
        {
            TouchpadParameters before = Read(read);
            TouchpadParameters wanted = before;
            wanted.userFlags = (before.userFlags & ~NativeTouchpadSnapshot.SupportedFlags) | (edited.Parameters.userFlags & NativeTouchpadSnapshot.SupportedFlags);
            wanted.cursorSpeed = edited.Parameters.cursorSpeed;
            write(wanted);
            TouchpadParameters after = Read(read);
            if (after.cursorSpeed != wanted.cursorSpeed || (after.userFlags & NativeTouchpadSnapshot.SupportedFlags) != (wanted.userFlags & NativeTouchpadSnapshot.SupportedFlags))
                throw new InvalidOperationException("Windows 触控板 API 回读与目标值不一致，未确认设置生效。");
        }

        internal static void Save(NativeTouchpadSnapshot edited)
        {
            Save(edited, NativeMethods.Write, NativeMethods.TryRead);
            SyncRegistry(edited);
        }

        private static void SyncRegistry(NativeTouchpadSnapshot edited)
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad"))
            {
                key.SetValue("CursorSpeed", edited.Parameters.cursorSpeed, Microsoft.Win32.RegistryValueKind.DWord);
                foreach (var item in new[] {
                    new { Name="LeaveOnWithMouse", Flag=0x01u }, new { Name="TapsEnabled", Flag=0x04u },
                    new { Name="TapAndDrag", Flag=0x08u }, new { Name="TwoFingerTapEnabled", Flag=0x10u },
                    new { Name="PanEnabled", Flag=0x80u }, new { Name="ZoomEnabled", Flag=0x100u },
                    new { Name="ScrollDirection", Flag=0x200u }
                }) key.SetValue(item.Name, edited.Parameters.HasFlag(item.Flag) ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
            }
        }
    }

    internal sealed class NativeTouchpadSnapshot
    {
        internal const uint SupportedFlags = 0x01 | 0x04 | 0x08 | 0x10 | 0x80 | 0x100 | 0x200;
        internal TouchpadParameters Parameters { get; private set; }
        internal NativeTouchpadSnapshot(TouchpadParameters value) { Parameters = value; }
        internal static NativeTouchpadSnapshot Read() { return new NativeTouchpadSnapshot(NativeTouchpadApi.Read()); }
        internal bool SameEditableValues(NativeTouchpadSnapshot other)
        {
            return other != null && Parameters.cursorSpeed == other.Parameters.cursorSpeed
                && (Parameters.userFlags & SupportedFlags) == (other.Parameters.userFlags & SupportedFlags);
        }
        internal NativeTouchpadSnapshot WithSetting(string name, object value)
        {
            uint number = Convert.ToUInt32(value);
            TouchpadParameters edited = Parameters;
            if (name == "cursorSpeed")
            {
                if (number < 1 || number > 20) throw new ArgumentOutOfRangeException("cursorSpeed");
                edited.cursorSpeed = number;
                Parameters = edited;
                return this;
            }
            uint mask;
            switch (name)
            {
                case "leaveOnWithMouse": mask = 0x01; break;
                case "tap": mask = 0x04; break;
                case "tapAndDrag": mask = 0x08; break;
                case "twoFingerTap": mask = 0x10; break;
                case "pan": mask = 0x80; break;
                case "zoom": mask = 0x100; break;
                case "reverseScroll": mask = 0x200; break;
                default: throw new ArgumentException("Unknown native touchpad setting: " + name);
            }
            Parameters = edited.SetFlag(mask, number != 0);
            return this;
        }
    }

    [DataContract]
    internal sealed class RuntimeStatusSnapshot
    {
        [DataMember] internal string Version;
        [DataMember] internal DateTime TimeUtc;
        [DataMember] internal long ValidScanFrames;
        [DataMember] internal long DuplicateScanFrames;
        [DataMember] internal long Discontinuities;
        [DataMember] internal long PointerSubmissionFailures;
        [DataMember] internal bool RawInputRegistered;
        [DataMember] internal bool PressBlocked;
        [DataMember] internal string PointerOutput;
        [DataMember] internal ForceClickOptions Options;
        internal bool IsLive { get; set; }
        internal string Error { get; private set; }
        internal DateTime LastWriteUtc { get; private set; }

        internal static RuntimeStatusSnapshot Read(string path, TimeSpan freshness)
        {
            var serializer = new DataContractJsonSerializer(typeof(RuntimeStatusSnapshot));
            RuntimeStatusSnapshot result;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                result = (RuntimeStatusSnapshot)serializer.ReadObject(stream);
            DateTime modified = File.GetLastWriteTimeUtc(path);
            result.LastWriteUtc = modified;
            DateTime now = DateTime.UtcNow;
            result.IsLive = Fresh(modified, now, freshness) && Fresh(result.TimeUtc, now, freshness);
            return result;
        }

        internal static bool Fresh(DateTime time, DateTime now, TimeSpan freshness)
        {
            if (time == DateTime.MinValue) return false;
            DateTime utc = time.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(time, DateTimeKind.Utc) : time.ToUniversalTime();
            return utc <= now && now - utc <= freshness;
        }

        internal static RuntimeStatusSnapshot Unavailable(string message)
        {
            return new RuntimeStatusSnapshot { IsLive = false, Error = message };
        }
    }

    internal interface INativeTouchpadPlatform
    {
        NativeTouchpadSnapshot Read();
        NativeTouchpadBackup Capture();
        void Apply(NativeTouchpadSnapshot wanted);
        void Restore(NativeTouchpadBackup backup);
    }

    internal sealed class NativeTouchpadBackup
    {
        internal NativeTouchpadSnapshot Snapshot;
        internal TouchpadRegistryBackup Registry;
    }

    internal sealed class WindowsTouchpadPlatform : INativeTouchpadPlatform
    {
        public NativeTouchpadSnapshot Read() { return NativeTouchpadSnapshot.Read(); }
        public NativeTouchpadBackup Capture() { return new NativeTouchpadBackup { Snapshot = Read(), Registry = TouchpadRegistryBackup.Capture() }; }
        public void Apply(NativeTouchpadSnapshot wanted)
        {
            NativeTouchpadApi.Save(wanted);
            TouchpadRegistryBackup.VerifyApplied(wanted);
        }
        public void Restore(NativeTouchpadBackup backup)
        {
            Exception failure = null;
            try { NativeTouchpadApi.Save(backup.Snapshot); }
            catch (Exception error) { failure = error; }
            try { backup.Registry.Restore(); }
            catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            if (failure != null) throw failure;
            if (!Read().SameEditableValues(backup.Snapshot)) throw new InvalidOperationException("原生设置恢复回读失败。");
        }
    }

    internal sealed class MemoryTouchpadPlatform : INativeTouchpadPlatform
    {
        private TouchpadParameters _value;
        internal MemoryTouchpadPlatform() { _value = TouchpadParameters.CreateForTest(); _value.userFlags = 439; _value.cursorSpeed = 10; }
        public NativeTouchpadSnapshot Read() { return new NativeTouchpadSnapshot(_value); }
        public NativeTouchpadBackup Capture() { return new NativeTouchpadBackup { Snapshot = Read() }; }
        public void Apply(NativeTouchpadSnapshot wanted) { _value = wanted.Parameters; }
        public void Restore(NativeTouchpadBackup backup) { _value = backup.Snapshot.Parameters; }
    }

    internal sealed class TouchpadRegistryBackup
    {
        private const string Path = @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";
        private static readonly string[] Names = { "CursorSpeed", "LeaveOnWithMouse", "TapsEnabled", "TapAndDrag", "TwoFingerTapEnabled", "PanEnabled", "ZoomEnabled", "ScrollDirection" };
        private readonly Dictionary<string, RegistryValueRecord> _values = new Dictionary<string, RegistryValueRecord>();
        private bool _existed;

        internal static TouchpadRegistryBackup Capture()
        {
            var result = new TouchpadRegistryBackup();
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Path))
            {
                result._existed = key != null;
                foreach (string name in Names)
                {
                    object value = key == null ? null : key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    result._values[name] = new RegistryValueRecord { Value = value, Kind = value == null ? RegistryValueKind.None : key.GetValueKind(name) };
                }
            }
            return result;
        }

        internal void Restore()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Path))
            {
                foreach (var item in _values)
                    if (item.Value.Value == null) key.DeleteValue(item.Key, false); else key.SetValue(item.Key, item.Value.Value, item.Value.Kind);
            }
            TouchpadRegistryBackup after = Capture();
            foreach (var item in _values)
            {
                RegistryValueRecord actual = after._values[item.Key];
                if (actual.Kind != item.Value.Kind || !RegistryValueRecord.Equal(actual.Value, item.Value.Value)) throw new InvalidOperationException("原生注册表恢复回读失败：" + item.Key);
            }
            if (!_existed)
            {
                bool empty;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Path)) empty = key != null && key.ValueCount == 0 && key.SubKeyCount == 0;
                if (empty) Registry.CurrentUser.DeleteSubKey(Path, false);
            }
        }

        internal static void VerifyApplied(NativeTouchpadSnapshot wanted)
        {
            uint[] flags = { 0, 0x01, 0x04, 0x08, 0x10, 0x80, 0x100, 0x200 };
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Path))
                for (int i = 0; i < Names.Length; i++)
                {
                    uint expected = i == 0 ? wanted.Parameters.cursorSpeed : (wanted.Parameters.HasFlag(flags[i]) ? 1u : 0u);
                    if (key == null || key.GetValue(Names[i]) == null || Convert.ToUInt32(key.GetValue(Names[i])) != expected)
                        throw new InvalidOperationException("原生注册表写入回读失败：" + Names[i]);
                }
        }

        private sealed class RegistryValueRecord
        {
            internal object Value; internal RegistryValueKind Kind;
            internal static bool Equal(object a, object b)
            {
                if (a == null || b == null) return a == b;
                if (a is Array && b is Array) return System.Collections.StructuralComparisons.StructuralEqualityComparer.Equals(a, b);
                return object.Equals(a, b);
            }
        }
    }

    internal sealed class RuntimeObservation
    {
        internal RuntimeStatusSnapshot Status;
        internal bool ProcessRunning;
        internal DateTime ProcessStartUtc;
    }

    internal interface IRuntimeStatusProvider
    {
        RuntimeObservation Read(string directory, string statusPath);
    }

    internal sealed class RuntimeStatusProvider : IRuntimeStatusProvider
    {
        private readonly bool _isolated;
        internal RuntimeStatusProvider(bool isolated) { _isolated = isolated; }
        public RuntimeObservation Read(string directory, string statusPath)
        {
            var result = new RuntimeObservation();
            if (_isolated) { result.Status = RuntimeStatusSnapshot.Unavailable("隔离测试模式"); return result; }
            try { result.Status = RuntimeStatusSnapshot.Read(statusPath, TimeSpan.FromSeconds(15)); }
            catch (Exception error) { result.Status = RuntimeStatusSnapshot.Unavailable(error.Message); }
            string expected = System.IO.Path.Combine(directory, "MagicForceClick.exe");
            foreach (Process process in Process.GetProcessesByName("MagicForceClick")) using (process)
            {
                try
                {
                    if (string.Equals(process.MainModule.FileName, expected, StringComparison.OrdinalIgnoreCase))
                    { result.ProcessRunning = true; result.ProcessStartUtc = process.StartTime.ToUniversalTime(); }
                }
                catch { }
            }
            return result;
        }
    }

    internal sealed class RuntimeAssessment
    {
        internal bool Healthy, Applied;
        internal string Reason;
        internal static RuntimeAssessment Evaluate(RuntimeObservation observation, ForceClickOptions saved, DateTime after, DateTime now)
        {
            var result = new RuntimeAssessment();
            RuntimeStatusSnapshot status = observation == null ? null : observation.Status;
            if (observation == null || !observation.ProcessRunning) result.Reason = "后台进程未运行";
            else if (status == null || status.Error != null) result.Reason = "没有可用的后台状态";
            else if (!status.IsLive || !RuntimeStatusSnapshot.Fresh(status.TimeUtc, now, TimeSpan.FromSeconds(15)) || !RuntimeStatusSnapshot.Fresh(status.LastWriteUtc, now, TimeSpan.FromSeconds(15))) result.Reason = "后台状态已过期";
            else if (status.TimeUtc.ToUniversalTime() < observation.ProcessStartUtc) result.Reason = "状态属于旧后台进程";
            else if (!status.RawInputRegistered) result.Reason = "触控板输入尚未注册";
            else
            {
                result.Healthy = true;
                if (!OptionValues.Equal(status.Options, saved)) result.Reason = "后台参数与已保存配置不一致";
                else if (status.TimeUtc.ToUniversalTime() < after || status.LastWriteUtc < after) result.Reason = "等待保存后的新鲜状态";
                else { result.Applied = true; result.Reason = "后台已生效"; }
            }
            return result;
        }
    }

    internal static class NativeMethods
    {
        internal delegate bool ReadParameters(out TouchpadParameters value);
        internal delegate void WriteParameters(TouchpadParameters value);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        private static extern bool Spi(uint action, uint size, ref TouchpadParameters value, uint flags);
        internal static bool TryRead(out TouchpadParameters value)
        {
            value = TouchpadParameters.CreateForTest();
            return Spi(0xAE, (uint)Marshal.SizeOf(typeof(TouchpadParameters)), ref value, 0);
        }
        internal static void Write(TouchpadParameters value)
        {
            if (!Spi(0xAF, (uint)Marshal.SizeOf(typeof(TouchpadParameters)), ref value, 3))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法写入 Windows 原生触控板参数。");
        }
    }
}
