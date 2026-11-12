using Microsoft.Win32;
using System;
using System.Diagnostics.Eventing.Reader;

namespace AsusFanControlGUI
{
    /// <summary>
    /// Raises <see cref="Sleeping"/> and <see cref="Woke"/> for both classic sleep (S3) and
    /// Modern Standby (S0 low power idle).
    ///
    /// On Modern Standby laptops Windows often does not send the suspend/resume broadcast
    /// that <see cref="SystemEvents.PowerModeChanged"/> relies on, so the Kernel-Power
    /// events 506 (entering Modern Standby) and 507 (exiting) in the System log are
    /// watched as well. Duplicate notifications from the two sources are collapsed.
    /// </summary>
    public sealed class SleepWatcher : IDisposable
    {
        const string StandbyQuery =
            "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=506 or EventID=507)]]";

        readonly object _sync = new object();
        EventLogWatcher _watcher;
        bool _asleep;

        public event Action Sleeping;
        public event Action Woke;

        public SleepWatcher()
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            try
            {
                _watcher = new EventLogWatcher(new EventLogQuery("System", PathType.LogName, StandbyQuery));
                _watcher.EventRecordWritten += OnStandbyEvent;
                _watcher.Enabled = true;
            }
            catch (Exception ex)
            {
                Log.Write("Modern Standby events are not available: " + ex.Message);
                _watcher = null;
            }
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend)
                Raise(true, "suspend");
            else if (e.Mode == PowerModes.Resume)
                Raise(false, "resume");
        }

        void OnStandbyEvent(object sender, EventRecordWrittenEventArgs e)
        {
            if (e.EventRecord == null)
                return;
            using (e.EventRecord)
            {
                if (e.EventRecord.Id == 506)
                    Raise(true, "Modern Standby entered");
                else if (e.EventRecord.Id == 507)
                    Raise(false, "Modern Standby exited");
            }
        }

        void Raise(bool sleeping, string source)
        {
            lock (_sync)
            {
                if (_asleep == sleeping)
                    return;
                _asleep = sleeping;
            }

            Log.Write((sleeping ? "Sleep" : "Wake") + " detected: " + source);
            var handler = sleeping ? Sleeping : Woke;
            if (handler != null)
                handler();
        }

        public void Dispose()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            if (_watcher != null)
            {
                _watcher.Enabled = false;
                _watcher.Dispose();
                _watcher = null;
            }
        }
    }
}
