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
            if (c.DischargeAboveLimit && ac)
            {
                // below 80 use the wider band to avoid flipping between charge and discharge too often
                int startAbove = x < HardwareStop ? x + h : x + 1;
                if (p > startAbove) d.CalibrationOn = true;
                else if (p <= x) d.CalibrationOn = false;
                else d.CalibrationOn = i.CalibrationOnNow;
            }
            // ---- description ----
            int effective = c.DischargeAboveLimit || x >= HardwareStop ? x : HardwareStop;
            if (d.CalibrationOn)
            {
                d.Mode = "Discharging";
                d.Message = $"Discharging to {x}%";
            }
            else if (!ac)
            {
                d.Mode = "OnBattery";
                d.Message = $"On battery · limit {effective}%";
            }
            else if (p < effective && !(d.HardwareLimitOn && p >= HardwareStop))
            {
                d.Mode = "Charging";
                d.Message = $"Charging to {effective}%";
            }
            else
            {
                d.Mode = "Holding";
                d.Message = x >= HardwareStop ? $"Holding at {x}%"
                    : c.DischargeAboveLimit ? $"Holding around {x}%"
                    : $"Holding at {HardwareStop}% (hardware minimum)";
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
            string below80 = dischargeEnabled
                ? " The battery is held here by discharging and recharging around the limit, which adds some wear."
                : " Note: without \"Discharge when above limit\", charging still stops at 80% (the hardware minimum).";

            if (x < 20) return ("Not recommended. The battery would sit almost empty; deep discharge wears lithium cells fastest and leaves no runtime in a power cut." + below80, red);
            if (x < 40) return ("Very low. Little runtime when unplugged, and holding such a low level means many extra charge cycles." + below80, orange);
            if (x < 60) return ("Gentle on the battery if the laptop is almost always plugged in, but runtime away from the charger is short." + below80, amber);
            if (x < 80) return ("Healthy range for a laptop that stays plugged in." + below80, amber);
            if (x == 80) return ("Recommended. Uses Acer's built-in 80% stop: no extra cycles, good runtime, slower battery aging.", green);
            if (x <= 90) return ("Good balance between battery lifespan and runtime.", green);
            if (x < 100) return ("More wear if the laptop stays plugged in most of the day. Fine before a trip.", orange);
            return ("No limit: always charges to 100%. Fine if you mostly use the laptop on battery.", gray);
        }
    }
}
