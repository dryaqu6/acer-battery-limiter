using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace AcerBatteryLimiter
{
    /// <summary>User settings, written by the window, read by the service.</summary>
    [DataContract]
    public sealed class AppConfig
    {
        [DataMember] public bool LimitEnabled = true;
        [DataMember] public int LimitPercent = 80;
        [DataMember] public bool DischargeAboveLimit = false;
        /// <summary>Band around the limit so the charger/discharger does not flip every minute.</summary>
        [DataMember] public int HysteresisPercent = 3;
        [DataMember] public int PollSeconds = 30;

        public AppConfig Normalized()
        {
            LimitPercent = Math.Max(0, Math.Min(100, LimitPercent));
            HysteresisPercent = Math.Max(1, Math.Min(10, HysteresisPercent));
            PollSeconds = Math.Max(10, Math.Min(600, PollSeconds));
            return this;
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
        [DataMember] public string Mode = "";      // Discharging / Charging / Holding / OnBattery / NoLimit / NotSupported / Error
        [DataMember] public string Message = "";   // human readable, shown in tray and window
    }

    public static class Store
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AcerBatteryLimiter");
        public static string ConfigPath => Path.Combine(Dir, "config.json");
        public static string StatusPath => Path.Combine(Dir, "status.json");
        public static string LogPath => Path.Combine(Dir, "service.log");

        public static AppConfig LoadConfig()
        {
            var c = Read<AppConfig>(ConfigPath);
            return (c ?? new AppConfig()).Normalized();
        }

        public static void SaveConfig(AppConfig c) => Write(ConfigPath, c.Normalized());
        public static AppStatus LoadStatus() => Read<AppStatus>(StatusPath);
        public static void SaveStatus(AppStatus s) => Write(StatusPath, s);

        static T Read<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                var bytes = File.ReadAllBytes(path);
                using (var ms = new MemoryStream(bytes))
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
