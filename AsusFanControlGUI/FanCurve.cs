using System;

namespace AsusFanControlGUI
{
    /// <summary>
    /// Fixed temperature ranges: &lt; 35°C → 0%, 35–55°C → 45%, 55–75°C → 80%, ≥ 75°C → 100%.
    /// </summary>
    public static class FanCurve
    {
        public static int GetFanPercentForTemperature(int temp, bool clampUnsafe = false)
        {
            int percent;
            if (temp < 35) percent = 0;
            else if (temp < 55) percent = 45;
            else if (temp < 75) percent = 80;
            else percent = 100;
            return clampUnsafe ? ClampUnsafe(percent) : percent;
        }

        private static int ClampUnsafe(int percent)
        {
            if (percent <= 0) return 0;
            return Math.Max(40, Math.Min(99, percent));
        }
    }
}
