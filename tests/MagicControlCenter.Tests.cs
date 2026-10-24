using System;
using System.IO;
using MagicForceClick;

namespace MagicControlCenter.Tests
{
    internal sealed class ControlCenterTests
    {
        private static readonly ControlCenterTests NativeHarness = new ControlCenterTests();
        private static int _passed;
        private static int _failed;

        public static int Main()
        {
            Run("save preserves advanced values and creates a restorable backup", SavePreservesAdvancedValuesAndBackup);
            Run("concurrent configuration edits are never silently overwritten", RejectsConcurrentEdits);
            Run("native gestures change only their documented flag bits", NativeSettingsPreserveUnknownFlags);
            Run("runtime diagnostics distinguish stale files from live input", RuntimeStatusRejectsStaleFiles);
            Run("native API writes are verified against the documented settings", NativeSaveRequiresVerifiedReadback);
            Run("preset identity compares values rather than the preset label", PresetsCompareActualValues);
            Run("unknown nested JSON fields survive an edited save", UnknownJsonFieldsSurvive);
            Console.WriteLine("RESULT control_center_passed=" + _passed + " failed=" + _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static void SavePreservesAdvancedValuesAndBackup()
        {
            InTempDirectory(delegate(string directory)
            {
                string path = Path.Combine(directory, "MagicForceClick.json");
                ForceClickOptions before = new ForceClickOptions();
                before.Save(path);
                string fingerprint = ConfigurationStore.Fingerprint(path);
                ForceClickOptions edited = ForceClickOptions.Load(path);
                edited.ApplyLightCrossScreenPreset();
                edited.SpeedSmoothingMs = 24.5;
                edited.MaxFrameIntervalMs = 80;
                edited.IdleResetMs = 420;
                edited.DragCancelDistance = 275;

                BackupInfo backup = new ConfigurationStore().Save(path, edited, fingerprint);
                ForceClickOptions saved = ForceClickOptions.Load(path);

                Equal(24.5, saved.SpeedSmoothingMs, "speed smoothing was not preserved");
                Equal(80.0, saved.MaxFrameIntervalMs, "frame interval was not preserved");
                Equal(420.0, saved.IdleResetMs, "idle reset was not preserved");
                Equal(275, saved.DragCancelDistance, "drag distance was not preserved");
                Equal(90, saved.ForceClickThreshold, "edited value was not saved");
                Check(File.Exists(backup.FullName), "backup file was not created");
                Equal(180, ForceClickOptions.Load(backup.FullName).ForceClickThreshold, "backup does not contain previous values");
            });
        }

        private static void RejectsConcurrentEdits()
        {
            InTempDirectory(delegate(string directory)
            {
                string path = Path.Combine(directory, "MagicForceClick.json");
                new ForceClickOptions().Save(path);
                string fingerprint = ConfigurationStore.Fingerprint(path);
                ForceClickOptions edited = ForceClickOptions.Load(path);
                edited.ForceClickThreshold = 175;
                new ForceClickOptions { MaxGain = 2.75 }.Save(path);

                bool rejected = false;
                try { new ConfigurationStore().Save(path, edited, fingerprint); }
                catch (ConfigurationChangedException) { rejected = true; }
                Check(rejected, "concurrent edit was not rejected");
                Equal(2.75, ForceClickOptions.Load(path).MaxGain, "another editor's change was overwritten");
            });
        }

        private static void UnknownJsonFieldsSurvive()
        {
            InTempDirectory(delegate(string directory) {
                string path = Path.Combine(directory, "MagicForceClick.json");
                new ForceClickOptions().Save(path);
                string text = File.ReadAllText(path);
                text = text.Substring(0, text.Length - 1) + ",\"FutureSettings\":{\"Label\":\"保留\",\"Values\":[1,true,3.5]}}";
                File.WriteAllText(path, text);
                string fingerprint = ConfigurationStore.Fingerprint(path);
                var edited = ForceClickOptions.Load(path); edited.MaxGain = 3.25;
                new ConfigurationStore().Save(path, edited, fingerprint);
                var json = new System.Web.Script.Serialization.JavaScriptSerializer();
                var saved = json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(path));
                var future = (System.Collections.Generic.Dictionary<string, object>)saved["FutureSettings"];
                Equal("保留", (string)future["Label"], "unknown nested object lost");
                Equal(3.25, ForceClickOptions.Load(path).MaxGain, "known edit lost");
            });
        }

        private static void NativeSettingsPreserveUnknownFlags()
        {
            TouchpadParameters before = TouchpadParameters.CreateForTest();
            before.userFlags = 0x80000405;
            before.sensitivityLevel = 2;
            before.cursorSpeed = 11;
            NativeTouchpadSnapshot edited = new NativeTouchpadSnapshot(before)
                .WithSetting("cursorSpeed", 17)
                .WithSetting("tapAndDrag", true)
                .WithSetting("pan", false);
            TouchpadParameters applied = edited.Parameters;

            Equal(17u, applied.cursorSpeed, "cursor speed not applied");
            Check((applied.userFlags & 0x08) != 0, "tap drag was not enabled");
            Check((applied.userFlags & 0x80) == 0, "pan was not disabled");
            Check((applied.userFlags & 0x80000000u) != 0, "unknown high flag bit was cleared");
            Check((applied.userFlags & 0x04) != 0, "single finger tap was cleared");
            Equal(2u, applied.sensitivityLevel, "unexposed sensitivity was overwritten");
        }

        private static void RuntimeStatusRejectsStaleFiles()
        {
            InTempDirectory(delegate(string directory)
            {
                string path = Path.Combine(directory, "MagicForceClick.status.json");
                File.WriteAllText(path, "{\"Version\":\"1.1.1\",\"RawInputRegistered\":true,\"ValidScanFrames\":42}");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
                RuntimeStatusSnapshot snapshot = RuntimeStatusSnapshot.Read(path, TimeSpan.FromSeconds(15));
                Check(!snapshot.IsLive, "stale runtime status was reported as live");
                Equal(42L, snapshot.ValidScanFrames, "diagnostic counter was not read");
            });
        }

        private TouchpadParameters _simulatedNativeParameters;

        private static void NativeSaveRequiresVerifiedReadback()
        {
            var harness = NativeHarness;
            TouchpadParameters before = TouchpadParameters.CreateForTest();
            before.cursorSpeed = 14;
            before.userFlags = 439;
            NativeTouchpadSnapshot expected = new NativeTouchpadSnapshot(before).WithSetting("cursorSpeed", 17);
            bool rejected = false;
            try { NativeTouchpadApi.Save(expected, delegate(TouchpadParameters value) { }, delegate(out TouchpadParameters value) { value = before; return true; }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "a no-op native API write was accepted");
            NativeTouchpadApi.Save(expected, delegate(TouchpadParameters value) { }, delegate(out TouchpadParameters value) { value = expected.Parameters; return true; });
            rejected = false;
            try { NativeTouchpadApi.Save(expected, delegate(TouchpadParameters value) { }, delegate(out TouchpadParameters value) { value = before; value.cursorSpeed = 19; return true; }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "clamped native API readback was accepted");
            harness._simulatedNativeParameters = before;
            NativeTouchpadApi.Save(expected, harness.SimulatedWrite, harness.SimulatedRead);
            Equal(17u, harness._simulatedNativeParameters.cursorSpeed, "verified native write did not reach the API");
        }

        private void SimulatedWrite(TouchpadParameters value) { _simulatedNativeParameters = value; }
        private bool SimulatedRead(out TouchpadParameters value) { value = _simulatedNativeParameters; return true; }

        private static void PresetsCompareActualValues()
        {
            ForceClickOptions light = new ForceClickOptions();
            light.ApplyLightCrossScreenPreset();
            Equal("LightCrossScreen", ProfileCatalog.Identify(light), "light preset not identified");
            light.MaxGain = 4.0;
            Equal("Custom", ProfileCatalog.Identify(light), "customized preset was mislabeled");
        }

        private static void Run(string name, Action test)
        {
            try { test(); _passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { _failed++; Console.Error.WriteLine("FAIL " + name + "\n" + ex); }
        }

        private static void InTempDirectory(Action<string> test)
        {
            string path = Path.Combine(Path.GetTempPath(), "MagicControlCenter-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try { test(path); }
            finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            if (!object.Equals(expected, actual)) throw new Exception(message + " Expected=" + expected + " Actual=" + actual);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
