using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class SqlModuleForm : ManagerModuleForm, IModuleOperation
    {
        private readonly SqlManagerPage page;
        internal SqlModuleForm() : base("SQL Server и SSMS", new Size(1100, 790))
        {
            page = new SqlManagerPage(this, SoftwareCatalog.FindFolder,
                () => { if (page.CanClose(false) && !ModuleNavigation.ReturnToHub()) Close(); }, Colors);
            Root.Controls.Add(page, 0, 1);
            Shown += async delegate { await page.RefreshLocal(SoftwareCatalog.FindFolder()); };
            FormClosing += (sender, args) => { if (!page.CanClose(ModuleNavigation.ExitConfirmed)) args.Cancel = true; };
            SetTheme(Colors.Dark, false);
        }
        string IModuleOperation.ActiveOperation { get { return page.Busy ? "SQL Server и SSMS" : null; } }
        protected override void OnThemeChanged() { if (page != null) page.ApplyTheme(Colors); }
    }

    internal sealed class BarsProgramsForm : BarsPackagesForm
    {
        internal BarsProgramsForm() : base(false) { }
    }

    internal sealed class BarsServicesForm : BarsPackagesForm
    {
        internal BarsServicesForm() : base(true) { }
    }

    internal abstract class BarsPackagesForm : ManagerModuleForm, IModuleOperation
    {
        private sealed class Package
        {
            internal string Name, Path;
            internal BarsReleaseCatalog.Artifact Remote;
            internal bool Shortcut;
            internal bool Local, Exists;
            internal string LocalError;
        }

        private readonly bool services;
        private string folder;
        private readonly Label localSource;
        private readonly ServiceGrid grid = new ServiceGrid();
        private readonly LibraryGridScrollBar scroll = new LibraryGridScrollBar { Dock = DockStyle.Right, Width = 14, AlignToRows = true, AutoHideWhenUnused = true, UseCardBackground = true };
        private readonly DownloadProgressView progress = new DownloadProgressView { Dock = DockStyle.Fill };
        private readonly JournalTextBox journal = new JournalTextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true };
        private readonly BrandButton scan, download, apply, cancel, menu;
        private List<BarsReleaseCatalog.Artifact> remote = new List<BarsReleaseCatalog.Artifact>();
        private BarsReleaseCatalog catalog;
        private readonly Dictionary<string, string> downloadErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource operation;
        private readonly HashSet<Package> markedPackages = new HashSet<Package>();
        private sealed class InstallerStillRunningException : IOException
        {
            internal readonly Task Completion;
            internal readonly string PackageName;
            internal InstallerStillRunningException(Process process, string package) :
                base("Ожидание прекращено. Установщик ещё работает (PID " + process.Id + ").")
            {
                PackageName = package;
                Completion = Task.Run(() => { using (process) process.WaitForExit(); });
            }
        }
        private bool installing;
        private bool closeAfterOperation;

        protected BarsPackagesForm(bool services, string localFolder = null) : base(services ? "Службы и агенты Барс" : "Программы Барс", new Size(1100, 780))
        {
            this.services = services;
            folder = localFolder ?? PackageFolder(BundlePreferences.Load(), services);
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 40, 34, 0, 56, 100, 106, 54 })
                body.RowStyles.Add(new RowStyle(height == 0 ? SizeType.Percent : SizeType.Absolute, height == 0 ? 100 : height));
            body.Controls.Add(ModuleHeading(Text), 0, 0);
            localSource = Label("Локальный комплект: " + folder, true);
            body.Controls.Add(localSource, 0, 1);
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(10), Margin = Padding.Empty };
            grid.Dock = DockStyle.Fill;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersHeight = 38;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.Padding = new Padding(6, 3, 6, 3);
            grid.RowTemplate.Height = 32;
            grid.ReadOnly = true;
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = grid.AllowUserToResizeRows = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.ScrollBars = ScrollBars.None;
            grid.TouchScrollingEnabled = true;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 36 });
            foreach (string label in new[] { "Файл", "Версия", "Источник", "Состояние" })
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = label, FillWeight = label == "Файл" ? 210 : 80,
                    SortMode = DataGridViewColumnSortMode.NotSortable });
            if (!services)
                grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Ярлык", FillWeight = 40, MinimumWidth = 68 });
            grid.CellMouseDown += (sender, args) => { if (args.Button == MouseButtons.Left && args.RowIndex >= 0) Toggle(args.RowIndex, args.ColumnIndex); };
            grid.CellPainting += PaintCheck;
            grid.KeyDown += (sender, args) =>
            {
                if (args.KeyCode == Keys.Space && grid.CurrentCell != null) { Toggle(grid.CurrentCell.RowIndex, grid.CurrentCell.ColumnIndex); args.Handled = true; }
            };
            scroll.Bind(grid);
            card.Controls.Add(grid);
            card.Controls.Add(scroll);
            body.Controls.Add(card, 0, 2);
            scan = Button("Проверить в сети", false);
            download = Button("Скачать выбранные", false);
            apply = Button(services ? "Распаковать" : "Установить", true);
            cancel = Button("Отменить", false);
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1, Padding = new Padding(0, 8, 0, 6), Margin = Padding.Empty };
            var buttons = new[] { scan, download, apply, cancel };
            for (int i = 0; i < buttons.Length; i++)
            {
                if (i > 0) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 10));
                actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                buttons[i].Margin = Padding.Empty;
                actions.Controls.Add(buttons[i], 2 * i, 0);
            }
            body.Controls.Add(actions, 0, 3);
            body.Controls.Add(progress, 0, 4);
            var journalCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(12), Margin = Padding.Empty };
            journal.BorderStyle = BorderStyle.None;
            journalCard.Controls.Add(journal);
            body.Controls.Add(journalCard, 0, 5);
            menu = Button("В меню", false);
            var close = Button("Закрыть", false);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(0, 10, 0, 0) };
            footer.Controls.Add(menu);
            footer.Controls.Add(close);
            ModuleNavigation.RightAlignedFooter(footer, menu, close);
            body.Controls.Add(footer, 0, 6);
            Root.Controls.Add(body, 0, 1);
            scan.Click += Scan;
            download.Click += Download;
            apply.Click += Apply;
            cancel.Click += delegate { if (operation != null) operation.Cancel(); };
            menu.Click += delegate { if (!ModuleNavigation.ReturnToHub()) Close(); };
            close.Click += delegate { Close(); };
            Shown += async delegate { await Run("Проверяем локальный комплект", false, _ => { }); };
            if (localFolder == null)
                Activated += async delegate
                {
                    if (operation != null) return;
                    string selected = PackageFolder(BundlePreferences.Load(), services);
                    if (String.Equals(folder, selected, StringComparison.OrdinalIgnoreCase)) return;
                    folder = selected;
                    localSource.Text = "Локальный комплект: " + folder;
                    markedPackages.Clear();
                    downloadErrors.Clear();
                    grid.Rows.Clear();
                    await Run("Проверяем локальный комплект", false, _ => { });
                };
            FormClosing += (sender, args) =>
            {
                if (operation == null) return;
                args.Cancel = true;
                closeAfterOperation = true;
                operation.Cancel();
            };
            SetTheme(Colors.Dark, false);
            UpdateActions();
        }

        internal static string PackageFolder(string root, bool services)
        {
            return Path.Combine(root, "_soft", "_Bars", services ? "_Services" : "_Soft");
        }

        string IModuleOperation.ActiveOperation { get { return operation == null ? null : Text; } }

        protected override void OnThemeChanged()
        {
            if (grid == null) return;
            grid.EnableHeadersVisualStyles = false;
            grid.BackgroundColor = Colors.Surface;
            grid.GridColor = Colors.Border;
            grid.DefaultCellStyle.BackColor = Colors.Surface;
            grid.DefaultCellStyle.ForeColor = Colors.Text;
            grid.DefaultCellStyle.SelectionBackColor = Colors.Selection;
            grid.DefaultCellStyle.SelectionForeColor = Colors.Text;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Colors.SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Colors.Text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Colors.SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Colors.Text;
            scroll.Palette = Colors;
            progress.Palette = Colors;
        }

        private void Toggle(int index, int column)
        {
            if (operation != null || column < 0) return;
            var item = (Package)grid.Rows[index].Tag;
            if (!services && column == 5)
            {
                if (!BarsReleaseCatalog.HasSilentProfile(item.Name)) return;
                item.Shortcut = !item.Shortcut;
                grid.Rows[index].Cells[5].Value = item.Shortcut;
                return;
            }
            var cell = grid.Rows[index].Cells[0];
            cell.Value = !Equals(cell.Value, true);
            if (Equals(cell.Value, true)) markedPackages.Add(item);
            else markedPackages.Remove(item);
            UpdateActions();
        }

        private void PaintCheck(object sender, DataGridViewCellPaintingEventArgs args)
        {
            if (args.RowIndex < 0 || (args.ColumnIndex != 0 && args.ColumnIndex != 5)) return;
            args.Paint(args.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.SelectionBackground);
            var item = grid.Rows[args.RowIndex].Tag as Package;
            if (args.ColumnIndex == 5 && (item == null || !BarsReleaseCatalog.HasSilentProfile(item.Name)))
            {
                args.Paint(args.CellBounds, DataGridViewPaintParts.Border);
                args.Handled = true;
                return;
            }
            int size = Math.Max(9, (int)Math.Round(18 * args.Graphics.DpiX / 96));
            var circle = new Rectangle(args.CellBounds.Left + (args.CellBounds.Width - size) / 2,
                args.CellBounds.Top + (args.CellBounds.Height - size) / 2, size, size);
            bool selected = Equals(args.Value, true);
            var state = args.Graphics.Save();
            try
            {
                args.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(selected ? Colors.Accent : Colors.Surface)) args.Graphics.FillEllipse(brush, circle);
                using (var pen = new Pen(selected ? Colors.Accent : Colors.Border)) args.Graphics.DrawEllipse(pen, circle);
                if (selected)
                    using (var dot = new SolidBrush(Colors.AccentText))
                        args.Graphics.FillEllipse(dot, Rectangle.Inflate(circle, -size / 3, -size / 3));
            }
            finally { args.Graphics.Restore(state); }
            args.Paint(args.CellBounds, DataGridViewPaintParts.Border);
            args.Handled = true;
        }

        private Package[] Selected()
        {
            return markedPackages.ToArray();
        }

        private void UpdateActions()
        {
            bool idle = operation == null;
            var selected = Selected();
            scan.Enabled = menu.Enabled = idle;
            download.Enabled = idle && selected.Any(item => item.Remote != null);
            apply.Enabled = idle && selected.Length > 0 && selected.All(item => item.Local &&
                (services || BarsReleaseCatalog.HasSilentProfile(item.Name)));
            cancel.Text = installing ? "Прекратить ожидание" : "Отменить";
            cancel.Enabled = !idle;
        }

        private async Task RefreshRows(CancellationToken token)
        {
            var files = new Dictionary<string, Package>(StringComparer.OrdinalIgnoreCase);
            var marked = new HashSet<string>(Selected().Select(item => item.Name), StringComparer.OrdinalIgnoreCase);
            var shortcuts = new HashSet<string>(grid.Rows.Cast<DataGridViewRow>().Select(row => row.Tag as Package)
                .Where(item => item != null && item.Shortcut).Select(item => item.Name), StringComparer.OrdinalIgnoreCase);
            await Task.Run(() =>
            {
                if (Directory.Exists(folder))
                {
                    ToolsDeployment.SafePath(folder);
                    foreach (string path in Directory.EnumerateFiles(folder))
                    {
                        string name = Path.GetFileName(path);
                        token.ThrowIfCancellationRequested();
                        if (BarsReleaseCatalog.ValidName(name, services))
                            files[name] = new Package { Name = name, Path = path };
                    }
                }
                foreach (var file in remote.OrderBy(item => item.Channel != "stable"))
                {
                    Package package;
                    if (!files.TryGetValue(file.fileName, out package))
                        files.Add(file.fileName, package = new Package { Name = file.fileName, Path = Path.Combine(folder, file.fileName) });
                    if (package.Remote == null) package.Remote = file;
                }
                foreach (var item in files.Values.ToArray())
                {
                    token.ThrowIfCancellationRequested();
                    item.Exists = File.Exists(item.Path);
                    if (!item.Exists) continue;
                    try
                    {
                        ToolsDeployment.SafePath(item.Path);
                        if (services)
                        {
                            string[] entries;
                            item.LocalError = ToolArchives.ReadStatus(ToolArchives.Inspect(item.Path, token, out entries));
                        }
                        else
                        {
                            if (new FileInfo(item.Path).Length < BarsReleaseCatalog.MinimumProgramBytes)
                            {
                                if (item.Remote == null) files.Remove(item.Name);
                                else item.LocalError = "Локальный установщик меньше 2 МБ. Скачай пакет заново";
                                continue;
                            }
                            BarsReleaseCatalog.ValidateExecutable(item.Path);
                        }
                        item.Local = String.IsNullOrEmpty(item.LocalError);
                    }
                    catch (InvalidDataException error) { item.LocalError = error.Message; }
                    catch (IOException) { item.LocalError = "Локальный файл недоступен"; }
                    catch (UnauthorizedAccessException) { item.LocalError = "Локальный файл недоступен"; }
                }
            }, token);
            token.ThrowIfCancellationRequested();
            if (IsDisposed) return;
            markedPackages.Clear();
            grid.Rows.Clear();
            foreach (var item in files.Values.OrderBy(item => !item.Local).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                string source = item.Exists ? "Локальный комплект" : "Bars.Release";
                string state = item.Local ? (services || BarsReleaseCatalog.HasSilentProfile(item.Name) ? "Готов к работе" : "Нет профиля тихой установки") : "Можно скачать";
                string failure;
                if (downloadErrors.TryGetValue(item.Name, out failure)) state = failure;
                if (!String.IsNullOrEmpty(item.LocalError)) state = item.LocalError;
                if (item.Exists && item.Remote != null) source += " / Bars.Release";
                item.Shortcut = shortcuts.Contains(item.Name);
                int index = grid.Rows.Add(marked.Contains(item.Name), item.Name, item.Remote == null ? LocalVersion(item.Name) :
                    item.Remote.Version + " / " + item.Remote.Channel, source, state);
                grid.Rows[index].Tag = item;
                if (marked.Contains(item.Name)) markedPackages.Add(item);
                grid.Rows[index].Cells[4].ToolTipText = state;
                if (!services) grid.Rows[index].Cells[5].Value = item.Shortcut;
            }
            if (files.Count == 0) Log("Локальный комплект пуст. Проверь доступные пакеты в сети.");
            UpdateActions();
        }

        private static string LocalVersion(string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(name),
                @"(?<!\d)\d+(?:\.\d+){1,3}(?!\d)");
            return match.Success ? match.Value : "—";
        }

        private async void Scan(object sender, EventArgs args)
        {
            await Run("Проверяем каталог Bars.Release", false, token =>
            {
                var next = new BarsReleaseCatalog();
                next.Login(token);
                var found = next.Scan(services, token);
                catalog = next;
                remote = found;
            });
        }

        private async void Download(object sender, EventArgs args)
        {
            var items = Selected().Where(item => item.Remote != null).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            if (catalog == null || items.Length == 0) return;
            if (items.Any(item => item.Exists) && BrandMessageBox.Show(this,
                "Скачать выбранные пакеты и заменить совпадающие локальные файлы после проверки?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            await Run("Скачиваем выбранные пакеты", false, token =>
            {
                var failed = new List<string>();
                foreach (var item in items)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        catalog.Download(item.Remote, folder, services, token, progress.Report);
                        downloadErrors.Remove(item.Name);
                    }
                    catch (Exception error)
                    {
                        if (!(error is IOException) && !(error is InvalidDataException) && !(error is UnauthorizedAccessException)) throw;
                        token.ThrowIfCancellationRequested();
                        downloadErrors[item.Name] = error.Message;
                        string detail = item.Name + ": " + error.Message;
                        failed.Add(detail);
                    }
                }
                if (failed.Count == 1) throw new IOException(failed[0]);
                if (failed.Count > 1)
                {
                    Invoke((MethodInvoker)delegate { foreach (string detail in failed) Log(detail); });
                    throw new IOException("Не скачано файлов: " + failed.Count + ". Подробности в журнале.");
                }
            });
        }

        private async void Apply(object sender, EventArgs args)
        {
            var items = Selected().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            if (items.Length == 0) return;
            string destination;
            using (var picker = new FolderBrowserDialog { Description = services ? "Выбери папку для распаковки" : "Выбери папку для установки",
                ShowNewFolderButton = true })
            {
                if (ScopedDialogs.Show(this, picker) != DialogResult.OK) return;
                destination = picker.SelectedPath;
            }
            string confirmation = "Тихо установить выбранные программы в " + destination + "?";
            if (!services)
            {
                var unverified = items.Where(item => !BarsReleaseCatalog.HasReferenceDigest(item.Name,
                    item.Remote == null ? null : item.Remote.sha256)).Select(item => item.Name).ToArray();
                if (unverified.Length > 0)
                    confirmation += "\r\n\r\nДля следующих установщиков нет контрольной суммы. Продолжай только для файлов из доверенного источника:\r\n" +
                        String.Join("\r\n", unverified.Take(5)) + (unverified.Length > 5 ? "\r\nИ ещё " + (unverified.Length - 5) : "");
            }
            if (!services && BrandMessageBox.Show(this, confirmation, Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            await Run(services ? "Распаковываем архивы" : "Выполняем тихую установку", !services, token =>
            {
                foreach (var item in items)
                {
                    token.ThrowIfCancellationRequested();
                    ToolsDeployment.SafePath(item.Path);
                    ToolsDeployment.SafePath(destination);
                    string target = YandexBundleSource.LocalPath(destination, "/" + (services ?
                        Path.GetFileNameWithoutExtension(item.Name) : BarsReleaseCatalog.InstallFolderName(item.Name)));
                    ToolsDeployment.SafePath(target);
                    if (services) Extract(item.Path, target, token);
                    else Install(item, target, token);
                }
            });
        }

        private void Extract(string path, string target, CancellationToken token)
        {
            if (Directory.Exists(target) || File.Exists(target))
                throw new IOException("Папка или файл уже существует: " + target + ".");
            string staging = Path.Combine(Path.GetDirectoryName(target), ".bars-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                string copy = Path.Combine(staging, Path.GetFileName(path));
                using (var input = File.OpenRead(path))
                using (var output = File.Create(copy))
                {
                    var buffer = new byte[65536];
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        token.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, count);
                    }
                }
                var prepared = ToolArchives.Extract(copy, token, progress.Report, false);
                token.ThrowIfCancellationRequested();
                Directory.Move(prepared, target);
            }
            finally { PackageZipExtraction.CleanupFolder(staging, "Не удалось удалить временную папку:", progress.Report); }
        }

        private void Install(Package item, string destination, CancellationToken token)
        {
            string path = item.Path;
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                BarsReleaseCatalog.VerifySilentProfile(path, item.Remote == null ? null : item.Remote.sha256);
                token.ThrowIfCancellationRequested();
                string installPath = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
                if (installPath.Length == 2 && installPath[1] == ':') installPath += "\\.";
                var info = new ProcessStartInfo(path, "/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010 /DIR=\"" +
                    installPath + "\" /MERGETASKS=\"" + (item.Shortcut ? "desktopicon" : "!desktopicon") + "\"")
                { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) };
                var process = Process.Start(info);
                if (process == null) throw new IOException("Не удалось запустить установщик.");
                try
                {
                    var wait = Stopwatch.StartNew();
                    while (!process.WaitForExit(200))
                    {
                        if (!token.IsCancellationRequested && wait.Elapsed < TimeSpan.FromMinutes(20)) continue;
                        var pending = new InstallerStillRunningException(process, Path.GetFileName(path));
                        process = null; // The completion task now owns the process.
                        throw pending;
                    }
                    if (process.ExitCode != 0 && process.ExitCode != 3010)
                        throw new IOException("Установщик завершился с кодом " + process.ExitCode + ".");
                    BarsReleaseCatalog.VerifyInstalledProgram(path, destination);
                    if (process.ExitCode == 3010) throw new IOException("Пакет установлен. Для продолжения требуется перезагрузка.");
                }
                finally { if (process != null) process.Dispose(); }
            }
        }

        private async Task Run(string stage, bool install, Action<CancellationToken> work)
        {
            if (operation != null) return;
            using (var cancellation = new CancellationTokenSource())
            {
                operation = cancellation;
                installing = install;
                UpdateActions();
                Log(stage);
                progress.Stage(stage);
                bool refreshed = false;
                IDisposable lease = null;
                InstallerStillRunningException detached = null;
                try
                {
                    if (install) lease = await WindowsOperationQueue.EnterAsync(() => Log(WindowsOperationQueue.WaitingMessage),
                        () => IsDisposed || cancellation.IsCancellationRequested);
                    try { await Task.Run(() => work(cancellation.Token)); }
                    catch (InstallerStillRunningException error) { detached = error; throw; }
                    finally
                    {
                        if (lease != null)
                            WindowsOperationQueue.ReleaseAfter(lease, detached == null ? null : detached.Completion,
                                detached == null ? null : detached.PackageName);
                    }
                    await RefreshRows(cancellation.Token);
                    refreshed = true;
                    progress.Stage("Готово", complete:true);
                    Log("Готово");
                }
                catch (OperationCanceledException) { progress.Stage("Действие отменено", cancelled:true); Log("Действие отменено"); }
                catch (Exception error) { progress.Stage(error.Message, failed:true); Log(error.Message); }
                {
                    try { if (!refreshed && !cancellation.IsCancellationRequested && !closeAfterOperation) await RefreshRows(cancellation.Token); }
                    catch (OperationCanceledException) { }
                    catch (Exception error) { Log(error.Message); }
                    operation = null;
                    installing = false;
                    UpdateActions();
                }
            }
            if (closeAfterOperation && !IsDisposed) Close();
        }

        private void Log(string text) { journal.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine); }
    }
}
