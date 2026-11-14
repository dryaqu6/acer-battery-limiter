using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;

namespace AcerBatteryLimiter
{
    static class Program
    {
        public const string AppName = "Acer Battery Limiter";

        [STAThread]
        static int Main(string[] args)
        {
            var mode = args.FirstOrDefault()?.ToLowerInvariant() ?? "";

            if (mode == "--service")
            {
                ServiceBase.Run(new LimiterService());
                return 0;
            }
            if (mode == "--apply-once")   // run one control step from an elevated prompt (testing / support)
            {
                new LimiterService().ApplyOnce(Store.LoadConfig());
                return 0;
            }
            if (mode == "--stop-discharge")   // used by the uninstaller
            {
                try { AcerWmi.SetCalibration(false); } catch { }
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (mode == "--tray")
            {
                using (var m = new Mutex(true, @"Local\AcerBatteryLimiterTray", out bool first))
                {
                    if (!first) return 0;
                    Application.Run(new TrayContext());
                }
                return 0;
            }

            using (var m = new Mutex(true, @"Local\AcerBatteryLimiterWindow", out bool first))
            {
                if (!first) { BringExistingWindowToFront(); return 0; }
                EnsureTrayRunning();
                Application.Run(new SettingsForm());
            }
            return 0;
        }

        public static Icon AppIcon(Size size)
        {
            using (var s = typeof(Program).Assembly.GetManifestResourceStream("AcerBatteryLimiter.app.ico"))
                return s == null ? SystemIcons.Application : new Icon(s, size);
        }

        public static void EnsureTrayRunning()
        {
            // the tray holds a named mutex in this session; if it can be opened, the tray is running
            try { using (Mutex.OpenExisting(@"Local\AcerBatteryLimiterTray")) return; }
            catch (WaitHandleCannotBeOpenedException) { }
            catch { return; }
            try { Process.Start(Application.ExecutablePath, "--tray"); } catch { }
        }

        public static void OpenWindow()
        {
            try { Process.Start(Application.ExecutablePath); } catch { }
        }

        static void BringExistingWindowToFront()
        {
            var me = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(me.ProcessName))
            {
                if (p.Id == me.Id || p.MainWindowHandle == IntPtr.Zero) continue;
                NativeMethods.ShowWindow(p.MainWindowHandle, 9 /* SW_RESTORE */);
                NativeMethods.SetForegroundWindow(p.MainWindowHandle);
            }
        }

        /// <summary>Service state as seen by a normal user.</summary>
        public static string ServiceState()
        {
            try
            {
                using (var sc = new ServiceController(LimiterService.Name))
                    return sc.Status.ToString();
            }
            catch { return "NotInstalled"; }
        }

        public static bool StatusIsFresh(AppStatus s)
        {
            if (s == null) return false;
            var poll = Store.LoadConfig().PollSeconds;
            return (DateTime.UtcNow - s.UpdatedUtc).TotalSeconds < poll * 3 + 30;
        }
    }

    static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
