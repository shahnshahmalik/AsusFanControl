using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AsusFanControlGUI
{
    /// <summary>
    /// Draws the step curve (temperature → fan %) with a marker at the current temperature.
    /// </summary>
    public sealed class CurvePreview : Control
    {
        const int MinTemp = 20;
        const int MaxTemp = 100;
        static readonly Color Accent = Color.FromArgb(0, 103, 192);

        FanSetpoint[] _points = new FanSetpoint[0];
        int _count;
        int _markerC = -1;

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

            var plot = new Rectangle(30, 6, Width - 38, Height - 24);
            if (plot.Width < 20 || plot.Height < 20)
                return;

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
