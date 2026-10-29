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
            Run("pointer presets preserve every non-curve option", PointerPresetsPreserveOtherSettings);
            Run("configuration options and fingerprint come from one byte snapshot", ConfigurationSnapshotIsConsistent);
            Run("native equality detects reverted edits and preserves unknown bits", NativeEquality);
            Run("native-only save never rewrites JSON or creates a JSON backup", NativeOnlySave);
            Run("native write failure restores API and registry state", delegate { NativeFailure(1); });
            Run("native readback failure restores API and registry state", delegate { NativeFailure(2); });
            Run("registry sync failure restores API and registry state", delegate { NativeFailure(3); });
            Run("JSON pre-commit failure restores native changes", JsonBeforeCommitFailure);
            Run("JSON post-commit failure reports committed state and keeps native values", JsonAfterCommitFailure);
            Run("external edits after commit are preserved and require reload", ExternalEditAfterCommit);
            Run("rollback failure is surfaced rather than reported as restored", RecoveryFailure);
            Run("native external edits block writes", NativeExternalEdit);
            Run("runtime health binds process, timestamp, input and options", RuntimeHealthChecks);
            Run("fresh file metadata cannot make old runtime content live", OldRuntimeContent);
            Run("validation points to fields with Chinese messages", ChineseValidation);
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

        private static void PointerPresetsPreserveOtherSettings()
        {
            var options = new ForceClickOptions(); options.ApplyLightCrossScreenPreset();
            options.EnableAcceleration = false; options.EnableForceClick = false; options.EnableMonitorCompensation = false; options.AccelerateWhilePressed = false;
            options.DragCancelDistance = 375; options.DragCancelCursorPixels = 9; options.SpeedSmoothingMs = 24.5;
            var original = new System.Collections.Generic.Dictionary<string, object>();
            foreach (var field in typeof(ForceClickOptions).GetFields()) original[field.Name] = field.GetValue(options);
            foreach (bool pressedAcceleration in new[] { false, true })
            {
                options.AccelerateWhilePressed = pressedAcceleration;
                original["AccelerateWhilePressed"] = pressedAcceleration;
                foreach (var preset in new[] { PointerPreset.Classic, PointerPreset.LightCrossScreen })
                {
                    ProfileCatalog.ApplyPointer(options, preset);
                    Equal(preset, ProfileCatalog.IdentifyPointer(options), "selected curve was not identified");
                    foreach (var field in typeof(ForceClickOptions).GetFields())
                        if (field.Name != "MinGain" && field.Name != "MaxGain" && field.Name != "MidSpeed" && field.Name != "CurvePower")
                            Equal(original[field.Name], field.GetValue(options), "preset changed " + field.Name);
                }
            }
            options.EnableForceClick = true;
            Equal("仅深按预览", ProfileCatalog.ModeName(options), "mode is conflated with curve");
        }

        private static void ConfigurationSnapshotIsConsistent()
        {
            InTempDirectory(delegate(string dir) {
                string path = Path.Combine(dir, "MagicForceClick.json"); new ForceClickOptions().Save(path);
                byte[] before = File.ReadAllBytes(path);
                var snapshot = ConfigurationSnapshot.Read(path);
                new ForceClickOptions { MaxGain = 2.5 }.Save(path);
                Equal(ConfigurationSnapshot.Hash(before), snapshot.Fingerprint, "fingerprint is not from snapshot bytes");
                Equal(3.4, snapshot.Options.MaxGain, "snapshot changed after external edit");
                bool rejected = false;
                try { new ConfigurationStore().Save(path, snapshot.Options, snapshot.Fingerprint); } catch (ConfigurationChangedException) { rejected = true; }
                Check(rejected, "stale byte snapshot was accepted");
            });
        }

        private static void NativeEquality()
        {
            var memory = new MemoryTouchpadPlatform(); var original = memory.Read();
            var edited = new NativeTouchpadSnapshot(original.Parameters);
            edited.WithSetting("tap", false); Check(!edited.SameEditableValues(original), "tap edit not detected");
            edited.WithSetting("tap", true); Check(edited.SameEditableValues(original), "reverted tap remains dirty");
            edited.WithSetting("cursorSpeed", 11); Check(!edited.SameEditableValues(original), "speed edit not detected");
            edited.WithSetting("cursorSpeed", 10); Check(edited.SameEditableValues(original), "reverted speed remains dirty");
            var unknown = original.Parameters; unknown.userFlags ^= 0x40000000;
            Check(new NativeTouchpadSnapshot(unknown).SameEditableValues(original), "unknown flags create a false edit");
        }

        private sealed class FaultNative : INativeTouchpadPlatform
        {
            internal TouchpadParameters Value;
            internal int RegistryValue = 14, Failure, ApplyCalls;
            internal bool Written, FailRestore;
            private int _registryBefore;
            internal FaultNative() { Value = TouchpadParameters.CreateForTest(); Value.userFlags = 439; Value.cursorSpeed = 10; }
            public NativeTouchpadSnapshot Read() { if (Written && Failure == 2) throw new IOException("simulated readback failure"); return new NativeTouchpadSnapshot(Value); }
            public NativeTouchpadBackup Capture() { _registryBefore = RegistryValue; return new NativeTouchpadBackup { Snapshot = Read() }; }
            public void Apply(NativeTouchpadSnapshot wanted)
            {
                ApplyCalls++; Value = wanted.Parameters; Written = true;
                if (Failure == 1) throw new IOException("simulated API write failure after mutation");
                RegistryValue = 99;
                if (Failure == 3) throw new IOException("simulated registry sync failure");
            }
            public void Restore(NativeTouchpadBackup backup)
            {
                if (FailRestore) throw new IOException("simulated rollback failure");
                Value = backup.Snapshot.Parameters; RegistryValue = _registryBefore; Written = false;
            }
        }

        private sealed class SaveFixture
        {
            internal string Path, Fingerprint;
            internal ForceClickOptions Saved, Wanted;
            internal FaultNative Native = new FaultNative();
            internal NativeTouchpadSnapshot NativeSaved, NativeWanted;
            internal SaveFixture(string dir)
            {
                Path = System.IO.Path.Combine(dir, "MagicForceClick.json");
                Saved = new ForceClickOptions(); Saved.ApplyLightCrossScreenPreset(); Saved.Save(Path);
                Wanted = ForceClickOptions.Load(Path); Fingerprint = ConfigurationStore.Fingerprint(Path);
                NativeSaved = Native.Read(); NativeWanted = new NativeTouchpadSnapshot(NativeSaved.Parameters).WithSetting("cursorSpeed", 11);
            }
            internal SettingsSaveResult Save(ConfigurationStore store) { return SettingsSaver.Save(Path, Wanted, Saved, Fingerprint, NativeWanted, NativeSaved, Native, store); }
        }

        private static void NativeOnlySave()
        {
            InTempDirectory(delegate(string dir) {
                var f = new SaveFixture(dir); DateTime modified = File.GetLastWriteTimeUtc(f.Path);
                var result = f.Save(new ConfigurationStore());
                Check(result.Success && result.NativeAttempted && !result.JsonCommitted, "native-only save was not isolated");
                Equal(f.Fingerprint, ConfigurationStore.Fingerprint(f.Path), "native-only save rewrote JSON");
                Equal(modified, File.GetLastWriteTimeUtc(f.Path), "native-only save touched JSON timestamp");
                Check(!Directory.Exists(System.IO.Path.Combine(dir, "control-center-backups")), "native-only save created a JSON backup");
            });
        }

        private static void NativeFailure(int phase)
        {
            InTempDirectory(delegate(string dir) {
                var f = new SaveFixture(dir); f.Native.Failure = phase;
                var result = f.Save(new ConfigurationStore());
                Check(!result.Success && result.NativeRestored && !result.RequiresReload, "native failure was not verified as restored");
                Check(f.Native.Read().SameEditableValues(f.NativeSaved), "API state was not restored");
                Equal(14, f.Native.RegistryValue, "registry state was not restored");
                Equal(f.Fingerprint, ConfigurationStore.Fingerprint(f.Path), "native failure changed JSON");
            });
        }

        private static void JsonBeforeCommitFailure()
        {
            InTempDirectory(delegate(string dir) {
                var f = new SaveFixture(dir); f.Wanted.MaxGain = 3.25;
                var result = f.Save(new ConfigurationStore(delegate(ConfigurationSaveStage stage) { if (stage == ConfigurationSaveStage.BeforeCommit) throw new IOException("pre-commit"); }));
                Check(!result.Success && !result.JsonCommitted && result.NativeRestored, "pre-commit failure did not restore native state");
                Equal(f.Fingerprint, ConfigurationStore.Fingerprint(f.Path), "pre-commit failure changed JSON");
                Equal(14, f.Native.RegistryValue, "pre-commit rollback missed registry");
                Equal(0, Directory.GetFiles(dir, "*.tmp").Length, "temporary JSON leaked");
            });
        }

        private static void JsonAfterCommitFailure()
        {
            InTempDirectory(delegate(string dir) {
                var f = new SaveFixture(dir); f.Wanted.MaxGain = 3.25;
                var result = f.Save(new ConfigurationStore(delegate(ConfigurationSaveStage stage) { if (stage == ConfigurationSaveStage.AfterCommit) throw new IOException("post-commit"); }));
                Check(!result.Success && result.JsonCommitted && result.RequiresReload && !result.NativeRestored, "commit result was misreported");
                Equal(3.25, ForceClickOptions.Load(f.Path).MaxGain, "committed JSON was lost");
                Check(f.Native.Read().SameEditableValues(f.NativeWanted), "native state was rolled back after JSON commit");
                Check(result.FailureMessage.Contains("已写入"), "partial success claims original file was unchanged");
            });
        }

        private static void ExternalEditAfterCommit()
        {
            InTempDirectory(delegate(string dir) {
                var f = new SaveFixture(dir); f.Wanted.MaxGain = 3.25;
                var result = f.Save(new ConfigurationStore(delegate(ConfigurationSaveStage stage) {
                    if (stage == ConfigurationSaveStage.AfterCommit) { var other = ForceClickOptions.Load(f.Path); other.MaxGain = 4.25; other.Save(f.Path); }
                }));
                Check(result.JsonCommitted && result.RequiresReload, "post-commit external edit was hidden");
                Equal(4.25, ForceClickOptions.Load(f.Path).MaxGain, "post-commit external edit was overwritten");
            });
        }

        private static void RecoveryFailure()
        {
            InTempDirectory(delegate(string dir) {
                var f = new SaveFixture(dir); f.Native.Failure = 1; f.Native.FailRestore = true;
                var result = f.Save(new ConfigurationStore());
                Check(!result.Success && result.RequiresReload && result.RecoveryError != null && !result.NativeRestored, "rollback failure was reported as success");
                Check(result.FailureMessage.Contains("恢复未获确认"), "rollback failure is not visible");
            });
        }

        private static void NativeExternalEdit()
        {
            InTempDirectory(delegate(string dir) {
                var f = new SaveFixture(dir); f.Native.Value.cursorSpeed = 17;
                var result = f.Save(new ConfigurationStore());
                Check(!result.Success && result.Error is ConfigurationChangedException, "external native edit was accepted");
                Equal(0, f.Native.ApplyCalls, "external native state was overwritten");
                Equal(17u, f.Native.Value.cursorSpeed, "external native value lost");
            });
        }

        private static RuntimeStatusSnapshot WriteStatus(string path, ForceClickOptions options, DateTime time, bool registered)
        {
            using (var stream = File.Create(path)) new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(RuntimeStatusSnapshot)).WriteObject(stream,
                new RuntimeStatusSnapshot { TimeUtc = time, RawInputRegistered = registered, Options = options, Version = "test" });
            return RuntimeStatusSnapshot.Read(path, TimeSpan.FromSeconds(15));
        }

        private static void RuntimeHealthChecks()
        {
            InTempDirectory(delegate(string dir) {
                var saved = new ForceClickOptions(); var path = System.IO.Path.Combine(dir, "status.json");
                DateTime now = DateTime.UtcNow;
                var observation = new RuntimeObservation { ProcessRunning = true, ProcessStartUtc = now.AddMinutes(-1), Status = WriteStatus(path, saved, now.AddMilliseconds(-50), true) };
                Check(RuntimeAssessment.Evaluate(observation, saved, DateTime.MinValue, DateTime.UtcNow).Applied, "healthy matching runtime was rejected");
                observation.ProcessRunning = false;
                Check(!RuntimeAssessment.Evaluate(observation, saved, DateTime.MinValue, DateTime.UtcNow).Applied, "missing process was confirmed");
                observation.ProcessRunning = true; observation.ProcessStartUtc = now;
                Check(!RuntimeAssessment.Evaluate(observation, saved, DateTime.MinValue, DateTime.UtcNow).Healthy, "previous-process state was accepted");
                observation.ProcessStartUtc = now.AddMinutes(-1); observation.Status = WriteStatus(path, saved, now, false);
                Check(!RuntimeAssessment.Evaluate(observation, saved, DateTime.MinValue, DateTime.UtcNow).Healthy, "unregistered input was accepted");
                observation.Status = WriteStatus(path, new ForceClickOptions { MaxGain = 4.25 }, now, true);
                Check(!RuntimeAssessment.Evaluate(observation, saved, DateTime.MinValue, DateTime.UtcNow).Applied, "different backend options were confirmed");
                observation.Status = WriteStatus(path, saved, now, true);
                Check(!RuntimeAssessment.Evaluate(observation, saved, now.AddSeconds(1), DateTime.UtcNow).Applied, "pre-save status confirmed a save");
            });
        }

        private static void OldRuntimeContent()
        {
            InTempDirectory(delegate(string dir) {
                string path = System.IO.Path.Combine(dir, "status.json");
                var status = WriteStatus(path, new ForceClickOptions(), DateTime.UtcNow.AddMinutes(-5), true);
                Check(!status.IsLive, "fresh filesystem metadata made old JSON live");
            });
        }

        private static void ChineseValidation()
        {
            var options = new ForceClickOptions { MinGain = 4, MaxGain = 3 };
            var issue = DraftValidationIssue.Check(options);
            Equal("MaxGain", issue.Field, "wrong field identified"); Check(issue.Message.Contains("快速最大增益"), "field error was not localized");
            options.ApplyLightCrossScreenPreset(); options.ForceClickDwellMs = 0;
            issue = DraftValidationIssue.Check(options);
            Equal("ForceClickThreshold", issue.Field, "unsafe low-pressure combination not located");
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
