using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace AsusFanControlGUI
{
    /// <summary>
    /// One point on a fan curve: at this temperature, this fan percent.
    /// </summary>
    [DataContract]
    public struct FanSetpoint
    {
        [DataMember(Name = "tempC", Order = 0)]
        public int TemperatureC;
        [DataMember(Name = "fanPercent", Order = 1)]
        public int FanPercent;

        public FanSetpoint(int temperatureC, int fanPercent)
        {
            TemperatureC = temperatureC;
            FanPercent = fanPercent;
        }
    }

    /// <summary>
    /// Fan curves used by Auto mode, with 2 to <see cref="MaxSetpoints"/> points joined by
    /// straight lines. Below the first point the first point's percent is used, above the
    /// last point the last point's (add a 0% point to have the fans stop when cool).
    ///
    /// The curve only decides one fan percent; the EC never sees the points, so more
    /// points do not mean more EC traffic (see FanController for how writes are limited).
    /// </summary>
    public static class FanCurve
    {
        public const int MinSetpoints = 2;
        public const int MaxSetpoints = 8;
        public const int MinTemperatureC = 0;
        public const int MaxTemperatureC = 110;
        public const int MinFanPercent = 0;
        public const int MaxFanPercent = 100;

        public static int ClampTemperature(int temperatureC)
        {
            if (temperatureC < MinTemperatureC) return MinTemperatureC;
            if (temperatureC > MaxTemperatureC) return MaxTemperatureC;
            return temperatureC;
        }

        public static int ClampPercent(int percent)
        {
            if (percent < MinFanPercent) return MinFanPercent;
            if (percent > MaxFanPercent) return MaxFanPercent;
            return percent;
        }

        /// <summary>
        /// Returns a usable copy: 2 to <see cref="MaxSetpoints"/> points, sorted by
        /// temperature with strictly increasing temperatures, percents within 0–100.
        /// <paramref name="fallback"/> is used when there are fewer than two points.
        /// </summary>
        public static FanSetpoint[] Normalize(FanSetpoint[] points, FanSetpoint[] fallback)
        {
            var list = (points ?? new FanSetpoint[0])
                .Select(p => new FanSetpoint(ClampTemperature(p.TemperatureC), ClampPercent(p.FanPercent)))
                .OrderBy(p => p.TemperatureC)
                .Take(MaxSetpoints)
                .ToList();
            if (list.Count < MinSetpoints)
                return (FanSetpoint[])fallback.Clone();

            for (int i = 1; i < list.Count; i++)
            {
                if (list[i].TemperatureC <= list[i - 1].TemperatureC)
                    list[i] = new FanSetpoint(list[i - 1].TemperatureC + 1, list[i].FanPercent);
            }
            // Pushed past the top: shift down from the end so every point fits.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                int max = MaxTemperatureC - (list.Count - 1 - i);
                if (list[i].TemperatureC > max)
                    list[i] = new FanSetpoint(max, list[i].FanPercent);
                if (i < list.Count - 1 && list[i].TemperatureC >= list[i + 1].TemperatureC)
                    list[i] = new FanSetpoint(list[i + 1].TemperatureC - 1, list[i].FanPercent);
            }
            return list.ToArray();
        }

        /// <summary>
        /// Fan percent for a temperature, or -1 when the temperature is invalid.
        /// <paramref name="points"/> must be sorted (see <see cref="Normalize"/>).
        /// </summary>
        public static int GetFanPercent(int temperatureC, FanSetpoint[] points, bool clampUnsafe)
        {
            if (temperatureC < 0 || points == null || points.Length < MinSetpoints)
                return -1;

            int percent = Interpolate(temperatureC, points);
            return clampUnsafe ? ClampUnsafe(percent) : percent;
        }

        /// <summary>
        /// Turns a step curve from versions before 1.5 into lines with the same feel: the
        /// fans were off below the first step, so a 0% point is added just below it.
        /// </summary>
        public static FanSetpoint[] FromSteps(FanSetpoint[] steps)
        {
            if (steps == null || steps.Length == 0 || steps[0].FanPercent == 0 || steps.Length >= MaxSetpoints)
                return steps;
            var first = steps[0];
            int offAt = Math.Max(MinTemperatureC, first.TemperatureC - 5);
            if (offAt >= first.TemperatureC)
                return steps;
            var list = new List<FanSetpoint> { new FanSetpoint(offAt, 0) };
            list.AddRange(steps);
            return list.ToArray();
        }

        static int Interpolate(int temperatureC, IList<FanSetpoint> points)
        {
            if (temperatureC <= points[0].TemperatureC)
                return ClampPercent(points[0].FanPercent);
            for (int i = 1; i < points.Count; i++)
            {
                FanSetpoint a = points[i - 1], b = points[i];
                if (temperatureC <= b.TemperatureC)
                {
                    double t = (temperatureC - a.TemperatureC) / (double)(b.TemperatureC - a.TemperatureC);
                    return ClampPercent((int)Math.Round(a.FanPercent + t * (b.FanPercent - a.FanPercent)));
                }
            }
            return ClampPercent(points[points.Count - 1].FanPercent);
        }

        /// <summary>
        /// Higher request wins. A negative percent means that curve has no reading.
        /// </summary>
        public static int Combine(int cpuPercent, int gpuPercent)
        {
            if (cpuPercent < 0) return gpuPercent;
            if (gpuPercent < 0) return cpuPercent;
            return Math.Max(cpuPercent, gpuPercent);
        }

        private static int ClampUnsafe(int percent)
        {
            if (percent <= 0) return 0;
            return Math.Max(40, Math.Min(99, percent));
        }
    }

    /// <summary>A measured fan speed: at this percent the fans ran at this RPM.</summary>
    [DataContract]
    public struct RpmPoint
    {
        [DataMember(Name = "percent", Order = 0)]
        public int Percent;
        [DataMember(Name = "rpm", Order = 1)]
        public int Rpm;

        public RpmPoint(int percent, int rpm)
        {
            Percent = percent;
            Rpm = rpm;
        }
    }

    /// <summary>
    /// Measured fan RPM at a set of fan percents (from Calibrate RPM), so the graph can
    /// show RPM. The fans are still driven by percent; this only converts for display
    /// and dragging.
    /// </summary>
    [DataContract]
    public sealed class RpmCalibration
    {
        [DataMember(Name = "measuredAt", Order = 0)]
        public string MeasuredAt { get; set; }

        /// <summary>Average RPM of all fans at each measured percent, sorted by percent.</summary>
        [DataMember(Name = "points", Order = 1)]
        public RpmPoint[] Points { get; set; }

        public bool IsUsable
        {
            get { return Points != null && Points.Length >= 2 && MaxRpm > 0; }
        }

        public int MaxRpm
        {
            get { return Points == null || Points.Length == 0 ? 0 : Points.Max(p => p.Rpm); }
        }

        /// <summary>Makes the table start at 0% / 0 RPM and never decrease.</summary>
        public void Normalize()
        {
            var list = (Points ?? new RpmPoint[0])
                .Where(p => p.Percent >= 0 && p.Percent <= 100)
                .OrderBy(p => p.Percent)
                .ToList();
            if (list.Count == 0 || list[0].Percent != 0)
                list.Insert(0, new RpmPoint(0, 0));
            int highest = 0;
            for (int i = 0; i < list.Count; i++)
            {
                highest = Math.Max(highest, Math.Max(0, list[i].Rpm));
                list[i] = new RpmPoint(list[i].Percent, highest);
            }
            Points = list.ToArray();
        }

        public int RpmAt(int percent)
        {
            percent = FanCurve.ClampPercent(percent);
            for (int i = 1; i < Points.Length; i++)
            {
                RpmPoint a = Points[i - 1], b = Points[i];
                if (percent <= b.Percent)
                {
                    double t = b.Percent == a.Percent ? 1 : (percent - a.Percent) / (double)(b.Percent - a.Percent);
                    return (int)Math.Round(a.Rpm + t * (b.Rpm - a.Rpm));
                }
            }
            return Points[Points.Length - 1].Rpm;
        }

        /// <summary>Smallest percent that reaches <paramref name="rpm"/>.</summary>
        public int PercentAt(int rpm)
        {
            if (rpm <= 0)
                return 0;
            for (int i = 1; i < Points.Length; i++)
            {
                RpmPoint a = Points[i - 1], b = Points[i];
                if (rpm <= b.Rpm)
                {
                    if (b.Rpm == a.Rpm)
                        return b.Percent;
                    double t = (rpm - a.Rpm) / (double)(b.Rpm - a.Rpm);
                    return FanCurve.ClampPercent((int)Math.Round(a.Percent + t * (b.Percent - a.Percent)));
                }
            }
            return FanCurve.MaxFanPercent;
        }
    }
}
