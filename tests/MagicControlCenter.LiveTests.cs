using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MagicForceClick;

namespace MagicControlCenter.Tests
{
    // Explicit opt-in integration check. It calls the real control-center save
    // handler, confirms fresh backend options, and restores the original bytes.
    internal static class LiveTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static string _output;

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length != 4 || args[0] != "--production-roundtrip") {
                Console.Error.WriteLine("Usage: --production-roundtrip <suite-directory> <accepted-app-sha256> <output-directory>");
                return 2;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string directory = Path.GetFullPath(args[1]);
            string app = Path.Combine(directory, "MagicControlCenter", "MagicControlCenter.exe");
            if (!Hash(app).Equals(args[2], StringComparison.OrdinalIgnoreCase)) throw new Exception("Deployed app differs from the accepted build.");
            _output = Path.GetFullPath(args[3]); Directory.CreateDirectory(_output);
            string config = Path.Combine(directory, "MagicForceClick.json");
            string statusPath = Path.Combine(directory, "MagicForceClick.status.json");
            byte[] originalBytes = File.ReadAllBytes(config);
            string originalHash = Hash(config), ownedHash = originalHash;
            File.WriteAllBytes(Path.Combine(_output, "OriginalConfiguration.json"), originalBytes);
            ForceClickOptions original = ForceClickOptions.Load(config);
            int backend = FindBackend(directory);
            if (backend == 0) throw new Exception("The existing backend is not running in this directory.");
            DateTime restoredAt = DateTime.MinValue;
            int passed = 0;
            Exception failure = null;
            try {
                using (var form = new ControlCenterForm(directory, false)) {
                    IntPtr handle = form.Handle;
                    if (!string.Equals(form.ConfigPath, config, StringComparison.OrdinalIgnoreCase)) throw new Exception("Control center resolved the wrong production configuration.");
                    passed++; Log("PASS production configuration and existing backend located");
                    form.Navigate("press");
                    var dwell = (NumericUpDown)typeof(ControlCenterForm).GetField("_dwell", Private).GetValue(form);
                    decimal temporary = (decimal)original.ForceClickDwellMs == 500 ? 499 : (decimal)original.ForceClickDwellMs + 1;
                    dwell.Value = temporary;
                    if (!form.CanSave) throw new Exception("Editing the live numeric control did not enable Save.");
                    DateTime changedAt = DateTime.UtcNow;
                    Invoke(form, "SaveChanges"); ownedHash = Hash(config);
                    ForceClickOptions changed = ForceClickOptions.Load(config);
                    if (changed.ForceClickDwellMs != (double)temporary) throw new Exception("Live UI save did not write the edited value.");
                    AwaitRuntime(form, statusPath, changed, changedAt);
                    passed++; Log("PASS real UI save and fresh backend options confirmed; dwell=" + temporary);
                    dwell = (NumericUpDown)typeof(ControlCenterForm).GetField("_dwell", Private).GetValue(form);
                    dwell.Value = (decimal)original.ForceClickDwellMs;
                    restoredAt = DateTime.UtcNow;
                    Invoke(form, "SaveChanges"); ownedHash = Hash(config);
                    if (Json(ForceClickOptions.Load(config)) != Json(original)) throw new Exception("UI restore changed another known option.");
                    AwaitRuntime(form, statusPath, original, restoredAt);
                    passed++; Log("PASS original option values restored through the UI and freshly confirmed");
                }
            } catch (Exception ex) { failure = ex; Log("FAIL " + ex); }
            finally {
                if (Hash(config) == ownedHash) {
                    string temp = config + ".live-restore-" + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllBytes(temp, originalBytes);
                    File.Replace(temp, config, null);
                    restoredAt = DateTime.UtcNow;
                    Log("Original configuration bytes restored.");
                } else {
                    failure = new Exception("An external configuration edit was detected. It was preserved; restore the saved original only after reviewing that edit.", failure);
                    Log("RECOVERY_CONFLICT external edit preserved");
                }
            }
            try {
                if (failure != null) throw failure;
                AwaitRuntime(null, statusPath, original, restoredAt);
                if (Hash(config) != originalHash) throw new Exception("Final configuration bytes differ from the original.");
                if (FindBackend(directory) != backend) throw new Exception("The existing backend was restarted or replaced.");
                passed++; Log("PASS byte-identical original JSON, fresh original options, and unchanged backend PID");
                File.WriteAllText(Path.Combine(_output, "LiveResults.json"), "{\"Passed\":true,\"Checks\":" + passed + ",\"ConfigurationSha256\":\"" + originalHash + "\",\"AppSha256\":\"" + Hash(app) + "\",\"BackendPid\":" + backend + "}");
                return 0;
            } catch (Exception ex) {
                Log("FAIL final verification " + ex);
                File.WriteAllText(Path.Combine(_output, "LiveResults.json"), "{\"Passed\":false,\"Checks\":" + passed + "}");
                return 1;
            }
        }

        private static void AwaitRuntime(ControlCenterForm form, string path, ForceClickOptions expected, DateTime after)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(22);
            while (DateTime.UtcNow < deadline) {
                Application.DoEvents();
                try {
                    RuntimeStatusSnapshot status = RuntimeStatusSnapshot.Read(path, TimeSpan.FromSeconds(15));
                    if (status.IsLive && status.RawInputRegistered && status.LastWriteUtc >= after && status.Options != null && Json(status.Options) == Json(expected)) {
                        if (form != null) {
                            Invoke(form, "RefreshRuntime");
                            var state = (Label)typeof(ControlCenterForm).GetField("_stateLabel", Private).GetValue(form);
                            if (state.Text != "已保存，后台已生效") { Thread.Sleep(100); continue; }
                        }
                        return;
                    }
                } catch (IOException) { }
                Thread.Sleep(100);
            }
            throw new Exception("No fresh backend status confirmed the expected options.");
        }

        private static int FindBackend(string directory)
        {
            foreach (Process process in Process.GetProcessesByName("MagicForceClick")) using (process) {
                try { if (string.Equals(Path.GetDirectoryName(process.MainModule.FileName), directory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return process.Id; }
                catch { }
            }
            return 0;
        }
        private static void Invoke(ControlCenterForm form, string name) {
            try { typeof(ControlCenterForm).GetMethod(name, Private).Invoke(form, null); }
            catch (TargetInvocationException ex) { throw ex.InnerException; }
        }
        private static string Json(ForceClickOptions options) {
            using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(ForceClickOptions)).WriteObject(stream, options); return Encoding.UTF8.GetString(stream.ToArray()); }
        }
        private static string Hash(string path) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
        private static void Log(string message) { Console.WriteLine(message); File.AppendAllText(Path.Combine(_output, "LiveLog.txt"), DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine, new UTF8Encoding(false)); }
    }
}
