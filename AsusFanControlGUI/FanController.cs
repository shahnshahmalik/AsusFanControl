using AsusFanControl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AsusFanControlGUI
{
    /// <summary>What the controller last read and applied. Immutable.</summary>
    public sealed class FanStatus
    {
        /// <summary>The fans are under the firmware's own control.</summary>
        public const int Released = -1;
        /// <summary>Nothing has been written yet.</summary>
        public const int Unknown = int.MinValue;

        public int CpuC { get; internal set; }
        public int GpuC { get; internal set; }
        public IList<int> Rpms { get; internal set; }
        public int Applied { get; internal set; }
        public int? Pending { get; internal set; }
        public bool Sleeping { get; internal set; }
    }

    /// <summary>The settings the controller acts on. Immutable.</summary>
    public sealed class FanTargetConfig
    {
        public bool Enabled;
        public bool Auto;
        public int ManualPercent;
        public bool Limit;
        public bool UseGpu;
        public FanSetpoint[] Cpu;
        public FanSetpoint[] Gpu;
    }

    /// <summary>
    /// Reads the sensors on a background timer, works out the fan speed and writes it to
    /// the EC.
    ///
    /// The EC can leave fan test mode on its own (Modern Standby, AC plug events, ASUS
    /// services), handing the fans back to the firmware. Writes are skipped while the
    /// target is unchanged, so the current duty is re-sent every <see cref="ReassertSeconds"/>
    /// to recover from that.
    ///
    /// EC writes are kept rare: a curve speed is written only after it has held for
    /// <see cref="DebounceSeconds"/>, and changes smaller than <see cref="Deadband"/>% are
    /// ignored, so the curve does not write on every 1% of drift.
    /// </summary>
    public sealed class FanController : IDisposable
    {
        const int TickMs = 3000;
        const int DebounceSeconds = 5;
        const int ReassertSeconds = 15;
        const int Deadband = 3;

        readonly AsusControl _asus = new AsusControl();
        readonly object _sync = new object();
        readonly object _tickLock = new object();
        Timer _timer;

        FanTargetConfig _config = new FanTargetConfig();
        bool _immediate = true;
        bool _sleeping;
        bool _stopped;

        int _applied = FanStatus.Unknown;
        int? _pending;
        DateTime _pendingSinceUtc;
        DateTime _lastWriteUtc;

        public event Action<FanStatus> StatusChanged;

        public void Start()
        {
            _timer = new Timer(_ => Tick(), null, 0, TickMs);
        }

        /// <summary>Replace the settings; a user change is applied without debounce.</summary>
        public void Configure(FanTargetConfig config)
        {
            lock (_sync)
            {
                _config = config;
                _immediate = true;
            }
            TickSoon();
        }

        public void OnSleep(bool releaseToFirmware)
        {
            lock (_tickLock)
            {
                lock (_sync)
                    _sleeping = true;
                if (releaseToFirmware)
                    Write(FanStatus.Released, "sleep");
                else
                    Log.Write("Sleep: keeping the current fan speed");
            }
        }

        public void OnWake()
        {
            lock (_sync)
            {
                _sleeping = false;
                _immediate = true;
                // The EC may have reset while asleep, so do not trust the cached value.
                _applied = FanStatus.Unknown;
            }
            Log.Write("Wake: re-applying fan speed");
            TickSoon();
        }

        /// <summary>Stops the timer and, if asked, hands the fans back to the firmware.</summary>
        public void Shutdown(bool releaseToFirmware)
        {
            lock (_tickLock)
            {
                _stopped = true;
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }
                if (releaseToFirmware && _applied != FanStatus.Released)
                    Write(FanStatus.Released, "exit");
            }
        }

        /// <summary>
        /// Measures the fans' RPM at 10%, 20% … 100% so the graph can show RPM. Blocks
        /// (run it on a background thread) for about one to two minutes; normal control
        /// is paused meanwhile and resumes afterwards. Returns null when cancelled or when
        /// the fans never reported a speed.
        /// </summary>
        public RpmCalibration Calibrate(Action<int, int> progress, CancellationToken cancel)
        {
            var points = new List<RpmPoint> { new RpmPoint(0, 0) };
            lock (_tickLock)
            {
                if (_stopped)
                    return null;
                Log.Write("Calibration started");
                try
                {
                    for (int percent = 10; percent <= 100; percent += 10)
                    {
                        if (cancel.IsCancellationRequested)
                            break;
                        _asus.SetFanDutyNow(percent);
                        int rpm = WaitForSteadyRpm(cancel);
                        if (rpm < 0)
                            break;
                        points.Add(new RpmPoint(percent, rpm));
                        Log.Write("Calibration: " + percent + "% -> " + rpm + " RPM");
                        if (progress != null)
                            progress(percent, rpm);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Calibration failed: " + ex.Message);
                    points.Clear();
                }
                finally
                {
                    // Re-apply the normal target on the next tick.
                    lock (_sync)
                    {
                        _applied = FanStatus.Unknown;
                        _immediate = true;
                    }
                }
            }
            TickSoon();

            if (cancel.IsCancellationRequested || points.Count < 11)
            {
                Log.Write("Calibration " + (cancel.IsCancellationRequested ? "cancelled" : "incomplete"));
                return null;
            }
            var result = new RpmCalibration
            {
                MeasuredAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Points = points.ToArray(),
            };
            result.Normalize();
            if (!result.IsUsable)
            {
                Log.Write("Calibration: the fans reported no speed");
                return null;
            }
            Log.Write("Calibration finished, max " + result.MaxRpm + " RPM");
            return result;
        }

        /// <summary>
        /// Average RPM of all fans once it has settled: two readings a second apart
        /// within 3% of each other, after at least 3 s, at most 12 s. -1 if cancelled.
        /// </summary>
        int WaitForSteadyRpm(CancellationToken cancel)
        {
            int previous = -1;
            for (int second = 1; second <= 12; second++)
            {
                if (cancel.WaitHandle.WaitOne(1000))
                    return -1;
                List<int> rpms = _asus.GetFanSpeeds();
                int average = rpms.Count == 0 ? 0 : (int)Math.Round(rpms.Average());
                if (second >= 3 && previous >= 0 && Math.Abs(average - previous) <= Math.Max(50, previous * 0.03))
                    return average;
                previous = average;
            }
            return previous;
        }

        /// <summary>Last-resort safety on a crash: never leave the fans stopped.</summary>
        public void EmergencyRelease()
        {
            try
            {
                _asus.ReleaseFanControlNow();
            }
            catch (Exception)
            {
            }
        }

        public void Dispose()
        {
            Shutdown(false);
        }

        void TickSoon()
        {
            ThreadPool.QueueUserWorkItem(_ => Tick());
        }

        void Tick()
        {
            if (!Monitor.TryEnter(_tickLock))
                return;
            try
            {
                if (_stopped)
                    return;

                int cpu = _asus.GetCpuTemperatureCelsius();
                int gpu = _asus.GetGpuTemperatureCelsius();
                List<int> rpms = _asus.GetFanSpeeds();

                FanTargetConfig config;
                bool immediate;
                bool sleeping;
                lock (_sync)
                {
                    config = _config;
                    immediate = _immediate;
                    sleeping = _sleeping;
                    _immediate = false;
                }

                if (!sleeping)
                    Decide(config, cpu, gpu, immediate);

                Publish(cpu, gpu, rpms, sleeping);
            }
            catch (Exception ex)
            {
                Log.Write("Fan update failed: " + ex.Message);
            }
            finally
            {
                Monitor.Exit(_tickLock);
            }
        }

        void Decide(FanTargetConfig config, int cpu, int gpu, bool immediate)
        {
            int target = ComputeTarget(config, cpu, gpu);
            DateTime now = DateTime.UtcNow;
            bool smooth = config.Enabled && config.Auto;

            // The curve drifts by 1% at a time; stay put until it has moved enough.
            if (smooth && !immediate && Near(target, _applied))
                target = _applied;

            if (target == _applied)
            {
                _pending = null;
                if (target != FanStatus.Released && (now - _lastWriteUtc).TotalSeconds >= ReassertSeconds)
                    Write(target, null);
                return;
            }

            // Curve changes wait out short temperature spikes; user actions do not.
            bool debounce = config.Enabled && config.Auto && !immediate
                && _applied != FanStatus.Unknown && target != FanStatus.Released;
            if (!debounce)
            {
                Write(target, _applied == FanStatus.Unknown ? "start" : "settings changed");
                return;
            }

            if (_pending == null || !(_pending == target || (smooth && Near(target, _pending.Value))))
            {
                _pending = target;
                _pendingSinceUtc = now;
                return;
            }
            _pending = target;
            if ((now - _pendingSinceUtc).TotalSeconds >= DebounceSeconds)
                Write(target, "curve");
        }

        static bool Near(int a, int b)
        {
            // 0 means "stop", so going to or from it always counts as a change.
            return a > 0 && b > 0 && Math.Abs(a - b) < Deadband;
        }

        static int ComputeTarget(FanTargetConfig config, int cpu, int gpu)
        {
            if (!config.Enabled)
                return FanStatus.Released;

            if (!config.Auto)
                return config.Limit && config.ManualPercent > 0
                    ? LimitPercent(config.ManualPercent)
                    : config.ManualPercent;

            int cpuPercent = cpu >= 0 && config.Cpu != null
                ? FanCurve.GetFanPercent(cpu, config.Cpu, config.Limit)
                : -1;
            int gpuPercent = config.UseGpu && gpu >= 0 && config.Gpu != null
                ? FanCurve.GetFanPercent(gpu, config.Gpu, config.Limit)
                : -1;
            int percent = FanCurve.Combine(cpuPercent, gpuPercent);

            // No usable temperature: let the firmware protect the machine.
            return percent < 0 ? FanStatus.Released : percent;
        }

        /// <summary>The 40–99% limit for non-zero speeds; 0 (off) is left alone by callers.</summary>
        public static int LimitPercent(int percent)
        {
            return Math.Max(40, Math.Min(99, percent));
        }

        void Write(int target, string reason)
        {
            if (target == FanStatus.Released)
                _asus.ReleaseFanControlNow();
            else
                _asus.SetFanDutyNow(target);

            if (reason != null && target != _applied)
                Log.Write("Fans -> " + Describe(target) + " (" + reason + ")");
            _applied = target;
            _pending = null;
            _lastWriteUtc = DateTime.UtcNow;
        }

        void Publish(int cpu, int gpu, List<int> rpms, bool sleeping)
        {
            var handler = StatusChanged;
            if (handler == null)
                return;
            handler(new FanStatus
            {
                CpuC = cpu,
                GpuC = gpu,
                Rpms = rpms.AsReadOnly(),
                Applied = _applied,
                Pending = _pending,
                Sleeping = sleeping,
            });
        }

        public static string Describe(int target)
        {
            if (target == FanStatus.Released) return "BIOS control";
            if (target == FanStatus.Unknown) return "--";
            if (target == 0) return "off";
            return target + "%";
        }
    }
}
