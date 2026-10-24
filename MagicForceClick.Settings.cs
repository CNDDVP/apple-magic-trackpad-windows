using System;
using System.Drawing;
using System.Windows.Forms;

namespace MagicForceClick
{
    internal sealed class SettingsForm : Form
    {
        private readonly string _path;
        private readonly CheckBox _preview = new CheckBox(), _acceleration = new CheckBox(), _monitor = new CheckBox(), _pressed = new CheckBox();
        private readonly NumericUpDown _min = Number(0.1M, 8, 2, 0.05M), _max = Number(0.1M, 8, 2, 0.1M);
        private readonly NumericUpDown _mid = Number(0.1M, 100, 2, 0.1M), _power = Number(0.1M, 8, 2, 0.05M);
        private readonly NumericUpDown _threshold = Number(70, 250, 0, 5), _dwell = Number(0, 500, 0, 10), _rise = Number(0, 100, 0, 1), _immediate = Number(0, 250, 0, 5), _drag = Number(1, 64, 0, 1);
        private ForceClickOptions _options;
        public SettingsForm(string path)
        {
            _path = path;
            Text = "Magic Trackpad 1.1.1 设置"; ClientSize = new Size(570, 680);
            AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18), AutoScroll = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            Controls.Add(layout);
            AddCheck(layout, _preview, "深按文件预览（资源管理器 / Directory Opus）");
            AddCheck(layout, _acceleration, "S 型指针加速");
            AddCheck(layout, _monitor, "多屏物理速度补偿");
            AddCheck(layout, _pressed, "按住拖拽时也启用 S 型加速");
            AddValue(layout, "慢速增益（越小越稳）", _min);
            AddValue(layout, "快速最大增益（越大跨屏越快）", _max);
            AddValue(layout, "加速介入速度（越小越早加速）", _mid);
            AddValue(layout, "曲线指数", _power);
            AddValue(layout, "深按压力阈值（越小越易触发）", _threshold);
            AddValue(layout, "深按稳定时间（毫秒，过滤短促用力）", _dwell);
            AddValue(layout, "按下后需增加的力度（防按住即预览）", _rise);
            AddValue(layout, "快速深按压力阈值（0 为关闭）", _immediate);
            AddValue(layout, "拖拽取消预览：光标移动像素", _drag);
            var hint = new Label { Text = "保存后约 1 秒自动生效。无效配置会保留上次有效设置。\n“轻压跨屏”降低深按门槛，增强快速移动；“v1.1 手感”恢复原始参数。", AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(0, 14, 0, 10) };
            layout.Controls.Add(hint); layout.SetColumnSpan(hint, 2);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            var baseline = new Button { Text = "v1.1 手感", AutoSize = true };
            baseline.Click += delegate { LoadValues(new ForceClickOptions()); };
            var light = new Button { Text = "轻压跨屏", AutoSize = true };
            light.Click += delegate {
                _options.MinGain = (double)_min.Value; _options.MaxGain = (double)_max.Value;
                _options.EnableForceClick = _preview.Checked; _options.EnableAcceleration = _acceleration.Checked;
                _options.EnableMonitorCompensation = _monitor.Checked; _options.AccelerateWhilePressed = _pressed.Checked;
                _options.DragCancelCursorPixels = (int)_drag.Value;
                _options.ApplyLightCrossScreenPreset(); LoadValues(_options);
            };
            var previewOnly = new Button { Text = "仅保留深按", AutoSize = true };
            previewOnly.Click += delegate { _preview.Checked = true; _acceleration.Checked = _monitor.Checked = false; };
            var save = new Button { Text = "保存设置", AutoSize = true };
            save.Click += delegate { Save(); };
            buttons.Controls.Add(baseline); buttons.Controls.Add(light); buttons.Controls.Add(previewOnly); buttons.Controls.Add(save);
            layout.Controls.Add(buttons); layout.SetColumnSpan(buttons, 2);
            try { LoadValues(ForceClickOptions.Load(path)); }
            catch (Exception ex) { LoadValues(new ForceClickOptions()); MessageBox.Show("现有配置无效，已显示默认值：\n" + ex.Message); }
        }
        private static NumericUpDown Number(decimal min, decimal max, int decimals, decimal step)
        { return new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = step, Width = 110 }; }
        private static void AddCheck(TableLayoutPanel panel, CheckBox box, string text)
        { box.Text = text; box.AutoSize = true; box.Margin = new Padding(0, 6, 0, 6); panel.Controls.Add(box); panel.SetColumnSpan(box, 2); }
        private static void AddValue(TableLayoutPanel panel, string text, Control value)
        { panel.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(0, 9, 0, 9) }); panel.Controls.Add(value); }
        private void LoadValues(ForceClickOptions o)
        {
            _options = o; _preview.Checked = o.EnableForceClick; _acceleration.Checked = o.EnableAcceleration;
            _monitor.Checked = o.EnableMonitorCompensation; _pressed.Checked = o.AccelerateWhilePressed;
            _min.Value = (decimal)o.MinGain; _max.Value = (decimal)o.MaxGain; _mid.Value = (decimal)o.MidSpeed; _power.Value = (decimal)o.CurvePower;
            _threshold.Value = o.ForceClickThreshold; _dwell.Value = (decimal)o.ForceClickDwellMs; _rise.Value = o.ForceClickPressureRise; _immediate.Value = o.ForceClickImmediateThreshold; _drag.Value = o.DragCancelCursorPixels;
        }
        private void Save()
        {
            _options.EnableForceClick = _preview.Checked; _options.EnableAcceleration = _acceleration.Checked;
            _options.EnableMonitorCompensation = _monitor.Checked; _options.AccelerateWhilePressed = _pressed.Checked;
            _options.MinGain = (double)_min.Value; _options.MaxGain = (double)_max.Value; _options.MidSpeed = (double)_mid.Value; _options.CurvePower = (double)_power.Value;
            _options.ForceClickThreshold = (int)_threshold.Value; _options.ForceClickDwellMs = (double)_dwell.Value; _options.ForceClickPressureRise = (int)_rise.Value; _options.ForceClickImmediateThreshold = (int)_immediate.Value; _options.DragCancelCursorPixels = (int)_drag.Value;
            try { _options.Save(_path); MessageBox.Show("已保存。后台程序将在约 1 秒内加载。", Text); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "保存失败"); }
        }
    }
}
