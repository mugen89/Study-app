using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StudyApp.Updater
{
    /// <summary>
    /// Usage (started by Study App from the extracted update package):
    ///   StudyApp.Updater.exe --pid 1234 --source "C:\...\update\package" --target "C:\...\StudyApp" --exe StudyApp.exe --from 1.0.0 --to 1.1.0 --lang bg
    /// Waits for the app to close, copies the new program files over the old ones (never the "data" folder),
    /// and starts the new version.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var options = Options.Parse(args);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (options == null)
            {
                MessageBox.Show("This is the update helper of Study App. Please start StudyApp.exe instead.\n\nТова е помощната програма за обновяване. Моля, стартирай StudyApp.exe.",
                    "Study App", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
            using (var form = new UpdaterForm(options))
            {
                Application.Run(form);
                return form.ExitCode;
            }
        }
    }

    internal sealed class Options
    {
        public int Pid;
        public string Source;
        public string Target;
        public string Exe = "StudyApp.exe";
        public string From = "";
        public string To = "";
        public bool Bulgarian;

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length - 1; i++)
            {
                var value = args[i + 1];
                switch (args[i].ToLowerInvariant())
                {
                    case "--pid": int.TryParse(value, out o.Pid); i++; break;
                    case "--source": o.Source = value; i++; break;
                    case "--target": o.Target = value; i++; break;
                    case "--exe": o.Exe = value; i++; break;
                    case "--from": o.From = value; i++; break;
                    case "--to": o.To = value; i++; break;
                    case "--lang": o.Bulgarian = value == "bg"; i++; break;
                }
            }
            if (string.IsNullOrEmpty(o.Source) || string.IsNullOrEmpty(o.Target)) return null;
            if (!Directory.Exists(o.Source) || !Directory.Exists(o.Target)) return null;
            return o;
        }

        public string T(string en, string bg) => Bulgarian ? bg : en;
    }

    internal sealed class UpdaterForm : Form
    {
        private readonly Options _o;
        private readonly Label _status;
        private readonly ProgressBar _bar;
        private readonly StringBuilder _log = new StringBuilder();

        public int ExitCode { get; private set; }

        public UpdaterForm(Options o)
        {
            _o = o;
            Text = "Study App";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowIcon = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* default icon */ }
            BackColor = Color.White;
            Font = new Font("Segoe UI", 10f);
            ClientSize = new Size(440, 150);

            var title = new Label
            {
                Text = o.T("Updating Study App", "Обновяване на Study App") + (string.IsNullOrEmpty(o.To) ? "" : " → " + o.To),
                Font = new Font("Segoe UI Semibold", 13f),
                AutoSize = false,
                Location = new Point(24, 20),
                Size = new Size(392, 30),
            };
            _status = new Label
            {
                Text = o.T("Waiting for Study App to close…", "Изчакване Study App да се затвори…"),
                AutoSize = false,
                ForeColor = Color.FromArgb(80, 85, 95),
                Location = new Point(24, 56),
                Size = new Size(392, 24),
            };
            _bar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Location = new Point(24, 96),
                Size = new Size(392, 18),
            };
            Controls.Add(title);
            Controls.Add(_status);
            Controls.Add(_bar);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            var thread = new Thread(Work) { IsBackground = true, Name = "Updater" };
            thread.Start();
        }

        private void SetStatus(string text)
        {
            Log(text);
            if (IsHandleCreated) BeginInvoke(new Action(() => _status.Text = text));
        }

        private void Log(string line) => _log.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line);

        private void Work()
        {
            Log($"Update {_o.From} -> {_o.To}; source={_o.Source}; target={_o.Target}");
            try
            {
                WaitForApp();
                SetStatus(_o.T("Installing the new version…", "Инсталиране на новата версия…"));
                int copied = CopyFiles();
                Log($"Copied {copied} files.");
                SetStatus(_o.T("Starting Study App…", "Стартиране на Study App…"));
                StartApp("--updated " + (string.IsNullOrEmpty(_o.From) ? "unknown" : _o.From));
                ExitCode = 0;
            }
            catch (Exception ex)
            {
                Log("FAILED: " + ex);
                ExitCode = 2;
                WriteLog();
                BeginInvoke(new Action(() =>
                {
                    MessageBox.Show(this,
                        _o.T("The update could not be completed. Your study data is safe.\n\n", "Актуализацията не можа да завърши. Данните ти за учене са в безопасност.\n\n") +
                        ex.Message + "\n\n" +
                        _o.T("You can download the latest version from GitHub and copy it over the old files (keep the \"data\" folder).",
                             "Можеш да изтеглиш последната версия от GitHub и да я копираш върху старите файлове (запази папката \"data\")."),
                        "Study App", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    try { StartApp(""); } catch { /* nothing more we can do */ }
                    Close();
                }));
                return;
            }
            WriteLog();
            BeginInvoke(new Action(Close));
        }

        private void WaitForApp()
        {
            if (_o.Pid <= 0) return;
            Process process;
            try { process = Process.GetProcessById(_o.Pid); }
            catch { return; } // already gone
            using (process)
            {
                if (process.WaitForExit(45000)) return;
                SetStatus(_o.T("Study App is still open — closing it…", "Study App е все още отворено — затваряне…"));
                try { process.CloseMainWindow(); } catch { }
                if (process.WaitForExit(15000)) return;
                throw new InvalidOperationException(_o.T("Study App did not close. Please close it and try the update again.",
                    "Study App не се затвори. Затвори го и опитай отново."));
            }
        }

        private static bool IsProtected(string relative)
        {
            var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            return first.Equals("data", StringComparison.OrdinalIgnoreCase) || first.Equals("update", StringComparison.OrdinalIgnoreCase);
        }

        private int CopyFiles()
        {
            var source = Path.GetFullPath(_o.Source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(_o.Target);
            int count = 0;
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(source.Length);
                if (IsProtected(relative)) continue; // never touch the user's data
                var dest = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                CopyWithRetry(file, dest);
                count++;
            }
            return count;
        }

        private void CopyWithRetry(string from, string to)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.Copy(from, to, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 12)
                {
                    Thread.Sleep(500);
                    if (attempt == 6 && File.Exists(to))
                    {
                        // A file that stays locked (e.g. by antivirus) can usually still be renamed.
                        try
                        {
                            var old = to + ".old";
                            if (File.Exists(old)) File.Delete(old);
                            File.Move(to, old);
                            Log("Renamed locked file " + Path.GetFileName(to));
                        }
                        catch { /* keep retrying */ }
                    }
                }
                catch (UnauthorizedAccessException) when (attempt < 12)
                {
                    Thread.Sleep(500);
                }
            }
        }

        private void StartApp(string arguments)
        {
            var exe = Path.Combine(_o.Target, _o.Exe);
            if (!File.Exists(exe)) throw new FileNotFoundException(exe);
            Process.Start(new ProcessStartInfo(exe, arguments) { WorkingDirectory = _o.Target, UseShellExecute = false });
        }

        private void WriteLog()
        {
            try
            {
                var dir = Path.Combine(_o.Target, "data", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "updater.log"), _log.ToString(), Encoding.UTF8);
            }
            catch
            {
                // logging is best effort
            }
        }
    }
}
