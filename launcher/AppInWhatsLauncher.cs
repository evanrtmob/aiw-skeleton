using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AppInWhats
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show(e.Exception.ToString(), "AppInWhats — error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                MessageBox.Show(Convert.ToString(e.ExceptionObject), "AppInWhats — error fatal", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            try
            {
                Application.Run(new LauncherForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "AppInWhats — error al abrir", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    sealed class LauncherForm : Form
    {
        static readonly Color Bg = Color.FromArgb(245, 246, 248);
        static readonly Color PanelBg = Color.FromArgb(255, 255, 255);
        static readonly Color Tile = Color.FromArgb(255, 255, 255);
        static readonly Color TileHover = Color.FromArgb(248, 249, 251);
        static readonly Color TextMain = Color.FromArgb(32, 36, 42);
        static readonly Color TextMuted = Color.FromArgb(110, 118, 128);
        static readonly Color Accent = Color.FromArgb(0, 150, 230);
        static readonly Color TitleBar = Color.FromArgb(255, 255, 255);
        static readonly Color Danger = Color.FromArgb(220, 70, 70);
        static readonly Color TermBg = Color.FromArgb(252, 252, 252);
        static readonly Color TermFg = Color.FromArgb(40, 40, 44);
        static readonly Color Border = Color.FromArgb(220, 224, 230);
        static readonly Color WindowFrame = Color.FromArgb(180, 186, 194);
        static readonly Color CaptionHover = Color.FromArgb(235, 238, 242);
        static readonly Color CloseHover = Color.FromArgb(232, 17, 35);

        readonly string _root;
        readonly string _sessionPath;
        readonly List<Process> _procs = new List<Process>();
        readonly object _procLock = new object();
        IntPtr _job = IntPtr.Zero;

        bool _maximized;
        Rectangle _restoreBounds;
        bool _running;
        bool _closing;
        string _modeTitle = "";
        bool _modeDual;
        bool _modeDocker;
        bool _modeRemote;
        bool _modeOpt;

        Panel _titleBar;
        Panel _titleSep;
        CaptionButton _capMin;
        CaptionButton _capMax;
        CaptionButton _capClose;
        Panel _homePanel;
        Panel _homeContent;
        Panel _tileGrid;
        readonly List<Control> _homeTiles = new List<Control>();
        Panel _resumeBanner;
        Label _resumeLabel;
        Panel _sessionPanel;
        Panel _toolbar;
        SplitContainer _split;
        TerminalView _termBack;
        TerminalView _termFront;
        Label _sessionTitle;
        Label _status;
        string _modeId = "";
        RoundedButton _btnStop;
        RoundedButton _btnStart;
        RoundedButton _btnHome;
        RoundedButton _btnOpen;
        RoundedButton _btnBuild;
        Process _buildProc;
        Panel _statsBar;
        StatsChip _statsBack;
        StatsChip _statsFront;
        StatsChip _statsTotal;
        StatsChip _statsOther;
        System.Windows.Forms.Timer _watchTimer;
        readonly Dictionary<int, TimeSpan> _prevCpu = new Dictionary<int, TimeSpan>();
        DateTime _prevCpuSample = DateTime.MinValue;

        public LauncherForm()
        {
            _root = ResolveRoot();
            _sessionPath = Path.Combine(_root, ".aiw-launcher-session");

            Text = "AppInWhats DevBuild";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1100, 720);
            MinimumSize = new Size(720, 480);
            BackColor = WindowFrame;
            ForeColor = TextMain;
            Font = new Font("Segoe UI", 10f);
            DoubleBuffered = true;
            Padding = new Padding(1);

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            InitKillOnCloseJob();
            BuildChrome();
            BuildHome();
            BuildSession();
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

        // Sombra nativa de Windows (ventanas borderless)
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            const int HTCLIENT = 1;
            const int HTLEFT = 10;
            const int HTRIGHT = 11;
            const int HTTOP = 12;
            const int HTTOPLEFT = 13;
            const int HTTOPRIGHT = 14;
            const int HTBOTTOM = 15;
            const int HTBOTTOMLEFT = 16;
            const int HTBOTTOMRIGHT = 17;

            if (m.Msg == WM_NCHITTEST && !_maximized)
            {
                // Prioridad a bordes/esquinas (sin base primero: los hijos no bloquean el grip)
                int lp = m.LParam.ToInt32();
                Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int g = 12;
                bool left = p.X <= g;
                bool right = p.X >= ClientSize.Width - g;
                bool top = p.Y <= g;
                bool bottom = p.Y >= ClientSize.Height - g;

                if (top && left) { m.Result = (IntPtr)HTTOPLEFT; return; }
                if (top && right) { m.Result = (IntPtr)HTTOPRIGHT; return; }
                if (bottom && left) { m.Result = (IntPtr)HTBOTTOMLEFT; return; }
                if (bottom && right) { m.Result = (IntPtr)HTBOTTOMRIGHT; return; }
                if (left) { m.Result = (IntPtr)HTLEFT; return; }
                if (right) { m.Result = (IntPtr)HTRIGHT; return; }
                if (top) { m.Result = (IntPtr)HTTOP; return; }
                if (bottom) { m.Result = (IntPtr)HTBOTTOM; return; }

                m.Result = (IntPtr)HTCLIENT;
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle r = ClientRectangle;
            if (r.Width < 8 || r.Height < 8) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int radius = _maximized ? 0 : 26;

            if (radius > 0)
            {
                using (GraphicsPath path = RoundRect(new Rectangle(0, 0, r.Width - 1, r.Height - 1), radius))
                using (Pen outer = new Pen(WindowFrame, 1.5f))
                using (Pen inner = new Pen(Color.FromArgb(255, 255, 255), 1))
                {
                    e.Graphics.DrawPath(outer, path);
                    if (Padding.All > 0)
                    {
                        using (GraphicsPath path2 = RoundRect(new Rectangle(1, 1, r.Width - 3, r.Height - 3), Math.Max(4, radius - 2)))
                            e.Graphics.DrawPath(inner, path2);
                    }
                }
            }
            else
            {
                using (Pen outer = new Pen(WindowFrame, 1))
                    e.Graphics.DrawRectangle(outer, 0, 0, r.Width - 1, r.Height - 1);
            }
        }

        void ApplyWindowFrame()
        {
            if (_maximized)
            {
                Padding = new Padding(0);
                BackColor = Bg;
                Region = null;
            }
            else
            {
                Padding = new Padding(1);
                BackColor = WindowFrame;
                // Sin Region redondeada: cortaba las esquinas y bloqueaba el resize
                Region = null;
                TryEnableWin11Corners();
            }
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            TryEnableWin11Corners();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            LayoutHomeTiles();
            LayoutStatsBar();
            LayoutToolbarButtons();
            if (_sessionPanel != null && _sessionPanel.Visible)
            {
                try
                {
                    if (_split != null && !_split.Panel1Collapsed && _split.Width > 400)
                        SafeConfigureSplit(true);
                }
                catch { }
            }
        }

        void TryEnableWin11Corners()
        {
            try
            {
                if (!IsHandleCreated) return;
                int preference = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int));
            }
            catch { }
        }

        void ApplyRoundedRegion()
        {
            // Desactivado a propósito: CreateRoundRectRgn elimina píxeles de esquina
            // y evita HTTOPLEFT/HTTOPRIGHT. El redondeo visual va por DWM + OnPaint.
            Region = null;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _closing = true;
            try
            {
                if (_modeDocker)
                    TryDockerDown();
                StopAll(false);
                FreeDevPorts();
            }
            catch { }
            try
            {
                if (_job != IntPtr.Zero)
                {
                    CloseHandle(_job); // KILL_ON_JOB_CLOSE mata hijos restantes
                    _job = IntPtr.Zero;
                }
            }
            catch { }
            ClearSessionFile();
            base.OnFormClosing(e);
        }

        static string ResolveRoot()
        {
            string start = Application.StartupPath;
            if (File.Exists(Path.Combine(start, "start.ps1")))
                return start;
            string parent = Path.GetFullPath(Path.Combine(start, ".."));
            if (File.Exists(Path.Combine(parent, "start.ps1")))
                return parent;
            return start;
        }

        void BuildChrome()
        {
            _titleBar = new Panel();
            _titleBar.Dock = DockStyle.Top;
            _titleBar.Height = 40;
            _titleBar.BackColor = TitleBar;
            _titleBar.MouseDown += BeginWindowDrag;
            _titleBar.DoubleClick += TitleBarDoubleClick;
            _titleBar.Resize += delegate { LayoutCaptionButtons(); };

            // Línea separadora dedicada (no la tapán los botones de caption)
            _titleSep = new Panel();
            _titleSep.Dock = DockStyle.Bottom;
            _titleSep.Height = 1;
            _titleSep.BackColor = Border;

            PictureBox brandIcon = new PictureBox();
            brandIcon.Size = new Size(18, 18);
            brandIcon.Location = new Point(14, 11);
            brandIcon.SizeMode = PictureBoxSizeMode.Zoom;
            brandIcon.BackColor = Color.Transparent;
            try { if (Icon != null) brandIcon.Image = Icon.ToBitmap(); } catch { }
            brandIcon.MouseDown += BeginWindowDrag;
            brandIcon.DoubleClick += TitleBarDoubleClick;

            Label brand = MakeDragLabel("AppInWhats DevBuild", new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold), TextMain, new Point(38, 10));
            Label hint = MakeDragLabel("desarrollo", new Font("Segoe UI", 9f), TextMuted, new Point(230, 11));
            brand.DoubleClick += TitleBarDoubleClick;
            hint.DoubleClick += TitleBarDoubleClick;

            const int capW = 46;
            _capMin = new CaptionButton(CaptionKind.Minimize, new Action(MinimizeWindow));
            _capMax = new CaptionButton(CaptionKind.Maximize, new Action(ToggleMaximize));
            _capClose = new CaptionButton(CaptionKind.Close, new Action(Close));
            _capMin.Width = capW;
            _capMax.Width = capW;
            _capClose.Width = capW;

            _titleBar.Controls.Add(_titleSep);
            _titleBar.Controls.Add(brandIcon);
            _titleBar.Controls.Add(brand);
            _titleBar.Controls.Add(hint);
            _titleBar.Controls.Add(_capMin);
            _titleBar.Controls.Add(_capMax);
            _titleBar.Controls.Add(_capClose);
            _titleSep.BringToFront();
            Controls.Add(_titleBar);
            LayoutCaptionButtons();

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 40;
            footer.BackColor = PanelBg;
            footer.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Border, 1))
                    e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };

            _status = new Label();
            _status.Text = _root;
            _status.AutoEllipsis = true;
            _status.ForeColor = TextMuted;
            _status.Font = new Font("Segoe UI", 8f);
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Padding = new Padding(16, 0, 16, 0);
            footer.Controls.Add(_status);
            Controls.Add(footer);
        }

        void LayoutCaptionButtons()
        {
            if (_titleBar == null || _capClose == null) return;
            int w = _capClose.Width;
            int h = Math.Max(1, _titleBar.Height - (_titleSep != null ? _titleSep.Height : 1));
            _capClose.Size = new Size(w, h);
            _capMax.Size = new Size(w, h);
            _capMin.Size = new Size(w, h);
            _capClose.Location = new Point(_titleBar.Width - w, 0);
            _capMax.Location = new Point(_titleBar.Width - w * 2, 0);
            _capMin.Location = new Point(_titleBar.Width - w * 3, 0);
            if (_titleSep != null) _titleSep.BringToFront();
            _capClose.BringToFront();
            _capMax.BringToFront();
            _capMin.BringToFront();
            if (_titleSep != null) _titleSep.BringToFront();
        }

        void BuildHome()
        {
            _homePanel = new DoubleBufferedPanel();
            _homePanel.Dock = DockStyle.Fill;
            _homePanel.BackColor = Bg;

            _resumeBanner = new Panel();
            _resumeBanner.Dock = DockStyle.Top;
            _resumeBanner.Height = 56;
            _resumeBanner.BackColor = Color.FromArgb(255, 244, 229);
            _resumeBanner.Visible = false;
            _resumeBanner.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Color.FromArgb(245, 158, 11), 1))
                    e.Graphics.DrawLine(pen, 0, _resumeBanner.Height - 1, _resumeBanner.Width, _resumeBanner.Height - 1);
            };

            _resumeLabel = new Label();
            _resumeLabel.AutoSize = false;
            _resumeLabel.Location = new Point(20, 10);
            _resumeLabel.Size = new Size(620, 36);
            _resumeLabel.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            _resumeLabel.ForeColor = Color.FromArgb(146, 64, 14);
            _resumeLabel.BackColor = Color.Transparent;
            _resumeLabel.TextAlign = ContentAlignment.MiddleLeft;

            RoundedButton btnResume = MakeToolButton("Abrir sesión", Accent, Color.White);
            btnResume.Size = new Size(120, 34);
            btnResume.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnResume.Click += delegate { ResumePreviousSession(); };

            RoundedButton btnKillPrev = MakeToolButton("Detener", Danger, Color.White);
            btnKillPrev.Size = new Size(100, 34);
            btnKillPrev.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnKillPrev.Click += delegate
            {
                KillSessionFromFile();
                HideResumeBanner();
                _status.Text = "Sesión anterior detenida";
                _status.ForeColor = Accent;
            };

            _resumeBanner.Controls.Add(_resumeLabel);
            _resumeBanner.Controls.Add(btnResume);
            _resumeBanner.Controls.Add(btnKillPrev);
            _resumeBanner.Resize += delegate
            {
                btnKillPrev.Location = new Point(_resumeBanner.Width - btnKillPrev.Width - 16, 12);
                btnResume.Location = new Point(btnKillPrev.Left - btnResume.Width - 8, 12);
                _resumeLabel.Width = Math.Max(200, btnResume.Left - 32);
            };

            Label intro = new Label();
            intro.Text = "Elige un modo";
            intro.Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold);
            intro.ForeColor = TextMain;
            intro.AutoSize = true;
            intro.Location = new Point(28, 16);
            intro.BackColor = Color.Transparent;

            Label introSub = new Label();
            introSub.Text = "Las terminales de frontend y backend se abrirán en esta ventana";
            introSub.Font = new Font("Segoe UI", 9.5f);
            introSub.ForeColor = TextMuted;
            introSub.AutoSize = true;
            introSub.Location = new Point(30, 52);
            introSub.BackColor = Color.Transparent;

            _tileGrid = new DoubleBufferedPanel();
            _tileGrid.Location = new Point(24, 90);
            _tileGrid.Size = new Size(1040, 400);
            _tileGrid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _tileGrid.BackColor = Bg;
            _tileGrid.AutoScroll = true;

            TileInfo[] tiles = new TileInfo[]
            {
                new TileInfo("Native", "Local", "Sin Docker · ligero", Color.FromArgb(0, 180, 120), "N", new Action(StartNativeLocal)),
                new TileInfo("Native", "Remote", "API remota", Color.FromArgb(0, 150, 230), "R", new Action(StartNativeRemote)),
                new TileInfo("Docker", "Local", "Stack completo", Color.FromArgb(80, 140, 220), "D", new Action(StartDockerLocal)),
                new TileInfo("Docker", "Remote", "Front + API remota", Color.FromArgb(230, 160, 40), "⇄", new Action(StartDockerRemote)),
                new TileInfo("Optimizado", "Docker", "Menos CPU/RAM", Color.FromArgb(40, 170, 160), "⚡", new Action(StartOptLocal)),
                new TileInfo("Optimizado", "Remote", "Opt + remoto", Color.FromArgb(70, 140, 210), "⚡", new Action(StartOptRemote)),
                new TileInfo("Instalar", "npm", "Dependencias", Color.FromArgb(220, 90, 110), "↓", new Action(StartInstall)),
            };

            const int tw = 168;
            const int th = 168;
            _homeTiles.Clear();
            for (int i = 0; i < tiles.Length; i++)
            {
                Control tile = CreateTile(tiles[i], tw, th);
                _tileGrid.Controls.Add(tile);
                _homeTiles.Add(tile);
            }

            _homeContent = new DoubleBufferedPanel();
            _homeContent.Dock = DockStyle.Fill;
            _homeContent.BackColor = Bg;
            _homeContent.Controls.Add(intro);
            _homeContent.Controls.Add(introSub);
            _homeContent.Controls.Add(_tileGrid);
            _homeContent.Resize += delegate { LayoutHomeTiles(); };

            _homePanel.Controls.Add(_homeContent);
            _homePanel.Controls.Add(_resumeBanner);
            Controls.Add(_homePanel);
            _homePanel.BringToFront();
            LayoutHomeTiles();
        }

        void LayoutHomeTiles()
        {
            if (_tileGrid == null || _homeContent == null || _homeTiles.Count == 0) return;

            const int tw = 168;
            const int th = 168;
            const int gap = 18;
            const int padX = 28;
            const int top = 90;

            int availW = Math.Max(200, _homeContent.ClientSize.Width - padX * 2);
            int availH = Math.Max(160, _homeContent.ClientSize.Height - top - 16);

            _tileGrid.Location = new Point(padX, top);
            _tileGrid.Size = new Size(availW, availH);

            int cols = Math.Max(1, (availW + gap) / (tw + gap));
            if (cols > _homeTiles.Count) cols = _homeTiles.Count;

            for (int i = 0; i < _homeTiles.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                _homeTiles[i].Location = new Point(col * (tw + gap), row * (th + gap));
            }
        }

        void BuildSession()
        {
            _sessionPanel = new Panel();
            _sessionPanel.Dock = DockStyle.Fill;
            _sessionPanel.BackColor = Bg;
            _sessionPanel.Visible = false;

            _toolbar = new Panel();
            _toolbar.Dock = DockStyle.Top;
            _toolbar.Height = 56;
            _toolbar.BackColor = PanelBg;
            _toolbar.Padding = new Padding(16, 10, 16, 10);

            _sessionTitle = new Label();
            _sessionTitle.Text = "Sesión";
            _sessionTitle.Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);
            _sessionTitle.ForeColor = TextMain;
            _sessionTitle.AutoSize = true;
            _sessionTitle.Location = new Point(20, 16);
            _sessionTitle.BackColor = Color.Transparent;

            _btnStop = MakeToolButton("Parar todo", Danger, Color.White);
            _btnStop.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnStop.Click += delegate { StopAll(true); };

            _btnStart = MakeToolButton("Iniciar aplicación", Accent, Color.White);
            _btnStart.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnStart.Enabled = false;
            _btnStart.Click += delegate { RestartCurrentMode(); };

            _btnOpen = MakeToolButton("Abrir app", Color.FromArgb(70, 130, 180), Color.White);
            _btnOpen.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnOpen.Click += delegate
            {
                try { Process.Start("http://localhost:50080"); }
                catch { }
            };

            _btnBuild = MakeToolButton("Compilar front", Color.FromArgb(90, 100, 120), Color.White);
            _btnBuild.Size = new Size(132, 34);
            _btnBuild.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnBuild.Click += delegate { StartFrontendProductionBuild(); };

            _btnHome = MakeToolButton("Inicio", Color.FromArgb(240, 240, 240), TextMain);
            _btnHome.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnHome.Enabled = false;
            _btnHome.Click += delegate
            {
                if (_running)
                {
                    DialogResult r = MessageBox.Show(
                        "Hay procesos en marcha. ¿Pararlos y volver al inicio?",
                        "AppInWhats", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r != DialogResult.Yes) return;
                    StopAll(false);
                }
                ShowHome();
            };

            _toolbar.Controls.Add(_sessionTitle);
            _toolbar.Controls.Add(_btnStop);
            _toolbar.Controls.Add(_btnStart);
            _toolbar.Controls.Add(_btnOpen);
            _toolbar.Controls.Add(_btnBuild);
            _toolbar.Controls.Add(_btnHome);
            _toolbar.Resize += delegate { LayoutToolbarButtons(); };

            _statsBar = new DoubleBufferedPanel();
            _statsBar.Dock = DockStyle.Top;
            _statsBar.Height = 48;
            _statsBar.BackColor = Color.FromArgb(250, 251, 253);
            _statsBar.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Border, 1))
                    e.Graphics.DrawLine(pen, 0, _statsBar.Height - 1, _statsBar.Width, _statsBar.Height - 1);
            };

            _statsBack = MakeStatsChip("Backend", Color.FromArgb(124, 58, 237));
            _statsFront = MakeStatsChip("Frontend", Accent);
            _statsOther = MakeStatsChip("Otros", Color.FromArgb(100, 116, 139));
            _statsTotal = MakeStatsChip("Total", TextMain);

            _statsBar.Controls.Add(_statsBack);
            _statsBar.Controls.Add(_statsFront);
            _statsBar.Controls.Add(_statsOther);
            _statsBar.Controls.Add(_statsTotal);
            _statsBar.Resize += delegate { LayoutStatsBar(); };

            _split = new SplitContainer();
            _split.Dock = DockStyle.Fill;
            _split.SplitterWidth = 6;
            _split.BackColor = Border;

            _termBack = new TerminalView("Backend", Color.FromArgb(124, 58, 237), Path.Combine(_root, "backend"));
            _termFront = new TerminalView("Frontend", Accent, Path.Combine(_root, "frontend"));
            _termBack.BeforeBranchSwitch = StopForBranchSwitch;
            _termFront.BeforeBranchSwitch = StopForBranchSwitch;
            _termBack.AfterBranchSwitchSuccess = RestartAfterBranchSwitch;
            _termFront.AfterBranchSwitchSuccess = RestartAfterBranchSwitch;
            _termBack.Dock = DockStyle.Fill;
            _termFront.Dock = DockStyle.Fill;
            _split.Panel1.Controls.Add(_termBack);
            _split.Panel2.Controls.Add(_termFront);

            // orden Dock: Fill primero, luego Top (stats), luego Top (toolbar)
            _sessionPanel.Controls.Add(_split);
            _sessionPanel.Controls.Add(_statsBar);
            _sessionPanel.Controls.Add(_toolbar);
            Controls.Add(_sessionPanel);
            LayoutStatsBar();
        }

        StatsChip MakeStatsChip(string role, Color accent)
        {
            StatsChip chip = new StatsChip(role, accent);
            chip.Size = new Size(220, 32);
            return chip;
        }

        void LayoutStatsBar()
        {
            if (_statsBar == null || _statsBack == null) return;
            int gap = 8;
            int x = 12;
            int inner = Math.Max(200, _statsBar.ClientSize.Width - 24);
            int w = Math.Max(140, (inner - gap * 3) / 4);
            StatsChip[] chips = new StatsChip[] { _statsBack, _statsFront, _statsOther, _statsTotal };
            for (int i = 0; i < chips.Length; i++)
            {
                chips[i].Size = new Size(w, 32);
                chips[i].Location = new Point(x, 8);
                x += w + gap;
            }
            _statsBar.Visible = true;
        }

        void SafeConfigureSplit(bool dual)
        {
            try
            {
                _sessionPanel.PerformLayout();
                _split.PerformLayout();

                int w = _split.Width;
                if (w < 100)
                    w = Math.Max(100, ClientSize.Width - 20);

                if (dual)
                {
                    _split.Panel1Collapsed = false;
                    // márgenes seguros respecto al ancho real
                    int min = Math.Max(80, Math.Min(200, (w - _split.SplitterWidth) / 4));
                    _split.Panel1MinSize = min;
                    _split.Panel2MinSize = min;
                    int dist = w / 2;
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

        void LayoutToolbarButtons()
        {
            if (_toolbar == null || _btnStop == null) return;
            _btnStop.Location = new Point(_toolbar.Width - _btnStop.Width - 20, 12);
            _btnStart.Location = new Point(_btnStop.Left - _btnStart.Width - 10, 12);
            _btnOpen.Location = new Point(_btnStart.Left - _btnOpen.Width - 10, 12);
            _btnBuild.Location = new Point(_btnOpen.Left - _btnBuild.Width - 10, 12);
            _btnHome.Location = new Point(_btnBuild.Left - _btnHome.Width - 10, 12);
        }

        RoundedButton MakeToolButton(string text, Color bg, Color fg)
        {
            RoundedButton b = new RoundedButton(text, bg, fg);
            b.Size = new Size(Math.Max(108, TextRenderer.MeasureText(text, b.Font).Width + 28), 34);
            return b;
        }

        void ShowHome()
        {
            _sessionPanel.Visible = false;
            _homePanel.Visible = true;
            _homePanel.BringToFront();
            _status.Text = _root;
            _status.ForeColor = TextMuted;
        }

        void ShowSession(string title, bool dual)
        {
            _homePanel.Visible = false;
            _sessionPanel.Visible = true;
            _sessionPanel.BringToFront();
            _sessionTitle.Text = title;
            _termBack.Clear();
            _termFront.Clear();
            _btnHome.Enabled = false;
            _btnStop.Enabled = true;
            if (_btnStart != null) _btnStart.Enabled = false;

            if (dual)
            {
                _termBack.SetTitle("Backend");
                _termFront.SetTitle("Frontend");
            }
            else
            {
                _termFront.SetTitle(title);
            }

            SafeConfigureSplit(dual);
            LayoutToolbarButtons();
            LayoutStatsBar();
            ResetStatsDisplay();
            if (_termBack != null) _termBack.RefreshGitBranch();
            if (_termFront != null) _termFront.RefreshGitBranch();
            if (_statsBar != null)
            {
                _statsBar.Visible = true;
                _statsBar.Invalidate(true);
            }
        }

        void ResetStatsDisplay()
        {
            if (_statsBack == null) return;
            _statsBack.SetStatsText("Backend  ·  —");
            _statsFront.SetStatsText("Frontend  ·  —");
            _statsOther.SetStatsText("Otros  ·  —");
            _statsTotal.SetStatsText("Total  ·  —");
            _prevCpu.Clear();
            _prevCpuSample = DateTime.MinValue;
        }

        void SetRunning(bool running)
        {
            _running = running;
            _btnHome.Enabled = !running;
            _btnStop.Enabled = running;
            if (_btnStart != null)
                _btnStart.Enabled = !running && !string.IsNullOrEmpty(_modeId);
            _status.ForeColor = running ? Accent : TextMuted;
            _status.Text = running ? "Ejecutando…  ·  http://localhost:50080" : "Detenido";
            if (!running && !_closing)
            {
                ClearSessionFile();
                ResetStatsDisplay();
            }
        }

        void RestartCurrentMode()
        {
            if (_running)
            {
                MessageBox.Show("La sesión ya está en marcha.", "AppInWhats DevBuild",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (string.IsNullOrEmpty(_modeId))
            {
                MessageBox.Show("No hay un modo seleccionado para reiniciar.", "AppInWhats DevBuild",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            switch (_modeId)
            {
                case "native-local": StartNativeLocal(); break;
                case "native-remote": StartNativeRemote(); break;
                case "docker-local": StartDockerLocal(); break;
                case "docker-remote": StartDockerRemote(); break;
                case "opt-local": StartOptLocal(); break;
                case "opt-remote": StartOptRemote(); break;
                case "install": StartInstall(); break;
                default:
                    MessageBox.Show("Modo desconocido: " + _modeId, "AppInWhats DevBuild",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
            }
        }

        bool StopForBranchSwitch()
        {
            if (!_running) return false;
            if (_termBack != null) _termBack.Append("\r\n■ Parando para cambiar de rama…\r\n", TextMuted);
            if (_termFront != null) _termFront.Append("\r\n■ Parando para cambiar de rama…\r\n", TextMuted);
            StopAll(false);
            Application.DoEvents();
            System.Threading.Thread.Sleep(500);
            return true;
        }

        void RestartAfterBranchSwitch()
        {
            if (string.IsNullOrEmpty(_modeId) || _running) return;
            RestartCurrentMode();
        }

        bool EnsureCanStart()
        {
            SessionState pending = ReadSessionFile();
            bool busy = (pending != null && pending.HasLiveProcesses()) || IsPortInUse(3001) || IsPortInUse(50080);
            if (busy)
            {
                string name = (pending != null && !string.IsNullOrEmpty(pending.Title)) ? pending.Title : "sesión anterior";
                DialogResult r = MessageBox.Show(
                    "Todavía hay una sesión activa («" + name + "»).\n\n¿Detenerla y liberar los puertos 3001/50080?",
                    "AppInWhats", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return false;
                KillSessionFromFile();
                HideResumeBanner();
            }
            if (_running)
            {
                DialogResult r = MessageBox.Show(
                    "Ya hay procesos en marcha. ¿Pararlos y continuar?",
                    "AppInWhats", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return false;
                StopAll(false);
            }
            return true;
        }

        void BeginMode(string title, bool dual, bool docker, bool remote, bool opt, string modeId)
        {
            _modeTitle = title;
            _modeDual = dual;
            _modeDocker = docker;
            _modeRemote = remote;
            _modeOpt = opt;
            _modeId = modeId;
            HideResumeBanner();
            ShowSession(title, dual);
            SetRunning(true);
            SaveSessionFile();
        }

        bool IsPortInUse(int port)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                psi.Arguments = "/d /c netstat -ano -p tcp | findstr \":" + port + "\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                Process p = Process.Start(psi);
                if (p == null) return false;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                if (string.IsNullOrEmpty(output)) return false;
                string[] lines = output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].ToUpperInvariant();
                    if (line.Contains("LISTENING") && line.Contains(":" + port))
                        return true;
                }
            }
            catch { }
            return false;
        }

        List<int> GetPidsListeningOnPort(int port)
        {
            List<int> result = new List<int>();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                psi.Arguments = "/d /c netstat -ano -p tcp | findstr \":" + port + "\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                Process p = Process.Start(psi);
                if (p == null) return result;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                if (string.IsNullOrEmpty(output)) return result;

                string[] lines = output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.IndexOf("LISTENING", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (line.IndexOf(":" + port, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5) continue;
                    int pid;
                    if (int.TryParse(parts[parts.Length - 1], out pid) && pid > 0 && !result.Contains(pid))
                        result.Add(pid);
                }
            }
            catch { }
            return result;
        }

        void KillPidTree(int pid)
        {
            try
            {
                ProcessStartInfo kill = new ProcessStartInfo();
                kill.FileName = "taskkill.exe";
                kill.Arguments = "/PID " + pid + " /T /F";
                kill.CreateNoWindow = true;
                kill.UseShellExecute = false;
                Process kp = Process.Start(kill);
                if (kp != null) kp.WaitForExit(4000);
            }
            catch { }
        }

        void FreeDevPorts()
        {
            List<int> all = new List<int>();
            List<int> a = GetPidsListeningOnPort(3001);
            List<int> b = GetPidsListeningOnPort(50080);
            for (int i = 0; i < a.Count; i++) if (!all.Contains(a[i])) all.Add(a[i]);
            for (int i = 0; i < b.Count; i++) if (!all.Contains(b[i])) all.Add(b[i]);
            for (int i = 0; i < all.Count; i++)
                KillPidTree(all[i]);

            // node/npx huérfanos del proyecto aunque no escuchen ya
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.Arguments =
                    "-NoProfile -Command \"Get-CimInstance Win32_Process -Filter \\\"Name='node.exe'\\\" -ErrorAction SilentlyContinue | " +
                    "Where-Object { $_.CommandLine -match 'AppInWhats\\\\\\\\(frontend|backend)|ng serve|ts-node scr|nodemon' } | " +
                    "ForEach-Object { taskkill /PID $_.ProcessId /T /F 2>$null }\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process p = Process.Start(psi);
                if (p != null) p.WaitForExit(8000);
            }
            catch { }
        }

        // --- Starts ---

        void StartNativeLocal()
        {
            if (!EnsureCanStart() || !EnsureNpx()) return;
            FreeDevPorts();
            BeginMode("Native · Local", true, false, false, false, "native-local");

            string backend = Path.Combine(_root, "backend");
            string frontend = Path.Combine(_root, "frontend");
            string cfg = Path.Combine(backend, "config.cfg");
            string proxy = Path.Combine(frontend, "proxy.conf.native.json");

            if (!File.Exists(cfg))
            {
                _termBack.Append("Falta backend/config.cfg\r\n", Color.FromArgb(255, 120, 120));
                SetRunning(false);
                return;
            }

            Dictionary<string, string> beEnv = new Dictionary<string, string>();
            beEnv["AIW_CONFIG_CFG"] = cfg;
            beEnv["PORT"] = "3001";
            beEnv["NODE_OPTIONS"] = "--max-old-space-size=768";
            beEnv["FORCE_COLOR"] = "0";

            StartTracked(
                "Backend",
                FindNpx(),
                "--yes nodemon --watch scr --ext ts --exec \"npx --yes ts-node scr/index.ts\"",
                backend,
                beEnv,
                _termBack);

            Dictionary<string, string> feEnv = new Dictionary<string, string>();
            feEnv["NODE_OPTIONS"] = "--max-old-space-size=4096";
            feEnv["NG_CLI_ANALYTICS"] = "false";
            feEnv["FORCE_COLOR"] = "0";

            StartTracked(
                "Frontend",
                FindNpx(),
                "--yes ng serve --host 0.0.0.0 --port 50080 --proxy-config \"" + proxy + "\"",
                frontend,
                feEnv,
                _termFront);

            _termBack.Append("→ http://127.0.0.1:3001\r\n", Accent);
            _termFront.Append("→ http://localhost:50080\r\n", Accent);
        }

        void StartNativeRemote()
        {
            if (!EnsureCanStart() || !EnsureNpx()) return;
            FreeDevPorts();
            BeginMode("Native · Remote", false, false, true, false, "native-remote");

            string frontend = Path.Combine(_root, "frontend");
            string proxy = Path.Combine(frontend, "proxy.conf.native.remote.json");

            _termFront.Append("API remota: https://desarrollo.appinwhats.com\r\n", Color.FromArgb(56, 189, 248));
            _termFront.Append("(sin backend local)\r\n\r\n", TextMuted);

            Dictionary<string, string> feEnv = new Dictionary<string, string>();
            feEnv["NODE_OPTIONS"] = "--max-old-space-size=4096";
            feEnv["NG_CLI_ANALYTICS"] = "false";
            feEnv["FORCE_COLOR"] = "0";

            StartTracked(
                "Frontend",
                FindNpx(),
                "--yes ng serve --host 0.0.0.0 --port 50080 --proxy-config \"" + proxy + "\"",
                frontend,
                feEnv,
                _termFront);
        }

        void StartDockerLocal()
        {
            if (!EnsureCanStart()) return;
            BeginMode("Docker · Local", false, true, false, false, "docker-local");
            StartDockerCompose(false, false);
        }

        void StartDockerRemote()
        {
            if (!EnsureCanStart()) return;
            BeginMode("Docker · Remote", false, true, true, false, "docker-remote");
            StartDockerCompose(true, false);
        }

        void StartOptLocal()
        {
            if (!EnsureCanStart()) return;
            BeginMode("Docker · Optimizado", false, true, false, true, "opt-local");
            StartDockerCompose(false, true);
        }

        void StartOptRemote()
        {
            if (!EnsureCanStart()) return;
            BeginMode("Docker · Optimizado + Remote", false, true, true, true, "opt-remote");
            StartDockerCompose(true, true);
        }

        void StartDockerCompose(bool remote, bool optimized)
        {
            StringBuilder args = new StringBuilder();
            args.Append("compose -f docker-compose.yml -f docker-compose.override.yml");
            if (remote)
                args.Append(" -f docker-compose.remote.yml");
            if (optimized)
                args.Append(" -f docker-compose.optimized.yml");
            args.Append(" up");
            if (optimized)
                args.Append(" --no-build");
            if (remote)
                args.Append(" --scale backend=0");

            Dictionary<string, string> env = new Dictionary<string, string>();
            if (optimized)
            {
                env["DOCKER_BUILDKIT"] = "1";
                env["COMPOSE_DOCKER_CLI_BUILD"] = "1";
                env["COMPOSE_BAKE"] = "true";
            }

            _termFront.Append("docker " + args + "\r\n\r\n", TextMuted);
            StartTracked("Docker", "docker", args.ToString(), _root, env, _termFront);
        }

        void StartInstall()
        {
            if (!EnsureCanStart() || !EnsureNpx()) return;
            BeginMode("Instalar dependencias", false, false, false, false, "install");

            string fe = Path.Combine(_root, "frontend");
            string be = Path.Combine(_root, "backend");
            string cmd =
                "npm install --legacy-peer-deps --prefix \"" + fe + "\" && " +
                "npm install --legacy-peer-deps --prefix \"" + be + "\" && " +
                "echo. && echo Dependencias instaladas.";

            StartTracked("Install", "cmd.exe", "/c " + cmd, _root, new Dictionary<string, string>(), _termFront);
        }

        void StartFrontendProductionBuild()
        {
            if (!EnsureNpx()) return;

            try
            {
                if (_buildProc != null && !_buildProc.HasExited)
                {
                    MessageBox.Show("Ya hay una compilación de frontend en curso.", "AppInWhats",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            catch { }

            string frontend = Path.Combine(_root, "frontend");
            if (!Directory.Exists(frontend))
            {
                MessageBox.Show("No se encuentra la carpeta frontend.", "AppInWhats",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Muestra el panel de frontend aunque esté en modo dual
            try
            {
                if (_split != null && _split.Panel1Collapsed)
                {
                    // sesión single-pane: el log ya va a _termFront
                }
            }
            catch { }

            _termFront.Append("\r\n", TermFg);
            _termFront.Append("═══ Compilar frontend (production) ═══\r\n", Accent);
            _termFront.Append("ng build --configuration=production\r\n", TextMuted);
            _termFront.Append("(el serve en marcha no se detiene)\r\n\r\n", TextMuted);

            if (_btnBuild != null) _btnBuild.Enabled = false;
            _status.Text = "Compilando frontend (production)…";
            _status.ForeColor = Accent;

            string npx = FindNpx();
            Dictionary<string, string> env = new Dictionary<string, string>();
            env["NODE_OPTIONS"] = "--max-old-space-size=4096";
            env["NG_CLI_ANALYTICS"] = "false";
            env["FORCE_COLOR"] = "0";

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                string lower = npx.ToLowerInvariant();
                if (lower.EndsWith(".cmd") || lower.EndsWith(".bat"))
                {
                    psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                    psi.Arguments = "/d /s /c \"\"" + npx + "\" --yes ng build --configuration=production\"";
                }
                else
                {
                    psi.FileName = npx;
                    psi.Arguments = "--yes ng build --configuration=production";
                }

                psi.WorkingDirectory = frontend;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardInput = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;

                foreach (System.Collections.DictionaryEntry de in Environment.GetEnvironmentVariables())
                {
                    try { psi.EnvironmentVariables[de.Key.ToString()] = de.Value == null ? "" : de.Value.ToString(); }
                    catch { }
                }
                foreach (KeyValuePair<string, string> kv in env)
                    psi.EnvironmentVariables[kv.Key] = kv.Value;

                Process proc = new Process();
                proc.StartInfo = psi;
                proc.EnableRaisingEvents = true;
                proc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) _termFront.Append(e.Data + "\r\n", TermFg);
                };
                proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) _termFront.Append(e.Data + "\r\n", Color.FromArgb(180, 100, 20));
                };
                proc.Exited += delegate
                {
                    if (IsDisposed || _closing) return;
                    BeginInvoke(new Action(delegate
                    {
                        int code = SafeExitCode(proc);
                        if (code == 0)
                            _termFront.Append("\r\n■ Build production OK\r\n", Accent);
                        else
                            _termFront.Append("\r\n■ Build production falló · código " + code + "\r\n", Danger);

                        if (_btnBuild != null) _btnBuild.Enabled = true;
                        if (_running)
                        {
                            _status.Text = "Ejecutando…  ·  http://localhost:50080";
                            _status.ForeColor = Accent;
                        }
                        else
                        {
                            _status.Text = code == 0 ? "Build production terminado" : "Build production falló";
                            _status.ForeColor = code == 0 ? Accent : Danger;
                        }
                        try { if (_buildProc == proc) _buildProc = null; } catch { }
                    }));
                };

                proc.Start();
                AssignProcessToKillJob(proc);
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                _buildProc = proc;
            }
            catch (Exception ex)
            {
                _termFront.Append("ERROR build: " + ex.Message + "\r\n", Color.FromArgb(255, 120, 120));
                if (_btnBuild != null) _btnBuild.Enabled = true;
            }
        }

        // --- Process helpers ---

        bool EnsureNpx()
        {
            if (FindNpx() != null) return true;
            MessageBox.Show("No se encontró npx/npm. Instala Node.js LTS.", "AppInWhats",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        static string FindNpx()
        {
            string[] candidates = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "npx.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "npx.cmd"),
            };
            for (int i = 0; i < candidates.Length; i++)
                if (File.Exists(candidates[i])) return candidates[i];

            // PATH
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] parts = path.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = Path.Combine(parts[i].Trim('"'), "npx.cmd");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        void StartTracked(string name, string fileName, string arguments, string workDir, Dictionary<string, string> env, TerminalView term)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();

                // .cmd/.bat no se pueden lanzar con Redirect + UseShellExecute=false
                string lower = fileName.ToLowerInvariant();
                if (lower.EndsWith(".cmd") || lower.EndsWith(".bat"))
                {
                    psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                    psi.Arguments = "/d /s /c \"\"" + fileName + "\" " + arguments + "\"";
                }
                else
                {
                    psi.FileName = fileName;
                    psi.Arguments = arguments;
                }

                psi.WorkingDirectory = workDir;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardInput = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;

                // heredar PATH y demás
                foreach (System.Collections.DictionaryEntry de in Environment.GetEnvironmentVariables())
                {
                    try { psi.EnvironmentVariables[de.Key.ToString()] = de.Value == null ? "" : de.Value.ToString(); }
                    catch { }
                }
                if (env != null)
                {
                    foreach (KeyValuePair<string, string> kv in env)
                        psi.EnvironmentVariables[kv.Key] = kv.Value;
                }

                Process proc = new Process();
                proc.StartInfo = psi;
                proc.EnableRaisingEvents = true;
                proc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) term.Append(e.Data + "\r\n", TermFg);
                };
                proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) term.Append(e.Data + "\r\n", Color.FromArgb(180, 100, 20));
                };
                proc.Exited += delegate
                {
                    BeginInvoke(new Action(delegate
                    {
                        term.Append("\r\n[" + name + " terminó · código " + SafeExitCode(proc) + "]\r\n", TextMuted);
                        CheckAllExited();
                    }));
                };

                term.Append("$ " + name + " · " + fileName + " " + arguments + "\r\n", TextMuted);
                proc.Start();
                AssignProcessToKillJob(proc);
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                lock (_procLock) { _procs.Add(proc); }
                SaveSessionFile();
            }
            catch (Exception ex)
            {
                term.Append("ERROR: " + ex.Message + "\r\n", Color.FromArgb(255, 120, 120));
                CheckAllExited();
            }
        }

        static int SafeExitCode(Process p)
        {
            try { return p.ExitCode; } catch { return -1; }
        }

        void CheckAllExited()
        {
            if (_closing) return;
            lock (_procLock)
            {
                for (int i = 0; i < _procs.Count; i++)
                {
                    try { if (!_procs[i].HasExited) return; } catch { }
                }
            }
            if (_running)
                SetRunning(false);
        }

        void StopAll(bool announce)
        {
            if (_modeDocker && !_closing)
                TryDockerDown();

            List<Process> copy;
            lock (_procLock)
            {
                copy = new List<Process>(_procs);
                _procs.Clear();
            }

            for (int i = 0; i < copy.Count; i++)
            {
                Process p = copy[i];
                try
                {
                    if (!p.HasExited)
                        KillPidTree(p.Id);
                }
                catch { }
                try { p.Dispose(); } catch { }
            }

            try
            {
                if (_buildProc != null && !_buildProc.HasExited)
                    KillPidTree(_buildProc.Id);
            }
            catch { }
            _buildProc = null;
            if (_btnBuild != null) _btnBuild.Enabled = true;

            // Asegura liberar puertos nativos aunque el árbol de PIDs haya quedado huérfano
            if (!_modeDocker)
                FreeDevPorts();

            ClearSessionFile();

            if (announce)
            {
                if (_termBack != null) _termBack.Append("\r\n■ Parado por el usuario\r\n", Danger);
                if (_termFront != null) _termFront.Append("\r\n■ Parado por el usuario\r\n", Danger);
            }
            SetRunning(false);
        }

        // --- Sesión / Job / recuperación ---

        void HideResumeBanner()
        {
            if (_resumeBanner != null) _resumeBanner.Visible = false;
        }

        void CheckPreviousSession()
        {
            SessionState st = ReadSessionFile();
            bool portsBusy = IsPortInUse(3001) || IsPortInUse(50080);
            bool sessionLive = st != null && st.HasLiveProcesses();

            if (!sessionLive && !portsBusy)
            {
                if (st != null) ClearSessionFile();
                return;
            }

            string title = (st != null && !string.IsNullOrEmpty(st.Title)) ? st.Title : "Native (puertos 3001/50080 ocupados)";
            _resumeLabel.Text = "Sesión aún activa: " + title + "  ·  Puedes reabrir el panel o detenerla.";
            _resumeBanner.Visible = true;
            _resumeBanner.BringToFront();
            _status.Text = "Aviso: hay procesos de una sesión anterior";
            _status.ForeColor = Color.FromArgb(180, 100, 20);

            MessageBox.Show(
                "Se detectó una sesión todavía en marcha:\n\n«" + title + "»\n\n" +
                (portsBusy ? "(Puertos 3001 y/o 50080 ocupados)\n\n" : "") +
                "Usa «Abrir sesión» para volver a ese modo, o «Detener» para liberar los puertos.",
                "AppInWhats", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void ResumePreviousSession()
        {
            SessionState st = ReadSessionFile();
            bool portsBusy = IsPortInUse(3001) || IsPortInUse(50080);
            bool sessionLive = st != null && st.HasLiveProcesses();

            if (!sessionLive && !portsBusy)
            {
                HideResumeBanner();
                ClearSessionFile();
                MessageBox.Show("La sesión ya no está activa.", "AppInWhats", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string title = (st != null && !string.IsNullOrEmpty(st.Title)) ? st.Title : "Native · Local";
            _modeTitle = title;
            _modeDual = st != null ? st.Dual : true;
            _modeDocker = st != null && st.Docker;
            _modeRemote = st != null && st.Remote;
            _modeOpt = st != null && st.Opt;
            _modeId = (st != null && !string.IsNullOrEmpty(st.ModeId)) ? st.ModeId : InferModeId(title, _modeDual, _modeDocker, _modeRemote, _modeOpt);
            if (st == null)
            {
                // Solo puertos ocupados: asumimos native local
                _modeDual = true;
                _modeDocker = false;
                _modeId = "native-local";
            }

            HideResumeBanner();
            ShowSession(title, _modeDual);
            _termFront.Append("Sesión recuperada: " + title + "\r\n", Accent);
            _termFront.Append("Los procesos siguen en ejecución (puertos ocupados).\r\n", TextMuted);
            _termFront.Append("Nota: los logs en vivo no se pueden reenganchar tras reabrir AppInWhats DevBuild.\r\n", TextMuted);
            _termFront.Append("Usa «Parar todo» para liberar 3001/50080.\r\n\r\n", TextMuted);
            if (_modeDual)
            {
                _termBack.Append("Backend: proceso(s) aún activos en :3001\r\n", TextMuted);
                _termBack.Append("Logs en vivo no disponibles en esta recuperación.\r\n", TextMuted);
            }

            lock (_procLock) { _procs.Clear(); }
            if (st != null)
            {
                for (int i = 0; i < st.Pids.Count; i++)
                {
                    try
                    {
                        Process p = Process.GetProcessById(st.Pids[i]);
                        p.EnableRaisingEvents = true;
                        int pid = st.Pids[i];
                        p.Exited += delegate
                        {
                            if (IsDisposed || _closing) return;
                            BeginInvoke(new Action(delegate
                            {
                                _termFront.Append("[PID " + pid + " terminó]\r\n", TextMuted);
                                CheckAllExited();
                            }));
                        };
                        AssignProcessToKillJob(p);
                        lock (_procLock) { _procs.Add(p); }
                    }
                    catch { }
                }
            }

            // Adjunta también los PIDs que escuchan los puertos
            List<int> portPids = GetPidsListeningOnPort(3001);
            portPids.AddRange(GetPidsListeningOnPort(50080));
            for (int i = 0; i < portPids.Count; i++)
            {
                try
                {
                    bool already = false;
                    lock (_procLock)
                    {
                        for (int j = 0; j < _procs.Count; j++)
                        {
                            try { if (_procs[j].Id == portPids[i]) { already = true; break; } } catch { }
                        }
                    }
                    if (already) continue;
                    Process p = Process.GetProcessById(portPids[i]);
                    p.EnableRaisingEvents = true;
                    lock (_procLock) { _procs.Add(p); }
                }
                catch { }
            }

            SetRunning(true);
            SaveSessionFile();
        }

        void KillSessionFromFile()
        {
            SessionState st = ReadSessionFile();
            if (st != null)
            {
                _modeDocker = st.Docker;
                _modeRemote = st.Remote;
                _modeOpt = st.Opt;
                if (st.Docker) TryDockerDown();
                for (int i = 0; i < st.Pids.Count; i++)
                    KillPidTree(st.Pids[i]);
            }
            FreeDevPorts();
            ClearSessionFile();
            lock (_procLock)
            {
                for (int i = 0; i < _procs.Count; i++)
                    try { _procs[i].Dispose(); } catch { }
                _procs.Clear();
            }
            SetRunning(false);
        }

        void TryDockerDown()
        {
            try
            {
                StringBuilder args = new StringBuilder();
                args.Append("compose -f docker-compose.yml -f docker-compose.override.yml");
                if (_modeRemote) args.Append(" -f docker-compose.remote.yml");
                if (_modeOpt) args.Append(" -f docker-compose.optimized.yml");
                args.Append(" down");

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "docker";
                psi.Arguments = args.ToString();
                psi.WorkingDirectory = _root;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process p = Process.Start(psi);
                if (p != null) p.WaitForExit(20000);
            }
            catch { }
        }

        void SaveSessionFile()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("title=" + (_modeTitle ?? ""));
                sb.AppendLine("mode=" + (_modeId ?? ""));
                sb.AppendLine("dual=" + (_modeDual ? "1" : "0"));
                sb.AppendLine("docker=" + (_modeDocker ? "1" : "0"));
                sb.AppendLine("remote=" + (_modeRemote ? "1" : "0"));
                sb.AppendLine("opt=" + (_modeOpt ? "1" : "0"));
                lock (_procLock)
                {
                    for (int i = 0; i < _procs.Count; i++)
                    {
                        try
                        {
                            if (!_procs[i].HasExited)
                                sb.AppendLine("pid=" + _procs[i].Id);
                        }
                        catch { }
                    }
                }
                File.WriteAllText(_sessionPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        void ClearSessionFile()
        {
            try { if (File.Exists(_sessionPath)) File.Delete(_sessionPath); } catch { }
        }

        SessionState ReadSessionFile()
        {
            try
            {
                if (!File.Exists(_sessionPath)) return null;
                SessionState st = new SessionState();
                string[] lines = File.ReadAllLines(_sessionPath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("title=")) st.Title = line.Substring(6);
                    else if (line.StartsWith("mode=")) st.ModeId = line.Substring(5);
                    else if (line.StartsWith("dual=")) st.Dual = line.Substring(5) == "1";
                    else if (line.StartsWith("docker=")) st.Docker = line.Substring(7) == "1";
                    else if (line.StartsWith("remote=")) st.Remote = line.Substring(7) == "1";
                    else if (line.StartsWith("opt=")) st.Opt = line.Substring(4) == "1";
                    else if (line.StartsWith("pid="))
                    {
                        int pid;
                        if (int.TryParse(line.Substring(4), out pid)) st.Pids.Add(pid);
                    }
                }
                if (string.IsNullOrEmpty(st.Title) && st.Pids.Count == 0) return null;
                return st;
            }
            catch { return null; }
        }

        void InitKillOnCloseJob()
        {
            try
            {
                _job = CreateJobObject(IntPtr.Zero, null);
                if (_job == IntPtr.Zero) return;

                JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE

                int len = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr ptr = Marshal.AllocHGlobal(len);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    SetInformationJobObject(_job, 9, ptr, (uint)len);
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            catch
            {
                _job = IntPtr.Zero;
            }
        }

        void AssignProcessToKillJob(Process proc)
        {
            if (_job == IntPtr.Zero || proc == null) return;
            try { AssignProcessToJobObject(_job, proc.Handle); } catch { }
        }

        // --- Stats de recursos ---

        void UpdateResourceStats()
        {
            if (_statsBar == null || !_sessionPanel.Visible) return;
            if (!_running)
            {
                ResetStatsDisplay();
                return;
            }

            try
            {
                Dictionary<int, int> parents = BuildParentMap();
                HashSet<int> backIds = new HashSet<int>();
                HashSet<int> frontIds = new HashSet<int>();
                HashSet<int> otherIds = new HashSet<int>();

                List<int> listenBack = GetPidsListeningOnPort(3001);
                List<int> listenFront = GetPidsListeningOnPort(50080);
                for (int i = 0; i < listenBack.Count; i++)
                    AddProcessTree(listenBack[i], parents, backIds);
                for (int i = 0; i < listenFront.Count; i++)
                    AddProcessTree(listenFront[i], parents, frontIds);

                lock (_procLock)
                {
                    for (int i = 0; i < _procs.Count; i++)
                    {
                        try
                        {
                            if (_procs[i].HasExited) continue;
                            int id = _procs[i].Id;
                            HashSet<int> target = otherIds;
                            if (_modeDual || !_modeDocker)
                            {
                                // clasificar por puertos / cmdline
                                string cmd = SafeProcessCommandHint(_procs[i]);
                                if (cmd.IndexOf("nodemon", StringComparison.OrdinalIgnoreCase) >= 0
                                    || cmd.IndexOf("ts-node", StringComparison.OrdinalIgnoreCase) >= 0
                                    || cmd.IndexOf("\\backend", StringComparison.OrdinalIgnoreCase) >= 0)
                                    target = backIds;
                                else if (cmd.IndexOf("ng serve", StringComparison.OrdinalIgnoreCase) >= 0
                                    || cmd.IndexOf("@angular", StringComparison.OrdinalIgnoreCase) >= 0
                                    || cmd.IndexOf("\\frontend", StringComparison.OrdinalIgnoreCase) >= 0)
                                    target = frontIds;
                                else if (_modeDocker)
                                    target = otherIds;
                            }
                            else if (_modeDocker)
                                target = otherIds;

                            AddProcessTree(id, parents, target);
                        }
                        catch { }
                    }
                }

                // quitar solapes: un PID solo en un grupo (prioridad front/back sobre otros)
                otherIds.ExceptWith(backIds);
                otherIds.ExceptWith(frontIds);
                // si un pid está en back y front (raro), dejarlo en front
                backIds.ExceptWith(frontIds);

                ProcAgg back = AggregateProcs(backIds);
                ProcAgg front = AggregateProcs(frontIds);
                ProcAgg other = AggregateProcs(otherIds);
                ProcAgg total = back.Add(front).Add(other);

                DateTime now = DateTime.UtcNow;
                double elapsed = (_prevCpuSample == DateTime.MinValue)
                    ? 0
                    : (now - _prevCpuSample).TotalMilliseconds;
                int cores = Math.Max(1, Environment.ProcessorCount);

                ApplyCpu(ref back, elapsed, cores);
                ApplyCpu(ref front, elapsed, cores);
                ApplyCpu(ref other, elapsed, cores);
                total.CpuPercent = Math.Min(100.0 * cores, back.CpuPercent + front.CpuPercent + other.CpuPercent);

                _prevCpuSample = now;

                _statsBack.SetStatsText(FormatStats("Backend", back));
                _statsFront.SetStatsText(FormatStats("Frontend", front));
                _statsOther.SetStatsText(FormatStats(_modeDocker ? "Docker" : "Otros", other));
                _statsTotal.SetStatsText(FormatStats("Total", total));

                if (_status != null && _running)
                {
                    _status.Text = string.Format(
                        "Ejecutando  ·  Total {0:0.0}% CPU  ·  {1}  ·  http://localhost:50080",
                        total.CpuPercent,
                        FormatBytes(total.MemoryBytes));
                    _status.ForeColor = Accent;
                }
            }
            catch
            {
            }
        }

        struct ProcAgg
        {
            public int Count;
            public long MemoryBytes;
            public double CpuPercent;
            public Dictionary<int, TimeSpan> CpuNow;

            public ProcAgg Add(ProcAgg o)
            {
                ProcAgg r = this;
                r.Count += o.Count;
                r.MemoryBytes += o.MemoryBytes;
                r.CpuPercent += o.CpuPercent;
                return r;
            }
        }

        ProcAgg AggregateProcs(HashSet<int> ids)
        {
            ProcAgg agg = new ProcAgg();
            agg.CpuNow = new Dictionary<int, TimeSpan>();
            if (ids == null) return agg;

            foreach (int pid in ids)
            {
                try
                {
                    Process p = Process.GetProcessById(pid);
                    p.Refresh();
                    if (p.HasExited) continue;
                    agg.Count++;
                    agg.MemoryBytes += p.WorkingSet64;
                    TimeSpan cpu = p.TotalProcessorTime;
                    agg.CpuNow[pid] = cpu;
                }
                catch { }
            }
            return agg;
        }

        void ApplyCpu(ref ProcAgg agg, double elapsedMs, int cores)
        {
            if (elapsedMs < 50 || agg.CpuNow == null)
            {
                // primera muestra: guardar baselines
                if (agg.CpuNow != null)
                {
                    foreach (KeyValuePair<int, TimeSpan> kv in agg.CpuNow)
                        _prevCpu[kv.Key] = kv.Value;
                }
                agg.CpuPercent = 0;
                return;
            }

            double cpuMs = 0;
            foreach (KeyValuePair<int, TimeSpan> kv in agg.CpuNow)
            {
                TimeSpan prev;
                if (_prevCpu.TryGetValue(kv.Key, out prev))
                {
                    double delta = (kv.Value - prev).TotalMilliseconds;
                    if (delta > 0) cpuMs += delta;
                }
                _prevCpu[kv.Key] = kv.Value;
            }
            agg.CpuPercent = Math.Max(0, Math.Min(100.0 * cores, (cpuMs / elapsedMs) * 100.0));
        }

        static string FormatStats(string role, ProcAgg agg)
        {
            if (agg.Count <= 0)
                return role + "  ·  inactivo";
            return string.Format("{0}  ·  {1:0.0}% CPU  ·  {2}  ·  {3} proc",
                role, agg.CpuPercent, FormatBytes(agg.MemoryBytes), agg.Count);
        }

        static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.0") + " MB";
            return (mb / 1024.0).ToString("0.00") + " GB";
        }

        static string SafeProcessCommandHint(Process p)
        {
            try
            {
                // Win32_Process via PowerShell es caro; usamos MainModule si es posible
                if (p.MainModule != null && p.MainModule.FileName != null)
                    return p.MainModule.FileName;
            }
            catch { }
            try { return p.ProcessName ?? ""; } catch { return ""; }
        }

        Dictionary<int, int> BuildParentMap()
        {
            Dictionary<int, int> map = new Dictionary<int, int>();
            IntPtr snap = CreateToolhelp32Snapshot(0x2, 0); // TH32CS_SNAPPROCESS
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return map;

            try
            {
                PROCESSENTRY32 pe = new PROCESSENTRY32();
                pe.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
                if (!Process32First(snap, ref pe)) return map;
                do
                {
                    map[(int)pe.th32ProcessID] = (int)pe.th32ParentProcessID;
                } while (Process32Next(snap, ref pe));
            }
            catch { }
            finally
            {
                CloseHandle(snap);
            }
            return map;
        }

        void AddProcessTree(int rootPid, Dictionary<int, int> parents, HashSet<int> into)
        {
            if (rootPid <= 0 || into == null) return;
            into.Add(rootPid);
            // hijos: quienes tienen parent == root (recursivo)
            Queue<int> q = new Queue<int>();
            q.Enqueue(rootPid);
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                foreach (KeyValuePair<int, int> kv in parents)
                {
                    if (kv.Value == cur && !into.Contains(kv.Key))
                    {
                        into.Add(kv.Key);
                        q.Enqueue(kv.Key);
                    }
                }
            }
        }

        // --- UI helpers (chrome / tiles) ---

        Label MakeDragLabel(string text, Font font, Color color, Point location)
        {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.AutoSize = true;
            lbl.Font = font;
            lbl.ForeColor = color;
            lbl.Location = location;
            lbl.BackColor = Color.Transparent;
            lbl.MouseDown += BeginWindowDrag;
            return lbl;
        }

        void MinimizeWindow() { WindowState = FormWindowState.Minimized; }

        void BeginWindowDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _maximized) return;
            if (e.Clicks > 1) return; // deja pasar el doble clic a maximizar/restaurar
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);
        }

        void TitleBarDoubleClick(object sender, EventArgs e)
        {
            ToggleMaximize();
        }

        void ToggleMaximize()
        {
            if (!_maximized)
            {
                _restoreBounds = Bounds;
                Bounds = Screen.FromControl(this).WorkingArea;
                _maximized = true;
            }
            else
            {
                Bounds = _restoreBounds;
                _maximized = false;
            }
            ApplyWindowFrame();
            if (_capMax != null)
            {
                _capMax.IsRestored = _maximized;
                _capMax.Invalidate();
            }
        }

        Control CreateTile(TileInfo info, int w, int h)
        {
            DoubleBufferedPanel card = new DoubleBufferedPanel();
            card.Size = new Size(w, h);
            card.BackColor = Bg;
            card.Cursor = Cursors.Hand;

            TileFace face = new TileFace(info);
            face.Dock = DockStyle.Fill;
            face.Cursor = Cursors.Hand;
            face.Click += delegate { info.Action(); };
            card.Controls.Add(face);
            return card;
        }

        sealed class TileFace : Control
        {
            readonly TileInfo _info;
            bool _hover;

            public TileFace(TileInfo info)
            {
                _info = info;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Bg;
                MouseEnter += delegate { _hover = true; Invalidate(); };
                MouseLeave += delegate { _hover = false; Invalidate(); };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Bg);

                Rectangle shadow = new Rectangle(5, 7, Width - 10, Height - 10);
                using (GraphicsPath sp = RoundRect(shadow, 24))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(_hover ? 35 : 22, 0, 0, 0)))
                    g.FillPath(sb, sp);

                Rectangle rect = new Rectangle(0, 0, Width - 8, Height - 10);
                Color face = _hover ? TileHover : Tile;
                using (GraphicsPath path = RoundRect(rect, 24))
                using (SolidBrush brush = new SolidBrush(face))
                using (Pen pen = new Pen(Border, 1.5f))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(pen, path);
                }

                int ix = (rect.Width - 56) / 2;
                int iy = 18;
                using (SolidBrush ring = new SolidBrush(Color.FromArgb(28, _info.Accent)))
                    g.FillEllipse(ring, ix - 3, iy - 3, 62, 62);
                using (SolidBrush iconBg = new SolidBrush(_info.Accent))
                    g.FillEllipse(iconBg, ix, iy, 56, 56);
                using (SolidBrush brush = new SolidBrush(Color.White))
                using (Font font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold))
                {
                    SizeF sz = g.MeasureString(_info.Glyph, font);
                    g.DrawString(_info.Glyph, font, brush, ix + (56 - sz.Width) / 2f, iy + (56 - sz.Height) / 2f);
                }

                using (SolidBrush tb = new SolidBrush(TextMain))
                using (Font tf = new Font("Segoe UI Semibold", 11f, FontStyle.Bold))
                {
                    SizeF sz = g.MeasureString(_info.Title, tf);
                    g.DrawString(_info.Title, tf, tb, (rect.Width - sz.Width) / 2f, 92);
                }
                using (SolidBrush bb = new SolidBrush(_info.Accent))
                using (Font bf = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                {
                    SizeF sz = g.MeasureString(_info.Badge, bf);
                    g.DrawString(_info.Badge, bf, bb, (rect.Width - sz.Width) / 2f, 114);
                }
                using (SolidBrush db = new SolidBrush(TextMuted))
                using (Font df = new Font("Segoe UI", 7.5f))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    g.DrawString(_info.Desc, df, db, new RectangleF(10, 134, rect.Width - 20, 24), sf);
                }
            }
        }

        sealed class StatsChip : Control
        {
            readonly Color _accent;
            string _statsText;

            public StatsChip(string role, Color accent)
            {
                _accent = accent;
                _statsText = role + "  ·  —";
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Color.White;
                Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold);
            }

            public void SetStatsText(string text)
            {
                _statsText = text ?? "";
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Parent != null ? Parent.BackColor : Color.FromArgb(250, 251, 253));

                Rectangle rr = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = RoundRect(rr, 10))
                using (SolidBrush fill = new SolidBrush(Color.White))
                using (Pen pen = new Pen(Border, 1))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(pen, path);
                }

                TextRenderer.DrawText(
                    g,
                    _statsText,
                    Font,
                    new Rectangle(12, 0, Width - 18, Height),
                    TextMain,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }

        sealed class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                UpdateStyles();
            }
        }

        static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        static string InferModeId(string title, bool dual, bool docker, bool remote, bool opt)
        {
            if (docker && opt && remote) return "opt-remote";
            if (docker && opt) return "opt-local";
            if (docker && remote) return "docker-remote";
            if (docker) return "docker-local";
            if (!string.IsNullOrEmpty(title) && title.IndexOf("Instalar", StringComparison.OrdinalIgnoreCase) >= 0)
                return "install";
            if (remote) return "native-remote";
            if (dual) return "native-local";
            return "native-local";
        }

        sealed class SessionState
        {
            public string Title = "";
            public string ModeId = "";
            public bool Dual;
            public bool Docker;
            public bool Remote;
            public bool Opt;
            public List<int> Pids = new List<int>();

            public bool HasLiveProcesses()
            {
                for (int i = 0; i < Pids.Count; i++)
                {
                    try
                    {
                        Process p = Process.GetProcessById(Pids[i]);
                        if (p != null && !p.HasExited) return true;
                    }
                    catch { }
                }
                return false;
            }
        }

        sealed class TileInfo
        {
            public string Title;
            public string Badge;
            public string Desc;
            public Color Accent;
            public string Glyph;
            public Action Action;

            public TileInfo(string title, string badge, string desc, Color accent, string glyph, Action action)
            {
                Title = title;
                Badge = badge;
                Desc = desc;
                Accent = accent;
                Glyph = glyph;
                Action = action;
            }
        }

        sealed class TerminalView : Panel
        {
            readonly Label _title;
            readonly RichTextBox _box;
            readonly Color _accent;
            readonly string _folderPath;
            readonly Panel _head;
            readonly RoundedButton _btnFolder;
            readonly RoundedButton _btnBranch;
            bool _gitReady;
            string _currentBranch = "";
            const int MaxChars = 200000;

            public Func<bool> BeforeBranchSwitch;
            public Action AfterBranchSwitchSuccess;

            public TerminalView(string title, Color accent, string folderPath)
            {
                _accent = accent;
                _folderPath = folderPath;
                BackColor = TermBg;
                Padding = new Padding(0);

                _head = new DoubleBufferedPanel();
                _head.Dock = DockStyle.Top;
                _head.Height = 40;
                _head.BackColor = PanelBg;
                _head.Paint += delegate(object s, PaintEventArgs e)
                {
                    using (Pen pen = new Pen(Border, 1))
                        e.Graphics.DrawLine(pen, 0, _head.Height - 1, _head.Width, _head.Height - 1);
                    using (SolidBrush b = new SolidBrush(_accent))
                        e.Graphics.FillRectangle(b, 0, _head.Height - 2, 56, 2);
                };
                _head.Resize += delegate { LayoutHead(); };

                _title = new Label();
                _title.Text = title;
                _title.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
                _title.ForeColor = TextMain;
                _title.AutoSize = true;
                _title.Location = new Point(14, 10);
                _title.BackColor = Color.Transparent;

                _btnBranch = new RoundedButton("rama ▾", Color.FromArgb(245, 247, 250), TextMain);
                _btnBranch.Size = new Size(150, 28);
                _btnBranch.Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold);
                _btnBranch.Visible = false;
                _btnBranch.Click += delegate { ShowBranchMenu(); };

                _btnFolder = new RoundedButton("Abrir carpeta", Color.FromArgb(245, 247, 250), TextMain);
                _btnFolder.Size = new Size(118, 28);
                _btnFolder.Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold);
                _btnFolder.Visible = !string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath);
                _btnFolder.Click += delegate
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(_folderPath) && Directory.Exists(_folderPath))
                            Process.Start("explorer.exe", _folderPath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("No se pudo abrir la carpeta:\n" + ex.Message, "AppInWhats DevBuild",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                };

                _head.Controls.Add(_title);
                _head.Controls.Add(_btnBranch);
                _head.Controls.Add(_btnFolder);

                _box = new RichTextBox();
                _box.Dock = DockStyle.Fill;
                _box.ReadOnly = true;
                _box.BorderStyle = BorderStyle.None;
                _box.BackColor = TermBg;
                _box.ForeColor = TermFg;
                _box.Font = new Font("Cascadia Mono", 9f);
                if (_box.Font.Name != "Cascadia Mono")
                    _box.Font = new Font("Consolas", 9f);
                _box.WordWrap = true;
                _box.DetectUrls = false;
                _box.ScrollBars = RichTextBoxScrollBars.Vertical;
                _box.HideSelection = false;

                Controls.Add(_box);
                Controls.Add(_head);
                LayoutHead();
                RefreshGitBranch();
            }

            void LayoutHead()
            {
                if (_btnFolder == null || _head == null) return;
                _btnFolder.Location = new Point(_head.Width - _btnFolder.Width - 12, 6);
                if (_btnBranch != null && _btnBranch.Visible)
                    _btnBranch.Location = new Point(_btnFolder.Left - _btnBranch.Width - 8, 6);
                _btnFolder.BringToFront();
                if (_btnBranch != null) _btnBranch.BringToFront();
            }

            public void SetTitle(string title)
            {
                _title.Text = title;
            }

            public void RefreshGitBranch()
            {
                _gitReady = IsGitRepo(_folderPath);
                _btnBranch.Visible = _gitReady;
                if (!_gitReady)
                {
                    LayoutHead();
                    return;
                }

                string branch = GetCurrentBranch(_folderPath);
                _currentBranch = branch ?? "";
                string label = string.IsNullOrEmpty(_currentBranch) ? "rama ▾" : TruncateBranch(_currentBranch) + " ▾";
                _btnBranch.Text = label;
                int w = Math.Max(120, TextRenderer.MeasureText(label, _btnBranch.Font).Width + 24);
                _btnBranch.Size = new Size(Math.Min(220, w), 28);
                LayoutHead();
            }

            static string TruncateBranch(string name)
            {
                if (string.IsNullOrEmpty(name)) return name;
                return name.Length <= 28 ? name : name.Substring(0, 26) + "…";
            }

            void ShowBranchMenu()
            {
                if (!_gitReady) return;

                Cursor = Cursors.WaitCursor;
                List<string> branches;
                try
                {
                    branches = ListLocalBranches(_folderPath);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }

                ContextMenuStrip menu = new ContextMenuStrip();
                menu.Font = new Font("Segoe UI", 9f);
                menu.ShowImageMargin = false;

                ToolStripMenuItem refreshItem = new ToolStripMenuItem("↻ Actualizar ramas (fetch)");
                refreshItem.Click += delegate
                {
                    Append("\r\n── git fetch --all --prune ──\r\n", Accent);
                    string err;
                    int code = RunGit(_folderPath, "fetch --all --prune", out err);
                    if (code == 0)
                        Append("Fetch OK\r\n", Accent);
                    else
                        Append("Fetch: " + (string.IsNullOrEmpty(err) ? ("código " + code) : err) + "\r\n", Danger);
                    RefreshGitBranch();
                    ShowBranchMenu();
                };
                menu.Items.Add(refreshItem);
                menu.Items.Add(new ToolStripSeparator());

                if (branches.Count == 0)
                {
                    ToolStripMenuItem empty = new ToolStripMenuItem("(sin ramas locales)");
                    empty.Enabled = false;
                    menu.Items.Add(empty);
                }
                else
                {
                    for (int i = 0; i < branches.Count; i++)
                    {
                        string branch = branches[i];
                        ToolStripMenuItem item = new ToolStripMenuItem(branch);
                        if (string.Equals(branch, _currentBranch, StringComparison.OrdinalIgnoreCase))
                            item.Checked = true;
                        string target = branch;
                        item.Click += delegate { SwitchToBranch(target); };
                        menu.Items.Add(item);
                    }
                }

                menu.Items.Add(new ToolStripSeparator());
                ToolStripMenuItem remotesItem = new ToolStripMenuItem("Ramas remotas…");
                remotesItem.Click += delegate { ShowRemoteBranchesMenu(); };
                menu.Items.Add(remotesItem);

                menu.Show(_btnBranch, new Point(0, _btnBranch.Height));
            }

            void ShowRemoteBranchesMenu()
            {
                List<string> remotes = ListRemoteBranches(_folderPath);
                ContextMenuStrip menu = new ContextMenuStrip();
                menu.Font = new Font("Segoe UI", 9f);
                menu.ShowImageMargin = false;

                if (remotes.Count == 0)
                {
                    ToolStripMenuItem empty = new ToolStripMenuItem("(sin ramas remotas)");
                    empty.Enabled = false;
                    menu.Items.Add(empty);
                }
                else
                {
                    for (int i = 0; i < remotes.Count; i++)
                    {
                        string branch = remotes[i];
                        ToolStripMenuItem item = new ToolStripMenuItem(branch);
                        string target = branch;
                        item.Click += delegate { SwitchToBranch(target); };
                        menu.Items.Add(item);
                    }
                }

                menu.Show(_btnBranch, new Point(0, _btnBranch.Height));
            }

            void SwitchToBranch(string branch)
            {
                if (string.IsNullOrEmpty(branch)) return;
                if (string.Equals(branch, _currentBranch, StringComparison.OrdinalIgnoreCase))
                    return;

                DialogResult r = MessageBox.Show(
                    "¿Cambiar a la rama «" + branch + "»?\n\n" +
                    "Si la app está en marcha se detendrá, se cambiará la rama y se volverá a iniciar.",
                    "AppInWhats DevBuild",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;

                bool shouldRestart = false;
                if (BeforeBranchSwitch != null)
                {
                    try { shouldRestart = BeforeBranchSwitch(); }
                    catch (Exception ex)
                    {
                        Append("Error al parar la app: " + ex.Message + "\r\n", Danger);
                        return;
                    }
                }

                Append("\r\n── git checkout " + branch + " ──\r\n", Accent);
                Cursor = Cursors.WaitCursor;
                string err;
                int code;
                try
                {
                    code = RunGit(_folderPath, "checkout \"" + branch.Replace("\"", "") + "\"", out err);
                    if (code != 0)
                    {
                        string remoteRef = "origin/" + branch;
                        code = RunGit(_folderPath, "checkout -B \"" + branch.Replace("\"", "") + "\" \"" + remoteRef + "\"", out err);
                    }
                }
                finally
                {
                    Cursor = Cursors.Default;
                }

                if (code == 0)
                {
                    Append("Rama activa: " + branch + "\r\n", Accent);
                    RefreshGitBranch();
                    if (shouldRestart && AfterBranchSwitchSuccess != null)
                    {
                        Append("Reiniciando aplicación…\r\n", Accent);
                        try { AfterBranchSwitchSuccess(); }
                        catch (Exception ex)
                        {
                            Append("Error al reiniciar: " + ex.Message + "\r\n", Danger);
                        }
                    }
                }
                else
                {
                    Append("Error al cambiar de rama:\r\n" + (err ?? "") + "\r\n", Danger);
                    MessageBox.Show(
                        "No se pudo cambiar a «" + branch + "».\n\n" + (err ?? "") +
                        (shouldRestart ? "\n\nLa app quedó detenida; puedes iniciarla de nuevo con «Iniciar aplicación»." : ""),
                        "AppInWhats DevBuild",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            public void Clear()
            {
                if (_box.InvokeRequired)
                {
                    _box.BeginInvoke(new Action(Clear));
                    return;
                }
                _box.Clear();
            }

            public void Append(string text, Color color)
            {
                if (_box.IsDisposed) return;
                if (_box.InvokeRequired)
                {
                    _box.BeginInvoke(new Action(delegate { Append(text, color); }));
                    return;
                }

                _box.SelectionStart = _box.TextLength;
                _box.SelectionLength = 0;
                _box.SelectionColor = color;
                _box.AppendText(text);

                if (_box.TextLength > MaxChars)
                {
                    _box.Select(0, _box.TextLength - MaxChars / 2);
                    _box.SelectedText = "";
                }

                _box.SelectionStart = _box.TextLength;
                _box.ScrollToCaret();
            }

            static bool IsGitRepo(string path)
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return false;
                string err;
                int code = RunGit(path, "rev-parse --is-inside-work-tree", out err);
                return code == 0;
            }

            static string GetCurrentBranch(string path)
            {
                string err;
                string output;
                int code = RunGitCapture(path, "rev-parse --abbrev-ref HEAD", out output, out err);
                if (code != 0) return "";
                return (output ?? "").Trim();
            }

            static List<string> ListLocalBranches(string path)
            {
                string err;
                string output;
                int code = RunGitCapture(path, "for-each-ref --format=%(refname:short) refs/heads/", out output, out err);
                List<string> list = new List<string>();
                if (code != 0 || string.IsNullOrEmpty(output)) return list;
                string[] lines = output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    string b = lines[i].Trim();
                    if (b.Length > 0 && !list.Contains(b)) list.Add(b);
                }
                list.Sort(StringComparer.OrdinalIgnoreCase);
                return list;
            }

            static List<string> ListRemoteBranches(string path)
            {
                string err;
                string output;
                int code = RunGitCapture(path, "for-each-ref --format=%(refname:short) refs/remotes/", out output, out err);
                List<string> list = new List<string>();
                HashSet<string> local = new HashSet<string>(ListLocalBranches(path), StringComparer.OrdinalIgnoreCase);
                if (code != 0 || string.IsNullOrEmpty(output)) return list;
                string[] lines = output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    string b = lines[i].Trim();
                    if (b.Length == 0) continue;
                    if (b.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(b, "origin/HEAD", StringComparison.OrdinalIgnoreCase)) continue;
                    // origin/foo -> foo
                    string shortName = b;
                    int slash = b.IndexOf('/');
                    if (slash > 0) shortName = b.Substring(slash + 1);
                    if (local.Contains(shortName)) continue;
                    if (!list.Contains(shortName)) list.Add(shortName);
                }
                list.Sort(StringComparer.OrdinalIgnoreCase);
                return list;
            }

            static int RunGit(string cwd, string args, out string err)
            {
                string output;
                return RunGitCapture(cwd, args, out output, out err);
            }

            static int RunGitCapture(string cwd, string args, out string output, out string err)
            {
                output = "";
                err = "";
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = "git";
                    psi.Arguments = args;
                    psi.WorkingDirectory = cwd;
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.CreateNoWindow = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    psi.StandardErrorEncoding = Encoding.UTF8;

                    using (Process p = Process.Start(psi))
                    {
                        if (p == null) { err = "No se pudo iniciar git"; return -1; }
                        output = p.StandardOutput.ReadToEnd();
                        err = p.StandardError.ReadToEnd();
                        p.WaitForExit(60000);
                        return p.ExitCode;
                    }
                }
                catch (Exception ex)
                {
                    err = ex.Message;
                    return -1;
                }
            }
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        [DllImport("gdi32.dll", SetLastError = true)]
        static extern bool DeleteObject(IntPtr hObject);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        sealed class RoundedButton : Control
        {
            Color _bg;
            Color _fg;
            bool _hover;
            bool _pressed;
            const int Radius = 10;

            public RoundedButton(string text, Color bg, Color fg)
            {
                Text = text;
                _bg = bg;
                _fg = fg;
                Cursor = Cursors.Hand;
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
                Size = new Size(118, 34);
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;

                MouseEnter += delegate { _hover = true; Invalidate(); };
                MouseLeave += delegate { _hover = false; _pressed = false; Invalidate(); };
                MouseDown += delegate { _pressed = true; Invalidate(); };
                MouseUp += delegate { _pressed = false; Invalidate(); };
            }

            public override Font Font
            {
                get { return base.Font; }
                set { base.Font = value; Invalidate(); }
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                base.OnEnabledChanged(e);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color parentBg = Parent != null ? Parent.BackColor : Color.White;
                g.Clear(parentBg);

                Rectangle face = new Rectangle(0, 0, Width - 1, Height - 1);

                Color fill = _bg;
                Color border = Color.FromArgb(220, Border);
                if (!Enabled)
                {
                    fill = Color.FromArgb(236, 238, 242);
                    border = Color.FromArgb(228, 230, 234);
                }
                else if (_pressed)
                {
                    fill = ControlPaint.Dark(_bg, 0.06f);
                }
                else if (_hover)
                {
                    // elevación sutil: un poco más claro + borde más marcado
                    fill = ControlPaint.Light(_bg, 0.08f);
                    border = Color.FromArgb(180, _bg.R > 200 ? 160 : _bg.R, _bg.G > 200 ? 160 : _bg.G, _bg.B > 200 ? 170 : _bg.B);
                    using (GraphicsPath sp = RoundRect(new Rectangle(1, 2, Width - 3, Height - 2), Radius))
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(28, 0, 0, 0)))
                        g.FillPath(sb, sp);
                }

                using (GraphicsPath path = RoundRect(face, Radius))
                using (SolidBrush brush = new SolidBrush(fill))
                using (Pen pen = new Pen(border, 1f))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(pen, path);
                }

                // acento inferior fino si es botón primario (fondo oscuro/saturado)
                if (Enabled && _bg.GetBrightness() < 0.65f)
                {
                    using (SolidBrush ab = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
                        g.FillRectangle(ab, 8, 1, Width - 16, 1);
                }

                Color text = Enabled ? _fg : Color.FromArgb(160, 166, 176);
                TextRenderer.DrawText(
                    g,
                    Text,
                    Font,
                    ClientRectangle,
                    text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }

        enum CaptionKind
        {
            Minimize,
            Maximize,
            Close
        }

        sealed class CaptionButton : Panel
        {
            readonly CaptionKind _kind;
            readonly Action _action;
            bool _hover;

            public bool IsRestored;

            public CaptionButton(CaptionKind kind, Action action)
            {
                _kind = kind;
                _action = action;
                BackColor = TitleBar;
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

                MouseEnter += delegate { _hover = true; Invalidate(); };
                MouseLeave += delegate { _hover = false; Invalidate(); };
                Click += delegate { _action(); };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(TitleBar);

                if (_hover)
                {
                    Color bg = _kind == CaptionKind.Close ? CloseHover : CaptionHover;
                    Rectangle rr = new Rectangle(4, 6, Width - 8, Height - 12);
                    using (GraphicsPath path = RoundRect(rr, 8))
                    using (SolidBrush b = new SolidBrush(bg))
                        g.FillPath(b, path);
                }

                Color icon = (_hover && _kind == CaptionKind.Close) ? Color.White : TextMain;
                using (Pen pen = new Pen(icon, 1.2f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    int cx = Width / 2;
                    int cy = Height / 2;

                    if (_kind == CaptionKind.Minimize)
                    {
                        g.DrawLine(pen, cx - 5, cy, cx + 5, cy);
                    }
                    else if (_kind == CaptionKind.Maximize)
                    {
                        if (IsRestored)
                        {
                            // restore: two overlapping squares
                            g.DrawRectangle(pen, cx - 3, cy - 5, 8, 8);
                            g.DrawLine(pen, cx - 5, cy - 2, cx - 5, cy + 5);
                            g.DrawLine(pen, cx - 5, cy + 5, cx + 2, cy + 5);
                            g.DrawLine(pen, cx - 5, cy - 2, cx - 2, cy - 2);
                        }
                        else
                        {
                            g.DrawRectangle(pen, cx - 5, cy - 5, 10, 10);
                        }
                    }
                    else
                    {
                        g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                        g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
                    }
                }
            }
        }
    }
}
