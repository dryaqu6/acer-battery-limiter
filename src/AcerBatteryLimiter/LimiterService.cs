using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;

namespace AcerBatteryLimiter
{
    /// <summary>
    /// Windows service (LocalSystem). Reads config.json, applies the policy to the hardware,
    /// writes status.json. Re-checks every PollSeconds, on config change and on power events.
    /// </summary>
    public sealed class LimiterService : ServiceBase
    {
        public const string Name = "AcerBatteryLimiter";

        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        volatile bool _stopping;
        Thread _worker;
        FileSystemWatcher _watcher;
        string _lastMode = "";

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
            Store.Log("Service started");
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
                Thread.Sleep(500);   // let a burst of file / power events settle
            }
        }

        /// <summary>One control step. Also used by "--apply-once" for testing.</summary>
        public void ApplyOnce(AppConfig c)
        {
            if (!AcerWmi.IsSupported())
            {
                Store.SaveStatus(new AppStatus { UpdatedUtc = DateTime.UtcNow, Supported = false, Mode = "NotSupported", Message = "Not supported on this device" });
                if (_lastMode != "NotSupported") Store.Log("Acer BatteryControl WMI class not found: not supported");
                _lastMode = "NotSupported";
                return;
            }
            var b = AcerWmi.ReadBattery();
            var (limitOn, calOn) = AcerWmi.GetState();
            var d = Policy.Decide(c, b, limitOn, calOn);

            if (d.HardwareLimitOn != limitOn) AcerWmi.SetChargeLimit(d.HardwareLimitOn);
            if (d.CalibrationOn != calOn) AcerWmi.SetCalibration(d.CalibrationOn);
            if (d.HardwareLimitOn != limitOn || d.CalibrationOn != calOn || d.Mode != _lastMode)
                Store.Log($"{b.Percent}% AC={(b.AcOnline ? "on" : "off")} limit={(c.LimitEnabled ? c.LimitPercent + "%" : "off")} discharge={(c.DischargeAboveLimit ? "on" : "off")} -> hw80={(d.HardwareLimitOn ? 1 : 0)} drain={(d.CalibrationOn ? 1 : 0)} | {d.Message}");
            _lastMode = d.Mode;

            Store.SaveStatus(new AppStatus
            {
                UpdatedUtc = DateTime.UtcNow,
                Supported = true,
                Percent = b.Percent,
                AcOnline = b.AcOnline,
                HardwareLimitOn = d.HardwareLimitOn,
                CalibrationOn = d.CalibrationOn,
                Mode = d.Mode,
                Message = d.Message,
            });
        }
    }
}
