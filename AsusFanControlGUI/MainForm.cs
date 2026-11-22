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
        const int ContentWidth = 480;
        const string ProjectUrl = "https://github.com/Karmel0x/AsusFanControl";

        readonly AppSettings _settings;
        readonly FanController _controller = new FanController();
        readonly SleepWatcher _sleepWatcher = new SleepWatcher();
        readonly bool _startHidden;
        bool _allowShow;
        bool _loading = true;
        bool _updatingEditors;
        bool _exiting;
        bool _exitRequested;   // by another copy (installer): no questions, fans to BIOS
        bool _listening;
        FanStatus _lastStatus;

        // Menu
        ToolStripMenuItem _menuStartWithWindows, _menuMinimizeToTray, _menuReleaseOnSleep,
            _menuReleaseOnExit, _menuLimit;

        // Status
        HeaderBanner _header;
        StatusHud _hud;
        ToolStripMenuItem _themeMenu, _settingsMenu, _helpMenu;
        ToolStripStatusLabel _statusText;

        // Control
        CheckBox _controlCheck;
        RadioButton _autoRadio, _manualRadio;

        // Manual
        GroupBox _manualGroup;
        NeonSlider _manualTrack;
        Label _manualValue;

        // Curve
        GroupBox _curveGroup;
        ComboBox _profileCombo;
        NeonButton _newProfileButton, _renameProfileButton, _deleteProfileButton;
        CheckBox _useGpuCheck;
        RadioButton _editCpuRadio, _editGpuRadio;
        CurvePreview _preview;
        NeonButton _saveGraphButton, _cancelGraphButton, _calibrateButton;
        Label _graphHint, _curveHint;
        bool _calibrating;
        System.Threading.CancellationTokenSource _calibrationCancel;
        string _statusOverride;
        bool _graphDirty;   // the graph shows changes that are not saved yet

        // Tray
        NotifyIcon _tray;
        ToolStripMenuItem _trayControl, _trayProfiles;

        public MainForm(bool startHidden)
        {
            _startHidden = startHidden;
            _settings = AppSettings.Load();

            Text = "Asus Fan Control";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Theme.Set(_settings.Theme);
            Font = Theme.Body(9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            // No window border: the header banner is the title bar (see OnHeaderButton).
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            Padding = new Padding(1);
            StartPosition = FormStartPosition.CenterScreen;

            BuildLayout();
            BuildTray();
            Theme.Apply(this);
            ApplyMenus();
            Theme.Changed += OnThemeChanged;
            LoadIntoControls();
            _loading = false;

            _controller.StatusChanged += OnControllerStatus;
            _sleepWatcher.Sleeping += () =>
            {
                CancelCalibration();
                _controller.OnSleep(_settings.ReleaseOnSleep);
            };
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
            var settingsMenu = _settingsMenu = new ToolStripMenuItem("&Settings");
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
            _themeMenu = new ToolStripMenuItem("Theme");
            foreach (var palette in Theme.Palettes)
            {
                string name = palette.Name;
                _themeMenu.DropDownItems.Add(new ToolStripMenuItem(name, null, (s, e) =>
                {
                    _settings.Theme = name;
                    _settings.Save();
                    Theme.Set(name);
                }) { Checked = name == Theme.Current.Name });
            }
            settingsMenu.DropDownItems.AddRange(new ToolStripItem[]
            {
                _menuStartWithWindows, _menuMinimizeToTray, new ToolStripSeparator(),
                _menuReleaseOnSleep, _menuReleaseOnExit, _menuLimit, new ToolStripSeparator(),
                _themeMenu, new ToolStripSeparator(),
                new ToolStripMenuItem("Open settings and log folder", null, (s, e) => OpenDataFolder()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("E&xit", null, (s, e) => ExitApplication()),
            });
            var helpMenu = _helpMenu = new ToolStripMenuItem("&Help");
            helpMenu.DropDownItems.Add(new ToolStripMenuItem("Project page", null, (s, e) => OpenUrl(ProjectUrl)));
            helpMenu.DropDownItems.Add(new ToolStripMenuItem("Check for updates", null, (s, e) => OpenUrl(ProjectUrl + "/releases")));

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
            _header = new HeaderBanner { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4) };
            _header.ButtonClicked += OnHeaderButton;
            _header.DragStarted += StartWindowMove;
            root.Controls.Add(_header);
            root.Controls.Add(BuildStatusGroup());
            root.Controls.Add(BuildControlRow());
            root.Controls.Add(_manualGroup = BuildManualGroup());
            root.Controls.Add(_curveGroup = BuildCurveGroup());

            Controls.Add(root);
            Controls.Add(status);
            root.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) StartWindowMove(); };
        }

        Control BuildStatusGroup()
        {
            var group = Group("Telemetry");
            _hud = new StatusHud { Dock = DockStyle.Top, Margin = new Padding(3, 2, 3, 2) };
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _hud.Width = ContentWidth - 24;
            table.Controls.Add(_hud);
            group.Controls.Add(table);
            return group;
        }

        Control BuildControlRow()
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 2) };
            _controlCheck = new NeonCheckBox
            {
                Text = "Control fans",
                AutoSize = true,
                Font = Theme.Display(9.5F),
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
            _autoRadio = new NeonRadioButton { Text = "Auto (fan curve)", AutoSize = true, Margin = new Padding(3, 4, 12, 3) };
            _manualRadio = new NeonRadioButton { Text = "Manual", AutoSize = true, Margin = new Padding(3, 4, 3, 3) };
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
            _manualTrack = new NeonSlider
            {
                Minimum = 0,
                Maximum = 100,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 6, 3, 3),
            };
            _manualTrack.ValueChanged += (s, e) =>
            {
                UpdateManualLabel();
                if (_loading) return;
                _settings.ManualPercent = _manualTrack.Value;
                _settings.Save();
                ApplyToController();
            };
            _manualValue = new Label { AutoSize = true, Font = Theme.Display(15F), Tag = "signal", Anchor = AnchorStyles.Left, Margin = new Padding(6, 4, 3, 3) };
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
            _profileCombo = new NeonComboBox { Width = 150, Margin = new Padding(3, 5, 6, 3) };
            _profileCombo.SelectedIndexChanged += OnProfileSelected;
            _newProfileButton = SmallButton("New", (s, e) => NewProfile());
            _renameProfileButton = SmallButton("Rename", (s, e) => RenameProfile());
            _deleteProfileButton = SmallButton("Delete", (s, e) => DeleteProfile());
            profileRow.Controls.AddRange(new Control[]
            {
                RowLabel("Profile"), _profileCombo, _newProfileButton, _renameProfileButton, _deleteProfileButton,
            });
            table.Controls.Add(profileRow);

            // Options row
            var optionsRow = Flow();
            _useGpuCheck = new NeonCheckBox { Text = "Also follow GPU temperature", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
            _useGpuCheck.CheckedChanged += OnUseGpuChanged;
            optionsRow.Controls.AddRange(new Control[] { RowLabel(""), _useGpuCheck });
            table.Controls.Add(optionsRow);

            // Which curve is shown and edited
            var editRow = Flow();
            _editCpuRadio = new NeonRadioButton { Text = "CPU curve", AutoSize = true, Checked = true, Margin = new Padding(3, 4, 12, 3) };
            _editGpuRadio = new NeonRadioButton { Text = "GPU curve", AutoSize = true, Margin = new Padding(3, 4, 3, 3) };
            _editCpuRadio.CheckedChanged += OnEditTargetChanged;
            _editGpuRadio.CheckedChanged += OnEditTargetChanged;
            editRow.Controls.AddRange(new Control[] { RowLabel("Show"), _editCpuRadio, _editGpuRadio });
            table.Controls.Add(editRow);

            // The graph is the editor
            _preview = new CurvePreview { Dock = DockStyle.Fill, MinimumSize = new Size(ContentWidth - 24, 260), Editing = true, Margin = new Padding(3, 4, 3, 0) };
            _preview.PointsChanged += OnGraphDragged;
            _preview.Notice += text => _graphHint.Text = text;
            table.Controls.Add(_preview);

            // Save / Cancel on the left, Calibrate RPM on the right, the hint underneath.
            var graphButtons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            graphButtons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            graphButtons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            graphButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            graphButtons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _saveGraphButton = SmallButton("Save", (s, e) => SaveGraph());
            _saveGraphButton.Kind = NeonButtonKind.Primary;
            _cancelGraphButton = SmallButton("Cancel", (s, e) => DiscardGraph());
            _calibrateButton = SmallButton("Calibrate RPM", (s, e) => OnCalibrateClicked());
            _graphHint = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(ContentWidth - 30, 0),
                Tag = "hint",
                Margin = new Padding(3, 4, 3, 2),
                Text = GraphHintText,
            };
            graphButtons.Controls.Add(_saveGraphButton, 0, 0);
            graphButtons.Controls.Add(_cancelGraphButton, 1, 0);
            graphButtons.Controls.Add(_calibrateButton, 3, 0);
            graphButtons.Controls.Add(_graphHint, 0, 1);
            graphButtons.SetColumnSpan(_graphHint, 4);
            table.Controls.Add(graphButtons);

            _curveHint = Hint("");
            table.Controls.Add(_curveHint);
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
                Theme.ApplyMenu(menu);
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

        /// <summary>The curve shown on the graph: CPU, or GPU when that is selected.</summary>
        FanSetpoint[] EditedPoints
        {
            get
            {
                var profile = _settings.ActiveProfile;
                return _editGpuRadio.Checked ? profile.Gpu : profile.Cpu;
            }
            set
            {
                var profile = _settings.ActiveProfile;
                if (_editGpuRadio.Checked)
                    profile.Gpu = value;
                else
                    profile.Cpu = value;
            }
        }

        void PushProfileToEditors()
        {
            var profile = _settings.ActiveProfile;
            _updatingEditors = true;
            try
            {
                _useGpuCheck.Checked = profile.UseGpu;
                _editGpuRadio.Enabled = profile.UseGpu && !_calibrating;
                if (!profile.UseGpu)
                    _editCpuRadio.Checked = true;
            }
            finally
            {
                _updatingEditors = false;
            }
            _graphDirty = false;
            UpdateCurveHint();
            UpdatePreview();
            UpdateGraphEditControls();
        }

        void UpdateCurveHint()
        {
            string shape = "The speed follows the lines between the points; below the first point the first point's speed is used (0% stops the fans).";
            string rpm = _settings.RpmCalibration != null
                ? " RPM from calibration on " + _settings.RpmCalibration.MeasuredAt + "."
                : " Calibrate RPM to show fan RPM instead of %.";
            _curveHint.Text = shape + " A new curve speed is applied once it has held for 5 seconds." + rpm;
        }

        // ---------------------------------------------------------------- graph editing

        const string GraphHintText = "Drag a point to move it. Double-click to add a point, double-click a point to remove it.";

        void OnGraphDragged(object sender, EventArgs e)
        {
            _graphDirty = true;
            _graphHint.Text = GraphHintText;
            UpdateGraphEditControls();
        }

        /// <summary>Applies the curve shown on the graph.</summary>
        void SaveGraph()
        {
            if (!_graphDirty)
                return;
            EditedPoints = FanCurve.Normalize(_preview.Points, EditedPoints);
            PushProfileToEditors();
            _settings.Save();
            ApplyToController();
            Log.Write("Curve saved (" + _settings.ActiveProfile.Name + ", " + (_editGpuRadio.Checked ? "GPU" : "CPU") + "): "
                + string.Join(", ", EditedPoints.Select(p => p.TemperatureC + "°C→" + p.FanPercent + "%")));
        }

        /// <summary>Puts the saved curve back on the graph.</summary>
        void DiscardGraph()
        {
            if (!_graphDirty)
                return;
            PushProfileToEditors();
            Log.Write("Curve changes discarded");
        }

        /// <summary>
        /// Before something replaces the curve on screen (profile, curve or mode switch,
        /// calibration, closing), deal with unsaved graph changes, asking when possible.
        /// </summary>
        void ResolveGraphEdit(bool canAsk)
        {
            if (!_graphDirty)
                return;
            if (canAsk && MessageBox.Show(this, "Save the changes you made on the graph?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                SaveGraph();
            else
                DiscardGraph();
        }

        void UpdateGraphEditControls()
        {
            _saveGraphButton.Enabled = _graphDirty && !_calibrating;
            _cancelGraphButton.Enabled = _graphDirty && !_calibrating;
            _calibrateButton.Enabled = true;
            _preview.Editing = !_calibrating;

            _profileCombo.Enabled = !_calibrating;
            _newProfileButton.Enabled = !_calibrating;
            _renameProfileButton.Enabled = !_calibrating;
            _deleteProfileButton.Enabled = !_calibrating && _settings.Profiles.Count > 1;
            _useGpuCheck.Enabled = !_calibrating;
            _editCpuRadio.Enabled = !_calibrating;
            _editGpuRadio.Enabled = !_calibrating && _settings.ActiveProfile.UseGpu;

            if (_graphDirty)
            {
                AcceptButton = _saveGraphButton;
                CancelButton = _cancelGraphButton;
            }
            else
            {
                if (AcceptButton == _saveGraphButton) AcceptButton = null;
                if (CancelButton == _cancelGraphButton) CancelButton = null;
            }
        }

        void OnUseGpuChanged(object sender, EventArgs e)
        {
            if (_loading || _updatingEditors)
                return;
            ResolveGraphEdit(true);
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
            if (_graphDirty)
            {
                // The radio has already switched; the graph still shows the other curve.
                bool save = MessageBox.Show(this, "Save the changes you made on the graph?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                if (save)
                {
                    var profile = _settings.ActiveProfile;
                    if (radio == _editGpuRadio)
                        profile.Cpu = FanCurve.Normalize(_preview.Points, profile.Cpu);
                    else
                        profile.Gpu = FanCurve.Normalize(_preview.Points, profile.Gpu);
                    _settings.Save();
                    ApplyToController();
                    Log.Write("Curve saved (" + profile.Name + ", " + (radio == _editGpuRadio ? "CPU" : "GPU") + ")");
                }
            }
            PushProfileToEditors();
        }

        // ---------------------------------------------------------------- RPM calibration

        void OnCalibrateClicked()
        {
            if (_calibrating)
            {
                CancelCalibration();
                return;
            }
            ResolveGraphEdit(true);
            if (MessageBox.Show(this,
                    "Calibration runs the fans at 10%, 20% … 100% and measures their RPM, so the graph can show RPM.\r\n\r\n" +
                    "It takes one to two minutes and the fans get loud near the end. Normal fan control resumes afterwards.",
                    "Calibrate RPM", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            _calibrating = true;
            _calibrationCancel = new System.Threading.CancellationTokenSource();
            var cancel = _calibrationCancel.Token;
            _calibrateButton.Text = "Stop calibration";
            UpdateGraphEditControls();
            SetStatusOverride("Calibrating: starting…");

            var worker = new System.Threading.Thread(() =>
            {
                RpmCalibration result = _controller.Calibrate(
                    (percent, rpm) => RunOnUi(() => SetStatusOverride("Calibrating: " + percent + "% → " + rpm + " RPM")),
                    cancel);
                RunOnUi(() => OnCalibrationFinished(result));
            }) { IsBackground = true, Name = "Calibration" };
            worker.Start();
        }

        void OnCalibrationFinished(RpmCalibration result)
        {
            _calibrating = false;
            if (_calibrationCancel != null)
            {
                _calibrationCancel.Dispose();
                _calibrationCancel = null;
            }
            _calibrateButton.Text = "Calibrate RPM";
            SetStatusOverride(null);
            UpdateGraphEditControls();

            if (result != null)
            {
                _settings.RpmCalibration = result;
                _settings.Save();
                _preview.SetCalibration(result);
                UpdateCurveHint();
                if (Visible)
                    MessageBox.Show(this, "Calibration finished. The fans reached up to " + result.MaxRpm + " RPM.",
                        "Calibrate RPM", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (Visible && !_exiting)
            {
                MessageBox.Show(this, "Calibration was stopped or the fans did not report a speed, so nothing was changed.",
                    "Calibrate RPM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void CancelCalibration()
        {
            try
            {
                var cancel = _calibrationCancel;
                if (cancel != null)
                    cancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        void OnModeChanged(object sender, EventArgs e)
        {
            var radio = sender as RadioButton;
            if (_loading || radio == null || !radio.Checked)
                return;
            ResolveGraphEdit(true);
            _settings.Mode = _autoRadio.Checked ? AppSettings.ModeAuto : AppSettings.ModeManual;
            _settings.Save();
            UpdateModeVisibility();
            ApplyToController();
        }

        void OnProfileSelected(object sender, EventArgs e)
        {
            if (_loading || _profileCombo.SelectedItem == null)
                return;
            SelectProfile((string)_profileCombo.SelectedItem);
        }

        void SelectProfile(string name)
        {
            if (_header != null)
                _header.Glitch();
            ResolveGraphEdit(Visible);
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
            _preview.SetCalibration(_settings.RpmCalibration);
            _preview.SetCurve(EditedPoints);
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
            int maxRpm = _settings.RpmCalibration != null ? _settings.RpmCalibration.MaxRpm : 6000;
            _hud.SetValues(status.CpuC, status.GpuC, status.Rpms, maxRpm, FormatTarget(status.Applied), status.Applied);
            _header.SetRpm(status.Rpms.Count == 0 ? 0 : (int)status.Rpms.Average());
            UpdateHeaderInfo();
            _preview.SetMarker(_editGpuRadio.Checked ? status.GpuC : status.CpuC);
            UpdateStatusText();

            string tip = "Asus Fan Control\nCPU " + FormatTemp(status.CpuC) + " · Fans " + FormatTarget(status.Applied);
            _tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        /// <summary>Shows <paramref name="text"/> in the status bar instead of the usual state (null to stop).</summary>
        void SetStatusOverride(string text)
        {
            _statusOverride = text;
            UpdateStatusText();
        }

        void UpdateStatusText()
        {
            string text;
            if (_statusOverride != null)
            {
                _statusText.Text = _statusOverride;
                return;
            }
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

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Theme.PaintBackdrop(e.Graphics, ClientRectangle);
        }

        void OnThemeChanged()
        {
            Theme.Apply(this);
            ApplyMenus();
            foreach (ToolStripMenuItem item in _themeMenu.DropDownItems)
                item.Checked = item.Text == Theme.Current.Name;
            _header.Glitch();
            Log.Write("Theme: " + Theme.Current.Name);
        }

        void UpdateHeaderInfo()
        {
            string mode = !_settings.ControlEnabled ? "BIOS CONTROL"
                : _settings.IsAuto ? "AUTO // " + _settings.ActiveProfile.Name
                : "MANUAL // " + _settings.ManualPercent + "%";
            string temps = _lastStatus != null && _lastStatus.CpuC >= 0 ? "  ·  CPU " + _lastStatus.CpuC + "°C" : "";
            _header.SetInfo("MODE " + mode + temps + "  ·  " + Theme.Current.Name);
        }

        void FitToContent()
        {
            var root = Controls.OfType<TableLayoutPanel>().FirstOrDefault();
            var status = Controls.OfType<StatusStrip>().FirstOrDefault();
            if (root == null || status == null)
                return;
            Size preferred = root.GetPreferredSize(new Size(ContentWidth + root.Padding.Horizontal, 0));
            ClientSize = new Size(preferred.Width + Padding.Horizontal, preferred.Height + status.Height + Padding.Vertical);
        }

        void ApplyMenus()
        {
            if (_tray != null)
                Theme.ApplyMenu(_tray.ContextMenuStrip);
            Theme.ApplyMenu(_settingsMenu.DropDown);
            Theme.ApplyMenu(_helpMenu.DropDown);
            Theme.ApplyMenu(_themeMenu.DropDown);
        }

        // ---------------------------------------------------------------- borderless window

        const int WS_MINIMIZEBOX = 0x00020000;
        const int WS_SYSMENU = 0x00080000;
        const int CS_DROPSHADOW = 0x00020000;
        const int WM_NCLBUTTONDOWN = 0x00A1;
        const int HTCAPTION = 2;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // Minimize box: the taskbar button minimizes and restores the window.
                // System menu: Alt+Space and the taskbar's Move/Close still work.
                cp.Style |= WS_MINIMIZEBOX | WS_SYSMENU;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        /// <summary>Hands the drag to Windows, so moving and Snap behave like a normal title bar.</summary>
        void StartWindowMove()
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        void OnHeaderButton(HeaderBanner.CaptionButton button, Point screen)
        {
            switch (button)
            {
                case HeaderBanner.CaptionButton.Settings:
                    _settingsMenu.DropDown.Show(screen);
                    break;
                case HeaderBanner.CaptionButton.Help:
                    _helpMenu.DropDown.Show(screen);
                    break;
                case HeaderBanner.CaptionButton.Minimize:
                    WindowState = FormWindowState.Minimized;
                    break;
                case HeaderBanner.CaptionButton.Close:
                    Close();
                    break;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
                StartWindowMove();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // A thin outline, since there is no window frame.
            using (var pen = new Pen(Theme.Current.LineHi))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ResolveGraphEdit(Visible && e.CloseReason == CloseReason.UserClosing && !_exitRequested);
            if (!_exiting && e.CloseReason == CloseReason.UserClosing && _settings.MinimizeToTray)
            {
                e.Cancel = true;
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.SetDarkTitleBar(this, Theme.Current.IsDark);
            if (_listening)
                return;
            _listening = true;
            InstanceSignals.Listen(
                () => RunOnUi(ShowWindow),
                () => RunOnUi(() =>
                {
                    Log.Write("Exit requested by another copy (installer)");
                    _exitRequested = true;
                    ExitApplication();
                }));
        }

        void RunOnUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        void Shutdown()
        {
            if (_tray == null)
                return;
            CancelCalibration();
            _settings.Save();
            _sleepWatcher.Dispose();
            InstanceSignals.Stop();
            _controller.Shutdown(_settings.ReleaseOnExit || _exitRequested);
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
            return new NeonPanel
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
            return new Label
            {
                Text = text.ToUpperInvariant(),
                Tag = "caption",
                Font = Theme.Hud(7.5F, FontStyle.Bold),
                AutoSize = false,
                Width = 56,
                Height = 23,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 4, 3, 3),
            };
        }

        static Label Hint(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth - 24, 0),
                Tag = "hint",
                Font = Theme.Body(8F),
                Margin = new Padding(3, 4, 3, 2),
            };
        }

        static NeonButton SmallButton(string text, EventHandler onClick)
        {
            var button = new NeonButton { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(2, 3, 2, 3) };
            button.Click += onClick;
            return button;
        }

    }
}
