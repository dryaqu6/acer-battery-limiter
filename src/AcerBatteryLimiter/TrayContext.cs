using System;
using System.Drawing;
using System.Windows.Forms;

namespace AcerBatteryLimiter
{
    /// <summary>Tray icon: battery % and what the limiter is doing; notifications when discharging starts/ends.</summary>
    sealed class TrayContext : ApplicationContext
    {
        readonly NotifyIcon _icon;
        readonly Timer _timer;
        string _lastMode;

        public TrayContext()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open " + Program.AppName, null, (s, e) => Program.OpenWindow());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Hide tray icon", null, (s, e) => ExitThread());
            menu.Items[0].Font = new Font(menu.Items[0].Font, FontStyle.Bold);

            _icon = new NotifyIcon
            {
                Icon = Program.AppIcon(SystemInformation.SmallIconSize),
                Text = Program.AppName,
                ContextMenuStrip = menu,
                Visible = true,
            };
            _icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Program.OpenWindow(); };

            _timer = new Timer { Interval = 5000 };
            _timer.Tick += (s, e) => Refresh();
            _timer.Start();
            Refresh();
        }

        void Refresh()
        {
            var st = Store.LoadStatus();
            string text;
            if (Program.ServiceState() != "Running") text = "Background service is not running";
            else if (st == null || !Program.StatusIsFresh(st)) text = "Waiting for the background service…";
            else if (!st.Supported) text = "Not supported on this device";
            else text = $"{st.Percent}% · {st.Message}";

            // NotifyIcon.Text is limited to 63 characters
            var tip = Program.AppName + "\n" + text;
            _icon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

            if (st != null && Program.StatusIsFresh(st))
            {
                if (_lastMode != null && st.Mode != _lastMode)
                {
                    if (st.Mode == "Discharging")
                        _icon.ShowBalloonTip(5000, Program.AppName, $"Battery at {st.Percent}%. {st.Message} while plugged in.", ToolTipIcon.Info);
                    else if (_lastMode == "Discharging")
                        _icon.ShowBalloonTip(5000, Program.AppName, $"Discharge finished at {st.Percent}%. {st.Message}.", ToolTipIcon.Info);
                }
                _lastMode = st.Mode;
            }
        }

        protected override void ExitThreadCore()
        {
            _timer.Stop();
            _icon.Visible = false;
            _icon.Dispose();
            base.ExitThreadCore();
        }
    }
}
