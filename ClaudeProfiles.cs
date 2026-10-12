// Claude Profiles: a tray app for running several Claude Desktop accounts side by side on Windows.
// Each profile = Claude started with its own --user-data-dir, a badge icon, and its own taskbar ID.
// Build with build.cmd (uses the C# compiler that ships with Windows; no SDK needed).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

class Profile
{
    public string Name, Letter, Color, Label;   // Label = optional display name
    public bool IsMain { get { return Name == "Main"; } }   // the normal Claude install, no --user-data-dir
    public bool HasBadge { get { return !string.IsNullOrEmpty(Letter); } }
    public string Display { get { return string.IsNullOrEmpty(Label) ? Name : Label; } }
    public string Key { get { return IsMain ? "" : Name; } }   // key in Core.Scan()
}

static class Core
{
    public static readonly string LocalApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static readonly string Home = Path.Combine(LocalApp, "ClaudeProfilesApp");
    static readonly string ListFile = Path.Combine(Home, "profiles.txt");
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static readonly string[][] Colors = {
        new[] { "Blue", "#2563EB" }, new[] { "Green", "#16A34A" }, new[] { "Purple", "#9333EA" }, new[] { "Red", "#DC2626" },
        new[] { "Orange", "#EA580C" }, new[] { "Teal", "#0D9488" }, new[] { "Pink", "#DB2777" }, new[] { "Gray", "#4B5563" } };

    public static string ClaudeExe()
    {
        foreach (var p in new[] { Path.Combine(LocalApp, @"Microsoft\WindowsApps\claude-desktop.exe"),
                                  Path.Combine(LocalApp, @"AnthropicClaude\claude.exe") })
            if (File.Exists(p)) return p;
        return null;
    }

    public static string AppIdOf(string name) { return "ClaudeProfile." + name; }
    public static bool ValidName(string n) { return Regex.IsMatch(n, "^[A-Za-z0-9_-]{1,32}$") && !n.Equals("Main", StringComparison.OrdinalIgnoreCase); }

    // Store installs silently redirect %LOCALAPPDATA% writes into the package's LocalCache.
    static IEnumerable<string> DataRoots()
    {
        var pk = Path.Combine(LocalApp, "Packages");
        if (Directory.Exists(pk))
            foreach (var d in Directory.GetDirectories(pk, "Claude_*")) yield return Path.Combine(d, @"LocalCache\Local");
        yield return LocalApp;
    }
    public static string RealDataDir(string name)
    {
        foreach (var r in DataRoots()) { var d = Path.Combine(r, "Claude-" + name); if (Directory.Exists(d)) return d; }
        return null;
    }

