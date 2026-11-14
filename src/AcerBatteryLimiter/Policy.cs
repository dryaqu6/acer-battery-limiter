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

    /// <summary>
    /// Pure decision logic (no WMI), so it is easy to reason about.
    /// The hardware can only stop charging at a fixed 80%. Other levels are emulated:
    ///   limit &gt; 80: let it charge, switch the 80% stop on once the limit is reached (battery is already above 80, so charging stops),
    ///               switch it off again when the battery drops below limit - hysteresis.
    ///   limit &lt; 80: charging still stops at 80; holding a lower level needs "Discharge when above limit".
    ///   Discharge: calibration mode drains the battery while plugged in; start above the limit, stop at the limit.
    /// </summary>
    public static class Policy
    {
        public const int HardwareStop = 80;

        public static Decision Decide(AppConfig c, BatteryReading b, bool limitOnNow, bool calibrationOnNow)
        {
            var d = new Decision();
            int x = c.LimitPercent, h = c.HysteresisPercent, p = b.Percent;

            if (!c.LimitEnabled || x >= 100)
            {
                d.HardwareLimitOn = false;
                d.CalibrationOn = false;
                d.Mode = "NoLimit";
                d.Message = b.AcOnline ? (p >= 100 ? "Full (no limit)" : "Charging (no limit)") : "On battery (no limit)";
                return d;
            }

            // ---- charging side ----
            if (x <= HardwareStop)
            {
                d.HardwareLimitOn = true;
            }
            else
            {
                if (p >= x) d.HardwareLimitOn = true;
                else if (p <= x - h) d.HardwareLimitOn = false;
                else d.HardwareLimitOn = limitOnNow;   // inside the band: keep what we have
            }

            // ---- discharging side ----
            if (!b.AcOnline)
            {
                d.CalibrationOn = false;
            }
            else if (c.DischargeAboveLimit)
            {
                // below 80 use the wider band to avoid flipping between charge and discharge too often
                int startAbove = x < HardwareStop ? x + h : x + 1;
                if (p > startAbove) d.CalibrationOn = true;
                else if (p <= x) d.CalibrationOn = false;
                else d.CalibrationOn = calibrationOnNow;
            }
            else
            {
                d.CalibrationOn = false;
            }

            // ---- description ----
            int effective = c.DischargeAboveLimit || x >= HardwareStop ? x : HardwareStop;
            if (!b.AcOnline)
            {
                d.Mode = "OnBattery";
                d.Message = $"On battery · limit {effective}%";
            }
            else if (d.CalibrationOn)
            {
                d.Mode = "Discharging";
                d.Message = $"Discharging to {x}%";
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
        public static (string text, System.Drawing.Color color) Advice(int x, bool dischargeEnabled)
        {
            var red = System.Drawing.Color.FromArgb(192, 57, 43);
            var orange = System.Drawing.Color.FromArgb(211, 84, 0);
            var amber = System.Drawing.Color.FromArgb(176, 122, 0);
            var green = System.Drawing.Color.FromArgb(30, 132, 73);
            var gray = System.Drawing.Color.FromArgb(90, 90, 90);
            string below80 = dischargeEnabled
                ? " The battery is held here by discharging and recharging around the limit, which adds some wear."
                : " Without \"Discharge when above limit\" charging still stops at 80% (hardware minimum).";

            if (x < 20) return ("Not recommended. The battery would sit almost empty; deep discharge wears lithium cells the fastest and leaves no runtime if the power goes out." + below80, red);
            if (x < 40) return ("Very low. Little runtime when unplugged, and holding such a low level means many extra charge/discharge cycles." + below80, orange);
            if (x < 60) return ("Gentle on the battery if the laptop is plugged in almost all the time, but runtime away from the charger is short." + below80, amber);
            if (x < 80) return ("Healthy range for a laptop that stays plugged in." + below80, amber);
            if (x == 80) return ("Recommended. Uses Acer's built-in 80% stop: no extra cycles, good runtime, slower battery aging.", green);
            if (x <= 90) return ("Good balance between battery lifespan and runtime.", green);
            if (x < 100) return ("More wear if the laptop stays plugged in most of the day. Fine before a trip.", orange);
            return ("No limit: always charges to 100%. Fine if you mostly use the laptop on battery.", gray);
        }
    }
}
