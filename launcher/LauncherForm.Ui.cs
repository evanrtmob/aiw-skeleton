using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AppInWhats
{
    // Interfaz del launcher: ventana, navegación y páginas (Inicio, Sesión, Ajustes)
    sealed partial class LauncherForm
    {
        static Color Accent { get { return Theme.Accent; } }
        static Color TextMuted { get { return Theme.LogMuted; } }
        static Color TextMain { get { return Theme.TextPrimary; } }
        static Color Danger { get { return Theme.Critical; } }
        static Color TermFg { get { return Theme.LogText; } }
        static Color LogError { get { return Theme.LogError; } }
        static Color LogWarn { get { return Theme.LogWarn; } }
        static Color LogStderr { get { return Theme.LogStderr; } }
        static Color LogInfo { get { return Theme.LogInfo; } }

        static readonly Color BackendColor = Color.FromArgb(135, 100, 184);
        static readonly Color OtherColor = Color.FromArgb(122, 117, 116);
        static readonly Color TotalColor = Color.FromArgb(0, 130, 114);
        static readonly string[] DefaultSectionOrder = new string[] { "Native", "Docker", "Servidor", "Otros" };

        const int TitleBarHeight = 40;

        TitleBar _titleBar;
        NavPane _nav;
        NavItem _navHome;
        NavItem _navSession;
        NavItem _navSettings;
        Panel _contentHost;
        LayerCorner _corner;
        StatusLabel _status;
        Label _statusPath;
        bool _ncTracking;
        bool? _lastWide;

        Panel _homePanel;
        Panel _homeHero;
        Panel _homeScrollHost;
        Panel _homeViewport;
        Panel _tileGrid;
        FluentScrollBar _homeScrollBar;
        readonly List<HomeSection> _homeSections = new List<HomeSection>();
        readonly string _homeLayoutPath;
        HomeSection _dragSection;
        bool _draggingSection;
        int _dragOffsetX;
        int _dragOffsetY;
        InfoBar _resumeBanner;
        InfoBar _runBanner;

        Panel _sessionPanel;
        Panel _toolbar;
        Panel _serverBar;
        Panel _statsBar;
        SplitContainer _split;
        TerminalView _termBack;
        TerminalView _termFront;
        string _sessionTitleText = "Sesión";
        FluentButton _btnStop;
        FluentButton _btnStart;
        FluentButton _btnOpen;
        FluentButton _btnBuild;
        FluentButton _btnSyncBack;
        FluentButton _btnSyncFront;
        FluentButton _btnSyncBoth;
        StatsCard _statsBack;
        StatsCard _statsFront;
        StatsCard _statsOther;
        StatsCard _statsTotal;

        Panel _settingsPanel;
        readonly List<Control> _settingsRows = new List<Control>();
        SettingsCard _setServer;
        SettingsCard _setPlesk;

        public LauncherForm()
        {
            _root = ResolveRoot();
            _sessionPath = Path.Combine(_root, ".aiw-launcher-session");
            _homeLayoutPath = Path.Combine(_root, "launcher", "home-sections.order");

            Text = "AppInWhats DevBuild";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1180, 740);
            MinimumSize = new Size(800, 540);
            BackColor = Theme.Bg;
            ForeColor = Theme.TextPrimary;
            Font = Theme.Text(10f);
            DoubleBuffered = true;

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            InitKillOnCloseJob();
            BuildChrome();
            BuildHome();
            BuildSession();
            BuildSettings();
            ShowHome();

            _watchTimer = new System.Windows.Forms.Timer();
            _watchTimer.Interval = 1000;
            _watchTimer.Tick += delegate
            {
                CheckAllExited();
                UpdateResourceStats();
            };
            _watchTimer.Start();

            Shown += delegate { CheckPreviousSession(); };
        }

        // --- Ventana: marco nativo sin barra de título (sombra, esquinas, Snap Layouts y animaciones de Windows) ---

        const int WM_NCCALCSIZE = 0x83;
        const int WM_NCHITTEST = 0x84;
        const int WM_NCMOUSEMOVE = 0xA0;
        const int WM_NCLBUTTONDOWN = 0xA1;
        const int WM_NCLBUTTONUP = 0xA2;
        const int WM_NCLBUTTONDBLCLK = 0xA3;
        const int WM_NCMOUSELEAVE = 0x2A2;
        const int HTCLIENT = 1;
        const int HTTOP = 12;
        const int HTTOPLEFT = 13;
        const int HTTOPRIGHT = 14;

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_NCCALCSIZE:
                    if (m.WParam != IntPtr.Zero)
                    {
                        HandleNcCalcSize(ref m);
                        return;
                    }
                    break;

                case WM_NCHITTEST:
                    base.WndProc(ref m);
                    if (m.Result.ToInt64() == HTCLIENT)
                        m.Result = (IntPtr)HitTestClient(PointFromLParam(m.LParam));
                    return;

                case WM_NCMOUSEMOVE:
                    SetCaptionHover(m.WParam.ToInt32());
                    if (!_ncTracking)
                    {
                        Native.TRACKMOUSEEVENT tme = new Native.TRACKMOUSEEVENT();
                        tme.cbSize = Marshal.SizeOf(typeof(Native.TRACKMOUSEEVENT));
                        tme.dwFlags = 0x2 | 0x10; // TME_LEAVE | TME_NONCLIENT
                        tme.hwndTrack = Handle;
                        _ncTracking = Native.TrackMouseEvent(ref tme);
                    }
                    break;

                case WM_NCMOUSELEAVE:
                    _ncTracking = false;
                    if (_titleBar != null) _titleBar.Pressed = 0;
                    SetCaptionHover(0);
                    break;

                case WM_NCLBUTTONDOWN:
                case WM_NCLBUTTONDBLCLK:
                    if (IsCaptionButton(m.WParam.ToInt32()))
                    {
                        _titleBar.Pressed = m.WParam.ToInt32();
                        _titleBar.Invalidate();
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;

                case WM_NCLBUTTONUP:
                    if (IsCaptionButton(m.WParam.ToInt32()))
                    {
                        int hit = m.WParam.ToInt32();
                        bool fire = _titleBar.Pressed == hit;
                        _titleBar.Pressed = 0;
                        _titleBar.Invalidate();
                        m.Result = IntPtr.Zero;
                        if (fire) PerformCaption(hit);
                        return;
                    }
                    break;
            }
            base.WndProc(ref m);
        }

        void HandleNcCalcSize(ref Message m)
        {
            Native.NCCALCSIZE_PARAMS nc = (Native.NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(Native.NCCALCSIZE_PARAMS));
            int top = nc.rgrc0.Top;
            base.WndProc(ref m);
            nc = (Native.NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(Native.NCCALCSIZE_PARAMS));
            // Se conservan los bordes laterales/inferior nativos; arriba se quita el título
            nc.rgrc0.Top = top + (Native.IsZoomed(Handle) ? Native.ResizeFrame() : 0);
            Marshal.StructureToPtr(nc, m.LParam, false);
            m.Result = IntPtr.Zero;
        }

        int HitTestClient(Point screen)
        {
            if (_titleBar == null) return HTCLIENT;
            Point p = PointToClient(screen);
            if (p.Y < 0 || p.Y >= _titleBar.Bottom) return HTCLIENT;
            if (!Native.IsZoomed(Handle) && p.Y < Math.Max(4, Native.ResizeFrame() - 3))
            {
                if (p.X < 16) return HTTOPLEFT;
                if (p.X >= ClientSize.Width - 16) return HTTOPRIGHT;
                return HTTOP;
            }
            return _titleBar.HitTest(new Point(p.X - _titleBar.Left, p.Y - _titleBar.Top));
        }

        static Point PointFromLParam(IntPtr lp)
        {
            long v = lp.ToInt64();
            return new Point(unchecked((short)(v & 0xFFFF)), unchecked((short)((v >> 16) & 0xFFFF)));
        }

        static bool IsCaptionButton(int hit)
        {
            return hit == TitleBar.HtMin || hit == TitleBar.HtMax || hit == TitleBar.HtClose;
        }

        void SetCaptionHover(int hit)
        {
            if (_titleBar == null) return;
            int h = IsCaptionButton(hit) ? hit : 0;
            if (_titleBar.Hover == h) return;
            _titleBar.Hover = h;
            _titleBar.Invalidate();
        }

        void PerformCaption(int hit)
        {
            if (hit == TitleBar.HtMin) WindowState = FormWindowState.Minimized;
            else if (hit == TitleBar.HtMax) WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            else if (hit == TitleBar.HtClose) Close();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle, false);
            Native.UseDarkFrame(Handle, Theme.Dark);
            Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x37); // NOSIZE|NOMOVE|NOZORDER|NOACTIVATE|FRAMECHANGED
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (_titleBar != null) { _titleBar.Active = true; _titleBar.Invalidate(); }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_titleBar != null) { _titleBar.Active = false; _titleBar.Hover = 0; _titleBar.Invalidate(); }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_titleBar != null)
            {
                bool max = WindowState == FormWindowState.Maximized;
                if (_titleBar.Maximized != max)
                {
                    _titleBar.Maximized = max;
                    _titleBar.Invalidate();
                }
            }
            if (_nav != null && WindowState != FormWindowState.Minimized)
            {
                bool wide = ClientSize.Width >= 1000;
                if (_lastWide != wide)
                {
                    _lastWide = wide;
                    _nav.Expanded = wide;
                }
            }
            LayoutHomeTiles();
            LayoutStatsBar();
            LayoutToolbarButtons();
            LayoutSettings();
            if (_sessionPanel != null && _sessionPanel.Visible && _split != null && !_split.Panel1Collapsed && _split.Width > 400)
                SafeConfigureSplit(true);
        }

        void BuildChrome()
        {
            _titleBar = new TitleBar();
            _titleBar.Dock = DockStyle.Top;
            _titleBar.Height = TitleBarHeight;
            _titleBar.Title = "AppInWhats DevBuild";
            try { if (Icon != null) _titleBar.AppIcon = new Icon(Icon, 16, 16).ToBitmap(); }
            catch { }

            _nav = new NavPane();
            _nav.Dock = DockStyle.Left;
            _navHome = _nav.Add("home", "Inicio", Glyphs.Home, false);
            _navSession = _nav.Add("session", "Sesión", Glyphs.Terminal, false);
            _navSettings = _nav.Add("settings", "Ajustes", Glyphs.Settings, true);
            _navSession.Enabled = false;
            _navSession.BadgeColor = Theme.Success;
            _nav.ItemInvoked += delegate(NavItem it)
            {
                if (it == _navHome) ShowHome();
                else if (it == _navSession) ShowSessionPage();
                else if (it == _navSettings) ShowSettings();
            };

            _contentHost = new DoubleBufferedPanel();
            _contentHost.Dock = DockStyle.Fill;
            _contentHost.BackColor = Theme.Layer;
            _contentHost.Padding = new Padding(1, 1, 0, 0);
            _contentHost.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.CardStroke, 1f))
                {
                    e.Graphics.DrawLine(pen, 8, 0, _contentHost.Width, 0);
                    e.Graphics.DrawLine(pen, 0, 8, 0, _contentHost.Height);
                }
            };

            Panel statusBar = new DoubleBufferedPanel();
            statusBar.Dock = DockStyle.Bottom;
            statusBar.Height = 32;
            statusBar.BackColor = Theme.Layer;
            statusBar.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Divider, 1f))
                    e.Graphics.DrawLine(pen, 0, 0, statusBar.Width, 0);
            };

            _status = new StatusLabel();
            _status.Dock = DockStyle.Fill;
            _status.Padding = new Padding(20, 0, 12, 0);
            _status.Text = "Listo";
            _status.ForeColor = Theme.TextTertiary;

            _statusPath = new Label();
            _statusPath.Dock = DockStyle.Right;
            _statusPath.AutoSize = false;
            _statusPath.AutoEllipsis = true;
            _statusPath.Text = _root;
            _statusPath.Font = Theme.Text(9f);
            _statusPath.ForeColor = Theme.TextTertiary;
            _statusPath.TextAlign = ContentAlignment.MiddleRight;
            _statusPath.Padding = new Padding(0, 0, 18, 0);
            _statusPath.Width = Math.Min(460, Gfx.Measure(_root, _statusPath.Font) + 40);
            _statusPath.Cursor = Cursors.Hand;
            _statusPath.Click += delegate { OpenFolder(_root); };
            FluentUi.Tip(_statusPath, "Abrir la carpeta del proyecto");

            statusBar.Controls.Add(_status);
            statusBar.Controls.Add(_statusPath);
            _contentHost.Controls.Add(statusBar);

            _corner = new LayerCorner();
            _contentHost.Controls.Add(_corner);

            Controls.Add(_contentHost);
            Controls.Add(_nav);
            Controls.Add(_titleBar);
        }

        void AddPage(Panel page)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _contentHost.Controls.Add(page);
            page.BringToFront();
            _corner.BringToFront();
        }

        void ShowPage(Panel page, string navKey)
        {
            Panel[] pages = new Panel[] { _homePanel, _sessionPanel, _settingsPanel };
            for (int i = 0; i < pages.Length; i++)
                if (pages[i] != null && pages[i] != page) pages[i].Visible = false;
            page.Visible = true;
            _corner.BringToFront();
            _nav.Selected = navKey;
        }

        void ShowHome()
        {
            ShowPage(_homePanel, "home");
            if (!_running)
            {
                _status.Text = "Listo";
                _status.ForeColor = Theme.TextTertiary;
            }
            UpdateHomeBanners();
        }

        void ShowSessionPage()
        {
            if (string.IsNullOrEmpty(_modeId)) return;
            ShowPage(_sessionPanel, "session");
            SafeConfigureSplit(_modeDual);
            LayoutStatsBar();
            LayoutToolbarButtons();
        }

        void ShowSettings()
        {
            RefreshSettingsPage();
            ShowPage(_settingsPanel, "settings");
            LayoutSettings();
        }

        void UpdateNavState()
        {
            if (_nav == null) return;
            _navSession.Enabled = !string.IsNullOrEmpty(_modeId);
            _navSession.Badge = _running;
            _navSession.Tooltip = string.IsNullOrEmpty(_modeTitle) ? null : (_modeTitle + (_running ? " · en ejecución" : " · detenida"));
            _nav.Invalidate();
        }

        static void OpenFolder(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo abrir la carpeta:\n" + ex.Message, "AppInWhats DevBuild", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Esquina redondeada superior izquierda de la capa de contenido (sobre Mica)
        sealed class LayerCorner : Control
        {
            public LayerCorner()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
                Size = new Size(10, 10);
                Location = Point.Empty;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Theme.Bg);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (GraphicsPath p = Gfx.RoundRect(new Rectangle(0, 0, 40, 40), 8))
                {
                    using (SolidBrush b = new SolidBrush(Theme.Layer)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Theme.CardStroke, 1f)) g.DrawPath(pen, p);
                }
            }
        }

        // --- Inicio ---

        sealed class HomeSection
        {
            public string Id;
            public SectionCard Card;
            public Color Accent;
            public Control DragHandle;
            public Control BtnUp;
            public Control BtnDown;
            public readonly List<Control> Tiles = new List<Control>();
        }

        void BuildHome()
        {
            _homePanel = new DoubleBufferedPanel();
            _homePanel.BackColor = Theme.Layer;
            _homePanel.Resize += delegate { LayoutHomeTiles(); };

            _homeHero = new DoubleBufferedPanel();
            _homeHero.BackColor = Theme.Layer;
            _homeHero.Paint += delegate(object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Theme.Layer);
                Gfx.TextLeft(g, "Elige un modo", Theme.Display(21f), new Rectangle(32, 22, _homeHero.Width - 64, 40), Theme.TextPrimary);
                Gfx.TextLeft(g, "Arranca el entorno de desarrollo, compila o despliega. Arrastra las categorías para reordenarlas.",
                    Theme.Text(10.5f), new Rectangle(33, 64, _homeHero.Width - 64, 22), Theme.TextSecondary);
            };

            _resumeBanner = new InfoBar(Severity.Warning);
            _resumeBanner.Title = "Sesión anterior activa";
            _resumeBanner.Visible = false;
            _resumeBanner.AddButton("Abrir sesión", ButtonKind.Accent, delegate { ResumePreviousSession(); });
            _resumeBanner.AddButton("Detener", ButtonKind.Standard, delegate
            {
                KillSessionFromFile();
                HideResumeBanner();
                _status.Text = "Sesión anterior detenida";
                _status.ForeColor = Theme.TextTertiary;
            });

            _runBanner = new InfoBar(Severity.Success);
            _runBanner.Title = "En ejecución";
            _runBanner.Visible = false;
            _runBanner.AddButton("Ver sesión", ButtonKind.Accent, delegate { ShowSessionPage(); });
            _runBanner.AddButton("Parar", ButtonKind.Standard, delegate { StopAll(true); });

            _homeScrollHost = new DoubleBufferedPanel();
            _homeScrollHost.BackColor = Theme.Layer;

            _homeScrollBar = new FluentScrollBar();
            _homeScrollBar.Dock = DockStyle.Right;
            _homeScrollBar.Width = 12;
            _homeScrollBar.Scroll += delegate { ApplyHomeScroll(); };

            _homeViewport = new DoubleBufferedPanel();
            _homeViewport.Dock = DockStyle.Fill;
            _homeViewport.BackColor = Theme.Layer;

            _tileGrid = new DoubleBufferedPanel();
            _tileGrid.Location = Point.Empty;
            _tileGrid.BackColor = Theme.Layer;

            Dictionary<string, HomeSection> byId = new Dictionary<string, HomeSection>(StringComparer.OrdinalIgnoreCase);
            byId["Native"] = MakeHomeSection("Native", "Native", "Sin Docker · más ligero y rápido de arrancar", Glyphs.Laptop, Color.FromArgb(16, 137, 62),
                new TileInfo[]
                {
                    new TileInfo("Nest", "Backend NestJS + frontend", Glyphs.Code, Color.FromArgb(16, 137, 62), StartNativeLocal, null),
                    new TileInfo("Legacy", "Express (scr/) + frontend", Glyphs.History, Color.FromArgb(73, 130, 5), StartNativeLegacy, null),
                    new TileInfo("Remote", "Solo frontend · API remota", Glyphs.Globe, Color.FromArgb(0, 120, 212), StartNativeRemote, null),
                });
            byId["Docker"] = MakeHomeSection("Docker", "Docker", "Stack en contenedores · local, remoto u optimizado", Glyphs.Package, Color.FromArgb(0, 99, 177),
                new TileInfo[]
                {
                    new TileInfo("Local", "Stack completo en contenedores", Glyphs.Package, Color.FromArgb(0, 99, 177), StartDockerLocal, null),
                    new TileInfo("Remote", "Front en Docker + API remota", Glyphs.Swap, Color.FromArgb(202, 80, 16), StartDockerRemote, null),
                    new TileInfo("Optimizado", "Menos CPU y RAM", Glyphs.Bolt, Color.FromArgb(0, 130, 114), StartOptLocal, null),
                    new TileInfo("Optimizado remoto", "Optimizado + API remota", Glyphs.Speed, Color.FromArgb(0, 153, 188), StartOptRemote, null),
                });
            byId["Servidor"] = MakeHomeSection("Servidor", "Servidor", BuildServerSubtitle(), Glyphs.Server, Color.FromArgb(81, 92, 107),
                new TileInfo[]
                {
                    new TileInfo("Ejecutar en servidor", "Despliega y arranca por SSH", Glyphs.Cloud, Color.FromArgb(81, 92, 107), StartServerMode, null),
                    new TileInfo("Configuración", "IP, usuario y puertos", Glyphs.Sliders, Color.FromArgb(107, 105, 214), OpenServerSettings, Glyphs.Edit),
                });
            byId["Otros"] = MakeHomeSection("Otros", "Otros", "Utilidades del entorno de desarrollo", Glyphs.Tools, BackendColor,
                new TileInfo[]
                {
                    new TileInfo("Instalar dependencias", "npm install en front y back", Glyphs.Download, Color.FromArgb(195, 0, 82), StartInstall, null),
                    new TileInfo("Compilar frontend", "Production · abre la carpeta dist", Glyphs.Wrench, BackendColor, StartCompileFrontFromHome, null),
                    new TileInfo("Deploy a Plesk", "Compila y sube dist por SFTP", Glyphs.Upload, Color.FromArgb(194, 57, 179), StartCompileAndUploadPlesk, Glyphs.Upload),
                });

            List<string> order = LoadHomeSectionOrder(DefaultSectionOrder);
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < order.Count; i++)
            {
                HomeSection sec;
                if (byId.TryGetValue(order[i], out sec) && used.Add(order[i]))
                    _homeSections.Add(sec);
            }
            for (int i = 0; i < DefaultSectionOrder.Length; i++)
            {
                if (used.Add(DefaultSectionOrder[i]))
                    _homeSections.Add(byId[DefaultSectionOrder[i]]);
            }
            EnsureOtrosLast();

            _homeViewport.Controls.Add(_tileGrid);
            _homeScrollHost.Controls.Add(_homeViewport);
            _homeScrollHost.Controls.Add(_homeScrollBar);
            _homeScrollHost.MouseWheel += HomeMouseWheel;
            _homeViewport.MouseWheel += HomeMouseWheel;
            _tileGrid.MouseWheel += HomeMouseWheel;

            _homePanel.Controls.Add(_homeHero);
            _homePanel.Controls.Add(_resumeBanner);
            _homePanel.Controls.Add(_runBanner);
            _homePanel.Controls.Add(_homeScrollHost);
            AddPage(_homePanel);
            UpdateSectionOrderButtons();
        }

        HomeSection MakeHomeSection(string id, string title, string subtitle, string glyph, Color accent, TileInfo[] tiles)
        {
            HomeSection section = new HomeSection();
            section.Id = id;
            section.Accent = accent;
            SectionCard card = new SectionCard(title, subtitle, glyph, accent);
            section.Card = card;
            bool otros = IsOtrosSection(section);

            FluentButton up = MakeIconButton(Glyphs.ChevronUp, "Subir categoría");
            FluentButton down = MakeIconButton(Glyphs.ChevronDown, "Bajar categoría");
            FluentButton grip = MakeIconButton(Glyphs.Gripper, "Arrastrar para reordenar");
            up.Click += delegate { MoveHomeSection(section, -1); };
            down.Click += delegate { MoveHomeSection(section, 1); };
            section.BtnUp = up;
            section.BtnDown = down;
            section.DragHandle = grip;
            card.Controls.Add(up);
            card.Controls.Add(down);
            card.Controls.Add(grip);

            for (int i = 0; i < tiles.Length; i++)
            {
                TileInfo info = tiles[i];
                TileFace face = new TileFace(info);
                face.Click += delegate { info.Action(); };
                card.Controls.Add(face);
                section.Tiles.Add(face);
            }

            WireSectionDrag(section, grip);
            card.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && !otros && e.Y < SectionCard.HeaderHeight)
                    BeginSectionDrag(section, e);
            };
            card.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (_draggingSection && _dragSection == section)
                    MoveSectionDrag(e);
                else
                    card.Cursor = !otros && e.Y < SectionCard.HeaderHeight ? Cursors.SizeAll : Cursors.Default;
            };
            card.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) EndSectionDrag();
            };
            card.Resize += delegate { LayoutSectionHeaderControls(section); };

            _tileGrid.Controls.Add(card);
            WireHomeWheel(card);
            return section;
        }

        static FluentButton MakeIconButton(string glyph, string tooltip)
        {
            FluentButton b = new FluentButton("", ButtonKind.Subtle, glyph);
            b.Size = new Size(30, 30);
            b.GlyphSize = 9f;
            b.TabStop = false;
            FluentUi.Tip(b, tooltip);
            return b;
        }

        void LayoutSectionHeaderControls(HomeSection section)
        {
            if (section == null || section.Card == null) return;
            int y = 20;
            int right = section.Card.Width - 14;
            bool shown = !IsOtrosSection(section);
            Control[] ctrls = new Control[] { section.DragHandle, section.BtnDown, section.BtnUp };
            for (int i = 0; i < ctrls.Length; i++)
            {
                if (ctrls[i] == null) continue;
                ctrls[i].Location = new Point(right - ctrls[i].Width, y);
                if (shown) right = ctrls[i].Left - 2;
            }
            section.Card.TextRight = right - 8;
            section.Card.Invalidate();
        }

        void HomeMouseWheel(object sender, MouseEventArgs e)
        {
            if (_homeScrollBar == null || !_homeScrollBar.Enabled) return;
            _homeScrollBar.Value = _homeScrollBar.Value + (e.Delta > 0 ? -64 : 64);
        }

        void ApplyHomeScroll()
        {
            if (_tileGrid == null || _homeScrollBar == null) return;
            _tileGrid.Top = -_homeScrollBar.Value;
        }

        void ShowResumeBanner(string message)
        {
            _resumeBanner.Message = message;
            _resumeBanner.Visible = true;
            LayoutHomeTiles();
        }

        void UpdateHomeBanners()
        {
            if (_runBanner == null) return;
            bool show = _running && !string.IsNullOrEmpty(_modeTitle);
            _runBanner.Message = _modeTitle + (IsServerMode() ? "" : " · http://localhost:50080");
            if (_runBanner.Visible != show)
            {
                _runBanner.Visible = show;
                LayoutHomeTiles();
            }
        }

        void LayoutHomeTiles()
        {
            if (_homePanel == null || _homeSections.Count == 0) return;
            int hostW = _homePanel.ClientSize.Width;
            int hostH = _homePanel.ClientSize.Height;
            if (hostW <= 0 || hostH <= 0) return;

            EnsureOtrosLast();

            const int padX = 32;
            const int padRight = 20;
            const int barW = 12;
            const int sectionGap = 16;
            const int gap = 8;
            const int cardPad = 16;
            const int tileH = 68;
            const int minTw = 210;
            const int minColW = 330;
            const int bottomPad = 24;

            int viewportW = Math.Max(180, hostW - barW);
            int contentW = Math.Max(minColW, viewportW - padX - padRight);

            int y = 96;
            _homeHero.Bounds = new Rectangle(0, 0, hostW, y);
            _homeHero.Invalidate();
            InfoBar[] banners = new InfoBar[] { _resumeBanner, _runBanner };
            for (int i = 0; i < banners.Length; i++)
            {
                if (banners[i] == null || !banners[i].Visible) continue;
                banners[i].Bounds = new Rectangle(padX, y, contentW, 52);
                y += 52 + 12;
            }
            y += 4;
            _homeScrollHost.Bounds = new Rectangle(0, y, hostW, Math.Max(80, hostH - y));
            int viewportH = Math.Max(80, hostH - y);

            // Masonry: cada categoría va a la columna más corta
            int cols = Math.Max(1, (contentW + sectionGap) / (minColW + sectionGap));
            if (cols > _homeSections.Count) cols = _homeSections.Count;
            int colW = (contentW - (cols - 1) * sectionGap) / cols;
            int[] colY = new int[cols];
            for (int c = 0; c < cols; c++) colY[c] = 2;
            int maxBottom = 2;

            for (int s = 0; s < _homeSections.Count; s++)
            {
                HomeSection section = _homeSections[s];
                if (section == null || section.Card == null) continue;

                int innerW = Math.Max(120, colW - cardPad * 2);
                int tileCols = Math.Max(1, (innerW + gap) / (minTw + gap));
                if (tileCols > section.Tiles.Count) tileCols = Math.Max(1, section.Tiles.Count);
                int tw = (innerW - (tileCols - 1) * gap) / tileCols;
                int rows = Math.Max(1, (section.Tiles.Count + tileCols - 1) / tileCols);
                int cardH = SectionCard.HeaderHeight + rows * tileH + (rows - 1) * gap + cardPad;

                int best = 0;
                for (int c = 1; c < cols; c++)
                    if (colY[c] < colY[best]) best = c;

                if (!(_draggingSection && _dragSection == section))
                    section.Card.Location = new Point(padX + best * (colW + sectionGap), colY[best]);
                section.Card.Size = new Size(colW, cardH);
                LayoutSectionHeaderControls(section);

                for (int i = 0; i < section.Tiles.Count; i++)
                {
                    int col = i % tileCols;
                    int row = i / tileCols;
                    section.Tiles[i].Bounds = new Rectangle(cardPad + col * (tw + gap), SectionCard.HeaderHeight + row * (tileH + gap), tw, tileH);
                }

                colY[best] += cardH + sectionGap;
                if (colY[best] > maxBottom) maxBottom = colY[best];
            }

            int contentH = maxBottom + bottomPad;
            _tileGrid.Width = viewportW;
            _tileGrid.Height = Math.Max(viewportH, contentH);

            int maxScroll = Math.Max(0, contentH - viewportH);
            _homeScrollBar.Minimum = 0;
            _homeScrollBar.LargeChange = Math.Max(1, viewportH);
            _homeScrollBar.Maximum = Math.Max(viewportH, contentH);
            if (_homeScrollBar.Value > maxScroll) _homeScrollBar.Value = maxScroll;
            _homeScrollBar.Enabled = maxScroll > 0;
            _homeScrollBar.Invalidate();
            ApplyHomeScroll();
        }

        void ResetHomeSectionOrder()
        {
            List<HomeSection> ordered = new List<HomeSection>();
            for (int i = 0; i < DefaultSectionOrder.Length; i++)
                for (int j = 0; j < _homeSections.Count; j++)
                    if (string.Equals(_homeSections[j].Id, DefaultSectionOrder[i], StringComparison.OrdinalIgnoreCase))
                        ordered.Add(_homeSections[j]);
            for (int j = 0; j < _homeSections.Count; j++)
                if (!ordered.Contains(_homeSections[j])) ordered.Add(_homeSections[j]);
            _homeSections.Clear();
            _homeSections.AddRange(ordered);
            EnsureOtrosLast();
            SaveHomeSectionOrder();
            LayoutHomeTiles();
            UpdateSectionOrderButtons();
            _status.Text = "Orden de categorías restablecido";
            _status.ForeColor = Theme.TextTertiary;
        }

        // --- Sesión ---

        void BuildSession()
        {
            _sessionPanel = new DoubleBufferedPanel();
            _sessionPanel.BackColor = Theme.Layer;
            _sessionPanel.Padding = new Padding(28, 18, 28, 20);

            _toolbar = new DoubleBufferedPanel();
            _toolbar.Dock = DockStyle.Top;
            _toolbar.Height = 74;
            _toolbar.BackColor = Theme.Layer;
            _toolbar.Paint += PaintSessionHeader;
            _toolbar.Resize += delegate { LayoutToolbarButtons(); };

            _btnOpen = new FluentButton("Abrir app", ButtonKind.Standard, Glyphs.OpenExternal);
            _btnOpen.Click += delegate { OpenAppInBrowser(); };
            FluentUi.Tip(_btnOpen, "Abrir la aplicación en el navegador");

            _btnBuild = new FluentButton("Compilar front", ButtonKind.Standard, Glyphs.Wrench);
            _btnBuild.Click += delegate { StartFrontendProductionBuild(); };
            FluentUi.Tip(_btnBuild, "ng build --configuration=production (sin parar el serve)");

            _btnStart = new FluentButton("Iniciar", ButtonKind.Accent, Glyphs.Play);
            _btnStart.Enabled = false;
            _btnStart.Visible = false;
            _btnStart.Click += delegate { RestartCurrentMode(); };
            FluentUi.Tip(_btnStart, "Volver a iniciar este modo");

            _btnStop = new FluentButton("Parar todo", ButtonKind.Danger, Glyphs.Stop);
            _btnStop.Click += delegate { StopAll(true); };
            FluentUi.Tip(_btnStop, "Detener todos los procesos de la sesión");

            _toolbar.Controls.Add(_btnOpen);
            _toolbar.Controls.Add(_btnBuild);
            _toolbar.Controls.Add(_btnStart);
            _toolbar.Controls.Add(_btnStop);

            _serverBar = new DoubleBufferedPanel();
            _serverBar.Dock = DockStyle.Top;
            _serverBar.Height = 68;
            _serverBar.BackColor = Theme.Layer;
            _serverBar.Visible = false;
            _serverBar.Paint += delegate(object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Theme.Layer);
                Gfx.Card(g, new Rectangle(0, 0, _serverBar.Width - 1, 55), 8, Theme.Card, Theme.CardStroke);
                Gfx.Glyph(g, Glyphs.Cloud, 13f, new Rectangle(14, 0, 32, 56), Theme.Accent);
                int right = _btnSyncBack != null ? _btnSyncBack.Left - 16 : _serverBar.Width - 16;
                Gfx.TextLeft(g, "Sincronizar con el servidor", Theme.Strong(10.5f), new Rectangle(56, 8, Math.Max(1, right - 56), 22), Theme.TextPrimary);
                Gfx.TextLeft(g, string.IsNullOrEmpty(_serverAppUrl) ? "Sube los cambios locales por SSH" : "Sube los cambios locales a " + GetServerHostPortLabel(),
                    Theme.Text(9f), new Rectangle(56, 29, Math.Max(1, right - 56), 18), Theme.TextSecondary);
            };

            _btnSyncBack = new FluentButton("Backend", ButtonKind.Standard, Glyphs.Upload);
            _btnSyncFront = new FluentButton("Frontend", ButtonKind.Standard, Glyphs.Upload);
            _btnSyncBoth = new FluentButton("Ambos", ButtonKind.Accent, Glyphs.Upload);
            _btnSyncBack.Click += delegate { RunServerAction("deploy-backend", true); };
            _btnSyncFront.Click += delegate { RunServerAction("deploy-frontend", false); };
            _btnSyncBoth.Click += delegate { RunServerAction("deploy-both", true); };
            FluentButton[] sync = new FluentButton[] { _btnSyncBack, _btnSyncFront, _btnSyncBoth };
            for (int i = 0; i < sync.Length; i++)
            {
                sync[i].BackColor = Theme.Card;
                _serverBar.Controls.Add(sync[i]);
            }
            _serverBar.Resize += delegate { LayoutServerBar(); };

            _statsBar = new DoubleBufferedPanel();
            _statsBar.Dock = DockStyle.Top;
            _statsBar.Height = 78;
            _statsBar.BackColor = Theme.Layer;
            _statsBack = new StatsCard("Backend", BackendColor);
            _statsFront = new StatsCard("Frontend", Theme.Accent);
            _statsOther = new StatsCard("Otros", OtherColor);
            _statsTotal = new StatsCard("Total", TotalColor);
            _statsBar.Controls.Add(_statsBack);
            _statsBar.Controls.Add(_statsFront);
            _statsBar.Controls.Add(_statsOther);
            _statsBar.Controls.Add(_statsTotal);
            _statsBar.Resize += delegate { LayoutStatsBar(); };

            _split = new SplitContainer();
            _split.Dock = DockStyle.Fill;
            _split.SplitterWidth = 12;
            _split.TabStop = false;
            _split.BackColor = Theme.Layer;
            _split.Panel1.BackColor = Theme.Layer;
            _split.Panel2.BackColor = Theme.Layer;

            _termBack = new TerminalView("Backend", BackendColor, Path.Combine(_root, "backend"));
            _termFront = new TerminalView("Frontend", Theme.Accent, Path.Combine(_root, "frontend"));
            _termBack.BeforeBranchSwitch = StopForBranchSwitch;
            _termFront.BeforeBranchSwitch = StopForBranchSwitch;
            _termBack.AfterBranchSwitchSuccess = RestartAfterBranchSwitch;
            _termFront.AfterBranchSwitchSuccess = RestartAfterBranchSwitch;
            _termBack.Dock = DockStyle.Fill;
            _termFront.Dock = DockStyle.Fill;
            _split.Panel1.Controls.Add(_termBack);
            _split.Panel2.Controls.Add(_termFront);

            // Orden de Dock: Fill primero; las barras superiores se apilan en orden inverso
            _sessionPanel.Controls.Add(_split);
            _sessionPanel.Controls.Add(_statsBar);
            _sessionPanel.Controls.Add(_serverBar);
            _sessionPanel.Controls.Add(_toolbar);
            AddPage(_sessionPanel);
            LayoutStatsBar();
            LayoutServerBar();
        }

        string SessionSubtitle()
        {
            switch (_modeId)
            {
                case "native-local": return "NestJS en :3001 · Angular en http://localhost:50080";
                case "native-legacy": return "Express en :3001 · Angular en http://localhost:50080";
                case "native-remote": return "Angular en http://localhost:50080 · API https://desarrollo.appinwhats.com";
                case "docker-local":
                case "opt-local": return "Docker Compose · http://localhost:50080";
                case "docker-remote":
                case "opt-remote": return "Docker Compose con API remota · http://localhost:50080";
                case "install": return "npm install --legacy-peer-deps en frontend y backend";
                case "server": return "Servidor remoto · " + (string.IsNullOrEmpty(_serverAppUrl) ? "sin configurar" : _serverAppUrl);
                case "compile-front": return "ng build --configuration=production · al terminar se abre dist";
                case "compile-plesk": return "ng build --configuration=production · subida por SFTP a Plesk";
                default: return "";
            }
        }

        void PaintSessionHeader(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Layer);

            int limit = _toolbar.Width;
            FluentButton[] btns = new FluentButton[] { _btnOpen, _btnBuild, _btnStart, _btnStop };
            for (int i = 0; i < btns.Length; i++)
                if (btns[i] != null && btns[i].Visible) limit = Math.Min(limit, btns[i].Left);
            limit -= 20;

            Font tf = Theme.Display(15f);
            string pill = _running ? (IsServerMode() ? "Conectado" : "En ejecución") : "Detenido";
            Font pf = Theme.Strong(8.25f);
            int pw = Gfx.Measure(pill, pf) + 28;
            int tw = Math.Max(40, Math.Min(Gfx.Measure(_sessionTitleText, tf) + 2, limit - pw - 12));
            Gfx.TextLeft(g, _sessionTitleText, tf, new Rectangle(0, 4, tw, 34), Theme.TextPrimary);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle pr = new Rectangle(tw + 12, 11, pw, 22);
            Gfx.FillRound(g, pr, 11, _running ? Theme.SuccessBg : Theme.SubtleHover(Theme.Layer));
            using (SolidBrush b = new SolidBrush(_running ? Theme.Success : Theme.TextTertiary))
                g.FillEllipse(b, pr.X + 10, pr.Y + 8, 6, 6);
            Gfx.TextLeft(g, pill, pf, new Rectangle(pr.X + 20, pr.Y, pw - 22, pr.Height), _running ? Theme.Success : Theme.TextSecondary);

            Gfx.TextLeft(g, SessionSubtitle(), Theme.Text(9.75f), new Rectangle(1, 40, Math.Max(1, limit), 20), Theme.TextSecondary);
        }

        void LayoutToolbarButtons()
        {
            if (_toolbar == null || _btnStop == null) return;
            FluentButton[] btns = new FluentButton[] { _btnOpen, _btnBuild, _btnStart, _btnStop };
            int full = 0;
            for (int i = 0; i < btns.Length; i++)
                if (btns[i].Visible) full += btns[i].MeasureWidth(false) + 8;
            int titleW = Gfx.Measure(_sessionTitleText, Theme.Display(15f)) + 140;
            bool compact = full + titleW > _toolbar.Width;
            int x = _toolbar.Width;
            for (int i = btns.Length - 1; i >= 0; i--)
            {
                FluentButton b = btns[i];
                b.IconOnly = compact;
                if (!b.Visible) continue;
                x -= b.Width;
                b.Location = new Point(x, 10);
                x -= 8;
            }
            _toolbar.Invalidate();
        }

        void LayoutServerBar()
        {
            if (_serverBar == null || _btnSyncBoth == null) return;
            int x = _serverBar.Width - 12;
            FluentButton[] sync = new FluentButton[] { _btnSyncBoth, _btnSyncFront, _btnSyncBack };
            for (int i = 0; i < sync.Length; i++)
            {
                x -= sync[i].Width;
                sync[i].Location = new Point(x, 12);
                x -= 8;
            }
            _serverBar.Invalidate();
        }

        void LayoutStatsBar()
        {
            if (_statsBar == null || _statsBack == null) return;
            const int gap = 12;
            int w = Math.Max(120, (_statsBar.ClientSize.Width - gap * 3) / 4);
            StatsCard[] cards = new StatsCard[] { _statsBack, _statsFront, _statsOther, _statsTotal };
            for (int i = 0; i < cards.Length; i++)
                cards[i].Bounds = new Rectangle(i * (w + gap), 0, i == cards.Length - 1 ? _statsBar.ClientSize.Width - i * (w + gap) : w, 64);
        }

        void SafeConfigureSplit(bool dual)
        {
            try
            {
                _sessionPanel.PerformLayout();
                _split.PerformLayout();
                int w = _split.Width;
                if (w < 100) w = Math.Max(100, ClientSize.Width - 20);
                if (dual)
                {
                    _split.Panel1Collapsed = false;
                    int min = Math.Max(80, Math.Min(200, (w - _split.SplitterWidth) / 4));
                    _split.Panel1MinSize = min;
                    _split.Panel2MinSize = min;
                    int dist = (w - _split.SplitterWidth) / 2;
                    int maxDist = w - _split.Panel2MinSize - _split.SplitterWidth;
                    if (dist < _split.Panel1MinSize) dist = _split.Panel1MinSize;
                    if (dist > maxDist) dist = Math.Max(_split.Panel1MinSize, maxDist);
                    if (maxDist >= _split.Panel1MinSize)
                        _split.SplitterDistance = dist;
                }
                else
                {
                    _split.Panel1MinSize = 0;
                    _split.Panel2MinSize = 0;
                    _split.Panel1Collapsed = true;
                }
            }
            catch
            {
                try { _split.Panel1Collapsed = !dual; } catch { }
            }
        }

        bool IsServerMode()
        {
            return string.Equals(_modeId, "server", StringComparison.OrdinalIgnoreCase);
        }

        void UpdateServerBarVisibility()
        {
            if (_serverBar == null) return;
            _serverBar.Visible = IsServerMode();
            LayoutToolbarButtons();
            LayoutServerBar();
        }

        void UpdateSessionButtons()
        {
            if (_btnStop == null) return;
            _btnStop.Visible = _running;
            _btnStart.Visible = !_running;
            _btnStart.Enabled = !_running && !string.IsNullOrEmpty(_modeId);
            LayoutToolbarButtons();
        }

        void ShowSession(string title, bool dual)
        {
            _sessionTitleText = title;
            _termBack.Clear();
            _termFront.Clear();

            if (dual)
            {
                if (IsServerMode())
                {
                    _termBack.SetTitle("Backend remoto (SSH)");
                    _termFront.SetTitle("Frontend remoto · " + GetServerHostPortLabel());
                }
                else
                {
                    _termBack.SetTitle("Backend");
                    _termFront.SetTitle("Frontend");
                }
            }
            else
            {
                _termFront.SetTitle(title);
            }

            UpdateNavState();
            ShowPage(_sessionPanel, "session");
            SafeConfigureSplit(dual);
            UpdateServerBarVisibility();
            UpdateSessionButtons();
            LayoutStatsBar();
            ResetStatsDisplay();
            _termBack.RefreshGitBranch();
            _termFront.RefreshGitBranch();
            _statsBar.Visible = !IsServerMode();
            _toolbar.Invalidate();
        }

        void ResetStatsDisplay()
        {
            if (_statsBack == null) return;
            _statsOther.Role = _modeDocker ? "Docker" : "Otros";
            _statsBack.Reset();
            _statsFront.Reset();
            _statsOther.Reset();
            _statsTotal.Reset();
            _prevCpu.Clear();
            _prevCpuSample = DateTime.MinValue;
        }

        static void ApplyStats(StatsCard card, string role, ProcAgg agg)
        {
            card.Role = role;
            if (agg.Count <= 0) card.SetIdle();
            else card.SetValues(agg.CpuPercent, agg.MemoryBytes, agg.Count);
        }

        void SetRunning(bool running)
        {
            _running = running;
            _status.ForeColor = running ? Theme.Success : Theme.TextTertiary;
            if (running && IsServerMode())
                _status.Text = "Servidor remoto  ·  " + _serverAppUrl;
            else
                _status.Text = running ? "Ejecutando…  ·  http://localhost:50080" : "Detenido";
            if (!running && !_closing)
            {
                ClearSessionFile();
                ResetStatsDisplay();
            }
            UpdateSessionButtons();
            UpdateServerBarVisibility();
            UpdateNavState();
            UpdateHomeBanners();
            if (_toolbar != null) _toolbar.Invalidate();
        }

        // --- Ajustes ---

        void BuildSettings()
        {
            _settingsPanel = new DoubleBufferedPanel();
            _settingsPanel.BackColor = Theme.Layer;
            _settingsPanel.AutoScroll = true;
            _settingsPanel.HandleCreated += delegate { Native.ThemeScrollbars(_settingsPanel); };
            _settingsPanel.Resize += delegate { LayoutSettings(); };

            Label title = new Label();
            title.Text = "Ajustes";
            title.Font = Theme.Display(21f);
            title.ForeColor = Theme.TextPrimary;
            title.AutoSize = true;
            title.Tag = "title";
            _settingsRows.Add(title);

            AddSettingsHeader("Conexiones");
            _setServer = new SettingsCard(Glyphs.Cloud, "Servidor remoto (SSH)", "");
            _setServer.SetAction("Editar", Glyphs.Edit, delegate { OpenServerSettings(); });
            _settingsRows.Add(_setServer);

            _setPlesk = new SettingsCard(Glyphs.Upload, "Subida a Plesk (SFTP)", "");
            _setPlesk.SetAction("Editar", Glyphs.Edit, delegate { if (ShowPleskConfigDialog(false)) RefreshSettingsPage(); });
            _settingsRows.Add(_setPlesk);

            AddSettingsHeader("Inicio");
            SettingsCard order = new SettingsCard(Glyphs.Sort, "Orden de categorías",
                "Arrastra las categorías en Inicio o usa las flechas. «Otros» siempre va al final.");
            order.SetAction("Restablecer", null, delegate { ResetHomeSectionOrder(); });
            _settingsRows.Add(order);

            AddSettingsHeader("Proyecto");
            SettingsCard folder = new SettingsCard(Glyphs.FolderOpen, "Carpeta del proyecto", _root);
            folder.SetAction("Abrir", null, delegate { OpenFolder(_root); });
            _settingsRows.Add(folder);

            SettingsCard term = new SettingsCard(Glyphs.Terminal, "Terminal", "Abre Windows Terminal (o PowerShell) en la carpeta del proyecto");
            term.SetAction("Abrir", null, delegate { OpenTerminalHere(); });
            _settingsRows.Add(term);

            AddSettingsHeader("Acerca de");
            string built = "";
            try { built = File.GetLastWriteTime(Application.ExecutablePath).ToString("d 'de' MMMM yyyy, HH:mm", new System.Globalization.CultureInfo("es-ES")); }
            catch { }
            SettingsCard about = new SettingsCard(Glyphs.Info, "AppInWhats DevBuild",
                "Tema " + (Theme.Dark ? "oscuro" : "claro") + " según Windows" + (built.Length > 0 ? " · compilado el " + built : ""));
            _settingsRows.Add(about);

            for (int i = 0; i < _settingsRows.Count; i++)
                _settingsPanel.Controls.Add(_settingsRows[i]);
            AddPage(_settingsPanel);
            RefreshSettingsPage();
        }

        void AddSettingsHeader(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.Strong(10.5f);
            l.ForeColor = Theme.TextPrimary;
            l.AutoSize = true;
            l.Tag = "header";
            _settingsRows.Add(l);
        }

        void LayoutSettings()
        {
            if (_settingsPanel == null) return;
            int w = Math.Max(320, Math.Min(1000, _settingsPanel.ClientSize.Width - 64));
            int y = 22 + _settingsPanel.AutoScrollPosition.Y;
            int x = 32 + _settingsPanel.AutoScrollPosition.X;
            for (int i = 0; i < _settingsRows.Count; i++)
            {
                Control c = _settingsRows[i];
                string tag = c.Tag as string;
                if (tag == "title")
                {
                    c.Location = new Point(x - 2, y);
                    y += 62;
                }
                else if (tag == "header")
                {
                    y += 14;
                    c.Location = new Point(x, y);
                    y += 30;
                }
                else
                {
                    c.Bounds = new Rectangle(x, y, w, c.Height);
                    y += c.Height + 4;
                }
            }
        }

        void RefreshSettingsPage()
        {
            if (_setServer == null) return;
            ServerConfigData s = ReadServerConfigData();
            _setServer.Description = s != null && s.IsConfigured()
                ? s.User + "@" + s.Host + ":" + (string.IsNullOrEmpty(s.SshPort) ? "22" : s.SshPort) + " · web en el puerto " + s.FrontendPort
                : "Sin configurar · IP, usuario, contraseña y puertos";
            PleskConfigData p = ReadPleskConfigData();
            _setPlesk.Description = p != null && p.IsConfigured()
                ? p.User + "@" + p.Host + " · " + p.RemotePath
                : "Sin configurar · host, usuario SSH y document root";
        }

        void OpenTerminalHere()
        {
            try
            {
                ProcessStartInfo wt = new ProcessStartInfo("wt.exe", "-d \"" + _root + "\"");
                wt.UseShellExecute = true;
                Process.Start(wt);
                return;
            }
            catch { }
            try
            {
                ProcessStartInfo ps = new ProcessStartInfo("powershell.exe");
                ps.WorkingDirectory = _root;
                ps.UseShellExecute = true;
                Process.Start(ps);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo abrir la terminal:\n" + ex.Message, "AppInWhats DevBuild", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // --- Diálogos (estilo ContentDialog) ---

        sealed class DialogParts
        {
            public Form Form;
            public Panel Body;
            public FluentButton Ok;
            public FluentButton Cancel;
            public Label Error;
            public int Width;
            public int Y;
        }

        const int FieldStep = 72;

        DialogParts CreateDialog(string caption, string heading, string description, int width)
        {
            DialogParts d = new DialogParts();
            d.Width = width;
            Form dlg = new Form();
            d.Form = dlg;
            dlg.Text = caption;
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.MaximizeBox = false;
            dlg.MinimizeBox = false;
            dlg.ShowInTaskbar = false;
            dlg.ShowIcon = false;
            dlg.BackColor = Theme.Card;
            dlg.ForeColor = Theme.TextPrimary;
            dlg.Font = Theme.Text(10.5f);
            dlg.HandleCreated += delegate { Native.UseDarkFrame(dlg.Handle, Theme.Dark); };

            Panel footer = new DoubleBufferedPanel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 80;
            footer.BackColor = Theme.Bg;
            footer.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.CardStroke, 1f))
                    e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };

            d.Ok = new FluentButton("Guardar", ButtonKind.Accent, null);
            d.Cancel = new FluentButton("Cancelar", ButtonKind.Standard, null);
            d.Cancel.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(d.Ok);
            footer.Controls.Add(d.Cancel);
            footer.Resize += delegate
            {
                int bw = (footer.ClientSize.Width - 48 - 8) / 2;
                d.Ok.Bounds = new Rectangle(24, 24, bw, 32);
                d.Cancel.Bounds = new Rectangle(24 + bw + 8, 24, bw, 32);
            };

            d.Body = new DoubleBufferedPanel();
            d.Body.Dock = DockStyle.Fill;
            d.Body.BackColor = Theme.Card;

            Label head = new Label();
            head.Text = heading;
            head.Font = Theme.Display(15f);
            head.ForeColor = Theme.TextPrimary;
            head.AutoSize = true;
            head.Location = new Point(22, 22);

            Label desc = new Label();
            desc.Text = description;
            desc.Font = Theme.Text(10.5f);
            desc.ForeColor = Theme.TextSecondary;
            desc.AutoSize = true;
            desc.MaximumSize = new Size(width - 48, 0);
            desc.Location = new Point(24, 62);

            d.Body.Controls.Add(head);
            d.Body.Controls.Add(desc);
            d.Y = 62 + desc.GetPreferredSize(new Size(width - 48, 0)).Height + 22;

            dlg.Controls.Add(d.Body);
            dlg.Controls.Add(footer);
            dlg.AcceptButton = d.Ok;
            dlg.CancelButton = d.Cancel;
            return d;
        }

        static FluentTextBox AddDialogField(DialogParts d, string label, string value, bool password, string cue, int x, int width, int y)
        {
            Label l = new Label();
            l.Text = label;
            l.Font = Theme.Text(10.5f);
            l.ForeColor = Theme.TextPrimary;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            FluentTextBox tb = new FluentTextBox(value, password, cue);
            tb.Bounds = new Rectangle(x, y + 26, width, 32);
            d.Body.Controls.Add(l);
            d.Body.Controls.Add(tb);
            return tb;
        }

        static void FinishDialog(DialogParts d)
        {
            d.Error = new Label();
            d.Error.AutoSize = false;
            d.Error.Location = new Point(24, d.Y - 6);
            d.Error.Size = new Size(d.Width - 48, 22);
            d.Error.Font = Theme.Text(9.75f);
            d.Error.ForeColor = Theme.Critical;
            d.Body.Controls.Add(d.Error);
            d.Form.ClientSize = new Size(d.Width, d.Y + 24 + 80);
        }

        static void FieldError(DialogParts d, FluentTextBox field, string message)
        {
            d.Error.Text = message;
            if (field != null)
            {
                field.Inner.Focus();
                field.Inner.SelectAll();
            }
        }

        bool ShowServerConfigDialog(bool firstTime)
        {
            ServerConfigData current = ReadServerConfigData() ?? new ServerConfigData();
            DialogParts d = CreateDialog(
                firstTime ? "Configurar servidor remoto" : "Ajustes · servidor remoto",
                firstTime ? "Conectar con el servidor Debian" : "Servidor remoto (SSH)",
                "La app se despliega y se arranca por SSH. IP, usuario y contraseña son obligatorios.",
                500);

            string hostVal = firstTime ? "" : (current.Host ?? "");
            string userVal = firstTime ? "" : (current.User ?? "");
            string passVal = firstTime ? "" : (current.IsPlaceholderPassword() ? "" : (current.Password ?? ""));
            string webVal = firstTime ? "" : (current.FrontendPort ?? "");
            string sshVal = firstTime ? "" : (current.SshPort ?? "");

            int full = d.Width - 48;
            int half = (full - 12) / 2;
            int y = d.Y;
            FluentTextBox tbHost = AddDialogField(d, "IP / host", hostVal, false, "p. ej. 192.168.1.20", 24, full, y); y += FieldStep;
            FluentTextBox tbUser = AddDialogField(d, "Usuario", userVal, false, "root", 24, full, y); y += FieldStep;
            FluentTextBox tbPass = AddDialogField(d, "Contraseña", passVal, true, "", 24, full, y); y += FieldStep;
            FluentTextBox tbWeb = AddDialogField(d, "Puerto web (salida)", webVal, false, "1231", 24, half, y);
            FluentTextBox tbSsh = AddDialogField(d, "Puerto SSH", sshVal, false, "22", 24 + half + 12, half, y); y += FieldStep;
            d.Y = y;
            FinishDialog(d);

            bool saved = false;
            d.Ok.Click += delegate
            {
                string host = (tbHost.Text ?? "").Trim();
                string user = (tbUser.Text ?? "").Trim();
                string pass = tbPass.Text ?? "";
                string web = (tbWeb.Text ?? "").Trim();
                string ssh = (tbSsh.Text ?? "").Trim();
                if (string.IsNullOrEmpty(host)) { FieldError(d, tbHost, "Indica la IP o el host del servidor."); return; }
                if (string.IsNullOrEmpty(user)) { FieldError(d, tbUser, "Indica el usuario SSH."); return; }
                if (string.IsNullOrEmpty(pass)) { FieldError(d, tbPass, "Indica la contraseña SSH."); return; }
                if (string.IsNullOrEmpty(web)) web = "1231";
                if (string.IsNullOrEmpty(ssh)) ssh = "22";
                if (!IsDigits(web)) { FieldError(d, tbWeb, "El puerto web debe ser un número."); return; }
                if (!IsDigits(ssh)) { FieldError(d, tbSsh, "El puerto SSH debe ser un número."); return; }
                current.Host = host;
                current.User = user;
                current.Password = pass;
                current.FrontendPort = web;
                current.SshPort = ssh;
                if (string.IsNullOrEmpty(current.RemoteRoot)) current.RemoteRoot = "/root/appinwhats";
                if (string.IsNullOrEmpty(current.BackendPort) || !IsDigits(current.BackendPort))
                    current.BackendPort = "3001";
                try
                {
                    WriteServerConfigData(current);
                    saved = true;
                    d.Form.DialogResult = DialogResult.OK;
                }
                catch (Exception ex)
                {
                    FieldError(d, null, "No se pudo guardar la configuración: " + ex.Message);
                }
            };
            d.Form.Shown += delegate { tbHost.Inner.Focus(); };
            using (d.Form) d.Form.ShowDialog(this);
            return saved;
        }

        bool ShowPleskConfigDialog(bool firstTime)
        {
            PleskConfigData current = ReadPleskConfigData() ?? new PleskConfigData();
            DialogParts d = CreateDialog(
                firstTime ? "Configurar subida a Plesk" : "Ajustes · Plesk",
                "Subida a Plesk (SFTP)",
                "Se compila el frontend y se sube dist al document root por SFTP. El File Manager web de Plesk no sirve para automatizar.",
                520);

            string hostVal = current.Host ?? "";
            string userVal = current.User ?? "";
            string passVal = current.IsPlaceholderPassword() ? "" : (current.Password ?? "");
            string portVal = string.IsNullOrEmpty(current.Port) ? "22" : current.Port;
            string pathVal = (current.RemotePath ?? "").IndexOf("TU_DOMINIO", StringComparison.OrdinalIgnoreCase) >= 0 ? "" : (current.RemotePath ?? "");

            int full = d.Width - 48;
            int portW = 110;
            int y = d.Y;
            FluentTextBox tbHost = AddDialogField(d, "IP / host de Plesk", hostVal, false, "p. ej. plesk.midominio.com", 24, full, y); y += FieldStep;
            FluentTextBox tbUser = AddDialogField(d, "Usuario SSH", userVal, false, "usuario del dominio", 24, full - portW - 12, y);
            FluentTextBox tbPort = AddDialogField(d, "Puerto SSH", portVal, false, "22", 24 + full - portW, portW, y); y += FieldStep;
            FluentTextBox tbPass = AddDialogField(d, "Contraseña SSH", passVal, true, "", 24, full, y); y += FieldStep;
            FluentTextBox tbPath = AddDialogField(d, "Document root (remotePath)", pathVal, false, "/var/www/vhosts/dominio.com/httpdocs", 24, full, y); y += FieldStep;
            d.Y = y;
            FinishDialog(d);

            bool saved = false;
            d.Ok.Click += delegate
            {
                string host = (tbHost.Text ?? "").Trim();
                string user = (tbUser.Text ?? "").Trim();
                string pass = tbPass.Text ?? "";
                string port = (tbPort.Text ?? "").Trim();
                string rpath = (tbPath.Text ?? "").Trim();
                if (string.IsNullOrEmpty(host)) { FieldError(d, tbHost, "Indica el host de Plesk."); return; }
                if (string.IsNullOrEmpty(user)) { FieldError(d, tbUser, "Indica el usuario SSH."); return; }
                if (string.IsNullOrEmpty(pass)) { FieldError(d, tbPass, "Indica la contraseña SSH."); return; }
                if (string.IsNullOrEmpty(rpath)) { FieldError(d, tbPath, "Indica el document root del dominio."); return; }
                if (string.IsNullOrEmpty(port)) port = "22";
                if (!IsDigits(port)) { FieldError(d, tbPort, "El puerto SSH debe ser un número."); return; }
                current.Host = host;
                current.User = user;
                current.Password = pass;
                current.Port = port;
                current.RemotePath = rpath.Replace("\\", "/");
                try
                {
                    WritePleskConfigData(current);
                    saved = true;
                    d.Form.DialogResult = DialogResult.OK;
                }
                catch (Exception ex)
                {
                    FieldError(d, null, "No se pudo guardar: " + ex.Message);
                }
            };
            d.Form.Shown += delegate { tbHost.Inner.Focus(); };
            using (d.Form) d.Form.ShowDialog(this);
            return saved;
        }

        // --- Terminal (cabecera y contenedor; la lógica git está en AppInWhatsLauncher.cs) ---

        sealed partial class TerminalView : Panel
        {
            readonly Color _accent;
            readonly string _folderPath;
            string _titleText;
            readonly DoubleBufferedPanel _head;
            readonly DoubleBufferedPanel _bodyHost;
            readonly RichTextBox _box;
            readonly FluentButton _btnBranch;
            readonly FluentButton _btnCopy;
            readonly FluentButton _btnClear;
            readonly FluentButton _btnFolder;
            bool _gitReady;
            string _currentBranch = "";
            const int MaxChars = 200000;
            const int Radius = 8;

            public Func<bool> BeforeBranchSwitch;
            public Action AfterBranchSwitchSuccess;

            public TerminalView(string title, Color accent, string folderPath)
            {
                _accent = accent;
                _folderPath = folderPath;
                _titleText = title;
                BackColor = Theme.Layer;

                _head = new DoubleBufferedPanel();
                _head.Dock = DockStyle.Top;
                _head.Height = 48;
                _head.BackColor = Theme.Card;
                _head.Paint += PaintHead;
                _head.Resize += delegate { LayoutHead(); };

                _btnBranch = new FluentButton("rama", ButtonKind.Standard, Glyphs.Branch);
                _btnBranch.Height = 28;
                _btnBranch.Font = Theme.Text(9.75f);
                _btnBranch.GlyphSize = 9.5f;
                _btnBranch.Visible = false;
                _btnBranch.Click += delegate { ShowBranchMenu(); };

                _btnCopy = MakeIconButton(Glyphs.Copy, "Copiar el registro");
                _btnCopy.Click += delegate { CopyLog(); };
                _btnClear = MakeIconButton(Glyphs.Delete, "Limpiar el registro");
                _btnClear.Click += delegate { Clear(); };
                _btnFolder = MakeIconButton(Glyphs.FolderOpen, "Abrir la carpeta");
                _btnFolder.Visible = !string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath);
                _btnFolder.Click += delegate { OpenFolder(_folderPath); };

                _head.Controls.Add(_btnBranch);
                _head.Controls.Add(_btnCopy);
                _head.Controls.Add(_btnClear);
                _head.Controls.Add(_btnFolder);

                _box = new RichTextBox();
                _box.Dock = DockStyle.Fill;
                _box.ReadOnly = true;
                _box.BorderStyle = BorderStyle.None;
                _box.BackColor = Theme.Card;
                _box.ForeColor = Theme.LogText;
                _box.Font = Theme.Mono(9.25f);
                _box.WordWrap = true;
                _box.DetectUrls = false;
                _box.ScrollBars = RichTextBoxScrollBars.Vertical;
                _box.HideSelection = false;
                _box.HandleCreated += delegate { Native.ThemeScrollbars(_box); };

                _bodyHost = new DoubleBufferedPanel();
                _bodyHost.Dock = DockStyle.Fill;
                _bodyHost.BackColor = Theme.Card;
                _bodyHost.Padding = new Padding(16, 8, 3, 12);
                _bodyHost.Paint += PaintBody;
                _bodyHost.Controls.Add(_box);

                Controls.Add(_bodyHost);
                Controls.Add(_head);
                LayoutHead();
                RefreshGitBranch();
            }

            void PaintHead(object sender, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Theme.Layer);
                Gfx.Card(g, new Rectangle(0, 0, _head.Width - 1, _head.Height + Radius * 2), Radius, Theme.Card, Theme.CardStroke);
                using (Pen pen = new Pen(Theme.Divider, 1f))
                    g.DrawLine(pen, 1, _head.Height - 1, _head.Width - 2, _head.Height - 1);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(_accent))
                    g.FillEllipse(b, 18, _head.Height / 2 - 4, 8, 8);
                int right = _btnBranch.Visible ? _btnBranch.Left : _btnCopy.Left;
                Gfx.TextLeft(g, _titleText, Theme.Strong(10.5f), new Rectangle(34, 0, Math.Max(1, right - 46), _head.Height - 1), Theme.TextPrimary);
            }

            void PaintBody(object sender, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Theme.Layer);
                Gfx.Card(g, new Rectangle(0, -Radius * 2, _bodyHost.Width - 1, _bodyHost.Height - 1 + Radius * 2), Radius, Theme.Card, Theme.CardStroke);
            }

            void LayoutHead()
            {
                if (_btnFolder == null || _head == null) return;
                int x = _head.Width - 10;
                FluentButton[] icons = new FluentButton[] { _btnFolder, _btnClear, _btnCopy };
                for (int i = 0; i < icons.Length; i++)
                {
                    if (!icons[i].Visible) continue;
                    x -= icons[i].Width;
                    icons[i].Location = new Point(x, (_head.Height - icons[i].Height) / 2);
                    x -= 2;
                }
                if (_btnBranch.Visible)
                    _btnBranch.Location = new Point(x - 8 - _btnBranch.Width, (_head.Height - _btnBranch.Height) / 2);
                _head.Invalidate();
            }

            public void SetTitle(string title)
            {
                _titleText = title;
                _head.Invalidate();
            }

            void CopyLog()
            {
                try
                {
                    if (_box.TextLength == 0) return;
                    Clipboard.SetText(_box.Text);
                    _btnCopy.Glyph = Glyphs.Check;
                    Timer t = new Timer();
                    t.Interval = 1200;
                    t.Tick += delegate { t.Stop(); t.Dispose(); _btnCopy.Glyph = Glyphs.Copy; };
                    t.Start();
                }
                catch { }
            }
        }
    }
}
