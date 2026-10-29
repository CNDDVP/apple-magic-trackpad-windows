using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MagicForceClick
{
    [DataContract]
    public sealed class ForceClickOptions
    {
        [DataMember] public int SchemaVersion;
        [DataMember] public bool EnableForceClick;
        [DataMember] public bool EnableAcceleration;
        [DataMember] public bool EnableMonitorCompensation;
        [DataMember] public bool AccelerateWhilePressed;
        [DataMember] public int ForceClickThreshold;
        [DataMember] public double ForceClickDwellMs;
        [DataMember] public int ForceClickPressureRise;
        [DataMember] public int ForceClickImmediateThreshold;
        [DataMember] public int DragCancelDistance;
        [DataMember] public int DragCancelCursorPixels;
        [DataMember] public double MinGain;
        [DataMember] public double MaxGain;
        [DataMember] public double MidSpeed;
        [DataMember] public double CurvePower;
        [DataMember] public double SpeedSmoothingMs;
        [DataMember] public double MaxFrameIntervalMs;
        [DataMember] public double IdleResetMs;

        public ForceClickOptions() { Defaults(); }
        [OnDeserializing] private void OnDeserializing(StreamingContext context) { Defaults(); }
        private void Defaults()
        {
            SchemaVersion = 1;
            EnableForceClick = EnableAcceleration = EnableMonitorCompensation = true;
            // Keep physical clicks steady; held-button acceleration remains an explicit opt-in.
            AccelerateWhilePressed = false;
            ForceClickThreshold = 180; ForceClickDwellMs = 0; ForceClickPressureRise = 0; ForceClickImmediateThreshold = 0; DragCancelDistance = 220; DragCancelCursorPixels = 4;
            MinGain = 0.95; MaxGain = 3.40; MidSpeed = 4.5; CurvePower = 1.85;
            // Equivalent to the previous 0.55 EMA at an 8 ms frame period.
            SpeedSmoothingMs = -8.0 / Math.Log(0.45);
            MaxFrameIntervalMs = 65; IdleResetMs = 250;
        }
        public void ApplyLightCrossScreenPreset()
        {
            // Optional calibrated profile. Preserve switches and drag protection.
            ForceClickThreshold = 90;
            ForceClickDwellMs = 180;
            ForceClickPressureRise = 15;
            ForceClickImmediateThreshold = 180;
            MinGain = 0.95; MaxGain = 3.5; MidSpeed = 3.8; CurvePower = 2.0;
        }
        public void Validate()
        {
            if (SchemaVersion != 1) throw new InvalidDataException("Unsupported configuration schema.");
            Range(ForceClickThreshold, 70, 250, "ForceClickThreshold");
            Range(ForceClickDwellMs, 0, 500, "ForceClickDwellMs");
            Range(ForceClickPressureRise, 0, 100, "ForceClickPressureRise");
            Range(ForceClickImmediateThreshold, 0, 250, "ForceClickImmediateThreshold");
            if (ForceClickImmediateThreshold != 0 && ForceClickImmediateThreshold < ForceClickThreshold)
                throw new InvalidDataException("Immediate pressure must be zero or at least the normal pressure threshold.");
            if (ForceClickThreshold < 120 && (ForceClickDwellMs < 120 || ForceClickPressureRise < 10))
                throw new InvalidDataException("Pressure below 120 requires at least 120 ms dwell and 10 pressure rise after button-down.");
            Range(DragCancelDistance, 10, 2000, "DragCancelDistance");
            Range(DragCancelCursorPixels, 1, 64, "DragCancelCursorPixels");
            Range(MinGain, 0.1, 8, "MinGain"); Range(MaxGain, MinGain, 8, "MaxGain");
            Range(MidSpeed, 0.1, 100, "MidSpeed"); Range(CurvePower, 0.1, 8, "CurvePower");
            Range(SpeedSmoothingMs, 0.1, 1000, "SpeedSmoothingMs");
            Range(MaxFrameIntervalMs, 1, 1000, "MaxFrameIntervalMs");
            Range(IdleResetMs, MaxFrameIntervalMs, 60000, "IdleResetMs");
        }
        private static void Range(double v, double min, double max, string name)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v < min || v > max)
                throw new InvalidDataException(name + " must be between " + min + " and " + max + ".");
        }
        public static ForceClickOptions Load(string path)
        {
            if (!File.Exists(path)) return new ForceClickOptions();
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(reader.ReadToEnd())))
            {
                var value = (ForceClickOptions)new DataContractJsonSerializer(typeof(ForceClickOptions)).ReadObject(stream);
                if (value == null) throw new InvalidDataException("Configuration is null.");
                value.Validate(); return value;
            }
        }
        public void Save(string path)
        {
            Validate();
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temp))
                    new DataContractJsonSerializer(typeof(ForceClickOptions)).WriteObject(stream, this);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public enum PreviewRoute { None, QuickLook, FocusedSpace }
    public static class PreviewPolicy
    {
        public static PreviewRoute Resolve(string processName, string windowClass, string focusClass, bool modifiersHeld)
        {
            if (modifiersHeld || string.IsNullOrEmpty(windowClass) || string.IsNullOrEmpty(focusClass)) return PreviewRoute.None;
            // Renaming, address bars and text viewers must never receive a synthetic Space.
            if (focusClass.Equals("Edit", StringComparison.OrdinalIgnoreCase)
                || focusClass.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
                || focusClass.StartsWith("WindowsForms10.EDIT", StringComparison.OrdinalIgnoreCase)
                || focusClass.Equals("Scintilla", StringComparison.OrdinalIgnoreCase)) return PreviewRoute.None;
            if (string.Equals(processName, "dopus", StringComparison.OrdinalIgnoreCase)
                && windowClass == "dopus.lister")
                return focusClass == "dopus.filedisplay" || focusClass == "dopus.iconfiledisplay"
                    ? PreviewRoute.FocusedSpace : PreviewRoute.None;
            if (string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase)
                && (windowClass == "CabinetWClass" || windowClass == "ExploreWClass" || windowClass == "Progman" || windowClass == "WorkerW"))
                return PreviewRoute.QuickLook;
            if (string.Equals(processName, "QuickLook", StringComparison.OrdinalIgnoreCase)
                && (windowClass.Contains("QuickLook") || windowClass.StartsWith("HwndWrapper[QuickLook")))
                return PreviewRoute.QuickLook;
            return PreviewRoute.None;
        }
    }

    public struct TrackpadSample
    {
        public ushort ScanTime;
        public uint ContactId;
        public int X, Y, Pressure, ContactCount;
        public bool Tip, Button;
        public static bool TryDecode(byte[] r, out TrackpadSample sample)
        {
            sample = new TrackpadSample();
            if (r == null || r.Length != 112 || r[0] != 5) return false;
            sample.Tip = (r[1] & 2) != 0;
            sample.ContactId = BitConverter.ToUInt32(r, 2);
            sample.X = BitConverter.ToUInt16(r, 6); sample.Y = BitConverter.ToUInt16(r, 8);
            sample.Pressure = Math.Max(BitConverter.ToUInt16(r, 14), BitConverter.ToUInt16(r, 109));
            sample.ScanTime = BitConverter.ToUInt16(r, 106);
            sample.ContactCount = r[108]; sample.Button = (r[111] & 1) != 0;
            return true;
        }
    }

    public struct MotionResult
    {
        public bool Valid, ContinuityLost;
        public double DeltaMs, Speed, PadDistance;
    }
    public sealed class ScanTimeMotion
    {
        private bool _ready;
        private TrackpadSample _previous;
        private double _lastArrival, _speed;
        public long ValidFrames, DuplicateFrames, Discontinuities;
        public void Reset() { _ready = false; _speed = 0; }
        public MotionResult Update(TrackpadSample sample, double arrivalMs, ForceClickOptions options)
        {
            var result = new MotionResult();
            if (sample.ContactCount != 1 || !sample.Tip) { Reset(); return result; }
            if (!_ready || sample.ContactId != _previous.ContactId || arrivalMs < _lastArrival || arrivalMs - _lastArrival > options.IdleResetMs)
            {
                result.ContinuityLost = _ready;
                Rebase(sample, arrivalMs); return result;
            }
            // HID scan time is a 16-bit counter in 100 microsecond units, not receipt time.
            int ticks = unchecked((ushort)(sample.ScanTime - _previous.ScanTime));
            _lastArrival = arrivalMs;
            if (ticks == 0) { DuplicateFrames++; return result; }
            double dt = ticks * 0.1;
            if (dt > options.MaxFrameIntervalMs)
            {
                Discontinuities++; result.ContinuityLost = true; Rebase(sample, arrivalMs); return result;
            }
            double dx = sample.X - _previous.X, dy = sample.Y - _previous.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            double alpha = 1.0 - Math.Exp(-dt / options.SpeedSmoothingMs);
            _speed = alpha * (distance / dt) + (1.0 - alpha) * _speed;
            _previous = sample;
            result.Valid = true; result.DeltaMs = dt; result.Speed = _speed; result.PadDistance = distance;
            ValidFrames++; return result;
        }
        private void Rebase(TrackpadSample sample, double arrival)
        { _ready = true; _previous = sample; _lastArrival = arrival; _speed = 0; }
    }

    public sealed class PressGesture
    {
        private bool _pressed, _blocked, _fired;
        private int _x, _y, _cursorX, _cursorY, _downPressure;
        private uint _contact;
        private double _lastFire = double.NegativeInfinity;
        private double _thresholdSince = double.NaN;
        public bool Blocked { get { return _blocked; } }
        public bool Held { get { return _pressed; } }
        public void CancelHeld() { if (_pressed) _blocked = true; }
        public bool Update(TrackpadSample sample, int cursorX, int cursorY, double nowMs, ForceClickOptions options)
        {
            if (!sample.Button)
            {
                _pressed = _blocked = _fired = false;
                _thresholdSince = double.NaN;
                return false;
            }
            if (!_pressed)
            {
                _pressed = true; _blocked = _fired = false;
                _thresholdSince = double.NaN;
                _x = sample.X; _y = sample.Y; _cursorX = cursorX; _cursorY = cursorY; _contact = sample.ContactId;
                _downPressure = sample.Pressure;
            }
            // Anchor at physical button-down, including pressures BELOW 110. Latch until release.
            if (sample.ContactCount != 1 || !sample.Tip || sample.ContactId != _contact
                || Math.Abs(sample.X - _x) > options.DragCancelDistance
                || Math.Abs(sample.Y - _y) > options.DragCancelDistance
                || Math.Abs(cursorX - _cursorX) > options.DragCancelCursorPixels
                || Math.Abs(cursorY - _cursorY) > options.DragCancelCursorPixels)
                _blocked = true;
            if (!options.EnableForceClick) _blocked = true;
            if (sample.Pressure < Math.Max(options.ForceClickThreshold, _downPressure + options.ForceClickPressureRise))
            {
                _thresholdSince = double.NaN;
                return false;
            }
            if (double.IsNaN(_thresholdSince)) _thresholdSince = nowMs;
            bool immediate = options.ForceClickImmediateThreshold > 0 && sample.Pressure >= options.ForceClickImmediateThreshold;
            if (_blocked || _fired || (!immediate && nowMs - _thresholdSince < options.ForceClickDwellMs) || nowMs - _lastFire < 450)
                return false;
            _fired = true; _lastFire = nowMs;
            return true;
        }
    }

    public static class PointerGain
    {
        public static double Curve(double speed, ForceClickOptions options)
        {
            if (speed <= 0.001) return options.MinGain;
            double power = Math.Pow(speed / options.MidSpeed, options.CurvePower);
            return options.MinGain + (options.MaxGain - options.MinGain) * power / (1 + power);
        }
        public static double Combined(double speed, double monitorScale, bool pressed, ForceClickOptions options)
        {
            double curve = options.EnableAcceleration && (!pressed || options.AccelerateWhilePressed) ? Curve(speed, options) : 1;
            return curve * (options.EnableMonitorCompensation ? monitorScale : 1);
        }
    }

    public static class AbsolutePointer
    {
        public static int Normalize(int pixel, int origin, int extent)
        {
            if (extent <= 0) throw new ArgumentOutOfRangeException("extent");
            long offset = Math.Max(0L, Math.Min((long)extent - 1, (long)pixel - origin));
            // SendInput maps 0..65535 over the entire virtual desktop. Aim at the pixel center.
            return (int)Math.Min(65535L, ((2 * offset + 1) * 65536L) / (2L * extent));
        }
    }
}
