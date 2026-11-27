using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Bootstrap
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--test-install")
        {
            try { return Install(args[1], true, args[2], args[3], delegate(string s) {}); }
            catch (Exception e) { File.AppendAllText(args[3], e + Environment.NewLine); return 1; }
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupWindow());
        return 0;
    }

    static string Q(string s)
    {
        if (s.IndexOf('"') >= 0 || s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0)
            throw new ArgumentException("Invalid path");
        return "\"" + s.TrimEnd('\\') + "\"";
    }

    static void Extract(string name, string file)
    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (s == null) throw new InvalidDataException("Missing installer resource " + name);
            using (FileStream f = File.Create(file)) s.CopyTo(f);
        }
    }

    internal static int Install(string destination, bool noShortcut, string cache, string log, Action<string> output)
    {
        string temp = Path.Combine(Path.GetTempPath(), "hero-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(log)));
        object gate = new object();
        Action<string> emit = delegate(string line) {
            if (line == null) return;
            lock (gate) { File.AppendAllText(log, line + Environment.NewLine); }
            output(line);
        };
        try
        {
            string payload = Path.Combine(temp, "hero.zip");
            string script = Path.Combine(temp, "InstallOnline.ps1");
            Extract("hero.payload", payload);
            Extract("hero.install", script);
            string hash;
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(payload)) hash = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
            if (!String.Equals(hash, PayloadIdentity.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Embedded application checksum mismatch.");
            string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe");
            var info = new ProcessStartInfo(ps);
            info.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Q(script)
                + " -Payload " + Q(payload) + " -PayloadSha256 " + hash + " -Destination " + Q(destination);
            if (noShortcut) info.Arguments += " -NoShortcut";
            if (!String.IsNullOrEmpty(cache)) info.Arguments += " -VerifiedCache " + Q(cache);
            info.UseShellExecute = false; info.CreateNoWindow = true;
            info.RedirectStandardOutput = true; info.RedirectStandardError = true;
            using (Process p = new Process())
            {
                p.StartInfo = info;
                p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { emit(e.Data); };
                p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { emit(e.Data); };
                p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine(); p.WaitForExit();
                return p.ExitCode;
            }
        }
        finally { try { Directory.Delete(temp, true); } catch {} }
    }
}

sealed class SetupWindow : Form
{
    TextBox destination = new TextBox(), console = new TextBox();
    Button browse = new Button(), install = new Button(), open = new Button();
    ProgressBar progress = new ProgressBar();
    Label state = new Label();
    CheckBox shortcut = new CheckBox();
    bool busy;
    string logPath;
    public SetupWindow()
    {
        Text = "HERO AVR Studio | Setup";
        ClientSize = new Size(680, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(24, 29, 38); ForeColor = Color.White;
        Label title = new Label { Text = "HERO AVR Studio", Font = new Font("Segoe UI", 25, FontStyle.Bold), AutoSize = true, Location = new Point(24, 18) };
        Label subtitle = new Label { Text = "AI-first AVR development & PCB production  |  Version 2.0.1", AutoSize = true, Location = new Point(26, 65), ForeColor = Color.LightGray };
        Label location = new Label { Text = "Installation folder", AutoSize = true, Location = new Point(26, 108) };
        destination.SetBounds(26, 134, 530, 29);
        destination.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HERO_AVR_Studio");
        browse.Text = "Browse..."; browse.SetBounds(565, 132, 90, 31);
        browse.Click += delegate { using (var f = new FolderBrowserDialog()) { f.Description = "Choose the parent folder for HERO AVR Studio"; if (f.ShowDialog() == DialogResult.OK) destination.Text = Path.Combine(f.SelectedPath, "HERO_AVR_Studio"); } };
        shortcut.Text = "Create desktop shortcut"; shortcut.Checked = true; shortcut.AutoSize = true; shortcut.Location = new Point(26, 174);
        state.Text = "Downloads official tools. No administrator account or Arduino IDE required."; state.SetBounds(26, 207, 630, 45);
        progress.SetBounds(26, 253, 629, 12);
        console.SetBounds(26, 278, 629, 145); console.Multiline = true; console.ReadOnly = true; console.ScrollBars = ScrollBars.Vertical;
        console.Font = new Font("Consolas", 9); console.BackColor = Color.FromArgb(14, 18, 24); console.ForeColor = Color.FromArgb(188, 223, 200);
        console.Text = "Internet connection required for first installation.\r\nThe installer never overwrites an existing installation.";
        install.Text = "Install"; install.SetBounds(445, 444, 100, 36);
        open.Text = "Launch"; open.SetBounds(555, 444, 100, 36); open.Enabled = false;
        foreach (Button b in new[] { browse, install, open }) { b.FlatStyle = FlatStyle.Flat; b.ForeColor = Color.White; b.BackColor = Color.FromArgb(42, 52, 65); }
        install.BackColor = Color.FromArgb(24, 113, 88);
        Controls.AddRange(new Control[] { title, subtitle, location, destination, browse, shortcut, state, progress, console, install, open });
        install.Click += async delegate {
            string path;
            try { path = Path.GetFullPath(destination.Text); if (Directory.Exists(path) || File.Exists(path)) throw new IOException("Choose a new folder; existing installations are preserved."); }
            catch (Exception e) { MessageBox.Show(e.Message, "Choose a new folder"); return; }
            busy = true; destination.Enabled = browse.Enabled = shortcut.Enabled = install.Enabled = false;
            progress.Style = ProgressBarStyle.Marquee; console.Clear();
            state.Text = "Installing... official downloads and verification may take a few minutes.";
            logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HERO AVR Studio Setup", "install-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            int code = 1;
            bool noShortcut = !shortcut.Checked;
            try { code = await Task.Run(() => Bootstrap.Install(path, noShortcut, null, logPath, line => BeginInvoke(new Action(() => console.AppendText(line + Environment.NewLine))))); }
            catch (Exception e) { console.AppendText(e.Message + Environment.NewLine); }
            busy = false; progress.Style = ProgressBarStyle.Continuous; progress.Value = code == 0 ? 100 : 0;
            if (code == 0) { state.Text = "Installation verified. HERO AVR Studio is ready."; open.Enabled = true; }
            else { state.Text = "Installation did not complete. See the log: " + logPath; install.Enabled = destination.Enabled = browse.Enabled = shortcut.Enabled = true; }
        };
        open.Click += delegate { Process.Start(new ProcessStartInfo(Path.Combine(destination.Text, "Launch.cmd")) { UseShellExecute = true }); Close(); };
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; MessageBox.Show("Installation is in progress. Please keep setup open."); } };
    }
}
