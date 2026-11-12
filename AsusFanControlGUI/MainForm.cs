using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AsusFanControlGUI
{
    /// <summary>
    /// Main window. Layout is built in code so it stays readable and adapts to DPI and
    /// to which mode is shown.
    /// </summary>
    public sealed class MainForm : Form
    {
        const int ContentWidth = 440;
        const string ProjectUrl = "https://github.com/Karmel0x/AsusFanControl";

        readonly AppSettings _settings;
        readonly FanController _controller = new FanController();
        readonly SleepWatcher _sleepWatcher = new SleepWatcher();
        readonly bool _startHidden;
        bool _allowShow;
        bool _loading = true;
        bool _updatingEditors;
        bool _exiting;
        FanStatus _lastStatus;

        // Menu
        ToolStripMenuItem _menuStartWithWindows, _menuMinimizeToTray, _menuReleaseOnSleep,
            _menuReleaseOnExit, _menuLimit;

        // Status
        Label _cpuValue, _gpuValue, _fanValue, _targetValue;
        ToolStripStatusLabel _statusText;

        // Control
        CheckBox _controlCheck;
        RadioButton _autoRadio, _manualRadio;

        // Manual
        GroupBox _manualGroup;
        TrackBar _manualTrack;
        Label _manualValue;

        // Curve
        GroupBox _curveGroup;
        ComboBox _profileCombo, _stepsCombo;
        Button _newProfileButton, _renameProfileButton, _deleteProfileButton;
        CheckBox _useGpuCheck;
        RadioButton _editCpuRadio, _editGpuRadio;
        readonly Label[] _stepLabels = new Label[FanCurve.MaxSetpoints];
        readonly NumericUpDown[] _tempEdits = new NumericUpDown[FanCurve.MaxSetpoints];
        readonly NumericUpDown[] _fanEdits = new NumericUpDown[FanCurve.MaxSetpoints];
        CurvePreview _preview;

        // Tray
        NotifyIcon _tray;
        ToolStripMenuItem _trayControl, _trayProfiles;

        public MainForm(bool startHidden)
        {
            _startHidden = startHidden;
            _settings = AppSettings.Load();

            Text = "Asus Fan Control";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            BuildLayout();
            BuildTray();
            LoadIntoControls();
            _loading = false;

            _controller.StatusChanged += OnControllerStatus;
            _sleepWatcher.Sleeping += () => _controller.OnSleep(_settings.ReleaseOnSleep);
            _sleepWatcher.Woke += () => _controller.OnWake();
            ApplyToController();
            _controller.Start();

            if (_settings.StartWithWindows)
            {
                // Re-create the task each start so it follows the exe if it was moved.
                string error;
                if (!WindowsStartup.Enable(out error))
                    Log.Write("Start with Windows: " + error);
            }
        }

        public void EmergencyRelease()
        {
            _controller.EmergencyRelease();
        }

        // ---------------------------------------------------------------- layout

        void BuildLayout()
        {
            var menu = new MenuStrip { Dock = DockStyle.Top };
            var settingsMenu = new ToolStripMenuItem("&Settings");
            _menuStartWithWindows = CheckItem("Start with Windows", OnStartWithWindowsClicked);
            _menuMinimizeToTray = CheckItem("Keep running in the notification area when closed", (s, e) =>
            {
                _settings.MinimizeToTray = _menuMinimizeToTray.Checked;
                _settings.Save();
            });
            _menuReleaseOnSleep = CheckItem("Hand fans to the BIOS during sleep", (s, e) =>
            {
                _settings.ReleaseOnSleep = _menuReleaseOnSleep.Checked;
                _settings.Save();
            });
            _menuReleaseOnExit = CheckItem("Hand fans to the BIOS on exit", (s, e) =>
            {
                _settings.ReleaseOnExit = _menuReleaseOnExit.Checked;
                _settings.Save();
            });
            _menuLimit = CheckItem("Limit fan speed to 40–99% (0% still stops the fans)", (s, e) =>
            {
                _settings.LimitFanSpeed = _menuLimit.Checked;
                _settings.Save();
                UpdateManualLabel();
                ApplyToController();
            });
            settingsMenu.DropDownItems.AddRange(new ToolStripItem[]
            {
                _menuStartWithWindows, _menuMinimizeToTray, new ToolStripSeparator(),
                _menuReleaseOnSleep, _menuReleaseOnExit, _menuLimit, new ToolStripSeparator(),
                new ToolStripMenuItem("Open settings and log folder", null, (s, e) => OpenDataFolder()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("E&xit", null, (s, e) => ExitApplication()),
            });
            var helpMenu = new ToolStripMenuItem("&Help");
            helpMenu.DropDownItems.Add(new ToolStripMenuItem("Project page", null, (s, e) => OpenUrl(ProjectUrl)));
            helpMenu.DropDownItems.Add(new ToolStripMenuItem("Check for updates", null, (s, e) => OpenUrl(ProjectUrl + "/releases")));
            menu.Items.Add(settingsMenu);
            menu.Items.Add(helpMenu);

            var status = new StatusStrip { SizingGrip = false };
            _statusText = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            status.Items.Add(_statusText);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(12, 8, 12, 8),
                AutoSize = true,
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth));
            root.Controls.Add(BuildStatusGroup());
            root.Controls.Add(BuildControlRow());
            root.Controls.Add(_manualGroup = BuildManualGroup());
            root.Controls.Add(_curveGroup = BuildCurveGroup());

            Controls.Add(root);
            Controls.Add(status);
            Controls.Add(menu);
            MainMenuStrip = menu;
        }

        Control BuildStatusGroup()
        {
            var group = Group("Now");
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, AutoSize = true };
            // Two fan readings ("2730 · 2736") need the widest column.
            foreach (float width in new[] { 20F, 20F, 35F, 25F })
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
            string[] captions = { "CPU", "GPU", "Fans (RPM)", "Fan speed" };
            var values = new Label[4];
            for (int i = 0; i < 4; i++)
            {
                table.Controls.Add(new Label { Text = captions[i], AutoSize = true, ForeColor = SystemColors.GrayText }, i, 0);
                values[i] = new Label
                {
                    Text = "--",
                    AutoSize = true,
                    Font = new Font("Segoe UI Semibold", 12F),
                    Margin = new Padding(3, 0, 3, 3),
                };
                table.Controls.Add(values[i], i, 1);
            }
            _cpuValue = values[0];
            _gpuValue = values[1];
            _fanValue = values[2];
            _targetValue = values[3];
            group.Controls.Add(table);
            return group;
        }

        Control BuildControlRow()
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 2) };
            _controlCheck = new CheckBox
            {
                Text = "Control fans",
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(3, 4, 24, 3),
            };
            _controlCheck.CheckedChanged += (s, e) =>
            {
                if (_loading) return;
                _settings.ControlEnabled = _controlCheck.Checked;
                _settings.Save();
                UpdateEnabledState();
                ApplyToController();
            };
            _autoRadio = new RadioButton { Text = "Auto (fan curve)", AutoSize = true, Margin = new Padding(3, 4, 12, 3) };
            _manualRadio = new RadioButton { Text = "Manual", AutoSize = true, Margin = new Padding(3, 4, 3, 3) };
            _autoRadio.CheckedChanged += OnModeChanged;
            _manualRadio.CheckedChanged += OnModeChanged;
            row.Controls.AddRange(new Control[] { _controlCheck, _autoRadio, _manualRadio });
            return row;
        }

        GroupBox BuildManualGroup()
        {
            var group = Group("Manual speed");
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            _manualTrack = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                SmallChange = 1,
                LargeChange = 10,
                Dock = DockStyle.Fill,
            };
            _manualTrack.ValueChanged += (s, e) =>
            {
                UpdateManualLabel();
                if (_loading) return;
                _settings.ManualPercent = _manualTrack.Value;
                _settings.Save();
                ApplyToController();
            };
            _manualValue = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 11F), Anchor = AnchorStyles.Left, Margin = new Padding(6, 8, 3, 3) };
            table.Controls.Add(_manualTrack, 0, 0);
            table.Controls.Add(_manualValue, 1, 0);
            table.Controls.Add(Hint("0% stops the fans. Untick “Control fans” to hand them back to the BIOS."), 0, 1);
            table.SetColumnSpan(table.GetControlFromPosition(0, 1), 2);
            group.Controls.Add(table);
            return group;
        }

        GroupBox BuildCurveGroup()
        {
            var group = Group("Fan curve");
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };

            // Profile row
            var profileRow = Flow();
            _profileCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(3, 4, 6, 3) };
            _profileCombo.SelectedIndexChanged += OnProfileSelected;
            _newProfileButton = SmallButton("New…", (s, e) => NewProfile());
            _renameProfileButton = SmallButton("Rename…", (s, e) => RenameProfile());
            _deleteProfileButton = SmallButton("Delete", (s, e) => DeleteProfile());
            profileRow.Controls.AddRange(new Control[]
            {
                RowLabel("Profile"), _profileCombo, _newProfileButton, _renameProfileButton, _deleteProfileButton,
            });
            table.Controls.Add(profileRow);

            // Options row
            var optionsRow = Flow();
            _stepsCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 48, Margin = new Padding(3, 4, 18, 3) };
            _stepsCombo.Items.AddRange(new object[] { "2", "3" });
            _stepsCombo.SelectedIndexChanged += OnStepsChanged;
            _useGpuCheck = new CheckBox { Text = "Also follow GPU temperature", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
            _useGpuCheck.CheckedChanged += OnUseGpuChanged;
            optionsRow.Controls.AddRange(new Control[] { RowLabel("Steps"), _stepsCombo, _useGpuCheck });
            table.Controls.Add(optionsRow);

            // Which curve is being edited
            var editRow = Flow();
            _editCpuRadio = new RadioButton { Text = "CPU curve", AutoSize = true, Checked = true, Margin = new Padding(3, 4, 12, 3) };
            _editGpuRadio = new RadioButton { Text = "GPU curve", AutoSize = true, Margin = new Padding(3, 4, 3, 3) };
            _editCpuRadio.CheckedChanged += OnEditTargetChanged;
            _editGpuRadio.CheckedChanged += OnEditTargetChanged;
            editRow.Controls.AddRange(new Control[] { RowLabel("Edit"), _editCpuRadio, _editGpuRadio });
            table.Controls.Add(editRow);

            // Steps grid + preview side by side
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 4, 8, 0) };
            grid.Controls.Add(new Label { Text = "", AutoSize = true }, 0, 0);
            grid.Controls.Add(new Label { Text = "From °C", AutoSize = true, ForeColor = SystemColors.GrayText }, 1, 0);
            grid.Controls.Add(new Label { Text = "Fan %", AutoSize = true, ForeColor = SystemColors.GrayText }, 2, 0);
            for (int i = 0; i < FanCurve.MaxSetpoints; i++)
            {
                _stepLabels[i] = new Label { Text = "Step " + (i + 1), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 3) };
                _tempEdits[i] = Numeric(FanCurve.MinTemperatureC, FanCurve.MaxTemperatureC);
                _fanEdits[i] = Numeric(FanCurve.MinFanPercent, FanCurve.MaxFanPercent);
                grid.Controls.Add(_stepLabels[i], 0, i + 1);
                grid.Controls.Add(_tempEdits[i], 1, i + 1);
                grid.Controls.Add(_fanEdits[i], 2, i + 1);
            }
            _preview = new CurvePreview { Dock = DockStyle.Fill, MinimumSize = new Size(180, 120), Margin = new Padding(0, 4, 0, 0) };
            body.Controls.Add(grid, 0, 0);
            body.Controls.Add(_preview, 1, 0);
            table.Controls.Add(body);

            table.Controls.Add(Hint("Below the first step the fans are off. A new speed is applied once the temperature has stayed in a step for 5 seconds; changes you make apply right away."));
            group.Controls.Add(table);
            return group;
        }

        void BuildTray()
        {
            var menu = new ContextMenuStrip();
            var show = new ToolStripMenuItem("Show", null, (s, e) => ShowWindow());
            show.Font = new Font(show.Font, FontStyle.Bold);
            _trayControl = new ToolStripMenuItem("Control fans", null, (s, e) => _controlCheck.Checked = !_controlCheck.Checked);
            _trayProfiles = new ToolStripMenuItem("Profile");
            menu.Items.AddRange(new ToolStripItem[]
            {
                show, new ToolStripSeparator(), _trayControl, _trayProfiles, new ToolStripSeparator(),
                new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication()),
            });
            menu.Opening += (s, e) =>
            {
                _trayControl.Checked = _settings.ControlEnabled;
                _trayProfiles.DropDownItems.Clear();
                foreach (var profile in _settings.Profiles)
                {
                    string name = profile.Name;
                    var item = new ToolStripMenuItem(name, null, (s2, e2) => SelectProfile(name))
                    {
                        Checked = _settings.IsAuto && name == _settings.ActiveProfileName,
                    };
                    _trayProfiles.DropDownItems.Add(item);
                }
            };

            _tray = new NotifyIcon
            {
                Icon = Icon,
                Text = "Asus Fan Control",
                ContextMenuStrip = menu,
                Visible = true,
            };
            _tray.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    ShowWindow();
            };
        }

        // ---------------------------------------------------------------- state <-> controls

        void LoadIntoControls()
        {
            _menuStartWithWindows.Checked = _settings.StartWithWindows;
            _menuMinimizeToTray.Checked = _settings.MinimizeToTray;
            _menuReleaseOnSleep.Checked = _settings.ReleaseOnSleep;
            _menuReleaseOnExit.Checked = _settings.ReleaseOnExit;
            _menuLimit.Checked = _settings.LimitFanSpeed;

            _controlCheck.Checked = _settings.ControlEnabled;
            _autoRadio.Checked = _settings.IsAuto;
            _manualRadio.Checked = !_settings.IsAuto;
            _manualTrack.Value = _settings.ManualPercent;
            UpdateManualLabel();

            RefreshProfileList();
            PushProfileToEditors();
            UpdateModeVisibility();
            UpdateEnabledState();
        }

        void RefreshProfileList()
        {
            bool wasLoading = _loading;
            _loading = true;
            _profileCombo.BeginUpdate();
            _profileCombo.Items.Clear();
            foreach (var profile in _settings.Profiles)
                _profileCombo.Items.Add(profile.Name);
            _profileCombo.SelectedItem = _settings.ActiveProfile.Name;
            _profileCombo.EndUpdate();
            _deleteProfileButton.Enabled = _settings.Profiles.Count > 1;
            _loading = wasLoading;
        }

        FanSetpoint[] EditedPoints
        {
            get
            {
                var profile = _settings.ActiveProfile;
                return _editGpuRadio.Checked ? profile.Gpu : profile.Cpu;
            }
        }

        void PushProfileToEditors()
        {
            var profile = _settings.ActiveProfile;
            _updatingEditors = true;
            try
            {
                _stepsCombo.SelectedIndex = profile.PointCount == FanCurve.MinSetpoints ? 0 : 1;
                _useGpuCheck.Checked = profile.UseGpu;
                _editGpuRadio.Enabled = profile.UseGpu;
                if (!profile.UseGpu)
                    _editCpuRadio.Checked = true;

                var points = EditedPoints;
                for (int i = 0; i < FanCurve.MaxSetpoints; i++)
                {
                    bool visible = i < profile.PointCount;
                    _stepLabels[i].Visible = visible;
                    _tempEdits[i].Visible = visible;
                    _fanEdits[i].Visible = visible;
                    _tempEdits[i].Value = points[i].TemperatureC;
                    _fanEdits[i].Value = points[i].FanPercent;
                }
            }
            finally
            {
                _updatingEditors = false;
            }
            UpdatePreview();
        }

        /// <summary>Reads the editors into the curve being edited.</summary>
        void CommitEditors()
        {
            var profile = _settings.ActiveProfile;
            var points = EditedPoints;
            bool wasUpdating = _updatingEditors;
            _updatingEditors = true;
            try
            {
                for (int i = 0; i < profile.PointCount; i++)
                {
                    points[i].TemperatureC = (int)_tempEdits[i].Value;
                    points[i].FanPercent = (int)_fanEdits[i].Value;
                }
            }
            finally
            {
                _updatingEditors = wasUpdating;
            }
            FanCurve.Normalize(points, profile.PointCount);
        }

        /// <summary>
        /// Commits numbers the user typed but has not confirmed with Enter or Tab: reading
        /// NumericUpDown.Value validates the pending text and raises ValueChanged, which
        /// saves through <see cref="OnCurveEdited"/>. Without this, a typed value was lost
        /// when the window was closed straight away.
        /// </summary>
        void FlushTypedValues()
        {
            if (_loading)
                return;
            foreach (var numeric in _tempEdits.Concat(_fanEdits))
            {
                var unused = numeric.Value;
            }
        }

        void OnCurveEdited(object sender, EventArgs e)
        {
            if (_loading || _updatingEditors)
                return;
            CommitEditors();
            PushProfileToEditors();
            _settings.Save();
            ApplyToController();
        }

        void OnStepsChanged(object sender, EventArgs e)
        {
            if (_loading || _updatingEditors)
                return;
            var profile = _settings.ActiveProfile;
            int newCount = _stepsCombo.SelectedIndex == 0 ? FanCurve.MinSetpoints : FanCurve.MaxSetpoints;
            if (newCount == profile.PointCount)
                return;

            CommitEditors();
            foreach (var points in new[] { profile.Cpu, profile.Gpu })
            {
                if (newCount == FanCurve.MinSetpoints)
                {
                    points[1] = points[2];
                }
                else
                {
                    points[2] = points[1];
                    points[1] = FanCurve.Midpoint(points[0], points[2]);
                }
                FanCurve.Normalize(points, newCount);
            }
            profile.PointCount = newCount;
            PushProfileToEditors();
            _settings.Save();
            ApplyToController();
        }

        void OnUseGpuChanged(object sender, EventArgs e)
        {
            if (_loading || _updatingEditors)
                return;
            CommitEditors();
            _settings.ActiveProfile.UseGpu = _useGpuCheck.Checked;
            PushProfileToEditors();
            _settings.Save();
            ApplyToController();
        }

        void OnEditTargetChanged(object sender, EventArgs e)
        {
            var radio = sender as RadioButton;
            if (_loading || _updatingEditors || radio == null || !radio.Checked)
                return;
            // The editors still show the other curve; save it before switching.
            _updatingEditors = true;
            var other = radio == _editGpuRadio ? _settings.ActiveProfile.Cpu : _settings.ActiveProfile.Gpu;
            for (int i = 0; i < _settings.ActiveProfile.PointCount; i++)
            {
                other[i].TemperatureC = (int)_tempEdits[i].Value;
                other[i].FanPercent = (int)_fanEdits[i].Value;
            }
            FanCurve.Normalize(other, _settings.ActiveProfile.PointCount);
            _updatingEditors = false;
            PushProfileToEditors();
            _settings.Save();
        }

        void OnModeChanged(object sender, EventArgs e)
        {
            var radio = sender as RadioButton;
            if (_loading || radio == null || !radio.Checked)
                return;
            _settings.Mode = _autoRadio.Checked ? AppSettings.ModeAuto : AppSettings.ModeManual;
            _settings.Save();
            UpdateModeVisibility();
            ApplyToController();
        }

        void OnProfileSelected(object sender, EventArgs e)
        {
            if (_loading || _profileCombo.SelectedItem == null)
                return;
            FlushTypedValues();
            SelectProfile((string)_profileCombo.SelectedItem);
        }

        void SelectProfile(string name)
        {
            _settings.ActiveProfileName = name;
            if (!_settings.IsAuto)
            {
                _settings.Mode = AppSettings.ModeAuto;
                _autoRadio.Checked = true;
            }
            _settings.Save();
            RefreshProfileList();
            PushProfileToEditors();
            ApplyToController();
        }

        void NewProfile()
        {
            FlushTypedValues();
            string name = InputDialog.Prompt(this, "New profile", "Name for a copy of “" + _settings.ActiveProfile.Name + "”:",
                _settings.UniqueProfileName(_settings.ActiveProfile.Name + " copy"));
            if (name == null)
                return;
            if (_settings.HasProfile(name))
            {
                MessageBox.Show(this, "A profile named “" + name + "” already exists.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _settings.Profiles.Add(_settings.ActiveProfile.Clone(name));
            SelectProfile(name);
        }

        void RenameProfile()
        {
            var profile = _settings.ActiveProfile;
            string name = InputDialog.Prompt(this, "Rename profile", "New name:", profile.Name);
            if (name == null || name == profile.Name)
                return;
            if (_settings.HasProfile(name) && !string.Equals(name, profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "A profile named “" + name + "” already exists.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            profile.Name = name;
            _settings.ActiveProfileName = name;
            _settings.Save();
            RefreshProfileList();
        }

        void DeleteProfile()
        {
            if (_settings.Profiles.Count <= 1)
                return;
            var profile = _settings.ActiveProfile;
            if (MessageBox.Show(this, "Delete the profile “" + profile.Name + "”?", Text,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            _settings.Profiles.Remove(profile);
            SelectProfile(_settings.Profiles[0].Name);
        }

        void OnStartWithWindowsClicked(object sender, EventArgs e)
        {
            bool enable = _menuStartWithWindows.Checked;
            if (enable)
            {
                string error;
                if (!WindowsStartup.Enable(out error))
                {
                    _menuStartWithWindows.Checked = false;
                    MessageBox.Show(this, "Could not turn on Start with Windows.\r\n\r\n" + error, Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            else
            {
                WindowsStartup.Disable();
            }
            _settings.StartWithWindows = enable;
            _settings.Save();
        }

        void ApplyToController()
        {
            var profile = _settings.ActiveProfile;
            _controller.Configure(new FanTargetConfig
            {
                Enabled = _settings.ControlEnabled,
                Auto = _settings.IsAuto,
                ManualPercent = _settings.ManualPercent,
                Limit = _settings.LimitFanSpeed,
                PointCount = profile.PointCount,
                UseGpu = profile.UseGpu,
                Cpu = (FanSetpoint[])profile.Cpu.Clone(),
                Gpu = (FanSetpoint[])profile.Gpu.Clone(),
            });
            UpdateStatusText();
        }

        // ---------------------------------------------------------------- view updates

        void UpdateModeVisibility()
        {
            SuspendLayout();
            _manualGroup.Visible = !_settings.IsAuto;
            _curveGroup.Visible = _settings.IsAuto;
            ResumeLayout(true);
            FitToContent();
        }

        void UpdateEnabledState()
        {
            bool on = _settings.ControlEnabled;
            _autoRadio.Enabled = on;
            _manualRadio.Enabled = on;
            _manualGroup.Enabled = on;
            _curveGroup.Enabled = on;
        }

        void UpdateManualLabel()
        {
            int value = _manualTrack.Value;
            int effective = _settings.LimitFanSpeed && value > 0 ? FanController.LimitPercent(value) : value;
            string text = FormatTarget(effective);
            if (effective != value)
                text += " (limited)";
            _manualValue.Text = text;
        }

        void UpdatePreview()
        {
            var profile = _settings.ActiveProfile;
            _preview.SetCurve(EditedPoints, profile.PointCount);
            if (_lastStatus != null)
                _preview.SetMarker(_editGpuRadio.Checked ? _lastStatus.GpuC : _lastStatus.CpuC);
        }

        void OnControllerStatus(FanStatus status)
        {
            if (!IsHandleCreated || IsDisposed)
                return;
            try
            {
                BeginInvoke(new Action(() => ShowStatus(status)));
            }
            catch (InvalidOperationException)
            {
            }
        }

        void ShowStatus(FanStatus status)
        {
            if (IsDisposed)
                return;
            _lastStatus = status;
            _cpuValue.Text = FormatTemp(status.CpuC);
            _gpuValue.Text = FormatTemp(status.GpuC);
            _fanValue.Text = status.Rpms.Count == 0 ? "--" : string.Join(" · ", status.Rpms);
            _targetValue.Text = FormatTarget(status.Applied);
            _preview.SetMarker(_editGpuRadio.Checked ? status.GpuC : status.CpuC);
            UpdateStatusText();

            string tip = "Asus Fan Control\nCPU " + FormatTemp(status.CpuC) + " · Fans " + _targetValue.Text;
            _tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        void UpdateStatusText()
        {
            string text;
            if (!_settings.ControlEnabled)
                text = "Fan control is off: the BIOS controls the fans.";
            else if (_lastStatus != null && _lastStatus.Sleeping)
                text = "Sleeping";
            else if (!_settings.IsAuto)
                text = "Manual speed";
            else if (_lastStatus != null && _lastStatus.Applied == FanStatus.Released)
                text = "No temperature reading: the BIOS controls the fans.";
            else
                text = "Following “" + _settings.ActiveProfile.Name + "”";

            if (_lastStatus != null && _lastStatus.Pending.HasValue && _settings.ControlEnabled)
                text += " — changing to " + FanController.Describe(_lastStatus.Pending.Value) + " shortly";
            _statusText.Text = text;
        }

        void FitToContent()
        {
            var root = Controls.OfType<TableLayoutPanel>().FirstOrDefault();
            var menu = MainMenuStrip;
            var status = Controls.OfType<StatusStrip>().FirstOrDefault();
            if (root == null || menu == null || status == null)
                return;
            Size preferred = root.GetPreferredSize(new Size(ContentWidth + root.Padding.Horizontal, 0));
            ClientSize = new Size(preferred.Width, menu.Height + preferred.Height + status.Height);
        }

        static string FormatTemp(int value)
        {
            return value >= 0 ? value + " °C" : "--";
        }

        static string FormatTarget(int target)
        {
            if (target == FanStatus.Released) return "BIOS";
            if (target == FanStatus.Unknown) return "--";
            if (target == 0) return "Off";
            return target + "%";
        }

        // ---------------------------------------------------------------- window and tray

        protected override void SetVisibleCore(bool value)
        {
            if (_startHidden && !_allowShow)
            {
                // Start in the notification area, but create the handle for BeginInvoke.
                value = false;
                if (!IsHandleCreated)
                    CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FitToContent();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            FlushTypedValues();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing && _settings.MinimizeToTray)
            {
                e.Cancel = true;
                FlushTypedValues();
                Hide();
                return;
            }
            Shutdown();
            base.OnFormClosing(e);
        }

        void ShowWindow()
        {
            _allowShow = true;
            Show();
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Activate();
        }

        void ExitApplication()
        {
            _exiting = true;
            Close();
        }

        void Shutdown()
        {
            if (_tray == null)
                return;
            FlushTypedValues();
            _settings.Save();
            _sleepWatcher.Dispose();
            _controller.Shutdown(_settings.ReleaseOnExit);
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
            Log.Write("Exited");
        }

        void OpenDataFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(AppPaths.DataDirectory);
                Process.Start("explorer.exe", "\"" + AppPaths.DataDirectory + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        static void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch (Exception)
            {
            }
        }

        // ---------------------------------------------------------------- small factories

        static ToolStripMenuItem CheckItem(string text, EventHandler onClick)
        {
            var item = new ToolStripMenuItem(text) { CheckOnClick = true };
            item.Click += onClick;
            return item;
        }

        static GroupBox Group(string text)
        {
            return new GroupBox
            {
                Text = text,
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 4, 8, 6),
                Margin = new Padding(0, 4, 0, 4),
            };
        }

        static FlowLayoutPanel Flow()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        }

        static Label RowLabel(string text)
        {
            return new Label { Text = text, AutoSize = false, Width = 52, Height = 23, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3, 4, 3, 3) };
        }

        static Label Hint(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth - 24, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 4, 3, 2),
            };
        }

        static Button SmallButton(string text, EventHandler onClick)
        {
            var button = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(2, 3, 2, 3) };
            button.Click += onClick;
            return button;
        }

        NumericUpDown Numeric(int min, int max)
        {
            var numeric = new NumericUpDown { Minimum = min, Maximum = max, Width = 64, Margin = new Padding(3, 3, 6, 3) };
            numeric.ValueChanged += OnCurveEdited;
            return numeric;
        }
    }
}
