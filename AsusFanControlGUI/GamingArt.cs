using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AsusFanControlGUI
{
    /// <summary>
    /// Banner across the top of the window: a turbine that spins with the real fan RPM,
    /// a glitch-split "FAN//CONTROL" title, a telemetry line and slash stripes. All drawn
    /// in code from the current theme (no images, no third-party logos).
    ///
    /// The window has no border, so this is also its title bar: dragging it moves the
    /// window, and the buttons at the top right open Settings and Help, minimize and close.
    /// </summary>
    public sealed class HeaderBanner : Control
    {
        public enum CaptionButton { None = -1, Settings, Help, Minimize, Close }

        static readonly string[] Glyphs = { "\uE713", "\uE897", "\uE921", "\uE8BB" };
        static readonly string[] Tips = { "Settings", "Help", "Minimize", "Close" };
        CaptionButton _hoverButton = CaptionButton.None, _pressedButton = CaptionButton.None;
        readonly ToolTip _tip = new ToolTip();

        /// <summary>A caption button was clicked; the point is its bottom-left corner on screen.</summary>
        public event Action<CaptionButton, Point> ButtonClicked;

        /// <summary>The user pressed the mouse on the banner outside the buttons (start a window move).</summary>
        public event Action DragStarted;
        readonly Timer _timer = new Timer { Interval = 33 };
        float _angle;
        int _rpm;
        string _info = "";
        int _glitchFrames;
        readonly Random _random = new Random();

        public HeaderBanner()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 66;
            _timer.Tick += (s, e) => Animate();
            _timer.Start();
        }

        /// <summary>Average fan RPM; sets how fast the turbine spins.</summary>
        public void SetRpm(int rpm)
        {
            _rpm = Math.Max(0, rpm);
        }

        public void SetInfo(string text)
        {
            if (text == _info)
                return;
            _info = text ?? "";
            Invalidate();
        }

        /// <summary>A short RGB-split flicker on the title (on theme change, profile switch…).</summary>
        public void Glitch()
        {
            _glitchFrames = 6;
        }

        void Animate()
        {
            var form = FindForm();
            if (!Visible || form == null || !form.Visible || form.WindowState == FormWindowState.Minimized)
                return;
            // Idle drift when the fans are off, faster with RPM (about one turn a second at 6000 RPM).
            _angle = (_angle + (_rpm <= 0 ? 0.4f : 1.5f + _rpm / 600f)) % 360f;
            if (_glitchFrames > 0)
                _glitchFrames--;
            else if (_random.Next(400) == 0)
                _glitchFrames = 3;   // the odd spontaneous glitch, like a CRT
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _tip.Dispose();
            }
            base.Dispose(disposing);
        }

        Rectangle ButtonRect(CaptionButton button)
        {
            const int w = 30, h = 24, gap = 2;
            int index = (int)button;
            int right = Width - 12;
            return new Rectangle(right - (4 - index) * (w + gap) + gap, 7, w, h);
        }

        CaptionButton ButtonAt(Point location)
        {
            for (int i = 0; i < 4; i++)
            {
                if (ButtonRect((CaptionButton)i).Contains(location))
                    return (CaptionButton)i;
            }
            return CaptionButton.None;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hover = ButtonAt(e.Location);
            if (hover != _hoverButton)
            {
                _hoverButton = hover;
                _tip.SetToolTip(this, hover == CaptionButton.None ? null : Tips[(int)hover]);
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverButton = CaptionButton.None;
            _pressedButton = CaptionButton.None;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            _pressedButton = ButtonAt(e.Location);
            if (_pressedButton == CaptionButton.None)
            {
                var handler = DragStarted;
                if (handler != null)
                    handler();
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var pressed = _pressedButton;
            _pressedButton = CaptionButton.None;
            Invalidate();
            if (pressed != CaptionButton.None && ButtonAt(e.Location) == pressed)
            {
                var r = ButtonRect(pressed);
                var handler = ButtonClicked;
                if (handler != null)
                    handler(pressed, PointToScreen(new Point(r.Left, r.Bottom + 2)));
            }
        }

        void DrawButtons(Graphics g, ThemePalette p)
        {
            for (int i = 0; i < 4; i++)
            {
                var button = (CaptionButton)i;
                var r = ButtonRect(button);
                bool hover = button == _hoverButton, pressed = button == _pressedButton;
                bool close = button == CaptionButton.Close;
                Color signal = close ? p.Danger : p.Primary;
                using (var path = Theme.CutRectAlt(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 6))
                {
                    using (var brush = new SolidBrush(pressed ? Theme.Alpha(signal, 120) : hover ? Theme.Alpha(signal, close ? 200 : 60) : Theme.Alpha(p.Void, 150)))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(hover ? signal : p.Line))
                        g.DrawPath(pen, path);
                }
                Color ink = hover && close ? Color.White : hover ? signal : p.Ink2;
                TextRenderer.DrawText(g, Glyphs[i], IconFont, r, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        static Font _iconFont;

        static Font IconFont
        {
            get
            {
                if (_iconFont == null)
                {
                    using (var installed = new System.Drawing.Text.InstalledFontCollection())
                    {
                        bool fluent = installed.Families.Any(f => f.Name == "Segoe Fluent Icons");
                        _iconFont = new Font(fluent ? "Segoe Fluent Icons" : "Segoe MDL2 Assets", 9f);
                    }
                }
                return _iconFont;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.CutRect(r, 16))
            {
                using (var brush = new LinearGradientBrush(r, Theme.Alpha(p.Panel, 245), Theme.Alpha(Theme.Mix(p.Panel, p.Secondary, 0.12f), 245), LinearGradientMode.Horizontal))
                    g.FillPath(brush, path);
                g.SetClip(path);
                DrawSlashes(g, r, p);
                DrawHexes(g, r, p);
                g.ResetClip();
                using (var pen = new Pen(p.Line))
                    g.DrawPath(pen, path);
            }

            // Bottom edge: secondary → primary signal line.
            using (var brush = new LinearGradientBrush(new RectangleF(0, Height - 3, Width, 2), p.Secondary, p.Primary, LinearGradientMode.Horizontal))
                g.FillRectangle(brush, 14, Height - 3, Width - 28, 2);

            DrawTurbine(g, new PointF(36, Height / 2f - 1), 24, p);
            DrawTitle(g, p);
            DrawButtons(g, p);
        }

        void DrawSlashes(Graphics g, RectangleF r, ThemePalette p)
        {
            // Aggressive diagonal stripes on the right, fading out to the left.
            float w = 16, gap = 9, skew = r.Height * 0.55f;
            for (int i = 0; i < 7; i++)
            {
                float x = r.Right - 40 - i * (w + gap);
                Color c = i % 3 == 1 ? p.Secondary : p.Primary;
                int alpha = Math.Max(0, (p.IsDark ? 95 : 70) - i * 13);
                using (var brush = new SolidBrush(Theme.Alpha(c, alpha)))
                {
                    g.FillPolygon(brush, new[]
                    {
                        new PointF(x + skew, r.Top), new PointF(x + skew + w, r.Top),
                        new PointF(x + w, r.Bottom), new PointF(x, r.Bottom),
                    });
                }
            }
        }

        void DrawHexes(Graphics g, RectangleF r, ThemePalette p)
        {
            // Faint honeycomb behind the title, the "armour plating" look.
            using (var pen = new Pen(Theme.Alpha(p.Primary, p.IsDark ? 18 : 22)))
            {
                float size = 9, h = size * 1.732f;
                for (int row = 0; row * h * 0.5f < r.Height + h; row++)
                {
                    for (int col = 0; col * size * 3 < r.Width * 0.62f; col++)
                    {
                        float cx = 70 + col * size * 3 + (row % 2) * size * 1.5f;
                        float cy = row * h * 0.5f;
                        var hex = new PointF[6];
                        for (int k = 0; k < 6; k++)
                        {
                            double a = Math.PI / 3 * k;
                            hex[k] = new PointF(cx + size * (float)Math.Cos(a), cy + size * (float)Math.Sin(a));
                        }
                        g.DrawPolygon(pen, hex);
                    }
                }
            }
        }

        void DrawTurbine(Graphics g, PointF c, float radius, ThemePalette p)
        {
            // Glow behind.
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(c.X - radius * 1.6f, c.Y - radius * 1.6f, radius * 3.2f, radius * 3.2f);
                using (var brush = new PathGradientBrush(path) { CenterColor = Theme.Alpha(p.Primary, p.IsDark ? 70 : 40), SurroundColors = new[] { Color.Transparent } })
                    g.FillPath(brush, path);
            }
            // Hexagonal housing.
            var housing = new PointF[6];
            for (int k = 0; k < 6; k++)
            {
                double a = Math.PI / 3 * k + Math.PI / 6;
                housing[k] = new PointF(c.X + (radius + 4) * (float)Math.Cos(a), c.Y + (radius + 4) * (float)Math.Sin(a));
            }
            using (var brush = new SolidBrush(Theme.Alpha(p.Void, 200)))
                g.FillPolygon(brush, housing);
            using (var pen = new Pen(p.Primary, 1.5f))
                g.DrawPolygon(pen, housing);

            // Blades: curved, swept, rotating.
            var state = g.Save();
            g.TranslateTransform(c.X, c.Y);
            g.RotateTransform(_angle);
            const int blades = 9;
            for (int i = 0; i < blades; i++)
            {
                using (var blade = new GraphicsPath())
                {
                    float inner = radius * 0.3f, outer = radius - 1;
                    blade.AddBezier(inner, -2, inner + 6, -radius * 0.45f, outer - 4, -radius * 0.35f, outer, -1);
                    blade.AddLine(outer, -1, outer - 2, 4);
                    blade.AddBezier(outer - 2, 4, outer - 8, -radius * 0.12f, inner + 4, -radius * 0.1f, inner, 3);
                    blade.CloseFigure();
                    using (var brush = new LinearGradientBrush(new RectangleF(inner, -radius * 0.5f, outer - inner + 1, radius * 0.6f),
                        Theme.Alpha(p.Primary, 230), Theme.Alpha(p.Secondary, 200), LinearGradientMode.Horizontal))
                        g.FillPath(brush, blade);
                }
                g.RotateTransform(360f / blades);
            }
            g.Restore(state);

            // Hub.
            using (var brush = new SolidBrush(p.Void))
                g.FillEllipse(brush, c.X - 6, c.Y - 6, 12, 12);
            using (var pen = new Pen(p.Secondary, 2f))
                g.DrawEllipse(pen, c.X - 6, c.Y - 6, 12, 12);
            using (var brush = new SolidBrush(p.Primary))
                g.FillEllipse(brush, c.X - 2, c.Y - 2, 4, 4);
        }

        void DrawTitle(Graphics g, ThemePalette p)
        {
            var font = Theme.Display(17f);
            float x = 76, y = 9;
            int split = _glitchFrames > 0 ? 3 : 1;
            // RGB split: primary to the left, secondary to the right, then the ink on top.
            DrawTitleText(g, font, x - split, y, Theme.Alpha(p.Primary, 150), Theme.Alpha(p.Primary, 150));
            DrawTitleText(g, font, x + split, y, Theme.Alpha(p.Secondary, 150), Theme.Alpha(p.Secondary, 150));
            DrawTitleText(g, font, x, y, p.Ink, p.Secondary);

            TextRenderer.DrawText(g, _info.ToUpperInvariant(), Theme.Hud(7.5f), new Point((int)x + 2, 40), p.Ink3, TextFormatFlags.NoPadding);
        }

        static void DrawTitleText(Graphics g, Font font, float x, float y, Color ink, Color slashes)
        {
            Size fan = TextRenderer.MeasureText(g, "FAN", font, Size.Empty, TextFormatFlags.NoPadding);
            Size sep = TextRenderer.MeasureText(g, "//", font, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "FAN", font, new Point((int)x, (int)y), ink, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "//", font, new Point((int)x + fan.Width + 1, (int)y), slashes, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "CONTROL", font, new Point((int)x + fan.Width + sep.Width + 2, (int)y), ink, TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// Telemetry readout: CPU, GPU, fans and fan speed, each with a big value and a
    /// segmented "health bar" that shifts from the signal colour to warning and danger.
    /// </summary>
    public sealed class StatusHud : Control
    {
        sealed class Cell
        {
            public string Caption, Value;
            public float Fraction;    // 0–1, or -1 for no bar
            public bool Heat;         // colour by level (temperatures)
            public bool Signal;       // value in the signal colour
        }

        readonly Cell[] _cells =
        {
            new Cell { Caption = "CPU TEMP", Value = "--", Fraction = 0, Heat = true },
            new Cell { Caption = "GPU TEMP", Value = "--", Fraction = 0, Heat = true },
            new Cell { Caption = "FANS RPM", Value = "--", Fraction = 0 },
            new Cell { Caption = "FAN SPEED", Value = "--", Fraction = 0, Signal = true },
        };

        public StatusHud()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 62;
        }

        public void SetValues(int cpuC, int gpuC, IList<int> rpms, int maxRpm, string speedText, int speedPercent)
        {
            _cells[0].Value = cpuC >= 0 ? cpuC + "°C" : "--";
            _cells[0].Fraction = cpuC >= 0 ? cpuC / 100f : 0;
            _cells[1].Value = gpuC >= 0 ? gpuC + "°C" : "--";
            _cells[1].Fraction = gpuC >= 0 ? gpuC / 100f : 0;
            _cells[2].Value = rpms == null || rpms.Count == 0 ? "--" : string.Join(" · ", rpms);
            _cells[2].Fraction = rpms == null || rpms.Count == 0 ? 0 : rpms.Max() / (float)Math.Max(1000, maxRpm);
            _cells[3].Value = speedText;
            _cells[3].Fraction = Math.Max(0, speedPercent) / 100f;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cellWidth = (Width - 2) / (float)_cells.Length;
            for (int i = 0; i < _cells.Length; i++)
            {
                var cell = _cells[i];
                float x = 2 + i * cellWidth;
                if (i > 0)
                {
                    using (var pen = new Pen(p.Line))
                        g.DrawLine(pen, x - 6, 6, x - 6, Height - 6);
                }
                TextRenderer.DrawText(g, cell.Caption, Theme.Hud(7f, FontStyle.Bold), new Point((int)x, 2), p.Ink3, TextFormatFlags.NoPadding);

                Color valueColor = cell.Signal ? p.Primary : cell.Heat ? HeatColor(p, cell.Fraction, p.Ink) : p.Ink;
                var font = cell.Value.Length > 9 ? Theme.Display(10.5f) : Theme.Display(15f);
                TextRenderer.DrawText(g, cell.Value, font, new Rectangle((int)x, 15, (int)cellWidth - 10, 26), valueColor,
                    TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                DrawBar(g, new RectangleF(x, Height - 13, cellWidth - 14, 7), cell, p);
            }
        }

        static Color HeatColor(ThemePalette p, float fraction, Color cool)
        {
            if (fraction >= 0.85f) return p.Danger;
            if (fraction >= 0.7f) return p.Accent;
            return cool;
        }

        static void DrawBar(Graphics g, RectangleF r, Cell cell, ThemePalette p)
        {
            const int segments = 12;
            float gap = 2, w = (r.Width - gap * (segments - 1)) / segments;
            int lit = (int)Math.Round(Math.Max(0, Math.Min(1, cell.Fraction)) * segments);
            for (int s = 0; s < segments; s++)
            {
                float x = r.Left + s * (w + gap);
                Color c;
                if (s >= lit)
                    c = p.Line;
                else if (cell.Heat)
                    c = HeatColor(p, (s + 1) / (float)segments, p.Cool);
                else
                    c = Theme.Mix(p.Secondary, p.Primary, s / (float)(segments - 1));
                using (var brush = new SolidBrush(c))
                {
                    // Slanted segments, like a game HUD.
                    g.FillPolygon(brush, new[]
                    {
                        new PointF(x + 2, r.Top), new PointF(x + w + 2, r.Top),
                        new PointF(x + w, r.Bottom), new PointF(x, r.Bottom),
                    });
                }
            }
        }
    }
}
