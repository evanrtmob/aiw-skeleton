using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AppInWhats
{
    // Paleta y tipografía Fluent (Windows 11). Sigue el modo claro/oscuro y el color de acento del sistema.
    static class Theme
    {
        public static bool Dark;
        public static bool IsWin11;

        public static Color Bg, Layer, Card, CardStroke, Divider;
        public static Color TextPrimary, TextSecondary, TextTertiary, TextDisabled;
        public static Color ControlFill, ControlFillHover, ControlFillPressed, ControlFillDisabled, ControlStroke, ControlStrokeBottom;
        public static Color InputFill, InputFillFocus, InputBottom;
        public static Color Accent, AccentHover, AccentPressed, AccentDisabled, AccentText, OnAccent, OnAccentDisabled;
        public static Color Danger, DangerHover, DangerPressed;
        public static Color Success, Caution, Critical, SuccessBg, CautionBg, CriticalBg, InfoBg;
        public static Color TileFill, TileFillHover, TileFillPressed, TileStroke;
        public static Color MenuBg, CloseHover, ClosePressed, FocusOuter, FocusInner;
        public static Color LogText, LogMuted, LogError, LogWarn, LogStderr, LogInfo;

        public static string TextFamily, TextSemiboldFamily, SmallFamily, SmallSemiboldFamily, DisplayFamily, IconFamily, MonoFamily;

        static readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();

        static Theme()
        {
            IsWin11 = DetectWin11();
            ResolveFonts();
            Init(SystemUsesDarkTheme());
        }

        public static void Init(bool dark)
        {
            Dark = dark;
            Color[] pal = ReadAccentPalette();
            if (!dark)
            {
                Bg = Gray(243); Layer = Gray(249); Card = Gray(255); CardStroke = Gray(229); Divider = Gray(235);
                TextPrimary = Gray(27); TextSecondary = Gray(96); TextTertiary = Gray(138); TextDisabled = Gray(163);
                ControlFill = Gray(251); ControlFillHover = Gray(246); ControlFillPressed = Gray(243); ControlFillDisabled = Gray(245);
                ControlStroke = Gray(229); ControlStrokeBottom = Gray(206);
                InputFill = Gray(251); InputFillFocus = Gray(255); InputBottom = Gray(134);
                Accent = pal != null ? pal[4] : Color.FromArgb(0, 95, 184);
                AccentText = pal != null ? pal[5] : Color.FromArgb(0, 62, 146);
                AccentDisabled = Gray(191); OnAccent = Color.White; OnAccentDisabled = Color.White;
                Success = Color.FromArgb(15, 123, 15); Caution = Color.FromArgb(157, 93, 0); Critical = Color.FromArgb(196, 43, 28);
                SuccessBg = Color.FromArgb(223, 246, 221); CautionBg = Color.FromArgb(255, 244, 206); CriticalBg = Color.FromArgb(253, 231, 233); InfoBg = Gray(246);
                TileFill = Gray(250); TileFillHover = Gray(244); TileFillPressed = Gray(240); TileStroke = Gray(234);
                MenuBg = Gray(249); FocusOuter = Gray(27); FocusInner = Color.White;
                LogText = Gray(36); LogMuted = Gray(112); LogError = Color.FromArgb(196, 43, 28); LogWarn = Color.FromArgb(157, 93, 0);
                LogStderr = Color.FromArgb(140, 92, 24); LogInfo = Color.FromArgb(0, 120, 150);
            }
            else
            {
                Bg = Gray(32); Layer = Gray(39); Card = Gray(45); CardStroke = Gray(29); Divider = Gray(58);
                TextPrimary = Gray(255); TextSecondary = Gray(206); TextTertiary = Gray(150); TextDisabled = Gray(110);
                ControlFill = Gray(52); ControlFillHover = Gray(58); ControlFillPressed = Gray(46); ControlFillDisabled = Gray(48);
                ControlStroke = Gray(62); ControlStrokeBottom = Gray(52);
                InputFill = Gray(52); InputFillFocus = Gray(30); InputBottom = Gray(154);
                Accent = pal != null ? pal[1] : Color.FromArgb(96, 205, 255);
                AccentText = pal != null ? pal[0] : Color.FromArgb(153, 235, 255);
                AccentDisabled = Gray(68); OnAccent = Color.Black; OnAccentDisabled = Gray(135);
                Success = Color.FromArgb(108, 203, 95); Caution = Color.FromArgb(252, 225, 0); Critical = Color.FromArgb(255, 153, 164);
                SuccessBg = Color.FromArgb(57, 61, 27); CautionBg = Color.FromArgb(67, 53, 25); CriticalBg = Color.FromArgb(68, 39, 38); InfoBg = Gray(46);
                TileFill = Gray(50); TileFillHover = Gray(56); TileFillPressed = Gray(47); TileStroke = Gray(58);
                MenuBg = Gray(44); FocusOuter = Color.White; FocusInner = Color.Black;
                LogText = Gray(228); LogMuted = Gray(150); LogError = Color.FromArgb(255, 153, 164); LogWarn = Color.FromArgb(240, 200, 90);
                LogStderr = Color.FromArgb(225, 170, 95); LogInfo = Color.FromArgb(96, 205, 255);
            }
            AccentHover = Gfx.Blend(Accent, Layer, 0.10f);
            AccentPressed = Gfx.Blend(Accent, Layer, 0.20f);
            Danger = Color.FromArgb(196, 43, 28); DangerHover = Color.FromArgb(209, 62, 48); DangerPressed = Color.FromArgb(171, 36, 23);
            CloseHover = Color.FromArgb(196, 43, 28); ClosePressed = Color.FromArgb(200, 64, 52);
        }

        static Color Gray(int v) { return Color.FromArgb(v, v, v); }

        public static Color SubtleHover(Color under)
        {
            return Dark ? Gfx.Blend(under, Color.White, 0.0605f) : Gfx.Blend(under, Color.Black, 0.0373f);
        }

        public static Color SubtlePressed(Color under)
        {
            return Dark ? Gfx.Blend(under, Color.White, 0.0419f) : Gfx.Blend(under, Color.Black, 0.0241f);
        }

        public static bool SystemUsesDarkTheme()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k != null ? k.GetValue("AppsUseLightTheme") : null;
                    if (v is int) return (int)v == 0;
                }
            }
            catch { }
            return false;
        }

        // AccentPalette: 8 colores RGBA (Light3, Light2, Light1, Base, Dark1, Dark2, Dark3, ...)
        static Color[] ReadAccentPalette()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    byte[] p = k != null ? k.GetValue("AccentPalette") as byte[] : null;
                    if (p == null || p.Length < 28) return null;
                    Color[] c = new Color[7];
                    for (int i = 0; i < 7; i++) c[i] = Color.FromArgb(p[i * 4], p[i * 4 + 1], p[i * 4 + 2]);
                    return c;
                }
            }
            catch { return null; }
        }

        static bool DetectWin11()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int build;
                    object v = k != null ? k.GetValue("CurrentBuildNumber") : null;
                    return v != null && int.TryParse(v.ToString(), out build) && build >= 22000;
                }
            }
            catch { return false; }
        }

        static void ResolveFonts()
        {
            HashSet<string> installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (InstalledFontCollection c = new InstalledFontCollection())
                    foreach (FontFamily f in c.Families) installed.Add(f.Name);
            }
            catch { }
            TextFamily = Pick(installed, "Segoe UI Variable Text", "Segoe UI");
            TextSemiboldFamily = Pick(installed, "Segoe UI Variable Text Semibold", "Segoe UI Semibold");
            SmallFamily = Pick(installed, "Segoe UI Variable Small", "Segoe UI");
            SmallSemiboldFamily = Pick(installed, "Segoe UI Variable Small Semibol", "Segoe UI Semibold");
            DisplayFamily = Pick(installed, "Segoe UI Variable Display Semib", "Segoe UI Semibold");
            IconFamily = Pick(installed, "Segoe Fluent Icons", "Segoe MDL2 Assets");
            MonoFamily = Pick(installed, "Cascadia Mono", "Consolas");
        }

        static string Pick(HashSet<string> installed, string preferred, string fallback)
        {
            return installed.Contains(preferred) ? preferred : fallback;
        }

        static Font Get(string family, float size)
        {
            string key = family + "|" + size.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Font f;
            if (!_fonts.TryGetValue(key, out f))
            {
                f = new Font(family, size, FontStyle.Regular, GraphicsUnit.Point);
                _fonts[key] = f;
            }
            return f;
        }

        public static Font Text(float size) { return Get(size <= 9f ? SmallFamily : TextFamily, size); }
        public static Font Strong(float size) { return Get(size <= 9f ? SmallSemiboldFamily : TextSemiboldFamily, size); }
        public static Font Display(float size) { return Get(DisplayFamily, size); }
        public static Font Icon(float size) { return Get(IconFamily, size); }
        public static Font Mono(float size) { return Get(MonoFamily, size); }
    }

    static class Glyphs
    {
        public const string Nav = "\uE700", Home = "\uE80F", Terminal = "\uE756", Settings = "\uE713";
        public const string Code = "\uE943", History = "\uE81C", Globe = "\uE774", Package = "\uE7B8", Swap = "\uE8AB";
        public const string Bolt = "\uE945", Speed = "\uEC4A", Cloud = "\uE753", Server = "\uE968", Sliders = "\uE9E9";
        public const string Download = "\uE896", Upload = "\uE898", Wrench = "\uE90F", Tools = "\uEC7A", Laptop = "\uE7F8";
        public const string Play = "\uE768", Stop = "\uE71A", Edit = "\uE70F", ChevronRight = "\uE76C", ChevronUp = "\uE70E", ChevronDown = "\uE70D";
        public const string Gripper = "\uE76F", Copy = "\uE8C8", Delete = "\uE74D", FolderOpen = "\uE838", View = "\uE890", Check = "\uE73E";
        public const string Sort = "\uE8CB", Info = "\uE946", Warning = "\uE7BA", OpenExternal = "\uE8A7", Branch = "\uF003";
        public const string Minimize = "\uE921", Maximize = "\uE922", Restore = "\uE923", Close = "\uE8BB", Cancel = "\uE711";
    }

    static class Gfx
    {
        public static Color Blend(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t + 0.5f),
                (int)(a.G + (b.G - a.G) * t + 0.5f),
                (int)(a.B + (b.B - a.B) * t + 0.5f));
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int rad = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            if (rad <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            int d = rad * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill)
        {
            using (GraphicsPath p = RoundRect(r, radius))
            using (SolidBrush b = new SolidBrush(fill))
                g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Rectangle r, int radius, Color stroke, float width)
        {
            using (GraphicsPath p = RoundRect(r, radius))
            using (Pen pen = new Pen(stroke, width))
                g.DrawPath(pen, p);
        }

        // Tarjeta: relleno + borde 1px
        public static void Card(Graphics g, Rectangle r, int radius, Color fill, Color stroke)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = RoundRect(r, radius))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(stroke, 1f)) g.DrawPath(pen, p);
            }
            g.SmoothingMode = old;
        }

        public static void Text(Graphics g, string text, Font font, Rectangle r, Color color, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text ?? "", font, r, color, flags | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        public static void TextLeft(Graphics g, string text, Font font, Rectangle r, Color color)
        {
            Text(g, text, font, r, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        public static void Glyph(Graphics g, string glyph, float size, Rectangle r, Color color)
        {
            Text(g, glyph, Theme.Icon(size), r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        public static int Measure(string text, Font font)
        {
            return TextRenderer.MeasureText(text ?? "", font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        }

        public static void FocusRing(Graphics g, Rectangle r, int radius)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawRound(g, new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), radius, Theme.FocusOuter, 2f);
            DrawRound(g, new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4), Math.Max(1, radius - 2), Theme.FocusInner, 1f);
            g.SmoothingMode = old;
        }

        public static string Bytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0") + " MB";
            return (mb / 1024.0).ToString("0.00") + " GB";
        }
    }

    static class Native
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT tme);

        [StructLayout(LayoutKind.Sequential)]
        public struct TRACKMOUSEEVENT
        {
            public int cbSize;
            public uint dwFlags;
            public IntPtr hwndTrack;
            public uint dwHoverTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NCCALCSIZE_PARAMS
        {
            public RECT rgrc0, rgrc1, rgrc2;
            public IntPtr lppos;
        }

        // DWMWA_WINDOW_CORNER_PREFERENCE: 2 = redondeado, 3 = redondeado pequeño
        public static void RoundCorners(IntPtr hwnd, bool small)
        {
            try { int v = small ? 3 : 2; DwmSetWindowAttribute(hwnd, 33, ref v, sizeof(int)); } catch { }
        }

        public static void UseDarkFrame(IntPtr hwnd, bool dark)
        {
            try { int v = dark ? 1 : 0; DwmSetWindowAttribute(hwnd, 20, ref v, sizeof(int)); } catch { }
        }

        public static void ThemeScrollbars(Control c)
        {
            try { if (c.IsHandleCreated) SetWindowTheme(c.Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }

        public static void SetCueBanner(TextBox tb, string text)
        {
            try { SendMessage(tb.Handle, 0x1501, (IntPtr)1, text ?? ""); } catch { }
        }

        public static int ResizeFrame()
        {
            try { return Math.Max(4, GetSystemMetrics(33) + GetSystemMetrics(92)); } catch { return 8; }
        }
    }

    static class FluentUi
    {
        static ToolTip _tip;

        public static void Tip(Control c, string text)
        {
            if (_tip == null)
            {
                _tip = new ToolTip();
                _tip.InitialDelay = 450;
                _tip.ReshowDelay = 150;
                _tip.AutoPopDelay = 8000;
            }
            _tip.SetToolTip(c, text ?? "");
        }
    }

    class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            UpdateStyles();
        }
    }

    enum ButtonKind { Standard, Accent, Danger, Subtle }

    // Botón Fluent (estándar, acento, peligro o sutil) con glifo opcional
    sealed class FluentButton : Control, IButtonControl
    {
        ButtonKind _kind;
        string _glyph;
        bool _hover;
        bool _pressed;
        bool _iconOnly;
        DialogResult _dialogResult = DialogResult.None;

        public float GlyphSize = 11f;
        public int Radius = 4;

        public FluentButton(string text, ButtonKind kind, string glyph)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            _kind = kind;
            _glyph = glyph;
            Text = text ?? "";
            Font = Theme.Text(10.5f);
            TabStop = true;
            Height = 32;
            AutoWidth();
        }

        public ButtonKind Kind
        {
            get { return _kind; }
            set { _kind = value; Invalidate(); }
        }

        public string Glyph
        {
            get { return _glyph; }
            set { _glyph = value; Invalidate(); }
        }

        public bool IconOnly
        {
            get { return _iconOnly; }
            set
            {
                if (_iconOnly == value) return;
                _iconOnly = value;
                AutoWidth();
                Invalidate();
            }
        }

        public int MeasureWidth(bool iconOnly)
        {
            bool hasGlyph = !string.IsNullOrEmpty(_glyph);
            if ((iconOnly || string.IsNullOrEmpty(Text)) && hasGlyph) return Math.Max(Height, 32);
            int w = Gfx.Measure(Text, Font) + 24;
            if (hasGlyph) w += 24;
            return Math.Max(w, 40);
        }

        public void AutoWidth()
        {
            Width = MeasureWidth(_iconOnly);
        }

        public DialogResult DialogResult
        {
            get { return _dialogResult; }
            set { _dialogResult = value; }
        }

        public void NotifyDefault(bool value) { }

        public void PerformClick()
        {
            if (Enabled && Visible) OnClick(EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (_dialogResult != DialogResult.None)
            {
                Form f = FindForm();
                if (f != null) f.DialogResult = _dialogResult;
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Enter || keyData == Keys.Space) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                e.Handled = true;
                PerformClick();
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color surface = BackColor;
            g.Clear(surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill, stroke = Color.Empty, bottom = Color.Empty, fg;
            bool enabled = Enabled;

            switch (_kind)
            {
                case ButtonKind.Accent:
                    fill = !enabled ? Theme.AccentDisabled : _pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent;
                    fg = !enabled ? Theme.OnAccentDisabled : _pressed ? Gfx.Blend(Theme.OnAccent, fill, 0.25f) : Theme.OnAccent;
                    if (enabled && !_pressed) bottom = Gfx.Blend(fill, Color.Black, Theme.Dark ? 0.15f : 0.3f);
                    break;
                case ButtonKind.Danger:
                    if (!enabled) goto default;
                    fill = _pressed ? Theme.DangerPressed : _hover ? Theme.DangerHover : Theme.Danger;
                    fg = _pressed ? Color.FromArgb(240, 210, 206) : Color.White;
                    if (!_pressed) bottom = Gfx.Blend(fill, Color.Black, 0.3f);
                    break;
                case ButtonKind.Subtle:
                    fill = !enabled ? surface : _pressed ? Theme.SubtlePressed(surface) : _hover ? Theme.SubtleHover(surface) : surface;
                    fg = !enabled ? Theme.TextDisabled : _pressed ? Theme.TextSecondary : (_hover ? Theme.TextPrimary : Theme.TextSecondary);
                    if (!string.IsNullOrEmpty(Text) && !_iconOnly && enabled) fg = _pressed ? Theme.TextSecondary : Theme.TextPrimary;
                    break;
                default:
                    fill = !enabled ? Theme.ControlFillDisabled : _pressed ? Theme.ControlFillPressed : _hover ? Theme.ControlFillHover : Theme.ControlFill;
                    fg = !enabled ? Theme.TextDisabled : _pressed ? Theme.TextSecondary : Theme.TextPrimary;
                    stroke = Theme.ControlStroke;
                    if (enabled && !_pressed) bottom = Theme.ControlStrokeBottom;
                    break;
            }

            using (GraphicsPath p = Gfx.RoundRect(r, Radius))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                if (!stroke.IsEmpty)
                    using (Pen pen = new Pen(stroke, 1f)) g.DrawPath(pen, p);
                if (!bottom.IsEmpty)
                {
                    Region old = g.Clip;
                    g.SetClip(new Rectangle(0, Height - 3, Width, 3));
                    using (Pen pen = new Pen(bottom, 1f)) g.DrawPath(pen, p);
                    g.Clip = old;
                }
            }

            bool hasGlyph = !string.IsNullOrEmpty(_glyph);
            bool showText = !_iconOnly && !string.IsNullOrEmpty(Text);
            if (hasGlyph && !showText)
            {
                Gfx.Glyph(g, _glyph, GlyphSize, new Rectangle(0, 0, Width, Height), fg);
            }
            else
            {
                int tw = Gfx.Measure(Text, Font);
                int total = tw + (hasGlyph ? 24 : 0);
                int x = Math.Max(8, (Width - total) / 2);
                if (hasGlyph)
                {
                    Gfx.Glyph(g, _glyph, GlyphSize, new Rectangle(x, 0, 16, Height), fg);
                    x += 24;
                }
                Gfx.Text(g, Text, Font, new Rectangle(x, 0, Math.Max(1, Width - x - 6), Height - 1), fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }

            if (Focused && ShowFocusCues)
                Gfx.FocusRing(g, ClientRectangle, Radius + 1);
        }
    }

    // Barra de título dibujada a mano; el formulario hace el hit-test (arrastre, Snap Layouts, botones)
    sealed class TitleBar : Control
    {
        public const int HtCaption = 2, HtMin = 8, HtMax = 9, HtClose = 20;
        public const int ButtonWidth = 46;

        public int Hover;
        public int Pressed;
        public bool Maximized;
        public bool Active = true;
        public string Title = "";
        public string Badge = "";
        Image _icon;

        public TitleBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public Image AppIcon
        {
            get { return _icon; }
            set { _icon = value; Invalidate(); }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84)
            {
                m.Result = (IntPtr)(-1); // HTTRANSPARENT: lo resuelve el formulario
                return;
            }
            base.WndProc(ref m);
        }

        public Rectangle ButtonRect(int hit)
        {
            int idx = hit == HtClose ? 1 : hit == HtMax ? 2 : 3;
            return new Rectangle(Width - ButtonWidth * idx, 0, ButtonWidth, Height);
        }

        public int HitTest(Point p)
        {
            if (ButtonRect(HtClose).Contains(p)) return HtClose;
            if (ButtonRect(HtMax).Contains(p)) return HtMax;
            if (ButtonRect(HtMin).Contains(p)) return HtMin;
            return HtCaption;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            Color text = Active ? Theme.TextPrimary : Theme.TextTertiary;
            if (_icon != null)
                g.DrawImage(_icon, new Rectangle(16, (Height - 16) / 2, 16, 16));

            Font tf = Theme.Text(9f);
            int tx = _icon != null ? 44 : 16;
            int tw = Gfx.Measure(Title, tf);
            Gfx.TextLeft(g, Title, tf, new Rectangle(tx, 0, tw + 4, Height), text);

            if (!string.IsNullOrEmpty(Badge))
            {
                Font bf = Theme.Strong(7.5f);
                int bw = Gfx.Measure(Badge, bf) + 12;
                Rectangle br = new Rectangle(tx + tw + 10, (Height - 18) / 2, bw, 18);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Gfx.FillRound(g, br, 4, Theme.SubtleHover(Theme.Bg));
                g.SmoothingMode = SmoothingMode.None;
                Gfx.Text(g, Badge, bf, br, Active ? Theme.TextSecondary : Theme.TextTertiary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            PaintCaption(g, HtMin, Glyphs.Minimize, text);
            PaintCaption(g, HtMax, Maximized ? Glyphs.Restore : Glyphs.Maximize, text);
            PaintCaption(g, HtClose, Glyphs.Close, text);
        }

        void PaintCaption(Graphics g, int hit, string glyph, Color fg)
        {
            Rectangle r = ButtonRect(hit);
            bool hover = Hover == hit;
            bool pressed = Pressed == hit && hover;
            if (hit == HtClose && (hover || pressed))
            {
                using (SolidBrush b = new SolidBrush(pressed ? Theme.ClosePressed : Theme.CloseHover)) g.FillRectangle(b, r);
                fg = Color.White;
            }
            else if (pressed)
            {
                using (SolidBrush b = new SolidBrush(Theme.SubtlePressed(Theme.Bg))) g.FillRectangle(b, r);
                fg = Theme.TextSecondary;
            }
            else if (hover)
            {
                using (SolidBrush b = new SolidBrush(Theme.SubtleHover(Theme.Bg))) g.FillRectangle(b, r);
            }
            Gfx.Glyph(g, glyph, 7.5f, r, fg);
        }
    }

    sealed class NavItem
    {
        public string Key;
        public string Text;
        public string Glyph;
        public bool Enabled = true;
        public bool Footer;
        public bool Badge;
        public Color BadgeColor;
        public string Tooltip;
        public Rectangle Bounds;
    }

    // Panel de navegación lateral estilo NavigationView (expandido / compacto)
    sealed class NavPane : Control
    {
        public const int ExpandedWidth = 216;
        public const int CompactWidth = 52;
        const int ItemHeight = 38;

        readonly List<NavItem> _items = new List<NavItem>();
        NavItem _hover;
        NavItem _down;
        bool _toggleHover;
        bool _toggleDown;
        bool _expanded = true;
        string _selected = "";
        string _tipShown;
        readonly ToolTip _tip = new ToolTip();

        public event Action<NavItem> ItemInvoked;

        public NavPane()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = ExpandedWidth;
            _tip.InitialDelay = 400;
        }

        public NavItem Add(string key, string text, string glyph, bool footer)
        {
            NavItem it = new NavItem();
            it.Key = key;
            it.Text = text;
            it.Glyph = glyph;
            it.Footer = footer;
            _items.Add(it);
            LayoutItems();
            return it;
        }

        public string Selected
        {
            get { return _selected; }
            set { _selected = value ?? ""; Invalidate(); }
        }

        public bool Expanded
        {
            get { return _expanded; }
            set
            {
                if (_expanded == value) return;
                _expanded = value;
                Width = value ? ExpandedWidth : CompactWidth;
                LayoutItems();
                Invalidate();
            }
        }

        Rectangle ToggleRect { get { return new Rectangle(6, 4, 40, 36); } }

        void LayoutItems()
        {
            int y = 48;
            int footerCount = 0;
            for (int i = 0; i < _items.Count; i++) if (_items[i].Footer) footerCount++;
            int fy = Height - 8 - footerCount * (ItemHeight + 4);
            for (int i = 0; i < _items.Count; i++)
            {
                NavItem it = _items[i];
                if (it.Footer)
                {
                    it.Bounds = new Rectangle(6, fy, Width - 12, ItemHeight);
                    fy += ItemHeight + 4;
                }
                else
                {
                    it.Bounds = new Rectangle(6, y, Width - 12, ItemHeight);
                    y += ItemHeight + 4;
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutItems();
        }

        NavItem HitItem(Point p)
        {
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].Bounds.Contains(p)) return _items[i];
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            NavItem h = HitItem(e.Location);
            bool th = ToggleRect.Contains(e.Location);
            if (h != _hover || th != _toggleHover)
            {
                _hover = h;
                _toggleHover = th;
                Invalidate();
            }
            string tip = th ? (_expanded ? "Contraer menú" : "Expandir menú") : (h != null ? (h.Tooltip ?? (_expanded ? null : h.Text)) : null);
            if (tip != _tipShown)
            {
                _tipShown = tip;
                _tip.SetToolTip(this, tip ?? "");
            }
            Cursor = Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = null;
            _toggleHover = false;
            _down = null;
            _toggleDown = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _toggleDown = ToggleRect.Contains(e.Location);
            _down = _toggleDown ? null : HitItem(e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (_toggleDown && ToggleRect.Contains(e.Location))
            {
                Expanded = !_expanded;
            }
            else
            {
                NavItem it = HitItem(e.Location);
                if (it != null && it == _down && it.Enabled && ItemInvoked != null)
                    ItemInvoked(it);
            }
            _down = null;
            _toggleDown = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle tr = ToggleRect;
            if (_toggleHover)
                Gfx.FillRound(g, tr, 4, _toggleDown ? Theme.SubtlePressed(Theme.Bg) : Theme.SubtleHover(Theme.Bg));
            Gfx.Glyph(g, Glyphs.Nav, 11.5f, tr, Theme.TextPrimary);

            for (int i = 0; i < _items.Count; i++)
            {
                NavItem it = _items[i];
                Rectangle r = it.Bounds;
                bool selected = string.Equals(it.Key, _selected, StringComparison.OrdinalIgnoreCase);
                bool hover = it == _hover && it.Enabled;
                bool down = it == _down && hover;

                if (selected || hover)
                {
                    Color fill = selected
                        ? (down ? Theme.SubtlePressed(Theme.Bg) : (hover ? Gfx.Blend(Theme.SubtleHover(Theme.Bg), Theme.SubtlePressed(Theme.Bg), 0.5f) : Theme.SubtleHover(Theme.Bg)))
                        : (down ? Theme.SubtlePressed(Theme.Bg) : Theme.SubtleHover(Theme.Bg));
                    Gfx.FillRound(g, r, 4, fill);
                }
                if (selected)
                    Gfx.FillRound(g, new Rectangle(r.X, r.Y + (r.Height - 16) / 2, 3, 16), 1, Theme.Accent);

                Color fg = it.Enabled ? Theme.TextPrimary : Theme.TextDisabled;
                Rectangle gr = new Rectangle(r.X, r.Y, 40, r.Height);
                Gfx.Glyph(g, it.Glyph, 12f, gr, fg);
                if (it.Badge)
                {
                    Rectangle dot = new Rectangle(gr.X + 26, r.Y + 8, 8, 8);
                    using (SolidBrush b = new SolidBrush(it.BadgeColor)) g.FillEllipse(b, dot);
                    using (Pen p = new Pen(Theme.Bg, 1.5f)) g.DrawEllipse(p, dot);
                }
                if (_expanded)
                    Gfx.TextLeft(g, it.Text, Theme.Text(10.5f), new Rectangle(r.X + 44, r.Y, r.Width - 48, r.Height - 1), fg);
            }
        }
    }

    // Texto de estado con indicador de color (el ForeColor marca el estado)
    sealed class StatusLabel : Control
    {
        public StatusLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int x = Padding.Left;
            using (SolidBrush b = new SolidBrush(ForeColor))
                g.FillEllipse(b, x, (Height - 8) / 2, 8, 8);
            Gfx.TextLeft(g, Text, Theme.Text(9f), new Rectangle(x + 16, 0, Math.Max(1, Width - x - 16 - Padding.Right), Height), Theme.TextSecondary);
        }
    }

    enum Severity { Info, Success, Warning, Error }

    // InfoBar de WinUI: icono, título, mensaje y acciones
    sealed class InfoBar : DoubleBufferedPanel
    {
        readonly Severity _severity;
        readonly List<FluentButton> _buttons = new List<FluentButton>();
        string _title = "";
        string _message = "";

        public InfoBar(Severity severity)
        {
            _severity = severity;
            Height = 52;
            BackColor = SeverityBg();
        }

        public string Title { get { return _title; } set { _title = value ?? ""; Invalidate(); } }
        public string Message { get { return _message; } set { _message = value ?? ""; Invalidate(); } }

        Color SeverityBg()
        {
            switch (_severity)
            {
                case Severity.Success: return Theme.SuccessBg;
                case Severity.Warning: return Theme.CautionBg;
                case Severity.Error: return Theme.CriticalBg;
                default: return Theme.InfoBg;
            }
        }

        Color SeverityColor()
        {
            switch (_severity)
            {
                case Severity.Success: return Theme.Success;
                case Severity.Warning: return Theme.Caution;
                case Severity.Error: return Theme.Critical;
                default: return Theme.Accent;
            }
        }

        public FluentButton AddButton(string text, ButtonKind kind, EventHandler click)
        {
            FluentButton b = new FluentButton(text, kind, null);
            b.Click += click;
            _buttons.Add(b);
            Controls.Add(b);
            LayoutButtons();
            return b;
        }

        void LayoutButtons()
        {
            int x = Width - 12;
            for (int i = _buttons.Count - 1; i >= 0; i--)
            {
                FluentButton b = _buttons[i];
                x -= b.Width;
                b.Location = new Point(x, (Height - b.Height) / 2);
                x -= 8;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutButtons();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color surface = Parent != null ? Parent.BackColor : Theme.Layer;
            g.Clear(surface);
            Gfx.Card(g, new Rectangle(0, 0, Width - 1, Height - 1), 6, BackColor, Theme.CardStroke);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle ic = new Rectangle(16, (Height - 16) / 2, 16, 16);
            Color sev = SeverityColor();
            using (SolidBrush b = new SolidBrush(sev)) g.FillEllipse(b, ic);
            Color onSev = Theme.Dark ? Color.Black : Color.White;
            string glyph = _severity == Severity.Success ? Glyphs.Check : _severity == Severity.Error ? Glyphs.Cancel : null;
            if (glyph != null)
                Gfx.Glyph(g, glyph, 6.5f, ic, onSev);
            else
                Gfx.Text(g, _severity == Severity.Warning ? "!" : "i", Theme.Strong(8f), new Rectangle(ic.X, ic.Y - 1, ic.Width, ic.Height), onSev,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            int right = _buttons.Count > 0 ? _buttons[0].Left - 16 : Width - 16;
            int x = 44;
            Font tf = Theme.Strong(10.5f);
            int tw = Math.Min(Gfx.Measure(_title, tf) + 2, Math.Max(0, right - x));
            Gfx.TextLeft(g, _title, tf, new Rectangle(x, 0, tw, Height - 1), Theme.TextPrimary);
            x += tw + 12;
            if (right - x > 20)
                Gfx.TextLeft(g, _message, Theme.Text(10.5f), new Rectangle(x, 0, right - x, Height - 1), Theme.TextPrimary);
        }
    }

    // Tarjeta de categoría del inicio
    sealed class SectionCard : DoubleBufferedPanel
    {
        public const int HeaderHeight = 68;
        readonly string _title;
        readonly string _glyph;
        readonly Color _accent;
        string _subtitle;
        public int TextRight;

        public SectionCard(string title, string subtitle, string glyph, Color accent)
        {
            _title = title;
            _subtitle = subtitle;
            _glyph = glyph;
            _accent = accent;
            BackColor = Theme.Card;
        }

        public string Subtitle
        {
            get { return _subtitle; }
            set { _subtitle = value ?? ""; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Layer);
            Gfx.Card(g, new Rectangle(0, 0, Width - 1, Height - 1), 8, Theme.Card, Theme.CardStroke);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle plate = new Rectangle(18, 18, 34, 34);
            Gfx.FillRound(g, plate, 6, Gfx.Blend(Theme.Card, _accent, Theme.Dark ? 0.28f : 0.13f));
            Gfx.Glyph(g, _glyph, 12.5f, plate, Theme.Dark ? Gfx.Blend(_accent, Color.White, 0.45f) : _accent);

            int right = TextRight > 0 ? TextRight : Width - 16;
            Gfx.TextLeft(g, _title, Theme.Strong(11.5f), new Rectangle(64, 14, Math.Max(1, right - 64), 24), Theme.TextPrimary);
            Gfx.TextLeft(g, _subtitle, Theme.Text(9f), new Rectangle(64, 37, Math.Max(1, right - 64), 18), Theme.TextSecondary);
        }
    }

    sealed class TileInfo
    {
        public string Title;
        public string Desc;
        public string Glyph;
        public Color Accent;
        public Action Action;
        public string CtaGlyph;

        public TileInfo(string title, string desc, string glyph, Color accent, Action action, string ctaGlyph)
        {
            Title = title;
            Desc = desc;
            Glyph = glyph;
            Accent = accent;
            Action = action;
            CtaGlyph = string.IsNullOrEmpty(ctaGlyph) ? Glyphs.Play : ctaGlyph;
        }
    }

    // Botón de modo: icono, título, descripción y acción (▶) al pasar el ratón
    sealed class TileFace : Control
    {
        readonly TileInfo _info;
        readonly Timer _anim;
        float _t;
        float _from;
        float _to;
        int _elapsed;
        bool _pressed;
        const int AnimMs = 150;

        public TileFace(TileInfo info)
        {
            _info = info;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            TabStop = true;
            Cursor = Cursors.Hand;
            FluentUi.Tip(this, info.Title + " · " + info.Desc);

            _anim = new Timer();
            _anim.Interval = 15;
            _anim.Tick += delegate
            {
                _elapsed += _anim.Interval;
                float p = Math.Min(1f, _elapsed / (float)AnimMs);
                float ease = 1f - (1f - p) * (1f - p) * (1f - p);
                _t = _from + (_to - _from) * ease;
                if (p >= 1f) _anim.Stop();
                Invalidate();
            };
            Disposed += delegate { try { _anim.Stop(); _anim.Dispose(); } catch { } };
        }

        void Animate(float target)
        {
            _from = _t;
            _to = target;
            _elapsed = 0;
            if (!_anim.Enabled) _anim.Start();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Animate(1f); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _pressed = false; Animate(0f); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Enter || keyData == Keys.Space) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                e.Handled = true;
                OnClick(EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float t = _t;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = _pressed ? Theme.TileFillPressed : Gfx.Blend(Theme.TileFill, Theme.TileFillHover, t);
            Color stroke = Gfx.Blend(Theme.TileStroke, Gfx.Blend(Theme.TileStroke, _info.Accent, Theme.Dark ? 0.55f : 0.4f), t);
            Gfx.Card(g, r, 6, fill, stroke);

            Rectangle plate = new Rectangle(12, (Height - 40) / 2, 40, 40);
            using (GraphicsPath pp = Gfx.RoundRect(plate, 6))
            using (LinearGradientBrush lg = new LinearGradientBrush(plate,
                Gfx.Blend(_info.Accent, Color.White, 0.16f), Gfx.Blend(_info.Accent, Color.Black, 0.10f), 90f))
                g.FillPath(lg, pp);
            Gfx.Glyph(g, _info.Glyph, 13f, plate, Color.White);

            int tx = plate.Right + 12;
            int tw = Math.Max(1, Width - tx - 46);
            int mid = Height / 2;
            Gfx.TextLeft(g, _info.Title, Theme.Strong(10.5f), new Rectangle(tx, mid - 20, tw, 20), Theme.TextPrimary);
            Gfx.TextLeft(g, _info.Desc, Theme.Text(9f), new Rectangle(tx, mid + 1, tw, 18), Theme.TextSecondary);

            Rectangle cta = new Rectangle(Width - 12 - 28, (Height - 28) / 2, 28, 28);
            if (t < 0.99f)
                Gfx.Glyph(g, Glyphs.ChevronRight, 8.5f, cta, Gfx.Blend(fill, Theme.TextTertiary, 1f - t));
            if (t > 0.01f)
            {
                Color circle = Gfx.Blend(fill, _info.Accent, t);
                using (SolidBrush b = new SolidBrush(circle)) g.FillEllipse(b, cta);
                Rectangle gr = _info.CtaGlyph == Glyphs.Play ? new Rectangle(cta.X + 1, cta.Y, cta.Width, cta.Height) : cta;
                Gfx.Glyph(g, _info.CtaGlyph, 9f, gr, Gfx.Blend(circle, Color.White, t));
            }

            if (Focused && ShowFocusCues)
                Gfx.FocusRing(g, ClientRectangle, 7);
        }
    }

    // Tarjeta de consumo (CPU/RAM) con mini gráfica de CPU
    sealed class StatsCard : Control
    {
        readonly Color _accent;
        readonly List<double> _history = new List<double>();
        string _role;
        string _value = "—";
        string _detail = "";
        string _procs = "";
        bool _idle = true;
        const int MaxSamples = 48;

        public StatsCard(string role, Color accent)
        {
            _role = role;
            _accent = accent;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public string Role
        {
            get { return _role; }
            set { if (_role != value) { _role = value; Invalidate(); } }
        }

        public void Reset()
        {
            _history.Clear();
            _value = "—";
            _detail = "";
            _procs = "";
            _idle = true;
            Invalidate();
        }

        public void SetIdle()
        {
            Push(0);
            _value = "Inactivo";
            _detail = "";
            _procs = "";
            _idle = true;
            Invalidate();
        }

        public void SetValues(double cpu, long memory, int count)
        {
            Push(cpu);
            _value = cpu.ToString("0.0") + " %";
            _detail = Gfx.Bytes(memory);
            _procs = count + (count == 1 ? " proceso" : " procesos");
            _idle = false;
            Invalidate();
        }

        void Push(double v)
        {
            _history.Add(v);
            while (_history.Count > MaxSamples) _history.RemoveAt(0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Layer);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Gfx.Card(g, r, 8, Theme.Card, Theme.CardStroke);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Font vf = Theme.Strong(12f);
            Font df = Theme.Text(10f);
            int vw = Gfx.Measure(_value, _idle ? Theme.Text(10.5f) : vf);
            int textEnd = 14 + vw + (_idle ? 0 : 10 + Gfx.Measure(_detail, df));
            int chartLeft = Math.Max(Width * 45 / 100, textEnd + 14);
            if (_history.Count >= 2 && Width - 14 - chartLeft >= 40)
            {
                Rectangle area = new Rectangle(chartLeft, 30, Width - 14 - chartLeft, Height - 40);
                double max = 10;
                for (int i = 0; i < _history.Count; i++) if (_history[i] > max) max = _history[i];
                max *= 1.15;
                PointF[] pts = new PointF[_history.Count];
                float step = area.Width / (float)(MaxSamples - 1);
                float x0 = area.Right - step * (_history.Count - 1);
                for (int i = 0; i < _history.Count; i++)
                    pts[i] = new PointF(x0 + step * i, area.Bottom - (float)(_history[i] / max) * area.Height);
                using (GraphicsPath fillPath = new GraphicsPath())
                {
                    fillPath.AddLines(pts);
                    fillPath.AddLine(pts[pts.Length - 1], new PointF(pts[pts.Length - 1].X, area.Bottom));
                    fillPath.AddLine(new PointF(pts[pts.Length - 1].X, area.Bottom), new PointF(pts[0].X, area.Bottom));
                    fillPath.CloseFigure();
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(Theme.Dark ? 46 : 30, _accent))) g.FillPath(b, fillPath);
                }
                using (Pen p = new Pen(Color.FromArgb(200, _accent), 1.5f))
                {
                    p.LineJoin = LineJoin.Round;
                    g.DrawLines(p, pts);
                }
            }

            using (SolidBrush b = new SolidBrush(_accent)) g.FillEllipse(b, 14, 15, 8, 8);
            Gfx.TextLeft(g, _role, Theme.Text(9f), new Rectangle(28, 9, Width / 2, 20), Theme.TextSecondary);

            if (_idle)
            {
                Gfx.TextLeft(g, _value, Theme.Text(10.5f), new Rectangle(14, 32, Width - 28, 24), Theme.TextTertiary);
                return;
            }
            Gfx.TextLeft(g, _value, vf, new Rectangle(14, 31, vw + 2, 26), Theme.TextPrimary);
            Gfx.TextLeft(g, _detail, df, new Rectangle(14 + vw + 10, 33, Math.Max(1, Width - vw - 40), 24), Theme.TextSecondary);
            int pw = Gfx.Measure(_procs, Theme.Text(8f));
            Gfx.TextLeft(g, _procs, Theme.Text(8f), new Rectangle(Width - 14 - pw, 9, pw + 2, 20), Theme.TextTertiary);
        }
    }

    // Barra de desplazamiento fina estilo Windows 11 (se ensancha al pasar el ratón)
    sealed class FluentScrollBar : Control
    {
        int _minimum;
        int _maximum = 100;
        int _largeChange = 10;
        int _value;
        bool _dragging;
        bool _hover;
        int _dragOffset;

        public event EventHandler Scroll;

        public FluentScrollBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = 12;
        }

        public int Minimum { get { return _minimum; } set { _minimum = value; Invalidate(); } }
        public int Maximum { get { return _maximum; } set { _maximum = Math.Max(_minimum, value); ClampValue(); Invalidate(); } }
        public int LargeChange { get { return _largeChange; } set { _largeChange = Math.Max(1, value); Invalidate(); } }

        public int Value
        {
            get { return _value; }
            set
            {
                int maxVal = Math.Max(_minimum, _maximum - Math.Max(0, _largeChange - 1));
                int v = Math.Max(_minimum, Math.Min(maxVal, value));
                if (v == _value) return;
                _value = v;
                Invalidate();
                if (Scroll != null) Scroll(this, EventArgs.Empty);
            }
        }

        void ClampValue()
        {
            int maxVal = Math.Max(_minimum, _maximum - Math.Max(0, _largeChange - 1));
            if (_value > maxVal) _value = maxVal;
            if (_value < _minimum) _value = _minimum;
        }

        void Metrics(out int y, out int thumbH, out int travel, out int maxVal)
        {
            int track = Math.Max(1, Height - 8);
            int range = Math.Max(1, _maximum - _minimum);
            thumbH = Math.Min(track, Math.Max(32, (int)((long)_largeChange * track / range)));
            maxVal = Math.Max(1, range - Math.Max(0, _largeChange - 1));
            travel = Math.Max(1, track - thumbH);
            y = 4 + (int)((long)(_value - _minimum) * travel / maxVal);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (!Enabled) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int y, thumbH, travel, maxVal;
            Metrics(out y, out thumbH, out travel, out maxVal);
            bool wide = _hover || _dragging;
            if (wide)
            {
                Gfx.FillRound(g, new Rectangle(2, 2, Width - 4, Height - 4), 5, Theme.SubtleHover(BackColor));
                Gfx.FillRound(g, new Rectangle(3, y, Width - 6, thumbH), 3, _dragging ? Theme.TextSecondary : Theme.TextTertiary);
            }
            else
            {
                Gfx.FillRound(g, new Rectangle(Width - 5, y, 2, thumbH), 1, Theme.TextTertiary);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int y, thumbH, travel, maxVal;
            Metrics(out y, out thumbH, out travel, out maxVal);
            if (e.Y >= y && e.Y <= y + thumbH)
            {
                _dragging = true;
                _dragOffset = e.Y - y;
                Invalidate();
            }
            else
            {
                int page = Math.Max(1, _largeChange - 8);
                Value = e.Y < y ? _value - page : _value + page;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            int y, thumbH, travel, maxVal;
            Metrics(out y, out thumbH, out travel, out maxVal);
            int ny = Math.Max(0, Math.Min(travel, e.Y - _dragOffset - 4));
            Value = _minimum + (int)((long)ny * maxVal / travel);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Invalidate();
        }
    }

    // TextBox con aspecto Fluent: borde redondeado, línea inferior de acento al enfocar y botón para ver la contraseña
    sealed class FluentTextBox : DoubleBufferedPanel
    {
        readonly TextBox _tb;
        readonly FluentButton _reveal;

        public FluentTextBox(string value, bool password, string cue)
        {
            Height = 32;
            BackColor = Theme.InputFill;
            Cursor = Cursors.IBeam;
            _tb = new TextBox();
            _tb.BorderStyle = BorderStyle.None;
            _tb.Font = Theme.Text(10.5f);
            _tb.BackColor = Theme.InputFill;
            _tb.ForeColor = Theme.TextPrimary;
            _tb.Text = value ?? "";
            _tb.UseSystemPasswordChar = password;
            _tb.GotFocus += delegate { _tb.BackColor = Theme.InputFillFocus; BackColor = Theme.InputFillFocus; Invalidate(); };
            _tb.LostFocus += delegate { _tb.BackColor = Theme.InputFill; BackColor = Theme.InputFill; Invalidate(); };
            if (!string.IsNullOrEmpty(cue)) _tb.HandleCreated += delegate { Native.SetCueBanner(_tb, cue); };
            Controls.Add(_tb);

            if (password)
            {
                _reveal = new FluentButton("", ButtonKind.Subtle, Glyphs.View);
                _reveal.Size = new Size(28, 24);
                _reveal.GlyphSize = 9.5f;
                _reveal.TabStop = false;
                _reveal.Cursor = Cursors.Default;
                FluentUi.Tip(_reveal, "Mostrar u ocultar la contraseña");
                _reveal.Click += delegate
                {
                    _tb.UseSystemPasswordChar = !_tb.UseSystemPasswordChar;
                    _tb.Focus();
                    _tb.SelectionStart = _tb.TextLength;
                };
                Controls.Add(_reveal);
            }
            MouseDown += delegate { _tb.Focus(); };
        }

        public TextBox Inner { get { return _tb; } }

        public override string Text
        {
            get { return _tb != null ? _tb.Text : base.Text; }
            set { if (_tb != null) _tb.Text = value; else base.Text = value; }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_tb == null) return;
            int right = _reveal != null ? 34 : 10;
            _tb.Location = new Point(11, (Height - _tb.Height) / 2);
            _tb.Width = Math.Max(20, Width - 11 - right);
            if (_reveal != null) _reveal.Location = new Point(Width - _reveal.Width - 4, (Height - _reveal.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool focus = _tb.Focused;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Gfx.RoundRect(r, 4))
            {
                using (SolidBrush b = new SolidBrush(focus ? Theme.InputFillFocus : Theme.InputFill)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.ControlStroke, 1f)) g.DrawPath(pen, p);
                Region old = g.Clip;
                g.SetClip(p);
                int lh = focus ? 2 : 1;
                using (SolidBrush b = new SolidBrush(focus ? Theme.Accent : Theme.InputBottom))
                    g.FillRectangle(b, 0, Height - lh - (focus ? 0 : 1), Width, lh + 1);
                g.Clip = old;
            }
        }
    }

    // Fila de ajustes estilo SettingsCard de Windows 11
    sealed class SettingsCard : DoubleBufferedPanel
    {
        readonly string _glyph;
        readonly string _title;
        string _desc;
        Control _action;

        public SettingsCard(string glyph, string title, string desc)
        {
            _glyph = glyph;
            _title = title;
            _desc = desc;
            Height = 68;
            BackColor = Theme.Card;
        }

        public string Description
        {
            get { return _desc; }
            set { _desc = value ?? ""; Invalidate(); }
        }

        public FluentButton SetAction(string text, string glyph, EventHandler click)
        {
            FluentButton b = new FluentButton(text, ButtonKind.Standard, glyph);
            b.Click += click;
            _action = b;
            Controls.Add(b);
            PerformLayout();
            return b;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_action != null) _action.Location = new Point(Width - _action.Width - 16, (Height - _action.Height) / 2);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Layer);
            Gfx.Card(g, new Rectangle(0, 0, Width - 1, Height - 1), 6, Theme.Card, Theme.CardStroke);
            Gfx.Glyph(g, _glyph, 12.5f, new Rectangle(14, 0, 32, Height), Theme.TextPrimary);
            int right = (_action != null ? _action.Left : Width) - 16;
            Gfx.TextLeft(g, _title, Theme.Text(10.5f), new Rectangle(60, 13, Math.Max(1, right - 60), 22), Theme.TextPrimary);
            Gfx.TextLeft(g, _desc, Theme.Text(9f), new Rectangle(60, 35, Math.Max(1, right - 60), 18), Theme.TextSecondary);
        }
    }

    static class FluentMenu
    {
        public static ContextMenuStrip Create()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new FluentMenuRenderer();
            m.Font = Theme.Text(10f);
            m.ShowImageMargin = false;
            m.ShowCheckMargin = true;
            m.BackColor = Theme.MenuBg;
            m.ForeColor = Theme.TextPrimary;
            m.Padding = new Padding(2, 4, 2, 4);
            m.HandleCreated += delegate
            {
                Native.RoundCorners(m.Handle, false);
                Native.UseDarkFrame(m.Handle, Theme.Dark);
            };
            m.ItemAdded += delegate(object s, ToolStripItemEventArgs e)
            {
                if (!(e.Item is ToolStripSeparator)) e.Item.Padding = new Padding(4, 6, 12, 6);
            };
            return m;
        }
    }

    sealed class FluentMenuRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Theme.MenuBg);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (Theme.IsWin11) return;
            using (Pen p = new Pen(Theme.CardStroke))
                e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Gfx.FillRound(e.Graphics, new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2), 4, Theme.SubtleHover(Theme.MenuBg));
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(Theme.Divider))
                e.Graphics.DrawLine(p, 0, y, e.Item.Width, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.TextPrimary : Theme.TextDisabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Gfx.Glyph(e.Graphics, Glyphs.Check, 9f, e.ImageRectangle, Theme.Accent);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.TextSecondary;
            base.OnRenderArrow(e);
        }
    }
}
