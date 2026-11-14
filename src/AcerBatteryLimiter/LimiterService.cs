using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;

namespace AcerBatteryLimiter
{
    /// <summary>
    /// Windows service (LocalSystem). Reads config.json, applies the policy to the hardware,
    /// writes status.json and state.json. Re-checks every PollSeconds, on config change and on power events.
    /// </summary>
    public sealed class LimiterService : ServiceBase
    {
        public const string Name = "AcerBatteryLimiter";
        const double DaysPerMonth = 30.44;

        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        readonly object _stepLock = new object();
        volatile bool _stopping;
        Thread _worker;
        FileSystemWatcher _watcher;
        string _lastLine = "";
        // last command sent per function, so a slow controller is not flooded with repeats
        bool? _limitCmd, _calCmd;
        DateTime _limitCmdUtc, _calCmdUtc;

        public LimiterService()
        {
            ServiceName = Name;
            CanHandlePowerEvent = true;
            CanStop = true;
            CanShutdown = true;
            AutoLog = false;
        }

        protected override void OnStart(string[] args)
        {
            Directory.CreateDirectory(Store.Dir);
            Store.Log("Service started (version " + typeof(LimiterService).Assembly.GetName().Version + ")");
            _watcher = new FileSystemWatcher(Store.Dir, "config.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            _watcher.Changed += (s, e) => _wake.Set();
            _watcher.Created += (s, e) => _wake.Set();
            _watcher.Renamed += (s, e) => _wake.Set();
            _watcher.EnableRaisingEvents = true;
            _worker = new Thread(Loop) { IsBackground = true, Name = "limiter" };
            _worker.Start();
        }

        protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
        {
            _wake.Set();   // plug/unplug, resume from sleep
            return true;
        }

        protected override void OnStop() => Shutdown("Service stopped");
        protected override void OnShutdown() => Shutdown("Windows shutting down");

        void Shutdown(string why)
        {
            _stopping = true;
            _wake.Set();
            _worker?.Join(5000);
            // never leave the battery draining while nothing is watching it
            try { AcerWmi.SetCalibration(false); } catch { }
            var st = Store.LoadState();
            if (st != null) { st.DischargeSinceUtc = Time.Never; Store.SaveState(st); }
            Store.Log(why + ": discharge switched off");
        }

        void Loop()
        {
            while (!_stopping)
            {
                var c = Store.LoadConfig();
                try { ApplyOnce(c); }
                catch (Exception ex)
                {
                    Store.Log("ERROR " + ex.Message);
                    Store.SaveStatus(new AppStatus { UpdatedUtc = DateTime.UtcNow, Supported = true, Mode = "Error", Message = "Error: " + ex.Message });
                }
                _wake.WaitOne(TimeSpan.FromSeconds(c.PollSeconds));
                Thread.Sleep(1000);   // let a burst of file / power events settle
            }
        }

        /// <summary>One control step. Also used by "--apply-once" for testing.</summary>
        public void ApplyOnce(AppConfig c)
        {
            lock (_stepLock)
            {
                if (!AcerWmi.IsSupported())
                {
                    Store.SaveStatus(new AppStatus { UpdatedUtc = DateTime.UtcNow, Supported = false, Mode = "NotSupported", Message = "Not supported on this device" });
                    LogOnce("Acer BatteryControl WMI class not found: not supported");
                    return;
                }

                var now = DateTime.UtcNow;
                var st = Store.LoadState() ?? new ServiceState();
                if (st.LastFullChargeUtc == Time.Never) st.LastFullChargeUtc = now;   // first run: the schedule counts from now
                var b = AcerWmi.ReadBattery();
                var (limitOn, calOn) = AcerWmi.GetState();
                // the controller may report the old state for a while after a command: trust the command meanwhile
                if (_limitCmd.HasValue && (now - _limitCmdUtc).TotalSeconds < c.CommandSettleSeconds) limitOn = _limitCmd.Value;
                if (_calCmd.HasValue && (now - _calCmdUtc).TotalSeconds < c.CommandSettleSeconds) calOn = _calCmd.Value;
                if (calOn && st.DischargeSinceUtc == Time.Never) st.DischargeSinceUtc = now;   // e.g. after a restart
                var input = new PolicyInput
                {
                    Config = c, Battery = b, LimitOnNow = limitOn, CalibrationOnNow = calOn,
                    DischargeSeconds = calOn ? (now - st.DischargeSinceUtc).TotalSeconds : 0,
                };
                bool ac = Policy.ChargerConnected(input);

                UpdateFullCharge(c, st, b, ac, now);
                input.FullCharge = st.FullCharge;

                var d = Policy.Decide(input);
                if (d.HardwareLimitOn != limitOn)
                {
                    AcerWmi.SetChargeLimit(d.HardwareLimitOn);
                    _limitCmd = d.HardwareLimitOn; _limitCmdUtc = now;
                }
                if (d.CalibrationOn != calOn)
                {
                    AcerWmi.SetCalibration(d.CalibrationOn);
                    _calCmd = d.CalibrationOn; _calCmdUtc = now;
                    st.DischargeSinceUtc = d.CalibrationOn ? now : Time.Never;
                }
                Store.SaveState(st);

                LogOnce($"{b.Percent}% charger={(ac ? "on" : "off")} limit={(c.LimitEnabled ? c.LimitPercent + "%" : "off")} " +
                        $"discharge={(c.DischargeAboveLimit ? "on" : "off")} full={(st.FullCharge == "" ? "-" : st.FullCharge)} -> " +
                        $"hw80={(d.HardwareLimitOn ? 1 : 0)} drain={(d.CalibrationOn ? 1 : 0)} | {d.Message}");

                Store.SaveStatus(new AppStatus
                {
                    UpdatedUtc = now,
                    Supported = true,
                    Percent = b.Percent,
                    AcOnline = ac,
                    HardwareLimitOn = d.HardwareLimitOn,
                    CalibrationOn = d.CalibrationOn,
                    Mode = d.Mode,
                    Message = d.Message,
                    FullCharge = st.FullCharge,
                    NextFullChargeUtc = NextFullCharge(c, st),
                });
            }
        }

        static DateTime NextFullCharge(AppConfig c, ServiceState st) =>
            c.FullChargeEveryMonths > 0 && c.LimitEnabled && c.LimitPercent < 100
                ? st.LastFullChargeUtc.AddDays(c.FullChargeEveryMonths * DaysPerMonth)
                : Time.Never;

        /// <summary>Starts / ends the scheduled and one-time full charge.</summary>
        void UpdateFullCharge(AppConfig c, ServiceState st, BatteryReading b, bool ac, DateTime now)
        {
            // "Charge to 100% once" clicked (each click handled once)
            if (c.ChargeOnceRequestedUtc > st.HandledChargeOnceUtc)
            {
                st.HandledChargeOnceUtc = c.ChargeOnceRequestedUtc;
                st.FullCharge = "Once";
                st.FullChargeSinceUtc = now;
                st.FullChargeSawCharger = false;
                Store.Log("One-time full charge requested");
            }
            if (c.ChargeOnceCancelledUtc > st.HandledCancelUtc)
            {
                st.HandledCancelUtc = c.ChargeOnceCancelledUtc;
                if (st.FullCharge == "Once") { st.FullCharge = ""; Store.Log("One-time full charge cancelled"); }
            }

            // scheduled full charge is due (only while plugged in)
            var due = NextFullCharge(c, st);
            if (st.FullCharge == "" && due != Time.Never && now >= due && ac)
            {
                st.FullCharge = "Scheduled";
                st.FullChargeSinceUtc = now;
                Store.Log($"Scheduled full charge started (every {c.FullChargeEveryMonths} month(s))");
            }

            if (st.FullCharge == "") return;
            if (ac) st.FullChargeSawCharger = true;
            bool full = b.Percent >= 100 || (b.Percent >= 97 && ac && !b.Charging && (now - st.FullChargeSinceUtc).TotalMinutes > 20);
            if (full && st.LastFullChargeUtc < st.FullChargeSinceUtc) st.LastFullChargeUtc = now;   // counts as the periodic full charge

            if (st.FullCharge == "Scheduled" && full)
            {
                st.FullCharge = "";
                Store.Log($"Scheduled full charge finished at {b.Percent}%");
            }
            else if (st.FullCharge == "Once" && !ac && st.FullChargeSawCharger)
            {
                st.FullCharge = "";
                Store.Log($"One-time full charge ended: unplugged at {b.Percent}%");
            }
            else if (!c.LimitEnabled || c.LimitPercent >= 100)
            {
                st.FullCharge = "";   // nothing to override
            }
        }

        void LogOnce(string line)
        {
            // log only when something changes (the percentage alone is not a change)
            var key = line.Substring(line.IndexOf(' ') + 1);
            if (key == _lastLine) return;
            _lastLine = key;
            Store.Log(line);
        }
    }
}
