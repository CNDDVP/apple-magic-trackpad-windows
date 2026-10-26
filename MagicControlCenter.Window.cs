using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Globalization;
using System.Collections.Generic;

namespace MagicForceClick
{
    internal static class ControlCenterProgram
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
 [STAThread]
        private static int Main(string[] args)
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string executableDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string directory = ResolveConfigurationDirectory(executableDirectory);
            bool isolated = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--config-dir" && i + 1 < args.Length) directory = args[++i];
                if (args[i] == "--isolated") isolated = true;
            }
            Application.Run(new ControlCenterForm(directory, isolated));
            return 0;
        }

        internal static string ResolveConfigurationDirectory(string executableDirectory)
        {
            string local = Path.GetFullPath(executableDirectory);
            if (File.Exists(Path.Combine(local, "MagicForceClick.json"))) return local;
            DirectoryInfo parent = Directory.GetParent(local.TrimEnd(Path.DirectorySeparatorChar));
            if (parent == null) return local;
            return File.Exists(Path.Combine(parent.FullName, "MagicForceClick.json")) ? parent.FullName : local;
        }
    }

    internal sealed class ControlCenterForm : Form
    {
        [DllImport("user32.dll")] private static extern int GetDpiForSystem();
        private readonly string _directory, _configPath, _statusPath;
        private readonly bool _isolated;
        private readonly Panel _pageHost = new Panel();
        private readonly Panel _pageCanvas = new Panel();
        private readonly Label _pageTitle = new Label(), _pageDescription = new Label(), _stateLabel = new Label(), _profileLabel = new Label();
        private readonly Label _runtimeLabel = new Label(), _deviceLabel = new Label(), _feedbackLabel = new Label();
        private readonly Button _saveButton = new Button(), _discardButton = new Button();
        private readonly Timer _timer = new Timer();
        private readonly ConfigurationStore _store;
        private readonly INativeTouchpadPlatform _nativePlatform;
        private readonly IRuntimeStatusProvider _runtimeProvider;
        private readonly ErrorProvider _errors;
        private ForceClickOptions _savedOptions, _draft;
        private NativeTouchpadSnapshot _savedNative, _draftNative;
        private RuntimeStatusSnapshot _runtime;
        private RuntimeObservation _observation;
        private RuntimeAssessment _assessment;
        private string _fingerprint, _page = "overview";
        private bool _loading, _nativeAvailable, _saving, _awaitingReload, _nativeDirty;
        private bool _disposed;
        private readonly Icon _applicationIcon;
        private bool _saveFailed;
        private bool _reloadRequired, _nativeSaved;
        private string _notice = "配置已读取", _failureState = "保存未完成";
        private DraftValidationIssue _numberIssue;
        private float _dpiScale = 1f;
        private DateTime _savedAt;
        private FeelCurve _curve;
        private PressDiagram _pressDiagram;
        private Panel _overviewRuntime, _overviewDevice;
        private FlowLayoutPanel _presetButtons;
        private Button _previewModeButton;
        private Label _draftProfileLabel, _modeLabel, _overviewProfileCaption, _overviewSummary, _curveNote;
        private readonly Dictionary<string, NumericUpDown> _numberEditors = new Dictionary<string, NumericUpDown>();
        private readonly HashSet<string> _editedNumbers = new HashSet<string>();
        private NumericUpDown _threshold, _dwell, _rise, _immediate, _dragDistance, _dragPixels;
        private NumericUpDown _minGain, _maxGain, _midSpeed, _curvePower;
        private CheckBox _preview, _acceleration, _monitor, _pressed;
        private TrackBar _speed;
        private Label _speedValue;
        private readonly System.Collections.Generic.Dictionary<string, CheckBox> _nativeChecks = new System.Collections.Generic.Dictionary<string, CheckBox>();
        private readonly System.Collections.Generic.Dictionary<string, Button> _navigation = new System.Collections.Generic.Dictionary<string, Button>();
        private TextBox _diagnosticsText;
        private Panel _nativeNote;

        internal int PageCount { get { return 5; } }
        internal Panel PageHost { get { return _pageHost; } }
        internal bool CanSave { get { return _saveButton.Enabled && _saveButton.Text == "保存并应用"; } }
        internal float LayoutScale { get { return _dpiScale; } }
        internal string ConfigPath { get { return _configPath; } }
        internal string CurrentPage { get { return _page; } }

        internal ControlCenterForm(string directory, bool isolated)
            : this(directory, isolated, null, null, null) { }

        internal ControlCenterForm(string directory, bool isolated, INativeTouchpadPlatform native, IRuntimeStatusProvider runtime, ConfigurationStore store)
        {
            SuspendLayout();
            _directory = Path.GetFullPath(directory);
            _configPath = Path.Combine(_directory, "MagicForceClick.json");
            _statusPath = Path.Combine(_directory, "MagicForceClick.status.json");
            _isolated = isolated;
            _store = store ?? new ConfigurationStore();
            _nativePlatform = native ?? (isolated ? (INativeTouchpadPlatform)new MemoryTouchpadPlatform() : new WindowsTouchpadPlatform());
            _runtimeProvider = runtime ?? new RuntimeStatusProvider(isolated);
            _errors = new ErrorProvider { ContainerControl = this, BlinkStyle = ErrorBlinkStyle.NeverBlink };
            Text = "Magic Trackpad · 控制中心";
            _applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Icon = _applicationIcon;
            ClientSize = new Size(1140, 760);
            MinimumSize = new Size(920, 650);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Ui.Canvas;
            ForeColor = Ui.Ink;
            Font = Ui.Body;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            Ui.BuildShell(this, _pageHost, _pageTitle, _pageDescription, _stateLabel, _saveButton, _discardButton, _navigation);
            _pageCanvas.BackColor = Ui.Canvas;
            _pageHost.Controls.Add(_pageCanvas);
            _dpiScale = 1f;
            Ui.ArrangeShell(this, _pageHost);
            AutoScroll = false;
            _pageHost.AutoScroll = true;
            Resize += delegate { Ui.ArrangeShell(this, _pageHost); };
            _saveButton.Click += delegate { SaveChanges(); };
            _discardButton.Click += delegate { LoadConfiguration(true); };
            _timer.Interval = 1500;
            _timer.Tick += delegate { RefreshRuntime(); };
            Shown += delegate { _timer.Start(); };
            FormClosing += OnFormClosing;
            LoadConfiguration(false);
            ResumeLayout(true);
        }

        internal void Navigate(string page)
        {
            if (!_navigation.ContainsKey(page)) throw new ArgumentException("Unknown page: " + page);
            if (!_loading && !CommitNumericEditors()) return;
            CaptureDraft();
            _loading = true;
            try
            {
                _page = page;
                foreach (var item in _navigation) Ui.StyleNavigation(item.Value, item.Key == page);
                _pageHost.SuspendLayout();
                ClearPageControls();
                switch (page)
                {
                    case "overview": BuildOverview(); break;
                    case "feel": BuildFeel(); break;
                    case "press": BuildPress(); break;
                    case "gestures": BuildGestures(); break;
                    case "diagnostics": BuildDiagnostics(); break;
                }
                int bottom = 0;
                foreach (Control control in _pageCanvas.Controls) bottom = Math.Max(bottom, control.Bottom);
                _pageCanvas.SetBounds(24, 8, 820, bottom + 24);
                ScalePageToDpi();
                SyncDpiLayout();
                _pageHost.ResumeLayout(true);
            }
            finally { _loading = false; }
            RefreshOverview();
            RefreshFeedback();
        }

        private void SyncDpiLayout()
        {
            _dpiScale = IsHandleCreated ? DeviceDpi / 96f : 1f;
            if (_dpiScale <= 0) _dpiScale = 1f;
            Ui.ArrangeShell(this, _pageHost);
            Ui.ArrangeDividers(_pageCanvas, _dpiScale);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SyncDpiLayout();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SyncDpiLayout();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            SyncDpiLayout();
        }

        private void ScalePageToDpi()
        {
            // During construction the form's AutoScaleMode.Dpi pass scales the
            // initial page together with the shell. Pages created after the
            // handle exists need one logical-to-physical pass of their own.
            if (!IsHandleCreated) return;
            if (Math.Abs(_dpiScale - 1f) >= 0.01f) _pageCanvas.Scale(new SizeF(_dpiScale, _dpiScale));
            Ui.ScalePageFonts(_pageCanvas, DeviceDpi);
        }

        private void ClearPageControls()
        {
            _pageHost.SuspendLayout();
            foreach (Label shared in new[] { _runtimeLabel, _deviceLabel, _profileLabel })
            {
                if (shared.Parent != null) shared.Parent.Controls.Remove(shared);
            }
            _overviewRuntime = null;
            _overviewDevice = null;
            _presetButtons = null;
            _previewModeButton = null; _draftProfileLabel = null; _modeLabel = null;
            _overviewProfileCaption = null; _overviewSummary = null; _curveNote = null;
            _errors.Clear(); _numberEditors.Clear(); _editedNumbers.Clear(); _numberIssue = null;
            _curve = null;
            _pressDiagram = null;
            _threshold = null; _dwell = null; _rise = null; _immediate = null; _dragDistance = null; _dragPixels = null;
            _minGain = null; _maxGain = null; _midSpeed = null; _curvePower = null;
            _preview = null; _acceleration = null; _monitor = null; _pressed = null;
            _speed = null; _speedValue = null; _diagnosticsText = null; _nativeNote = null;
            _nativeChecks.Clear();
            while (_pageCanvas.Controls.Count > 0) _pageCanvas.Controls[0].Dispose();
            _pageHost.AutoScrollPosition = Point.Empty;
            _pageHost.ResumeLayout(false);
        }

        private void LoadConfiguration(bool notify)
        {
            _loading = true;
            try
            {
                ConfigurationSnapshot snapshot = ConfigurationSnapshot.Read(_configPath);
                _savedOptions = snapshot.Options;
                _draft = Clone(_savedOptions);
                _fingerprint = snapshot.Fingerprint;
                try
                {
                    _savedNative = _nativePlatform.Read();
                    _draftNative = new NativeTouchpadSnapshot(_savedNative.Parameters);
                    _nativeAvailable = true;
                }
                catch { _nativeAvailable = false; _nativeDirty = false; _savedNative = _draftNative = null; }
                _awaitingReload = false;
                _saveFailed = false;
                _reloadRequired = false; _nativeSaved = false;
                _notice = notify ? "已放弃未保存修改" : "配置已读取";
                _nativeDirty = false;
                RefreshRuntime();
                Navigate(_page);
            }
            catch (Exception ex)
            {
                _stateLabel.Text = "配置读取失败，未修改原文件";
                _reloadRequired = true; _saveFailed = true; _failureState = "配置读取失败，请重新读取";
                if (!IsHandleCreated) throw;
                MessageBox.Show(this, ex.Message + "\r\n\r\n" + _configPath, "无法读取配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { _loading = false; RefreshFeedback(); }
        }

        private bool HasChanges()
        {
            _nativeDirty = _nativeAvailable && _draftNative != null && !_draftNative.SameEditableValues(_savedNative);
            return _numberIssue != null || _nativeDirty || !SameOptions(_draft, _savedOptions);
        }

        private void RequestReload()
        {
            CaptureDraft();
            if (HasChanges() && MessageBox.Show(this, "重新读取将放弃未保存修改，是否继续？", "重新读取配置", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            LoadConfiguration(false);
            if (!_reloadRequired) _notice = "已重新读取配置";
            RefreshFeedback();
        }

        private static ForceClickOptions Clone(ForceClickOptions source)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(ForceClickOptions));
                serializer.WriteObject(stream, source);
                stream.Position = 0;
                return (ForceClickOptions)serializer.ReadObject(stream);
            }
        }

        private void BuildOverview()
        {
            Header("你的触控板，按你的手感。", "保留熟悉的 Windows 手势，在这里调整指针、深按与滚动。", "概览");
            var intro = Ui.Card(0, 0, 820, 116);
            intro.Controls.Add(Ui.Text("Magic Trackpad", 22, 20, 16, new Font("Segoe UI", 24, FontStyle.Regular), Ui.Ink));
            _overviewProfileCaption = Ui.Text("已保存曲线", 22, 73, 12, Ui.Body, Ui.Muted);
            intro.Controls.Add(_overviewProfileCaption);
            _profileLabel.SetBounds(126, 71, 235, 30); _profileLabel.Font = Ui.Emphasis; intro.Controls.Add(_profileLabel);
            intro.Controls.Add(Ui.Pill("保留现有驱动", 700, 22, Ui.SagePale, Ui.Sage));
            _overviewSummary = Ui.Text("", 400, 62, 11, Ui.Small, Ui.Muted);
            _overviewSummary.SetBounds(400, 62, 390, 48); intro.Controls.Add(_overviewSummary);
            _pageCanvas.Controls.Add(intro);
            _overviewRuntime = Ui.Card(0, 136, 400, 152);
            _overviewDevice = Ui.Card(420, 136, 400, 152);
            _overviewRuntime.Controls.Add(Ui.Text("增强后台", 20, 18, 13, Ui.Emphasis, Ui.Ink));
            _overviewDevice.Controls.Add(Ui.Text("触控板与驱动", 20, 18, 13, Ui.Emphasis, Ui.Ink));
            _runtimeLabel.SetBounds(20, 52, 356, 50); _runtimeLabel.Font = Ui.Body; _overviewRuntime.Controls.Add(_runtimeLabel);
            _deviceLabel.SetBounds(20, 52, 356, 50); _deviceLabel.Font = Ui.Body; _overviewDevice.Controls.Add(_deviceLabel);
            _overviewRuntime.Controls.Add(Ui.Text("读取进程与实时诊断，不把旧文件当成在线。", 20, 110, 10, Ui.Small, Ui.Muted));
            _overviewDevice.Controls.Add(Ui.Text("Windows Precision Touchpad · Apple USB", 20, 110, 10, Ui.Small, Ui.Muted));
            _pageCanvas.Controls.Add(_overviewRuntime); _pageCanvas.Controls.Add(_overviewDevice);
            var actions = Ui.Card(0, 308, 820, 150);
            actions.Controls.Add(Ui.Text("从这里开始", 20, 17, 13, Ui.Emphasis, Ui.Ink));
            actions.Controls.Add(Ui.ActionButton("调节指针手感", "速度、S 型曲线和多屏补偿", 20, 52, 246, delegate { Navigate("feel"); }));
            actions.Controls.Add(Ui.ActionButton("设置深按预览", "压力、稳定时间与拖拽保护", 286, 52, 246, delegate { Navigate("press"); }));
            actions.Controls.Add(Ui.ActionButton("查看运行诊断", "配置热加载与输入状态", 552, 52, 246, delegate { Navigate("diagnostics"); }));
            _pageCanvas.Controls.Add(actions);
            _pageCanvas.Controls.Add(Ui.Note("使用提示", "单指按住可拖拽；三指、四指手势沿用 Windows 设置。深按预览支持资源管理器和 Directory Opus。", 0, 478, 820));
        }

        private void BuildFeel()
        {
            Header("快一点，但不失控。", "先选曲线手感，再按需要微调参数；Windows 基础速度可在本页下方单独设置。", "指针手感");
            var presets = Ui.Card(0, 0, 820, 136);
            presets.Controls.Add(Ui.Text("指针曲线方案", 18, 13, 12, Ui.Emphasis, Ui.Ink));
            _draftProfileLabel = Ui.Text("", 170, 15, 10, Ui.Small, Ui.Sage);
            _draftProfileLabel.Width = 620; presets.Controls.Add(_draftProfileLabel);
            _presetButtons = new FlowLayoutPanel { Left = 18, Top = 39, Width = 780, Height = 36, WrapContents = false };
            var light = Ui.Button("轻压跨屏", 116, delegate { ApplyPreset("light"); }); light.Tag = PointerPreset.LightCrossScreen;
            var classic = Ui.Button("v1.1 经典", 116, delegate { ApplyPreset("classic"); }); classic.Tag = PointerPreset.Classic;
            _presetButtons.Controls.Add(light); _presetButtons.Controls.Add(classic);
            _presetButtons.Controls.Add(Ui.Text("只改曲线；深按力度、功能开关和拖拽保护保持不变。", 14, 8, 10, Ui.Small, Ui.Muted));
            _presetButtons.Controls[2].Width = 510;
            presets.Controls.Add(_presetButtons); _pageCanvas.Controls.Add(presets);
            _previewModeButton = Ui.Button("快捷：仅深按预览", 156, delegate { ApplyPreset("preview"); });
            _previewModeButton.SetBounds(18, 88, 156, 32); presets.Controls.Add(_previewModeButton);
            _modeLabel = Ui.Text("", 190, 95, 10, Ui.Small, Ui.Muted); _modeLabel.Width = 600; presets.Controls.Add(_modeLabel);
            var graph = Ui.Card(0, 152, 430, 356);
            graph.Controls.Add(Ui.Text("S 型指针曲线", 18, 15, 13, Ui.Emphasis, Ui.Ink));
            _curve = new FeelCurve { Left = 18, Top = 48, Width = 392, Height = 232, Options = _draft };
            graph.Controls.Add(_curve);
            graph.Controls.Add(Ui.Text("指尖速度 →", 288, 290, 10, Ui.Small, Ui.Muted));
            _curveNote = Ui.Text("", 18, 318, 10, Ui.Small, Ui.Muted); _curveNote.Width = 394; graph.Controls.Add(_curveNote);
            _pageCanvas.Controls.Add(graph);
            var controls = Ui.Card(450, 152, 370, 408);
            controls.Controls.Add(Ui.Text("增强功能", 18, 15, 13, Ui.Emphasis, Ui.Ink));
            _acceleration = Switch("启用 S 型指针加速", 18, 47, _draft.EnableAcceleration);
            _monitor = Switch("多屏物理速度补偿", 18, 85, _draft.EnableMonitorCompensation);
            _pressed = Switch("按住拖拽时也加速", 18, 123, _draft.AccelerateWhilePressed);
            controls.Controls.Add(_acceleration); controls.Controls.Add(_monitor); controls.Controls.Add(_pressed);
            controls.Controls.Add(Ui.Divider(18, 163, 334));
            _minGain = AddNumber(controls, "慢速增益", "越低越稳", 18, 178, 0.1M, 8M, 2, _draft.MinGain, FeelChanged, "MinGain");
            _maxGain = AddNumber(controls, "快速最大增益", "跨屏效率", 18, 228, 0.1M, 8M, 2, _draft.MaxGain, FeelChanged, "MaxGain");
            _midSpeed = AddNumber(controls, "加速介入速度", "越小越早", 18, 278, 0.1M, 100M, 2, _draft.MidSpeed, FeelChanged, "MidSpeed");
            _curvePower = AddNumber(controls, "曲线指数", "变化陡缓", 18, 328, 0.1M, 8M, 2, _draft.CurvePower, FeelChanged, "CurvePower");
            _pageCanvas.Controls.Add(controls);
            var native = Ui.Card(0, 580, 820, 104);
            native.Controls.Add(Ui.Text("Windows 基础指针速度", 18, 13, 12, Ui.Emphasis, Ui.Ink));
            _speed = new TrackBar { Left = 18, Top = 42, Width = 260, Height = 44, Minimum = 1, Maximum = 20, TickFrequency = 1, AutoSize = false };
            _speed.ValueChanged += delegate { if (_loading || !_nativeAvailable) return; _speedValue.Text = _speed.Value + " / 20"; _draftNative.WithSetting("cursorSpeed", _speed.Value); MarkDirty(); };
            native.Controls.Add(_speed);
            _speedValue = Ui.Text("", 292, 51, 11, Ui.Emphasis, Ui.Sage);
            _speedValue.Width = 105;
            native.Controls.Add(_speedValue);
            native.Controls.Add(Ui.Text("基础速度与增强曲线分别生效，不会重复写入驱动。", 420, 51, 10, Ui.Small, Ui.Muted));
            _pageCanvas.Controls.Add(native);
            if (_nativeAvailable) { _speed.Value = (int)Math.Max(1, Math.Min(20, _draftNative.Parameters.cursorSpeed)); _speedValue.Text = _speed.Value + " / 20"; }
            else { _speed.Enabled = false; _speedValue.Text = "API 不可用"; }
            _pageCanvas.Controls.Add(Ui.Note("高级参数", "速度平滑、帧间隔和空闲重置会原样保留；需要精细调整时可在诊断页查看配置文件。", 0, 704, 820));
        }

        private void BuildPress()
        {
            Header("深按，留下一个明确的动作。", "在资源管理器或 Directory Opus 中深按触控板，调用文件预览；移动拖拽时自动取消。", "深按预览");
            var main = Ui.Card(0, 0, 370, 474);
            main.Controls.Add(Ui.Text("触发方式", 18, 15, 13, Ui.Emphasis, Ui.Ink));
            _preview = Switch("启用深按文件预览", 18, 47, _draft.EnableForceClick);
            main.Controls.Add(_preview);
            _threshold = AddNumber(main, "压力阈值", "70–250，越低越容易", 18, 94, 70, 250, 0, _draft.ForceClickThreshold, PressChanged, "ForceClickThreshold");
            _dwell = AddNumber(main, "稳定时间", "毫秒，过滤短促用力", 18, 144, 0, 500, 0, _draft.ForceClickDwellMs, PressChanged, "ForceClickDwellMs");
            _rise = AddNumber(main, "按下后加力", "防止按住就触发", 18, 194, 0, 100, 0, _draft.ForceClickPressureRise, PressChanged, "ForceClickPressureRise");
            _immediate = AddNumber(main, "快速深按阈值", "0 为关闭即时触发", 18, 244, 0, 250, 0, _draft.ForceClickImmediateThreshold, PressChanged, "ForceClickImmediateThreshold");
            _dragPixels = AddNumber(main, "拖拽光标容差", "像素，防止预览打断拖拽", 18, 294, 1, 64, 0, _draft.DragCancelCursorPixels, PressChanged, "DragCancelCursorPixels");
            _dragDistance = AddNumber(main, "触控板移动容差", "原始坐标，默认 220", 18, 344, 10, 2000, 0, _draft.DragCancelDistance, PressChanged, "DragCancelDistance");
            main.Controls.Add(Ui.Text("低压阈值必须配合足够稳定时间和加力；\r\n不安全的组合会在保存前被拒绝。", 18, 414, 10, Ui.Small, Ui.Muted));
            _pageCanvas.Controls.Add(main);
            var visual = Ui.Card(390, 0, 430, 474);
            visual.Controls.Add(Ui.Text("一次深按的安全边界", 18, 15, 13, Ui.Emphasis, Ui.Ink));
            _pressDiagram = new PressDiagram { Left = 18, Top = 50, Width = 392, Height = 235, Options = _draft };
            visual.Controls.Add(_pressDiagram);
            visual.Controls.Add(Ui.Text("物理按下", 26, 294, 10, Ui.Small, Ui.Muted));
            visual.Controls.Add(Ui.Text("稳定加力", 180, 294, 10, Ui.Small, Ui.Muted));
            visual.Controls.Add(Ui.Text("触发预览", 316, 294, 10, Ui.Small, Ui.Sage));
            visual.Controls.Add(Ui.Divider(18, 323, 392));
            visual.Controls.Add(Ui.Text("支持范围", 18, 337, 11, Ui.Emphasis, Ui.Ink));
            visual.Controls.Add(Ui.Text("Windows 资源管理器 / Directory Opus\r\n文本编辑、重命名等场景不会发送空格键。", 18, 365, 10, Ui.Small, Ui.Muted));
            _pageCanvas.Controls.Add(visual);
            _pageCanvas.Controls.Add(Ui.Note("不改变普通点击", "预览只在物理按下后满足压力和稳定条件时触发。保存后由现有 MagicForceClick 后台自动热加载。", 0, 494, 820));
        }

        private void BuildGestures()
        {
            Header("熟悉的原生手势，保持原样。", "读写 Windows 当前用户的 Precision Touchpad 参数。三指、四指动作仍由 Windows 设置管理。", "原生手势");
            _nativeNote = Ui.Note(_nativeAvailable ? "原生参数" : "原生 API 不可用", _nativeAvailable ? "保存时通过 Windows API 写入并回读；未识别的参数位会原样保留。" : "当前会话无法读取触控板 API。原生设置暂不可改，其余增强配置仍可管理。", 0, 0, 820);
            _pageCanvas.Controls.Add(_nativeNote);
            var basic = Ui.Card(0, 116, 400, 312);
            basic.Controls.Add(Ui.Text("轻点与滚动", 18, 16, 13, Ui.Emphasis, Ui.Ink));
            AddNative(basic, "leaveOnWithMouse", "连接鼠标后仍使用触控板", 18, 54);
            AddNative(basic, "tap", "单指轻点 = 左键", 18, 96);
            AddNative(basic, "tapAndDrag", "轻点两次并拖动", 18, 138);
            AddNative(basic, "twoFingerTap", "双指轻点 = 右键", 18, 180);
            AddNative(basic, "pan", "双指滚动", 18, 222);
            basic.Controls.Add(Ui.Text("基础指针速度在「指针手感」页调节。", 18, 272, 10, Ui.Small, Ui.Muted));
            _pageCanvas.Controls.Add(basic);
            var advanced = Ui.Card(420, 116, 400, 312);
            advanced.Controls.Add(Ui.Text("方向与系统手势", 18, 16, 13, Ui.Emphasis, Ui.Ink));
            AddNative(advanced, "zoom", "双指缩放", 18, 54);
            AddNative(advanced, "reverseScroll", "反向滚动方向", 18, 96);
            advanced.Controls.Add(Ui.Divider(18, 140, 360));
            advanced.Controls.Add(Ui.Text("三指、四指手势和触觉反馈", 18, 157, 11, Ui.Emphasis, Ui.Ink));
            advanced.Controls.Add(Ui.Text("继续使用 Windows 与驱动原生面板，\r\n避免另一套映射抢占系统手势。", 18, 188, 10, Ui.Small, Ui.Muted));
            var windows = Ui.Button("Windows 触控板设置", 190, delegate { Open("ms-settings:devices-touchpad"); });
            windows.SetBounds(18, 242, 190, 34); advanced.Controls.Add(windows);
            _pageCanvas.Controls.Add(advanced);
            var driver = Ui.Card(0, 448, 820, 144);
            driver.Controls.Add(Ui.Text("驱动触觉反馈", 18, 15, 12, Ui.Emphasis, Ui.Ink));
            driver.Controls.Add(Ui.Text("反馈强度、按压灵敏度等硬件参数由 AmtPtpControlPanel 提供。", 18, 46, 10, Ui.Small, Ui.Muted));
            var feedback = Ui.Button("打开原驱动面板", 168, delegate { OpenFeedback(); });
            feedback.SetBounds(18, 92, 168, 34); driver.Controls.Add(feedback);
            _pageCanvas.Controls.Add(driver);
        }

        private void BuildDiagnostics()
        {
            Header("知道保存了，也知道是否生效。", "状态来自实际进程和诊断文件；旧状态、进程不匹配或热加载未完成都会明确显示。", "运行诊断");
            var status = Ui.Card(0, 0, 820, 176);
            status.Controls.Add(Ui.Text("后台与配置", 18, 15, 13, Ui.Emphasis, Ui.Ink));
            _diagnosticsText = new TextBox { Left = 18, Top = 48, Width = 780, Height = 105, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Ui.Ink, Font = Ui.Small, ScrollBars = ScrollBars.Vertical };
            status.Controls.Add(_diagnosticsText); _pageCanvas.Controls.Add(status);
            var files = Ui.Card(0, 196, 820, 178);
            files.Controls.Add(Ui.Text("配置与备份", 18, 15, 13, Ui.Emphasis, Ui.Ink));
            files.Controls.Add(new TextBox { Text = _configPath, Left = 18, Top = 48, Width = 780, Height = 28, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Ui.Muted, Font = Ui.Small });
            var openConfig = Ui.Button("打开配置目录", 156, delegate { OpenFolder(_directory); });
            openConfig.SetBounds(18, 96, 156, 32); files.Controls.Add(openConfig);
            var refresh = Ui.Button("重新读取", 120, delegate { RequestReload(); });
            refresh.SetBounds(188, 96, 120, 32); files.Controls.Add(refresh);
            files.Controls.Add(Ui.Text("每次保存前创建 JSON 备份；不会自动还原，也不会覆盖其他窗口的新修改。", 350, 98, 10, Ui.Small, Ui.Muted));
            _pageCanvas.Controls.Add(files);
            var tools = Ui.Card(0, 394, 820, 114);
            tools.Controls.Add(Ui.Text("配套工具", 18, 15, 13, Ui.Emphasis, Ui.Ink));
            var old = Ui.Button("原参数窗口", 136, delegate { Launch("MagicForceClick.exe", "--settings"); });
            old.SetBounds(18, 53, 136, 34); tools.Controls.Add(old);
            var repair = Ui.Button("查看使用说明", 144, delegate { Open(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MAGIC_CONTROL_CENTER.md")); });
            repair.SetBounds(168, 53, 144, 34); tools.Controls.Add(repair);
            tools.Controls.Add(Ui.Text("面板不会关闭后台、重装驱动或修改开机任务。\r\n" + (_isolated ? "当前为隔离测试模式：不读取本机运行目录。" : "当前为正常模式：保存后由现有后台热加载。"), 370, 54, 10, Ui.Small, Ui.Muted));
            _pageCanvas.Controls.Add(tools);
        }

        private void AddNative(Control parent, string name, string text, int x, int y)
        {
            bool value = _nativeAvailable && _draftNative.Parameters.HasFlag(Flag(name));
            var box = Switch(text, x, y, value, false);
            box.Enabled = _nativeAvailable;
            box.CheckedChanged += delegate { if (_loading || !_nativeAvailable) return; _draftNative.WithSetting(name, box.Checked); MarkDirty(); };
            _nativeChecks[name] = box;
            parent.Controls.Add(box);
        }

        private static uint Flag(string name)
        {
            switch (name)
            {
                case "leaveOnWithMouse": return 0x01;
                case "tap": return 0x04;
                case "tapAndDrag": return 0x08;
                case "twoFingerTap": return 0x10;
                case "pan": return 0x80;
                case "zoom": return 0x100;
                default: return 0x200;
            }
        }

        private CheckBox Switch(string text, int x, int y, bool value, bool markDirty = true)
        {
            var box = new CheckBox { Text = text, Left = x, Top = y, Width = 320, Height = 30, Checked = value, Font = Ui.Body, ForeColor = Ui.Ink, AutoEllipsis = true };
            if (markDirty) box.CheckedChanged += delegate { MarkDirty(); };
            return box;
        }

        private NumericUpDown AddNumber(Control parent, string title, string hint, int x, int y, decimal min, decimal max, int decimals, double value, EventHandler changed, string field)
        {
            var caption = Ui.Text(title, x, y, 11, Ui.Emphasis, Ui.Ink); caption.Width = 215; parent.Controls.Add(caption);
            var detail = Ui.Text(hint, x, caption.Bottom + 2, 9, Ui.Small, Ui.Muted); detail.Width = 215; parent.Controls.Add(detail);
            var input = new NumericUpDown { Left = x + 225, Top = y + 2, Width = 100, Height = 28, Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = decimals == 0 ? 1 : 0.05M, Value = (decimal)value, Font = Ui.Body, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right };
            input.Name = field; input.AccessibleName = title; _numberEditors[field] = input;
            EventHandler edit = delegate { if (!_loading) { _editedNumbers.Add(field); changed(input, EventArgs.Empty); MarkDirty(); } };
            input.ValueChanged += edit; input.TextChanged += edit;
            parent.Controls.Add(input);
            return input;
        }

        private void FeelChanged(object sender, EventArgs args)
        {
            if (_curve != null) _curve.Invalidate();
        }
        private void PressChanged(object sender, EventArgs args)
        {
            if (_pressDiagram != null) _pressDiagram.Invalidate();
        }

        private void ApplyPreset(string preset)
        {
            if (preset == "preview" && !CommitNumericEditors()) return;
            CaptureDraft();
            if (preset == "light") ProfileCatalog.ApplyPointer(_draft, PointerPreset.LightCrossScreen);
            else if (preset == "classic") ProfileCatalog.ApplyPointer(_draft, PointerPreset.Classic);
            else { _draft.EnableForceClick = true; _draft.EnableAcceleration = false; _draft.EnableMonitorCompensation = false; }
            _loading = true;
            Navigate(_page);
            MarkDirty();
        }

        private bool TryNumber(NumericUpDown input, out decimal value)
        {
            if (!decimal.TryParse(input.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
                || value < input.Minimum || value > input.Maximum || decimal.Round(value, input.DecimalPlaces) != value)
            {
                if (_numberIssue == null) _numberIssue = new DraftValidationIssue { Field = input.Name,
                    Message = input.AccessibleName + (input.DecimalPlaces == 0 ? "须输入允许范围内的整数。" : "须输入允许范围内、最多两位小数的数字。") };
                return false;
            }
            return true;
        }

        private bool CommitNumericEditors()
        {
            if (_loading) return true;
            CaptureDraft();
            if (_numberIssue != null) { RefreshFeedback(); return false; }
            _loading = true;
            try
            {
                foreach (string field in _editedNumbers)
                {
                    NumericUpDown input;
                    decimal value;
                    if (_numberEditors.TryGetValue(field, out input) && TryNumber(input, out value))
                    { input.Value = value; input.Text = value.ToString("F" + input.DecimalPlaces, CultureInfo.CurrentCulture); }
                }
            }
            finally { _loading = false; }
            CaptureDraft();
            return _numberIssue == null;
        }

        private void CaptureDraft()
        {
            if (_draft == null || _loading) return;
            _numberIssue = null;
            if (_preview != null) _draft.EnableForceClick = _preview.Checked;
            if (_acceleration != null) _draft.EnableAcceleration = _acceleration.Checked;
            if (_monitor != null) _draft.EnableMonitorCompensation = _monitor.Checked;
            if (_pressed != null) _draft.AccelerateWhilePressed = _pressed.Checked;
            foreach (string name in _editedNumbers)
            {
                NumericUpDown input; decimal value;
                if (!_numberEditors.TryGetValue(name, out input) || !TryNumber(input, out value)) continue;
                var field = typeof(ForceClickOptions).GetField(name);
                field.SetValue(_draft, Convert.ChangeType(value, field.FieldType, CultureInfo.InvariantCulture));
            }
        }

        private void MarkDirty()
        {
            if (_loading) return;
            if (!_reloadRequired) _saveFailed = false;
            CaptureDraft();
            RefreshFeedback();
            RefreshOverview();
        }

        private void RefreshFeedback()
        {
            if (_draft == null || _savedOptions == null) return;
            bool dirty = HasChanges();
            DraftValidationIssue issue = _numberIssue ?? DraftValidationIssue.Check(_draft);
            _errors.Clear();
            NumericUpDown input;
            if (issue != null && _numberEditors.TryGetValue(issue.Field, out input)) _errors.SetError(input, issue.Message);
            _saveButton.Enabled = dirty && !_saving && !_reloadRequired && issue == null;
            _saveButton.BackColor = _saveButton.Enabled ? Ui.Sage : Ui.Line;
            _discardButton.Enabled = (dirty || _reloadRequired) && !_saving;
            if (_saving) _stateLabel.Text = "正在保存并回读…";
            else if (_saveFailed) _stateLabel.Text = _failureState;
            else if (issue != null) _stateLabel.Text = "参数错误：" + issue.Message;
            else if (dirty) _stateLabel.Text = "有未保存修改";
            else if (_nativeSaved) _stateLabel.Text = "Windows 设置已应用并回读";
            else if (_awaitingReload)
                _stateLabel.Text = _assessment != null && _assessment.Applied ? "已保存，后台已生效" : "已保存，未确认生效：" + (_assessment == null ? "等待新鲜状态" : _assessment.Reason);
            else _stateLabel.Text = _notice;
            _stateLabel.ForeColor = dirty || issue != null || _saveFailed ? Ui.Warning : Ui.Muted;
            RefreshPresetState();
            RefreshOverview();
        }

        private void RefreshPresetState()
        {
            PointerPreset selected = ProfileCatalog.IdentifyPointer(_draft);
            if (_presetButtons != null)
                foreach (Control control in _presetButtons.Controls)
                {
                    var button = control as Button;
                    if (button == null || !(button.Tag is PointerPreset)) continue;
                    bool active = (PointerPreset)button.Tag == selected;
                    button.BackColor = active ? Ui.SagePale : Color.White;
                    button.ForeColor = active ? Ui.Sage : Ui.Ink;
                    button.FlatAppearance.BorderColor = active ? Ui.Sage : Ui.Line;
                    button.AccessibleDescription = active ? "当前草稿曲线" : "应用此曲线，保留其他参数";
                }
            if (_draftProfileLabel != null) _draftProfileLabel.Text = "当前草稿曲线：" + ProfileName(_draft) + (HasChanges() ? "（未保存）" : "（已保存）");
            if (_modeLabel != null) _modeLabel.Text = "当前草稿模式：" + ProfileCatalog.ModeName(_draft);
            if (_curveNote != null) _curveNote.Text = _draft.EnableAcceleration ? "增益随速度平滑上升，慢速微调，快速跨屏。" : "指针加速已关闭；图中仅保留曲线设置。";
            if (_curve != null) _curve.Invalidate();
            if (_pressDiagram != null) _pressDiagram.Invalidate();
        }

        private static string ProfileName(ForceClickOptions value)
        {
            switch (ProfileCatalog.IdentifyPointer(value))
            {
                case PointerPreset.LightCrossScreen: return "轻压跨屏";
                case PointerPreset.Classic: return "v1.1 经典";
                default: return "自定义曲线";
            }
        }

        private static bool SameOptions(ForceClickOptions a, ForceClickOptions b) { return OptionValues.Equal(a, b); }

        private void RefreshRuntime()
        {
            try { _observation = _runtimeProvider.Read(_directory, _statusPath); }
            catch (Exception error) { _observation = new RuntimeObservation { Status = RuntimeStatusSnapshot.Unavailable(error.Message) }; }
            _runtime = _observation.Status;
            _assessment = RuntimeAssessment.Evaluate(_observation, _savedOptions, _awaitingReload ? _savedAt : DateTime.MinValue, DateTime.UtcNow);
            string live = _assessment.Healthy ? "运行中 · 输入已注册" : _assessment.Reason;
            string age = _runtime != null && _runtime.LastWriteUtc != DateTime.MinValue ? Math.Max(0, (DateTime.UtcNow - _runtime.LastWriteUtc).TotalSeconds).ToString("0") + " 秒前更新" : "没有新鲜状态";
            _runtimeLabel.Text = live + "\r\n" + age + " · " + (_runtime == null ? "未知版本" : (_runtime.Version ?? "未知版本"));
            _runtimeLabel.ForeColor = _assessment.Healthy ? Ui.Sage : Ui.Warning;
            _deviceLabel.Text = _nativeAvailable ? (_isolated ? "隔离原生参数 · 仅用于测试\r\n不写入 Windows 实际设置" : "原生 API 可读取\r\n轻点 / 滚动 / 缩放可在此管理") : "原生 API 暂不可用\r\n请从正常 Windows 会话打开面板";
            _deviceLabel.ForeColor = _nativeAvailable ? Ui.Sage : Ui.Warning;
            if (_diagnosticsText != null)
            {
                _diagnosticsText.Text = "后台进程：" + (_observation.ProcessRunning ? "运行中" : "未找到 / 隔离模式") + "\r\n"
                    + "输入与进程：" + live + "    " + age + "\r\n"
                    + "Raw Input：" + (_runtime != null && _runtime.RawInputRegistered ? "已注册" : "未确认") + "    生效确认：" + _assessment.Reason + "\r\n"
                    + "指针输出：" + (_runtime == null ? "—" : (_runtime.PointerOutput ?? "—")) + "    有效帧：" + (_runtime == null ? 0 : _runtime.ValidScanFrames) + "\r\n"
                    + "重复帧：" + (_runtime == null ? 0 : _runtime.DuplicateScanFrames) + "    不连续：" + (_runtime == null ? 0 : _runtime.Discontinuities) + "    提交失败：" + (_runtime == null ? 0 : _runtime.PointerSubmissionFailures) + "\r\n"
                    + "配置文件：" + _configPath + (_isolated ? "\r\n隔离模式使用内存原生设置，不写入 Windows。" : "");
            }
            RefreshFeedback();
        }

        private void RefreshOverview()
        {
            if (_page != "overview" || _draft == null || _savedOptions == null) return;
            bool applied = _assessment != null && _assessment.Applied;
            ForceClickOptions actual = applied && _runtime != null ? _runtime.Options : _savedOptions;
            _profileLabel.Text = ProfileName(actual);
            if (_overviewProfileCaption != null) _overviewProfileCaption.Text = applied ? "后台生效曲线" : "已保存曲线";
            if (_overviewSummary != null) _overviewSummary.Text = ProfileCatalog.ModeName(actual) + "\r\n"
                + (HasChanges() ? "待保存草稿：" + ProfileName(_draft) : (applied ? "已保存与生效参数一致" : "后台未确认：" + (_assessment == null ? "没有状态" : _assessment.Reason)));
        }

        private void SaveChanges()
        {
            if (_saving || _reloadRequired || !CommitNumericEditors()) return;
            CaptureDraft();
            if (!HasChanges() || _numberIssue != null || DraftValidationIssue.Check(_draft) != null) { RefreshFeedback(); return; }
            _saving = true; _saveFailed = false;
            RefreshFeedback();
            DateTime started = DateTime.UtcNow;
            try
            {
                SettingsSaveResult result = SettingsSaver.Save(_configPath, _draft, _savedOptions, _fingerprint,
                    _nativeAvailable ? _draftNative : null, _savedNative, _nativePlatform, _store);
                if (!result.Success)
                {
                    _saveFailed = true; _reloadRequired = result.RequiresReload;
                    _failureState = result.JsonCommitted ? "配置已写入，回读未确认" : (result.RecoveryError != null ? "恢复未确认，请重新读取" : "保存未完成");
                    MessageBox.Show(this, result.Error.Message + "\r\n\r\n" + result.FailureMessage
                        + (result.RecoveryError == null ? "" : "\r\n恢复异常：" + result.RecoveryError.Message),
                        "保存未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (result.Configuration != null)
                {
                    _savedOptions = result.Configuration.Options; _draft = Clone(_savedOptions);
                    _fingerprint = result.Configuration.Fingerprint;
                }
                if (result.Native != null)
                {
                    _savedNative = result.Native;
                    _draftNative = new NativeTouchpadSnapshot(_savedNative.Parameters);
                }
                _savedAt = started; _awaitingReload = result.JsonCommitted;
                _nativeSaved = result.NativeAttempted && !result.JsonCommitted;
                _nativeDirty = false;
                _loading = true;
                Navigate(_page);
                RefreshRuntime();
            }
            finally { _loading = false; _saving = false; RefreshFeedback(); }
        }

        private void OpenFeedback()
        {
            string path = Path.Combine(_directory, "AmtPtpControlPanel.exe");
            if (!File.Exists(path)) { MessageBox.Show(this, "未找到原驱动面板：\r\n" + path, "配套工具", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            Launch("AmtPtpControlPanel.exe", "");
        }

        private void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void Launch(string file, string arguments)
        {
            string path = Path.Combine(_directory, file);
            if (!File.Exists(path)) { MessageBox.Show(this, "未找到配套文件：\r\n" + path, "配套工具", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            try { Process.Start(new ProcessStartInfo(path, arguments) { WorkingDirectory = _directory, UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void OpenFolder(string path) { Open(path); }

        private void Header(string title, string description, string shortTitle)
        {
            _pageTitle.Text = shortTitle;
            _pageDescription.Text = title + "\r\n" + description;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            CaptureDraft();
            if (HasChanges() || _reloadRequired)
            {
                DialogResult result = MessageBox.Show(this, "有未保存修改。要放弃这些修改并关闭吗？", "Magic Trackpad 控制中心", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result != DialogResult.Yes) e.Cancel = true;
            }
            if (e.Cancel) return;
            _timer.Stop();
            DetachForDisposal();
        }

        private void DetachForDisposal()
        {
            CaptureDraft();
            _loading = true;
            _timer.Stop();
            ClearPageControls();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!_disposed) {
                    DetachForDisposal();
                    _runtimeLabel.Dispose(); _deviceLabel.Dispose(); _profileLabel.Dispose();
                    if (_applicationIcon != null) _applicationIcon.Dispose();
                }
                _disposed = true;
                _timer.Dispose();
                _errors.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class Ui
    {
        internal static readonly Color Canvas = Color.FromArgb(246, 248, 247);
        internal static readonly Color Ink = Color.FromArgb(31, 45, 41);
        internal static readonly Color Muted = Color.FromArgb(105, 120, 114);
        internal static readonly Color Sage = Color.FromArgb(44, 116, 89);
        internal static readonly Color SagePale = Color.FromArgb(229, 241, 233);
        internal static readonly Color Line = Color.FromArgb(224, 231, 226);
        internal static readonly Color Warning = Color.FromArgb(157, 103, 32);
        internal static readonly Font Body = new Font("Microsoft YaHei UI", 10, FontStyle.Regular);
        internal static readonly Font Small = new Font("Microsoft YaHei UI", 9, FontStyle.Regular);
        internal static readonly Font Emphasis = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
        [DllImport("user32.dll")] private static extern int GetDpiForSystem();
        private static readonly int FontReferenceDpi = GetDpiForSystem();
        private static readonly System.Collections.Generic.Dictionary<string, Font> DpiFonts = new System.Collections.Generic.Dictionary<string, Font>();

        internal static Font FontForDpi(Control owner, Font logicalFont)
        {
            Form form = owner.FindForm();
            return FontForDpi(form != null ? form.DeviceDpi : owner.DeviceDpi, logicalFont);
        }

        private static Font FontForDpi(int dpi, Font logicalFont)
        {
            if (dpi == FontReferenceDpi) return logicalFont;
            string key = dpi + "|" + logicalFont.Name + "|" + logicalFont.SizeInPoints + "|" + logicalFont.Style;
            Font font;
            if (!DpiFonts.TryGetValue(key, out font)) {
                font = new Font(logicalFont.FontFamily, logicalFont.SizeInPoints * dpi / FontReferenceDpi, logicalFont.Style, GraphicsUnit.Point);
                DpiFonts.Add(key, font);
            }
            return font;
        }

        internal static void ScalePageFonts(Control root, int dpi)
        {
            // Newly built pages start with logical fonts. Framework DPI changes
            // scale existing controls, but do not retarget these new fonts.
            var fonts = new System.Collections.Generic.Dictionary<Control, Font>();
            CollectExplicitFonts(root, fonts);
            foreach (var item in fonts) item.Key.Font = FontForDpi(dpi, item.Value);
        }

        private static void CollectExplicitFonts(Control root, System.Collections.Generic.Dictionary<Control, Font> fonts)
        {
            foreach (Control child in root.Controls) {
                if (System.ComponentModel.TypeDescriptor.GetProperties(child)["Font"].ShouldSerializeValue(child)) fonts.Add(child, child.Font);
                CollectExplicitFonts(child, fonts);
            }
        }

        internal static Label Text(string text, int x, int y, int size, Font font, Color color)
        {
            Size measured = MeasureLogicalText(text, font, 650);
            return new Label { Text = text, Left = x, Top = y, Width = measured.Width, Height = measured.Height, AutoSize = false, UseMnemonic = false, Font = font, ForeColor = color, BackColor = Color.Transparent };
        }

        private static Size MeasureLogicalText(string text, Font font, int maxWidth)
        {
            // Page builders use 96-DPI coordinates. Measure in the same space;
            // the existing single DPI pass then scales these bounds once.
            using (var bitmap = new Bitmap(1, 1))
            {
                bitmap.SetResolution(96, 96);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    // Labels use GDI, whose rounded glyph widths differ from
                    // GDI+ MeasureString. Reserve the largest logical extent
                    // across the supported scale factors to avoid line reflow.
                    int width = 0, height = 0;
                    foreach (int dpi in new[] { 96, 120, 144, 168, 192 }) {
                        float scale = dpi / 96f;
                        Size measured = TextRenderer.MeasureText(graphics, string.IsNullOrEmpty(text) ? " " : text, FontForDpi(dpi, font),
                            new Size(Math.Max(1, (int)((maxWidth - 4) * scale)), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                        width = Math.Max(width, (int)Math.Ceiling(measured.Width / scale) + 4);
                        height = Math.Max(height, (int)Math.Ceiling(measured.Height / scale) + 4);
                    }
                    return new Size(Math.Min(maxWidth, width), Math.Max(22, height));
                }
            }
        }
        internal static Panel Card(int x, int y, int width, int height)
        {
            var panel = new Panel { Left = x, Top = y, Width = width, Height = height, BackColor = Color.White, Padding = new Padding(0) };
            panel.ControlAdded += delegate(object sender, ControlEventArgs args) {
                var label = args.Control as Label;
                if (label != null) {
                    label.Width = Math.Min(label.Width, Math.Max(30, panel.ClientSize.Width - label.Left - 18));
                    if (!string.IsNullOrEmpty(label.Text)) label.Height = Math.Max(label.Height, MeasureLogicalText(label.Text, label.Font, label.Width).Height);
                }
            };
            return panel;
        }
        internal static Label Pill(string text, int x, int y, Color background, Color foreground)
        {
            return new Label { Text = text, Left = x, Top = y, Width = 100, Height = 26, TextAlign = ContentAlignment.MiddleCenter, Font = Small, ForeColor = foreground, BackColor = background };
        }
        internal static Panel Divider(int x, int y, int width)
        {
            return new Panel { Name = "divider", Left = x, Top = y, Width = width, Height = 1, BackColor = Line };
        }
        internal static void ArrangeDividers(Control parent, float scale)
        {
            foreach (Control child in parent.Controls) {
                if (child.Name == "divider") child.Height = Math.Max(1, (int)Math.Round(scale));
                ArrangeDividers(child, scale);
            }
        }
        internal static Panel Note(string title, string text, int x, int y, int width)
        {
            var panel = new Panel { Left = x, Top = y, Width = width, Height = 96, BackColor = SagePale };
            Label heading = Text(title, 16, 9, 10, Emphasis, Sage); panel.Controls.Add(heading);
            Label body = Text(text, 16, heading.Bottom + 8, 9, Small, Muted);
            body.Width = width - 32;
            body.Height = panel.Height - body.Top - 12;
            panel.Controls.Add(body);
            return panel;
        }
        internal static Button Button(string text, int width, EventHandler click) { return Button(text, width, click, false); }
        internal static Button Button(string text, int width, EventHandler click, bool selected)
        {
            var button = new Button { Text = text, Width = width, Height = 32, Margin = new Padding(0, 0, 8, 0), FlatStyle = FlatStyle.Flat, BackColor = selected ? SagePale : Color.White, ForeColor = selected ? Sage : Ink, Font = Small, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = selected ? Sage : Line;
            button.FlatAppearance.BorderSize = 1;
            button.Click += click;
            return button;
        }
        internal static Button ActionButton(string title, string subtitle, int x, int y, int width, EventHandler click)
        {
            var button = new Button { Text = title + "\r\n" + subtitle, Left = x, Top = y, Width = width, Height = 70, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0), FlatStyle = FlatStyle.Flat, BackColor = Canvas, ForeColor = Ink, Font = Body, Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = Line; button.Click += click;
            return button;
        }

        internal static void BuildShell(ControlCenterForm form, Panel host, Label title, Label description, Label state, Button save, Button discard, System.Collections.Generic.Dictionary<string, Button> navigation)
        {
            var sidebar = new Panel { Dock = DockStyle.Left, Width = 240, BackColor = Color.FromArgb(237, 242, 238), Padding = new Padding(18) };
            form.Controls.Add(sidebar);
            var brand = Text("Magic Trackpad", 22, 27, 17, new Font("Segoe UI", 19, FontStyle.Regular), Ink); sidebar.Controls.Add(brand);
            var subtitle = Text("CONTROL CENTER", 24, brand.Bottom + 8, 8, new Font("Segoe UI", 8, FontStyle.Regular), Muted); sidebar.Controls.Add(subtitle);
            sidebar.Controls.Add(Text("把触控板调成你的手感", 24, subtitle.Bottom + 14, 9, Small, Muted));
            AddNav(sidebar, navigation, "overview", "⌂   概览", 158);
            AddNav(sidebar, navigation, "feel", "↗   指针手感", 206);
            AddNav(sidebar, navigation, "press", "◉   深按预览", 254);
            AddNav(sidebar, navigation, "gestures", "☝   原生手势", 302);
            AddNav(sidebar, navigation, "diagnostics", "≡   运行诊断", 350);
            var note = Text("Windows 原生手势\r\nMagicForceClick 增强\r\n\r\n后台版本见运行诊断", 24, 505, 9, Small, Muted);
            note.Height = 90; sidebar.Controls.Add(note);
            var main = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };
            form.Controls.Add(main); main.BringToFront();
            foreach (Control control in sidebar.Controls) control.Width = sidebar.Width - control.Left - 20;
            var top = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Canvas, Padding = new Padding(24, 20, 24, 0) };
            main.Controls.Add(top);
            title.SetBounds(24, 18, 180, 30); title.Font = new Font("Microsoft YaHei UI", 17, FontStyle.Bold); title.ForeColor = Ink; top.Controls.Add(title);
            description.SetBounds(24, 55, 775, 48); description.Font = Small; description.ForeColor = Muted; top.Controls.Add(description);
            host.Dock = DockStyle.Fill; host.BackColor = Canvas; host.Padding = new Padding(0); host.AutoScroll = true;
            main.Controls.Add(host); host.BringToFront();
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 72, BackColor = Color.White, Padding = new Padding(32, 17, 28, 12) };
            main.Controls.Add(bottom);
            state.SetBounds(32, 23, 420, 26); state.Font = Small; bottom.Controls.Add(state);
            save.Text = "保存并应用"; save.SetBounds(670, 17, 150, 38); save.FlatStyle = FlatStyle.Flat; save.BackColor = Sage; save.ForeColor = Color.White; save.Font = Emphasis; save.FlatAppearance.BorderSize = 0; save.Cursor = Cursors.Hand; bottom.Controls.Add(save);
            discard.Text = "放弃修改"; discard.SetBounds(540, 17, 112, 38); discard.FlatStyle = FlatStyle.Flat; discard.BackColor = Color.White; discard.ForeColor = Muted; discard.FlatAppearance.BorderColor = Line; discard.Font = Small; bottom.Controls.Add(discard);
            form.AcceptButton = save;
        }

        internal static void ArrangeShell(ControlCenterForm form, Panel host)
        {
            Control sidebar = null, main = null;
            foreach (Control control in form.Controls)
            {
                if (control.Dock == DockStyle.Left) sidebar = control;
                else if (control.Dock == DockStyle.Fill) main = control;
            }
            if (sidebar == null || main == null) return;
            Panel top = null, bottom = null;
            foreach (Control control in main.Controls)
            {
                if (control.Dock == DockStyle.Top) top = (Panel)control;
                else if (control.Dock == DockStyle.Bottom) bottom = (Panel)control;
            }
            int width = main.ClientSize.Width;
            float scale = form.LayoutScale;
            sidebar.Width = (int)Math.Round(240 * scale);
            top.Height = (int)Math.Round(112 * scale);
            bottom.Height = (int)Math.Round(72 * scale);
            sidebar.Height = main.ClientSize.Height;
            top.Width = width; bottom.Width = width;
            bottom.Top = Math.Max(0, main.ClientSize.Height - bottom.Height);
            foreach (Control control in sidebar.Controls)
            {
                if (control.Text.StartsWith("Windows 原生手势"))
                {
                    control.Top = Math.Max(0, main.ClientSize.Height - control.Height - (int)Math.Round(24 * scale));
                    control.Width = sidebar.Width - control.Left - (int)Math.Round(20 * scale);
                }
            }
            foreach (Control control in top.Controls) control.Width = Math.Max(80, width - control.Left - 28);
            int saveWidth = 0;
            foreach (Control control in bottom.Controls) if (control.Text == "保存并应用") saveWidth = control.Width;
            int saveLeft = Math.Max(12, width - (int)Math.Round(28 * scale) - saveWidth);
            int right = saveLeft - (int)Math.Round(18 * scale);
            foreach (Control control in bottom.Controls)
                if (control.Text == "保存并应用") control.Left = saveLeft;
            foreach (Control control in bottom.Controls)
                if (control.Text == "放弃修改") { control.Left = right - control.Width; right = control.Left - (int)Math.Round(18 * scale); }
            // Size status text after both buttons have their final bounds.
            foreach (Control control in bottom.Controls)
                if (control is Label) { control.Width = Math.Max(0, right - control.Left); ((Label)control).AutoEllipsis = true; }
            host.AutoScrollMinSize = new Size(0, 0);
        }

        private static void AddNav(Control parent, System.Collections.Generic.Dictionary<string, Button> navigation, string page, string text, int top)
        {
            var button = new Button { Text = text, Tag = page, Left = 14, Top = top, Width = 178, Height = 40, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0), FlatStyle = FlatStyle.Flat, Font = Body, Cursor = Cursors.Hand };
            button.FlatAppearance.BorderSize = 0;
            button.Click += delegate { ((ControlCenterForm)parent.FindForm()).Navigate(page); };
            navigation[page] = button; parent.Controls.Add(button);
        }
        internal static void StyleNavigation(Button button, bool selected)
        {
            button.BackColor = selected ? Color.White : Color.FromArgb(237, 242, 238);
            button.ForeColor = selected ? Sage : Muted;
            button.Font = FontForDpi(button, selected ? Emphasis : Body);
        }
    }

    internal sealed class FeelCurve : Control
    {
        internal ForceClickOptions Options { get; set; }
        internal FeelCurve() { DoubleBuffered = true; BackColor = Color.White; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Options == null) return;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle plot = new Rectangle(36, 12, Math.Max(50, Width - 50), Math.Max(50, Height - 38));
            using (var grid = new Pen(Ui.Line)) using (var text = new SolidBrush(Ui.Muted))
            {
                for (int i = 0; i <= 4; i++)
                {
                    int y = plot.Bottom - i * plot.Height / 4;
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    g.DrawString((Options.MinGain + (Options.MaxGain - Options.MinGain) * i / 4).ToString("0.0"), Ui.FontForDpi(this, Ui.Small), text, 0, y - 9);
                }
            }
            using (var pen = new Pen(Ui.Sage, 3))
            {
                PointF? previous = null;
                for (int i = 0; i <= 100; i++)
                {
                    double speed = 12.0 * i / 100;
                    double gain = PointerGain.Curve(speed, Options);
                    PointF point = new PointF(plot.Left + plot.Width * i / 100f, (float)(plot.Bottom - (gain - Options.MinGain) / Math.Max(0.01, Options.MaxGain - Options.MinGain) * plot.Height));
                    if (previous.HasValue) g.DrawLine(pen, previous.Value, point);
                    previous = point;
                }
            }
        }
    }

    internal sealed class PressDiagram : Control
    {
        internal ForceClickOptions Options { get; set; }
        internal PressDiagram() { DoubleBuffered = true; BackColor = Color.White; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Options == null) return;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int left = 16, right = Width - 12, baseline = Height - 20;
            using (var axis = new Pen(Ui.Line, 2)) { g.DrawLine(axis, left, baseline, right, baseline); g.DrawLine(axis, left, 10, left, baseline); }
            using (var threshold = new Pen(Ui.Warning, 1) { DashStyle = DashStyle.Dash })
            {
                int y = baseline - (int)(Options.ForceClickThreshold / 250.0 * (baseline - 20));
                g.DrawLine(threshold, left, y, right, y);
                using (var brush = new SolidBrush(Ui.Warning)) g.DrawString("阈值 " + Options.ForceClickThreshold, Ui.FontForDpi(this, Ui.Small), brush, right - 80, y - 20);
            }
            using (var pen = new Pen(Ui.Sage, 4))
            {
                g.DrawLines(pen, new[] { new Point(left, baseline), new Point(left + 52, baseline), new Point(left + 78, baseline - 30), new Point(left + 126, baseline - 75), new Point(left + 178, baseline - 140), new Point(right - 30, baseline - 165), new Point(right, baseline - 165) });
            }
            using (var brush = new SolidBrush(Ui.Muted)) g.DrawString("保持 " + Options.ForceClickDwellMs + " ms", Ui.FontForDpi(this, Ui.Small), brush, left + 124, baseline - 112);
        }
    }
}
