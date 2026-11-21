using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace AsusFanControlGUI
{
    /// <summary>
    /// Named events that let another copy of the exe talk to the running one, which may
    /// run as SYSTEM: "show" brings its window forward (a second launch from a shortcut),
    /// "exit" closes it and hands the fans to the BIOS (installer upgrade / uninstall).
    /// </summary>
    static class InstanceSignals
    {
        const string ShowName = @"Global\AsusFanControlGUI-Show";
        const string ExitName = @"Global\AsusFanControlGUI-Exit";

        static EventWaitHandle _show, _exit;
        static RegisteredWaitHandle _showWait, _exitWait;

        /// <summary>Called by the running copy. Callbacks run on a thread-pool thread.</summary>
        public static void Listen(Action onShow, Action onExit)
        {
            _show = Create(ShowName);
            _exit = Create(ExitName);
            if (_show != null)
                _showWait = ThreadPool.RegisterWaitForSingleObject(_show, (s, t) => onShow(), null, Timeout.Infinite, false);
            if (_exit != null)
                _exitWait = ThreadPool.RegisterWaitForSingleObject(_exit, (s, t) => onExit(), null, Timeout.Infinite, true);
        }

        public static void Stop()
        {
            if (_showWait != null) _showWait.Unregister(null);
            if (_exitWait != null) _exitWait.Unregister(null);
            _showWait = _exitWait = null;
        }

        public static bool SignalShow()
        {
            return Signal(ShowName);
        }

        public static bool SignalExit()
        {
            return Signal(ExitName);
        }

        static EventWaitHandle Create(string name)
        {
            // Copies started by hand (an administrator, not SYSTEM) must be able to set it.
            var security = new EventWaitHandleSecurity();
            var rights = EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify;
            security.AddAccessRule(new EventWaitHandleAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), EventWaitHandleRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new EventWaitHandleAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), rights, AccessControlType.Allow));
            security.AddAccessRule(new EventWaitHandleAccessRule(
                new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), rights, AccessControlType.Allow));
            try
            {
                bool created;
                return new EventWaitHandle(false, EventResetMode.AutoReset, name, out created, security);
            }
            catch (Exception ex)
            {
                Log.Write("Could not create " + name + ": " + ex.Message);
                return null;
            }
        }

        static bool Signal(string name)
        {
            try
            {
                using (var handle = EventWaitHandle.OpenExisting(name, EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify))
                    return handle.Set();
            }
            catch (Exception)
            {
                // Not running, or a version without this signal.
                return false;
            }
        }
    }
}
