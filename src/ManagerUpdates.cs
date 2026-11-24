using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace ServiceTool
{
    internal static class ManagerUpdates
    {
        private const string Repository = "forslaey/Bars-Automation-Manager";
        private const string ReleaseFilePrefix = "Bars.Automation.Manager.";
        internal const string RepositoryUrl = "https://github.com/" + Repository;
        private const string Certificate = "6953D55E71360D565B095F1BF0B1442B4A211A93";
        private static bool busy;
        internal static bool CheckInProgress { get { return busy; } }
        private static Version notified;
        private static Form toast;
        private static Version pendingVersion;
        private static Asset pendingAsset;
        internal const string ReadyVariable = "BARS_MANAGER_UPDATE_READY";

        internal static bool ShowPending(Form owner)
        {
            if (pendingAsset == null || busy || owner == null || owner.IsDisposed || !owner.Visible ||
                !owner.Enabled || owner.WindowState == FormWindowState.Minimized) return false;
            if (notified != pendingVersion && !ShowToast(owner, pendingVersion, pendingAsset)) return false;
            notified = pendingVersion;
            pendingAsset = null;
            return true;
        }

        internal static void SignalReady()
        {
            string name = Environment.GetEnvironmentVariable(ReadyVariable);
            if (String.IsNullOrEmpty(name) || !name.StartsWith(@"Local\BARS.Manager.Ready.", StringComparison.Ordinal)) return;
            Environment.SetEnvironmentVariable(ReadyVariable, null);
            try { using (var ready = EventWaitHandle.OpenExisting(name)) ready.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
        }
        private static Version Current { get { return typeof(ManagerUpdates).Assembly.GetName().Version; } }

        private sealed class Release
        {
            public string tag_name { get; set; }
            public bool draft { get; set; }
            public bool prerelease { get; set; }
            public Asset[] assets { get; set; }
        }
        private sealed class Asset
        {
            public string name { get; set; }
            public string browser_download_url { get; set; }
            public long size { get; set; }
        }
        private sealed class Client : WebClient
        {
            public Client()
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                Headers[HttpRequestHeader.UserAgent] = "Bars-Automation-Manager";
            }
            protected override WebRequest GetWebRequest(Uri address)
            {
                var request = base.GetWebRequest(address);
                request.Timeout = 10000;
                var http = request as HttpWebRequest;
                if (http != null) http.ReadWriteTimeout = 15000;
                return request;
            }
        }

        internal static async void Check(Form owner, bool manual)
        {
            if (busy)
            {
                if (manual) BrandMessageBox.Show(owner, "Проверка или скачивание уже выполняется.", "Обновление программы");
                return;
            }
            busy = true;
            try
            {
                var release = await Task.Run(() =>
                {
                    using (var client = new Client())
                    {
                        string endpoint = "https://api.github.com/repos/" + Repository + "/releases";
                        var serializer = new JavaScriptSerializer();
                        try { return serializer.Deserialize<Release>(client.DownloadString(endpoint + "/latest")); }
                        catch (WebException error)
                        {
                            using (var response = error.Response as HttpWebResponse)
                                if (response == null || response.StatusCode != HttpStatusCode.NotFound) throw;
                            var releases = serializer.Deserialize<Release[]>(client.DownloadString(endpoint + "?per_page=20"));
                            return releases.FirstOrDefault(item => !item.draft && !item.prerelease);
                        }
                    }
                });
                if (manual && (owner.IsDisposed || !owner.Visible)) return;
                Version version;
                if (release == null || release.draft || release.prerelease ||
                    !TryReleaseVersion(release.tag_name, out version) || version <= Current)
                {
                    if (manual) BrandMessageBox.Show(owner, "Обновления не найдены.", "Обновление программы");
                    return;
                }
                string name = ReleaseFileName(version);
                string url = RepositoryUrl + "/releases/download/" + release.tag_name + "/" + name;
                var asset = (release.assets ?? new Asset[0]).SingleOrDefault(item => item.name == name &&
                    item.browser_download_url == url && item.size > 0 && item.size <= 100 * 1024 * 1024);
                if (manual) await Offer(owner, version, asset);
                else if (asset != null && notified != version)
                {
                    pendingVersion = version;
                    pendingAsset = asset;
                }
            }
            catch (WebException error)
            {
                string message;
                using (var response = error.Response as HttpWebResponse)
                    message = response == null ? "Не удаётся установить соединение с сервером обновлений." :
                        "Сервер обновлений не смог выполнить проверку. Попробуй позже.";
                if (manual && !owner.IsDisposed && owner.Visible)
                    BrandMessageBox.Show(owner, message, "Обновление программы");
            }
            catch (Exception error)
            {
                if (manual && !owner.IsDisposed && owner.Visible)
                    BrandMessageBox.Show(owner, "Не удалось проверить обновления. " + error.Message, "Обновление программы");
            }
            finally { busy = false; }
        }

        internal static bool TryReleaseVersion(string tag, out Version version)
        {
            version = null;
            Version parsed;
            if (String.IsNullOrEmpty(tag) || !Version.TryParse(tag.Substring(1), out parsed) ||
                parsed.Build < 0 || parsed.Revision >= 0 || tag != "v" + parsed.ToString(3)) return false;
            version = new Version(parsed.Major, parsed.Minor, parsed.Build, 0);
            return true;
        }

        private static bool ShowToast(Form owner, Version version, Asset asset)
        {
            if (VisibleOwnerArea(owner).Width < 180 || VisibleOwnerArea(owner).Height < 100) return false;
            if (toast != null) toast.Close();
            var colors = ThemePalette.Create(owner.BackColor.GetBrightness() < .5F);
            var window = new UpdateToast(colors.Border) { BackColor = colors.Canvas, ForeColor = colors.Text,
                FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi,
                ClientSize = new Size(320, 96), Font = owner.Font };
            toast = window;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 2, RowCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            var label = new Label { Text = "Доступна версия " + version.ToString(3), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            layout.Controls.Add(label, 0, 0);
            layout.SetColumnSpan(label, 2);
            var download = new BrandButton { Text = "Скачать…", Palette = colors, Primary = true, Dock = DockStyle.Fill };
            var later = new BrandButton { Text = "Позже", Palette = colors, Dock = DockStyle.Fill };
            layout.Controls.Add(download, 0, 1);
            layout.Controls.Add(later, 1, 1);
            window.Controls.Add(layout);
            Size preferred = window.ClientSize;
            Action position = () =>
            {
                if (owner.IsDisposed || owner.WindowState == FormWindowState.Minimized) return;
                var area = VisibleOwnerArea(owner);
                if (area.Width < 180 || area.Height < 100) { window.Close(); return; }
                window.Size = new Size(Math.Min(preferred.Width, area.Width - 24), Math.Min(preferred.Height, area.Height - 24));
                window.Location = new Point(area.Right - window.Width - 12, area.Bottom - window.Height - 12);
            };
            position();
            window.Opacity = SystemInformation.IsMenuAnimationEnabled ? 0.08 : 1;
            var timer = new Timer { Interval = 30 };
            var elapsed = Stopwatch.StartNew();
            timer.Tick += delegate
            {
                if (window.Opacity < 1) window.Opacity = Math.Min(1, elapsed.Elapsed.TotalMilliseconds / 240);
                else timer.Stop();
            };
            EventHandler closeWithOwner = (sender, args) => window.Close();
            EventHandler moveWithOwner = (sender, args) => position();
            owner.Disposed += closeWithOwner;
            owner.LocationChanged += moveWithOwner;
            owner.SizeChanged += moveWithOwner;
            window.FormClosed += delegate
            {
                timer.Dispose();
                owner.Disposed -= closeWithOwner;
                owner.LocationChanged -= moveWithOwner;
                owner.SizeChanged -= moveWithOwner;
                toast = null;
            };
            later.Click += delegate { window.Close(); };
            download.Click += async delegate
            {
                window.Close();
                if (busy || owner.IsDisposed) return;
                busy = true;
                try { await Offer(owner, version, asset); }
                finally { busy = false; }
            };
            window.Show(owner);
            preferred = window.ClientSize;
            position();
            if (!window.IsDisposed) timer.Start();
            return !window.IsDisposed;
        }

        private static Rectangle VisibleOwnerArea(Form owner)
        {
            return Rectangle.Intersect(owner.RectangleToScreen(owner.ClientRectangle), Screen.FromControl(owner).WorkingArea);
        }

        private sealed class UpdateToast : Form
        {
            private readonly Color border;
            internal UpdateToast(Color borderColor) { border = borderColor; }
            protected override bool ShowWithoutActivation { get { return true; } }

            private static GraphicsPath Outline(Rectangle rectangle)
            {
                int diameter = Math.Min(24, Math.Min(rectangle.Width, rectangle.Height));
                var path = new GraphicsPath();
                path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
                path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
                path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                return path;
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                if (Width < 2 || Height < 2) return;
                var previous = Region;
                using (var path = Outline(ClientRectangle)) Region = new Region(path);
                if (previous != null) previous.Dispose();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Width < 4 || Height < 4) return;
                var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Outline(bounds))
                using (var pen = new Pen(border)) e.Graphics.DrawPath(pen, path);
            }
        }

        private sealed class UpdateProgressWindow : BrandWindow { }

        private static async Task Offer(Form owner, Version version, Asset asset)
        {
            const string title = "Обновление программы";
            string directory = null;
            bool handedOff = false;
            try
            {
                if (asset == null) throw new InvalidDataException("В релизе нет подходящего EXE.");
                if (BrandMessageBox.Show(owner, "Скачать версию " + version.ToString(3) + "?", title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                var parent = ParentId();
                string target;
                using (var launcher = Process.GetProcessById(parent)) target = launcher.MainModule.FileName;
                await Task.Run(() => Verify(target, Current));
                EnsureDestinationAvailable(target, UpdatedPath(target, version));
                if (owner.IsDisposed || !owner.Visible) return;
                directory = Path.Combine(Path.GetTempPath(), "bars-update-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                string payload = Path.Combine(directory, "update.exe");
                using (var client = new Client())
                using (var timer = new Timer { Interval = 120000 })
                using (var progress = new UpdateProgressWindow { Text = title, ClientSize = new Size(510, 182),
                    StartPosition = FormStartPosition.Manual, FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
                    AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi })
                {
                    var colors = ThemePalette.Create(owner.BackColor.GetBrightness() < .5F);
                    progress.Icon = owner.Icon;
                    progress.BackColor = colors.Canvas;
                    progress.ForeColor = colors.Text;
                    NativeWindowTheme.Bind(progress, () => colors.Dark);
                    var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                        Padding = new Padding(24, 16, 24, 16) };
                    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
                    var bar = new DownloadProgressView { Dock = DockStyle.Fill, Palette = colors };
                    Action<long> report = bytes => bar.Report(new TransferProgress { Stage = "Скачиваем обновление",
                        File = asset.name, Key = asset.name, Bytes = bytes, NetworkBytes = bytes, Total = asset.size,
                        Overall = Math.Min(.999, (double)bytes / asset.size) });
                    report(0);
                    client.DownloadProgressChanged += (sender, args) =>
                    {
                        if (args.BytesReceived > asset.size) client.CancelAsync();
                        report(args.BytesReceived);
                    };
                    var stop = new BrandButton { Text = "Отменить", Palette = colors };
                    stop.Click += delegate { client.CancelAsync(); };
                    var footer = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
                    footer.Controls.Add(stop);
                    ModuleNavigation.RightAlignedFooter(footer, stop);
                    layout.Controls.Add(bar, 0, 0);
                    layout.Controls.Add(footer, 0, 1);
                    progress.Controls.Add(layout);
                    timer.Tick += delegate { timer.Stop(); client.CancelAsync(); };
                    FormClosedEventHandler cancel = (sender, args) => client.CancelAsync();
                    owner.FormClosed += cancel;
                    progress.FormClosed += cancel;
                    Action center = () =>
                    {
                        var area = Screen.FromControl(owner).WorkingArea;
                        progress.Location = new Point(area.Left + (area.Width - progress.Width) / 2,
                            area.Top + (area.Height - progress.Height) / 2);
                    };
                    progress.Shown += delegate { center(); };
                    center();
                    try { progress.Show(owner); timer.Start(); await client.DownloadFileTaskAsync(new Uri(asset.browser_download_url), payload); }
                    finally { owner.FormClosed -= cancel; }
                }
                await Task.Run(() =>
                {
                    if (new FileInfo(payload).Length != asset.size) throw new InvalidDataException("Файл скачан не полностью.");
                    Verify(payload, version);
                });
                if (owner.IsDisposed || !owner.Visible) return;
                if (BrandMessageBox.Show(owner, "Версия скачана и проверена. Закрыть менеджер и установить обновление?", title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                if (Application.OpenForms.Cast<Form>().OfType<IModuleOperation>().Any(module => module.ActiveOperation != null))
                    throw new InvalidOperationException("Дождись завершения текущих операций и повтори обновление.");
                string helper = Path.Combine(directory, "helper.exe");
                File.Copy(Application.ExecutablePath, helper);
                string handshake = @"Local\BARS.Update." + Guid.NewGuid().ToString("N");
                using (var ready = new EventWaitHandle(false, EventResetMode.AutoReset, handshake))
                using (var proceed = new EventWaitHandle(false, EventResetMode.AutoReset, handshake + ".Proceed"))
                using (var current = Process.GetCurrentProcess())
                using (var process = Process.Start(new ProcessStartInfo(helper,
                    "--apply-manager-update " + current.Id + " " + parent + " " + version + " " +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(target)) + " " + handshake) { UseShellExecute = false }))
                {
                    if (process == null) throw new IOException("Не удалось запустить помощник обновления.");
                    if (!await Task.Run(() => ready.WaitOne(5000)))
                        throw new IOException("Помощник обновления не подтвердил готовность.");
                    handedOff = true;
                    if (Application.OpenForms.Cast<Form>().OfType<IModuleOperation>().Any(module => module.ActiveOperation != null))
                        throw new InvalidOperationException("Началась другая операция. Повтори обновление после её завершения.");
                    Application.Exit();
                    if (Application.OpenForms.Count == 0) proceed.Set();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!owner.IsDisposed)
                    BrandMessageBox.Show(owner, error.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (!handedOff && directory != null) Cleanup(directory);
            }
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr handle, int kind, IntPtr[] info, int length, out int written);

        private static int ParentId()
        {
            using (var process = Process.GetCurrentProcess()) return ParentId(process);
        }

        private static int ParentId(Process process)
        {
            var info = new IntPtr[6];
            int written;
            if (NtQueryInformationProcess(process.Handle, 0, info, info.Length * IntPtr.Size, out written) != 0)
                throw new IOException("Не удалось определить путь запускателя.");
            return info[5].ToInt32();
        }

        private static void Verify(string path, Version expected)
        {
            using (var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
            {
                if (!String.Equals(cert.Thumbprint, Certificate, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Файл подписан другим сертификатом.");
                SoftwareTrust.Signature(path, cert.GetNameInfo(X509NameType.SimpleName, false));
            }
            var metadata = FileVersionInfo.GetVersionInfo(path);
            if (metadata.ProductName != "Менеджер автоматизации Барс" || new Version(metadata.FileMajorPart,
                metadata.FileMinorPart, metadata.FileBuildPart, metadata.FilePrivatePart) != expected)
                throw new InvalidDataException("Версия или название программы в файле не совпадает.");
        }

        internal static string ReleaseFileName(Version version)
        {
            return ReleaseFilePrefix + version.ToString(3) + ".exe";
        }

        internal static string UpdatedPath(string target, Version version)
        {
            string name = Path.GetFileNameWithoutExtension(target);
            const string previousPrefix = "Менеджер автоматизации Барс ";
            string prefix = name.StartsWith(ReleaseFilePrefix, StringComparison.OrdinalIgnoreCase)
                ? ReleaseFilePrefix : previousPrefix;
            Version old;
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !TryReleaseVersion("v" + name.Substring(prefix.Length), out old)) return target;
            return Path.Combine(Path.GetDirectoryName(target), ReleaseFileName(version));
        }

        private static void EnsureDestinationAvailable(string target, string destination)
        {
            if (!String.Equals(target, destination, StringComparison.OrdinalIgnoreCase) &&
                (File.Exists(destination) || Directory.Exists(destination)))
                throw new IOException("Рядом уже есть файл или папка «" + Path.GetFileName(destination) +
                    "». Освободи это имя и повтори обновление.");
        }

        internal static int Apply(string[] args)
        {
            string directory = Path.GetDirectoryName(Application.ExecutablePath);
            string candidate = null;
            string backup = null;
            string target = null;
            string destination = null;
            bool replaced = false;
            bool oldMoved = false;
            try
            {
                if (args.Length != 6 || !Path.GetFileName(directory).StartsWith("bars-update-", StringComparison.Ordinal) ||
                    Int32.Parse(args[1]) != ParentId()) return 87;
                var version = Version.Parse(args[3]);
                if (version <= Current) return 87;
                target = Path.GetFullPath(Encoding.UTF8.GetString(Convert.FromBase64String(args[4])));
                using (var launcher = Process.GetProcessById(Int32.Parse(args[2])))
                using (var manager = Process.GetProcessById(Int32.Parse(args[1])))
                using (var ready = EventWaitHandle.OpenExisting(args[5]))
                using (var proceed = EventWaitHandle.OpenExisting(args[5] + ".Proceed"))
                {
                    if (!String.Equals(launcher.MainModule.FileName, target, StringComparison.OrdinalIgnoreCase) ||
                        ParentId(manager) != launcher.Id) return 87;
                    ready.Set();
                    if (!proceed.WaitOne(15000)) return 1223;
                    if (!manager.WaitForExit(30000)) throw new IOException("Менеджер ещё работает.");
                    if (!launcher.WaitForExit(30000)) throw new IOException("Запускатель ещё работает.");
                }
                Verify(target, Current);
                destination = UpdatedPath(target, version);
                EnsureDestinationAvailable(target, destination);
                string nonce = Guid.NewGuid().ToString("N");
                candidate = target + "." + nonce + ".new";
                backup = target + "." + nonce + ".bak";
                File.Copy(Path.Combine(directory, "update.exe"), candidate);
                Verify(candidate, version);
                if (String.Equals(target, destination, StringComparison.OrdinalIgnoreCase))
                    File.Replace(candidate, target, backup);
                else
                {
                    File.Move(target, backup);
                    oldMoved = true;
                    File.Move(candidate, destination);
                }
                replaced = true;
                string readyName = @"Local\BARS.Manager.Ready." + nonce;
                using (var ready = new EventWaitHandle(false, EventResetMode.AutoReset, readyName))
                {
                    var start = new ProcessStartInfo(destination)
                    {
                        WorkingDirectory = Path.GetDirectoryName(destination), UseShellExecute = false
                    };
                    start.EnvironmentVariables[ReadyVariable] = readyName;
                    using (var started = Process.Start(start))
                    {
                        if (started == null) throw new IOException("Новая версия не запустилась.");
                        var wait = Stopwatch.StartNew();
                        while (!ready.WaitOne(200))
                        {
                            if (started.HasExited) throw new IOException("Новая версия закрылась до открытия главного окна.");
                            if (wait.Elapsed < TimeSpan.FromSeconds(45)) continue;
                            // A live process may have the replacement open. Keep the recovery copy.
                            replaced = oldMoved = false;
                            throw new IOException("Новая версия не подтвердила запуск. Резервная копия: " + backup);
                        }
                    }
                }
                replaced = oldMoved = false;
                TryDelete(backup);
                if (!String.Equals(target, destination, StringComparison.OrdinalIgnoreCase))
                {
                    string warning = RetargetShortcuts(target, destination, new[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
                    });
                    if (warning != null)
                    {
                        Application.EnableVisualStyles();
                        BrandMessageBox.Show("Обновление установлено. Некоторые ярлыки не удалось проверить или изменить. " + warning,
                            "Обновление программы");
                    }
                }
                return 0;
            }
            catch (Exception error)
            {
                string recovery = "";
                if (replaced || oldMoved)
                    try
                    {
                        if (oldMoved)
                        {
                            if (replaced) File.Delete(destination);
                            File.Move(backup, target);
                        }
                        else File.Replace(backup, target, null);
                    }
                    catch (IOException) { recovery = "\r\nРезервная копия: " + backup; }
                    catch (UnauthorizedAccessException) { recovery = "\r\nРезервная копия: " + backup; }
                Application.EnableVisualStyles();
                BrandMessageBox.Show("Обновление не завершено. " + error.Message + recovery, "Обновление программы");
                return 1;
            }
            finally { TryDelete(candidate); Cleanup(directory); }
        }

        internal static string RetargetShortcuts(string oldTarget, string destination, string[] roots)
        {
            string warning = null;
            object shell = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                var folders = new System.Collections.Generic.Queue<string>(roots.Where(path => !String.IsNullOrWhiteSpace(path)));
                var visited = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (folders.Count > 0)
                {
                    string folder = folders.Dequeue();
                    if (!visited.Add(folder) || !Directory.Exists(folder)) continue;
                    try
                    {
                        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                        foreach (string child in Directory.GetDirectories(folder)) folders.Enqueue(child);
                        foreach (string path in Directory.GetFiles(folder, "*.lnk"))
                        {
                            object link = null;
                            try
                            {
                                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                                link = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
                                var type = link.GetType();
                                const System.Reflection.BindingFlags read = System.Reflection.BindingFlags.GetProperty;
                                const System.Reflection.BindingFlags write = System.Reflection.BindingFlags.SetProperty;
                                string target = Convert.ToString(type.InvokeMember("TargetPath", read, null, link, null));
                                if (!String.Equals(oldTarget, target, StringComparison.OrdinalIgnoreCase)) continue;
                                string icon = Convert.ToString(type.InvokeMember("IconLocation", read, null, link, null));
                                type.InvokeMember("TargetPath", write, null, link, new object[] { destination });
                                if (icon.StartsWith(oldTarget + ",", StringComparison.OrdinalIgnoreCase))
                                    type.InvokeMember("IconLocation", write, null, link, new object[] { destination + icon.Substring(oldTarget.Length) });
                                type.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
                            }
                            catch (Exception error) { if (warning == null) warning = Path.GetFileName(path) + ": " + error.Message; }
                            finally { if (link != null) Marshal.FinalReleaseComObject(link); }
                        }
                    }
                    catch (IOException error) { if (warning == null) warning = error.Message; }
                    catch (UnauthorizedAccessException error) { if (warning == null) warning = error.Message; }
                }
            }
            catch (Exception error) { if (warning == null) warning = error.Message; }
            finally { if (shell != null) Marshal.FinalReleaseComObject(shell); }
            return warning;
        }

        private static void TryDelete(string path)
        {
            if (path == null) return;
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void Cleanup(string directory)
        {
            if (String.IsNullOrEmpty(directory) || !Path.GetFileName(directory).StartsWith("bars-update-", StringComparison.Ordinal)) return;
            TryDelete(Path.Combine(directory, "update.exe"));
            TryDelete(Path.Combine(directory, "helper.exe"));
            try { Directory.Delete(directory); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
