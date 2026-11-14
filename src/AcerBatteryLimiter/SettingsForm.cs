using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace AcerBatteryLimiter
{
    /// <summary>
    /// Main window.
    ///   Status       — battery %, what the limiter is doing, charger / service, next full charge
    ///   Charge limit — "Limit charging", slider 0–100 with advice, "Discharge when above limit"
    ///   Maintenance  — "Full charge every N months", "Charge to 100% once"
    ///   Footer       — Open log · Reset to defaults · Apply · Close
    /// </summary>
    sealed class SettingsForm : Form
    {
        const int ContentWidth = 470;
        static readonly Color Muted = Color.FromArgb(96, 96, 96);
        static readonly Color Green = Color.FromArgb(30, 132, 73);
        static readonly Color Orange = Color.FromArgb(202, 81, 0);
        static readonly Color Red = Color.FromArgb(192, 57, 43);
        static readonly string[] MonthChoices = { "Off", "Every month", "Every 2 months", "Every 3 months (recommended)", "Every 4 months", "Every 5 months", "Every 6 months" };

        // status
        readonly Label _percent = new Label();
        readonly Label _mode = new Label();
        readonly Label _power = new Label();
        readonly Label _next = new Label();
        readonly Label _banner = new Label();
        // charge limit
        readonly CheckBox _chkLimit = new CheckBox();
        readonly TrackBar _slider = new TrackBar();
        readonly Label _value = new Label();
        readonly Panel _adviceBox = new Panel();
        readonly Label _advice = new Label();
        readonly CheckBox _chkDischarge = new CheckBox();
        // maintenance
        readonly ComboBox _months = new ComboBox();
        readonly Button _once = new Button();
        readonly Label _onceNote = new Label();
        // footer
        readonly LinkLabel _log = new LinkLabel();
        readonly Button _reset = new Button();
        readonly Button _apply = new Button();
        readonly Button _close = new Button();
        readonly Label _saved = new Label();

        readonly Timer _timer = new Timer { Interval = 3000 };
        bool _loading;
        bool _onceActive;

        public SettingsForm()
        {
            Text = Program.AppName;
            Icon = Program.AppIcon(new Size(32, 32));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Segoe UI", 9.75F);
            BackColor = Color.White;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(16, 12, 16, 12);

            var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            root.Controls.Add(Header());
            _banner.AutoSize = true; _banner.MaximumSize = new Size(ContentWidth, 0); _banner.MinimumSize = new Size(ContentWidth, 0);
            _banner.ForeColor = Color.White; _banner.BackColor = Red; _banner.Padding = new Padding(8, 6, 8, 6);
            _banner.Margin = new Padding(0, 0, 0, 8); _banner.Visible = false;
            root.Controls.Add(_banner);
            root.Controls.Add(StatusSection());
            root.Controls.Add(LimitSection());
            root.Controls.Add(MaintenanceSection());
            root.Controls.Add(Footer());
            Controls.Add(root);

            _chkLimit.CheckedChanged += (s, e) => { UpdateEnabled(); Changed(); };
            _chkDischarge.CheckedChanged += (s, e) => { UpdateAdvice(); Changed(); };
            _slider.ValueChanged += (s, e) => { _value.Text = _slider.Value + "%"; UpdateAdvice(); Changed(); };
            _months.SelectedIndexChanged += (s, e) => Changed();
            _once.Click += (s, e) => ToggleChargeOnce();
            _apply.Click += (s, e) => Save();
            _reset.Click += (s, e) => ResetToDefaults();
            _close.Click += (s, e) => Close();
            _log.LinkClicked += (s, e) => OpenLog();
            AcceptButton = _apply;
            CancelButton = _close;

            LoadConfig();
            _timer.Tick += (s, e) => RefreshStatus();
            _timer.Start();
            RefreshStatus();
        }

        // ------------------------------------------------------------------ layout

        Control Header()
        {
            var p = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
            var pic = new PictureBox { Image = Program.AppIcon(new Size(48, 48)).ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(48, 48), Margin = new Padding(0, 0, 12, 0) };
            var title = new Label { Text = Program.AppName, AutoSize = true, Font = new Font("Segoe UI Semibold", 15F), Margin = new Padding(0, 2, 0, 0) };
            var sub = new Label { Text = "Keep your Acer laptop battery at a healthy level", AutoSize = true, ForeColor = Muted };
            var texts = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            texts.Controls.Add(title); texts.Controls.Add(sub);
            p.Controls.Add(pic, 0, 0); p.Controls.Add(texts, 1, 0);
            return p;
        }

        static GroupBox Section(string title, Control body)
        {
            var g = new GroupBox
            {
                Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 8, 12, 10), Margin = new Padding(0, 0, 0, 10),
                MinimumSize = new Size(ContentWidth, 0), Font = new Font("Segoe UI Semibold", 9.75F),
            };
            body.Font = new Font("Segoe UI", 9.75F);
            body.Dock = DockStyle.Fill;
            g.Controls.Add(body);
            return g;
        }

        Control StatusSection()
        {
            var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _percent.Font = new Font("Segoe UI Semibold", 26F); _percent.AutoSize = true; _percent.Margin = new Padding(0, 0, 8, 0);
            _percent.Anchor = AnchorStyles.Left;
            var info = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left };
            _mode.Font = new Font("Segoe UI Semibold", 11F); _mode.AutoSize = true;
            _power.AutoSize = true; _power.ForeColor = Muted;
            _next.AutoSize = true; _next.ForeColor = Muted;
            info.Controls.Add(_mode); info.Controls.Add(_power); info.Controls.Add(_next);
            t.Controls.Add(_percent, 0, 0); t.Controls.Add(info, 1, 0);
            return Section("Status", t);
        }

        Control LimitSection()
        {
            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true };
            _chkLimit.Text = "Limit charging"; _chkLimit.AutoSize = true; _chkLimit.Font = new Font("Segoe UI Semibold", 10F);
            _chkLimit.Margin = new Padding(0, 2, 0, 2);
            t.Controls.Add(_chkLimit);

            var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(18, 0, 0, 0) };
            _slider.Minimum = 0; _slider.Maximum = 100; _slider.TickFrequency = 10; _slider.SmallChange = 1; _slider.LargeChange = 5;
            _slider.Width = 360; _slider.TickStyle = TickStyle.BottomRight;
            _value.AutoSize = true; _value.Font = new Font("Segoe UI Semibold", 16F); _value.Margin = new Padding(6, 2, 0, 0);
            _value.MinimumSize = new Size(64, 0);
            row.Controls.Add(_slider, 0, 0); row.Controls.Add(_value, 1, 0);
            var scale = new TableLayoutPanel { ColumnCount = 2, Width = 360, Height = 18, Margin = new Padding(0, -6, 0, 0) };
            scale.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); scale.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            scale.Controls.Add(new Label { Text = "0%", AutoSize = true, ForeColor = Muted, Anchor = AnchorStyles.Left, Font = new Font("Segoe UI", 8.25F) }, 0, 0);
            scale.Controls.Add(new Label { Text = "100%", AutoSize = true, ForeColor = Muted, Anchor = AnchorStyles.Right, Font = new Font("Segoe UI", 8.25F) }, 1, 0);
            row.Controls.Add(scale, 0, 1);
            t.Controls.Add(row);

            // advice: tinted box with a colored bar on the left
            _adviceBox.AutoSize = true; _adviceBox.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _adviceBox.Padding = new Padding(10, 6, 8, 6); _adviceBox.Margin = new Padding(18, 6, 0, 10);
            _adviceBox.MinimumSize = new Size(ContentWidth - 50, 0);
            _advice.AutoSize = true; _advice.MaximumSize = new Size(ContentWidth - 72, 0); _advice.Dock = DockStyle.Fill;
            _adviceBox.Controls.Add(_advice);
            _adviceBox.Paint += (s, e) => { using (var br = new SolidBrush(_advice.ForeColor)) e.Graphics.FillRectangle(br, 0, 0, 4, _adviceBox.Height); };
            t.Controls.Add(_adviceBox);

            _chkDischarge.Text = "Discharge when above limit"; _chkDischarge.AutoSize = true; _chkDischarge.Font = new Font("Segoe UI Semibold", 10F);
            t.Controls.Add(_chkDischarge);
            t.Controls.Add(Note("Drains the battery down to the limit while the charger stays connected (Acer calibration mode). Stops by itself at the limit.", 18));
            return Section("Charge limit", t);
        }

        Control MaintenanceSection()
        {
            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true };
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
            row.Controls.Add(new Label { Text = "Full charge:", AutoSize = true, Font = new Font("Segoe UI Semibold", 10F), Margin = new Padding(0, 6, 8, 0) });
            _months.DropDownStyle = ComboBoxStyle.DropDownList; _months.Width = 230;
            _months.Items.AddRange(MonthChoices);
            row.Controls.Add(_months);
            t.Controls.Add(row);
            t.Controls.Add(Note("Charges to 100% once in a while to balance the cells and keep the % reading accurate, then returns to the limit. Only runs while plugged in.", 0));

            var onceRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
            _once.AutoSize = true; _once.Padding = new Padding(8, 2, 8, 2); _once.MinimumSize = new Size(190, 30);
            _onceNote.AutoSize = true; _onceNote.ForeColor = Muted; _onceNote.MaximumSize = new Size(250, 0); _onceNote.Margin = new Padding(10, 2, 0, 0);
            _onceNote.Anchor = AnchorStyles.Left;
            onceRow.Controls.Add(_once, 0, 0); onceRow.Controls.Add(_onceNote, 1, 0);
            t.Controls.Add(onceRow);
            return Section("Maintenance", t);
        }

        Control Footer()
        {
            var p = new TableLayoutPanel { ColumnCount = 5, AutoSize = true, MinimumSize = new Size(ContentWidth, 0), Margin = new Padding(0, 2, 0, 0) };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _reset.Text = "Reset to defaults"; _apply.Text = "Apply"; _close.Text = "Close";
            foreach (var b in new[] { _reset, _apply, _close }) { b.AutoSize = true; b.MinimumSize = new Size(80, 30); b.Padding = new Padding(6, 0, 6, 0); }
            _log.Text = "Open log"; _log.AutoSize = true; _log.Anchor = AnchorStyles.Left; _log.Margin = new Padding(12, 0, 0, 0);
            _saved.AutoSize = true; _saved.ForeColor = Green; _saved.Anchor = AnchorStyles.Right; _saved.Margin = new Padding(0, 0, 8, 0);
            p.Controls.Add(_reset, 0, 0); p.Controls.Add(_log, 1, 0); p.Controls.Add(_saved, 2, 0); p.Controls.Add(_apply, 3, 0); p.Controls.Add(_close, 4, 0);
            return p;
        }

        static Label Note(string text, int indent) => new Label
        {
            Text = text, AutoSize = true, MaximumSize = new Size(ContentWidth - 40 - indent, 0), ForeColor = Muted,
            Margin = new Padding(indent, 2, 0, 0), Font = new Font("Segoe UI", 9F),
        };

        // ------------------------------------------------------------------ settings

        void LoadConfig()
        {
            _loading = true;
            var c = Store.LoadConfig();
            _chkLimit.Checked = c.LimitEnabled;
            _slider.Value = c.LimitPercent;
            _value.Text = c.LimitPercent + "%";
            _chkDischarge.Checked = c.DischargeAboveLimit;
            _months.SelectedIndex = Math.Min(c.FullChargeEveryMonths, MonthChoices.Length - 1);
            UpdateEnabled();
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
            bool on = _chkLimit.Checked;
            _slider.Enabled = on; _value.Enabled = on; _chkDischarge.Enabled = on; _months.Enabled = on;
            UpdateAdvice();
        }

        void UpdateAdvice()
        {
            string text; Color color;
            if (!_chkLimit.Checked) { text = "Charging is not limited: the battery always charges to 100%."; color = Muted; }
            else (text, color) = Policy.Advice(_slider.Value, _chkDischarge.Checked);
            _advice.Text = text;
            _advice.ForeColor = color;
            _adviceBox.BackColor = Color.FromArgb(22, color);
            _adviceBox.Invalidate();
        }

        void ResetToDefaults()
        {
            var d = new AppConfig();
            _chkLimit.Checked = d.LimitEnabled;
            _slider.Value = d.LimitPercent;
            _chkDischarge.Checked = d.DischargeAboveLimit;
            _months.SelectedIndex = d.FullChargeEveryMonths;
            Changed();
        }

        void Save()
        {
            try
            {
                var c = Store.LoadConfig();   // keeps fields this window does not edit
                c.LimitEnabled = _chkLimit.Checked;
                c.LimitPercent = _slider.Value;
                c.DischargeAboveLimit = _chkDischarge.Checked;
                c.FullChargeEveryMonths = _months.SelectedIndex;
                Store.SaveConfig(c);
                _apply.Enabled = false;
                _saved.Text = "Saved ✓";
            }
            catch (Exception ex) { SaveError(ex); }
        }

        void ToggleChargeOnce()
        {
            try
            {
                var c = Store.LoadConfig();
                if (_onceActive) c.ChargeOnceCancelledUtc = DateTime.UtcNow;
                else c.ChargeOnceRequestedUtc = DateTime.UtcNow;
                Store.SaveConfig(c);
                _onceActive = !_onceActive;
                UpdateOnceButton();
            }
            catch (Exception ex) { SaveError(ex); }
        }

        void SaveError(Exception ex) =>
            MessageBox.Show(this, "Could not save the settings:\n" + ex.Message + "\n\nTry reinstalling the app.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);

        void UpdateOnceButton()
        {
            _once.Text = _onceActive ? "Cancel full charge" : "Charge to 100% once";
            _onceNote.Text = _onceActive
                ? "Charging to 100% now. Ends by itself when you unplug the charger."
                : "Before a trip. Ends by itself when you unplug; back on the charger, the limit applies again.";
        }

        static void OpenLog()
        {
            try { Process.Start("notepad.exe", "\"" + Store.LogPath + "\""); } catch { }
        }

        // ------------------------------------------------------------------ status

        void RefreshStatus()
        {
            var svc = Program.ServiceState();
            var st = Store.LoadStatus();
            var ps = SystemInformation.PowerStatus;
            int pct = st != null && Program.StatusIsFresh(st) ? st.Percent : (int)Math.Round(ps.BatteryLifePercent * 100);
            bool ac = st != null && Program.StatusIsFresh(st) ? st.AcOnline : ps.PowerLineStatus == PowerLineStatus.Online;

            _percent.Text = pct + "%";
            _power.Text = (ac ? "Plugged in" : "On battery") + " · " + (svc == "Running" ? "Service running" : "Service " + (svc == "NotInstalled" ? "not installed" : svc.ToLowerInvariant()));
            _onceActive = st != null && st.FullCharge == "Once";
            UpdateOnceButton();
            _next.Text = st == null ? ""
                : st.FullCharge == "Scheduled" ? "Scheduled full charge in progress"
                : st.NextFullChargeUtc == DateTime.MinValue ? "Scheduled full charge: off"
                : "Next full charge: " + st.NextFullChargeUtc.ToLocalTime().ToString("d MMM yyyy");

            if (svc != "Running")
            {
                ShowBanner(svc == "NotInstalled"
                    ? "The background service is not installed, so nothing is applied. Please reinstall Acer Battery Limiter."
                    : "The background service is not running, so the limit is not applied. Restart the computer or reinstall the app.");
                SetMode("Not active", Red);
                return;
            }
            if (st != null && !st.Supported)
            {
                ShowBanner("This laptop does not expose Acer's battery health control (Acer Care Center WMI), so the app cannot change charging here.");
                SetMode("Not supported", Red);
                return;
            }
            _banner.Visible = false;
            if (st == null || !Program.StatusIsFresh(st)) { SetMode("Waiting for the service…", Muted); return; }
            SetMode(st.Message,
                st.Mode == "Discharging" ? Orange :
                st.Mode == "Error" ? Red :
                st.Mode == "NoLimit" || st.Mode == "OnBattery" ? Color.FromArgb(40, 40, 40) : Green);
        }

        void SetMode(string text, Color color) { _mode.Text = text; _mode.ForeColor = color; }
        void ShowBanner(string text) { _banner.Text = text; _banner.Visible = true; }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            base.OnFormClosed(e);
        }

        /// <summary>Render the window to a PNG without showing it (layout checks, README screenshots).</summary>
        public static void RenderTo(string path)
        {
            using (var f = new SettingsForm { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), ShowInTaskbar = false })
            {
                f.Show();
                Application.DoEvents();
                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                f.Close();
            }
        }
    }
}
