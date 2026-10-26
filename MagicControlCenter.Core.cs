using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Runtime.Serialization.Json;
using System.Web.Script.Serialization;

namespace MagicForceClick
{
    internal sealed class ConfigurationSnapshot
    {
        internal byte[] Bytes;
        internal string Fingerprint;
        internal ForceClickOptions Options;

        internal static ConfigurationSnapshot Read(string path)
        {
            if (!File.Exists(path)) return new ConfigurationSnapshot { Fingerprint = "missing", Options = new ForceClickOptions() };
            byte[] bytes;
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var buffer = new MemoryStream()) { file.CopyTo(buffer); bytes = buffer.ToArray(); }
            return FromBytes(bytes);
        }

        internal static ConfigurationSnapshot FromBytes(byte[] bytes)
        {
            ForceClickOptions options;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(Text(bytes))))
                options = (ForceClickOptions)new DataContractJsonSerializer(typeof(ForceClickOptions)).ReadObject(stream);
            if (options == null) throw new InvalidDataException("配置不能为 null。");
            options.Validate();
            return new ConfigurationSnapshot { Bytes = bytes, Fingerprint = Hash(bytes), Options = options };
        }

        internal static string Text(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true)) return reader.ReadToEnd();
        }

        internal static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
    }

    internal enum ConfigurationSaveStage { BeforeCommit, AfterCommit }

    internal sealed class ConfigurationStore
    {
        // Tests observe the real commit boundary without replacing the file implementation.
        private readonly Action<ConfigurationSaveStage> _observe;
        internal ConfigurationStore() { }
        internal ConfigurationStore(Action<ConfigurationSaveStage> observe) { _observe = observe; }

        internal static string Fingerprint(string path)
        {
            if (!File.Exists(path)) return "missing";
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        internal BackupInfo Save(string path, ForceClickOptions value, string expectedFingerprint)
        {
            value.Validate();
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            using (var gate = new FileStream(path + ".control-center.lock", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                ConfigurationSnapshot before = ConfigurationSnapshot.Read(path);
                CheckFingerprint(before.Fingerprint, expectedFingerprint);
                string backupDirectory = Path.Combine(directory, "control-center-backups");
                Directory.CreateDirectory(backupDirectory);
                string backupPath = Path.Combine(backupDirectory, "MagicForceClick-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllBytes(backupPath, before.Bytes ?? Encoding.UTF8.GetBytes("{}"));
                var json = new JavaScriptSerializer();
                var merged = before.Bytes != null ? json.Deserialize<Dictionary<string, object>>(ConfigurationSnapshot.Text(before.Bytes)) : new Dictionary<string, object>();
                if (merged == null) throw new InvalidDataException("配置必须是 JSON 对象。");
                using (var stream = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(ForceClickOptions)).WriteObject(stream, value);
                    var known = json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(stream.ToArray()));
                    foreach (var item in known) merged[item.Key] = item.Value;
                }
                string temp = path + ".control-center-" + Guid.NewGuid().ToString("N") + ".tmp";
                bool committed = false;
                try
                {
                    byte[] wantedBytes = new UTF8Encoding(false).GetBytes(json.Serialize(merged));
                    ConfigurationSnapshot wanted = ConfigurationSnapshot.FromBytes(wantedBytes);
                    File.WriteAllBytes(temp, wantedBytes);
                    if (_observe != null) _observe(ConfigurationSaveStage.BeforeCommit);
                    CheckFingerprint(Fingerprint(path), expectedFingerprint);
                    if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                    committed = true;
                    if (_observe != null) _observe(ConfigurationSaveStage.AfterCommit);
                    ConfigurationSnapshot after = ConfigurationSnapshot.Read(path);
                    CheckFingerprint(after.Fingerprint, wanted.Fingerprint);
                    return new BackupInfo { FullName = backupPath, Snapshot = after };
                }
                catch (Exception error)
                {
                    if (committed) throw new ConfigurationCommittedException(backupPath, error);
                    throw;
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }

        private static void CheckFingerprint(string actual, string expected)
        {
            if (actual != expected) throw new ConfigurationChangedException("配置已在其他窗口中更改。请重新读取后再保存，未覆盖其他窗口的修改。");
        }
    }

    internal enum PointerPreset { LightCrossScreen, Classic, Custom }

    internal static class ProfileCatalog
    {
        internal static PointerPreset IdentifyPointer(ForceClickOptions actual)
        {
            var light = new ForceClickOptions(); light.ApplyLightCrossScreenPreset();
            if (SameCurve(actual, light)) return PointerPreset.LightCrossScreen;
            if (SameCurve(actual, new ForceClickOptions())) return PointerPreset.Classic;
            return PointerPreset.Custom;
        }

        internal static string Identify(ForceClickOptions actual) { return IdentifyPointer(actual).ToString(); }

        internal static void ApplyPointer(ForceClickOptions actual, PointerPreset preset)
        {
            if (preset == PointerPreset.Custom) throw new ArgumentException("自定义曲线不能作为固定预设应用。");
            var wanted = new ForceClickOptions();
            if (preset == PointerPreset.LightCrossScreen) wanted.ApplyLightCrossScreenPreset();
            actual.MinGain = wanted.MinGain; actual.MaxGain = wanted.MaxGain;
            actual.MidSpeed = wanted.MidSpeed; actual.CurvePower = wanted.CurvePower;
        }

        internal static string ModeName(ForceClickOptions value)
        {
            if (value.EnableAcceleration && value.EnableForceClick) return "指针加速与预览均启用";
            if (value.EnableAcceleration) return "仅指针加速";
            return value.EnableForceClick ? "仅深按预览" : "增强功能均已关闭";
        }

        private static bool SameCurve(ForceClickOptions a, ForceClickOptions b)
        {
            return a.MinGain == b.MinGain && a.MaxGain == b.MaxGain && a.MidSpeed == b.MidSpeed && a.CurvePower == b.CurvePower;
        }
    }

    internal static class OptionValues
    {
        internal static bool Equal(ForceClickOptions a, ForceClickOptions b)
        {
            if (a == null || b == null) return false;
            foreach (var field in typeof(ForceClickOptions).GetFields())
                if (!object.Equals(field.GetValue(a), field.GetValue(b))) return false;
            return true;
        }
    }

    internal sealed class DraftValidationIssue
    {
        internal string Field, Message;
        internal static DraftValidationIssue Check(ForceClickOptions options)
        {
            try { options.Validate(); return null; }
            catch (InvalidDataException error)
            {
                if (error.Message.StartsWith("Immediate pressure")) return new DraftValidationIssue { Field = "ForceClickImmediateThreshold", Message = "快速深按阈值须为 0，或不小于压力阈值。" };
                if (error.Message.StartsWith("Pressure below")) return new DraftValidationIssue { Field = "ForceClickThreshold", Message = "压力低于 120 时，稳定时间须至少 120 ms，加力须至少 10。" };
                var names = new Dictionary<string, string> {
                    {"ForceClickThreshold","压力阈值"}, {"ForceClickDwellMs","稳定时间"}, {"ForceClickPressureRise","按下后加力"},
                    {"ForceClickImmediateThreshold","快速深按阈值"}, {"DragCancelDistance","触控板移动容差"}, {"DragCancelCursorPixels","拖拽光标容差"},
                    {"MinGain","慢速增益"}, {"MaxGain","快速最大增益"}, {"MidSpeed","加速介入速度"}, {"CurvePower","曲线指数"},
                    {"SpeedSmoothingMs","速度平滑"}, {"MaxFrameIntervalMs","最大帧间隔"}, {"IdleResetMs","空闲重置时间"}
                };
                foreach (var item in names)
                    if (error.Message.StartsWith(item.Key + " ")) return new DraftValidationIssue { Field = item.Key, Message = item.Value + "超出允许范围。" + (item.Key == "MaxGain" ? "快速最大增益不能小于慢速增益。" : "") };
                return new DraftValidationIssue { Field = "", Message = "配置格式或版本不受支持，请重新读取配置。" };
            }
        }
    }

    internal sealed class SettingsSaveResult
    {
        internal bool Success, JsonCommitted, NativeAttempted, NativeRestored, RequiresReload;
        internal ConfigurationSnapshot Configuration;
        internal NativeTouchpadSnapshot Native;
        internal Exception Error, RecoveryError;
        internal string BackupPath;
        internal string FailureMessage
        {
            get
            {
                if (JsonCommitted) return "配置已写入，但回读未确认完成。请重新读取后核对，暂不能再次保存。";
                if (RecoveryError != null) return "保存未完成，原生设置恢复未获确认。请重新读取并核对 Windows 设置。";
                return "增强配置未被本次保存替换。" + (NativeRestored ? "原生设置已恢复并回读。" : "") + "可修改后重试，或重新读取。";
            }
        }
    }

    internal static class SettingsSaver
    {
        internal static SettingsSaveResult Save(string path, ForceClickOptions wanted, ForceClickOptions saved, string fingerprint,
            NativeTouchpadSnapshot nativeWanted, NativeTouchpadSnapshot nativeSaved, INativeTouchpadPlatform native, ConfigurationStore store)
        {
            var result = new SettingsSaveResult();
            NativeTouchpadBackup before = null;
            try
            {
                wanted.Validate();
                bool jsonChanged = !OptionValues.Equal(wanted, saved);
                bool nativeChanged = nativeWanted != null && !nativeWanted.SameEditableValues(nativeSaved);
                if (!jsonChanged && !nativeChanged) { result.Success = true; return result; }
                if (ConfigurationStore.Fingerprint(path) != fingerprint) throw new ConfigurationChangedException("配置已在其他窗口中更改。请重新读取后再保存。");
                if (nativeChanged)
                {
                    before = native.Capture();
                    if (!before.Snapshot.SameEditableValues(nativeSaved)) throw new ConfigurationChangedException("Windows 触控板设置已被其他窗口更改。请重新读取后再保存。");
                    result.NativeAttempted = true;
                    native.Apply(nativeWanted);
                    result.Native = native.Read();
                    if (!result.Native.SameEditableValues(nativeWanted)) throw new InvalidOperationException("Windows 触控板参数回读不一致。");
                }
                if (jsonChanged)
                {
                    BackupInfo backup = store.Save(path, wanted, fingerprint);
                    result.JsonCommitted = true; result.Configuration = backup.Snapshot; result.BackupPath = backup.FullName;
                }
                result.Success = true;
            }
            catch (Exception error)
            {
                result.Error = error;
                if (error is ConfigurationChangedException) result.RequiresReload = true;
                var committed = error as ConfigurationCommittedException;
                if (committed != null) { result.JsonCommitted = true; result.BackupPath = committed.BackupPath; result.RequiresReload = true; }
                if (result.NativeAttempted && !result.JsonCommitted)
                {
                    try { native.Restore(before); result.NativeRestored = true; }
                    catch (Exception recovery) { result.RecoveryError = recovery; result.RequiresReload = true; }
                }
            }
            return result;
        }
    }

    internal sealed class ConfigurationChangedException : Exception
    {
        internal ConfigurationChangedException(string message) : base(message) { }
    }

    internal sealed class ConfigurationCommittedException : Exception
    {
        internal readonly string BackupPath;
        internal ConfigurationCommittedException(string backupPath, Exception inner) : base("配置已提交，但后续回读失败。", inner) { BackupPath = backupPath; }
    }

    internal sealed class BackupInfo
    {
        internal string FullName { get; set; }
        internal ConfigurationSnapshot Snapshot { get; set; }
    }
}
