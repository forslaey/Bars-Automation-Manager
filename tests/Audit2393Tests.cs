using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class Audit2393Tests
    {
        private sealed class FixturePackages : BarsPackagesForm
        {
            internal FixturePackages(string localFolder, bool services = true) : base(services, localFolder) { }
        }

        private static int count;

        private static void Check(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException(detail);
            count++;
        }

        private static void Wait(Func<bool> succeeded, string detail)
        {
            var elapsed = Stopwatch.StartNew();
            do
            {
                Application.DoEvents();
                if (succeeded()) return;
                Thread.Sleep(10);
            } while (elapsed.ElapsedMilliseconds < 5000);
            throw new TimeoutException("UI fixture did not complete: " + detail);
        }

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 1)
            {
                using (var exit = EventWaitHandle.OpenExisting(args[0])) return exit.WaitOne(15000) ? 0 : 1;
            }
            Application.EnableVisualStyles();
            string root = Path.Combine(Path.GetTempPath(), "bars-audit-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllBytes(Path.Combine(root, "BrokenService.zip"), new byte[] { 1 });
                using (var file = File.Create(Path.Combine(root, "WorkingService.zip")))
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
                using (var writer = new StreamWriter(archive.CreateEntry("readme.txt").Open()))
                    writer.Write("Inert test fixture.");
                foreach (bool dark in new[] { false, true })
                {
                    using (var hub = new ManagerHub())
                    {
                        hub.SetTheme(dark, false);
                        hub.Show();
                        foreach (int width in new[] { 1120, 1000, 900, 870 })
                        {
                            hub.ClientSize = new Size(width, hub.ClientSize.Height);
                            Application.DoEvents();
                            foreach (var label in Find(hub).OfType<InheritedThemeLabel>().Where(label => label.Parent.Parent is Card))
                                Check(TextRenderer.MeasureText(label.Text, label.Font, new Size(label.Width, Int32.MaxValue),
                                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height <= label.Height,
                                    "Clipped at width " + width + ", bounds " + label.Size + ", font " + label.Font.SizeInPoints + ": " + label.Text);
                        }
                        hub.Close();
                    }
                    using (var form = new FixturePackages(root))
                    {
                        form.SetTheme(dark, false);
                        form.Show();
                        var table = Find(form).OfType<ServiceGrid>().Single();
                        Wait(() => table.Rows.Count > 0, "services");
                        ClickPackageGroup(table, table.Rows[0]);
                        Check(table.Rows.Cast<DataGridViewRow>().Count(row => row.Cells[4].Value != null) == 2, "Service rows appear on expansion");
                        var rows = table.Rows.Cast<DataGridViewRow>().ToDictionary(row => (string)row.Cells[1].Value);
                        Check((string)rows["BrokenService.zip"].Cells[4].Value == "Архив повреждён", "Corrupt archive marked ready.");
                        Check((string)rows["WorkingService.zip"].Cells[4].Value == "Готов к работе", "Valid archive did not pass.");
                        form.MinimumSize = Size.Empty;
                        form.ClientSize = new Size(768, 650);
                        var viewport = Find(form).OfType<FillContentPanel>().Single();
                        Wait(() => viewport.VerticalScroll.Visible, "Compact packages scroll");
                        Check(table.Height - table.ColumnHeadersHeight >= table.RowTemplate.Height, "Compact package table retains a full row");
                        var bottom = Find(form).OfType<BrandButton>().Single(button => button.Text == "В меню");
                        viewport.ScrollControlIntoView(bottom);
                        Application.DoEvents();
                        Check(viewport.ClientRectangle.Contains(viewport.RectangleToClient(bottom.RectangleToScreen(bottom.ClientRectangle))),
                            "Compact package footer is reachable by scrolling");
                        form.Close();
                    }
                }
                string installer = Path.Combine(root, "Bars.Administrator_v90.0.0.exe");
                byte[] bytes = new byte[(int)BarsReleaseCatalog.MinimumProgramBytes];
                bytes[0] = 0x4d; bytes[1] = 0x5a; bytes[60] = 64; bytes[64] = 0x50; bytes[65] = 0x45;
                var marker = System.Text.Encoding.ASCII.GetBytes("Inno Setup Setup Data (");
                Buffer.BlockCopy(marker, 0, bytes, 128, marker.Length);
                File.WriteAllBytes(installer, bytes);
                string digest;
                using (var hash = System.Security.Cryptography.SHA256.Create())
                    digest = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
                BarsReleaseCatalog.VerifySilentProfile(installer, digest);
                Check(!BarsReleaseCatalog.HasReferenceDigest(Path.GetFileName(installer), null), "Unknown build has no pinned digest.");
                Check(Rejected(() => BarsReleaseCatalog.VerifySilentProfile(installer, new string('0', 64))), "Digest mismatch accepted.");
                File.WriteAllBytes(installer, new byte[] { 1 });
                Check(Rejected(() => BarsReleaseCatalog.VerifySilentProfile(installer)), "Truncated installer accepted.");

                using (var programs = new FixturePackages(root, false))
                {
                    string name = Path.GetFileName(installer);
                    var server = new System.Collections.Generic.List<BarsReleaseCatalog.Artifact>
                    {
                        new BarsReleaseCatalog.Artifact { fileName = name, sizeBytes = 4000000, Channel = "stable" }
                    };
                    typeof(BarsPackagesForm).GetField("remote", System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance).SetValue(programs, server);
                    programs.Show();
                    var table = Find(programs).OfType<ServiceGrid>().Single();
                    Wait(() => table.Rows.Count > 0, "merged programs");
                    ClickPackageGroup(table, table.Rows[0]);
                    ClickPackageGroup(table, table.Rows[1]);
                    Check(table.Rows.Cast<DataGridViewRow>().Count(row => row.Cells[4].Value != null) == 1, "Merged program appears once");
                    var package=table.Rows.Cast<DataGridViewRow>().Single(row => row.Cells[4].Value != null);
                    Check(((string)package.Cells[4].Value).Contains("меньше 2 МБ") && (string)package.Cells[3].Value == "Локальный источник", "Tiny local EXE retains local source and error.");
                    programs.Close();
                }

                string first = Directory.CreateDirectory(Path.Combine(root, "First")).FullName;
                string second = Directory.CreateDirectory(Path.Combine(root, "Second")).FullName;
                Directory.CreateDirectory(Path.Combine(second, "OnlyInSecond"));
                using (var picker = new BrandPathDialog("Test picker", root, null, null, null, true, false, true))
                {
                    picker.Show();
                    var address = Find(picker).OfType<BrandTextBox>().Single(box => box.AccessibleName == "Путь к папке");
                    var go = Find(picker).OfType<BrandButton>().Single(button => button.Text == "Перейти");
                    var rows = Find(picker).OfType<ServiceGrid>().Single().Rows;
                    Wait(() => rows.Count == 2, "initial folder");
                    var make = Find(picker).OfType<BrandButton>().Single(button => button.Text == "Новая папка");
                    address.Text = first; go.PerformClick();
                    Check(!make.Enabled && !((Control)picker.AcceptButton).Enabled, "Actions could use the old directory during navigation.");
                    address.Text = second; go.PerformClick();
                    try { Wait(() => rows.Count == 1 && Equals(rows[0].Tag, Path.Combine(second, "OnlyInSecond")), "latest folder"); }
                    catch (TimeoutException)
                    {
                        throw new InvalidOperationException("Latest folder: " + address.Text + " / " +
                            String.Join(" / ", rows.Cast<DataGridViewRow>().Select(row => Convert.ToString(row.Tag))) + " / " +
                            String.Join(" / ", Find(picker).OfType<Label>().Select(label => label.Text)));
                    }
                    Check(address.Text == second, "Stale navigation replaced the selected path. Expected " + second + ", actual " + address.Text + ", statuses " + String.Join(" / ", Find(picker).OfType<Label>().Select(label => label.Text)));
                    Check(make.Enabled && ((Control)picker.AcceptButton).Enabled, "Actions did not recover after navigation.");
                    picker.Close();
                }

                string invalidArchive = Path.Combine(root, "0.Microsoft Visual C++ 2005-2022 Redistributable 99.1.2.3.7z");
                var corrupt = new byte[32];
                Buffer.BlockCopy(new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c }, 0, corrupt, 0, 6);
                File.WriteAllBytes(invalidArchive, corrupt);
                Check(Rejected(() => VcArchivePackage.ValidateContainer(invalidArchive)), "Magic bytes alone passed 7z validation.");
                Check(!LibraryDiscovery.Scan(root, delegate { }, null).Any(item => item.RelativePath.EndsWith(".7z")), "Corrupt 7z became an install profile.");

                string installed = Directory.CreateDirectory(Path.Combine(root, "Installed")).FullName;
                string executing = System.Reflection.Assembly.GetExecutingAssembly().Location;
                File.Copy(executing, Path.Combine(installed, "unrelated.exe"));
                Check(InstallRejected(installer, installed), "An unrelated executable confirmed installation.");
                File.Copy(executing, Path.Combine(installed, "Bars.Administrator.exe"));
                Check(InstallRejected(installer, installed), "The wrong version confirmed installation.");
                string matching = "Bars.Administrator_v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version + ".exe";
                BarsReleaseCatalog.VerifyInstalledProgram(matching, installed);
                Check(true, "Expected program and version accepted.");

                foreach (bool dark in new[] { false, true })
                foreach (bool services in new[] { false, true }) VerifyPackageGroups(root, dark, services);
                Check(NewFolderNameDialog.DestinationText(@"C:\") == @"Папка будет создана на диске C:\", "Root destination identifies drive");
                Check(NewFolderNameDialog.DestinationText(@"D:\Комплект").Contains(@"на диске D: в папке D:\Комплект"), "Nested destination identifies drive and directory");
                Check(NewFolderNameDialog.DestinationText(@"\\server\share\folder").Contains("по пути"), "Network destination uses path wording");
                foreach (bool dark in new[] { false, true })
                using (var prompt = new NewFolderNameDialog(ThemePalette.Create(dark), null, root))
                {
                    prompt.Show();
                    Application.DoEvents();
                    var caption = Find(prompt).OfType<InputCaptionLabel>().Single();
                    var frame = Find(prompt).OfType<PathFieldFrame>().Single();
                    var status = Find(prompt).OfType<FlushLabel>().Single();
                    var footer = ((Control)prompt.AcceptButton).Parent.Parent;
                    Check(caption.Left == frame.Left && frame.Left == status.Left, "Caption, field and status share the left edge");
                    Check(frame.Top - caption.Top > caption.Font.Height && frame.Bottom <= status.Top &&
                            status.Bottom <= footer.Top && footer.Bottom <= footer.Parent.ClientSize.Height,
                        "Compact layout preserves input spacing and footer: caption " + caption.Bounds + " font " + caption.Font.Height + " field " + frame.Bounds + " hint " + status.Bounds + " footer " + footer.Bounds + " parent " + footer.Parent.ClientSize);
                    using (var image = new Bitmap(prompt.Width,prompt.Height))
                    {
                        prompt.DrawToBitmap(image, new Rectangle(Point.Empty,image.Size));
                        image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"new-folder-"+dark+".png"));
                    }
                    prompt.Close();
                }
                VerifyBundleDiscovery(root);
                VerifyPackagePaths(root);
                VerifyDetachedQueue(root, executing);
                VerifyRetargeting(root, executing);

                string signal = @"Local\BARS.Manager.Ready." + Guid.NewGuid().ToString("N");
                using (var ready = new EventWaitHandle(false, EventResetMode.AutoReset, signal))
                {
                    Check(!ready.WaitOne(0), "Readiness must start unset.");
                    Environment.SetEnvironmentVariable(ManagerUpdates.ReadyVariable, signal);
                    ManagerUpdates.SignalReady();
                    Check(ready.WaitOne(0), "The helper did not receive readiness.");
                    Check(Environment.GetEnvironmentVariable(ManagerUpdates.ReadyVariable) == null, "Readiness leaked to child processes.");
                }
                Console.WriteLine("PASS: " + count + " narrow launcher, archive status and update readiness checks.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { Directory.Delete(root, true); }
        }

        private static void VerifyBundleDiscovery(string root)
        {
            string space = Directory.CreateDirectory(Path.Combine(root,"bundle-search")).FullName;
            string executable = Directory.CreateDirectory(Path.Combine(space,"launcher")).FullName;
            string primary = Directory.CreateDirectory(Path.Combine(space,"Комплект для касс")).FullName;
            string secondary = Directory.CreateDirectory(Path.Combine(space,"Набор для стенда")).FullName;
            string partial = Directory.CreateDirectory(Path.Combine(space,"Только программы Барс")).FullName;
            string ignored = Directory.CreateDirectory(Path.Combine(space,"deep","middle","bottom")).FullName;
            foreach (string path in new[] { primary, secondary, ignored })
            {
                File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(path,"_libraries")).FullName,"fixture.msi"),"metadata");
                File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(path,"_tools")).FullName,"fixture.zip"),"metadata");
            }
            string programs = Directory.CreateDirectory(Path.Combine(partial,"_soft","_Bars","_Soft","2.12.xx.168","168.2")).FullName;
            using (var stub = File.Create(Path.Combine(programs,"Bars.Administrator_v2.12.2.168.exe")))
                stub.SetLength(BarsReleaseCatalog.MinimumProgramBytes);
            string empty = Directory.CreateDirectory(Path.Combine(space,"empty","_libraries")).FullName;
            File.Create(Path.Combine(empty,"empty.exe")).Dispose();
            var drives = new[] { space };
            var token = CancellationToken.None;
            var candidates = BundleDiscovery.Find(BundleSection.Any, null, executable + Path.DirectorySeparatorChar, null, drives, token);
            Check(candidates.Count == 2 && candidates.Any(item => item.Root == primary) && candidates.Any(item => item.Root == secondary),
                "Search discovers renamed nearby sets and rejects incomplete and out-of-depth sets");
            Check(BundleDiscovery.Find(BundleSection.Any,secondary,executable,null,drives,token).Single().Root == secondary,
                "Remembered matching root has priority");
            Check(BundleDiscovery.Find(BundleSection.BarsPrograms,primary,executable,null,drives,token).Single().Root == partial,
                "A partial Bars bundle is recognized by the relevant package branch");
            Check(BundleDiscovery.Find(BundleSection.Sql,null,executable,null,drives,token).Count == 0,
                "Unrelated packages cannot satisfy SQL discovery");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                bool stopped = false;
                try { BundleDiscovery.Find(BundleSection.Any,null,executable,null,drives,cancelled.Token); }
                catch (OperationCanceledException) { stopped = true; }
                Check(stopped,"Discovery honors cancellation");
            }
            candidates[0].Sections = BundleSection.Libraries | BundleSection.Software | BundleSection.Tools | BundleSection.Wallpapers |
                BundleSection.Framework | BundleSection.Sql | BundleSection.BarsPrograms | BundleSection.BarsServices;
            foreach (bool dark in new[] { false, true })
            using (var picker = new BundleChoiceDialog(ThemePalette.Create(dark),null,candidates))
            {
                picker.Show();
                Application.DoEvents();
                var grid = Find(picker).OfType<ServiceGrid>().Single();
                Check(grid.Rows.Count == 2 && grid.TouchScrollingEnabled &&
                    (grid.Rows[0].Cells[1].Value as string).Contains("Библиотеки"), "Branded chooser lists detected sections");
                Check(grid.CellBorderStyle == DataGridViewCellBorderStyle.Single &&
                    grid.ColumnHeadersBorderStyle == DataGridViewHeaderBorderStyle.Single &&
                    grid.ColumnHeadersDefaultCellStyle.Alignment == DataGridViewContentAlignment.MiddleCenter &&
                    grid.ColumnHeadersDefaultCellStyle.Font.Bold, "Chooser uses library-grid header and borders");
                Check(grid.Rows[0].Height > grid.Rows[1].Height && grid.DefaultCellStyle.WrapMode == DataGridViewTriState.True,
                    "Long section list wraps without widening the dialog");
                using (var bitmap = new Bitmap(picker.Width, picker.Height))
                {
                    picker.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "found-bundles-" + dark + ".png"));
                }
                grid.CurrentCell = grid.Rows[1].Cells[0];
                ((Button)picker.AcceptButton).PerformClick();
                Check(picker.SelectedRoot == candidates[1].Root, "Chooser returns the selected root");
                picker.Close();
            }
        }

        private static void VerifyPackagePaths(string root)
        {
            const string installer = "Bars.Administrator_v2.12.3.168.exe";
            const string server = "BarsServer_2.8.8_BLSki_2.12.3.168.zip";
            Check(BarsPackagePaths.Official(installer, null, false) == "2.12.xx.168/168.3/" + installer,
                "Official installer goes into the series and minor folder");
            Check(BarsPackagePaths.Official(server, "2.8.8", true) == "_bin/168.3/" + server,
                "Common release suffix wins over the embedded server version");
            Check(BarsPackagePaths.Official("HttpService.1.24.zip", null, true) == "HttpService.1.24.zip",
                "Independent release stays at the service root");
            var branch = BarsPackagePaths.Branch(true);
            Check(BarsPackagePaths.Relative(branch + "/_bin/168.3/" + server, true) == "_bin/168.3/" + server &&
                !BarsPackagePaths.Include(branch + "/_bin/_CUSTOM/" + server), "Yandex path and excluded subtree");
            string local = Directory.CreateDirectory(Path.Combine(root, "path-fixture")).FullName;
            Directory.CreateDirectory(Path.Combine(local, "_bin", "168.3"));
            Directory.CreateDirectory(Path.Combine(local, "_custom"));
            File.WriteAllText(Path.Combine(local, "_bin", "168.3", server), "fixture");
            File.WriteAllText(Path.Combine(local, "_custom", server), "excluded");
            Check(BarsPackagePaths.LocalFiles(local, true, CancellationToken.None).Single() == "_bin/168.3/" + server,
                "Recursive local discovery excludes custom folders");
        }

        private static void VerifyPackageGroups(string root, bool dark, bool services)
        {
            string fixture = Directory.CreateDirectory(Path.Combine(root, "group-fixture")).FullName;
            using (var form = new FixturePackages(fixture, services))
            {
                string suffix = services ? ".zip" : ".exe";
                var versions = new[] { "2.12.0.168", "2.12.2.168", "2.12.1.168", "2.12.0.169" }
                    .Concat(Enumerable.Range(140,20).Select(number => "2.12.0." + number)).ToArray();
                var catalog = versions.SelectMany(version => new[] { "Bars.CacheDesk", "Bars.Administrator" }
                    .Select(name => new BarsReleaseCatalog.Artifact { fileName = name + "_" + version + suffix, Version = version, Channel = "stable" })).ToList();
                catalog.Add(new BarsReleaseCatalog.Artifact { fileName = "Unknown" + suffix, Channel = "stable" });
                typeof(BarsPackagesForm).GetField("remote", System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance).SetValue(form, catalog);
                form.SetTheme(dark, false);
                form.Show();
                var table = Find(form).OfType<ServiceGrid>().Single();
                Wait(() => table.Rows.Count > 0, "grouped catalog");
                Func<string, DataGridViewRow> rowNamed = label => table.Rows.Cast<DataGridViewRow>()
                    .SingleOrDefault(row => ((string)row.Cells[1].Value).StartsWith(label, StringComparison.Ordinal));
                var scroll = Find(form).OfType<LibraryGridScrollBar>().Single();
                var picker = Find(form).OfType<BrandComboBox>().Single();
                Check(picker.UseManagedPopup && picker.Items.Cast<string>().SequenceEqual(new[] { "Официальный репозиторий", "Яндекс Диск" }),
                    "Both sources use the branded popup");
                if (services)
                {
                    var visible = table.Rows.Cast<DataGridViewRow>().ToArray();
                    Check(visible.Length == 2 && (string)visible[0].Cells[1].Value == "_Services" &&
                        (string)visible[1].Cells[1].Value == "_bin", "Standalone service packages precede the collapsed release directory");
                    ClickPackageGroup(table, visible[1]);
                    Check(rowNamed("Выпуск 168") != null, "Release header appears under _bin");
                }
                else
                    Check(table.Rows.Cast<DataGridViewRow>().All(row => ((string)row.Cells[1].Value).StartsWith("Выпуск ") ||
                        (string)row.Cells[1].Value == "Без версии"), "Only release headers are allocated on first open");
                var newest = rowNamed("Выпуск 169");
                var major = rowNamed("Выпуск 168");
                Check(newest.Index < major.Index, "Newest major group first");
                ClickPackageGroup(table, newest);
                var newestChild = rowNamed("Версия 2.12.0.169");
                ClickPackageGroup(table, newestChild);
                var first169 = table.Rows[newestChild.Index + 1];
                Check(((string)first169.Cells[1].Value).StartsWith("Bars.Administrator") &&
                    (string)first169.Cells[3].Value == "Официальный репозиторий", "Product ordering and official source caption");
                ClickPackageGroup(table, newest);
                ClickPackageGroup(table, major);
                var child = rowNamed("Версия 2.12.2.168");
                Check(child.Index < rowNamed("Версия 2.12.1.168").Index &&
                    rowNamed("Версия 2.12.1.168").Index < rowNamed("Версия 2.12.0.168").Index, "Newest minor groups first");
                Check(rowNamed("Bars.Administrator") == null, "Click opens release synchronously and keeps versions collapsed");
                table.FirstDisplayedScrollingRowIndex = child.Index;
                TapPackageRow(table, child, false);
                var first = table.Rows[child.Index + 1];
                Check(first.Cells[4].Value != null, "Touch expands version before processing deferred redraw");
                Application.DoEvents();
                TapPackageRow(table, first);
                Check((bool)first.Cells[0].Value, "Single touch selects a package");
                TapPackageRow(table, child);
                Check(first.Index == -1 && ((string)child.Cells[1].Value).Contains("Выбрано: 1"), "Collapsed child removes rows and shows selection count");
                table.FirstDisplayedScrollingRowIndex = major.Index;
                TapPackageRow(table, major);
                Check(child.Index == -1 && ((string)major.Cells[1].Value).Contains("Выбрано: 1"), "Collapsed release shows selection count");
                TapPackageRow(table, major);
                child = rowNamed("Версия 2.12.2.168");
                Check(child != null && rowNamed("Bars.Administrator") == null, "Reopening release restores child collapse state");
                TapPackageRow(table, child);
                first = table.Rows[child.Index + 1];
                Check((bool)first.Cells[0].Value, "Reopening child preserves selected package");
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "bars-groups-" + (services ? "services" : "programs") + "-" + dark + ".png"));
                }
                TapPackageRow(table, child);
                Check(first.Index == -1, "Nested rows stay unallocated during scrolling");
                table.FirstDisplayedScrollingRowIndex = 0;
                for (int pass=0;pass<4;pass++)
                {
                    int before = table.FirstDisplayedScrollingRowIndex;
                    table.TouchPan(new Point(70,200),true,false);
                    table.TouchPan(new Point(70,120),false,true);
                    int after = table.FirstDisplayedScrollingRowIndex;
                    int steps = after - before;
                    Check(after > before && steps < 6 && table.Rows[after].Visible, "Small touch swipe scrolls a few visible rows");
                }
                table.TouchPan(new Point(70,200),true,false);
                table.TouchPan(new Point(70,-10000),false,true);
                Check(table.Rows[table.FirstDisplayedScrollingRowIndex].Visible && scroll.Visible, "Touch scroll reaches final page with themed scrollbar");
                table.FirstDisplayedScrollingRowIndex = child.Index;
                TapPackageRow(table, child);
                Check((bool)table.Rows[child.Index + 1].Cells[0].Value, "Swipe does not toggle a checkbox");
                form.Close();
            }
        }

        private static void ClickPackageGroup(ServiceGrid table, DataGridViewRow row)
        {
            var click = typeof(DataGridView).GetMethod("OnCellMouseDown",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(DataGridViewCellMouseEventArgs) }, null);
            click.Invoke(table, new object[] { new DataGridViewCellMouseEventArgs(1,row.Index,12,12,
                new MouseEventArgs(MouseButtons.Left,1,12,12,0)) });
        }

        private static void TapPackageRow(ServiceGrid table, DataGridViewRow row, bool pump = true)
        {
            var bounds = table.GetCellDisplayRectangle(1,row.Index,true);
            if(bounds.Height==0) throw new InvalidOperationException("Tap row is outside viewport");
            var point = new Point(bounds.Left+20,bounds.Top+bounds.Height/2);
            table.TouchMouse(0x201,point,true);
            table.TouchMouse(0x202,point,true);
            if (pump) Application.DoEvents();
        }

        private static void VerifyDetachedQueue(string root, string executing)
        {
            string name = @"Local\BARS.InstallFixture." + Guid.NewGuid().ToString("N");
            using (var exit = new EventWaitHandle(false, EventResetMode.AutoReset, name))
            using (var form = new FixturePackages(root, false))
            using (var child = Process.Start(new ProcessStartInfo(executing, name) { UseShellExecute = false, CreateNoWindow = true }))
            {
                const System.Reflection.BindingFlags hidden = System.Reflection.BindingFlags.NonPublic;
                var detached = typeof(BarsPackagesForm).GetNestedType("InstallerStillRunningException", hidden);
                var run = typeof(BarsPackagesForm).GetMethod("Run", hidden | System.Reflection.BindingFlags.Instance);
                Action<CancellationToken> interrupted = token =>
                {
                    var observed = Process.GetProcessById(child.Id);
                    throw (Exception)Activator.CreateInstance(detached, hidden | System.Reflection.BindingFlags.Instance,
                        null, new object[] { observed, "Fixture.exe" }, null);
                };
                try
                {
                    var operation = (System.Threading.Tasks.Task)run.Invoke(form, new object[] { "Fixture", true, interrupted });
                    Wait(() => operation.IsCompleted, "detached installation returns to UI");
                    operation.GetAwaiter().GetResult();
                    IDisposable lease;
                    bool acquired = WindowsOperationQueue.TryEnter(out lease);
                    if (lease != null) lease.Dispose();
                    Check(!acquired, "Another module acquired the queue while the installer was running.");
                }
                finally { exit.Set(); child.WaitForExit(5000); }
                Wait(() =>
                {
                    IDisposable lease;
                    if (!WindowsOperationQueue.TryEnter(out lease)) return false;
                    lease.Dispose();
                    return true;
                }, "queue freed on installer exit");
            }
        }

        private static void VerifyRetargeting(string root, string executing)
        {
            string links = Directory.CreateDirectory(Path.Combine(root, "Links")).FullName;
            string oldTarget = Path.Combine(root, "Bars.Automation.Manager.2.3.97.exe");
            string next = Path.Combine(root, "Bars.Automation.Manager.2.3.98.exe");
            File.Copy(executing, oldTarget);
            File.Copy(executing, next);
            string shortcut = Path.Combine(links, "Manager.lnk");
            string another = Path.Combine(links, "Other.lnk");
            string differentFolder = Directory.CreateDirectory(Path.Combine(root, "OtherCopy")).FullName;
            string unrelatedTarget = Path.Combine(differentFolder, Path.GetFileName(oldTarget));
            File.Copy(executing, unrelatedTarget);
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            try
            {
                using (var original = new TestShortcut(shell, shortcut))
                {
                    original.Set("TargetPath", oldTarget);
                    original.Set("Arguments", "--fixture");
                    original.Set("IconLocation", oldTarget + ",0");
                    original.Save();
                }
                using (var unrelated = new TestShortcut(shell, another))
                {
                    unrelated.Set("TargetPath", unrelatedTarget);
                    unrelated.Save();
                }
                File.Delete(oldTarget);
                string warning = ManagerUpdates.RetargetShortcuts(oldTarget, next, new[] { links });
                Check(warning == null, warning);
                using (var updated = new TestShortcut(shell, shortcut))
                {
                    Check(String.Equals(updated.Get("TargetPath"), next, StringComparison.OrdinalIgnoreCase), "Matching shortcut was not updated.");
                    Check(updated.Get("Arguments") == "--fixture" && updated.Get("IconLocation").StartsWith(next, StringComparison.OrdinalIgnoreCase),
                        "Shortcut arguments or icon failed to transfer.");
                }
                using (var unrelated = new TestShortcut(shell, another))
                    Check(String.Equals(unrelated.Get("TargetPath"), unrelatedTarget, StringComparison.OrdinalIgnoreCase), "Unrelated shortcut was modified.");
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }

        private sealed class TestShortcut : IDisposable
        {
            private readonly object link;
            internal TestShortcut(object shell, string path)
            {
                link = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
            }
            internal void Set(string property, string value)
            { link.GetType().InvokeMember(property, System.Reflection.BindingFlags.SetProperty, null, link, new object[] { value }); }
            internal string Get(string property)
            { return Convert.ToString(link.GetType().InvokeMember(property, System.Reflection.BindingFlags.GetProperty, null, link, null)); }
            internal void Save()
            { link.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null); }
            public void Dispose() { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link); }
        }

        private static bool InstallRejected(string installer, string destination)
        {
            try { BarsReleaseCatalog.VerifyInstalledProgram(installer, destination); return false; }
            catch (IOException) { return true; }
        }

        private static bool Rejected(Action action)
        {
            try { action(); return false; }
            catch (InvalidDataException) { return true; }
        }

        private static System.Collections.Generic.IEnumerable<Control> Find(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var nested in Find(child)) yield return nested;
            }
        }
    }
}