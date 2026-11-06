using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AiDesktopMulti
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                Native.TryEnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "AI Desktop Multi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal static class Theme
    {
        public static bool HighContrast
        {
            get { return SystemInformation.HighContrast; }
        }

        public static Color Canvas { get { return HighContrast ? SystemColors.Window : Rgb(15, 15, 16); } }
        public static Color Surface { get { return HighContrast ? SystemColors.Window : Rgb(23, 23, 25); } }
        public static Color SurfaceHover { get { return HighContrast ? SystemColors.Window : Rgb(29, 29, 32); } }
        public static Color Selected { get { return HighContrast ? SystemColors.Highlight : Rgb(34, 34, 38); } }
        public static Color Border { get { return HighContrast ? SystemColors.WindowText : Rgb(43, 43, 47); } }
        public static Color BorderStrong { get { return HighContrast ? SystemColors.WindowText : Rgb(60, 60, 66); } }
        public static Color Text { get { return HighContrast ? SystemColors.WindowText : Rgb(236, 236, 238); } }
        public static Color TextMuted { get { return HighContrast ? SystemColors.GrayText : Rgb(162, 162, 169); } }
        public static Color TextDim { get { return HighContrast ? SystemColors.GrayText : Rgb(119, 119, 127); } }
        public static Color Accent { get { return HighContrast ? SystemColors.Highlight : Rgb(226, 164, 92); } }
        public static Color Success { get { return HighContrast ? SystemColors.WindowText : Rgb(121, 197, 143); } }
        public static Color Warning { get { return HighContrast ? SystemColors.WindowText : Rgb(226, 164, 92); } }
        public static Color Danger { get { return HighContrast ? SystemColors.WindowText : Rgb(224, 138, 138); } }
        public static Color DangerBg { get { return HighContrast ? SystemColors.Window : Rgb(43, 25, 25); } }
        public static Color DangerBorder { get { return HighContrast ? SystemColors.WindowText : Rgb(94, 43, 43); } }
        public static Color Focus { get { return HighContrast ? SystemColors.WindowText : Rgb(155, 195, 214); } }
        public static Color InputBg { get { return HighContrast ? SystemColors.Window : Rgb(12, 12, 13); } }
        public static Color ChipBg { get { return HighContrast ? SystemColors.Window : Rgb(30, 30, 34); } }
        public static Color ChipBorder { get { return HighContrast ? SystemColors.WindowText : Rgb(53, 53, 58); } }
        public static Color SharedBg { get { return HighContrast ? SystemColors.Window : Rgb(24, 37, 28); } }
        public static Color SharedBorder { get { return HighContrast ? SystemColors.WindowText : Rgb(47, 74, 55); } }
        public static Color SharedText { get { return HighContrast ? SystemColors.WindowText : Rgb(169, 211, 180); } }
        public static Color LocalBg { get { return HighContrast ? SystemColors.Window : Rgb(38, 31, 21); } }
        public static Color LocalBorder { get { return HighContrast ? SystemColors.WindowText : Rgb(78, 61, 37); } }
        public static Color LocalText { get { return HighContrast ? SystemColors.WindowText : Rgb(214, 186, 141); } }

        private static Color Rgb(int r, int g, int b)
        {
            return Color.FromArgb(r, g, b);
        }
    }

    internal static class Draw
    {
        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Font Semibold(float size)
        {
            Font f = new Font("Segoe UI Semibold", size);
            if (string.Equals(f.Name, "Segoe UI Semibold", StringComparison.OrdinalIgnoreCase))
            {
                return f;
            }
            f.Dispose();
            return new Font("Segoe UI", size, FontStyle.Bold);
        }

        public static void Mark(Graphics g, Rectangle r)
        {
            int gap = Math.Max(2, r.Width / 9);
            int s = (r.Width - gap) / 2;
            Color edge = Theme.HighContrast ? SystemColors.WindowText : Color.FromArgb(90, 90, 98);
            Color quiet = Theme.HighContrast ? SystemColors.Window : Color.FromArgb(42, 42, 46);

            for (int i = 0; i < 4; i++)
            {
                int x = r.X + (i % 2) * (s + gap);
                int y = r.Y + (i / 2) * (s + gap);
                Rectangle cell = new Rectangle(x, y, s, s);
                using (GraphicsPath path = RoundRect(cell, Math.Max(2, s / 4)))
                {
                    if (i == 0)
                    {
                        using (SolidBrush b = new SolidBrush(Theme.Accent)) { g.FillPath(b, path); }
                    }
                    else
                    {
                        using (SolidBrush b = new SolidBrush(quiet)) { g.FillPath(b, path); }
                        using (Pen p = new Pen(edge)) { g.DrawPath(p, path); }
                    }
                }
            }
        }

        public static int Px(Graphics g, int value)
        {
            return (int)Math.Round(value * Native.ScaleFactor);
        }

        public static int Px(Control control, int value)
        {
            return (int)Math.Round(value * Native.ScaleFactor);
        }

        public static void Chip(Graphics g, Rectangle bounds, string text, Color bg, Color border, Color fg, Font font)
        {
            Size size = TextRenderer.MeasureText(text, font);
            int height = Px(g, 22);
            int pad = Px(g, 10);
            Rectangle chip = new Rectangle(bounds.Left, bounds.Top + (bounds.Height - height) / 2,
                size.Width + pad * 2, height);
            if (chip.Right > bounds.Right) { chip.Width = Math.Max(0, bounds.Width - 2); }
            using (GraphicsPath path = RoundRect(chip, height / 2))
            {
                using (SolidBrush b = new SolidBrush(bg)) { g.FillPath(b, path); }
                using (Pen p = new Pen(border)) { g.DrawPath(p, path); }
            }
            TextRenderer.DrawText(g, text, font, chip, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class ProcInfo
    {
        public int Pid;
        public string Profile;
        public DateTime Started;
        public IntPtr Window;
    }

    internal sealed class EngineResult
    {
        public readonly int ExitCode;
        public readonly string Output;
        public readonly string Error;

        public EngineResult(int exitCode, string output, string error)
        {
            ExitCode = exitCode;
            Output = output ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public string Message
        {
            get
            {
                string outText = Output.Trim();
                string[] outLines = outText.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = outLines.Length - 1; i >= 0; i--)
                {
                    string line = outLines[i].Trim();
                    if (line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                    {
                        return line.Substring(6).Trim();
                    }
                }

                string errText = Error.Trim();
                string[] errLines = errText.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (errLines.Length > 0)
                {
                    string line = errLines[errLines.Length - 1].Trim();
                    if (!line.StartsWith("<", StringComparison.Ordinal))
                    {
                        return line;
                    }
                }

                if (outLines.Length > 0) { return outLines[outLines.Length - 1].Trim(); }
                return "The action did not finish.";
            }
        }
    }

    internal static class Engine
    {
        public static EngineResult Run(string script, string action, string name, bool force)
        {
            if (!File.Exists(script))
            {
                return new EngineResult(1, string.Empty, "CodexMulti.ps1 is missing next to the app.");
            }

            string powershell = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                @"System32\WindowsPowerShell\v1.0\powershell.exe");

            StringBuilder args = new StringBuilder();
            args.Append("-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"");
            args.Append(script);
            args.Append("\" ");
            args.Append(action);
            if (!string.IsNullOrEmpty(name))
            {
                args.Append(" \"");
                args.Append(name.Replace("\"", string.Empty));
                args.Append("\"");
            }
            if (force) { args.Append(" -Force"); }

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = powershell;
            psi.Arguments = args.ToString();
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using (Process p = Process.Start(psi))
            {
                Task<string> err = Task.Run(delegate { return p.StandardError.ReadToEnd(); });
                string output = p.StandardOutput.ReadToEnd();
                bool exited = p.WaitForExit(60000);
                string error = err.Result;

                if (!exited)
                {
                    try { p.Kill(); } catch { }
                    return new EngineResult(1, output, "The helper did not finish in time.");
                }

                return new EngineResult(p.ExitCode, output, error);
            }
        }
    }

    internal static class Native
    {
        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        internal static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int command);
        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        internal static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")]
        internal static extern bool SwitchToThisWindow(IntPtr hWnd, bool altTab);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string appName, string idList);
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);
        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        internal static void TryEnableDpiAwareness()
        {
            try { SetProcessDpiAwareness(1); } catch { }
        }

        internal static void ScaleForDpi(Form form, Size minimum)
        {
            // WinForms reports DeviceDpi 96 in this host even when the window is
            // at 144, so ask the window itself. Fonts already scale with the
            // device; this scales the fixed layout to match them.
            uint dpi = GetDpiForWindow(form.Handle);
            if (dpi == 0) { dpi = GetDpiForSystem(); }
            float scale = dpi / 96f;
            ScaleFactor = scale;
            if (scale <= 1.01f) { return; }

            form.SuspendLayout();
            form.Scale(new SizeF(scale, scale));
            if (minimum != Size.Empty)
            {
                form.MinimumSize = new Size(
                    (int)Math.Round(minimum.Width * scale),
                    (int)Math.Round(minimum.Height * scale));
            }
            form.ResumeLayout(true);
        }

        internal static float ScaleFactor = 1f;

        internal static void DarkTitleBar(IntPtr hWnd)
        {
            if (Theme.HighContrast) { return; }
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hWnd, 20, ref on, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hWnd, 19, ref on, sizeof(int));
                }
            }
            catch { }
        }

        internal static void DarkScrollbars(IntPtr hWnd)
        {
            if (Theme.HighContrast) { return; }
            try { SetWindowTheme(hWnd, "DarkMode_Explorer", null); } catch { }
        }

        internal static void Focus(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) { return; }
            ShowWindow(hWnd, IsIconic(hWnd) ? 9 : 5);
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
            SwitchToThisWindow(hWnd, true);
        }

        internal static IntPtr FindWindow(int pid)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                uint owner;
                GetWindowThreadProcessId(hWnd, out owner);
                if (owner != (uint)pid) { return true; }
                if (!IsWindowVisible(hWnd)) { return true; }
                if (GetWindowTextLength(hWnd) == 0) { return true; }

                StringBuilder cls = new StringBuilder(64);
                GetClassName(hWnd, cls, cls.Capacity);
                if (!cls.ToString().StartsWith("Chrome_WidgetWin", StringComparison.Ordinal)) { return true; }

                found = hWnd;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        internal static List<ProcInfo> QueryChatGpt(string profilesRoot)
        {
            List<ProcInfo> list = new List<ProcInfo>();
            if (!Directory.Exists(profilesRoot)) { return list; }

            Dictionary<string, string> needles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in Directory.GetDirectories(profilesRoot))
            {
                string name = Path.GetFileName(dir);
                needles[name] = Path.Combine(dir, "chromium");
            }

            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine, CreationDate FROM Win32_Process WHERE Name = 'ChatGPT.exe'"))
            {
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementBaseObject raw in results)
                    {
                        using (ManagementBaseObject item = raw)
                        {
                            string commandLine = item["CommandLine"] as string;
                            if (string.IsNullOrEmpty(commandLine)) { continue; }
                            if (commandLine.IndexOf("--type=", StringComparison.OrdinalIgnoreCase) >= 0) { continue; }

                            string profile = null;
                            foreach (KeyValuePair<string, string> pair in needles)
                            {
                                if (commandLine.IndexOf(pair.Value, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    profile = pair.Key;
                                    break;
                                }
                            }
                            if (profile == null) { continue; }

                            int pid = Convert.ToInt32(item["ProcessId"]);
                            Process live = null;
                            try
                            {
                                live = Process.GetProcessById(pid);
                                if (live.HasExited) { continue; }
                            }
                            catch
                            {
                                continue;
                            }
                            finally
                            {
                                if (live != null) { live.Dispose(); }
                            }

                            DateTime started = DateTime.Now;
                            try
                            {
                                string created = item["CreationDate"] as string;
                                if (!string.IsNullOrEmpty(created))
                                {
                                    started = ManagementDateTimeConverter.ToDateTime(created);
                                }
                            }
                            catch { }

                            ProcInfo info = new ProcInfo();
                            info.Pid = pid;
                            info.Profile = profile;
                            info.Started = started;
                            info.Window = FindWindow(pid);
                            list.Add(info);
                        }
                    }
                }
            }
            return list;
        }
    }

    internal sealed class FlatButton : Button
    {
        public enum ButtonKind
        {
            Primary,
            Secondary,
            Danger,
            Ghost
        }

        private ButtonKind _kind = ButtonKind.Secondary;
        private bool _hover;
        private bool _pressed;

        public ButtonKind Kind
        {
            get { return _kind; }
            set { _kind = value; Invalidate(); }
        }

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Height = 34;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9f);
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color bg;
            Color fg;
            Color border;
            GetColors(out bg, out fg, out border);

            using (SolidBrush b = new SolidBrush(bg))
            {
                g.FillRectangle(b, ClientRectangle);
            }
            using (Pen p = new Pen(border))
            {
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }

            Rectangle textRect = new Rectangle(8, 0, Math.Max(0, Width - 16), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            if (Focused)
            {
                if (Theme.HighContrast)
                {
                    ControlPaint.DrawFocusRectangle(g, ClientRectangle, fg, bg);
                }
                else
                {
                    using (Pen p = new Pen(Theme.Focus, 2f))
                    {
                        g.DrawRectangle(p, 2, 2, Width - 5, Height - 5);
                    }
                }
            }
        }

        private void GetColors(out Color bg, out Color fg, out Color border)
        {
            if (!Enabled)
            {
                bg = Theme.Surface;
                fg = Theme.TextDim;
                border = Theme.Border;
                return;
            }

            if (Theme.HighContrast)
            {
                bg = Kind == ButtonKind.Primary ? SystemColors.Highlight : SystemColors.Window;
                fg = Kind == ButtonKind.Primary ? SystemColors.HighlightText : SystemColors.WindowText;
                border = SystemColors.WindowText;
                return;
            }

            switch (Kind)
            {
                case ButtonKind.Primary:
                    bg = _pressed ? Color.FromArgb(214, 214, 216)
                        : _hover ? Color.White
                        : Color.FromArgb(237, 237, 239);
                    fg = Color.FromArgb(18, 18, 18);
                    border = bg;
                    break;

                case ButtonKind.Danger:
                    bg = _pressed ? Color.FromArgb(74, 38, 38)
                        : _hover ? Color.FromArgb(58, 30, 30)
                        : Color.FromArgb(42, 23, 23);
                    fg = Color.FromArgb(240, 180, 180);
                    border = Color.FromArgb(106, 48, 48);
                    break;

                case ButtonKind.Ghost:
                    bg = _hover ? Theme.SurfaceHover : Theme.Canvas;
                    fg = _hover ? Theme.Text : Theme.TextMuted;
                    border = bg;
                    break;

                default:
                    bg = _pressed ? Color.FromArgb(36, 36, 40)
                        : _hover ? Theme.SurfaceHover
                        : Theme.Surface;
                    fg = Theme.Text;
                    border = _hover ? Theme.BorderStrong : Theme.Border;
                    break;
            }
        }
    }

    internal sealed class ProfileRow
    {
        public string Name;
        public string Account = "Not signed in";
        public DateTime AccountStamp = DateTime.MinValue;
        public string History = "Checking";
        public bool ConfigShared;
        public DateTime ConfigStamp = DateTime.MinValue;
        public string Status = "Idle";
        public Color StatusColor = Theme.TextMuted;
    }

    internal sealed class MainForm : Form
    {
        private readonly string _profilesRoot;
        private readonly string _scriptPath;

        private ListView _list;
        private Panel _emptyPanel;
        private Panel _header;
        private Panel _banner;
        private Panel _footer;
        private Label _activity;
        private Label _bannerText;
        private FlatButton _launch;
        private FlatButton _close;
        private FlatButton _newProfile;
        private FlatButton _shortcut;
        private FlatButton _delete;
        private FlatButton _refresh;
        private FlatButton _bannerRetry;
        private FlatButton _bannerDismiss;
        private FlatButton _emptyCreate;
        private ToolTip _tips;
        private Timer _timer;
        private RowStyle _bannerRow;
        private Panel _emptyStack;

        private readonly Dictionary<string, ProfileRow> _rows =
            new Dictionary<string, ProfileRow>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _starting =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private Font _titleFont;
        private Font _subtitleFont;
        private Font _rowFont;
        private Font _rowBold;
        private Font _smallFont;
        private Font _chipFont;
        private Font _buttonFont;
        private Font _headerFont;
        private Icon _appIcon;

        private string _signature = string.Empty;
        private int _hover = -1;
        private bool _busy;
        private bool _wmiWarningShown;
        private Action _bannerAction;

        private bool _centering;

        public MainForm()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            _profilesRoot = Path.Combine(baseDir, "profiles");
            _scriptPath = Path.Combine(baseDir, "CodexMulti.ps1");

            BuildFonts();
            BuildWindow();
            BuildUi();
            SetActivity("Reading profiles...", false);

            _timer = new Timer();
            _timer.Interval = 2000;
            _timer.Tick += OnTick;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Native.ScaleForDpi(this, new Size(720, 460));

            Rectangle area = Screen.FromControl(this).WorkingArea;
            Location = new Point(
                area.Left + (area.Width - Width) / 2,
                area.Top + (area.Height - Height) / 2);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitleBar(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyRowHeight();
            Native.DarkScrollbars(_list.Handle);
            LoadProfiles(null);
            _timer.Start();
            RepairSharedHistory();
            if (_list.Items.Count > 0) { _list.Focus(); }
        }

        private void ApplyRowHeight()
        {
            int height = Draw.Px(_list, 48);
            if (_list.SmallImageList.ImageSize.Height == height) { return; }
            _list.SmallImageList.Images.Clear();
            _list.SmallImageList.ImageSize = new Size(1, height);
            _list.SmallImageList.Images.Add(new Bitmap(1, height));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _timer.Stop();
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                RefreshAll();
                return true;
            }
            if (keyData == (Keys.Control | Keys.N))
            {
                NewProfile();
                return true;
            }
            if (keyData == Keys.Delete && _list.Focused)
            {
                DeleteSelected();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void BuildFonts()
        {
            _titleFont = Draw.Semibold(12f);
            _subtitleFont = new Font("Segoe UI", 8.75f);
            _rowFont = new Font("Segoe UI", 9.5f);
            _rowBold = Draw.Semibold(9.75f);
            _smallFont = new Font("Segoe UI", 9f);
            _chipFont = new Font("Segoe UI", 8f);
            _buttonFont = new Font("Segoe UI", 9f);
            _headerFont = Draw.Semibold(7.75f);
        }

        private void BuildWindow()
        {
            Text = "AI Desktop Multi";
            BackColor = Theme.Canvas;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.75f);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(880, 580);
            MinimumSize = new Size(720, 460);
            DoubleBuffered = true;
            KeyPreview = true;

            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"assets\app.ico");
                if (File.Exists(iconPath))
                {
                    _appIcon = new Icon(iconPath);
                    Icon = _appIcon;
                }
            }
            catch { }
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.BackColor = Theme.Canvas;
            root.Margin = Padding.Empty;
            root.Padding = Padding.Empty;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            _bannerRow = new RowStyle(SizeType.Absolute, 0);
            root.RowStyles.Add(_bannerRow);
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));

            _header = new Panel();
            _header.Dock = DockStyle.Fill;
            _header.BackColor = Theme.Canvas;
            _header.Paint += HeaderPaint;

            _banner = new Panel();
            _banner.Dock = DockStyle.Fill;
            _banner.BackColor = Theme.DangerBg;
            _banner.Visible = false;
            _banner.Paint += BannerPaint;

            Panel content = new Panel();
            content.Dock = DockStyle.Fill;
            content.BackColor = Theme.Canvas;

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.OwnerDraw = true;
            _list.BorderStyle = BorderStyle.None;
            _list.BackColor = Theme.Canvas;
            _list.ForeColor = Theme.Text;
            _list.Font = new Font("Segoe UI", 9.75f);
            _list.Columns.Add("Profile", 220);
            _list.Columns.Add("Account", 360);
            _list.Columns.Add("Status", 120);
            _list.Columns.Add("History", 120);
            _list.ShowItemToolTips = false;
            _list.AccessibleName = "Profiles";
            _list.AccessibleDescription = "Select a profile. Press Enter to launch it.";
            _list.SmallImageList = new ImageList();
            _list.SmallImageList.ImageSize = new Size(1, 48);
            _list.SmallImageList.Images.Add(new Bitmap(1, 48));
            SetDoubleBuffered(_list);

            _list.DrawColumnHeader += ListDrawColumnHeader;
            _list.DrawItem += ListDrawItem;
            _list.DrawSubItem += ListDrawSubItem;
            _list.Resize += delegate { ResizeColumns(); UpdateScrolling(); };
            _list.MouseMove += ListMouseMove;
            _list.MouseLeave += delegate { _hover = -1; _list.Invalidate(); };
            _list.DoubleClick += delegate { LaunchSelected(); };
            _list.ItemSelectionChanged += delegate { UpdateActions(); };

            _emptyPanel = new Panel();
            _emptyPanel.Dock = DockStyle.Fill;
            _emptyPanel.BackColor = Theme.Canvas;
            _emptyPanel.Visible = false;
            BuildEmptyState();
            _emptyPanel.Resize += delegate { CenterEmpty(); };

            content.Controls.Add(_list);
            content.Controls.Add(_emptyPanel);

            _footer = new Panel();
            _footer.Dock = DockStyle.Fill;
            _footer.BackColor = Theme.Canvas;
            _footer.Paint += FooterPaint;

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Top;
            actions.Height = 54;
            actions.Padding = new Padding(20, 10, 20, 0);
            actions.WrapContents = false;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.BackColor = Theme.Canvas;

            _launch = MakeButton("&Launch", FlatButton.ButtonKind.Primary, 92, "Launch this profile or focus its window. (Enter)");
            _launch.Click += delegate { LaunchSelected(); };

            _close = MakeButton("&Close", FlatButton.ButtonKind.Secondary, 84, "Close this profile's window and helper processes.");
            _close.Click += delegate { CloseSelected(); };

            Panel rule = new Panel();
            rule.Width = 1;
            rule.Height = 22;
            rule.Margin = new Padding(6, 6, 14, 0);
            rule.BackColor = Theme.Border;

            _newProfile = MakeButton("&New profile", FlatButton.ButtonKind.Secondary, 116, "Create a profile. (Ctrl+N)");
            _newProfile.Click += delegate { NewProfile(); };

            _shortcut = MakeButton("S&hortcut", FlatButton.ButtonKind.Secondary, 94, "Put a desktop shortcut for this profile.");
            _shortcut.Click += delegate { CreateShortcut(); };

            _delete = MakeButton("&Delete", FlatButton.ButtonKind.Danger, 84, "Delete this profile and its local app data. (Delete)");
            _delete.Click += delegate { DeleteSelected(); };

            actions.Controls.Add(_launch);
            actions.Controls.Add(_close);
            actions.Controls.Add(rule);
            actions.Controls.Add(_newProfile);
            actions.Controls.Add(_shortcut);
            actions.Controls.Add(_delete);

            _activity = new Label();
            _activity.Dock = DockStyle.Bottom;
            _activity.Height = 42;
            _activity.Padding = new Padding(24, 0, 24, 10);
            _activity.TextAlign = ContentAlignment.MiddleLeft;
            _activity.BackColor = Theme.Canvas;
            _activity.ForeColor = Theme.TextMuted;
            _activity.Font = new Font("Segoe UI", 8.75f);
            _activity.AutoEllipsis = true;

            _footer.Controls.Add(actions);
            _footer.Controls.Add(_activity);

            root.Controls.Add(_header, 0, 0);
            root.Controls.Add(_banner, 0, 1);
            root.Controls.Add(content, 0, 2);
            root.Controls.Add(_footer, 0, 3);
            Controls.Add(root);

            BuildHeaderControls();
            BuildBannerControls();
            BuildEmptyStateControls();

            _tips = new ToolTip();
            _tips.SetToolTip(_launch, "Launch this profile or focus its window. (Enter)");
            _tips.SetToolTip(_close, "Close this profile's window and helper processes.");
            _tips.SetToolTip(_newProfile, "Create a profile. (Ctrl+N)");
            _tips.SetToolTip(_shortcut, "Put a desktop shortcut for this profile.");
            _tips.SetToolTip(_delete, "Delete this profile and its local app data. (Delete)");
            _tips.SetToolTip(_refresh, "Refresh the profile list. (F5)");

            AcceptButton = _launch;
        }

        private void BuildHeaderControls()
        {
            _refresh = new FlatButton();
            _refresh.Kind = FlatButton.ButtonKind.Ghost;
            _refresh.Text = "&Refresh";
            _refresh.Width = 84;
            _refresh.Height = 30;
            _refresh.AccessibleName = "Refresh";
            _refresh.AccessibleDescription = "Refresh the profile list. (F5)";
            _refresh.Click += delegate { RefreshAll(); };

            _header.Controls.Add(_refresh);
            _header.Layout += delegate
            {
                _refresh.Location = new Point(
                    Math.Max(Draw.Px(_header, 8),
                        _header.ClientSize.Width - _refresh.Width - Draw.Px(_header, 24)),
                    Draw.Px(_header, 24));
            };
        }

        private void BuildBannerControls()
        {
            _bannerText = new Label();
            _bannerText.Dock = DockStyle.Fill;
            _bannerText.Padding = new Padding(24, 0, 210, 0);
            _bannerText.TextAlign = ContentAlignment.MiddleLeft;
            _bannerText.ForeColor = Theme.Danger;
            _bannerText.BackColor = Theme.DangerBg;
            _bannerText.Font = new Font("Segoe UI", 9f);
            _bannerText.AutoEllipsis = true;

            _bannerRetry = new FlatButton();
            _bannerRetry.Kind = FlatButton.ButtonKind.Secondary;
            _bannerRetry.Text = "&Retry";
            _bannerRetry.Width = 78;
            _bannerRetry.Height = 28;
            _bannerRetry.AccessibleName = "Retry";
            _bannerRetry.Click += delegate
            {
                Action action = _bannerAction;
                HideBanner();
                if (action != null) { action(); }
            };

            _bannerDismiss = new FlatButton();
            _bannerDismiss.Kind = FlatButton.ButtonKind.Ghost;
            _bannerDismiss.Text = "&Dismiss";
            _bannerDismiss.Width = 84;
            _bannerDismiss.Height = 28;
            _bannerDismiss.AccessibleName = "Dismiss";
            _bannerDismiss.Click += delegate { HideBanner(); };

            _banner.Controls.Add(_bannerText);
            _banner.Controls.Add(_bannerRetry);
            _banner.Controls.Add(_bannerDismiss);
            _banner.Layout += delegate
            {
                int right = Draw.Px(_banner, 24);
                int gap = Draw.Px(_banner, 8);
                int top = Draw.Px(_banner, 9);
                _bannerDismiss.Location = new Point(
                    Math.Max(gap, _banner.ClientSize.Width - right - _bannerDismiss.Width), top);
                _bannerRetry.Location = new Point(
                    Math.Max(gap, _bannerDismiss.Left - gap - _bannerRetry.Width), top);
            };
        }

        private void BuildEmptyState()
        {
            _emptyStack = new Panel();
            _emptyStack.Size = new Size(480, 190);
            _emptyStack.BackColor = Theme.Canvas;
            _emptyPanel.Controls.Add(_emptyStack);

            _emptyPanel.Resize += delegate { CenterEmpty(); };
        }

        private void BuildEmptyStateControls()
        {
            Label title = new Label();
            title.Text = "No profiles yet";
            title.Font = Draw.Semibold(14f);
            title.ForeColor = Theme.Text;
            title.BackColor = Theme.Canvas;
            title.TextAlign = ContentAlignment.MiddleCenter;
            title.Bounds = new Rectangle(0, 16, 480, 32);
            title.AccessibleName = "No profiles yet";

            Label body = new Label();
            body.Text = "Create a profile, launch it, and sign in with the account you want. Every window shares the same local chat history.";
            body.Font = new Font("Segoe UI", 9.25f);
            body.ForeColor = Theme.TextMuted;
            body.BackColor = Theme.Canvas;
            body.TextAlign = ContentAlignment.MiddleCenter;
            body.Bounds = new Rectangle(10, 56, 460, 52);

            _emptyCreate = new FlatButton();
            _emptyCreate.Kind = FlatButton.ButtonKind.Primary;
            _emptyCreate.Text = "&Create profile";
            _emptyCreate.Size = new Size(148, 34);
            _emptyCreate.Location = new Point(166, 126);
            _emptyCreate.AccessibleName = "Create profile";
            _emptyCreate.Click += delegate { NewProfile(); };

            _emptyStack.Controls.Add(title);
            _emptyStack.Controls.Add(body);
            _emptyStack.Controls.Add(_emptyCreate);
        }

        private static void SetDoubleBuffered(Control control)
        {
            try
            {
                PropertyInfo property = typeof(Control).GetProperty(
                    "DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
                if (property != null) { property.SetValue(control, true, null); }
            }
            catch { }
        }

        private FlatButton MakeButton(string text, FlatButton.ButtonKind kind, int width, string description)
        {
            FlatButton button = new FlatButton();
            button.Kind = kind;
            button.Text = text;
            button.Width = width;
            button.Height = 34;
            button.Margin = new Padding(0, 0, 8, 0);
            button.AccessibleName = text.Replace("&", string.Empty);
            button.AccessibleDescription = description;
            return button;
        }

        private void CenterEmpty()
        {
            if (_centering || _emptyStack == null) { return; }
            _centering = true;
            try
            {
                int left = Math.Max(0, (_emptyPanel.ClientSize.Width - _emptyStack.Width) / 2);
                int top = Math.Max(0, (_emptyPanel.ClientSize.Height - _emptyStack.Height) / 2 - 8);
                _emptyStack.Location = new Point(left, top);
            }
            finally
            {
                _centering = false;
            }
        }

        private void HeaderPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Theme.Canvas)) { g.FillRectangle(b, _header.ClientRectangle); }
            Draw.Mark(g, new Rectangle(Draw.Px(g, 24), Draw.Px(g, 27), Draw.Px(g, 22), Draw.Px(g, 22)));
            TextRenderer.DrawText(g, "AI Desktop Multi", _titleFont,
                new Point(Draw.Px(g, 58), Draw.Px(g, 20)), Theme.Text, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "Separate accounts, one local chat history.", _subtitleFont,
                new Point(Draw.Px(g, 59), Draw.Px(g, 46)), Theme.TextMuted, TextFormatFlags.NoPadding);
            using (Pen p = new Pen(Theme.Border))
            {
                g.DrawLine(p, 0, _header.Height - 1, _header.Width, _header.Height - 1);
            }
        }

        private void BannerPaint(object sender, PaintEventArgs e)
        {
            using (Pen p = new Pen(Theme.DangerBorder))
            {
                e.Graphics.DrawLine(p, 0, _banner.Height - 1, _banner.Width, _banner.Height - 1);
            }
        }

        private void FooterPaint(object sender, PaintEventArgs e)
        {
            using (Pen p = new Pen(Theme.Border))
            {
                e.Graphics.DrawLine(p, 0, 0, _footer.Width, 0);
            }
        }

        private void ListDrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.Canvas)) { e.Graphics.FillRectangle(b, e.Bounds); }
            using (Pen p = new Pen(Theme.Border))
            {
                e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
            Rectangle text = new Rectangle(e.Bounds.Left + Draw.Px(e.Graphics, 14), e.Bounds.Top,
                e.Bounds.Width - Draw.Px(e.Graphics, 14), e.Bounds.Height);
            if (e.ColumnIndex == 0) { text.X = e.Bounds.Left + Draw.Px(e.Graphics, 18); }
            TextRenderer.DrawText(e.Graphics, e.Header.Text.ToUpperInvariant(), _headerFont, text,
                Theme.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        private void ListDrawItem(object sender, DrawListViewItemEventArgs e)
        {
            Rectangle row = new Rectangle(0, e.Bounds.Top, _list.ClientSize.Width, e.Bounds.Height);
            Color bg = e.Item.Selected ? Theme.Selected
                : e.ItemIndex == _hover ? Theme.SurfaceHover
                : Theme.Canvas;

            using (SolidBrush b = new SolidBrush(bg)) { e.Graphics.FillRectangle(b, row); }

            if (e.Item.Selected && !Theme.HighContrast)
            {
                using (SolidBrush b = new SolidBrush(Theme.Accent))
                {
                    int inset = Draw.Px(e.Graphics, 8);
                    e.Graphics.FillRectangle(b, row.X, row.Y + inset,
                        Draw.Px(e.Graphics, 3), row.Height - inset * 2);
                }
            }
        }

        private void ListDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            if (e.Item == null || e.SubItem == null) { return; }
            ProfileRow row = e.Item.Tag as ProfileRow;
            if (row == null) { return; }

            bool selected = e.Item.Selected;
            Color textColor = selected && Theme.HighContrast ? SystemColors.HighlightText : Theme.Text;
            Color mutedColor = selected && Theme.HighContrast ? SystemColors.HighlightText : Theme.TextMuted;
            Rectangle bounds = e.Bounds;

            switch (e.ColumnIndex)
            {
                case 0:
                    int nameLeft = Draw.Px(e.Graphics, 18);
                    Rectangle nameRect = new Rectangle(bounds.Left + nameLeft, bounds.Top,
                        bounds.Width - nameLeft, bounds.Height);
                    TextRenderer.DrawText(e.Graphics, row.Name, _rowBold, nameRect, textColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    break;

                case 1:
                    int accountLeft = Draw.Px(e.Graphics, 14);
                    Rectangle accountRect = new Rectangle(bounds.Left + accountLeft, bounds.Top,
                        bounds.Width - accountLeft, bounds.Height);
                    TextRenderer.DrawText(e.Graphics, row.Account, _rowFont, accountRect, mutedColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    break;

                case 2:
                    int dot = Draw.Px(e.Graphics, 6);
                    int statusLeft = Draw.Px(e.Graphics, 14);
                    int dotY = bounds.Top + bounds.Height / 2 - dot / 2;
                    Color dotColor = selected && Theme.HighContrast ? SystemColors.HighlightText : row.StatusColor;
                    using (SolidBrush b = new SolidBrush(dotColor))
                    {
                        e.Graphics.FillEllipse(b, bounds.Left + statusLeft, dotY, dot, dot);
                    }
                    int textLeft = statusLeft + dot + Draw.Px(e.Graphics, 7);
                    Rectangle statusRect = new Rectangle(bounds.Left + textLeft, bounds.Top,
                        bounds.Width - textLeft, bounds.Height);
                    Color statusText = selected && Theme.HighContrast ? SystemColors.HighlightText : row.StatusColor;
                    TextRenderer.DrawText(e.Graphics, row.Status, _smallFont, statusRect, statusText,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    break;

                case 3:
                    Color chipBg = Theme.ChipBg;
                    Color chipBorder = Theme.ChipBorder;
                    Color chipText = mutedColor;

                    if (!Theme.HighContrast)
                    {
                        if (row.History == "Shared")
                        {
                            chipBg = Theme.SharedBg;
                            chipBorder = Theme.SharedBorder;
                            chipText = Theme.SharedText;
                        }
                        else if (row.History == "Repair")
                        {
                            chipBg = Theme.LocalBg;
                            chipBorder = Theme.LocalBorder;
                            chipText = Theme.LocalText;
                        }
                    }

                    int chipLeft = Draw.Px(e.Graphics, 14);
                    Rectangle chipBounds = new Rectangle(bounds.Left + chipLeft, bounds.Top,
                        bounds.Width - chipLeft, bounds.Height);
                    Draw.Chip(e.Graphics, chipBounds, row.History, chipBg, chipBorder, chipText, _chipFont);
                    break;
            }
        }

        private void ListMouseMove(object sender, MouseEventArgs e)
        {
            ListViewItem item = _list.GetItemAt(e.X, e.Y);
            int index = item == null ? -1 : item.Index;
            if (index != _hover)
            {
                _hover = index;
                _list.Invalidate();
            }
        }

        private void ResizeColumns()
        {
            if (_list == null || _list.Columns.Count < 4) { return; }
            int name = Draw.Px(_list, 220);
            int status = Draw.Px(_list, 120);
            int history = Draw.Px(_list, 120);
            int account = Math.Max(Draw.Px(_list, 170),
                _list.ClientSize.Width - name - status - history);
            _list.Columns[0].Width = name;
            _list.Columns[1].Width = account;
            _list.Columns[2].Width = status;
            _list.Columns[3].Width = history;
        }

        private void SetActivity(string text, bool error)
        {
            if (_activity == null) { return; }
            _activity.Text = text ?? string.Empty;
            _activity.ForeColor = error ? Theme.Danger : Theme.TextMuted;
        }

        private void ShowBanner(string text, Action retry)
        {
            _bannerText.Text = text;
            _bannerAction = retry;
            _bannerRetry.Visible = retry != null;
            _banner.Visible = true;
            _bannerRow.Height = (int)Math.Round(46 * Native.ScaleFactor);
            PerformLayout();
        }

        private void HideBanner()
        {
            _banner.Visible = false;
            _bannerRow.Height = 0;
            _bannerAction = null;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            UseWaitCursor = busy;
            UpdateActions();
        }

        private void UpdateActions()
        {
            bool has = SelectedName() != null;
            if (_launch != null) { _launch.Enabled = !_busy && has; }
            if (_close != null) { _close.Enabled = !_busy && has; }
            if (_delete != null) { _delete.Enabled = !_busy && has; }
            if (_shortcut != null) { _shortcut.Enabled = !_busy && has; }
            if (_newProfile != null) { _newProfile.Enabled = !_busy; }
            if (_refresh != null) { _refresh.Enabled = !_busy; }
            if (_emptyCreate != null) { _emptyCreate.Enabled = !_busy; }
        }

        private string SelectedName()
        {
            if (_list == null || _list.SelectedItems.Count == 0) { return null; }
            ProfileRow row = _list.SelectedItems[0].Tag as ProfileRow;
            return row == null ? null : row.Name;
        }

        private void LoadProfiles(string selectName)
        {
            string[] names;
            try
            {
                if (Directory.Exists(_profilesRoot))
                {
                    names = Directory.GetDirectories(_profilesRoot)
                        .Select(Path.GetFileName)
                        .Where(IsValidName)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                else
                {
                    names = new string[0];
                }
                HideBanner();
            }
            catch (Exception ex)
            {
                ShowBanner("Could not read the profiles folder. " + ex.Message,
                    delegate { LoadProfiles(null); });
                SetActivity("Could not read profiles.", true);
                return;
            }

            string signature = string.Join("|", names);
            if (!string.Equals(signature, _signature, StringComparison.Ordinal))
            {
                RebuildRows(names);
                _signature = signature;
            }

            UpdateRows();

            bool hasProfiles = names.Length > 0;
            _list.Visible = hasProfiles;
            _emptyPanel.Visible = !hasProfiles;
            if (!hasProfiles)
            {
                _emptyPanel.BringToFront();
                SetActivity("No profiles yet. Create one to start.", false);
            }
            else
            {
                if (selectName != null) { SelectRow(selectName); }
                if (_list.SelectedItems.Count == 0)
                {
                    _list.Items[0].Selected = true;
                    _list.Items[0].Focused = true;
                }
                if (string.IsNullOrEmpty(_activity.Text) ||
                    _activity.Text.StartsWith("No profiles yet", StringComparison.Ordinal))
                {
                    SetActivity("Ready. Select a profile to launch it.", false);
                }
            }

            ResizeColumns();
            UpdateScrolling();
            UpdateActions();
        }

        private void UpdateScrolling()
        {
            if (_list == null || _list.Items.Count == 0) { return; }
            int rowHeight = Math.Max(1, _list.SmallImageList.ImageSize.Height);
            int header = Draw.Px(_list, 34);
            int visible = Math.Max(1, (_list.ClientSize.Height - header) / rowHeight);
            _list.Scrollable = _list.Items.Count > visible;
        }

        private void RebuildRows(string[] names)
        {
            string keep = SelectedName();
            _rows.Clear();
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (string name in names)
                {
                    ProfileRow row = new ProfileRow();
                    row.Name = name;
                    RefreshAccount(row);
                    RefreshHistory(row);

                    ListViewItem item = new ListViewItem(name);
                    item.SubItems.Add(row.Account);
                    item.SubItems.Add(row.Status);
                    item.SubItems.Add(row.History);
                    item.ImageIndex = 0;
                    item.Name = name;
                    item.Tag = row;
                    _list.Items.Add(item);
                    _rows[name] = row;
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            if (!string.IsNullOrEmpty(keep)) { SelectRow(keep); }
        }

        private void SelectRow(string name)
        {
            foreach (ListViewItem item in _list.Items)
            {
                ProfileRow row = item.Tag as ProfileRow;
                if (row != null && string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    break;
                }
            }
        }

        private void UpdateRows()
        {
            List<ProcInfo> processes;
            try
            {
                processes = Native.QueryChatGpt(_profilesRoot);
                _wmiWarningShown = false;
            }
            catch (Exception ex)
            {
                processes = new List<ProcInfo>();
                if (!_wmiWarningShown)
                {
                    _wmiWarningShown = true;
                    SetActivity("Could not inspect running apps. " + ex.Message, true);
                }
            }

            bool changed = false;
            DateTime now = DateTime.Now;

            foreach (ListViewItem item in _list.Items)
            {
                ProfileRow row = item.Tag as ProfileRow;
                if (row == null) { continue; }

                ProcInfo live = null;
                foreach (ProcInfo p in processes)
                {
                    if (string.Equals(p.Profile, row.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        live = p;
                        break;
                    }
                }

                string status;
                Color color;

                if (live != null)
                {
                    if (live.Window != IntPtr.Zero)
                    {
                        status = "Running";
                        color = Theme.Success;
                    }
                    else if ((now - live.Started).TotalSeconds > 15)
                    {
                        status = "Stale";
                        color = Theme.Warning;
                    }
                    else
                    {
                        status = "Starting";
                        color = Theme.Warning;
                    }
                }
                else if (_starting.ContainsKey(row.Name) && _starting[row.Name] > now)
                {
                    status = "Starting";
                    color = Theme.Warning;
                }
                else
                {
                    status = "Idle";
                    color = Theme.TextMuted;
                }

                RefreshAccount(row);
                RefreshHistory(row);

                if (item.SubItems[1].Text != row.Account)
                {
                    item.SubItems[1].Text = row.Account;
                    changed = true;
                }
                if (item.SubItems[2].Text != status)
                {
                    item.SubItems[2].Text = status;
                    changed = true;
                }
                if (item.SubItems[3].Text != row.History)
                {
                    item.SubItems[3].Text = row.History;
                    changed = true;
                }

                row.Status = status;
                row.StatusColor = color;

                if (status == "Running" && _starting.ContainsKey(row.Name))
                {
                    _starting.Remove(row.Name);
                    SetActivity("Running '" + row.Name + "'.", false);
                }
            }

            if (_starting.Count > 0)
            {
                List<string> expired = new List<string>();
                foreach (KeyValuePair<string, DateTime> pair in _starting)
                {
                    if (pair.Value <= now) { expired.Add(pair.Key); }
                }
                foreach (string name in expired)
                {
                    _starting.Remove(name);
                    SetActivity("No window appeared for '" + name + "'. Try Launch again.", true);
                    ShowBanner("No Codex window appeared for '" + name + "'.",
                        delegate { LaunchProfile(name); });
                }
            }

            if (changed) { _list.Invalidate(); }
        }

        private void RefreshAccount(ProfileRow row)
        {
            string path = Path.Combine(_profilesRoot, row.Name, "codex-home", "auth.json");
            DateTime stamp = DateTime.MinValue;
            try
            {
                if (File.Exists(path)) { stamp = File.GetLastWriteTimeUtc(path); }
            }
            catch { }

            if (row.AccountStamp == stamp) { return; }

            row.AccountStamp = stamp;
            if (stamp == DateTime.MinValue)
            {
                row.Account = "Not signed in";
                return;
            }

            try
            {
                string raw = File.ReadAllText(path);
                if (raw.Trim().Length < 10)
                {
                    row.Account = "Not signed in";
                    return;
                }

                Match token = Regex.Match(raw, "\"id_token\"\\s*:\\s*\"([^\"]+)\"");
                if (token.Success)
                {
                    string[] parts = token.Groups[1].Value.Split('.');
                    if (parts.Length >= 2)
                    {
                        string payload = parts[1].Replace('-', '+').Replace('_', '/');
                        while (payload.Length % 4 != 0) { payload += "="; }
                        string json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                        Match email = Regex.Match(json, "\"email\"\\s*:\\s*\"([^\"]+)\"");
                        if (email.Success)
                        {
                            row.Account = email.Groups[1].Value;
                            return;
                        }
                    }
                }
                row.Account = "Signed in";
            }
            catch
            {
                row.Account = "Auth unreadable";
            }
        }

        private void RefreshHistory(ProfileRow row)
        {
            string configPath = Path.Combine(_profilesRoot, row.Name, "codex-home", "config.toml");
            DateTime stamp = DateTime.MinValue;
            try
            {
                if (File.Exists(configPath)) { stamp = File.GetLastWriteTimeUtc(configPath); }
            }
            catch { }

            if (row.ConfigStamp != stamp)
            {
                row.ConfigStamp = stamp;
                row.ConfigShared = ConfigPointsToShared(configPath);
            }

            bool sessions = IsReparse(Path.Combine(_profilesRoot, row.Name, "codex-home", "sessions"));
            bool archived = IsReparse(Path.Combine(_profilesRoot, row.Name, "codex-home", "archived_sessions"));

            if (row.ConfigShared && sessions && archived) { row.History = "Shared"; }
            else if (row.ConfigShared || sessions || archived) { row.History = "Repair"; }
            else { row.History = "Separate"; }
        }

        private static bool ConfigPointsToShared(string configPath)
        {
            try
            {
                if (!File.Exists(configPath)) { return false; }
                string text = File.ReadAllText(configPath);
                Match match = Regex.Match(text, "(?m)^\\s*sqlite_home\\s*=\\s*['\"]([^'\"]+)['\"]");
                if (!match.Success) { return false; }

                string expected = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                string actualPath = Path.GetFullPath(match.Groups[1].Value).TrimEnd('\\');
                string expectedPath = Path.GetFullPath(expected).TrimEnd('\\');
                return string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsReparse(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsValidName(string name)
        {
            return name != null && Regex.IsMatch(name, "^[A-Za-z0-9][A-Za-z0-9 _-]{0,31}$");
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized) { return; }
            if (_list.Visible) { UpdateRows(); }
        }

        private void RefreshAll()
        {
            LoadProfiles(SelectedName());
            if (!_banner.Visible) { SetActivity("Refreshed.", false); }
            if (_list.Items.Count > 0 && _list.SelectedItems.Count == 0)
            {
                _list.Items[0].Selected = true;
                _list.Items[0].Focused = true;
            }
            _list.Focus();
        }

        private async void RepairSharedHistory()
        {
            if (!File.Exists(_scriptPath)) { return; }
            SetActivity("Checking shared history links...", false);
            EngineResult result = await Task.Run(delegate
            {
                return Engine.Run(_scriptPath, "repair", null, false);
            });

            if (result.ExitCode != 0)
            {
                SetActivity(result.Message, true);
                ShowBanner("Shared history links could not be repaired. Launch a profile to retry.",
                    delegate { RepairSharedHistory(); });
                return;
            }

            LoadProfiles(SelectedName());
            SetActivity("Ready. Select a profile to launch it.", false);
        }

        private void LaunchSelected()
        {
            string name = SelectedName();
            if (name == null) { return; }
            LaunchProfile(name);
        }

        private async void LaunchProfile(string name)
        {
            if (_busy) { return; }
            if (!IsValidName(name)) { return; }
            if (_starting.ContainsKey(name))
            {
                SetActivity("Profile '" + name + "' is already starting.", false);
                return;
            }
            if (!File.Exists(_scriptPath))
            {
                ShowBanner("CodexMulti.ps1 is missing next to the app.", null);
                return;
            }

            try
            {
                SetBusy(true);
                ProcInfo live = FindLive(name);
                if (live != null)
                {
                    if (live.Window != IntPtr.Zero)
                    {
                        Native.Focus(live.Window);
                        SetActivity("Focused '" + name + "'.", false);
                        return;
                    }

                    if ((DateTime.Now - live.Started).TotalSeconds > 15)
                    {
                        SetActivity("Closing a stuck process for '" + name + "'...", false);
                        await Task.Run(delegate
                        {
                            return Engine.Run(_scriptPath, "close", name, false);
                        });
                    }
                }

                _starting[name] = DateTime.Now.AddSeconds(30);
                SetActivity("Starting '" + name + "'. Sign in inside the Codex window if asked.", false);

                EngineResult result = await Task.Run(delegate
                {
                    return Engine.Run(_scriptPath, "launch", name, false);
                });

                if (result.ExitCode != 0)
                {
                    _starting.Remove(name);
                    SetActivity(result.Message, true);
                    ShowBanner("The Codex window did not start. Check that the Codex app is installed.",
                        delegate { LaunchProfile(name); });
                }
            }
            catch (Exception ex)
            {
                _starting.Remove(name);
                SetActivity(ex.Message, true);
                ShowBanner("The Codex window did not start.", delegate { LaunchProfile(name); });
            }
            finally
            {
                SetBusy(false);
                UpdateRows();
            }
        }

        private ProcInfo FindLive(string name)
        {
            try
            {
                foreach (ProcInfo p in Native.QueryChatGpt(_profilesRoot))
                {
                    if (string.Equals(p.Profile, name, StringComparison.OrdinalIgnoreCase)) { return p; }
                }
            }
            catch { }
            return null;
        }

        private async void CloseSelected()
        {
            if (_busy) { return; }
            string name = SelectedName();
            if (name == null) { return; }

            ProcInfo live = FindLive(name);
            if (live == null)
            {
                SetActivity("Profile '" + name + "' is not running.", false);
                return;
            }

            SetBusy(true);
            SetActivity("Closing '" + name + "'...", false);
            try
            {
                EngineResult result = await Task.Run(delegate
                {
                    return Engine.Run(_scriptPath, "close", name, false);
                });
                if (result.ExitCode != 0)
                {
                    SetActivity(result.Message, true);
                    ShowBanner("Could not close '" + name + "'.", delegate { CloseSelected(); });
                }
                else
                {
                    _starting.Remove(name);
                    SetActivity("Closed '" + name + "'.", false);
                }
            }
            catch (Exception ex)
            {
                SetActivity(ex.Message, true);
            }
            finally
            {
                SetBusy(false);
                UpdateRows();
            }
        }

        private void NewProfile()
        {
            if (_busy) { return; }
            using (PromptDialog dialog = new PromptDialog(_profilesRoot, _appIcon))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
                RunMutation("new", dialog.ProfileName, false,
                    "Created '" + dialog.ProfileName + "'. Launch it, then sign in inside Codex.",
                    dialog.ProfileName);
            }
        }

        private void CreateShortcut()
        {
            if (_busy) { return; }
            string name = SelectedName();
            if (name == null) { return; }
            RunMutation("shortcut", name, false,
                "Desktop shortcut created for '" + name + "'.", name);
        }

        private void DeleteSelected()
        {
            if (_busy) { return; }
            string name = SelectedName();
            if (name == null) { return; }

            using (ConfirmDialog dialog = new ConfirmDialog(name, _appIcon))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
            }

            RunMutation("delete", name, true,
                "Deleted '" + name + "' and its local data.", null);
        }

        private async void RunMutation(string action, string name, bool force, string success, string selectName)
        {
            SetBusy(true);
            try
            {
                EngineResult result = await Task.Run(delegate
                {
                    return Engine.Run(_scriptPath, action, name, force);
                });

                if (result.ExitCode != 0)
                {
                    SetActivity(result.Message, true);
                    ShowBanner("That action did not finish.", null);
                }
                else
                {
                    SetActivity(success, false);
                    LoadProfiles(selectName);
                }
            }
            catch (Exception ex)
            {
                SetActivity(ex.Message, true);
                ShowBanner("That action did not finish.", null);
            }
            finally
            {
                SetBusy(false);
                UpdateRows();
            }
        }
    }

    internal sealed class PromptDialog : Form
    {
        private readonly string _profilesRoot;
        private readonly TextBox _input;
        private readonly Label _hint;
        private readonly Panel _inputBorder;
        private const string HintText = "1 to 32 characters. Letters, numbers, spaces, dashes, underscores.";

        public string ProfileName { get; private set; }

        public PromptDialog(string profilesRoot, Icon icon)
        {
            _profilesRoot = profilesRoot;
            Text = "New profile";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(468, 276);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.75f);
            KeyPreview = true;
            if (icon != null) { Icon = icon; }

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 8;
            root.Padding = new Padding(24, 20, 24, 20);
            root.BackColor = Theme.Surface;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

            Label title = new Label();
            title.Text = "New profile";
            title.Font = Draw.Semibold(14f);
            title.ForeColor = Theme.Text;
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;

            Label subtitle = new Label();
            subtitle.Text = "Each profile keeps its own sign-in, cookies, and window state.";
            subtitle.Font = new Font("Segoe UI", 9f);
            subtitle.ForeColor = Theme.TextMuted;
            subtitle.Dock = DockStyle.Fill;
            subtitle.TextAlign = ContentAlignment.MiddleLeft;

            Label label = new Label();
            label.Text = "PROFILE NAME";
            label.Font = Draw.Semibold(7.75f);
            label.ForeColor = Theme.TextMuted;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.BottomLeft;

            _inputBorder = new Panel();
            _inputBorder.Dock = DockStyle.Fill;
            _inputBorder.BackColor = Theme.Border;
            _inputBorder.Padding = new Padding(1);

            Panel inputInset = new Panel();
            inputInset.Dock = DockStyle.Fill;
            inputInset.BackColor = Theme.InputBg;

            _input = new TextBox();
            _input.BorderStyle = BorderStyle.None;
            _input.BackColor = Theme.InputBg;
            _input.ForeColor = Theme.Text;
            _input.Font = new Font("Segoe UI", 10.5f);
            _input.AccessibleName = "Profile name";
            _input.AccessibleDescription = HintText;
            _input.TextChanged += delegate
            {
                _inputBorder.BackColor = Theme.Accent;
                ResetHint();
            };

            inputInset.Controls.Add(_input);
            _inputBorder.Controls.Add(inputInset);

            _hint = new Label();
            _hint.Text = HintText;
            _hint.Font = new Font("Segoe UI", 8.25f);
            _hint.ForeColor = Theme.TextMuted;
            _hint.Dock = DockStyle.Fill;
            _hint.TextAlign = ContentAlignment.MiddleLeft;

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.BackColor = Theme.Surface;

            FlatButton create = new FlatButton();
            create.Kind = FlatButton.ButtonKind.Primary;
            create.Text = "&Create profile";
            create.Size = new Size(132, 34);
            create.Margin = new Padding(8, 0, 0, 0);
            create.AccessibleName = "Create profile";
            create.Click += OnCreate;

            FlatButton cancel = new FlatButton();
            cancel.Kind = FlatButton.ButtonKind.Secondary;
            cancel.Text = "&Cancel";
            cancel.Size = new Size(92, 34);
            cancel.Margin = Padding.Empty;
            cancel.AccessibleName = "Cancel";
            cancel.DialogResult = DialogResult.Cancel;

            buttons.Controls.Add(create);
            buttons.Controls.Add(cancel);

            root.Controls.Add(title, 0, 0);
            root.Controls.Add(subtitle, 0, 1);
            root.Controls.Add(label, 0, 3);
            root.Controls.Add(_inputBorder, 0, 4);
            root.Controls.Add(_hint, 0, 5);
            root.Controls.Add(buttons, 0, 7);
            Controls.Add(root);

            AcceptButton = create;
            CancelButton = cancel;

            _inputBorder.Resize += delegate
            {
                int margin = Draw.Px(_inputBorder, 11);
                int textHeight = Draw.Px(_inputBorder, 24);
                _input.SetBounds(margin,
                    Math.Max(Draw.Px(_inputBorder, 4), (_inputBorder.ClientSize.Height - textHeight) / 2),
                    Math.Max(40, _inputBorder.ClientSize.Width - margin * 2), textHeight);
            };
            _input.GotFocus += delegate { _inputBorder.BackColor = Theme.Accent; };
            _input.LostFocus += delegate { _inputBorder.BackColor = Theme.Border; };
            Shown += delegate
            {
                Native.DarkTitleBar(Handle);
                _input.Focus();
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitleBar(Handle);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Native.ScaleForDpi(this, Size.Empty);
        }

        private void ResetHint()
        {
            _hint.Text = HintText;
            _hint.ForeColor = Theme.TextMuted;
        }

        private void ShowError(string message)
        {
            _hint.Text = message;
            _hint.ForeColor = Theme.Danger;
            _input.Focus();
        }

        private void OnCreate(object sender, EventArgs e)
        {
            string name = _input.Text.Trim();
            if (name.Length == 0)
            {
                ShowError("Enter a profile name.");
                return;
            }
            if (!Regex.IsMatch(name, "^[A-Za-z0-9][A-Za-z0-9 _-]{0,31}$"))
            {
                ShowError("Use 1 to 32 characters. Start with a letter or number.");
                return;
            }
            if (Directory.Exists(Path.Combine(_profilesRoot, name)))
            {
                ShowError("A profile with that name already exists.");
                return;
            }

            ProfileName = name;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    internal sealed class ConfirmDialog : Form
    {
        public ConfirmDialog(string profileName, Icon icon)
        {
            Text = "Delete profile";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(500, 296);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.75f);
            if (icon != null) { Icon = icon; }

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.Padding = new Padding(24, 20, 24, 20);
            root.BackColor = Theme.Surface;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

            Label title = new Label();
            title.Text = "Delete profile";
            title.Font = Draw.Semibold(14f);
            title.ForeColor = Theme.Text;
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;

            Label question = new Label();
            question.Text = "Delete '" + profileName + "' and its local app data?";
            question.Font = new Font("Segoe UI", 10.5f);
            question.ForeColor = Theme.Text;
            question.Dock = DockStyle.Fill;
            question.TextAlign = ContentAlignment.MiddleLeft;

            Label detail = new Label();
            detail.Text = "This closes the Codex window for this profile, then removes its profile folder, sign-in, and Chromium data. Shared chat history is not deleted.";
            detail.Font = new Font("Segoe UI", 9f);
            detail.ForeColor = Theme.TextMuted;
            detail.Dock = DockStyle.Fill;
            detail.TextAlign = ContentAlignment.TopLeft;

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.BackColor = Theme.Surface;

            FlatButton delete = new FlatButton();
            delete.Kind = FlatButton.ButtonKind.Danger;
            delete.Text = "&Delete profile";
            delete.Size = new Size(132, 34);
            delete.Margin = new Padding(8, 0, 0, 0);
            delete.AccessibleName = "Delete profile";
            delete.Click += delegate
            {
                DialogResult = DialogResult.OK;
                Close();
            };

            FlatButton cancel = new FlatButton();
            cancel.Kind = FlatButton.ButtonKind.Secondary;
            cancel.Text = "&Cancel";
            cancel.Size = new Size(92, 34);
            cancel.Margin = Padding.Empty;
            cancel.AccessibleName = "Cancel";
            cancel.DialogResult = DialogResult.Cancel;

            buttons.Controls.Add(delete);
            buttons.Controls.Add(cancel);

            root.Controls.Add(title, 0, 0);
            root.Controls.Add(question, 0, 2);
            root.Controls.Add(detail, 0, 3);
            root.Controls.Add(buttons, 0, 5);
            Controls.Add(root);

            AcceptButton = cancel;
            CancelButton = cancel;

            Shown += delegate
            {
                Native.DarkTitleBar(Handle);
                cancel.Focus();
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitleBar(Handle);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Native.ScaleForDpi(this, Size.Empty);
        }
    }
}
