using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace AsusFanControlGUI
{
    /// <summary>
    /// A named fan curve. CPU and GPU each have up to three setpoints; only the first
    /// <see cref="PointCount"/> are used.
    /// </summary>
    [DataContract]
    public sealed class FanProfile
    {
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; }

        [DataMember(Name = "pointCount", Order = 1)]
        public int PointCount { get; set; }

        [DataMember(Name = "useGpu", Order = 2)]
        public bool UseGpu { get; set; }

        [DataMember(Name = "cpu", Order = 3)]
        public FanSetpoint[] Cpu { get; set; }

        [DataMember(Name = "gpu", Order = 4)]
        public FanSetpoint[] Gpu { get; set; }

        public static FanProfile Create(string name, int t1, int f1, int t2, int f2, int t3, int f3)
        {
            return new FanProfile
            {
                Name = name,
                PointCount = FanCurve.MaxSetpoints,
                UseGpu = false,
                Cpu = new[] { new FanSetpoint(t1, f1), new FanSetpoint(t2, f2), new FanSetpoint(t3, f3) },
                Gpu = new[] { new FanSetpoint(t1, f1), new FanSetpoint(t2, f2), new FanSetpoint(t3, f3) },
            };
        }

        public FanProfile Clone(string newName)
        {
            return new FanProfile
            {
                Name = newName,
                PointCount = PointCount,
                UseGpu = UseGpu,
                Cpu = (FanSetpoint[])Cpu.Clone(),
                Gpu = (FanSetpoint[])Gpu.Clone(),
            };
        }

        internal void Normalize()
        {
            PointCount = FanCurve.ClampCount(PointCount);
            Cpu = FixPoints(Cpu);
            Gpu = FixPoints(Gpu);
            FanCurve.Normalize(Cpu, PointCount);
            FanCurve.Normalize(Gpu, PointCount);
        }

        static FanSetpoint[] FixPoints(FanSetpoint[] points)
        {
            var fixedPoints = new[] { new FanSetpoint(35, 45), new FanSetpoint(55, 80), new FanSetpoint(75, 100) };
            if (points != null)
                Array.Copy(points, fixedPoints, Math.Min(points.Length, fixedPoints.Length));
            return fixedPoints;
        }
    }

    /// <summary>
    /// All GUI settings and fan profiles, stored as JSON in
    /// %ProgramData%\AsusFanControl\settings.json. ProgramData is shared by every account,
    /// so the settings are the same whether the app runs as the user or as SYSTEM, and
    /// they survive unzipping a new release into a different folder.
    /// </summary>
    [DataContract]
    public sealed class AppSettings
    {
        public const string ModeAuto = "auto";
        public const string ModeManual = "manual";
        const int CurrentVersion = 1;

        [DataMember(Name = "version", Order = 0)]
        public int Version { get; set; }

        [DataMember(Name = "controlEnabled", Order = 1)]
        public bool ControlEnabled { get; set; }

        [DataMember(Name = "mode", Order = 2)]
        public string Mode { get; set; }

        [DataMember(Name = "manualPercent", Order = 3)]
        public int ManualPercent { get; set; }

        [DataMember(Name = "activeProfile", Order = 4)]
        public string ActiveProfileName { get; set; }

        [DataMember(Name = "profiles", Order = 5)]
        public List<FanProfile> Profiles { get; set; }

        [DataMember(Name = "startWithWindows", Order = 6)]
        public bool StartWithWindows { get; set; }

        [DataMember(Name = "minimizeToTray", Order = 7)]
        public bool MinimizeToTray { get; set; }

        [DataMember(Name = "releaseOnSleep", Order = 8)]
        public bool ReleaseOnSleep { get; set; }

        [DataMember(Name = "releaseOnExit", Order = 9)]
        public bool ReleaseOnExit { get; set; }

        [DataMember(Name = "limitFanSpeed", Order = 10)]
        public bool LimitFanSpeed { get; set; }

        public bool IsAuto
        {
            get { return Mode != ModeManual; }
        }

        public FanProfile ActiveProfile
        {
            get
            {
                var profile = Profiles.FirstOrDefault(p => p.Name == ActiveProfileName);
                if (profile == null)
                {
                    profile = Profiles[0];
                    ActiveProfileName = profile.Name;
                }
                return profile;
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(AppPaths.DataDirectory, "settings.json"); }
        }

        public static AppSettings Load()
        {
            AppSettings settings = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    using (var stream = File.OpenRead(FilePath))
                        settings = (AppSettings)Serializer().ReadObject(stream);
                }
            }
            catch (Exception ex)
            {
                Log.Write("Could not read settings.json, using defaults: " + ex.Message);
                TryBackupBrokenFile();
            }

            if (settings == null)
            {
                settings = MigrateFromUserSettings();
                settings.Normalize();
                settings.Save();
                Log.Write("Created settings.json");
            }
            else
            {
                settings.Normalize();
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                string temp = FilePath + ".tmp";
                using (var stream = File.Create(temp))
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, true, true, "  "))
                {
                    Serializer().WriteObject(writer, this);
                    writer.Flush();
                }
                if (File.Exists(FilePath))
                    File.Replace(temp, FilePath, null);
                else
                    File.Move(temp, FilePath);
            }
            catch (Exception ex)
            {
                Log.Write("Could not save settings.json: " + ex.Message);
            }
        }

        public string UniqueProfileName(string baseName)
        {
            string name = baseName;
            for (int i = 2; Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); i++)
                name = baseName + " " + i;
            return name;
        }

        public bool HasProfile(string name)
        {
            return Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        void Normalize()
        {
            if (Mode != ModeManual) Mode = ModeAuto;
            ManualPercent = FanCurve.ClampPercent(ManualPercent);
            if (Profiles == null) Profiles = new List<FanProfile>();
            Profiles.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Name));
            if (Profiles.Count == 0)
                Profiles.AddRange(DefaultProfiles());
            foreach (var profile in Profiles)
                profile.Normalize();
            if (!Profiles.Any(p => p.Name == ActiveProfileName))
                ActiveProfileName = Profiles[0].Name;
            Version = CurrentVersion;
        }

        static IEnumerable<FanProfile> DefaultProfiles()
        {
            yield return FanProfile.Create("Balanced", 35, 45, 55, 80, 75, 100);
            yield return FanProfile.Create("Quiet", 50, 25, 65, 45, 80, 80);
            yield return FanProfile.Create("Performance", 30, 50, 50, 80, 70, 100);
        }

        /// <summary>
        /// First run with this file: carry over the per-user .NET settings that earlier
        /// versions used, so nobody loses their curve when upgrading.
        /// </summary>
        static AppSettings MigrateFromUserSettings()
        {
            var settings = new AppSettings
            {
                ControlEnabled = true,
                Mode = ModeAuto,
                ManualPercent = 60,
                MinimizeToTray = true,
                ReleaseOnSleep = true,
                ReleaseOnExit = true,
                LimitFanSpeed = false,
                Profiles = DefaultProfiles().ToList(),
            };
            settings.ActiveProfileName = settings.Profiles[0].Name;

            try
            {
                var old = Properties.Settings.Default;
                if (!HasSavedValues(old))
                    return settings;
                settings.Mode = old.useFanCurve ? ModeAuto : ModeManual;
                settings.ManualPercent = old.fanSpeed;
                settings.MinimizeToTray = old.minimizeToTrayOnClose;
                settings.ReleaseOnSleep = old.fansOffOnSleep;
                settings.ReleaseOnExit = old.turnOffControlOnExit;
                settings.LimitFanSpeed = old.forbidUnsafeSettings;
                settings.StartWithWindows = old.startWithWindows;

                var balanced = settings.Profiles[0];
                balanced.PointCount = old.fanCurvePointCount;
                balanced.UseGpu = old.useGpuCurve;
                balanced.Cpu = new[]
                {
                    new FanSetpoint(old.cpuCurveTemp1, old.cpuCurveFan1),
                    new FanSetpoint(old.cpuCurveTemp2, old.cpuCurveFan2),
                    new FanSetpoint(old.cpuCurveTemp3, old.cpuCurveFan3),
                };
                balanced.Gpu = new[]
                {
                    new FanSetpoint(old.gpuCurveTemp1, old.gpuCurveFan1),
                    new FanSetpoint(old.gpuCurveTemp2, old.gpuCurveFan2),
                    new FanSetpoint(old.gpuCurveTemp3, old.gpuCurveFan3),
                };
                Log.Write("Imported settings from the previous version");
            }
            catch (Exception ex)
            {
                Log.Write("Could not read previous settings: " + ex.Message);
            }
            return settings;
        }

        /// <summary>
        /// True when a per-user settings file from an earlier version exists. Without this
        /// check a fresh install would "import" the old defaults (Manual mode at 90%):
        /// values read from the exe's .config count as saved, so the values themselves
        /// cannot tell us.
        /// </summary>
        static bool HasSavedValues(Properties.Settings old)
        {
            try
            {
                var config = System.Configuration.ConfigurationManager.OpenExeConfiguration(
                    System.Configuration.ConfigurationUserLevel.PerUserRoamingAndLocal);
                return config.HasFile;
            }
            catch (System.Configuration.ConfigurationException)
            {
                return false;
            }
        }

        static void TryBackupBrokenFile()
        {
            try
            {
                File.Copy(FilePath, FilePath + ".broken", true);
            }
            catch (Exception)
            {
            }
        }

        static DataContractJsonSerializer Serializer()
        {
            return new DataContractJsonSerializer(typeof(AppSettings));
        }
    }

    static class AppPaths
    {
        public static string DataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "AsusFanControl");
            }
        }
    }

    /// <summary>
    /// Small rolling log in the data folder, so sleep, startup and EC problems can be
    /// reported without a debugger.
    /// </summary>
    static class Log
    {
        const long MaxBytes = 256 * 1024;
        static readonly object Sync = new object();

        public static string FilePath
        {
            get { return Path.Combine(AppPaths.DataDirectory, "log.txt"); }
        }

        public static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(AppPaths.DataDirectory);
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > MaxBytes)
                        File.Copy(FilePath, Path.Combine(AppPaths.DataDirectory, "log.old.txt"), true);
                    bool append = !(info.Exists && info.Length > MaxBytes);
                    using (var writer = new StreamWriter(FilePath, append, Encoding.UTF8))
                        writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
