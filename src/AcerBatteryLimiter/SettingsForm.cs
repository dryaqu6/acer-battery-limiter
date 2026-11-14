using System;
using System.Drawing;
using System.Windows.Forms;

namespace AcerBatteryLimiter
{
    /// <summary>Main window: current status + the two options and the limit slider.</summary>
    sealed class SettingsForm : Form
    {
        readonly Label _percent = new Label();
        readonly Label _power = new Label();
        readonly Label _now = new Label();
        readonly Label _service = new Label();
        readonly Label _banner = new Label();
        readonly CheckBox _chkLimit = new CheckBox();
        readonly CheckBox _chkDischarge = new CheckBox();
        readonly TrackBar _slider = new TrackBar();
        readonly Label _value = new Label();
        readonly Label _advice = new Label();
        readonly Button _apply = new Button();
        readonly Button _reset = new Button();
        readonly Button _close = new Button();
        readonly Label _saved = new Label();
        readonly Timer _timer = new Timer { Interval = 3000 };
        AppConfig _config;
        bool _loading;

        public SettingsForm()
        {
            Text = Program.AppName;
            Icon = Program.AppIcon(new Size(32, 32));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Segoe UI", 9.5F);
            BackColor = Color.White;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(18);

            var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
            root.Controls.Add(Header());
            _banner.AutoSize = true; _banner.MaximumSize = new Size(440, 0); _banner.ForeColor = Color.White;
            _banner.BackColor = Color.FromArgb(192, 57, 43); _banner.Padding = new Padding(8); _banner.Visible = false;
            _banner.Margin = new Padding(0, 6, 0, 6);
            root.Controls.Add(_banner);
            root.Controls.Add(StatusBox());
            root.Controls.Add(SettingsBox());
            root.Controls.Add(Buttons());
            Controls.Add(root);

            _chkLimit.CheckedChanged += (s, e) => { UpdateEnabled(); Changed(); };
            _chkDischarge.CheckedChanged += (s, e) => { UpdateAdvice(); Changed(); };
            _slider.ValueChanged += (s, e) => { _value.Text = _slider.Value + "%"; UpdateAdvice(); Changed(); };
            _apply.Click += (s, e) => Save();
            _reset.Click += (s, e) => { _chkLimit.Checked = true; _slider.Value = 80; _chkDischarge.Checked = false; };
            _close.Click += (s, e) => Close();
            AcceptButton = _apply;
            CancelButton = _close;

            LoadConfig();
            _timer.Tick += (s, e) => RefreshStatus();
            _timer.Start();
            RefreshStatus();
        }

        Control Header()
        {
            var p = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
            var pic = new PictureBox { Image = Program.AppIcon(new Size(48, 48)).ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(48, 48), Margin = new Padding(0, 0, 10, 0) };
            var title = new Label { Text = Program.AppName, AutoSize = true, Font = new Font("Segoe UI Semibold", 15F) };
            var sub = new Label { Text = "Charge limit and discharge control for Acer laptops", AutoSize = true, ForeColor = Color.DimGray };
            var texts = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            texts.Controls.Add(title); texts.Controls.Add(sub);
            p.Controls.Add(pic, 0, 0); p.Controls.Add(texts, 1, 0);
            return p;
        }

        Control StatusBox()
        {
            var g = new GroupBox { Text = "Status", AutoSize = true, Padding = new Padding(10), Width = 460, Margin = new Padding(0, 0, 0, 10) };
            var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            _percent.Font = new Font("Segoe UI Semibold", 22F); _percent.AutoSize = true; _percent.Margin = new Padding(0, 0, 16, 0);
            var info = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            foreach (var l in new[] { _now, _power, _service }) { l.AutoSize = true; info.Controls.Add(l); }
            _now.Font = new Font("Segoe UI Semibold", 10.5F);
            _service.ForeColor = Color.DimGray;
            t.Controls.Add(_percent, 0, 0); t.Controls.Add(info, 1, 0);
            g.Controls.Add(t);
            return g;
        }

        Control SettingsBox()
        {
            var g = new GroupBox { Text = "Settings", AutoSize = true, Padding = new Padding(10), Width = 460, Margin = new Padding(0, 0, 0, 10) };
            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };

            _chkLimit.Text = "Limit charging"; _chkLimit.AutoSize = true; _chkLimit.Font = new Font("Segoe UI Semibold", 10F);
            t.Controls.Add(_chkLimit);

            var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(16, 4, 0, 0) };
            _slider.Minimum = 0; _slider.Maximum = 100; _slider.TickFrequency = 10; _slider.SmallChange = 1; _slider.LargeChange = 5;
            _slider.Width = 330; _slider.TickStyle = TickStyle.BottomRight;
            _value.AutoSize = true; _value.Font = new Font("Segoe UI Semibold", 16F); _value.Margin = new Padding(8, 4, 0, 0);
            row.Controls.Add(_slider, 0, 0); row.Controls.Add(_value, 1, 0);
            t.Controls.Add(row);