    // ---- profile list ----
    public static List<Profile> Load()
    {
        var list = new List<Profile>();
        if (File.Exists(ListFile))
            foreach (var line in File.ReadAllLines(ListFile))
            {
                var f = line.Split('|');
                if (f.Length >= 3 && (f[0] == "Main" || ValidName(f[0])))
                    list.Add(new Profile { Name = f[0], Letter = f[1], Color = f[2], Label = f.Length > 3 ? f[3] : "" });
            }
        var main = list.FirstOrDefault(p => p.IsMain) ?? new Profile { Name = "Main", Letter = "", Color = "#D97757", Label = "" };
        list.Remove(main); list.Insert(0, main);
        // Pick up profiles made elsewhere (e.g. new-profile.ps1). "Local State" marks a real Chromium profile.
        bool added = false;
        foreach (var root in DataRoots())
        {
            if (!Directory.Exists(root)) continue;
            foreach (var d in Directory.GetDirectories(root, "Claude-*"))
            {
                var name = Path.GetFileName(d).Substring(7);
                if (!File.Exists(Path.Combine(d, "Local State")) || !ValidName(name) ||
                    list.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(new Profile { Name = name, Letter = name.Substring(0, 1).ToUpper(), Color = Colors[list.Count % Colors.Length][1] });
                added = true;
            }
        }
        if (added) Save(list);
        foreach (var p in list) if (p.HasBadge && !File.Exists(IconPath(p))) WriteIcon(p);
        return list;
    }

    public static void Save(List<Profile> list)
    {
        Directory.CreateDirectory(Home);
        File.WriteAllLines(ListFile, list.Select(p => p.Name + "|" + p.Letter + "|" + p.Color + "|" + (p.Label ?? "").Replace("|", "")));
    }

    // ---- icons ----
    static Image logo;
    public static Image Logo() { return logo ?? (logo = LoadLogo()); }

    static Image LoadLogo()
    {
        try
        {   // Store package: the logo asset sits in the package folder, found via the registry.
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                if (k != null)
                    foreach (var sub in k.GetSubKeyNames().Where(n => n.StartsWith("Claude_") && n.Contains("_x64_"))
                                         .OrderByDescending(n => SafeVersion(n.Split('_')[1])))
                        using (var s = k.OpenSubKey(sub))
                        {
                            var root = s == null ? null : s.GetValue("PackageRootFolder") as string;
                            var png = root == null ? null : Path.Combine(root, @"assets\Square310x310Logo.scale-400.png");
                            if (png == null || !File.Exists(png)) continue;
                            using (var src = Image.FromFile(png))
                            {
                                var b = new Bitmap(256, 256);
                                using (var g = Graphics.FromImage(b))
                                {
                                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                    float c = src.Width * 0.52f, o = (src.Width - c) / 2;   // asset has wide padding
                                    g.DrawImage(src, new RectangleF(0, 0, 256, 256), new RectangleF(o, o, c, c), GraphicsUnit.Pixel);
                                }
                                return b;
                            }
                        }
            var exe = ClaudeExe();
            if (exe != null) { var ic = Icon.ExtractAssociatedIcon(exe); if (ic != null) return new Bitmap(ic.ToBitmap(), 256, 256); }
        }
        catch { }
        var f = new Bitmap(256, 256);   // last resort: plain Claude-orange circle
        using (var g = Graphics.FromImage(f)) { g.SmoothingMode = SmoothingMode.AntiAlias; g.FillEllipse(new SolidBrush(ColorTranslator.FromHtml("#D97757")), 8, 8, 240, 240); }
        return f;
    }
    static Version SafeVersion(string s) { Version v; return Version.TryParse(s, out v) ? v : new Version(0, 0); }

    // Claude logo with a Chrome-style letter badge in the bottom-right corner.
    public static Bitmap Badge(string letter, string color, int size)
    {
        var b = new Bitmap(size, size);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.DrawImage(Logo(), 0, 0, size, size);
            if (string.IsNullOrEmpty(letter)) return b;   // no badge: plain Claude logo
            float k = size / 256f, d = 104 * k, x = size - d - 4 * k;
            g.FillEllipse(Brushes.White, x - 7 * k, x - 7 * k, d + 14 * k, d + 14 * k);
            g.FillEllipse(new SolidBrush(ColorTranslator.FromHtml(color)), x, x, d, d);
            using (var font = new Font("Segoe UI", (letter.Length > 1 ? 46 : 62) * k, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(letter, font, Brushes.White, new RectangleF(x, x + 4 * k, d, d), fmt);
        }
        return b;
    }

    // New file name per look, so Windows' icon cache never shows a stale icon.
    public static string IconPath(Profile p)
    {
        return Path.Combine(Home, "icons", p.Name + "_" + p.Color.TrimStart('#') + "_" + (p.HasBadge ? ((int)p.Letter[0]).ToString("X") : "0") + ".ico");
    }

    public static void WriteIcon(Profile p)
    {
        var dir = Path.Combine(Home, "icons");
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, p.Name + "_*.ico")) File.Delete(old);
        byte[] png;
        using (var bmp = Badge(p.Letter, p.Color, 256)) using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); png = ms.ToArray(); }
        using (var w = new BinaryWriter(File.Create(IconPath(p))))
        {   // .ico holding one PNG frame (supported since Vista)
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)1);
            w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)32); w.Write((uint)png.Length); w.Write((uint)22);
            w.Write(png);
        }
    }

    // ---- launching, shortcuts, startup ----
    public static void Open(string name)
    {
        var exe = ClaudeExe();
        if (exe == null) { MessageBox.Show("Claude Desktop isn't installed. Get it from claude.ai/download.", "Claude Profiles"); return; }
        if (string.IsNullOrEmpty(name) || name == "Main") Process.Start(exe);
        else Process.Start(exe, "--user-data-dir=\"" + Path.Combine(LocalApp, "Claude-" + name) + "\"");
    }

    // Claude's own close button only hides it to the tray, so stopping means ending its processes.
    // Ends the whole tree at once (taskkill /T) so Claude can't respawn helpers mid-way.
    public static void Stop(IEnumerable<uint> pids)
    {
        var log = new System.Text.StringBuilder(DateTime.Now + " stop " + string.Join(",", pids) + "\r\n");
        foreach (var id in pids)
        {
            try
            {
                var psi = new ProcessStartInfo("taskkill.exe", "/PID " + id + " /T /F")
                    { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    log.Append(p.StandardOutput.ReadToEnd()).Append(p.StandardError.ReadToEnd());
                    p.WaitForExit(5000);
                }
            }
            catch (Exception ex) { log.AppendLine(id + ": " + ex.Message); }
        }
        try { Directory.CreateDirectory(Home); File.AppendAllText(Path.Combine(Home, "stop.log"), log.ToString()); } catch { }
    }

    public static string[] ShortcutPaths(string name)
    {
        return new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Claude " + name + ".lnk"),
                       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Claude " + name + ".lnk") };
    }

    public static void CreateShortcuts(Profile p)
    {
        dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        foreach (var lnk in ShortcutPaths(p.Name))
        {
            var s = sh.CreateShortcut(lnk);
            s.TargetPath = Application.ExecutablePath;
            s.Arguments = "--open " + p.Name;
            s.IconLocation = IconPath(p) + ",0";
            s.Description = "Claude Desktop (" + p.Display + ")";
            s.Save();
            AppId.TagShortcut(lnk, AppIdOf(p.Name));
        }
    }

    public static bool StartupEnabled()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("ClaudeProfiles") != null;
    }
    public static void SetStartup(bool on)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            if (on) k.SetValue("ClaudeProfiles", "\"" + Application.ExecutablePath + "\" --tray");
            else k.DeleteValue("ClaudeProfiles", false);
    }

    // ---- running Claude processes, grouped by profile ("" = main install) ----
    public static Dictionary<string, List<uint>> Scan()
    {
        var r = new Dictionary<string, List<uint>>(StringComparer.OrdinalIgnoreCase);
        using (var s = new ManagementObjectSearcher("SELECT ProcessId, CommandLine, ExecutablePath FROM Win32_Process WHERE Name='claude.exe'"))
            foreach (ManagementObject o in s.Get())
            {
                var cmd = o["CommandLine"] as string;
                // Only Claude Desktop (Claude Code's CLI is also claude.exe). Judge by the real exe path:
                // the main process's command line shows the claude-desktop.exe alias instead.
                var exe = (o["ExecutablePath"] as string) ?? cmd ?? "";
                if (cmd == null || (exe.IndexOf(@"\WindowsApps\Claude_", StringComparison.OrdinalIgnoreCase) < 0 &&
                                    exe.IndexOf(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase) < 0)) continue;
                string key = "";
                int i = cmd.IndexOf("--user-data-dir=");
                if (i >= 0)
                {
                    var v = cmd.Substring(i + 16).TrimStart('"');
                    int e = v.IndexOf('"'); if (e < 0) e = v.IndexOf(" --"); if (e >= 0) v = v.Substring(0, e);
                    var leaf = Path.GetFileName(v.TrimEnd('\\'));
                    key = leaf.StartsWith("Claude-", StringComparison.OrdinalIgnoreCase) ? leaf.Substring(7) : "?" + leaf;
                }
                List<uint> l;
                if (!r.TryGetValue(key, out l)) r[key] = l = new List<uint>();
                l.Add(Convert.ToUInt32(o["ProcessId"]));
            }
        return r;
    }
}

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    public static float Scale = 1;
    public static int S(float v) { return (int)Math.Round(v * Scale); }

    [STAThread]
    static void Main(string[] args)
    {
        string open = null; bool tray = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--open" && i + 1 < args.Length) open = args[++i];
            if (args[i] == "--tray") tray = true;
        }

        bool first;
        var mutex = new Mutex(true, @"Local\ClaudeProfilesApp", out first);
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ClaudeProfilesApp.Show");
        if (!first)
        {   // Already running: it will tag the new window. Just launch, or bring up its window.
            if (open != null) Core.Open(open == "Main" ? null : open); else showEvent.Set();
            return;
        }

        // Our own taskbar identity. Without it, being started from a profile's shortcut (which carries
        // that profile's ID), or matching a pin of this same exe, puts this window under that profile's button.
        SetCurrentProcessExplicitAppUserModelID("ClaudeProfiles.App");
        SetProcessDPIAware();
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var app = new TrayApp(showEvent);
        if (open != null) Core.Open(open == "Main" ? null : open);
        else if (!tray) app.ShowMain();
        Application.Run(app);
        GC.KeepAlive(mutex);
    }
}

