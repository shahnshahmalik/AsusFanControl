using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace AsusFanControlGUI
{
    /// <summary>
    /// Registers the GUI to start at logon with a scheduled task.
    ///
    /// The task belongs to the logged-on user even when this app runs as SYSTEM (as it
    /// does when started through run.bat / PsExec -s); a task created for SYSTEM would
    /// never show a window at logon. The task is created from XML so it also starts on
    /// battery and is not stopped after the default three-day limit. When PsExec.exe is
    /// next to the exe, the task starts the GUI with --as-system and the GUI relaunches
    /// itself through PsExec as SYSTEM (hidden), like run.bat, because some machines
    /// only allow EC access from SYSTEM.
    /// </summary>
    static class WindowsStartup
    {
        public const string TaskName = "AsusFanControlGUI";
        public const string StartMinimizedArgument = "--minimized";
        public const string AsSystemArgument = "--as-system";
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValueName = "AsusFanControlGUI";

        public static bool Enable(out string error)
        {
            error = null;

            string exe = Application.ExecutablePath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                error = "Could not find AsusFanControlGUI.exe.";
                return false;
            }

            string user = GetInteractiveUser();
            if (string.IsNullOrEmpty(user))
            {
                error = "Could not find the signed-in Windows user.";
                return false;
            }

            string directory = Path.GetDirectoryName(exe);
            // The task always starts the GUI exe (a windowed app). Starting PsExec.exe
            // directly would open a console window at logon, so the GUI starts PsExec
            // itself with no window (see TryRelaunchAsSystem).
            string command = exe;
            string arguments = File.Exists(PsExecPath())
                ? AsSystemArgument + " " + StartMinimizedArgument
                : StartMinimizedArgument;

            string xmlPath = Path.Combine(Path.GetTempPath(), "AsusFanControlGUI-task.xml");
            try
            {
                File.WriteAllText(xmlPath, BuildTaskXml(user, command, arguments, directory), Encoding.Unicode);
                string output;
                if (!RunSchtasks("/Create /F /TN \"" + TaskName + "\" /XML \"" + xmlPath + "\"", out output))
                {
                    error = string.IsNullOrWhiteSpace(output) ? "Task Scheduler refused the task." : output;
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try { File.Delete(xmlPath); }
                catch (Exception) { }
            }

            // Earlier versions could leave a Run-key entry; the task replaces it.
            RemoveRunValue();
            Log.Write("Start with Windows: task created for " + user);
            return true;
        }

        static string PsExecPath()
        {
            return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "PsExec.exe");
        }

        /// <summary>
        /// Starts this exe again as SYSTEM in the current session through PsExec, with no
        /// console window. Returns false (so the caller should keep running as itself)
        /// when already SYSTEM, PsExec.exe is missing, or PsExec could not be started.
        /// </summary>
        public static bool TryRelaunchAsSystem(bool startMinimized)
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                if (identity.IsSystem)
                    return false;
            }

            string psexec = PsExecPath();
            if (!File.Exists(psexec))
            {
                Log.Write("Start as SYSTEM: PsExec.exe not found, running as " + Environment.UserName);
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = psexec,
                Arguments = "-accepteula -nobanner -i " + Process.GetCurrentProcess().SessionId
                    + " -s -d \"" + Application.ExecutablePath + "\""
                    + (startMinimized ? " " + StartMinimizedArgument : ""),
                WorkingDirectory = Path.GetDirectoryName(psexec),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                        return false;
                    // With -d PsExec returns once the new process has started.
                    if (!process.WaitForExit(30000))
                    {
                        Log.Write("Start as SYSTEM: PsExec did not finish in time");
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("Start as SYSTEM: " + ex.Message);
                return false;
            }
        }

        public static void Disable()
        {
            string output;
            RunSchtasks("/Delete /F /TN \"" + TaskName + "\"", out output);
            RemoveRunValue();
            Log.Write("Start with Windows: task removed");
        }

        static string BuildTaskXml(string user, string command, string arguments, string workingDirectory)
        {
            string u = SecurityElement.Escape(user);
            return
"<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
"<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
"  <RegistrationInfo><Description>Starts Asus Fan Control when you sign in.</Description></RegistrationInfo>\r\n" +
"  <Triggers>\r\n" +
"    <LogonTrigger><Enabled>true</Enabled><UserId>" + u + "</UserId><Delay>PT5S</Delay></LogonTrigger>\r\n" +
"  </Triggers>\r\n" +
"  <Principals>\r\n" +
"    <Principal id=\"Author\"><UserId>" + u + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>\r\n" +
"  </Principals>\r\n" +
"  <Settings>\r\n" +
"    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
"    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
"    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
"    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
"    <Priority>7</Priority>\r\n" +
"  </Settings>\r\n" +
"  <Actions Context=\"Author\">\r\n" +
"    <Exec>\r\n" +
"      <Command>" + SecurityElement.Escape(command) + "</Command>\r\n" +
"      <Arguments>" + SecurityElement.Escape(arguments) + "</Arguments>\r\n" +
"      <WorkingDirectory>" + SecurityElement.Escape(workingDirectory) + "</WorkingDirectory>\r\n" +
"    </Exec>\r\n" +
"  </Actions>\r\n" +
"</Task>\r\n";
        }

        /// <summary>
        /// DOMAIN\user of the person at the console. When running as SYSTEM the current
        /// identity is not that person, so ask Remote Desktop Services for the console
        /// session's user instead.
        /// </summary>
        static string GetInteractiveUser()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                if (!identity.IsSystem)
                    return identity.Name;
            }

            uint session = WTSGetActiveConsoleSessionId();
            if (session == 0xFFFFFFFF)
                return null;

            string user = QuerySessionString(session, WtsUserName);
            if (string.IsNullOrEmpty(user))
                return null;
            string domain = QuerySessionString(session, WtsDomainName);
            return string.IsNullOrEmpty(domain) ? user : domain + "\\" + user;
        }

        const int WtsUserName = 5;
        const int WtsDomainName = 7;

        [DllImport("kernel32.dll")]
        static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool WTSQuerySessionInformation(IntPtr server, uint sessionId, int infoClass,
            out IntPtr buffer, out int bytesReturned);

        [DllImport("wtsapi32.dll")]
        static extern void WTSFreeMemory(IntPtr memory);

        static string QuerySessionString(uint session, int infoClass)
        {
            IntPtr buffer;
            int bytes;
            if (!WTSQuerySessionInformation(IntPtr.Zero, session, infoClass, out buffer, out bytes))
                return null;
            try
            {
                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }

        static void RemoveRunValue()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null)
                        key.DeleteValue(RunValueName, false);
                }
            }
            catch (Exception)
            {
            }
        }

        static bool RunSchtasks(string arguments, out string output)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        output = "Could not start schtasks.exe.";
                        return false;
                    }

                    var stdoutTask = process.StandardOutput.ReadToEndAsync();
                    var stderrTask = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(8000))
                    {
                        try { process.Kill(); }
                        catch (Exception) { }
                        output = "Timed out while talking to Task Scheduler.";
                        return false;
                    }

                    output = (stdoutTask.Result + "\n" + stderrTask.Result).Trim();
                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return false;
            }
        }
    }
}
