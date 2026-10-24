using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Runtime.Serialization.Json;
using System.Web.Script.Serialization;

namespace MagicForceClick
{
    internal sealed class ConfigurationStore
    {
        internal static string Fingerprint(string path)
        {
            if (!File.Exists(path)) return "missing";
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        internal BackupInfo Save(string path, ForceClickOptions value, string expectedFingerprint)
        {
            value.Validate();
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            // Serialize saves from multiple control-center windows. Editors that
            // do not use this lock are checked again just before the replacement.
            using (var gate = new FileStream(path + ".control-center.lock", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            CheckFingerprint(path, expectedFingerprint);
            string backupDirectory = Path.Combine(directory, "control-center-backups");
            Directory.CreateDirectory(backupDirectory);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string backupPath = Path.Combine(backupDirectory, "MagicForceClick-" + stamp + ".json");
            if (File.Exists(path)) File.Copy(path, backupPath, false);
            else File.WriteAllText(backupPath, "{}", new UTF8Encoding(false));
            var json = new JavaScriptSerializer();
            var merged = File.Exists(path) ? json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8)) : new Dictionary<string, object>();
            if (merged == null) throw new InvalidDataException("配置必须是 JSON 对象。");
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(ForceClickOptions)).WriteObject(stream, value);
                var known = json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(stream.ToArray()));
                foreach (var item in known) merged[item.Key] = item.Value;
            }
            string temp = path + ".control-center-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, json.Serialize(merged), new UTF8Encoding(false));
                ForceClickOptions.Load(temp);
                CheckFingerprint(path, expectedFingerprint);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                ForceClickOptions.Load(path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return new BackupInfo { FullName = File.Exists(backupPath) ? backupPath : null };
            }
        }

        private static void CheckFingerprint(string path, string expected)
        {
            if (Fingerprint(path) != expected)
                throw new ConfigurationChangedException("配置已在其他窗口中更改。请重新加载后再保存，未覆盖任何修改。");
        }
    }

    internal static class ProfileCatalog
    {
        internal static string Identify(ForceClickOptions actual)
        {
            ForceClickOptions baseline = new ForceClickOptions();
            ForceClickOptions light = new ForceClickOptions();
            light.ApplyLightCrossScreenPreset();
            if (SameFeel(actual, light)) return "LightCrossScreen";
            if (SameFeel(actual, baseline)) return "Classic";
            return "Custom";
        }

        private static bool SameFeel(ForceClickOptions a, ForceClickOptions b)
        {
            return a.ForceClickThreshold == b.ForceClickThreshold && a.ForceClickDwellMs == b.ForceClickDwellMs
                && a.ForceClickPressureRise == b.ForceClickPressureRise && a.ForceClickImmediateThreshold == b.ForceClickImmediateThreshold
                && a.MinGain == b.MinGain && a.MaxGain == b.MaxGain && a.MidSpeed == b.MidSpeed && a.CurvePower == b.CurvePower;
        }
    }

    internal sealed class ConfigurationChangedException : Exception
    {
        internal ConfigurationChangedException(string message) : base(message) { }
    }

    internal sealed class BackupInfo
    {
        internal string FullName { get; set; }
    }
}
