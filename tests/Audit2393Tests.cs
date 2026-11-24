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
                        Wait(() => table.Rows.Count == 2, "services");
                        var rows = table.Rows.Cast<DataGridViewRow>().ToDictionary(row => (string)row.Cells[1].Value);
                        Check((string)rows["BrokenService.zip"].Cells[4].Value == "Архив повреждён", "Corrupt archive marked ready.");
                        Check((string)rows["WorkingService.zip"].Cells[4].Value == "Готов к работе", "Valid archive did not pass.");
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
                    Wait(() => table.Rows.Count == 1, "merged programs");
                    Check(((string)table.Rows[0].Cells[4].Value).Contains("меньше 2 МБ"), "Tiny local EXE returned as ready after merging.");
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