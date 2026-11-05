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

    // Lógica de procesos/modos. La interfaz está en LauncherForm.Ui.cs y los controles en FluentControls.cs
    sealed partial class LauncherForm : Form
    {
        readonly string _root;
        readonly string _sessionPath;
        readonly List<Process> _procs = new List<Process>();
        readonly object _procLock = new object();
        IntPtr _job = IntPtr.Zero;

        bool _running;
        bool _closing;
        string _modeTitle = "";
        bool _modeDual;
        bool _modeDocker;
        bool _modeRemote;
        bool _modeOpt;
        string _modeId = "";
        string _serverAppUrl = "";
        bool _openDistAfterBuild;
        bool _uploadPleskAfterBuild;
        Process _buildProc;
        System.Windows.Forms.Timer _watchTimer;
        readonly Dictionary<int, TimeSpan> _prevCpu = new Dictionary<int, TimeSpan>();
        DateTime _prevCpuSample = DateTime.MinValue;


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


        static bool IsOtrosSection(HomeSection section)
        {
            return section != null && string.Equals(section.Id, "Otros", StringComparison.OrdinalIgnoreCase);
        }

        void EnsureOtrosLast()
        {
            HomeSection otros = null;
            for (int i = 0; i < _homeSections.Count; i++)
            {
                if (IsOtrosSection(_homeSections[i]))
                {
                    otros = _homeSections[i];
                    break;
                }
            }
            if (otros == null) return;
            _homeSections.Remove(otros);
            _homeSections.Add(otros);
        }

        void MoveHomeSection(HomeSection section, int delta)
        {
            if (section == null || IsOtrosSection(section)) return;
            int oldIndex = _homeSections.IndexOf(section);
            if (oldIndex < 0) return;
            int newIndex = oldIndex + delta;
            if (newIndex < 0 || newIndex >= _homeSections.Count) return;
            if (IsOtrosSection(_homeSections[newIndex])) return;
            _homeSections.RemoveAt(oldIndex);
            _homeSections.Insert(newIndex, section);
            EnsureOtrosLast();
            SaveHomeSectionOrder();
            LayoutHomeTiles();
            UpdateSectionOrderButtons();
        }

        void UpdateSectionOrderButtons()
        {
            EnsureOtrosLast();
            for (int i = 0; i < _homeSections.Count; i++)
            {
                HomeSection s = _homeSections[i];
                if (s == null) continue;
                bool otros = IsOtrosSection(s);
                bool canUp = !otros && i > 0;
                bool canDown = !otros && i < _homeSections.Count - 1 && !IsOtrosSection(_homeSections[i + 1]);
                if (s.BtnUp != null)
                {
                    s.BtnUp.Enabled = canUp;
                    s.BtnUp.Visible = !otros;
                }
                if (s.BtnDown != null)
                {
                    s.BtnDown.Enabled = canDown;
                    s.BtnDown.Visible = !otros;
                }
                if (s.DragHandle != null)
                    s.DragHandle.Visible = !otros;
            }
        }

        void WireSectionDrag(HomeSection section, Control handle)
        {
            if (section == null || handle == null) return;
            handle.Cursor = Cursors.SizeAll;
            handle.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                BeginSectionDrag(section, e);
            };
            handle.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (_draggingSection && _dragSection == section)
                    MoveSectionDrag(e);
            };
            handle.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                    EndSectionDrag();
            };
        }

        void BeginSectionDrag(HomeSection section, MouseEventArgs e)
        {
            if (section == null || section.Card == null || IsOtrosSection(section)) return;
            _draggingSection = true;
            _dragSection = section;
            Point grid = _tileGrid.PointToClient(Control.MousePosition);
            _dragOffsetX = grid.X - section.Card.Left;
            _dragOffsetY = grid.Y - section.Card.Top;
            section.Card.BringToFront();
            section.Card.Cursor = Cursors.SizeAll;
            section.Card.Capture = true;
        }

        void MoveSectionDrag(MouseEventArgs e)
        {
            if (!_draggingSection || _dragSection == null || _dragSection.Card == null) return;
            Point grid = _tileGrid.PointToClient(Control.MousePosition);
            int newLeft = grid.X - _dragOffsetX;
            int newTop = grid.Y - _dragOffsetY;
            if (newLeft < 0) newLeft = 0;
            if (newTop < 0) newTop = 0;
            int maxLeft = Math.Max(0, _tileGrid.Width - _dragSection.Card.Width);
            int maxTop = Math.Max(0, _tileGrid.Height - _dragSection.Card.Height);
            if (newLeft > maxLeft) newLeft = maxLeft;
            if (newTop > maxTop) newTop = maxTop;
            _dragSection.Card.Location = new Point(newLeft, newTop);

            if (_homeScrollBar != null && _homeScrollBar.Enabled && _homeViewport != null)
            {
                Point vp = _homeViewport.PointToClient(Control.MousePosition);
                if (vp.Y < 28)
                    _homeScrollBar.Value = Math.Max(_homeScrollBar.Minimum, _homeScrollBar.Value - 18);
                else if (vp.Y > _homeViewport.Height - 28)
                {
                    int maxVal = Math.Max(_homeScrollBar.Minimum, _homeScrollBar.Maximum - Math.Max(0, _homeScrollBar.LargeChange - 1));
                    _homeScrollBar.Value = Math.Min(maxVal, _homeScrollBar.Value + 18);
                }
            }
        }

        void EndSectionDrag()
        {
            if (!_draggingSection || _dragSection == null)
            {
                _draggingSection = false;
                _dragSection = null;
                return;
            }

            HomeSection moving = _dragSection;
            try { if (moving.Card != null) moving.Card.Capture = false; } catch { }

            int cx = moving.Card.Left + moving.Card.Width / 2;
            int cy = moving.Card.Top + moving.Card.Height / 2;
            int oldIndex = _homeSections.IndexOf(moving);
            int newIndex = _homeSections.Count - 1;

            for (int i = 0; i < _homeSections.Count; i++)
            {
                HomeSection other = _homeSections[i];
                if (other == moving || other.Card == null || IsOtrosSection(other)) continue;
                int ocx = other.Card.Left + other.Card.Width / 2;
                int ocy = other.Card.Top + other.Card.Height / 2;
                bool before =
                    cy < ocy - Math.Max(12, other.Card.Height / 4) ||
                    (Math.Abs(cy - ocy) <= Math.Max(other.Card.Height, moving.Card.Height) / 2 && cx < ocx);
                if (before)
                {
                    newIndex = _homeSections.IndexOf(other);
                    if (oldIndex >= 0 && newIndex > oldIndex) newIndex--;
                    break;
                }
            }
            if (newIndex < 0) newIndex = 0;
            if (newIndex >= _homeSections.Count) newIndex = _homeSections.Count - 1;

            if (oldIndex >= 0 && oldIndex != newIndex && !IsOtrosSection(moving))
            {
                _homeSections.RemoveAt(oldIndex);
                _homeSections.Insert(newIndex, moving);
            }

            EnsureOtrosLast();
            SaveHomeSectionOrder();

            if (moving.Card != null) moving.Card.Cursor = Cursors.Default;
            _draggingSection = false;
            _dragSection = null;
            LayoutHomeTiles();
            UpdateSectionOrderButtons();
        }

        List<string> LoadHomeSectionOrder(string[] defaults)
        {
            List<string> result = new List<string>();
            try
            {
                if (!string.IsNullOrEmpty(_homeLayoutPath) && File.Exists(_homeLayoutPath))
                {
                    string[] lines = File.ReadAllLines(_homeLayoutPath, Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = (lines[i] ?? "").Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        if (!result.Contains(line)) result.Add(line);
                    }
                }
            }
            catch { }
            if (result.Count == 0 && defaults != null)
            {
                for (int i = 0; i < defaults.Length; i++)
                    result.Add(defaults[i]);
            }
            return result;
        }

        void SaveHomeSectionOrder()
        {
            try
            {
                EnsureOtrosLast();
                if (string.IsNullOrEmpty(_homeLayoutPath)) return;
                string dir = Path.GetDirectoryName(_homeLayoutPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Orden de categorías del inicio (AppInWhats DevBuild)");
                for (int i = 0; i < _homeSections.Count; i++)
                {
                    if (_homeSections[i] != null && !string.IsNullOrEmpty(_homeSections[i].Id))
                        sb.AppendLine(_homeSections[i].Id);
                }
                File.WriteAllText(_homeLayoutPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        void WireHomeWheel(Control root)
        {
            if (root == null) return;
            root.MouseWheel -= HomeMouseWheel;
            root.MouseWheel += HomeMouseWheel;
            for (int i = 0; i < root.Controls.Count; i++)
                WireHomeWheel(root.Controls[i]);
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
                case "native-legacy": StartNativeLegacy(); break;
                case "native-remote": StartNativeRemote(); break;
                case "docker-local": StartDockerLocal(); break;
                case "docker-remote": StartDockerRemote(); break;
                case "opt-local": StartOptLocal(); break;
                case "opt-remote": StartOptRemote(); break;
                case "install": StartInstall(); break;
                case "server": StartServerMode(); break;
                case "compile-front": StartCompileFrontFromHome(); break;
                case "compile-plesk": StartCompileAndUploadPlesk(); break;
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
                    "Where-Object { $_.CommandLine -match 'AppInWhats\\\\\\\\(frontend|backend)|ng serve|nest start|ts-node|nodemon' } | " +
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
            BeginMode("Native · Nest", true, false, false, false, "native-local");

            string backend = Path.Combine(_root, "backend");
            string frontend = Path.Combine(_root, "frontend");
            string cfg = Path.Combine(backend, "config.cfg");
            string mainTs = Path.Combine(backend, "src", "main.ts");
            string envFile = Path.Combine(backend, "src", ".env");
            string proxy = Path.Combine(frontend, "proxy.conf.native.json");

            if (!File.Exists(mainTs))
            {
                _termBack.Append("No hay backend Nest (falta backend/src/main.ts).\r\n", LogError);
                _termBack.Append("Usa Native · Legacy (Express) o restaura la rama Nest.\r\n", TextMuted);
                SetRunning(false);
                return;
            }
            if (!File.Exists(cfg))
            {
                _termBack.Append("Falta backend/config.cfg\r\n", LogError);
                SetRunning(false);
                return;
            }
            if (!File.Exists(envFile))
                _termBack.Append("AVISO: falta backend/src/.env (JWT/SECRET). El login puede fallar.\r\n", LogWarn);

            Dictionary<string, string> beEnv = new Dictionary<string, string>();
            beEnv["AIW_CONFIG_CFG"] = cfg;
            beEnv["PORT"] = "3001";
            beEnv["NODE_OPTIONS"] = "--max-old-space-size=768";
            beEnv["FORCE_COLOR"] = "0";

            string npm = FindNpm();
            if (npm == null)
            {
                _termBack.Append("No se encontró npm. Instala Node.js LTS.\r\n", LogError);
                SetRunning(false);
                return;
            }

            _termBack.Append("Backend NestJS (nest start --watch)\r\n", Accent);
            StartTracked(
                "Backend",
                npm,
                "run start:dev",
                backend,
                beEnv,
                _termBack);

            StartNativeFrontend(frontend, proxy);
            _termBack.Append("→ http://127.0.0.1:3001\r\n", Accent);
            _termFront.Append("→ http://localhost:50080\r\n", Accent);
        }

        void StartNativeLegacy()
        {
            if (!EnsureCanStart() || !EnsureNpx()) return;
            FreeDevPorts();
            BeginMode("Native · Legacy", true, false, false, false, "native-legacy");

            string backend = Path.Combine(_root, "backend");
            string frontend = Path.Combine(_root, "frontend");
            string cfg = Path.Combine(backend, "config.cfg");
            string indexTs = Path.Combine(backend, "scr", "index.ts");
            string envFile = Path.Combine(backend, "scr", ".env");
            string proxy = Path.Combine(frontend, "proxy.conf.native.json");

            if (!File.Exists(indexTs))
            {
                _termBack.Append("No hay backend Express legacy (falta backend/scr/index.ts).\r\n", LogError);
                SetRunning(false);
                return;
            }
            if (!File.Exists(cfg))
            {
                _termBack.Append("Falta backend/config.cfg\r\n", LogError);
                SetRunning(false);
                return;
            }
            if (!File.Exists(envFile))
                _termBack.Append("AVISO: falta backend/scr/.env (JWT/SECRET). El login puede fallar.\r\n", LogWarn);

            Dictionary<string, string> beEnv = new Dictionary<string, string>();
            beEnv["AIW_CONFIG_CFG"] = cfg;
            beEnv["PORT"] = "3001";
            beEnv["NODE_OPTIONS"] = "--max-old-space-size=768";
            beEnv["FORCE_COLOR"] = "0";

            _termBack.Append("Backend Express legacy (nodemon · scr/)\r\n", Accent);
            StartTracked(
                "Backend",
                FindNpx(),
                "--yes nodemon --watch scr --ext ts --exec \"npx --yes ts-node scr/index.ts\"",
                backend,
                beEnv,
                _termBack);

            StartNativeFrontend(frontend, proxy);
            _termBack.Append("→ http://127.0.0.1:3001\r\n", Accent);
            _termFront.Append("→ http://localhost:50080\r\n", Accent);
        }

        void StartNativeFrontend(string frontend, string proxy)
        {
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

        void StartNativeRemote()
        {
            if (!EnsureCanStart() || !EnsureNpx()) return;
            FreeDevPorts();
            BeginMode("Native · Remote", false, false, true, false, "native-remote");

            string frontend = Path.Combine(_root, "frontend");
            string proxy = Path.Combine(frontend, "proxy.conf.native.remote.json");

            _termFront.Append("API remota: https://desarrollo.appinwhats.com\r\n", LogInfo);
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

        void OpenAppInBrowser()
        {
            if (IsServerMode())
                LoadServerAppUrlFromConfig();

            string url = IsServerMode() ? _serverAppUrl : "http://localhost:50080";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = url;
                psi.UseShellExecute = true;
                Process.Start(psi);
                if (_status != null)
                {
                    _status.Text = "Abriendo  ·  " + url;
                    _status.ForeColor = Accent;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "No se pudo abrir el navegador con:\n" + url + "\n\n" + ex.Message,
                    "AppInWhats DevBuild",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        string GetServerHostPortLabel()
        {
            try
            {
                string u = _serverAppUrl ?? "";
                if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    return u.Substring(7);
                if (u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return u.Substring(8);
                return u;
            }
            catch { return "sin configurar"; }
        }

        void OpenServerSettings()
        {
            if (ShowServerConfigDialog(false))
                RefreshServerSectionUi();
        }

        string BuildServerSubtitle()
        {
            ServerConfigData cfg = ReadServerConfigData();
            if (cfg != null && cfg.IsConfigured())
                return "Remoto · " + cfg.Host + ":" + cfg.FrontendPort;
            return "Configura IP, usuario, password y puerto · primera vez te lo pide";
        }

        void RefreshServerSectionUi()
        {
            for (int i = 0; i < _homeSections.Count; i++)
            {
                HomeSection s = _homeSections[i];
                if (s == null || !string.Equals(s.Id, "Servidor", StringComparison.OrdinalIgnoreCase)) continue;
                if (s.Card != null)
                    s.Card.Subtitle = BuildServerSubtitle();
                break;
            }
            LoadServerAppUrlFromConfig();
            RefreshSettingsPage();
        }

        void StartServerMode()
        {
            if (!EnsureCanStart()) return;
            if (!EnsureServerConfig()) return;

            LoadServerAppUrlFromConfig();
            bool alreadyInSession = _sessionPanel != null && _sessionPanel.Visible && IsServerMode();
            if (!alreadyInSession)
            {
                BeginMode("Servidor remoto", true, false, false, false, "server");
                _termBack.SetTitle("Backend remoto (SSH)");
                _termFront.SetTitle("Frontend remoto · " + GetServerHostPortLabel());
                _termBack.Append("Modo ejecución en servidor\r\n", Accent);
                _termBack.Append("App en el navegador local: " + _serverAppUrl + "\r\n", TextMuted);
                _termBack.Append("Usa «Abrir app» para abrirla aquí.\r\n\r\n", TextMuted);
                _termFront.Append("Logs de despliegue / arranque remoto\r\n", Accent);
                _termFront.Append("URL: " + _serverAppUrl + "\r\n\r\n", TextMuted);
                RunServerAction("bootstrap", true);
            }
            else
            {
                SetRunning(true);
                _termBack.SetTitle("Backend remoto (SSH)");
                _termFront.SetTitle("Frontend remoto · " + GetServerHostPortLabel());
                RunServerAction("start", true);
            }
        }

        bool EnsureServerConfig()
        {
            ServerConfigData cfg = ReadServerConfigData();
            if (cfg != null && cfg.IsConfigured()) return true;
            MessageBox.Show(
                "Antes de usar el servidor remoto hay que configurarlo:\nIP, usuario, password y puerto web de salida.",
                "AppInWhats DevBuild",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            if (!ShowServerConfigDialog(true)) return false;
            RefreshServerSectionUi();
            cfg = ReadServerConfigData();
            return cfg != null && cfg.IsConfigured();
        }

        string ServerConfigPath()
        {
            return Path.Combine(_root, "launcher", "server.config.json");
        }

        ServerConfigData ReadServerConfigData()
        {
            try
            {
                string path = ServerConfigPath();
                ServerConfigData d = new ServerConfigData();
                if (!File.Exists(path)) return d;
                string json = File.ReadAllText(path, Encoding.UTF8);
                string v;
                v = ExtractJsonValue(json, "host"); if (!string.IsNullOrEmpty(v)) d.Host = v;
                v = ExtractJsonValue(json, "port"); if (!string.IsNullOrEmpty(v)) d.SshPort = v;
                v = ExtractJsonValue(json, "user"); if (!string.IsNullOrEmpty(v)) d.User = v;
                v = ExtractJsonValue(json, "password"); if (!string.IsNullOrEmpty(v)) d.Password = v;
                v = ExtractJsonValue(json, "frontendPort"); if (!string.IsNullOrEmpty(v)) d.FrontendPort = v;
                v = ExtractJsonValue(json, "backendPort"); if (!string.IsNullOrEmpty(v)) d.BackendPort = v;
                v = ExtractJsonValue(json, "remoteRoot"); if (!string.IsNullOrEmpty(v)) d.RemoteRoot = v;
                return d;
            }
            catch
            {
                return new ServerConfigData();
            }
        }

        void WriteServerConfigData(ServerConfigData d)
        {
            string path = ServerConfigPath();
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            int sshPort;
            if (!int.TryParse(d.SshPort, out sshPort)) sshPort = 22;
            int fePort;
            if (!int.TryParse(d.FrontendPort, out fePort))
                throw new InvalidOperationException("Puerto web inválido");
            int bePort;
            if (!int.TryParse(d.BackendPort, out bePort)) bePort = 3001;
            if (string.IsNullOrEmpty(d.RemoteRoot)) d.RemoteRoot = "/root/appinwhats";
            string json =
                "{\r\n" +
                "  \"host\": \"" + EscapeJson(d.Host) + "\",\r\n" +
                "  \"port\": " + sshPort + ",\r\n" +
                "  \"user\": \"" + EscapeJson(d.User) + "\",\r\n" +
                "  \"password\": \"" + EscapeJson(d.Password) + "\",\r\n" +
                "  \"remoteRoot\": \"" + EscapeJson(d.RemoteRoot) + "\",\r\n" +
                "  \"frontendPort\": " + fePort + ",\r\n" +
                "  \"backendPort\": " + bePort + "\r\n" +
                "}\r\n";
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }


        void LoadServerAppUrlFromConfig()
        {
            try
            {
                ServerConfigData d = ReadServerConfigData();
                if (d == null || !d.IsConfigured())
                {
                    _serverAppUrl = "";
                    return;
                }
                _serverAppUrl = "http://" + d.Host + ":" + d.FrontendPort;
            }
            catch
            {
                _serverAppUrl = "";
            }
        }

        static bool IsDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
                if (!char.IsDigit(s[i])) return false;
            return true;
        }

        // Lee "key": "texto" o "key": 1231 (sin confundir con la siguiente clave)
        static string ExtractJsonValue(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return "";
            string needle = "\"" + key + "\"";
            int i = 0;
            int colon = -1;
            while (true)
            {
                i = json.IndexOf(needle, i, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return "";
                int afterKey = i + needle.Length;
                int p = afterKey;
                while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
                if (p < json.Length && json[p] == ':')
                {
                    colon = p;
                    break;
                }
                i = afterKey;
            }
            if (colon < 0) return "";

            int j = colon + 1;
            while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
            if (j >= json.Length) return "";

            if (json[j] == '"')
            {
                int q2 = json.IndexOf('"', j + 1);
                if (q2 < 0) return "";
                return json.Substring(j + 1, q2 - j - 1);
            }

            int k = j;
            if (json[k] == '-') k++;
            while (k < json.Length && char.IsDigit(json[k])) k++;
            if (k <= j || (json[j] == '-' && k == j + 1)) return "";
            return json.Substring(j, k - j);
        }

        void RunServerAction(string action, bool preferBackLog)
        {
            string script = Path.Combine(_root, "launcher", "server_remote.py");
            if (!File.Exists(script))
            {
                MessageBox.Show("No se encuentra launcher/server_remote.py", "AppInWhats DevBuild",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string py = FindPython();
            if (string.IsNullOrEmpty(py))
            {
                MessageBox.Show("No se encontró Python. Instálalo para el modo servidor (SSH).",
                    "AppInWhats DevBuild", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            TerminalView term = preferBackLog ? _termBack : _termFront;
            if (term == null) term = _termFront;
            term.Append("\r\n═══ Servidor · " + action + " ═══\r\n", Accent);

            SetServerSyncEnabled(false);
            Dictionary<string, string> env = new Dictionary<string, string>();
            env["PYTHONIOENCODING"] = "utf-8";
            env["PYTHONUNBUFFERED"] = "1";

            string args = (py == "py" ? "-3 " : "") + "\"" + script + "\" " + action;
            StartTracked(
                "Server-" + action,
                py,
                args,
                _root,
                env,
                term);

            BeginInvoke(new Action(delegate
            {
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 800;
                t.Tick += delegate
                {
                    bool busy = false;
                    lock (_procLock)
                    {
                        for (int i = 0; i < _procs.Count; i++)
                        {
                            try { if (!_procs[i].HasExited) { busy = true; break; } } catch { }
                        }
                    }
                    if (!busy)
                    {
                        t.Stop();
                        t.Dispose();
                        SetServerSyncEnabled(true);
                        if (IsServerMode())
                        {
                            SetRunning(true);
                            _status.Text = "Servidor remoto  ·  " + _serverAppUrl;
                            _status.ForeColor = Accent;
                        }
                    }
                };
                t.Start();
            }));
        }

        void SetServerSyncEnabled(bool enabled)
        {
            if (_btnSyncBack != null) _btnSyncBack.Enabled = enabled;
            if (_btnSyncFront != null) _btnSyncFront.Enabled = enabled;
            if (_btnSyncBoth != null) _btnSyncBoth.Enabled = enabled;
        }

        string FindPython()
        {
            string[] candidates = new string[] { "python", "python3", "py" };
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = candidates[i];
                    psi.Arguments = "--version";
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    using (Process p = Process.Start(psi))
                    {
                        if (p == null) continue;
                        p.WaitForExit(5000);
                        if (p.ExitCode == 0) return candidates[i];
                    }
                }
                catch { }
            }
            // py launcher
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "py";
                psi.Arguments = "-3 --version";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        p.WaitForExit(5000);
                        if (p.ExitCode == 0) return "py";
                    }
                }
            }
            catch { }
            return null;
        }

        void StartCompileFrontFromHome()
        {
            if (!EnsureCanStart() || !EnsureNpx()) return;
            BeginMode("Compilar frontend", false, false, false, false, "compile-front");
            _termFront.SetTitle("Compilar frontend (production)");
            _openDistAfterBuild = true;
            _uploadPleskAfterBuild = false;
            StartFrontendProductionBuild();
        }

        void StartCompileAndUploadPlesk()
        {
            if (!EnsureCanStart() || !EnsureNpx()) return;
            if (!EnsurePleskConfig()) return;
            BeginMode("Compilar → Plesk", false, false, false, false, "compile-plesk");
            _termFront.SetTitle("Compilar y subir a Plesk");
            _openDistAfterBuild = false;
            _uploadPleskAfterBuild = true;
            StartFrontendProductionBuild();
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
                _openDistAfterBuild = false;
                return;
            }

            if (_sessionPanel == null || !_sessionPanel.Visible)
            {
                // Llamado desde toolbar sin sesión: no debería pasar; fuerza sesión mínima
                BeginMode("Compilar frontend", false, false, false, false, "compile-front");
            }

            _termFront.Append("\r\n", TermFg);
            _termFront.Append("═══ Compilar frontend (production) ═══\r\n", Accent);
            _termFront.Append("ng build --configuration=production\r\n", TextMuted);
            if (_uploadPleskAfterBuild)
                _termFront.Append("Al terminar se subirá dist a Plesk por SFTP\r\n\r\n", TextMuted);
            else if (_openDistAfterBuild)
                _termFront.Append("Al terminar se abrirá la carpeta dist\r\n\r\n", TextMuted);
            else
                _termFront.Append("(el serve en marcha no se detiene)\r\n\r\n", TextMuted);

            if (_btnBuild != null) _btnBuild.Enabled = false;
            _status.Text = "Compilando frontend (production)…";
            _status.ForeColor = Accent;

            string npx = FindNpx();
            Dictionary<string, string> env = new Dictionary<string, string>();
            env["NODE_OPTIONS"] = "--max-old-space-size=4096";
            env["NG_CLI_ANALYTICS"] = "false";
            env["FORCE_COLOR"] = "0";
            bool openDist = _openDistAfterBuild;
            bool uploadPlesk = _uploadPleskAfterBuild;
            _openDistAfterBuild = false;
            _uploadPleskAfterBuild = false;

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
                    if (e.Data != null) _termFront.Append(e.Data + "\r\n", LogStderr);
                };
                proc.Exited += delegate
                {
                    if (IsDisposed || _closing) return;
                    BeginInvoke(new Action(delegate
                    {
                        int code = SafeExitCode(proc);
                        if (code == 0)
                        {
                            _termFront.Append("\r\n■ Build production OK\r\n", Accent);
                            if (uploadPlesk)
                                StartPleskFrontUpload();
                            else if (openDist)
                                OpenFrontendDistFolder();
                        }
                        else
                            _termFront.Append("\r\n■ Build production falló · código " + code + "\r\n", Danger);

                        if (_btnBuild != null) _btnBuild.Enabled = true;
                        if (string.Equals(_modeId, "compile-front", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(_modeId, "compile-plesk", StringComparison.OrdinalIgnoreCase))
                        {
                            if (uploadPlesk && code == 0)
                            {
                                // El estado lo actualiza el upload
                            }
                            else
                            {
                                SetRunning(false);
                                _status.Text = code == 0 ? "Build OK · dist abierta" : "Build production falló";
                                _status.ForeColor = code == 0 ? Accent : Danger;
                            }
                        }
                        else if (_running)
                        {
                            _status.Text = IsServerMode()
                                ? ("Servidor remoto  ·  " + _serverAppUrl)
                                : "Ejecutando…  ·  http://localhost:50080";
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
                _termFront.Append("ERROR build: " + ex.Message + "\r\n", LogError);
                if (_btnBuild != null) _btnBuild.Enabled = true;
            }
        }

        void OpenFrontendDistFolder()
        {
            string frontend = Path.Combine(_root, "frontend");
            string[] candidates = new string[]
            {
                Path.Combine(frontend, "dist", "AiW"),
                Path.Combine(frontend, "dist"),
            };
            string folder = null;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (Directory.Exists(candidates[i]))
                {
                    folder = candidates[i];
                    break;
                }
            }
            if (folder == null)
            {
                _termFront.Append("No se encontró la carpeta dist tras el build.\r\n", Danger);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo()
                {
                    FileName = folder,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                _termFront.Append("No se pudo abrir dist: " + ex.Message + "\r\n", Danger);
            }
        }

        string PleskConfigPath()
        {
            return Path.Combine(_root, "launcher", "plesk.config.json");
        }

        sealed class PleskConfigData
        {
            public string Host = "";
            public string User = "";
            public string Password = "";
            public string Port = "22";
            public string RemotePath = "";

            public bool IsPlaceholderPassword()
            {
                if (string.IsNullOrEmpty(Password)) return true;
                string p = Password.Trim();
                return string.Equals(p, "TU_PASSWORD_AQUI", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p, "changeme", StringComparison.OrdinalIgnoreCase);
            }

            public bool IsConfigured()
            {
                return !string.IsNullOrEmpty(Host)
                    && !string.IsNullOrEmpty(User)
                    && !IsPlaceholderPassword()
                    && !string.IsNullOrEmpty(RemotePath)
                    && RemotePath.IndexOf("TU_DOMINIO", StringComparison.OrdinalIgnoreCase) < 0
                    && (string.IsNullOrEmpty(Port) || IsDigits(Port));
            }
        }

        PleskConfigData ReadPleskConfigData()
        {
            try
            {
                string path = PleskConfigPath();
                PleskConfigData d = new PleskConfigData();
                if (!File.Exists(path)) return d;
                string json = File.ReadAllText(path, Encoding.UTF8);
                d.Host = ExtractJsonValue(json, "host");
                d.User = ExtractJsonValue(json, "user");
                d.Password = ExtractJsonValue(json, "password");
                d.Port = ExtractJsonValue(json, "port");
                d.RemotePath = ExtractJsonValue(json, "remotePath");
                if (string.IsNullOrEmpty(d.Port)) d.Port = "22";
                return d;
            }
            catch
            {
                return new PleskConfigData();
            }
        }

        void WritePleskConfigData(PleskConfigData d)
        {
            string path = PleskConfigPath();
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"host\": \"" + EscapeJson(d.Host) + "\",");
            sb.AppendLine("  \"port\": " + (IsDigits(d.Port) ? d.Port : "22") + ",");
            sb.AppendLine("  \"user\": \"" + EscapeJson(d.User) + "\",");
            sb.AppendLine("  \"password\": \"" + EscapeJson(d.Password) + "\",");
            sb.AppendLine("  \"remotePath\": \"" + EscapeJson(d.RemotePath) + "\"");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        bool EnsurePleskConfig()
        {
            PleskConfigData cfg = ReadPleskConfigData();
            if (cfg != null && cfg.IsConfigured()) return true;
            MessageBox.Show(
                "Configura la subida a Plesk.\n\n"
                + "Necesitas el document root del dominio, por ejemplo:\n"
                + "/var/www/vhosts/tudominio.com/httpdocs\n\n"
                + "(El File Manager web de Plesk no sirve para automatizar; se usa SFTP/SSH.)",
                "AppInWhats DevBuild",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            if (!ShowPleskConfigDialog(true)) return false;
            cfg = ReadPleskConfigData();
            return cfg != null && cfg.IsConfigured();
        }


        void StartPleskFrontUpload()
        {
            string script = Path.Combine(_root, "launcher", "server_remote.py");
            if (!File.Exists(script))
            {
                MessageBox.Show("No se encuentra launcher/server_remote.py", "AppInWhats DevBuild",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetRunning(false);
                return;
            }
            string py = FindPython();
            if (string.IsNullOrEmpty(py))
            {
                MessageBox.Show("No se encontró Python (necesario para SFTP a Plesk).",
                    "AppInWhats DevBuild", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetRunning(false);
                return;
            }

            _termFront.Append("\r\n═══ Subir dist a Plesk (SFTP) ═══\r\n", Accent);
            _status.Text = "Subiendo front a Plesk…";
            _status.ForeColor = Accent;

            Dictionary<string, string> env = new Dictionary<string, string>();
            env["PYTHONIOENCODING"] = "utf-8";
            env["PYTHONUNBUFFERED"] = "1";
            string args = (py == "py" ? "-3 " : "") + "\"" + script + "\" upload-plesk-front";

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = py == "py" ? "py" : py;
                psi.Arguments = args.StartsWith("-3 ") ? args : ("\"" + script + "\" upload-plesk-front");
                if (py != "py")
                    psi.Arguments = "\"" + script + "\" upload-plesk-front";
                else
                    psi.Arguments = "-3 \"" + script + "\" upload-plesk-front";

                psi.WorkingDirectory = _root;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
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
                    if (e.Data != null) _termFront.Append(e.Data + "\r\n", LogStderr);
                };
                proc.Exited += delegate
                {
                    if (IsDisposed || _closing) return;
                    BeginInvoke(new Action(delegate
                    {
                        int code = SafeExitCode(proc);
                        SetRunning(false);
                        if (code == 0)
                        {
                            _termFront.Append("\r\n■ Subida a Plesk OK\r\n", Accent);
                            _status.Text = "Front subido a Plesk";
                            _status.ForeColor = Accent;
                        }
                        else
                        {
                            _termFront.Append("\r\n■ Subida a Plesk falló · código " + code + "\r\n", Danger);
                            _status.Text = "Subida a Plesk falló";
                            _status.ForeColor = Danger;
                        }
                    }));
                };
                proc.Start();
                AssignProcessToKillJob(proc);
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                _termFront.Append("ERROR subida Plesk: " + ex.Message + "\r\n", Danger);
                SetRunning(false);
                _status.Text = "Subida a Plesk falló";
                _status.ForeColor = Danger;
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

        static string FindNpm()
        {
            string[] candidates = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "npm.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "npm.cmd"),
            };
            for (int i = 0; i < candidates.Length; i++)
                if (File.Exists(candidates[i])) return candidates[i];

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] parts = pathEnv.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = Path.Combine(parts[i].Trim(), "npm.cmd");
                if (File.Exists(p)) return p;
                p = Path.Combine(parts[i].Trim(), "npm.exe");
                if (File.Exists(p)) return p;
            }
            return null;
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
                    if (e.Data != null) term.Append(e.Data + "\r\n", LogStderr);
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
                term.Append("ERROR: " + ex.Message + "\r\n", LogError);
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
            // En modo servidor los procesos locales son solo scripts de deploy;
            // la app sigue viva en el Debian aunque el script termine.
            if (IsServerMode()) return;
            try
            {
                if (_buildProc != null && !_buildProc.HasExited) return;
            }
            catch { }
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
            if (IsServerMode() && !_closing)
            {
                try
                {
                    string script = Path.Combine(_root, "launcher", "server_remote.py");
                    string py = FindPython();
                    if (!string.IsNullOrEmpty(py) && File.Exists(script))
                    {
                        if (_termFront != null)
                            _termFront.Append("\r\nParando servicios en el servidor remoto…\r\n", TextMuted);
                        ProcessStartInfo psi = new ProcessStartInfo();
                        psi.FileName = py;
                        psi.Arguments = (py == "py" ? "-3 " : "") + "\"" + script + "\" stop";
                        psi.WorkingDirectory = _root;
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        psi.RedirectStandardOutput = true;
                        psi.RedirectStandardError = true;
                        using (Process p = Process.Start(psi))
                        {
                            if (p != null) p.WaitForExit(120000);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (_termFront != null)
                        _termFront.Append("Error al parar remoto: " + ex.Message + "\r\n", Danger);
                }
            }

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
            if (!_modeDocker && !IsServerMode())
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
            if (_resumeBanner == null || !_resumeBanner.Visible) return;
            _resumeBanner.Visible = false;
            LayoutHomeTiles();
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
            ShowResumeBanner("«" + title + "» sigue abierta" + (portsBusy ? " · puertos 3001/50080 ocupados" : ""));
            _status.Text = "Aviso: hay procesos de una sesión anterior";
            _status.ForeColor = Theme.Caution;
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
                                    || cmd.IndexOf("nest start", StringComparison.OrdinalIgnoreCase) >= 0
                                    || cmd.IndexOf("start:dev", StringComparison.OrdinalIgnoreCase) >= 0
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

                ApplyStats(_statsBack, "Backend", back);
                ApplyStats(_statsFront, "Frontend", front);
                ApplyStats(_statsOther, _modeDocker ? "Docker" : "Otros", other);
                ApplyStats(_statsTotal, "Total", total);

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
            if (!string.IsNullOrEmpty(title) && title.IndexOf("Servidor", StringComparison.OrdinalIgnoreCase) >= 0)
                return "server";
            if (!string.IsNullOrEmpty(title) && title.IndexOf("Instalar", StringComparison.OrdinalIgnoreCase) >= 0)
                return "install";
            if (remote) return "native-remote";
            if (!string.IsNullOrEmpty(title) && title.IndexOf("Legacy", StringComparison.OrdinalIgnoreCase) >= 0)
                return "native-legacy";
            if (!string.IsNullOrEmpty(title) && title.IndexOf("Nest", StringComparison.OrdinalIgnoreCase) >= 0)
                return "native-local";
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

        sealed class ServerConfigData
        {
            public string Host = "";
            public string SshPort = "";
            public string User = "";
            public string Password = "";
            public string FrontendPort = "";
            public string BackendPort = "";
            public string RemoteRoot = "";

            public bool IsPlaceholderPassword()
            {
                if (string.IsNullOrEmpty(Password)) return true;
                string p = Password.Trim();
                return string.Equals(p, "TU_PASSWORD_AQUI", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p, "changeme", StringComparison.OrdinalIgnoreCase);
            }

            public bool IsConfigured()
            {
                return !string.IsNullOrEmpty(Host)
                    && !string.IsNullOrEmpty(User)
                    && !IsPlaceholderPassword()
                    && IsDigits(FrontendPort)
                    && (string.IsNullOrEmpty(SshPort) || IsDigits(SshPort));
            }
        }

        sealed partial class TerminalView
        {
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
                _btnBranch.Text = string.IsNullOrEmpty(_currentBranch) ? "rama" : TruncateBranch(_currentBranch);
                _btnBranch.AutoWidth();
                if (_btnBranch.Width > 240) _btnBranch.Width = 240;
                FluentUi.Tip(_btnBranch, "Rama actual: " + (_currentBranch.Length > 0 ? _currentBranch : "—") + " · clic para cambiar");
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

                ContextMenuStrip menu = FluentMenu.Create();

                ToolStripMenuItem refreshItem = new ToolStripMenuItem("Actualizar ramas (fetch)");
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
                ContextMenuStrip menu = FluentMenu.Create();

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

    }
}
