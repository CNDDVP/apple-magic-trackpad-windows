using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Runtime.InteropServices;
using System.ComponentModel;

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
            result.IsLive = modified <= DateTime.UtcNow && DateTime.UtcNow - modified <= freshness;
            return result;
        }

        internal static RuntimeStatusSnapshot Unavailable(string message)
        {
            return new RuntimeStatusSnapshot { IsLive = false, Error = message };
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
