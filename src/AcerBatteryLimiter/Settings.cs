using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace AcerBatteryLimiter
{
    public static class Time
    {
        /// <summary>"Never / not set". DateTime.MinValue cannot be written by the JSON serializer in UTC+ time zones.</summary>
        public static readonly DateTime Never = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>User settings, written by the window, read by the service.</summary>
    [DataContract]
    public sealed class AppConfig
    {
        [DataMember] public bool LimitEnabled;
        [DataMember] public int LimitPercent;
        [DataMember] public bool DischargeAboveLimit;
        /// <summary>0 = off. Charge to 100% this often (months), then go back to the limit.</summary>
        [DataMember] public int FullChargeEveryMonths;
        /// <summary>"Charge to 100% once": the window writes the time of the click; the service handles each value once.</summary>
        [DataMember] public DateTime ChargeOnceRequestedUtc;
        [DataMember] public DateTime ChargeOnceCancelledUtc;
        /// <summary>Band around the limit so charging/discharging does not flip every minute.</summary>
        [DataMember] public int HysteresisPercent;
        [DataMember] public int PollSeconds;
        /// <summary>Once discharge is switched on, keep it on at least this long (switching it off too quickly can leave the charger disconnected).</summary>
        [DataMember] public int MinDischargeSeconds;
        /// <summary>Discharge only starts at or above this level: Acer's calibration mode tops the battery up to 100% before it drains.</summary>
        [DataMember] public int DischargeStartPercent;

        public AppConfig() => SetDefaults();
        [OnDeserializing] void OnDeserializing(StreamingContext _) => SetDefaults();   // fields missing in older files keep their defaults

        public void SetDefaults()
        {
            LimitEnabled = true;
            LimitPercent = 80;
            DischargeAboveLimit = true;
            FullChargeEveryMonths = 3;
            DischargeStartPercent = 95;
            HysteresisPercent = 3;
            PollSeconds = 30;
            MinDischargeSeconds = 120;
            ChargeOnceRequestedUtc = Time.Never;
            ChargeOnceCancelledUtc = Time.Never;
        }

        /// <summary>Reset only what the user sees in the window (keeps request timestamps).</summary>
        public void ResetUserSettings()
        {
            LimitEnabled = true;
            LimitPercent = 80;
            DischargeAboveLimit = true;
            FullChargeEveryMonths = 3;
        }

        public AppConfig Normalized()
        {
            LimitPercent = Clamp(LimitPercent, 0, 100);
            FullChargeEveryMonths = Clamp(FullChargeEveryMonths, 0, 12);
            HysteresisPercent = Clamp(HysteresisPercent, 1, 10);
            PollSeconds = Clamp(PollSeconds, 10, 600);
            MinDischargeSeconds = Clamp(MinDischargeSeconds, 30, 1800);
            DischargeStartPercent = Clamp(DischargeStartPercent, 81, 100);
            return this;
        }

        static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));
    }

    /// <summary>Service memory that must survive restarts. Written only by the service.</summary>
    [DataContract]
    public sealed class ServiceState
    {
        [DataMember] public DateTime LastFullChargeUtc;
        [DataMember] public DateTime HandledChargeOnceUtc;
        [DataMember] public DateTime HandledCancelUtc;
        /// <summary>"" = none, "Scheduled" or "Once".</summary>
        [DataMember] public string FullCharge = "";
        [DataMember] public DateTime FullChargeSinceUtc;
        /// <summary>The one-time full charge ends when unplugged, but only after the charger was seen connected.</summary>
        [DataMember] public bool FullChargeSawCharger;
        [DataMember] public DateTime DischargeSinceUtc;

        public ServiceState() => SetDefaults();
        [OnDeserializing] void OnDeserializing(StreamingContext _) => SetDefaults();
        void SetDefaults()
        {
            LastFullChargeUtc = HandledChargeOnceUtc = HandledCancelUtc = FullChargeSinceUtc = DischargeSinceUtc = Time.Never;
            FullCharge = "";
        }
    }

    /// <summary>Current state, written by the service, read by the window and tray.</summary>
    [DataContract]
    public sealed class AppStatus
    {
        [DataMember] public DateTime UpdatedUtc;
        [DataMember] public bool Supported;
        [DataMember] public int Percent;
        [DataMember] public bool AcOnline;
        [DataMember] public bool HardwareLimitOn;
        [DataMember] public bool CalibrationOn;
        /// <summary>Discharging / Charging / Holding / FullCharge / OnBattery / NoLimit / NotSupported / Error</summary>
        [DataMember] public string Mode = "";
        [DataMember] public string Message = "";
        /// <summary>"" or "Scheduled" / "Once".</summary>
        [DataMember] public string FullCharge = "";
        /// <summary>Time.Never when the scheduled full charge is off.</summary>
        [DataMember] public DateTime NextFullChargeUtc = Time.Never;
    }

    public static class Store
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AcerBatteryLimiter");
        public static string ConfigPath => Path.Combine(Dir, "config.json");
        public static string StatusPath => Path.Combine(Dir, "status.json");
        public static string StatePath => Path.Combine(Dir, "state.json");
        public static string LogPath => Path.Combine(Dir, "service.log");

        public static AppConfig LoadConfig() => (Read<AppConfig>(ConfigPath) ?? new AppConfig()).Normalized();
        public static void SaveConfig(AppConfig c) => Write(ConfigPath, c.Normalized());
        public static AppStatus LoadStatus() => Read<AppStatus>(StatusPath);
        public static void SaveStatus(AppStatus s) => Write(StatusPath, s);
        public static ServiceState LoadState() => Read<ServiceState>(StatePath);
        public static void SaveState(ServiceState s) => Write(StatePath, s);

        static T Read<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                var bytes = File.ReadAllBytes(path);
                int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;   // tolerate a UTF-8 BOM
                using (var ms = new MemoryStream(bytes, start, bytes.Length - start))
                    return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(ms);
            }
            catch { return null; }
        }

        static void Write<T>(string path, T value)
        {
            Directory.CreateDirectory(Dir);
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(ms, value);
                var tmp = path + ".tmp";
                File.WriteAllBytes(tmp, ms.ToArray());
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
        }

        public static void Log(string line)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 512 * 1024)
                {
                    var lines = File.ReadAllLines(LogPath);
                    File.WriteAllLines(LogPath, System.Linq.Enumerable.Skip(lines, Math.Max(0, lines.Length - 2000)), Encoding.UTF8);
                }
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }
}
