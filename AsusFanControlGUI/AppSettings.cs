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
    /// A named fan curve. CPU and GPU each have their own list of 2 to
    /// <see cref="FanCurve.MaxSetpoints"/> points.
    /// </summary>
    [DataContract]
    public sealed class FanProfile
    {
        static readonly FanSetpoint[] DefaultPoints = { new FanSetpoint(35, 45), new FanSetpoint(55, 80), new FanSetpoint(75, 100) };

        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; }

        /// <summary>
        /// Versions before 1.5 always stored three points and used the first 2 or 3 of
        /// them. 0 now: every stored point is used.
        /// </summary>
        [DataMember(Name = "pointCount", Order = 1)]
        public int PointCount { get; set; }

        [DataMember(Name = "useGpu", Order = 2)]
        public bool UseGpu { get; set; }

        [DataMember(Name = "cpu", Order = 3)]
        public FanSetpoint[] Cpu { get; set; }

        [DataMember(Name = "gpu", Order = 4)]
        public FanSetpoint[] Gpu { get; set; }

        /// <param name="pairs">Temperature, percent, temperature, percent, …</param>
        public static FanProfile Create(string name, params int[] pairs)
        {
            var points = new FanSetpoint[pairs.Length / 2];
            for (int i = 0; i < points.Length; i++)
                points[i] = new FanSetpoint(pairs[i * 2], pairs[i * 2 + 1]);
            return new FanProfile
            {
                Name = name,
                UseGpu = false,
                Cpu = points,
                Gpu = (FanSetpoint[])points.Clone(),
            };
        }

        public FanProfile Clone(string newName)
        {
            return new FanProfile
            {
                Name = newName,
                UseGpu = UseGpu,
                Cpu = (FanSetpoint[])Cpu.Clone(),
                Gpu = (FanSetpoint[])Gpu.Clone(),
            };
        }

        internal void Normalize()
        {
            Cpu = FanCurve.Normalize(TrimOld(Cpu), DefaultPoints);
            Gpu = FanCurve.Normalize(TrimOld(Gpu), DefaultPoints);
            PointCount = 0;
        }

        /// <summary>Settings from before 1.5 held step curves; see <see cref="FanCurve.FromSteps"/>.</summary>
        internal void ConvertFromSteps()
        {
            Cpu = FanCurve.Normalize(FanCurve.FromSteps(Cpu), DefaultPoints);
            Gpu = FanCurve.Normalize(FanCurve.FromSteps(Gpu), DefaultPoints);
        }

        FanSetpoint[] TrimOld(FanSetpoint[] points)
        {
            if (points != null && PointCount >= FanCurve.MinSetpoints && PointCount < points.Length)
                return points.Take(PointCount).ToArray();
            return points;
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
        /// <summary>1: step curves (up to 1.4). 2: curves are lines between points.</summary>
        const int CurrentVersion = 2;

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

        /// <summary>From Calibrate RPM; null until the fans have been measured.</summary>
        [DataMember(Name = "rpmCalibration", Order = 11)]
        public RpmCalibration RpmCalibration { get; set; }

        /// <summary>Name of the window theme (see <see cref="Theme.Palettes"/>).</summary>
        [DataMember(Name = "theme", Order = 12)]
        public string Theme { get; set; }

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
                    // Editors (and PowerShell 5's Set-Content -Encoding utf8) add a UTF-8 byte
                    // order mark, which the JSON reader rejects; skip it rather than reset.
                    byte[] bytes = File.ReadAllBytes(FilePath);
                    int skip = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                    using (var stream = new MemoryStream(bytes, skip, bytes.Length - skip))
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
            if (!AsusFanControlGUI.Theme.Palettes.Any(p => p.Name == Theme))
                Theme = AsusFanControlGUI.Theme.DefaultName;
            ManualPercent = FanCurve.ClampPercent(ManualPercent);
            if (Profiles == null) Profiles = new List<FanProfile>();
            Profiles.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Name));
            if (Profiles.Count == 0)
                Profiles.AddRange(DefaultProfiles());
            foreach (var profile in Profiles)
            {
                profile.Normalize();
                if (Version < 2)
                    profile.ConvertFromSteps();
            }
            if (RpmCalibration != null)
            {
                RpmCalibration.Normalize();
                if (!RpmCalibration.IsUsable)
                    RpmCalibration = null;
            }
            if (!Profiles.Any(p => p.Name == ActiveProfileName))
                ActiveProfileName = Profiles[0].Name;
            Version = CurrentVersion;
        }

        static IEnumerable<FanProfile> DefaultProfiles()
        {
            yield return FanProfile.Create("Balanced", 40, 0, 50, 30, 60, 45, 70, 60, 80, 80, 90, 100);
            yield return FanProfile.Create("Quiet", 50, 0, 60, 25, 70, 40, 80, 60, 90, 85, 95, 100);
            yield return FanProfile.Create("Performance", 30, 25, 40, 40, 50, 55, 60, 70, 70, 85, 80, 100);
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
                balanced.PointCount = old.fanCurvePointCount;   // trimmed in Normalize
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
