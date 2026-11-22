using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AsusFanControlGUI
{
    /// <summary>
    /// Draws a fan curve (temperature → fan speed): straight lines between points, with a
    /// marker at the current temperature.
    ///
    /// The vertical axis is fan % until the fans have been calibrated; then it is RPM,
    /// converted through the measured table (the points are still stored in %).
    ///
    /// Points are always shown. With <see cref="Editing"/> on: drag a point to move it
    /// (left/right temperature, up/down speed), double-click empty space to add a point
    /// there, double-click a point to remove it.
    /// </summary>
    public sealed class CurvePreview : Control
    {
        const int MinTemp = 20;
        const int MaxTemp = 100;
        const int HandleRadius = 5;
        const int HitRadius = 9;

        List<FanSetpoint> _points = new List<FanSetpoint>();
        RpmCalibration _calibration;
        int _markerC = -1;
        bool _editing;
        int _hover = -1;
        int _drag = -1;

        /// <summary>Raised when a point is moved, added or removed.</summary>
        public event EventHandler PointsChanged;

        /// <summary>Raised with a short message when an edit is refused (point limits).</summary>
        public event Action<string> Notice;

        public CurvePreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.StandardDoubleClick | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.Hud(7.5F);
        }

        public void SetCurve(FanSetpoint[] points)
        {
            _points = points.OrderBy(p => p.TemperatureC).ToList();
            _hover = -1;
            _drag = -1;
            Invalidate();
        }

        /// <summary>null shows fan %; a usable table shows RPM.</summary>
        public void SetCalibration(RpmCalibration calibration)
        {
            _calibration = calibration != null && calibration.IsUsable ? calibration : null;
            Invalidate();
        }

        public void SetMarker(int temperatureC)
        {
            if (temperatureC == _markerC)
                return;
            _markerC = temperatureC;
            Invalidate();
        }

        /// <summary>A copy of the points as currently shown (including edits).</summary>
        public FanSetpoint[] Points
        {
            get { return _points.ToArray(); }
        }

        public bool Editing
        {
            get { return _editing; }
            set
            {
                if (_editing == value)
                    return;
                _editing = value;
                _hover = -1;
                _drag = -1;
                Capture = false;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        // ---------------------------------------------------------------- geometry

        Rectangle Plot
        {
            get
            {
                int left = _calibration != null ? 38 : 32;
                // Bottom margin: half a handle below the 0 line, then the temperature labels.
                return new Rectangle(left, 20, Width - left - 18, Height - 46);
            }
        }

        /// <summary>Top of the value axis: 100 (%) or a round RPM above the measured maximum.</summary>
        int AxisMax
        {
            get
            {
                if (_calibration == null)
                    return 100;
                return Math.Max(1000, (int)Math.Ceiling(_calibration.MaxRpm / 1000.0) * 1000);
            }
        }

        int ValueOf(int percent)
        {
            return _calibration != null ? _calibration.RpmAt(percent) : FanCurve.ClampPercent(percent);
        }

        static int X(Rectangle plot, int temperatureC)
        {
            int clamped = Math.Max(MinTemp, Math.Min(MaxTemp, temperatureC));
            return plot.Left + (int)Math.Round((clamped - MinTemp) * plot.Width / (double)(MaxTemp - MinTemp));
        }

        int YOfValue(Rectangle plot, int value)
        {
            int clamped = Math.Max(0, Math.Min(AxisMax, value));
            return plot.Bottom - (int)Math.Round(clamped * plot.Height / (double)AxisMax);
        }

        int Y(Rectangle plot, int percent)
        {
            return YOfValue(plot, ValueOf(percent));
        }

        int TemperatureAt(Rectangle plot, int x)
        {
            return (int)Math.Round(MinTemp + (x - plot.Left) * (MaxTemp - MinTemp) / (double)plot.Width);
        }

        int PercentAt(Rectangle plot, int y)
        {
            double value = (plot.Bottom - y) * AxisMax / (double)plot.Height;
            int rounded = (int)Math.Round(Math.Max(0, value));
            return _calibration != null ? _calibration.PercentAt(rounded) : FanCurve.ClampPercent(rounded);
        }

        Point HandleAt(Rectangle plot, int index)
        {
            return new Point(X(plot, _points[index].TemperatureC), Y(plot, _points[index].FanPercent));
        }

        int HitTest(Point location)
        {
            var plot = Plot;
            int best = -1;
            double bestDistance = HitRadius + 0.5;
            for (int i = 0; i < _points.Count; i++)
            {
                Point handle = HandleAt(plot, i);
                double dx = handle.X - location.X, dy = handle.Y - location.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                // <= so that, when handles overlap, the later (higher) point wins.
                if (distance <= bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- mouse

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!_editing || e.Button != MouseButtons.Left)
                return;
            _drag = HitTest(e.Location);
            if (_drag >= 0)
            {
                Capture = true;
                Cursor = Cursors.SizeAll;
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag < 0)
            {
                int hover = HitTest(e.Location);
                if (hover != _hover)
                {
                    _hover = hover;
                    Cursor = hover >= 0 && _editing ? Cursors.Hand : Cursors.Default;
                    Invalidate();
                }
                return;
            }

            var plot = Plot;
            int temperature = TemperatureAt(plot, e.X);
            int percent = PercentAt(plot, e.Y);

            // Keep the points in order: each one stays between its neighbours.
            int low = _drag > 0 ? Math.Max(MinTemp, _points[_drag - 1].TemperatureC + 1) : MinTemp;
            int high = _drag < _points.Count - 1 ? Math.Min(MaxTemp, _points[_drag + 1].TemperatureC - 1) : MaxTemp;
            if (low > high)
                low = high = _points[_drag].TemperatureC;
            temperature = Math.Max(low, Math.Min(high, temperature));

            if (temperature == _points[_drag].TemperatureC && percent == _points[_drag].FanPercent)
                return;
            _points[_drag] = new FanSetpoint(temperature, percent);
            Invalidate();
            RaisePointsChanged();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_drag < 0)
                return;
            _drag = -1;
            Capture = false;
            _hover = _editing ? HitTest(e.Location) : -1;
            Cursor = _hover >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (!_editing || e.Button != MouseButtons.Left)
                return;

            int hit = HitTest(e.Location);
            if (hit >= 0)
            {
                if (_points.Count <= FanCurve.MinSetpoints)
                {
                    RaiseNotice("A curve needs at least " + FanCurve.MinSetpoints + " points.");
                    return;
                }
                _points.RemoveAt(hit);
                _hover = -1;
                _drag = -1;
                Capture = false;
                Cursor = Cursors.Default;
                Invalidate();
                RaisePointsChanged();
                return;
            }

            var plot = Plot;
            if (!plot.Contains(e.Location))
                return;
            if (_points.Count >= FanCurve.MaxSetpoints)
            {
                RaiseNotice("A curve can have up to " + FanCurve.MaxSetpoints + " points.");
                return;
            }
            int temperature = Math.Max(MinTemp, Math.Min(MaxTemp, TemperatureAt(plot, e.X)));
            if (_points.Any(p => p.TemperatureC == temperature))
            {
                RaiseNotice("There is already a point at " + temperature + "°C.");
                return;
            }
            int index = _points.FindIndex(p => p.TemperatureC > temperature);
            if (index < 0)
                index = _points.Count;
            _points.Insert(index, new FanSetpoint(temperature, PercentAt(plot, e.Y)));
            _hover = index;
            Cursor = Cursors.Hand;
            Invalidate();
            RaisePointsChanged();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_drag < 0 && _hover >= 0)
            {
                _hover = -1;
                Invalidate();
            }
        }

        void RaisePointsChanged()
        {
            var handler = PointsChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        void RaiseNotice(string text)
        {
            var handler = Notice;
            if (handler != null)
                handler(text);
        }

        // ---------------------------------------------------------------- painting

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var palette = Theme.Current;

            var plot = Plot;
            if (plot.Width < 20 || plot.Height < 20)
                return;
            using (var brush = new SolidBrush(Theme.Alpha(palette.Void, palette.IsDark ? 170 : 255)))
                g.FillRectangle(brush, plot);

            DrawGrid(g, plot);

            if (_points.Count >= FanCurve.MinSetpoints)
                DrawCurve(g, plot);

            if (_markerC >= MinTemp && _markerC <= MaxTemp)
            {
                int x = X(plot, _markerC);
                using (var pen = new Pen(palette.Secondary, 1.5F) { DashStyle = DashStyle.Dash })
                    g.DrawLine(pen, x, plot.Top, x, plot.Bottom);
                string now = _markerC + "°";
                Size size = TextRenderer.MeasureText(now, Font);
                TextRenderer.DrawText(g, now, Font, new Point(Math.Min(plot.Right - size.Width, x + 3), plot.Top + 2), palette.Secondary, TextFormatFlags.NoPadding);
            }

            if (_points.Count > 0)
                DrawHandles(g, plot);
        }

        void DrawGrid(Graphics g, Rectangle plot)
        {
            var palette = Theme.Current;
            using (var gridPen = new Pen(Theme.Alpha(palette.Primary, palette.IsDark ? 26 : 34)))
            using (var textBrush = new SolidBrush(palette.Ink3))
            {
                int step = _calibration == null ? 10 : (AxisMax <= 3000 ? 250 : 500);
                for (int value = 0; value <= AxisMax; value += step)
                {
                    int y = YOfValue(plot, value);
                    g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
                    if (_calibration != null && value % (step * 2) != 0)
                        continue;
                    string label = _calibration == null ? value + "%" : value.ToString();
                    var size = g.MeasureString(label, Font);
                    g.DrawString(label, Font, textBrush, plot.Left - size.Width - 1, y - size.Height / 2);
                }
                for (int temp = MinTemp; temp <= MaxTemp; temp += 10)
                {
                    int x = X(plot, temp);
                    g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
                    var size = g.MeasureString(temp + "°", Font);
                    g.DrawString(temp + "°", Font, textBrush, x - size.Width / 2, plot.Bottom + 10);
                }
                string unit = _calibration == null ? "Fan %" : "RPM";
                g.DrawString(unit, Font, textBrush, 2, 0);
            }
        }

        void DrawCurve(Graphics g, Rectangle plot)
        {
            var points = _points.ToArray();
            var line = new List<PointF>();
            if (_calibration == null)
            {
                // Straight lines between the points, flat before the first and after the last.
                line.Add(new PointF(plot.Left, Y(plot, points[0].FanPercent)));
                foreach (var point in points)
                    line.Add(new PointF(X(plot, point.TemperatureC), Y(plot, point.FanPercent)));
                line.Add(new PointF(plot.Right, Y(plot, points[points.Length - 1].FanPercent)));
            }
            else
            {
                // Lines in %, drawn on the RPM axis: sample so the shape follows the table.
                for (double temp = MinTemp; temp <= MaxTemp + 0.01; temp += 0.5)
                {
                    float x = (float)(plot.Left + (temp - MinTemp) * plot.Width / (MaxTemp - MinTemp));
                    double value = ExactValue(ExactPercent(temp, points));
                    float y = (float)(plot.Bottom - Math.Max(0, Math.Min(AxisMax, value)) * plot.Height / AxisMax);
                    line.Add(new PointF(x, y));
                }
            }

            var fill = new List<PointF>(line)
            {
                new PointF(plot.Right, plot.Bottom),
                new PointF(plot.Left, plot.Bottom),
            };
            var palette = Theme.Current;
            using (var brush = new LinearGradientBrush(new RectangleF(plot.Left, plot.Top - 1, plot.Width, plot.Height + 2),
                Theme.Alpha(palette.Primary, palette.IsDark ? 90 : 70), Theme.Alpha(palette.Secondary, 8), LinearGradientMode.Vertical))
                g.FillPolygon(brush, fill.ToArray());
            // Neon glow: wide faint strokes under the line.
            foreach (var glow in new[] { new { W = 9f, A = 26 }, new { W = 5f, A = 60 } })
            {
                using (var pen = new Pen(Theme.Alpha(palette.Primary, palette.IsDark ? glow.A : glow.A / 2), glow.W) { LineJoin = LineJoin.Round })
                    g.DrawLines(pen, line.ToArray());
            }
            using (var pen = new Pen(palette.Primary, 2F) { LineJoin = LineJoin.Round })
                g.DrawLines(pen, line.ToArray());
        }

        static double ExactPercent(double temperatureC, FanSetpoint[] points)
        {
            if (temperatureC <= points[0].TemperatureC)
                return points[0].FanPercent;
            for (int i = 1; i < points.Length; i++)
            {
                FanSetpoint a = points[i - 1], b = points[i];
                if (temperatureC <= b.TemperatureC)
                    return a.FanPercent + (temperatureC - a.TemperatureC) * (b.FanPercent - a.FanPercent) / (b.TemperatureC - a.TemperatureC);
            }
            return points[points.Length - 1].FanPercent;
        }

        double ExactValue(double percent)
        {
            if (_calibration == null)
                return percent;
            int low = (int)Math.Floor(percent);
            double fraction = percent - low;
            return _calibration.RpmAt(low) * (1 - fraction) + _calibration.RpmAt(low + 1) * fraction;
        }

        void DrawHandles(Graphics g, Rectangle plot)
        {
            var palette = Theme.Current;
            using (var border = new Pen(palette.Primary, 2F))
            using (var fill = new SolidBrush(palette.Void))
            using (var activeFill = new SolidBrush(palette.Secondary))
            {
                for (int i = 0; i < _points.Count; i++)
                {
                    Point p = HandleAt(plot, i);
                    bool active = i == _drag || (i == _hover && _drag < 0);
                    int r = active ? HandleRadius + 2 : HandleRadius;
                    var rect = new Rectangle(p.X - r, p.Y - r, r * 2, r * 2);
                    if (active)
                    {
                        using (var glow = new SolidBrush(Theme.Alpha(palette.Secondary, 70)))
                            g.FillEllipse(glow, rect.X - 4, rect.Y - 4, rect.Width + 8, rect.Height + 8);
                    }
                    g.FillEllipse(active ? activeFill : fill, rect);
                    g.DrawEllipse(border, rect);
                }
            }

            int labelIndex = _drag >= 0 ? _drag : _hover;
            if (labelIndex < 0)
                return;

            FanSetpoint point = _points[labelIndex];
            string speed = _calibration != null
                ? ValueOf(point.FanPercent) + " RPM (" + point.FanPercent + "%)"
                : point.FanPercent + "%";
            string text = "Point " + (labelIndex + 1) + ": " + point.TemperatureC + "°C → " + speed;

            Point at = HandleAt(plot, labelIndex);
            SizeF size = g.MeasureString(text, Font);
            float x = at.X + 10;
            float y = at.Y - size.Height - 8;
            if (x + size.Width + 6 > Width) x = at.X - size.Width - 16;
            if (x < 2) x = 2;
            if (y < 2) y = at.Y + 10;
            var box = new RectangleF(x, y, size.Width + 6, size.Height + 2);
            using (var back = new SolidBrush(Theme.Alpha(palette.Void, 235)))
            using (var outline = new Pen(palette.LineHi))
            using (var textBrush = new SolidBrush(palette.Primary))
            {
                g.FillRectangle(back, box);
                g.DrawRectangle(outline, box.X, box.Y, box.Width, box.Height);
                g.DrawString(text, Font, textBrush, box.X + 3, box.Y + 1);
            }
        }
    }

    /// <summary>A minimal text prompt used for profile names.</summary>
    static class InputDialog
    {
        public static string Prompt(IWin32Window owner, string title, string label, string initial)
        {
            using (var form = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                Font = Theme.Body(9F),
                AutoScaleMode = AutoScaleMode.Dpi,
                ClientSize = new Size(320, 116),
            })
            {
                var prompt = new Label { Text = label, AutoSize = true, Location = new Point(12, 12), MaximumSize = new Size(296, 0) };
                var box = new TextBox { Text = initial, Location = new Point(12, 38), Width = 296, MaxLength = 40, BorderStyle = BorderStyle.FixedSingle };
                var ok = new NeonButton { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(146, 76), Size = new Size(78, 28), Kind = NeonButtonKind.Primary };
                var cancel = new NeonButton { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(230, 76), Size = new Size(78, 28) };
                form.Controls.AddRange(new Control[] { prompt, box, ok, cancel });
                Theme.Apply(form);
                box.BackColor = Theme.Current.PanelHi;
                box.ForeColor = Theme.Current.Ink;
                form.HandleCreated += (s, e) => Theme.SetDarkTitleBar(form, Theme.Current.IsDark);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                box.SelectAll();

                if (form.ShowDialog(owner) != DialogResult.OK)
                    return null;
                string name = box.Text.Trim();
                return name.Length == 0 ? null : name;
            }
        }
    }
}
