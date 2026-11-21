using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace AsusFanControlGUI
{
    internal static class Program
    {
        // Global\ so copies in different sessions see each other, e.g. one started as
        // SYSTEM by run.bat or the logon task and one started by hand.
        const string MutexName = @"Global\AsusFanControlGUI";

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Used by the installer: close a running copy, or turn Start with Windows on/off.
            if (HasArgument(args, ExitArgument))
            {
                Environment.ExitCode = ExitRunningCopy() ? 0 : 1;
                return;
            }
            int startupIndex = Array.FindIndex(args, a => string.Equals(a, StartupArgument, StringComparison.OrdinalIgnoreCase));
            if (startupIndex >= 0)
            {
                string value = startupIndex + 1 < args.Length ? args[startupIndex + 1] : "";
                Environment.ExitCode = SetStartWithWindows(string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)) ? 0 : 1;
                return;
            }

            bool startMinimized = args.Any(a =>
                string.Equals(a, WindowsStartup.StartMinimizedArgument, StringComparison.OrdinalIgnoreCase));
            bool asSystem = args.Any(a =>
                string.Equals(a, WindowsStartup.AsSystemArgument, StringComparison.OrdinalIgnoreCase));

            // The logon task starts us as the user; hand over to a SYSTEM copy (like
            // run.bat) and exit once it is up. If it never appears, keep running here.
            if (asSystem && !IsRunning() && WindowsStartup.TryRelaunchAsSystem(startMinimized))
            {
                for (int i = 0; i < 40; i++)
                {
                    if (IsRunning())
                        return;
                    Thread.Sleep(250);
                }
                Log.Write("Start as SYSTEM: the SYSTEM copy did not start, running as " + Environment.UserName);
            }

            Mutex mutex = null;
            bool createdNew;
            try
            {
                mutex = new Mutex(true, MutexName, out createdNew);
            }
            catch (UnauthorizedAccessException)
            {
                // Another account (e.g. SYSTEM) owns it, so a copy is already running.
                createdNew = false;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (!createdNew)
            {
                // A second automatic start (boot and logon tasks) should stay silent; a
                // start by hand (e.g. the Start menu shortcut) shows the running window.
                if (!startMinimized && !InstanceSignals.SignalShow())
                {
                    MessageBox.Show(
                        "Asus Fan Control is already running. Look for its icon in the notification area.",
                        "Asus Fan Control",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return;
            }

            MainForm form = null;
            Application.ThreadException += (s, e) => OnCrash(form, e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => OnCrash(form, e.ExceptionObject as Exception);

            Log.Write("Started" + (startMinimized ? " (minimized)" : "") + " as " + Environment.UserName);
            form = new MainForm(startMinimized);
            Application.Run(form);
            GC.KeepAlive(mutex);
        }

        const string ExitArgument = "--exit";
        const string StartupArgument = "--startup";

        static bool HasArgument(string[] args, string name)
        {
            return args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Asks the running copy to close (it hands the fans to the BIOS) and waits up to
        /// 15 s. Returns true when no copy is running afterwards.
        /// </summary>
        static bool ExitRunningCopy()
        {
            if (!IsRunning())
                return true;
            if (!InstanceSignals.SignalExit())
                return false;
            for (int i = 0; i < 60 && IsRunning(); i++)
                Thread.Sleep(250);
            return !IsRunning();
        }

        static bool SetStartWithWindows(bool enable)
        {
            var settings = AppSettings.Load();
            if (enable)
            {
                string error;
                if (!WindowsStartup.Enable(out error))
                {
                    Log.Write("Start with Windows (installer): " + error);
                    return false;
                }
            }
            else
            {
                WindowsStartup.Disable();
            }
            settings.StartWithWindows = enable;
            settings.Save();
            return true;
        }

        static bool IsRunning()
        {
            try
            {
                Mutex existing;
                if (!Mutex.TryOpenExisting(MutexName, out existing))
                    return false;
                existing.Dispose();
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                // Owned by another account (e.g. SYSTEM), so it exists.
                return true;
            }
        }

        static void OnCrash(MainForm form, Exception ex)
        {
            Log.Write("Unexpected error: " + ex);
            // The fans may be stopped (0%); never leave them that way without the app.
            if (form != null)
                form.EmergencyRelease();
            MessageBox.Show(
                "Asus Fan Control hit an unexpected error and has handed the fans back to the BIOS.\r\n\r\n"
                    + (ex != null ? ex.Message : "") + "\r\n\r\nDetails are in " + Log.FilePath,
                "Asus Fan Control",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Environment.Exit(1);
        }
    }
}
