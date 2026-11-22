using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AsusFanControlGUI
{
    /// <summary>
    /// Colours for one theme. The dark themes follow the NEON//GRID design language:
    /// a near-black "void", one bright primary signal colour, a secondary colour for
    /// markers and gradients, hairline borders and cut-corner panels.
    /// </summary>
    public sealed class ThemePalette
    {
        public string Name;
        public bool IsDark;
        /// <summary>Diagonal slash stripes in the background instead of a grid.</summary>
        public bool Slashes;
        /// <summary>Colour of temperatures and bars while they are in the safe range.</summary>
        public Color Cool;
        public Color Void, Panel, PanelHi, Line, LineHi;
        public Color Primary, Secondary, Accent, Danger, Ok;
        public Color Ink, Ink2, Ink3, Ink4, OnPrimary;
    }

    /// <summary>Current theme, fonts and helpers shared by the custom controls.</summary>
    public static class Theme
    {
        public const string DefaultName = "Neon Grid";

        public static readonly ThemePalette[] Palettes =
        {
            new ThemePalette
            {
                Name = "Neon Grid", IsDark = true,
                Void = Hex("05060a"), Panel = Hex("0c0f1c"), PanelHi = Hex("161c34"), Line = Hex("1d2b3d"), LineHi = Hex("0b7f8c"),
                Primary = Hex("00f0ff"), Secondary = Hex("ff2bd6"), Accent = Hex("e8ff3a"), Danger = Hex("ff3b5c"), Ok = Hex("3dffa8"),
                Ink = Hex("eef6ff"), Ink2 = Hex("b9c6dc"), Ink3 = Hex("7d8aa6"), Ink4 = Hex("56607a"), OnPrimary = Hex("001014"),
                Cool = Hex("00f0ff"),
            },
            new ThemePalette
            {
                // Black and crimson with slash stripes, in the spirit of gaming-laptop styling.
                Name = "Crimson Strike", IsDark = true, Slashes = true,
                Void = Hex("070405"), Panel = Hex("120709"), PanelHi = Hex("200c10"), Line = Hex("3b161b"), LineHi = Hex("8f1d2c"),
                Primary = Hex("ff1f3d"), Secondary = Hex("c9d1dd"), Accent = Hex("ffb020"), Danger = Hex("ff5a1f"), Ok = Hex("3dffa8"),
                Ink = Hex("f5f1f2"), Ink2 = Hex("cdbfc2"), Ink3 = Hex("8e7a7e"), Ink4 = Hex("5e4d51"), OnPrimary = Hex("ffffff"),
                Cool = Hex("c9d1dd"),
            },
            new ThemePalette
            {
                Name = "Matrix", IsDark = true,
                Void = Hex("030805"), Panel = Hex("08120b"), PanelHi = Hex("0f2215"), Line = Hex("173a22"), LineHi = Hex("1f7a3e"),
                Primary = Hex("3dff8a"), Secondary = Hex("00c2ff"), Accent = Hex("e8ff3a"), Danger = Hex("ff3b5c"), Ok = Hex("3dff8a"),
                Ink = Hex("e6ffee"), Ink2 = Hex("b5d9c0"), Ink3 = Hex("6f9a7d"), Ink4 = Hex("4b6b55"), OnPrimary = Hex("001a08"),
                Cool = Hex("3dff8a"),
            },
            new ThemePalette
            {
                Name = "Ember", IsDark = true,
                Void = Hex("0a0605"), Panel = Hex("160d0a"), PanelHi = Hex("261612"), Line = Hex("3a2318"), LineHi = Hex("8a4a1c"),
                Primary = Hex("ff8a1f"), Secondary = Hex("ff3b5c"), Accent = Hex("ffd23a"), Danger = Hex("ff3b5c"), Ok = Hex("3dffa8"),
                Ink = Hex("fff3ea"), Ink2 = Hex("e0c7b8"), Ink3 = Hex("a5826f"), Ink4 = Hex("74594b"), OnPrimary = Hex("1a0a00"),
                Cool = Hex("3dffa8"),
            },
            new ThemePalette
            {
                Name = "Daylight", IsDark = false,
                Void = Hex("eef1f7"), Panel = Hex("ffffff"), PanelHi = Hex("e8eef8"), Line = Hex("d3dae6"), LineHi = Hex("7aa7d9"),
                Primary = Hex("0067c0"), Secondary = Hex("c2185b"), Accent = Hex("b38600"), Danger = Hex("d32f2f"), Ok = Hex("1b8a4b"),
                Ink = Hex("121826"), Ink2 = Hex("3a4458"), Ink3 = Hex("6b7488"), Ink4 = Hex("a0a8b8"), OnPrimary = Hex("ffffff"),
                Cool = Hex("0067c0"),
            },
        };

        static ThemePalette _current = Palettes[0];

        public static ThemePalette Current
        {
            get { return _current; }
        }

        public static event Action Changed;

        public static void Set(string name)
        {
            var palette = Palettes.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Palettes[0];
            if (palette == _current)
                return;
            _current = palette;
            var handler = Changed;
            if (handler != null)
                handler();
        }

        // ---------------------------------------------------------------- fonts

        static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();
        static string _displayFamily, _hudFamily, _bodyFamily;

        /// <summary>Angular display face (Chakra Petch when installed, else Bahnschrift).</summary>
        public static Font Display(float size, FontStyle style = FontStyle.Bold)
        {
            return Get(ref _displayFamily, new[] { "Chakra Petch", "Bahnschrift SemiBold", "Bahnschrift", "Segoe UI Semibold" }, size, style);
        }

        /// <summary>Monospace face for data (JetBrains Mono when installed, else Cascadia Mono).</summary>
        public static Font Hud(float size, FontStyle style = FontStyle.Regular)
        {
            return Get(ref _hudFamily, new[] { "JetBrains Mono", "Cascadia Mono", "Cascadia Code", "Consolas" }, size, style);
        }

        /// <summary>Body text (Space Grotesk when installed, else Segoe UI).</summary>
        public static Font Body(float size, FontStyle style = FontStyle.Regular)
        {
            return Get(ref _bodyFamily, new[] { "Space Grotesk", "Segoe UI" }, size, style);
        }

        static Font Get(ref string family, string[] candidates, float size, FontStyle style)
        {
            if (family == null)
            {
                using (var installed = new InstalledFontCollection())
                {
                    var names = new HashSet<string>(installed.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
                    family = candidates.FirstOrDefault(names.Contains) ?? candidates[candidates.Length - 1];
                }
            }
            string key = family + "|" + size + "|" + style;
            Font font;
            if (!Fonts.TryGetValue(key, out font))
            {
                font = new Font(family, size, style);
                Fonts[key] = font;
            }
            return font;
        }

        // ---------------------------------------------------------------- helpers

        public static Color Hex(string rgb)
        {
            return Color.FromArgb(Convert.ToInt32(rgb.Substring(0, 2), 16), Convert.ToInt32(rgb.Substring(2, 2), 16), Convert.ToInt32(rgb.Substring(4, 2), 16));
        }

        public static Color Alpha(Color color, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color);
        }

        public static Color Mix(Color a, Color b, float amountOfB)
        {
            float t = Math.Max(0, Math.Min(1, amountOfB));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>Rectangle with the top-right and bottom-left corners cut off.</summary>
        public static GraphicsPath CutRect(RectangleF r, float cut)
        {
            var path = new GraphicsPath();
            path.AddPolygon(new[]
            {
                new PointF(r.Left, r.Top),
                new PointF(r.Right - cut, r.Top),
                new PointF(r.Right, r.Top + cut),
                new PointF(r.Right, r.Bottom),
                new PointF(r.Left + cut, r.Bottom),
                new PointF(r.Left, r.Bottom - cut),
            });
            return path;
        }

        /// <summary>Rectangle with the top-left and bottom-right corners cut off (buttons).</summary>
        public static GraphicsPath CutRectAlt(RectangleF r, float cut)
        {
            var path = new GraphicsPath();
            path.AddPolygon(new[]
            {
                new PointF(r.Left + cut, r.Top),
                new PointF(r.Right, r.Top),
                new PointF(r.Right, r.Bottom - cut),
                new PointF(r.Right - cut, r.Bottom),
                new PointF(r.Left, r.Bottom),
                new PointF(r.Left, r.Top + cut),
            });
            return path;
        }

        /// <summary>Faint grid and two colour glows, the "living background" of NEON//GRID.</summary>
        public static void PaintBackdrop(Graphics g, Rectangle area)
        {
            var p = Current;
            g.Clear(p.Void);
            int glowAlpha = p.IsDark ? 34 : 22;
            PaintGlow(g, new RectangleF(area.Right - area.Width * 0.9f, -area.Height * 0.35f, area.Width * 1.3f, area.Height * 0.8f), Alpha(p.Secondary, glowAlpha));
            PaintGlow(g, new RectangleF(-area.Width * 0.5f, -area.Height * 0.3f, area.Width * 1.1f, area.Height * 0.7f), Alpha(p.Primary, glowAlpha - 6));
            if (p.Slashes)
            {
                // Bold diagonal slashes, strongest at the bottom right.
                g.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = 0; i < 9; i++)
                {
                    float x = area.Right - 60 - i * 46;
                    int alpha = Math.Max(0, 30 - i * 3);
                    using (var brush = new SolidBrush(Alpha(i % 4 == 2 ? p.Secondary : p.Primary, alpha)))
                        g.FillPolygon(brush, new[]
                        {
                            new PointF(x + area.Height * 0.6f, area.Top), new PointF(x + area.Height * 0.6f + 18, area.Top),
                            new PointF(x + 18, area.Bottom), new PointF(x, area.Bottom),
                        });
                }
            }
            using (var pen = new Pen(Alpha(p.Primary, p.IsDark ? (p.Slashes ? 8 : 14) : 18)))
            {
                for (int x = area.Left; x < area.Right; x += 32)
                    g.DrawLine(pen, x, area.Top, x, area.Bottom);
                for (int y = area.Top; y < area.Bottom; y += 32)
                    g.DrawLine(pen, area.Left, y, area.Right, y);
            }
        }

        static void PaintGlow(Graphics g, RectangleF bounds, Color center)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(bounds);
                using (var brush = new PathGradientBrush(path) { CenterColor = center, SurroundColors = new[] { Color.FromArgb(0, center) } })
                    g.FillPath(brush, path);
            }
        }

        /// <summary>Applies the palette to a window and everything in it.</summary>
        public static void Apply(Control root)
        {
            var p = Current;
            ApplyTo(root, p);
            var form = root as Form;
            if (form != null)
            {
                form.BackColor = p.Void;
                form.ForeColor = p.Ink;
                SetDarkTitleBar(form, p.IsDark);
            }
            root.Invalidate(true);
        }

        static void ApplyTo(Control control, ThemePalette p)
        {
            if (control is NeonPanel)
            {
                control.BackColor = p.Panel;
                control.ForeColor = p.Ink;
            }
            else if (control is TableLayoutPanel || control is FlowLayoutPanel)
            {
                control.BackColor = Color.Transparent;
            }
            else if (control is Label)
            {
                control.BackColor = Color.Transparent;
                string role = control.Tag as string;
                control.ForeColor = role == "hint" || role == "caption" ? p.Ink3
                    : role == "value" ? p.Ink
                    : role == "signal" ? p.Primary
                    : p.Ink2;
            }
            else if (control is ComboBox)
            {
                control.BackColor = p.PanelHi;
                control.ForeColor = p.Ink;
            }
            else if (control is ToolStrip)
            {
                var strip = (ToolStrip)control;
                strip.Renderer = new NeonRenderer();
                strip.BackColor = p.Panel;
                strip.ForeColor = p.Ink2;
                foreach (ToolStripItem item in strip.Items)
                    ApplyToItem(item, p);
            }
            else
            {
                control.ForeColor = p.Ink;
            }

            foreach (Control child in control.Controls)
                ApplyTo(child, p);
        }

        static void ApplyToItem(ToolStripItem item, ThemePalette p)
        {
            item.ForeColor = p.Ink2;
            var menu = item as ToolStripMenuItem;
            if (menu != null)
            {
                menu.DropDown.Renderer = new NeonRenderer();
                menu.DropDown.BackColor = p.Panel;
                foreach (ToolStripItem child in menu.DropDownItems)
                    ApplyToItem(child, p);
            }
        }

        public static void ApplyMenu(ToolStripDropDown menu)
        {
            menu.Renderer = new NeonRenderer();
            menu.BackColor = Current.Panel;
            foreach (ToolStripItem item in menu.Items)
                ApplyToItem(item, Current);
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>Dark window title bar on Windows 10 2004+ / 11 (ignored elsewhere).</summary>
        public static void SetDarkTitleBar(Form form, bool dark)
        {
            if (!form.IsHandleCreated)
                return;
            try
            {
                int value = dark ? 1 : 0;
                DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int));
            }
            catch (Exception)
            {
            }
        }
    }

    // ==================================================================== controls

    /// <summary>Cut-corner panel with a hairline border and a "// TITLE" HUD caption.</summary>
    public sealed class NeonPanel : GroupBox
    {
        public NeonPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        public override Rectangle DisplayRectangle
        {
            get
            {
                // Room for the caption above the content.
                int top = 22;
                return new Rectangle(Padding.Left + 1, top + Padding.Top, Math.Max(0, Width - Padding.Horizontal - 2), Math.Max(0, Height - top - Padding.Vertical - 1));
            }
        }

        /// <summary>
        /// GroupBox sizes itself for its own caption; this panel's caption area is taller
        /// (see <see cref="DisplayRectangle"/>), so measure the content directly.
        /// </summary>
        public override Size GetPreferredSize(Size proposedSize)
        {
            if (Controls.Count != 1)
                return base.GetPreferredSize(proposedSize);
            Control content = Controls[0];
            int chromeW = Padding.Horizontal + 2, chromeH = 22 + Padding.Vertical + 1;
            int width = proposedSize.Width > 0 && proposedSize.Width < int.MaxValue ? proposedSize.Width - chromeW - content.Margin.Horizontal : 0;
            Size inner = content.GetPreferredSize(new Size(Math.Max(0, width), 0));
            return new Size(inner.Width + chromeW + content.Margin.Horizontal, inner.Height + chromeH + content.Margin.Vertical);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The parent's backdrop shows in the cut corners.
            var parent = Parent;
            if (parent != null)
            {
                var state = e.Graphics.Save();
                e.Graphics.TranslateTransform(-Left, -Top);
                InvokePaintBackground(parent, new PaintEventArgs(e.Graphics, new Rectangle(Location, Size)));
                InvokePaint(parent, new PaintEventArgs(e.Graphics, new Rectangle(Location, Size)));
                e.Graphics.Restore(state);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.CutRect(r, 12))
            {
                using (var brush = new SolidBrush(Theme.Alpha(p.Panel, p.IsDark ? 235 : 250)))
                    g.FillPath(brush, path);
                using (var pen = new Pen(p.Line))
                    g.DrawPath(pen, path);
            }
            // Corner bracket in the signal colour.
            using (var pen = new Pen(p.Primary, 2f))
            {
                g.DrawLine(pen, 1, 1, 14, 1);
                g.DrawLine(pen, 1, 1, 1, 14);
            }
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            string caption = "// " + (Text ?? "").ToUpperInvariant();
            TextRenderer.DrawText(g, caption, Theme.Hud(7.5f, FontStyle.Bold), new Point(12, 5), p.Primary, TextFormatFlags.NoPadding);
        }
    }

    public enum NeonButtonKind
    {
        Primary,
        Ghost,
    }

    /// <summary>Angular button: filled in the signal colour (Primary) or outlined (Ghost).</summary>
    public sealed class NeonButton : Button
    {
        bool _hover, _pressed;

        public NeonButtonKind Kind { get; set; }

        public NeonButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Kind = NeonButtonKind.Ghost;
        }

        string Caption
        {
            get { return (Text ?? "").ToUpperInvariant(); }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Caption, Theme.Display(8.5f), Size.Empty, TextFormatFlags.NoPadding);
            return new Size(text.Width + 26, Math.Max(26, text.Height + 12));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { _pressed = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { _pressed = false; Invalidate(); base.OnMouseUp(mevent); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.Current;
            var g = e.Graphics;
            ButtonRenderer.DrawParentBackground(g, ClientRectangle, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (_pressed)
                r.Inflate(-1, -1);
            Color signal = Enabled ? p.Primary : p.Ink4;
            Color text;
            using (var path = Theme.CutRectAlt(r, 7))
            {
                if (Kind == NeonButtonKind.Primary && Enabled)
                {
                    Color fill = _hover ? Theme.Mix(signal, Color.White, p.IsDark ? 0.25f : 0.15f) : signal;
                    using (var brush = new SolidBrush(fill))
                        g.FillPath(brush, path);
                    text = p.OnPrimary;
                }
                else
                {
                    using (var brush = new SolidBrush(_hover && Enabled ? Theme.Alpha(signal, 40) : Theme.Alpha(p.PanelHi, 200)))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(Enabled ? (_hover ? signal : Theme.Alpha(signal, 170)) : p.Line))
                        g.DrawPath(pen, path);
                    text = Enabled ? signal : p.Ink4;
                }
            }
            if (Focused && ShowFocusCues)
            {
                using (var pen = new Pen(Theme.Alpha(signal, 140)) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, 3, 3, Width - 7, Height - 7);
            }
            TextRenderer.DrawText(g, Caption, Theme.Display(8.5f), new Rectangle(0, 0, Width, Height), text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Square check box that fills with the signal colour.</summary>
    public class NeonCheckBox : CheckBox
    {
        protected bool Hover;

        public NeonCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            return new Size(text.Width + 24, Math.Max(20, text.Height + 4));
        }

        protected override void OnMouseEnter(EventArgs eventargs) { Hover = true; Invalidate(); base.OnMouseEnter(eventargs); }
        protected override void OnMouseLeave(EventArgs eventargs) { Hover = false; Invalidate(); base.OnMouseLeave(eventargs); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.Current;
            var g = e.Graphics;
            ButtonRenderer.DrawParentBackground(g, ClientRectangle, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color signal = Enabled ? p.Primary : p.Ink4;
            var box = new RectangleF(1.5f, (Height - 14) / 2f, 13, 13);
            using (var brush = new SolidBrush(Checked ? signal : p.PanelHi))
                g.FillRectangle(brush, box);
            using (var pen = new Pen(Checked || (Hover && Enabled) ? signal : p.LineHi))
                g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
            if (Checked)
            {
                using (var pen = new Pen(p.OnPrimary, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(pen, new[] { new PointF(box.X + 3, box.Y + 6.5f), new PointF(box.X + 5.5f, box.Y + 9.5f), new PointF(box.X + 10, box.Y + 3.5f) });
            }
            DrawText(g, p);
            if (Focused && ShowFocusCues)
            {
                using (var pen = new Pen(Theme.Alpha(signal, 140)) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        protected void DrawText(Graphics g, ThemePalette p)
        {
            TextRenderer.DrawText(g, Text, Font, new Rectangle(20, 0, Width - 20, Height), Enabled ? ForeColorOrInk(p) : p.Ink4,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        Color ForeColorOrInk(ThemePalette p)
        {
            return Font.Bold ? p.Ink : p.Ink2;
        }
    }

    /// <summary>Radio button drawn as a diamond.</summary>
    public sealed class NeonRadioButton : RadioButton
    {
        bool _hover;

        public NeonRadioButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            return new Size(text.Width + 24, Math.Max(20, text.Height + 4));
        }

        protected override void OnMouseEnter(EventArgs eventargs) { _hover = true; Invalidate(); base.OnMouseEnter(eventargs); }
        protected override void OnMouseLeave(EventArgs eventargs) { _hover = false; Invalidate(); base.OnMouseLeave(eventargs); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.Current;
            var g = e.Graphics;
            ButtonRenderer.DrawParentBackground(g, ClientRectangle, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color signal = Enabled ? p.Primary : p.Ink4;
            float cx = 8, cy = Height / 2f;
            var outer = new[] { new PointF(cx, cy - 7), new PointF(cx + 7, cy), new PointF(cx, cy + 7), new PointF(cx - 7, cy) };
            using (var brush = new SolidBrush(p.PanelHi))
                g.FillPolygon(brush, outer);
            using (var pen = new Pen(Checked || (_hover && Enabled) ? signal : p.LineHi))
                g.DrawPolygon(pen, outer);
            if (Checked)
            {
                var inner = new[] { new PointF(cx, cy - 3.5f), new PointF(cx + 3.5f, cy), new PointF(cx, cy + 3.5f), new PointF(cx - 3.5f, cy) };
                using (var brush = new SolidBrush(signal))
                    g.FillPolygon(brush, inner);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(20, 0, Width - 20, Height), Enabled ? (Checked ? p.Ink : p.Ink2) : p.Ink4,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
            {
                using (var pen = new Pen(Theme.Alpha(signal, 140)) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    /// <summary>Drop-down list drawn in the theme colours.</summary>
    public sealed class NeonComboBox : ComboBox
    {
        const int WM_PAINT = 0x000F;
        bool _hover;

        public NeonComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            ItemHeight = 18;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != WM_PAINT)
                return;
            // The flat style still paints a system-coloured arrow button and border; cover them.
            var p = Theme.Current;
            using (var g = Graphics.FromHwnd(Handle))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int button = SystemInformation.VerticalScrollBarWidth;
                var arrowBox = new Rectangle(Width - button - 1, 1, button, Height - 2);
                using (var brush = new SolidBrush(p.PanelHi))
                    g.FillRectangle(brush, arrowBox);
                Color signal = Enabled ? p.Primary : p.Ink4;
                float cx = arrowBox.Left + arrowBox.Width / 2f, cy = Height / 2f;
                using (var pen = new Pen(signal, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(pen, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
                g.SmoothingMode = SmoothingMode.None;
                using (var pen = new Pen(_hover || Focused ? signal : p.LineHi))
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            var p = Theme.Current;
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var brush = new SolidBrush(selected ? Theme.Alpha(p.Primary, 60) : p.PanelHi))
                e.Graphics.FillRectangle(brush, e.Bounds);
            if (selected)
            {
                using (var brush = new SolidBrush(p.Primary))
                    e.Graphics.FillRectangle(brush, e.Bounds.Left, e.Bounds.Top, 2, e.Bounds.Height);
            }
            if (e.Index >= 0)
            {
                TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, new Rectangle(e.Bounds.Left + 5, e.Bounds.Top, e.Bounds.Width - 5, e.Bounds.Height),
                    Enabled ? p.Ink : p.Ink4, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>Horizontal slider with a gradient fill and a glowing knob (replaces TrackBar).</summary>
    public sealed class NeonSlider : Control
    {
        int _minimum, _maximum = 100, _value;
        bool _dragging;

        public event EventHandler ValueChanged;

        public NeonSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            TabStop = true;
            Height = 30;
            Cursor = Cursors.Hand;
        }

        public int Minimum { get { return _minimum; } set { _minimum = value; Value = _value; Invalidate(); } }
        public int Maximum { get { return _maximum; } set { _maximum = value; Value = _value; Invalidate(); } }

        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
                if (clamped == _value)
                    return;
                _value = clamped;
                Invalidate();
                var handler = ValueChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
        }

        RectangleF Track
        {
            get { return new RectangleF(8, Height / 2f - 2, Width - 16, 4); }
        }

        float KnobX
        {
            get
            {
                var t = Track;
                return t.Left + (_maximum == _minimum ? 0 : (_value - _minimum) * t.Width / (_maximum - _minimum));
            }
        }

        void SetFromX(int x)
        {
            var t = Track;
            double fraction = Math.Max(0, Math.Min(1, (x - t.Left) / t.Width));
            Value = _minimum + (int)Math.Round(fraction * (_maximum - _minimum));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            Focus();
            _dragging = true;
            Capture = true;
            SetFromX(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
                SetFromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: Value -= 1; break;
                case Keys.Right: case Keys.Up: Value += 1; break;
                case Keys.PageDown: Value -= 10; break;
                case Keys.PageUp: Value += 10; break;
                case Keys.Home: Value = _minimum; break;
                case Keys.End: Value = _maximum; break;
            }
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var t = Track;
            using (var brush = new SolidBrush(p.Line))
                g.FillRectangle(brush, t);
            // Ticks every 10%.
            using (var pen = new Pen(Theme.Alpha(p.Ink3, 90)))
            {
                for (int i = 0; i <= 10; i++)
                {
                    float x = t.Left + i * t.Width / 10;
                    g.DrawLine(pen, x, t.Bottom + 4, x, t.Bottom + 7);
                }
            }
            float knob = KnobX;
            if (knob > t.Left + 1)
            {
                var fill = new RectangleF(t.Left, t.Top, knob - t.Left, t.Height);
                Color from = Enabled ? p.Secondary : p.Ink4, to = Enabled ? p.Primary : p.Ink4;
                using (var brush = new LinearGradientBrush(new RectangleF(t.Left - 1, t.Top, t.Width + 2, t.Height), from, to, LinearGradientMode.Horizontal))
                    g.FillRectangle(brush, fill);
            }
            Color signal = Enabled ? p.Primary : p.Ink4;
            if (Enabled)
            {
                using (var glow = new SolidBrush(Theme.Alpha(signal, Focused || _dragging ? 90 : 50)))
                    g.FillRectangle(glow, knob - 9, Height / 2f - 9, 18, 18);
            }
            using (var brush = new SolidBrush(signal))
                g.FillRectangle(brush, knob - 6, Height / 2f - 6, 12, 12);
        }
    }

    // ==================================================================== menus

    /// <summary>Menu, status bar and context menu colours from the current theme.</summary>
    public sealed class NeonRenderer : ToolStripProfessionalRenderer
    {
        public NeonRenderer() : base(new NeonColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var p = Theme.Current;
            e.TextColor = !e.Item.Enabled ? p.Ink4 : e.Item.Selected || e.Item.Pressed ? p.Ink : p.Ink2;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Current.Ink2;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var p = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(e.ImageRectangle.Left + 2, e.ImageRectangle.Top + 2, e.ImageRectangle.Width - 4, e.ImageRectangle.Height - 4);
            using (var brush = new SolidBrush(p.Primary))
                g.FillRectangle(brush, r);
            using (var pen = new Pen(p.OnPrimary, 2f))
                g.DrawLines(pen, new[] { new PointF(r.Left + 3, r.Top + r.Height / 2f), new PointF(r.Left + r.Width / 2.5f, r.Bottom - 3), new PointF(r.Right - 3, r.Top + 3) });
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                using (var pen = new Pen(Theme.Current.LineHi))
                    e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
            else if (e.ToolStrip is StatusStrip)
            {
                using (var pen = new Pen(Theme.Current.Line))
                    e.Graphics.DrawLine(pen, 0, 0, e.ToolStrip.Width, 0);
            }
            else
            {
                using (var pen = new Pen(Theme.Current.Line))
                    e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
            }
        }
    }

    sealed class NeonColors : ProfessionalColorTable
    {
        public NeonColors()
        {
            UseSystemColors = false;
        }

        static ThemePalette P { get { return Theme.Current; } }
        static Color Hi { get { return Theme.Mix(P.Panel, P.Primary, 0.18f); } }

        public override Color MenuStripGradientBegin { get { return P.Panel; } }
        public override Color MenuStripGradientEnd { get { return P.Panel; } }
        public override Color StatusStripGradientBegin { get { return P.Panel; } }
        public override Color StatusStripGradientEnd { get { return P.Panel; } }
        public override Color ToolStripDropDownBackground { get { return P.Panel; } }
        public override Color ImageMarginGradientBegin { get { return P.Panel; } }
        public override Color ImageMarginGradientMiddle { get { return P.Panel; } }
        public override Color ImageMarginGradientEnd { get { return P.Panel; } }
        public override Color MenuBorder { get { return P.LineHi; } }
        public override Color MenuItemBorder { get { return P.LineHi; } }
        public override Color MenuItemSelected { get { return Hi; } }
        public override Color MenuItemSelectedGradientBegin { get { return Hi; } }
        public override Color MenuItemSelectedGradientEnd { get { return Hi; } }
        public override Color MenuItemPressedGradientBegin { get { return P.PanelHi; } }
        public override Color MenuItemPressedGradientMiddle { get { return P.PanelHi; } }
        public override Color MenuItemPressedGradientEnd { get { return P.PanelHi; } }
        public override Color SeparatorDark { get { return P.Line; } }
        public override Color SeparatorLight { get { return P.Line; } }
        public override Color CheckBackground { get { return P.PanelHi; } }
        public override Color CheckSelectedBackground { get { return P.PanelHi; } }
        public override Color CheckPressedBackground { get { return P.PanelHi; } }
    }
}
