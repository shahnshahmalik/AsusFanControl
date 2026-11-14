using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AsusFanControlGUI
{
    /// <summary>
    /// Draws the step curve (temperature → fan %) with a marker at the current temperature.
    /// With <see cref="Editing"/> on, each step's corner gets a handle that can be dragged:
    /// left/right changes its temperature, up/down its fan percent.
    /// </summary>
    public sealed class CurvePreview : Control
    {
        const int MinTemp = 20;
        const int MaxTemp = 100;
        const int HandleRadius = 5;
        const int HitRadius = 9;
        static readonly Color Accent = Color.FromArgb(0, 103, 192);

        FanSetpoint[] _points = new FanSetpoint[0];
        int _count;
        int _markerC = -1;
        bool _editing;
        int _hover = -1;
        int _drag = -1;

        /// <summary>Raised while a handle is dragged, after the point has moved.</summary>
        public event EventHandler PointsChanged;

        public CurvePreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = SystemColors.Window;
            Font = new Font("Segoe UI", 7.5F);
        }

        public void SetCurve(FanSetpoint[] points, int count)
        {
            _points = (FanSetpoint[])points.Clone();
            _count = Math.Min(count, _points.Length);
            Invalidate();
        }

        /// <summary>A copy of the points as currently shown (including dragged changes).</summary>
        public FanSetpoint[] Points
        {
            get { return (FanSetpoint[])_points.Clone(); }
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

        Rectangle Plot
        {
            get { return new Rectangle(30, 8, Width - 46, Height - 26); }
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
            for (int i = 0; i < _count; i++)
            {
                Point handle = HandleAt(plot, i);
                double dx = handle.X - location.X, dy = handle.Y - location.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                // <= so that, when handles overlap, the later (higher) step wins.
                if (distance <= bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            return best;
        }

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
            if (!_editing)
                return;

            if (_drag < 0)
            {
                int hover = HitTest(e.Location);
                if (hover != _hover)
                {
                    _hover = hover;
                    Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default;
                    Invalidate();
                }
                return;
            }

            var plot = Plot;
            int temperature = (int)Math.Round(MinTemp + (e.X - plot.Left) * (MaxTemp - MinTemp) / (double)plot.Width);
            int percent = (int)Math.Round((plot.Bottom - e.Y) * 100.0 / plot.Height);

            // Keep the steps in order: each one stays between its neighbours.
            int low = _drag > 0 ? Math.Max(MinTemp, _points[_drag - 1].TemperatureC + 1) : MinTemp;
            int high = _drag < _count - 1 ? Math.Min(MaxTemp, _points[_drag + 1].TemperatureC - 1) : MaxTemp;
            if (low > high)
                low = high = _points[_drag].TemperatureC;
            temperature = Math.Max(low, Math.Min(high, temperature));
            percent = FanCurve.ClampPercent(percent);

            if (temperature == _points[_drag].TemperatureC && percent == _points[_drag].FanPercent)
                return;
            _points[_drag].TemperatureC = temperature;
            _points[_drag].FanPercent = percent;
            Invalidate();
            var handler = PointsChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
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

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_drag < 0 && _hover >= 0)
            {
                _hover = -1;
                Invalidate();
            }
        }

        public void SetMarker(int temperatureC)
        {
            if (temperatureC == _markerC)
                return;
            _markerC = temperatureC;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var plot = Plot;
            if (plot.Width < 20 || plot.Height < 20)
                return;

            if (_editing)
            {
                using (var pen = new Pen(Accent, 1F) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }

            using (var gridPen = new Pen(Color.FromArgb(40, ForeColor)))
            using (var textBrush = new SolidBrush(SystemColors.GrayText))
            {
                for (int percent = 0; percent <= 100; percent += 25)
                {
                    int y = Y(plot, percent);
                    g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
                    var size = g.MeasureString(percent + "%", Font);
                    g.DrawString(percent + "%", Font, textBrush, plot.Left - size.Width - 1, y - size.Height / 2);
                }
                for (int temp = MinTemp; temp <= MaxTemp; temp += 20)
                {
                    int x = X(plot, temp);
                    g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
                    var size = g.MeasureString(temp + "°", Font);
                    g.DrawString(temp + "°", Font, textBrush, x - size.Width / 2, plot.Bottom + 2);
                }
            }

            if (_count > 0)
            {
                using (var path = new GraphicsPath())
                {
                    int previousY = Y(plot, 0);
                    int startX = plot.Left;
                    path.StartFigure();
                    for (int i = 0; i < _count; i++)
                    {
                        int x = X(plot, _points[i].TemperatureC);
                        int y = Y(plot, _points[i].FanPercent);
                        path.AddLine(startX, previousY, x, previousY);
                        path.AddLine(x, previousY, x, y);
                        startX = x;
                        previousY = y;
                    }
                    path.AddLine(startX, previousY, plot.Right, previousY);

                    using (var fill = (GraphicsPath)path.Clone())
                    {
                        fill.AddLine(plot.Right, previousY, plot.Right, plot.Bottom);
                        fill.AddLine(plot.Right, plot.Bottom, plot.Left, plot.Bottom);
                        fill.CloseFigure();
                        using (var brush = new SolidBrush(Color.FromArgb(40, Accent)))
                            g.FillPath(brush, fill);
                    }
                    using (var pen = new Pen(Accent, 2F))
                        g.DrawPath(pen, path);
                }
            }

            if (_markerC >= MinTemp && _markerC <= MaxTemp)
            {
                int x = X(plot, _markerC);
                using (var pen = new Pen(Color.FromArgb(200, 196, 43, 28), 1.5F) { DashStyle = DashStyle.Dash })
                    g.DrawLine(pen, x, plot.Top, x, plot.Bottom);
            }

            if (_editing && _count > 0)
                DrawHandles(g, plot);
        }

        void DrawHandles(Graphics g, Rectangle plot)
        {
            using (var border = new Pen(Accent, 2F))
            using (var fill = new SolidBrush(SystemColors.Window))
            using (var activeFill = new SolidBrush(Accent))
            {
                for (int i = 0; i < _count; i++)
                {
                    Point p = HandleAt(plot, i);
                    bool active = i == _drag || (i == _hover && _drag < 0);
                    int r = active ? HandleRadius + 2 : HandleRadius;
                    var rect = new Rectangle(p.X - r, p.Y - r, r * 2, r * 2);
                    g.FillEllipse(active ? activeFill : fill, rect);
                    g.DrawEllipse(border, rect);
                }
            }

            int labelIndex = _drag >= 0 ? _drag : _hover;
            if (labelIndex < 0)
                return;

            Point at = HandleAt(plot, labelIndex);
            string text = "Step " + (labelIndex + 1) + ": " + _points[labelIndex].TemperatureC + "°C → "
                + _points[labelIndex].FanPercent + "%";
            SizeF size = g.MeasureString(text, Font);
            float x = at.X + 10;
            float y = at.Y - size.Height - 8;
            if (x + size.Width + 6 > Width) x = at.X - size.Width - 16;
            if (y < 2) y = at.Y + 10;
            var box = new RectangleF(x, y, size.Width + 6, size.Height + 2);
            using (var back = new SolidBrush(Color.FromArgb(235, SystemColors.Info)))
            using (var outline = new Pen(SystemColors.GrayText))
            using (var textBrush = new SolidBrush(SystemColors.InfoText))
            {
                g.FillRectangle(back, box);
                g.DrawRectangle(outline, box.X, box.Y, box.Width, box.Height);
                g.DrawString(text, Font, textBrush, box.X + 3, box.Y + 1);
            }
        }

        static int X(Rectangle plot, int temperatureC)
        {
            int clamped = Math.Max(MinTemp, Math.Min(MaxTemp, temperatureC));
            return plot.Left + (int)Math.Round((clamped - MinTemp) * plot.Width / (double)(MaxTemp - MinTemp));
        }

        static int Y(Rectangle plot, int percent)
        {
            int clamped = Math.Max(0, Math.Min(100, percent));
            return plot.Bottom - (int)Math.Round(clamped * plot.Height / 100.0);
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
                Font = new Font("Segoe UI", 9F),
                AutoScaleMode = AutoScaleMode.Dpi,
                ClientSize = new Size(320, 112),
            })
            {
                var prompt = new Label { Text = label, AutoSize = true, Location = new Point(12, 12), MaximumSize = new Size(296, 0) };
                var box = new TextBox { Text = initial, Location = new Point(12, 38), Width = 296, MaxLength = 40 };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(152, 74), Width = 75 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(233, 74), Width = 75 };
                form.Controls.AddRange(new Control[] { prompt, box, ok, cancel });
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