            _advice.AutoSize = true; _advice.MaximumSize = new Size(420, 0); _advice.Margin = new Padding(16, 0, 0, 10);
            t.Controls.Add(_advice);

            _chkDischarge.Text = "Discharge when above limit"; _chkDischarge.AutoSize = true; _chkDischarge.Font = new Font("Segoe UI Semibold", 10F);
            t.Controls.Add(_chkDischarge);
            var note = new Label
            {
                Text = "Drains the battery down to the limit while the charger stays connected (uses Acer's calibration mode). Stops automatically at the limit or when you unplug.",
                AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Color.DimGray, Margin = new Padding(16, 2, 0, 0),
            };
            t.Controls.Add(note);
            g.Controls.Add(t);
            return g;
        }

        Control Buttons()
        {
            var p = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Dock = DockStyle.Fill };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            foreach (var b in new[] { _reset, _apply, _close }) { b.AutoSize = true; b.Padding = new Padding(10, 2, 10, 2); }
            _reset.Text = "Reset to 80%"; _apply.Text = "Apply"; _close.Text = "Close";
            _saved.AutoSize = true; _saved.ForeColor = Color.FromArgb(30, 132, 73); _saved.Anchor = AnchorStyles.Right; _saved.Margin = new Padding(0, 8, 8, 0);
            p.Controls.Add(_reset, 0, 0); p.Controls.Add(_saved, 1, 0); p.Controls.Add(_apply, 2, 0); p.Controls.Add(_close, 3, 0);
            return p;
        }

        void LoadConfig()
        {
            _loading = true;
            _config = Store.LoadConfig();
            _chkLimit.Checked = _config.LimitEnabled;
            _slider.Value = _config.LimitPercent;
            _value.Text = _config.LimitPercent + "%";
            _chkDischarge.Checked = _config.DischargeAboveLimit;
            UpdateEnabled();
            UpdateAdvice();
            _apply.Enabled = false;
            _loading = false;
        }

        void Changed()
        {
            if (_loading) return;
            _apply.Enabled = true;
            _saved.Text = "";
        }

        void UpdateEnabled()
        {
            _slider.Enabled = _chkLimit.Checked;
            _value.Enabled = _chkLimit.Checked;
            _chkDischarge.Enabled = _chkLimit.Checked;
            UpdateAdvice();
        }

        void UpdateAdvice()
        {
            if (!_chkLimit.Checked)
            {
                _advice.Text = "Charging is not limited: the battery charges to 100%.";
                _advice.ForeColor = Color.DimGray;
                return;
            }
            var (text, color) = Policy.Advice(_slider.Value, _chkDischarge.Checked);
            _advice.Text = text;
            _advice.ForeColor = color;
        }

        void Save()
        {
            _config.LimitEnabled = _chkLimit.Checked;
            _config.LimitPercent = _slider.Value;
            _config.DischargeAboveLimit = _chkDischarge.Checked;
            try
            {
                Store.SaveConfig(_config);
                _apply.Enabled = false;
                _saved.Text = "Saved ✓";
                RefreshStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the settings:\n" + ex.Message + "\n\nTry reinstalling the app.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void RefreshStatus()
        {
            var svc = Program.ServiceState();
            var st = Store.LoadStatus();
            var ps = SystemInformation.PowerStatus;
            int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
            bool ac = ps.PowerLineStatus == PowerLineStatus.Online;

            _percent.Text = pct + "%";
            _power.Text = ac ? "Plugged in" : "On battery";

            if (svc != "Running")
            {
                ShowBanner(svc == "NotInstalled"
                    ? "The background service is not installed. Please reinstall Acer Battery Limiter."
                    : "The background service is not running, so the limit is not applied. Restart the computer or reinstall the app.");
                _now.Text = "Not active";
                _service.Text = "Background service: " + svc;
                return;
            }
            _service.Text = "Background service: running";
            if (st != null && !st.Supported)
            {
                ShowBanner("This laptop does not expose Acer's battery health control (Acer Care Center WMI). The app cannot change charging here.");
                _now.Text = "Not supported";
                return;
            }
            if (st == null || !Program.StatusIsFresh(st))
            {
                HideBanner();
                _now.Text = "Waiting for the background service…";
                return;
            }
            HideBanner();
            _now.Text = st.Message;
            _now.ForeColor = st.Mode == "Discharging" ? Color.FromArgb(211, 84, 0) : st.Mode == "Error" ? Color.FromArgb(192, 57, 43) : Color.FromArgb(30, 132, 73);
        }

        void ShowBanner(string text) { _banner.Text = text; _banner.Visible = true; }
        void HideBanner() { _banner.Visible = false; }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            base.OnFormClosed(e);
        }
    }
}
