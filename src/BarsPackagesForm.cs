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
            page = new SqlManagerPage(this, FindSqlSoftware,
                () => { if (page.CanClose(false) && !ModuleNavigation.ReturnToHub()) Close(); }, Colors);
            Root.Controls.Add(page, 0, 1);
            Shown += async delegate { await page.RefreshLocal(FindSqlSoftware()); };
            FormClosing += (sender, args) => { if (!page.CanClose(ModuleNavigation.ExitConfirmed)) args.Cancel = true; };
            SetTheme(Colors.Dark, false);
        }
        private static string FindSqlSoftware()
        {
            string discovered = BundlePreferences.DiscoveredRoot(BundleSection.Sql);
            return discovered == null ? SoftwareCatalog.FindFolder() : Path.Combine(discovered, "_soft");
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
            internal string Name, Path, RelativePath;
            internal BarsReleaseCatalog.Artifact Remote;
            internal BundleRemoteFile YandexFile;
            internal bool CanDownload { get { return Remote != null || YandexFile != null; } }
            internal string Digest { get { return YandexFile != null ? YandexFile.Sha256 : Remote == null ? null : Remote.sha256; } }
            internal bool Shortcut;
            internal bool Local, Exists;
            internal string LocalError;
            internal Version Version;
            internal VersionGroup Group;
        }

        private sealed class VersionGroup
        {
            internal string Key, Label;
            internal int SelectedCount;
            internal readonly List<VersionGroup> Children = new List<VersionGroup>();
            internal readonly List<Package> Packages = new List<Package>();
            internal VersionGroup Parent;
            internal DataGridViewRow Header;
            internal int Depth { get { return Parent == null ? 0 : Parent.Depth + 1; } }
        }

        private sealed class ArchiveStamp
        {
            internal long Length;
            internal DateTime LastWrite, Created;
            internal string Error;
        }

        private readonly List<VersionGroup> rootGroups = new List<VersionGroup>();
        private Package[] loadedPackages = new Package[0];
        private Dictionary<string, ArchiveStamp> archiveCache = new Dictionary<string, ArchiveStamp>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> expandedGroups = new HashSet<string>(StringComparer.Ordinal);
        private readonly Font groupFont = new Font("Segoe UI", 9F, FontStyle.Bold);
        private readonly bool services;
        private string folder, bundleRoot;
        private const string Official = "Официальный репозиторий";
        private const string Yandex = "Яндекс Диск";
        private readonly BrandComboBox sourcePicker = new BrandComboBox { Dock = DockStyle.None,
            Anchor = AnchorStyles.Left, Width = 230, MinimumSize = new Size(210, 0), UseManagedPopup = true,
            DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Источник скачивания" };
        private readonly YandexBundleSource yandex = new YandexBundleSource();
        private List<BundleRemoteFile> yandexFiles = new List<BundleRemoteFile>();
        private readonly Label localSource;
        private readonly ServiceGrid grid = new ServiceGrid();
        private readonly LibraryGridScrollBar scroll = new LibraryGridScrollBar { Dock = DockStyle.Right, Width = 14, AlignToRows = true, AutoHideWhenUnused = true, UseCardBackground = true, DeferScrollCorrection = true };
        private readonly DownloadProgressView progress = new DownloadProgressView { Dock = DockStyle.Fill };
        private readonly JournalTextBox journal = new JournalTextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true };
        private readonly BrandButton scan, download, apply, cancel, menu, sql;
        private List<BarsReleaseCatalog.Artifact> remote = new List<BarsReleaseCatalog.Artifact>();
        private BarsReleaseCatalog catalog;
        private readonly Dictionary<string, string> downloadErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource operation;
        private readonly HashSet<Package> markedPackages = new HashSet<Package>();
        private readonly Dictionary<string, string> deployedFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool sqlOpen;
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
            bundleRoot = BundlePreferences.Load(services ? BundleSection.BarsServices : BundleSection.BarsPrograms);
            folder = localFolder ?? PackageFolder(bundleRoot, services);
            Root.AutoScroll = true;
            Root.MinimumSize = new Size(470, 0);
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8,
                Margin = Padding.Empty, MinimumSize = new Size(0, 550) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 40, 34, services ? 54 : 42, 0, 56, 100, 106, 54 })
                body.RowStyles.Add(new RowStyle(height == 0 ? SizeType.Percent : SizeType.Absolute, height == 0 ? 100 : height));
            body.Controls.Add(ModuleHeading(Text), 0, 0);
            localSource = Label("Локальный комплект: " + folder, true);
            body.Controls.Add(localSource, 0, 1);
            sourcePicker.Items.AddRange(new object[] { Official, Yandex });
            sourcePicker.SelectedIndex = 0;
            sourcePicker.Margin = new Padding(0, 6, 0, 4);
            var sourceRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 226));
            sourceRow.Controls.Add(sourcePicker, 0, 0);
            if (!services)
            {
                sourcePicker.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                sourcePicker.Margin = new Padding(0, 4, 0, 0);
                sql = Button("Подключение к базе данных", false);
                sql.Dock = DockStyle.None;
                sql.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                sql.Margin = Padding.Empty;
                var sqlHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
                sqlHost.Controls.Add(sql);
                sourceRow.Controls.Add(sqlHost, 1, 0);
                sourceRow.Layout += delegate
                {
                    sql.SetBounds(0, sourcePicker.Top - sqlHost.Top, sqlHost.ClientSize.Width, sourcePicker.Height);
                };
            }
            body.Controls.Add(sourceRow, 0, 2);
            sourcePicker.SelectedIndexChanged += ChangeSource;
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
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 36 });
            foreach (string label in new[] { "Файл", "Версия", "Источник", "Состояние" })
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = label, FillWeight = label == "Файл" ? 185 : label == "Источник" ? 110 : 80,
                    MinimumWidth = label == "Источник" ? 129 : 28, SortMode = DataGridViewColumnSortMode.NotSortable });
            if (!services)
                grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Ярлык", FillWeight = 40, MinimumWidth = 68 });
            grid.ClientSizeChanged += delegate { FitColumns(); };
            grid.CellMouseDown += (sender, args) =>
            {
                if (args.Button != MouseButtons.Left || args.RowIndex < 0) return;
                if (args.Clicks > 1 && grid.Rows[args.RowIndex].Tag is VersionGroup) return;
                Toggle(args.RowIndex, args.ColumnIndex);
            };
            grid.CellPainting += PaintCheck;
            grid.KeyDown += (sender, args) =>
            {
                if (grid.CurrentCell == null) return;
                if (args.KeyCode == Keys.Space || (args.KeyCode == Keys.Enter && grid.CurrentRow.Tag is VersionGroup))
                {
                    Toggle(grid.CurrentCell.RowIndex, grid.CurrentCell.ColumnIndex);
                    args.SuppressKeyPress = true;
                }
            };
            scroll.Bind(grid);
            card.Controls.Add(grid);
            card.Controls.Add(scroll);
            body.Controls.Add(card, 0, 3);
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
            body.Controls.Add(actions, 0, 4);
            body.Controls.Add(progress, 0, 5);
            var journalCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(12), Margin = Padding.Empty };
            journal.BorderStyle = BorderStyle.None;
            journalCard.Controls.Add(journal);
            body.Controls.Add(journalCard, 0, 6);
            menu = Button("В меню", false);
            var close = Button("Закрыть", false);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(0, 10, 0, 0) };
            footer.Controls.Add(menu);
            footer.Controls.Add(close);
            ModuleNavigation.RightAlignedFooter(footer, menu, close);
            body.Controls.Add(footer, 0, 7);
            Root.Controls.Add(body, 0, 1);
            scan.Click += Scan;
            download.Click += Download;
            apply.Click += Apply;
            if (sql != null) sql.Click += async delegate
            {
                if (operation != null || sqlOpen) return;
                var selected = Selected();
                var item = selected.Length == 1 ? selected[0] : selected.Length == 0 && grid.CurrentRow != null ? grid.CurrentRow.Tag as Package : null;
                if (item == null)
                {
                    BrandMessageBox.Show(this, "Выбери конкретную программу для настройки подключения к SQL Server", Text);
                    return;
                }
                sqlOpen = true;
                UpdateActions();
                try
                {
                    string deployed;
                    deployedFolders.TryGetValue(item.Name, out deployed);
                    var configuration = await Task.Run(() => BarsSqlConfiguration.ForPackage(item.Name, deployed));
                    if (IsDisposed) return;
                    using (var dialog = new BarsSqlDialog(Colors, Icon, configuration))
                        if (await ModuleNavigation.ShowOwnedAsync(this, dialog, false) == DialogResult.OK && !IsDisposed)
                            Log("Настройки подключения SQL сохранены.");
                }
                catch (Exception error) { if (!IsDisposed) BrandMessageBox.Show(this, error.Message, Text); }
                finally { sqlOpen = false; if (!IsDisposed) UpdateActions(); }
            };
            cancel.Click += delegate { if (operation != null) operation.Cancel(); };
            menu.Click += delegate { if (!ModuleNavigation.ReturnToHub()) Close(); };
            close.Click += delegate { Close(); };
            Shown += async delegate { await Run("Проверяем локальный комплект", false, _ => { }); };
            if (localFolder == null)
                Activated += async delegate
                {
                    if (operation != null || sqlOpen) return;
                    string selectedRoot = BundlePreferences.Load(services ? BundleSection.BarsServices : BundleSection.BarsPrograms);
                    string selected = PackageFolder(selectedRoot, services);
                    if (String.Equals(folder, selected, StringComparison.OrdinalIgnoreCase)) return;
                    bundleRoot = selectedRoot;
                    folder = selected;
                    localSource.Text = "Локальный комплект: " + folder;
                    markedPackages.Clear();
                    loadedPackages = new Package[0];
                    archiveCache.Clear();
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

        private void FitColumns()
        {
            var columns = grid.Columns.Cast<DataGridViewColumn>().Skip(1).ToArray();
            int remaining = grid.ClientSize.Width - grid.Columns[0].Width - 1 - columns.Sum(column => column.MinimumWidth);
            if (remaining < 0) return;
            float weights = columns.Sum(column => column.FillWeight);
            foreach (var column in columns)
            {
                int extra = (int)Math.Round(remaining * column.FillWeight / weights);
                column.Width = column.MinimumWidth + extra;
                remaining -= extra;
                weights -= column.FillWeight;
            }
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
            sourcePicker.Palette = Colors;
            if (localSource != null) localSource.ForeColor = Colors.Muted;
            scroll.Palette = Colors;
            progress.Palette = Colors;
            foreach (DataGridViewRow row in grid.Rows)
                if (row.Tag is VersionGroup) ApplyGroupStyle(row, (VersionGroup)row.Tag);
        }

        private void Toggle(int index, int column)
        {
            if (operation != null || sqlOpen || column < 0) return;
            var group = grid.Rows[index].Tag as VersionGroup;
            if (group != null)
            {
                ToggleGroup(group);
                return;
            }
            var item = grid.Rows[index].Tag as Package;
            if (item == null) return;
            if (!services && column == 5)
            {
                if (!BarsReleaseCatalog.HasSilentProfile(item.Name)) return;
                item.Shortcut = !item.Shortcut;
                grid.Rows[index].Cells[5].Value = item.Shortcut;
                return;
            }
            var cell = grid.Rows[index].Cells[0];
            cell.Value = !Equals(cell.Value, true);
            bool selected = Equals(cell.Value, true);
            if (selected) markedPackages.Add(item);
            else markedPackages.Remove(item);
            CountSelection(item.Group, selected ? 1 : -1);
            UpdateActions();
        }

        private void PaintCheck(object sender, DataGridViewCellPaintingEventArgs args)
        {
            if (args.RowIndex < 0 || (args.ColumnIndex != 0 && args.ColumnIndex != 5)) return;
            args.Paint(args.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.SelectionBackground);
            var item = grid.Rows[args.RowIndex].Tag as Package;
            if (item == null || (args.ColumnIndex == 5 && !BarsReleaseCatalog.HasSilentProfile(item.Name)))
            {
                var group = grid.Rows[args.RowIndex].Tag as VersionGroup;
                if (group != null && args.ColumnIndex == 0)
                {
                    bool collapsed = !expandedGroups.Contains(group.Key);
                    TextRenderer.DrawText(args.Graphics, collapsed ? "▸" : "▾", groupFont,
                        args.CellBounds, Colors.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
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
            bool idle = operation == null && !sqlOpen;
            var selected = Selected();
            scan.Enabled = menu.Enabled = sourcePicker.Enabled = idle;
            if (sql != null) sql.Enabled = idle;
            download.Enabled = idle && selected.Any(item => item.CanDownload);
            apply.Enabled = idle && selected.Length > 0 && selected.All(item => item.Local &&
                (services || BarsReleaseCatalog.HasSilentProfile(item.Name)));
            cancel.Text = installing ? "Прекратить ожидание" : "Отменить";
            cancel.Enabled = !idle;
        }

        private async Task RefreshRows(CancellationToken token)
        {
            var files = new Dictionary<string, Package>(StringComparer.OrdinalIgnoreCase);
            var marked = new HashSet<string>(Selected().Select(item => item.RelativePath), StringComparer.OrdinalIgnoreCase);
            var shortcuts = new HashSet<string>(loadedPackages.Where(item => item.Shortcut)
                .Select(item => item.RelativePath), StringComparer.OrdinalIgnoreCase);
            var nextCache = new Dictionary<string, ArchiveStamp>(StringComparer.OrdinalIgnoreCase);
            await Task.Run(() =>
            {
                foreach (string relative in BarsPackagePaths.LocalFiles(folder, services, token))
                    files.Add(relative, NewPackage(relative));
                var legacyByName = files.Values.ToLookup(item => item.Name, StringComparer.OrdinalIgnoreCase);
                foreach (var file in remote.OrderBy(item => item.Channel != "stable"))
                {
                    string relative = BarsPackagePaths.Official(file.fileName, file.Version, services);
                    Package package;
                    if (!files.TryGetValue(relative, out package))
                    {
                        var legacy = legacyByName[file.fileName].ToArray();
                        if (legacy.Length == 1) package = legacy[0];
                        else files.Add(relative, package = NewPackage(relative));
                    }
                    if (package.Remote == null) package.Remote = file;
                }
                foreach (var file in yandexFiles)
                {
                    string relative = BarsPackagePaths.Relative(file.Path, services);
                    Package package;
                    if (!files.TryGetValue(relative, out package))
                        files.Add(relative, package = NewPackage(relative));
                    package.YandexFile = file;
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
                            var file = new FileInfo(item.Path);
                            long length = file.Length;
                            DateTime lastWrite = file.LastWriteTimeUtc;
                            DateTime created = file.CreationTimeUtc;
                            ArchiveStamp stamp;
                            if (!archiveCache.TryGetValue(item.Path, out stamp) || stamp.Length != length || stamp.LastWrite != lastWrite || stamp.Created != created)
                            {
                                string[] entries;
                                stamp = new ArchiveStamp { Length = length, LastWrite = lastWrite, Created = created,
                                    Error = ToolArchives.ReadStatus(ToolArchives.Inspect(item.Path, token, out entries)) };
                            }
                            item.LocalError = stamp.Error;
                            if (String.IsNullOrEmpty(stamp.Error)) nextCache[item.Path] = stamp;
                        }
                        else
                        {
                            if (new FileInfo(item.Path).Length < BarsReleaseCatalog.MinimumProgramBytes)
                            {
                                if (!item.CanDownload) files.Remove(item.RelativePath);
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
            archiveCache = nextCache;
            loadedPackages = files.Values.ToArray();
            markedPackages.Clear();
            grid.Rows.Clear();
            rootGroups.Clear();
            foreach (var package in loadedPackages)
                package.Version = BarsPackagePaths.PackageVersion(package.Name, package.Remote == null ? null : package.Remote.Version);

            IEnumerable<Package> grouped = files.Values;
            VersionGroup container = null;
            if (services)
            {
                var standalone = files.Values.Where(item => !item.RelativePath.Contains("/")).ToArray();
                if (standalone.Length > 0)
                    AddPackageRows(standalone, marked, shortcuts, AddGroup("services:root", "_Services", null));
                grouped = files.Values.Where(item => item.RelativePath.Contains("/")).ToArray();
                if (grouped.Any()) container = AddGroup("services:bin", "_bin", null);
            }

            foreach (var release in grouped.Where(item => item.Version != null)
                .GroupBy(item => ReleaseNumber(item.Version)).OrderByDescending(group => group.Key))
            {
                var parent = AddGroup("release:" + release.Key, "Выпуск " + release.Key, container);
                foreach (var version in release.GroupBy(item => item.Version)
                    .OrderByDescending(group => RevisionNumber(group.Key)).ThenByDescending(group => group.Key))
                {
                    var child = AddGroup("version:" + version.Key, "Версия " + version.Key, parent);
                    AddPackageRows(version, marked, shortcuts, child);
                }
            }
            var unversioned = grouped.Where(item => item.Version == null).ToArray();
            if (unversioned.Length > 0)
                AddPackageRows(unversioned, marked, shortcuts, AddGroup("unversioned", "Без версии", container));
            InsertRows(0, rootGroups.SelectMany(VisibleRows));
            expandedGroups.IntersectWith(AllGroups(rootGroups).Select(group => group.Key));
            if (files.Count == 0) Log("Локальный комплект пуст. Проверь доступные пакеты в сети.");
            UpdateActions();
        }

        private void AddPackageRows(IEnumerable<Package> items, HashSet<string> marked, HashSet<string> shortcuts, VersionGroup group)
        {
            foreach (var item in items.OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase))
            {
                item.Shortcut = shortcuts.Contains(item.RelativePath);
                item.Group = group;
                group.Packages.Add(item);
                if (marked.Contains(item.RelativePath))
                {
                    markedPackages.Add(item);
                    CountSelection(group, 1);
                }
            }
        }

        private DataGridViewRow PackageRow(Package item)
        {
                string source = item.Exists ? "Локальный источник" : item.YandexFile != null ? Yandex : Official;
                string state = item.Local ? (services || BarsReleaseCatalog.HasSilentProfile(item.Name) ? "Готов к работе" : "Нет профиля тихой установки") : "Можно скачать";
                string failure;
                if (downloadErrors.TryGetValue(item.RelativePath, out failure)) state = failure;
                if (!String.IsNullOrEmpty(item.LocalError)) state = item.LocalError;
                string version = item.Version == null ? "—" : item.Version.ToString();
                if (item.Remote != null && !String.IsNullOrWhiteSpace(item.Remote.Channel))
                    version += " / " + item.Remote.Channel;
                var row = (DataGridViewRow)grid.RowTemplate.Clone();
                row.CreateCells(grid, markedPackages.Contains(item), item.Name, version, source, state);
                row.Tag = item;
                row.Cells[1].Style.Padding = new Padding(16 + 10 * item.Group.Depth, 3, 6, 3);
                row.Cells[3].ToolTipText = source;
                row.Cells[4].ToolTipText = state;
                if (!services) row.Cells[5].Value = item.Shortcut;
                return row;
        }

        private VersionGroup AddGroup(string key, string label, VersionGroup parent)
        {
            var group = new VersionGroup { Key = key, Label = label, Parent = parent };
            (parent == null ? rootGroups : parent.Children).Add(group);
            return group;
        }

        private string GroupLabel(VersionGroup group)
        {
            return group.SelectedCount == 0 || expandedGroups.Contains(group.Key) ? group.Label :
                group.Label + "  Выбрано: " + group.SelectedCount;
        }

        private void CountSelection(VersionGroup group, int change)
        {
            for (; group != null; group = group.Parent)
            {
                group.SelectedCount += change;
                if (group.Header != null) group.Header.Cells[1].Value = GroupLabel(group);
            }
        }

        private static IEnumerable<VersionGroup> AllGroups(IEnumerable<VersionGroup> groups)
        {
            foreach (var group in groups)
            {
                yield return group;
                foreach (var child in AllGroups(group.Children)) yield return child;
            }
        }

        private IEnumerable<DataGridViewRow> VisibleRows(VersionGroup group)
        {
            var row = (DataGridViewRow)grid.RowTemplate.Clone();
            row.CreateCells(grid, false, GroupLabel(group));
            group.Header = row;
            row.Tag = group;
            ApplyGroupStyle(row, group);
            row.Cells[1].Style.Padding = new Padding(6 + 10 * group.Depth, 3, 6, 3);
            yield return row;
            if (expandedGroups.Contains(group.Key))
            {
                foreach (var child in group.Children)
                    foreach (var childRow in VisibleRows(child)) yield return childRow;
                foreach (var package in group.Packages) yield return PackageRow(package);
            }
        }

        private void InsertRows(int index, IEnumerable<DataGridViewRow> rows)
        {
            var batch = rows.ToArray();
            if (batch.Length != 0) grid.Rows.InsertRange(index, batch);
        }

        private void ApplyGroupStyle(DataGridViewRow row, VersionGroup group)
        {
            Color background = group.Parent == null ? Colors.SurfaceAlt : BrandMotion.Mix(Colors.Surface, Colors.SurfaceAlt, 0.55F);
            row.DefaultCellStyle.Font = groupFont;
            row.DefaultCellStyle.BackColor = background;
            row.DefaultCellStyle.ForeColor = Colors.Text;
            row.DefaultCellStyle.SelectionBackColor = BrandMotion.Mix(background, Colors.Selection, 0.55F);
            row.DefaultCellStyle.SelectionForeColor = Colors.SelectionText;
        }

        private static bool DescendsFrom(VersionGroup group, VersionGroup ancestor)
        {
            for (; group != null; group = group.Parent)
                if (group == ancestor) return true;
            return false;
        }

        private void ToggleGroup(VersionGroup group)
        {
            bool opened = expandedGroups.Add(group.Key);
            if (!opened) expandedGroups.Remove(group.Key);
            int start = group.Header.Index + 1;
            grid.SuspendLayout();
            try
            {
                if (opened)
                    InsertRows(start, group.Children.SelectMany(VisibleRows).Concat(group.Packages.Select(PackageRow)));
                else
                {
                    int end = start;
                    while (end < grid.Rows.Count)
                    {
                        var tag = grid.Rows[end].Tag;
                        var header = tag as VersionGroup;
                        var owner = header == null ? ((Package)tag).Group : header.Parent;
                        if (!DescendsFrom(owner, group)) break;
                        if (header != null) header.Header = null;
                        end++;
                    }
                    if (grid.CurrentRow != null && grid.CurrentRow.Index >= start && grid.CurrentRow.Index < end)
                        grid.CurrentCell = group.Header.Cells[1];
                    for (int index = end - 1; index >= start; index--) grid.Rows.RemoveAt(index);
                }
                group.Header.Cells[1].Value = GroupLabel(group);
            }
            finally { grid.ResumeLayout(); }
            grid.InvalidateRow(group.Header.Index);
        }

        private static int ReleaseNumber(Version version)
        {
            return version.Revision >= 0 ? version.Revision : version.Build >= 0 ? version.Build : version.Minor;
        }

        private static int RevisionNumber(Version version)
        {
            return version.Revision >= 0 ? version.Build : version.Build >= 0 ? version.Minor : version.Major;
        }
        private Package NewPackage(string relative)
        {
            return new Package { Name = relative.Split('/').Last(), RelativePath = relative,
                Path = YandexBundleSource.LocalPath(folder, "/" + relative) };
        }

        private async void ChangeSource(object sender, EventArgs args)
        {
            if (operation != null) return;
            remote.Clear();
            yandexFiles.Clear();
            catalog = null;
            downloadErrors.Clear();
            await Run("Проверяем локальный комплект", false, token => { });
        }

        private async void Scan(object sender, EventArgs args)
        {
            bool useYandex = sourcePicker.SelectedIndex == 1;
            await Run(useYandex ? "Проверяем Яндекс Диск..." : "Проверяем официальный репозиторий Барс...", false, token =>
            {
                if (useYandex)
                {
                    string branch = BarsPackagePaths.Branch(services);
                    try
                    {
                        yandexFiles = yandex.Scan(token, branch, BarsPackagePaths.Include)
                            .Where(file => BarsReleaseCatalog.ValidName(file.Path.Split('/').Last(), services) &&
                                (services || file.Size >= BarsReleaseCatalog.MinimumProgramBytes)).ToList();
                    }
                    catch (System.Net.WebException error)
                    {
                        token.ThrowIfCancellationRequested();
                        var response = error.Response as System.Net.HttpWebResponse;
                        bool missing = response != null && response.StatusCode == System.Net.HttpStatusCode.NotFound;
                        if (response != null) response.Dispose();
                        throw new IOException(missing ? "На Яндекс Диске не найдена папка " + branch + "." :
                            "Не удалось проверить каталог на Яндекс Диске.", error);
                    }
                }
                else
                {
                    var next = new BarsReleaseCatalog();
                    next.Login(token);
                    var found = next.Scan(services, token);
                    catalog = next;
                    remote = found;
                }
            });
        }

        private void DownloadYandex(Package item, string original, CancellationToken token)
        {
            var file = item.YandexFile;
            string stage = "Скачиваем пакет";
            Action<long> report = bytes => progress.Report(new TransferProgress { Stage = stage, File = item.Name,
                Key = item.RelativePath, Bytes = bytes, Total = file.Size, Overall = file.Size > 0 ? Math.Min(.99, (double)bytes/file.Size) : -1 });
            new BundleTransfer(yandex).Download(file, bundleRoot, original, report, token, null,
                value => { stage = value; progress.Report(new TransferProgress { Stage = stage, File = item.Name, Key = item.RelativePath }); },
                part =>
                {
                    if (services) ToolArchives.ValidateDownload(part, item.Name, token);
                    else BarsReleaseCatalog.ValidateExecutable(part);
                });
        }

        private async void Download(object sender, EventArgs args)
        {
            var items = Selected().Where(item => item.CanDownload).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            if (items.Length == 0) return;
            await Run("Скачиваем выбранные пакеты", false, token =>
            {
                var destinations = items.ToDictionary(item => item, item => item.YandexFile == null
                    ? item.Path : BundleFolders.PackageDestination(bundleRoot, item.YandexFile.Path));
                var duplicate = destinations.GroupBy(entry => entry.Value, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
                if (duplicate != null)
                    throw new IOException("Выбранные пакеты сохраняются в один файл: " + duplicate.Key);
                var originals = destinations.ToDictionary(entry => entry.Key, entry =>
                {
                    token.ThrowIfCancellationRequested();
                    ToolsDeployment.SafePath(entry.Value);
                    return File.Exists(entry.Value) ? YandexBundleSource.Hash(entry.Value, token) : null;
                });
                var replacements = items.Where(item => originals[item] != null).Select(item => item.Name).ToArray();
                token.ThrowIfCancellationRequested();
                if (replacements.Length != 0)
                {
                    var answer = (DialogResult)Invoke((Func<DialogResult>)(() => BrandMessageBox.Show(this,
                        "Заменить локальные файлы после проверки?\r\n\r\n" + SelectionPreview(replacements), Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)));
                    if (answer != DialogResult.Yes) throw new OperationCanceledException();
                }
                using (items.Any(item => item.YandexFile != null) ?
                    BundleSession.Begin(bundleRoot, message => Invoke((MethodInvoker)(() => Log(message)))) : null)
                {
                var failed = new List<string>();
                foreach (var item in items)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (item.YandexFile != null) DownloadYandex(item, originals[item], token);
                        else catalog.Download(item.Remote, destinations[item], originals[item], services, token, progress.Report);
                        archiveCache.Remove(destinations[item]);
                        downloadErrors.Remove(item.RelativePath);
                    }
                    catch (Exception error)
                    {
                        if (!(error is IOException) && !(error is InvalidDataException) && !(error is UnauthorizedAccessException) && !(error is System.Net.WebException)) throw;
                        token.ThrowIfCancellationRequested();
                        string message = error is System.Net.WebException && item.YandexFile != null
                            ? "Не удалось скачать файл с Яндекс Диска." : error.Message;
                        downloadErrors[item.RelativePath] = message;
                        string detail = item.Name + ": " + message;
                        failed.Add(detail);
                    }
                }
                if (failed.Count == 1) throw new IOException(failed[0]);
                if (failed.Count > 1)
                {
                    Invoke((MethodInvoker)delegate { foreach (string detail in failed) Log(detail); });
                    throw new IOException("Не скачано файлов: " + failed.Count + ". Подробности в журнале.");
                }
                }
            });
        }

        private static string SelectionPreview(string[] names)
        {
            return String.Join("\r\n", names.Take(6)) + (names.Length > 6 ? "\r\nИ ещё " + (names.Length - 6) : "");
        }

        internal static string[] ApplyFolders(IEnumerable<string> names, bool services)
        {
            var packages = names.ToArray();
            var folders = packages.Select(name => services ? Path.GetFileNameWithoutExtension(name) :
                BarsReleaseCatalog.InstallFolderName(name)).ToArray();
            var conflicts = packages.Zip(folders, (name, folder) => new { name, folder })
                .GroupBy(item => item.folder, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)
                .SelectMany(group => group.Select(item => item.name)).ToArray();
            if (conflicts.Length != 0)
                throw new IOException("Эти пакеты используют одну папку. Оставь по одному пакету для каждой папки:\r\n\r\n" +
                    SelectionPreview(conflicts));
            return folders;
        }

        private async void Apply(object sender, EventArgs args)
        {
            var items = Selected().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            if (items.Length == 0) return;
            string[] folders;
            try { folders = ApplyFolders(items.Select(item => item.Name), services); }
            catch (IOException error) { BrandMessageBox.Show(this, error.Message, Text); return; }
            string destination;
            using (var picker = new FolderBrowserDialog { Description = services ? "Выбери папку для распаковки" : "Выбери папку для установки",
                ShowNewFolderButton = true })
            {
                if (ScopedDialogs.Show(this, picker) != DialogResult.OK) return;
                destination = picker.SelectedPath;
            }
            string confirmation = (services ? "Распаковать выбранные архивы в " : "Тихо установить выбранные программы в ") +
                destination + "?\r\n\r\n" + SelectionPreview(items.Select(item => item.Name).ToArray());
            if (!services)
            {
                var unverified = items.Where(item => !BarsReleaseCatalog.HasReferenceDigest(item.Name,
                    item.Digest)).Select(item => item.Name).ToArray();
                if (unverified.Length > 0)
                    confirmation += "\r\n\r\nДля следующих установщиков нет контрольной суммы. Продолжай только для файлов из доверенного источника:\r\n" +
                        String.Join("\r\n", unverified.Take(5)) + (unverified.Length > 5 ? "\r\nИ ещё " + (unverified.Length - 5) : "");
            }
            if (BrandMessageBox.Show(this, confirmation, Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            await Run(services ? "Распаковываем архивы" : "Выполняем тихую установку", !services, token =>
            {
                var targets = folders.Select(name => YandexBundleSource.LocalPath(destination, "/" + name)).ToArray();
                for (int i = 0; i < items.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    ToolsDeployment.SafePath(items[i].Path);
                    if (File.Exists(targets[i]) || (services && Directory.Exists(targets[i])))
                        throw new IOException("Папка или файл уже существует: " + targets[i] + ".");
                }
                for (int i = 0; i < items.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    ToolsDeployment.SafePath(items[i].Path);
                    ToolsDeployment.SafePath(targets[i]);
                    if (services) Extract(items[i].Path, targets[i], token);
                    else Install(items[i], targets[i], token);
                    if (!services) deployedFolders[items[i].Name] = targets[i];
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
                BarsReleaseCatalog.VerifySilentProfile(path, item.Digest);
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
                    BundleSession.CleanIdle(bundleRoot, Log);
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

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) groupFont.Dispose();
        }
        private void Log(string text) { journal.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine); }
    }
}