class TrayApp : ApplicationContext
{
    public volatile List<Profile> Profiles;
    public Dictionary<string, List<uint>> Running = new Dictionary<string, List<uint>>();
    public readonly Icon AppIcon;
    readonly NotifyIcon tray;
    readonly Control ui = new Control();
    readonly System.Threading.Timer timer;
    MainForm main;
    int busy;
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

    public TrayApp(EventWaitHandle showEvent)
    {
        ui.CreateControl();
        var h = ui.Handle;   // force handle so BeginInvoke works
        Profiles = Core.Load();
        AppIcon = LoadAppIcon(Size.Empty);
        tray = new NotifyIcon { Icon = LoadAppIcon(SystemInformation.SmallIconSize), Text = "Claude Profiles", Visible = true };
        var menu = new ContextMenuStrip();
        tray.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) { ShowMain(); return; }
            if (e.Button != MouseButtons.Right) return;
            // Placed by hand: WinForms' own tray-menu placement overlaps the taskbar on high-DPI screens.
            BuildMenu(menu);
            var at = Cursor.Position;
            var area = Screen.FromPoint(at).WorkingArea;
            SetForegroundWindow(ui.Handle);   // lets the menu close when clicking elsewhere
            menu.Show(new Point(at.X, Math.Min(at.Y, area.Bottom)), ToolStripDropDownDirection.AboveLeft);
        };

        var t = new Thread(() => { while (showEvent.WaitOne()) ui.BeginInvoke((Action)ShowMain); }) { IsBackground = true };
        t.Start();
        timer = new System.Threading.Timer(_ => Tick(), null, 0, 1500);
    }

    // app.ico is embedded by build.cmd (/resource) so every size stays crisp.
    static Icon LoadAppIcon(Size size)
    {
        using (var s = typeof(TrayApp).Assembly.GetManifestResourceStream("app.ico"))
            return size.IsEmpty ? new Icon(s) : new Icon(s, size);
    }

    // Every 1.5 s: give each profile's windows their own taskbar ID, and track what's running.
    // Main keeps Claude's normal taskbar button unless it was given a badge.
    void Tick()
    {
        if (Interlocked.Exchange(ref busy, 1) == 1) return;
        try
        {
            var r = Core.Scan();
            foreach (var p in Profiles)
            {
                List<uint> pids;
                if ((!p.IsMain || p.HasBadge) && r.TryGetValue(p.Key, out pids))
                    AppId.TagWindows(pids.ToArray(), Core.AppIdOf(p.Name),
                        "\"" + Application.ExecutablePath + "\" --open " + p.Name, Core.IconPath(p) + ",0", "Claude " + p.Display);
            }
            var changed = !r.Keys.OrderBy(k => k).SequenceEqual(Running.Keys.OrderBy(k => k));
            Running = r;
            if (changed) ui.BeginInvoke((Action)(() => { if (main != null && main.Visible) main.Rebuild(); }));
        }
        catch { }
        finally { busy = 0; }
    }

    public bool IsRunning(Profile p) { return Running.ContainsKey(p.Key); }

    public void Stop(Profile p, IWin32Window owner)
    {
        List<uint> pids;
        if (!Running.TryGetValue(p.Key, out pids)) return;
        if (MessageBox.Show(owner, "Stop Claude (" + p.Display + ")?\n\nAnything not yet sent in that window will be lost.", "Claude Profiles",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Core.Stop(pids);
    }

    public void Reload() { Profiles = Core.Load(); if (main != null) main.Rebuild(); }

    public void ShowMain()
    {
        if (main == null || main.IsDisposed) main = new MainForm(this);
        main.Rebuild();
        main.Show();
        if (main.WindowState == FormWindowState.Minimized) main.WindowState = FormWindowState.Normal;
        main.Activate();
    }

    void BuildMenu(ContextMenuStrip m)
    {
        m.Items.Clear();
        m.Items.Add(new ToolStripLabel("Claude Profiles") { ForeColor = Color.Gray });
        m.Items.Add(new ToolStripSeparator());
        foreach (var p in Profiles)
        {
            var pr = p;
            m.Items.Add(p.Display + (IsRunning(p) ? "   •" : ""), Core.Badge(p.Letter, p.Color, 32), (s, e) => Core.Open(pr.Name));
        }
        var running = Profiles.Where(IsRunning).ToList();
        if (running.Count > 0)
        {
            var stop = new ToolStripMenuItem("Stop");
            foreach (var p in running) { var pr = p; stop.DropDownItems.Add(p.Display, Core.Badge(p.Letter, p.Color, 32), (s, e) => Stop(pr, null)); }
            m.Items.Add(stop);
        }
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Add account…", null, (s, e) => { ShowMain(); main.AddProfile(); });
        m.Items.Add("Manage accounts…", null, (s, e) => ShowMain());
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Exit", null, (s, e) => { timer.Dispose(); tray.Visible = false; ExitThread(); });
    }
}

class MainForm : Form
{
    readonly TrayApp app;
    readonly FlowLayoutPanel list;
    readonly Label count;
    static int S(float v) { return Program.S(v); }

    public MainForm(TrayApp app)
    {
        this.app = app;
        Text = "Claude Profiles"; Icon = app.AppIcon; AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Segoe UI", 9.5f); BackColor = Color.White; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(S(640), S(500)); MinimumSize = new Size(S(520), S(360));

        var title = new Label { Text = "Your Claude accounts", Font = new Font("Segoe UI Semibold", 18f), AutoSize = true, Location = new Point(S(20), S(16)) };
        var sub = new Label { Text = "Separate sign-ins, each with its own badge and taskbar button.", ForeColor = Color.DimGray, AutoSize = true, Location = new Point(S(23), S(58)) };
        var add = new Button { Text = "+ Add account", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right, FlatStyle = FlatStyle.System };
        add.Size = add.PreferredSize;
        add.Location = new Point(ClientSize.Width - add.Width - S(20), S(22));
        add.Click += (s, e) => AddProfile();
        count = new Label { ForeColor = Color.DimGray, AutoSize = true, Location = new Point(S(23), S(92)) };

        list = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            Location = new Point(S(16), S(118)), Size = new Size(ClientSize.Width - S(32), ClientSize.Height - S(170)),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
        list.Resize += (s, e) => { foreach (Control c in list.Controls) c.Width = RowWidth(); };

        var startup = new CheckBox { Text = "Start with Windows (keeps taskbar buttons separate however Claude is opened)",
            AutoSize = true, Checked = Core.StartupEnabled(), ForeColor = Color.DimGray,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Location = new Point(S(22), ClientSize.Height - S(44)) };
        startup.CheckedChanged += (s, e) => Core.SetStartup(startup.Checked);
        var tip = new Label { Text = "Closing this window keeps Claude Profiles in the tray. Sign-in trouble? Use the email-code sign-in.",
            ForeColor = Color.Gray, AutoSize = true, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Location = new Point(S(22), ClientSize.Height - S(22)) };

        Controls.AddRange(new Control[] { title, sub, add, count, list, startup, tip });
        FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }

    int RowWidth() { return list.ClientSize.Width - S(6); }

    public void Rebuild()
    {
        list.SuspendLayout();
        foreach (Control c in list.Controls.Cast<Control>().ToList()) c.Dispose();
        foreach (var p in app.Profiles) list.Controls.Add(Row(p));
        list.ResumeLayout();
        count.Text = app.Profiles.Count + " accounts · " + app.Profiles.Count(app.IsRunning) + " running";
    }

    Button RowButton(Control row, string text, ref int right)
    {
        var b = new Button { Text = text, Anchor = AnchorStyles.Top | AnchorStyles.Right, FlatStyle = FlatStyle.System };
        b.Size = new Size(b.PreferredSize.Width + S(8), b.PreferredSize.Height + S(4));
        b.Location = new Point(right - b.Width, (row.Height - b.Height) / 2);
        row.Controls.Add(b);
        right = b.Left - S(8);
        return b;
    }

    Control Row(Profile p)
    {
        bool running = app.IsRunning(p);
        var bar = ColorTranslator.FromHtml(p.Color);
        // Font set up front: the row isn't on the form yet, and PreferredWidth below must measure with the real font.
        var row = new Panel { Height = S(76), Width = RowWidth(), Margin = new Padding(0, 0, 0, S(10)), BackColor = Color.White, Font = Font };
        row.Paint += (s, e) =>
        {
            e.Graphics.DrawRectangle(Pens.Gainsboro, 0, 0, row.Width - 1, row.Height - 1);
            using (var b = new SolidBrush(bar)) e.Graphics.FillRectangle(b, S(10), S(14), S(4), row.Height - S(28));
        };
        var pic = new PictureBox { Size = new Size(S(44), S(44)), Location = new Point(S(26), S(16)), SizeMode = PictureBoxSizeMode.Zoom,
            Image = Core.Badge(p.Letter, p.Color, S(44)) };
        var nm = new Label { Text = p.Display, Font = new Font("Segoe UI Semibold", 11.5f), AutoSize = true, Location = new Point(S(82), S(12)) };
        var info = new Label { AutoSize = true, Location = new Point(S(84), S(42)),
            Text = running ? "●  Running" : "○  Not running",
            ForeColor = running ? Color.FromArgb(22, 163, 74) : Color.Gray };
        row.Controls.AddRange(new Control[] { pic, nm, info });
        var note = p.IsMain ? "your normal Claude install" : (p.Display != p.Name ? "profile: " + p.Name : null);
        if (note != null)
            row.Controls.Add(new Label { Text = "·  " + note, AutoSize = true, ForeColor = Color.Gray,
                Location = new Point(info.Left + info.PreferredWidth + S(6), info.Top) });

        int right = row.Width - S(16);
        var manage = RowButton(row, "Manage ▾", ref right);
        manage.Click += (s, e) => ManageMenu(p).Show(manage, new Point(0, manage.Height));
        if (running) RowButton(row, "Stop", ref right).Click += (s, e) => app.Stop(p, this);
        RowButton(row, running ? "Switch to" : "Open Claude", ref right).Click += (s, e) => Core.Open(p.Name);
        return row;
    }

    ContextMenuStrip ManageMenu(Profile p)
    {
        var m = new ContextMenuStrip();
        m.Items.Add("Edit name && badge…", null, (s, e) => EditProfile(p));
        var sc = m.Items.Add("Create Desktop && Start Menu shortcuts", null, (s, e) =>
        {
            Core.CreateShortcuts(p);
            MessageBox.Show(this, "Shortcuts created. To pin: right-click the Desktop shortcut → Show more options → Pin to taskbar." +
                (p.IsMain ? "\n\nUse this pin instead of the regular Claude pin, so Main's windows group under it." : ""), "Claude Profiles");
        });
        if (p.IsMain)
        {   // Main has no data folder of ours to open or delete.
            sc.Enabled = p.HasBadge;
            if (!p.HasBadge) sc.Text += " (give Main a badge first)";
            return m;
        }
        m.Items.Add("Open data folder", null, (s, e) =>
        {
            var d = Core.RealDataDir(p.Name);
            if (d != null) Process.Start("explorer.exe", "\"" + d + "\"");
            else MessageBox.Show(this, "No data yet. Open this profile and sign in first.", "Claude Profiles");
        });
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Delete profile…", null, (s, e) => DeleteProfile(p)).ForeColor = Color.Firebrick;
        return m;
    }

    public void AddProfile()
    {
        using (var d = new ProfileDialog(null, app.Profiles))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            var p = d.Result;
            Core.WriteIcon(p);
            var list = app.Profiles.ToList(); list.Add(p); Core.Save(list);
            try { Core.CreateShortcuts(p); } catch { }
            app.Reload();
            Core.Open(p.Name);
        }
    }

    void EditProfile(Profile p)
    {
        using (var d = new ProfileDialog(p, app.Profiles))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            p.Letter = d.Result.Letter; p.Color = d.Result.Color; p.Label = d.Result.Label;
            if (p.HasBadge) Core.WriteIcon(p);
            Core.Save(app.Profiles);
            if (p.HasBadge && Core.ShortcutPaths(p.Name).Any(File.Exists)) try { Core.CreateShortcuts(p); } catch { }
            app.Reload();
        }
    }

    void DeleteProfile(Profile p)
    {
        if (app.IsRunning(p)) { MessageBox.Show(this, "Stop " + p.Display + " first (Stop button).", "Claude Profiles"); return; }
        var data = Core.RealDataDir(p.Name);
        var msg = "Delete the \"" + p.Display + "\" profile?\n\n" +
                  "This signs it out on this PC and permanently deletes its local data" + (data != null ? ":\n" + data : ".") +
                  "\n\nIts shortcuts and icon are removed too. Chats saved in the Claude account itself are not affected.";
        if (MessageBox.Show(this, msg, "Delete profile", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            foreach (var l in Core.ShortcutPaths(p.Name)) if (File.Exists(l)) File.Delete(l);
            if (File.Exists(Core.IconPath(p))) File.Delete(Core.IconPath(p));
            if (data != null) Directory.Delete(data, true);
        }
        catch (Exception ex) { MessageBox.Show(this, "Couldn't delete everything: " + ex.Message, "Claude Profiles"); }
        Core.Save(app.Profiles.Where(x => x != p).ToList());
        app.Reload();
    }
}

