using System;
using System.Linq;
using System.Management;

namespace AcerBatteryLimiter
{
    /// <summary>Battery reading as Windows reports it.</summary>
    public sealed class BatteryReading
    {
        public bool Present;
        public int Percent;          // 0..100, same number as the Windows battery icon
        public bool AcOnline;
        public bool Charging;
        public bool Discharging;
        public int DischargeRateMw;
    }

    /// <summary>
    /// Acer Care Center battery health interface: WMI class root\wmi BatteryControl.
    /// Function 1 = stop charging at 80% (fixed, done by the embedded controller).
    /// Function 2 = calibration (the battery discharges even while the charger is connected).
    /// Writing needs administrator rights (the Windows service runs as LocalSystem).
    /// </summary>
    public static class AcerWmi
    {
        const byte FunctionChargeLimit = 1;
        const byte FunctionCalibration = 2;

        static ManagementObject GetControl()
        {
            using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM BatteryControl"))
                return searcher.Get().Cast<ManagementObject>().FirstOrDefault();
        }

        public static bool IsSupported()
        {
            try { using (var o = GetControl()) return o != null; }
            catch { return false; }
        }

        /// <summary>Returns (chargeLimitOn, calibrationOn).</summary>
        public static (bool limitOn, bool calibrationOn) GetState()
        {
            using (var o = GetControl())
            {
                if (o == null) throw new NotSupportedException("BatteryControl not found");
                var inParams = o.GetMethodParameters("GetBatteryHealthControlStatus");
                inParams["uBatteryNo"] = (byte)1;
                inParams["uFunctionQuery"] = (byte)1;
                inParams["uReserved"] = new byte[] { 0, 0 };
                var outParams = o.InvokeMethod("GetBatteryHealthControlStatus", inParams, null);
                var status = (byte[])outParams["uFunctionStatus"];
                return (status.Length > 0 && status[0] == 1, status.Length > 1 && status[1] == 1);
            }
        }

        public static void SetChargeLimit(bool on) => Set(FunctionChargeLimit, on);
        public static void SetCalibration(bool on) => Set(FunctionCalibration, on);

        static void Set(byte function, bool on)
        {
            using (var o = GetControl())
            {
                if (o == null) throw new NotSupportedException("BatteryControl not found");
                var inParams = o.GetMethodParameters("SetBatteryHealthControl");
                inParams["uBatteryNo"] = (byte)1;
                inParams["uFunctionMask"] = function;
                inParams["uFunctionStatus"] = on ? (byte)1 : (byte)0;
                inParams["uReservedIn"] = new byte[] { 0, 0, 0, 0, 0 };
                o.InvokeMethod("SetBatteryHealthControl", inParams, null);
            }
        }

        public static BatteryReading ReadBattery()
        {
            var r = new BatteryReading();
            using (var s = new ManagementObjectSearcher(@"root\cimv2", "SELECT EstimatedChargeRemaining FROM Win32_Battery"))
            {
                foreach (ManagementObject b in s.Get())
                {
                    r.Present = true;
                    r.Percent = Math.Max(0, Math.Min(100, Convert.ToInt32(b["EstimatedChargeRemaining"])));
                    b.Dispose();
                    break;
                }
            }
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM BatteryStatus"))
                {
                    foreach (ManagementObject b in s.Get())
                    {
                        r.AcOnline = (bool)b["PowerOnline"];
                        r.Charging = (bool)b["Charging"];
                        r.Discharging = (bool)b["Discharging"];
                        r.DischargeRateMw = Convert.ToInt32(b["DischargeRate"]);
                        b.Dispose();
                        break;
                    }
                }
            }
            catch
            {
                // root\wmi BatteryStatus may need admin; fall back to the user-level power API
                var ps = System.Windows.Forms.SystemInformation.PowerStatus;
                r.AcOnline = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                r.Charging = (ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.Charging) != 0;
            }
            return r;
        }
    }
}
