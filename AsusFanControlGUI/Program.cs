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
                // A second automatic start (boot and logon tasks) should stay silent.
                if (!startMinimized)
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
