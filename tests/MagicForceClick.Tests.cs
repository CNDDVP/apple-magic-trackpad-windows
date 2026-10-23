using System;
using System.IO;
using System.Text;

namespace MagicForceClick.Tests
{
    internal static class TestProgram
    {
        private static int _passed;
        private static ForceClickOptions Options() { return new ForceClickOptions(); }
        private static TrackpadSample Sample(int scan, int x, int pressure, bool button)
        { return new TrackpadSample { ScanTime = (ushort)scan, X = x, Y = 1000, Pressure = pressure, Button = button, Tip = true, ContactCount = 1, ContactId = 7 }; }
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Near(double a, double b) { Assert(Math.Abs(a - b) < 0.0000001, a + " != " + b); }
        private static void Run(string name, Action action) { action(); _passed++; Console.WriteLine("PASS " + name); }
        private static bool Throws(Action action) { try { action(); return false; } catch { return true; } }
        public static int Main()
        {
            try
            {
                Run("100us scan units", delegate { var m = new ScanTimeMotion(); var o = Options(); m.Update(Sample(100, 0, 0, false), 0, o); Near(m.Update(Sample(180, 80, 0, false), 8, o).DeltaMs, 8); });
                Run("16bit counter rollover", delegate { var m = new ScanTimeMotion(); var o = Options(); m.Update(Sample(65500, 0, 0, false), 0, o); var r = m.Update(Sample(44, 80, 0, false), 8, o); Assert(r.Valid, "rollover rejected"); Near(r.DeltaMs, 8); });
                Run("receipt jitter does not change speed", delegate {
                    var normal = new ScanTimeMotion(); var jitter = new ScanTimeMotion(); var o = Options();
                    for (int i = 0; i < 1000; i++) { var s = Sample((i * 80) % 65536, i * 3, 0, false); var a = normal.Update(s, i * 8, o); var b = jitter.Update(s, (i / 10) * 80, o); Near(a.Speed, b.Speed); Assert(a.Valid == b.Valid, "batch changed validity"); }
                });
                Run("duplicate frames preserve coordinates", delegate { var m = new ScanTimeMotion(); var o = Options(); m.Update(Sample(100, 0, 0, false), 0, o); Assert(!m.Update(Sample(100, 900, 0, false), 1, o).Valid, "duplicate accepted"); Near(m.Update(Sample(180, 80, 0, false), 8, o).PadDistance, 80); });
                Run("out of order scan resets", delegate { var m = new ScanTimeMotion(); var o = Options(); m.Update(Sample(1000, 0, 0, false), 0, o); Assert(m.Update(Sample(900, 80, 0, false), 8, o).ContinuityLost, "old scan not rejected"); });
                Run("counter restart resets", delegate { var m = new ScanTimeMotion(); var o = Options(); m.Update(Sample(3000, 0, 0, false), 0, o); Assert(!m.Update(Sample(0, 80, 0, false), 8, o).Valid, "reset treated as movement"); });
                Run("sleep gap rebases", delegate { var m = new ScanTimeMotion(); var o = Options(); m.Update(Sample(100, 0, 0, false), 0, o); Assert(!m.Update(Sample(180, 5000, 0, false), 5000, o).Valid, "wake jumped"); Assert(m.Update(Sample(260, 5010, 0, false), 5008, o).Valid, "did not recover"); });
                Run("large scan interval rebases", delegate { var m = new ScanTimeMotion(); var o = Options(); m.Update(Sample(100, 0, 0, false), 0, o); Assert(!m.Update(Sample(1100, 100, 0, false), 100, o).Valid, "large dt accepted"); });
                Run("contact ID change rebases", delegate { var m = new ScanTimeMotion(); var o = Options(); m.Update(Sample(100, 0, 0, false), 0, o); var s = Sample(180, 900, 0, false); s.ContactId++; Assert(!m.Update(s, 8, o).Valid, "new contact jumped"); });
                Run("two fingers stop custom motion", delegate { var m = new ScanTimeMotion(); var o = Options(); m.Update(Sample(100, 0, 0, false), 0, o); var s = Sample(180, 90, 0, false); s.ContactCount = 2; Assert(!m.Update(s, 8, o).Valid, "two fingers accelerated"); Assert(!m.Update(Sample(260, 0, 0, false), 16, o).Valid, "single return not rebased"); });
                Run("lift stops custom motion", delegate { var m = new ScanTimeMotion(); var s = Sample(10, 0, 0, false); s.Tip = false; Assert(!m.Update(s, 0, Options()).Valid, "lift accepted"); });
                Run("zero displacement not manufactured", delegate { var m = new ScanTimeMotion(); var o = Options(); m.Update(Sample(100, 0, 0, false), 0, o); Near(m.Update(Sample(180, 0, 0, false), 8, o).PadDistance, 0); });
                Run("legacy EMA matches at 8ms", delegate { var m = new ScanTimeMotion(); var o = Options(); m.Update(Sample(100, 0, 0, false), 0, o); Near(m.Update(Sample(180, 80, 0, false), 8, o).Speed, 5.5); });
                Run("ordinary press no preview", delegate { var p = new PressGesture(); Assert(!p.Update(Sample(1, 1000, 80, true), 10, 10, 0, Options()), "ordinary press fired"); });
                Run("stationary deep press fires", delegate { var p = new PressGesture(); var o = Options(); p.Update(Sample(1, 1000, 80, true), 10, 10, 0, o); Assert(p.Update(Sample(2, 1000, 180, true), 10, 10, 8, o), "deep press did not fire"); });
                Run("one preview per held press", delegate { var p = new PressGesture(); var o = Options(); Assert(p.Update(Sample(1, 1000, 200, true), 10, 10, 0, o), "first not fired"); Assert(!p.Update(Sample(2, 1000, 230, true), 10, 10, 1000, o), "repeated"); });
                Run("drag before pressure 110 blocks preview", delegate { var p = new PressGesture(); var o = Options(); p.Update(Sample(1, 1000, 70, true), 10, 10, 0, o); p.Update(Sample(2, 1500, 90, true), 10, 10, 8, o); Assert(!p.Update(Sample(3, 1500, 200, true), 10, 10, 16, o), "drag fired"); Assert(p.Blocked, "drag not latched"); });
                Run("return to drag origin stays blocked", delegate { var p = new PressGesture(); var o = Options(); p.Update(Sample(1, 1000, 80, true), 0, 0, 0, o); p.Update(Sample(2, 1500, 80, true), 0, 0, 8, o); Assert(!p.Update(Sample(3, 1000, 220, true), 0, 0, 16, o), "return rearmed"); });
                Run("pressure dip does not reanchor", delegate { var p = new PressGesture(); var o = Options(); p.Update(Sample(1, 1000, 120, true), 0, 0, 0, o); p.Update(Sample(2, 1400, 60, true), 0, 0, 8, o); Assert(!p.Update(Sample(3, 1400, 230, true), 0, 0, 16, o), "dip rearmed"); });
                Run("cursor drag blocks despite little HID movement", delegate { var p = new PressGesture(); var o = Options(); p.Update(Sample(1, 1000, 80, true), 0, 0, 0, o); Assert(!p.Update(Sample(2, 1001, 220, true), 20, 0, 8, o), "cursor drag fired"); });
                Run("multifinger press stays blocked until release", delegate { var p = new PressGesture(); var o = Options(); var s = Sample(1, 1000, 200, true); s.ContactCount = 2; p.Update(s, 0, 0, 0, o); Assert(!p.Update(Sample(2, 1000, 200, true), 0, 0, 8, o), "multi rearmed"); });
                Run("contact replacement blocks preview", delegate { var p = new PressGesture(); var o = Options(); p.Update(Sample(1, 1000, 80, true), 0, 0, 0, o); var s = Sample(2, 1000, 200, true); s.ContactId++; Assert(!p.Update(s, 0, 0, 8, o), "replacement fired"); });
                Run("full release rearms next press", delegate { var p = new PressGesture(); var o = Options(); p.Update(Sample(1, 1000, 80, true), 0, 0, 0, o); p.Update(Sample(2, 2000, 80, true), 0, 0, 8, o); p.Update(Sample(3, 2000, 0, false), 0, 0, 16, o); Assert(p.Update(Sample(4, 2000, 200, true), 0, 0, 24, o), "release not rearmed"); });
                Run("preview toggle off", delegate { var p = new PressGesture(); var o = Options(); o.EnableForceClick = false; Assert(!p.Update(Sample(1, 1000, 250, true), 0, 0, 0, o), "disabled preview fired"); });
                Run("enable mid press waits for release", delegate { var p = new PressGesture(); var o = Options(); o.EnableForceClick = false; p.Update(Sample(1, 1000, 80, true), 0, 0, 0, o); o.EnableForceClick = true; Assert(!p.Update(Sample(2, 1000, 250, true), 0, 0, 8, o), "toggle rearmed held button"); });
                Run("configuration reload cancels held press", delegate { var p = new PressGesture(); var o = Options(); p.Update(Sample(1, 1000, 80, true), 0, 0, 0, o); p.CancelHeld(); Assert(!p.Update(Sample(2, 1000, 250, true), 0, 0, 8, o), "reload fired"); });
                Run("preview cooldown", delegate { var p = new PressGesture(); var o = Options(); p.Update(Sample(1, 0, 200, true), 0, 0, 0, o); p.Update(Sample(2, 0, 0, false), 0, 0, 10, o); Assert(!p.Update(Sample(3, 0, 200, true), 0, 0, 20, o), "cooldown missed"); Assert(p.Update(Sample(4, 0, 200, true), 0, 0, 500, o), "cooldown stuck"); });
                Run("independent switches all off gives unity", delegate { var o = Options(); o.EnableAcceleration = o.EnableMonitorCompensation = false; Near(PointerGain.Combined(20, 0.673, false, o), 1); });
                Run("monitor only independent", delegate { var o = Options(); o.EnableAcceleration = false; Near(PointerGain.Combined(20, 0.673, false, o), 0.673); });
                Run("acceleration only independent", delegate { var o = Options(); o.EnableMonitorCompensation = false; Near(PointerGain.Combined(4.5, 0.673, false, o), 2.175); });
                Run("pressed acceleration toggle", delegate { var o = Options(); o.AccelerateWhilePressed = false; Near(PointerGain.Combined(20, 0.897, true, o), 0.897); });
                Run("curve monotonic and bounded", delegate { var o = Options(); double previous = 0; for(int i=0;i<10000;i++) { double g=PointerGain.Curve(i*0.01,o); Assert(g>=previous && g>=o.MinGain && g<=o.MaxGain,"curve invalid");previous=g;} });
                Run("decoder exact report and scan offset", delegate { var r = new byte[112]; r[0]=5;r[1]=2;r[106]=0x34;r[107]=0x12;r[108]=1;r[111]=1;r[109]=200;TrackpadSample s;Assert(TrackpadSample.TryDecode(r,out s) && s.ScanTime==0x1234 && s.Button && s.Pressure==200,"decode incorrect"); });
                Run("malformed report rejected", delegate { TrackpadSample s; Assert(!TrackpadSample.TryDecode(new byte[111],out s),"short accepted");var r=new byte[112];r[0]=1;Assert(!TrackpadSample.TryDecode(r,out s),"wrong ID accepted"); });
                Run("invalid gain rejected", delegate {var o=Options();o.MaxGain=0.2;Assert(Throws(o.Validate),"range accepted");});
                Run("nonfinite settings rejected", delegate {var o=Options();o.MidSpeed=double.NaN;Assert(Throws(o.Validate),"NaN accepted");});
                Run("configuration missing members retain defaults", delegate { WithFile("{\"EnableAcceleration\":false}", delegate(string path) {var o=ForceClickOptions.Load(path);Assert(!o.EnableAcceleration && o.EnableForceClick && o.ForceClickThreshold==180,"defaults lost");}); });
                Run("invalid JSON rejected", delegate {WithFile("{not-json",delegate(string path){Assert(Throws(delegate{ForceClickOptions.Load(path);}),"bad JSON accepted");});});
                Run("save is roundtrippable and replaceable", delegate {WithFile("{}",delegate(string path){var o=Options();o.EnableMonitorCompensation=false;o.Save(path);var b=ForceClickOptions.Load(path);Assert(!b.EnableMonitorCompensation && b.EnableAcceleration,"roundtrip");o.MaxGain=2.5;o.Save(path);Near(ForceClickOptions.Load(path).MaxGain,2.5);});});
                Console.WriteLine("RESULT passed=" + _passed + " failed=0"); return 0;
            }
            catch(Exception ex) { Console.Error.WriteLine("FAIL after " + _passed + ": " + ex); return 1; }
        }
        private static void WithFile(string content, Action<string> action)
        {
            string path=Path.Combine(Path.GetTempPath(),"MagicForceClick-test-"+Guid.NewGuid().ToString("N")+".json");
            try {File.WriteAllText(path,content,Encoding.UTF8);action(path);} finally {File.Delete(path);}
        }
    }
}