class ProfileDialog : Form
{
    public Profile Result;
    readonly TextBox name, label, letter;
    readonly ComboBox color;
    readonly PictureBox preview;
    readonly Label error;
    readonly bool isMain;
    static int S(float v) { return Program.S(v); }

    public ProfileDialog(Profile edit, List<Profile> existing)
    {
        isMain = edit != null && edit.IsMain;
        Text = edit == null ? "Add a Claude account" : "Edit " + edit.Display;
        Font = new Font("Segoe UI", 9.5f); AutoScaleMode = AutoScaleMode.None; BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(S(380), S(372));

        preview = new PictureBox { Size = new Size(S(64), S(64)), Location = new Point(S(20), S(18)), SizeMode = PictureBoxSizeMode.Zoom };
        var help = new Label { Location = new Point(S(96), S(16)), Size = new Size(S(270), S(72)), ForeColor = Color.DimGray,
            Text = edit == null ? "Pick a name and a badge. Then sign in to that account in the Claude window that opens."
                 : isMain ? "Leave the letter empty to keep Claude's normal icon and taskbar button. With a badge, Main gets its own button too."
                 : "Change how this profile is shown. Its sign-in and data stay as they are." };

        var l1 = new Label { Text = "Profile ID (letters, digits, - or _)", AutoSize = true, Location = new Point(S(20), S(96)) };
        name = new TextBox { Location = new Point(S(20), S(118)), Width = S(340), Text = edit == null ? "" : edit.Name, Enabled = edit == null };
        var l4 = new Label { Text = "Display name (optional)", AutoSize = true, Location = new Point(S(20), S(154)) };
        label = new TextBox { Location = new Point(S(20), S(176)), Width = S(340), MaxLength = 40, Text = edit == null ? "" : edit.Label };
        var l2 = new Label { Text = "Badge letter", AutoSize = true, Location = new Point(S(20), S(212)) };
        letter = new TextBox { Location = new Point(S(20), S(234)), Width = S(60), MaxLength = 2, Text = edit == null ? "" : edit.Letter };
        var l3 = new Label { Text = "Color", AutoSize = true, Location = new Point(S(100), S(212)) };
        color = new ComboBox { Location = new Point(S(100), S(234)), Width = S(150), DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed };
        foreach (var c in Core.Colors) color.Items.Add(c[0]);
        color.DrawItem += (s, e) =>
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            using (var b = new SolidBrush(ColorTranslator.FromHtml(Core.Colors[e.Index][1])))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillEllipse(b, e.Bounds.X + S(4), e.Bounds.Y + (e.Bounds.Height - S(12)) / 2, S(12), S(12));
            }
            TextRenderer.DrawText(e.Graphics, Core.Colors[e.Index][0], e.Font, new Point(e.Bounds.X + S(22), e.Bounds.Y + 1), e.ForeColor);
        };
        int ci = edit == null ? existing.Count % Core.Colors.Length : Array.FindIndex(Core.Colors, c => c[1].Equals(edit.Color, StringComparison.OrdinalIgnoreCase));
        color.SelectedIndex = Math.Max(0, ci);

        error = new Label { ForeColor = Color.Firebrick, AutoSize = true, Location = new Point(S(20), S(272)) };
        var ok = new Button { Text = edit == null ? "Add && Open" : "Save", Size = new Size(S(110), S(32)), Location = new Point(S(250), S(320)), FlatStyle = FlatStyle.System };
        var cancel = new Button { Text = "Cancel", Size = new Size(S(100), S(32)), Location = new Point(S(140), S(320)), DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.System };
        AcceptButton = ok; CancelButton = cancel;

        EventHandler update = (s, e) =>
        {
            if (edit == null && s == name) letter.Text = name.Text.Length > 0 ? name.Text.Substring(0, 1).ToUpper() : "";
            var old = preview.Image;
            preview.Image = Core.Badge(Letter(), Core.Colors[color.SelectedIndex][1], S(64));
            if (old != null) old.Dispose();
        };
        name.TextChanged += update; letter.TextChanged += update; color.SelectedIndexChanged += update;
        update(null, EventArgs.Empty);

        ok.Click += (s, e) =>
        {
            var n = name.Text.Trim();
            if (edit == null && !Core.ValidName(n)) { error.Text = "Use 1–32 letters, digits, - or _ (and not \"Main\")."; return; }
            if (edit == null && existing.Any(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) { error.Text = "A profile with that ID already exists."; return; }
            Result = new Profile { Name = edit == null ? n : edit.Name, Letter = Letter(), Color = Core.Colors[color.SelectedIndex][1],
                                   Label = label.Text.Trim().Replace("|", "") };
            DialogResult = DialogResult.OK;
        };
        Controls.AddRange(new Control[] { preview, help, l1, name, l4, label, l2, letter, l3, color, error, ok, cancel });
    }

    // Profiles always need a badge (it's how they're told apart); Main may have none.
    string Letter() { var t = letter.Text.Trim().ToUpper(); return t.Length > 0 || isMain ? t : "?"; }
}
