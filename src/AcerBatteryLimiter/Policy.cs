using System;
using System.Drawing;

namespace AcerBatteryLimiter
{
    /// <summary>What the service should set on the hardware, plus a description for the user.</summary>
    public sealed class Decision
    {
        public bool HardwareLimitOn;
        public bool CalibrationOn;
        public string Mode;
        public string Message;
    }

    /// <summary>Inputs for one control step.</summary>
    public sealed class PolicyInput
    {
        public AppConfig Config;
        public BatteryReading Battery;
        public bool LimitOnNow;
        public bool CalibrationOnNow;
        /// <summary>Seconds since discharge was switched on (0 when off).</summary>
        public double DischargeSeconds;
        /// <summary>"" / "Scheduled" / "Once".</summary>
        public string FullCharge = "";
    }

    /// <summary>
    /// Pure decision logic (no WMI).
    /// The hardware can only stop charging at a fixed 80%. Other levels are emulated:
    ///   limit &gt; 80: let it charge, switch the 80% stop on once the limit is reached (battery is already above 80, so charging stops),
    ///               switch it off again when the battery drops below limit - hysteresis.
    ///   limit &lt; 80: charging still stops at 80; holding a lower level needs "Discharge when above limit".
    ///   Discharge: calibration mode drains the battery while plugged in; start above the limit, stop at the limit.
    ///   While calibration is on the hardware reports "charger disconnected", so that report is ignored then.
    /// </summary>
    public static class Policy
    {
        public const int HardwareStop = 80;

        /// <summary>Charger connected, ignoring the false "disconnected" report caused by discharge mode.</summary>
        public static bool ChargerConnected(PolicyInput i) => i.Battery.AcOnline || i.CalibrationOnNow;

        public static Decision Decide(PolicyInput i)
        {
            var d = DecideCore(i);
            // never switch discharge off too soon after switching it on (in any mode)
            if (i.CalibrationOnNow && !d.CalibrationOn && i.DischargeSeconds < i.Config.MinDischargeSeconds)
            {
                d.CalibrationOn = true;
                d.Mode = "Discharging";
                d.Message = "Finishing discharge…";
            }
            return d;
        }

        static Decision DecideCore(PolicyInput i)
        {
            var c = i.Config;
            var b = i.Battery;
            var d = new Decision();
            int x = c.LimitPercent, h = c.HysteresisPercent, p = b.Percent;
            bool ac = ChargerConnected(i);

            // ---- full charge (scheduled or one-time): no limit, no discharge ----
            if (!string.IsNullOrEmpty(i.FullCharge))
            {
                d.HardwareLimitOn = false;
                d.CalibrationOn = false;
                d.Mode = "FullCharge";
                string what = i.FullCharge == "Once" ? "one-time full charge" : "scheduled full charge";
                d.Message = !ac ? $"On battery · {what} waiting"
                    : p >= 100 ? (i.FullCharge == "Once" ? "Full · stays at 100% until unplugged" : "Full")
                    : $"Charging to 100% ({what})";
                return d;
            }

            if (!c.LimitEnabled || x >= 100)
            {
                d.Mode = "NoLimit";
                d.Message = ac ? (p >= 100 ? "Full (no limit)" : "Charging (no limit)") : "On battery (no limit)";
                return d;
            }

            // ---- charging side ----
            if (x <= HardwareStop) d.HardwareLimitOn = true;
            else if (p >= x) d.HardwareLimitOn = true;
            else if (p <= x - h) d.HardwareLimitOn = false;
            else d.HardwareLimitOn = i.LimitOnNow;   // inside the band: keep what we have

            // ---- discharging side ----
            // Acer calibration mode tops the battery up to 100% before it drains, so only start it when the
            // battery is already (almost) full; stop at the limit. Below 80% the battery would charge back
            // to 80% anyway, so 80% is the lowest discharge target.
            int target = Math.Max(x, HardwareStop);
            if (c.DischargeAboveLimit && ac && target < 100)
            {
                if (!i.CalibrationOnNow) d.CalibrationOn = p >= Math.Max(c.DischargeStartPercent, target + 1);
                else d.CalibrationOn = p > target;
            }

            // ---- description ----
            int effective = Math.Max(x, HardwareStop);
            if (d.CalibrationOn)
            {
                d.Mode = "Discharging";
                d.Message = b.Charging && p < 100 ? $"Topping up to 100%, then discharging to {target}%" : $"Discharging to {target}%";
            }
            else if (!ac)
            {
                d.Mode = "OnBattery";
                d.Message = $"On battery · limit {effective}%";
            }
            else if (p < effective && (b.Charging || !d.HardwareLimitOn))   // the 80% stop has its own small band: trust the real charging flag
            {
                d.Mode = "Charging";
                d.Message = $"Charging to {effective}%";
            }
            else
            {
                d.Mode = "Holding";
                d.Message = x < HardwareStop ? $"Holding at {HardwareStop}% (lowest level this laptop supports)"
                    : p > effective + 1 ? $"Above the {effective}% limit · not charging"
                    : $"Holding at {effective}%";
            }
            return d;
        }

        /// <summary>Advice shown under the slider, for a laptop used in a normal way.</summary>
        public static (string text, Color color) Advice(int x, bool dischargeEnabled)
        {
            var red = Color.FromArgb(192, 57, 43);
            var orange = Color.FromArgb(202, 81, 0);
            var amber = Color.FromArgb(156, 106, 0);
            var green = Color.FromArgb(30, 132, 73);
            var gray = Color.FromArgb(96, 96, 96);
            const string hw = "This laptop can only stop charging at 80% or higher, so the battery will be held at 80%.";

            if (x < 20) return (hw + " Levels this low are not recommended anyway: the battery would sit almost empty and leave no runtime in a power cut.", red);
            if (x < 80) return (hw + " Holding a lower level would need a smart plug that switches the charger off.", amber);
            if (x == 80) return ("Recommended. Uses Acer's built-in 80% stop: no extra cycles, good runtime, slower battery aging.", green);
            if (x <= 90) return ("Good balance between battery lifespan and runtime.", green);
            if (x < 100) return ("More wear if the laptop stays plugged in most of the day. Fine before a trip.", orange);
            return ("No limit: always charges to 100%. Fine if you mostly use the laptop on battery.", gray);
        }
    }
}
