using AsusFanControl;
using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AsusFanControlGUI
{
    public partial class Form1 : Form
    {
        AsusControl asusControl = new AsusControl();
        int fanSpeed = 0;
        Timer timer;
        NotifyIcon trayIcon;

        int? _pendingFanPercent;
        DateTime _pendingSinceUtc;
        const int DebounceSeconds = 5;

        public Form1()
        {
            InitializeComponent();
            AppDomain.CurrentDomain.ProcessExit += new EventHandler(OnProcessExit);

            toolStripMenuItemTurnOffControlOnExit.Checked = Properties.Settings.Default.turnOffControlOnExit;
            toolStripMenuItemForbidUnsafeSettings.Checked = Properties.Settings.Default.forbidUnsafeSettings;
            toolStripMenuItemMinimizeToTrayOnClose.Checked = Properties.Settings.Default.minimizeToTrayOnClose;
            toolStripMenuItemAutoRefreshStats.Checked = Properties.Settings.Default.autoRefreshStats;
            trackBarFanSpeed.Value = Properties.Settings.Default.fanSpeed;

            radioManual.Checked = !Properties.Settings.Default.useFanCurve;
            radioFanCurve.Checked = Properties.Settings.Default.useFanCurve;
            panelManual.Visible = radioManual.Checked;
            panelFanCurve.Visible = radioFanCurve.Checked;
        }

        private void OnProcessExit(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.turnOffControlOnExit)
                asusControl.SetFanSpeeds(0);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            timerRefreshStats();
            // Show current temp and fan % in Auto mode immediately
            buttonRefreshCPUTemp_Click(sender, e);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (Properties.Settings.Default.minimizeToTrayOnClose && Visible)
            {
                if (trayIcon == null)
                {
                    trayIcon = new NotifyIcon()
                    {
                        Icon = Icon,
                        ContextMenu = new ContextMenu(new MenuItem[] {
                            new MenuItem("Show", (s1, e1) =>
                            {
                                trayIcon.Visible = false;
                                Show();
                            }),
                            new MenuItem("Exit", (s1, e1) =>
                            {
                                Close();
                                trayIcon.Visible = false;
                                Application.Exit();
                            }),
                        }),
                    };

                    trayIcon.MouseClick += (s1, e1) =>
                    {
                        if (e1.Button != MouseButtons.Left)
                            return;
                        trayIcon.Visible = false;
                        Show();
                    };
                }
                trayIcon.Visible = true;
                e.Cancel = true;
                Hide();
            }
        }

        private const int TimerIntervalMs = 5000; // 5 seconds - reduces hardware polling and UI blocking

        private void timerRefreshStats()
        {
            if (timer != null)
            {
                timer.Stop();
                timer = null;
            }
            if (!Properties.Settings.Default.autoRefreshStats &&
                !(radioFanCurve.Checked && checkBoxTurnOn.Checked))
                return;

            timer = new Timer();
            timer.Interval = TimerIntervalMs;
            timer.Tick += TimerEventProcessor;
            timer.Start();
        }

        private void TimerEventProcessor(object sender, EventArgs e)
        {
            // Run hardware reads on a background thread so the UI stays responsive
            Task.Run(() =>
            {
                int temp = asusControl.GetCpuTemperatureCelsius();
                string rpmText = string.Join(" ", asusControl.GetFanSpeeds());

                int newPercent = -1;
                if (radioFanCurve.Checked && temp >= 0 && checkBoxTurnOn.Checked)
                {
                    bool clampUnsafe = Properties.Settings.Default.forbidUnsafeSettings;
                    newPercent = FanCurve.GetFanPercentForTemperature(temp, clampUnsafe);
                }

                int tempForCapture = temp;
                bool inAuto = radioFanCurve.Checked;
                string curveStatus = temp >= 0
                    ? $"Current: {temp} °C → Fan: {FanCurve.GetFanPercentForTemperature(temp, Properties.Settings.Default.forbidUnsafeSettings)}%"
                    : "Current: N/A (temp sensor unavailable)";

                BeginInvoke(new Action(() =>
                {
                    labelCPUTemp.Text = tempForCapture >= 0 ? $"{tempForCapture}" : "N/A";
                    labelRPM.Text = rpmText;
                    if (inAuto)
                    {
                        labelCurveStatus.Text = curveStatus;
                        if (newPercent >= 0 && checkBoxTurnOn.Checked)
                        {
                            if (newPercent == fanSpeed)
                            {
                                _pendingFanPercent = null;
                            }
                            else
                            {
                                DateTime now = DateTime.UtcNow;
                                if (newPercent != _pendingFanPercent)
                                {
                                    _pendingFanPercent = newPercent;
                                    _pendingSinceUtc = now;
                                }
                                else if ((now - _pendingSinceUtc).TotalSeconds >= DebounceSeconds)
                                {
                                    fanSpeed = newPercent;
                                    asusControl.SetFanSpeeds(newPercent);
                                    _pendingFanPercent = null;
                                }
                            }
                        }
                    }
                }));
            });
        }

        private void toolStripMenuItemTurnOffControlOnExit_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.turnOffControlOnExit = toolStripMenuItemTurnOffControlOnExit.Checked;
            Properties.Settings.Default.Save();
        }

        private void toolStripMenuItemForbidUnsafeSettings_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.forbidUnsafeSettings = toolStripMenuItemForbidUnsafeSettings.Checked;
            Properties.Settings.Default.Save();
        }

        private void toolStripMenuItemMinimizeToTrayOnClose_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.minimizeToTrayOnClose = toolStripMenuItemMinimizeToTrayOnClose.Checked;
            Properties.Settings.Default.Save();
        }

        private void toolStripMenuItemAutoRefreshStats_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.autoRefreshStats = toolStripMenuItemAutoRefreshStats.Checked;
            Properties.Settings.Default.Save();
            timerRefreshStats();
        }

        private void toolStripMenuItemCheckForUpdates_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/Karmel0x/AsusFanControl/releases");
        }

        private void setFanSpeed()
        {
            if (!checkBoxTurnOn.Checked)
            {
                if (fanSpeed != 0)
                {
                    fanSpeed = 0;
                    asusControl.SetFanSpeeds(0);
                }
                _pendingFanPercent = null;
                labelValue.Text = "turned off";
                return;
            }

            if (radioManual.Checked)
            {
                var value = trackBarFanSpeed.Value;
                Properties.Settings.Default.fanSpeed = value;
                Properties.Settings.Default.Save();
                if (value == 0)
                    labelValue.Text = "turned off";
                else
                    labelValue.Text = value.ToString();
                if (fanSpeed == value) return;
                fanSpeed = value;
                asusControl.SetFanSpeeds(value);
            }
            else if (radioFanCurve.Checked)
            {
                int temp = asusControl.GetCpuTemperatureCelsius();
                if (temp >= 0)
                {
                    bool clampUnsafe = Properties.Settings.Default.forbidUnsafeSettings;
                    int percent = FanCurve.GetFanPercentForTemperature(temp, clampUnsafe);
                    fanSpeed = percent;
                    asusControl.SetFanSpeeds(percent);
                    labelCurveStatus.Text = $"Current: {temp} °C → Fan: {percent}%";
                }
                else
                {
                    labelCurveStatus.Text = "Current: N/A (temp sensor unavailable)";
                }
            }
        }

        private void checkBoxTurnOn_CheckedChanged(object sender, EventArgs e)
        {
            setFanSpeed();
            timerRefreshStats();
        }

        private void trackBarFanSpeed_MouseCaptureChanged(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.forbidUnsafeSettings)
            {
                if (trackBarFanSpeed.Value < 40)
                    trackBarFanSpeed.Value = 40;
                else if (trackBarFanSpeed.Value > 99)
                    trackBarFanSpeed.Value = 99;
            }
            setFanSpeed();
        }

        private void trackBarFanSpeed_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Left && e.KeyCode != Keys.Right) return;
            trackBarFanSpeed_MouseCaptureChanged(sender, e);
        }

        private void buttonRefreshRPM_Click(object sender, EventArgs e)
        {
            labelRPM.Text = string.Join(" ", asusControl.GetFanSpeeds());
        }

        private void buttonRefreshCPUTemp_Click(object sender, EventArgs e)
        {
            int tempC = asusControl.GetCpuTemperatureCelsius();
            labelCPUTemp.Text = tempC >= 0 ? $"{tempC}" : "N/A";
            if (radioFanCurve.Checked && tempC >= 0)
            {
                bool clampUnsafe = Properties.Settings.Default.forbidUnsafeSettings;
                int percent = FanCurve.GetFanPercentForTemperature(tempC, clampUnsafe);
                labelCurveStatus.Text = $"Current: {tempC} °C → Fan: {percent}%";
                if (checkBoxTurnOn.Checked && fanSpeed != percent)
                {
                    fanSpeed = percent;
                    asusControl.SetFanSpeeds(percent);
                }
            }
            else if (radioFanCurve.Checked)
            {
                if (tempC < 0)
                    labelCurveStatus.Text = "Current: N/A (temp sensor unavailable)";
            }
        }

        private void radioMode_CheckedChanged(object sender, EventArgs e)
        {
            if (radioManual.Checked)
            {
                panelManual.Visible = true;
                panelFanCurve.Visible = false;
                Properties.Settings.Default.useFanCurve = false;
                _pendingFanPercent = null;
            }
            else
            {
                panelManual.Visible = false;
                panelFanCurve.Visible = true;
                Properties.Settings.Default.useFanCurve = true;
                int temp = asusControl.GetCpuTemperatureCelsius();
                if (temp >= 0)
                {
                    bool clampUnsafe = Properties.Settings.Default.forbidUnsafeSettings;
                    int percent = FanCurve.GetFanPercentForTemperature(temp, clampUnsafe);
                    labelCurveStatus.Text = $"Current: {temp} °C → Fan: {percent}%";
                    if (checkBoxTurnOn.Checked)
                    {
                        fanSpeed = percent;
                        asusControl.SetFanSpeeds(percent);
                    }
                }
                else
                {
                    labelCurveStatus.Text = "Current: N/A (temp sensor unavailable)";
                }
            }
            Properties.Settings.Default.Save();
            timerRefreshStats();
            setFanSpeed();
        }
    }
}
