using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace ServiceTool
{
    internal static partial class LibraryDevExpress
    {
        internal const string FileName = "12.devexpress.7z";
        internal const string Hash = "C8654B2D336E4CA1FCF6F75853BB07A562239F09B14472D97CD6F4BDF158BBBC";
        internal const string Removing = "Удаляем регистрацию библиотек...";
        private const string Installing = "Устанавливаем библиотеки...";
        private const string RegistryPath = @"SOFTWARE\BARS\Manager\DevExpress";
        private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string InstallRoot = @"C:\Program Files (x86)\DataKrat";
        private static readonly StringComparer IdentityComparer = StringComparer.OrdinalIgnoreCase;

        [ComImport, Guid("e707dcde-d1cd-11d2-bab9-00c04f8eceae"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAssemblyCache
        {
            [PreserveSig] int UninstallAssembly(int flags, [MarshalAs(UnmanagedType.LPWStr)] string identity, IntPtr reference, out int disposition);
            [PreserveSig] int QueryAssemblyInfo(int flags, [MarshalAs(UnmanagedType.LPWStr)] string identity, IntPtr info);
            [PreserveSig] int CreateAssemblyCacheItem(int flags, IntPtr reserved, out IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string identity);
            [PreserveSig] int CreateAssemblyScavenger(out IntPtr scavenger);
            [PreserveSig] int InstallAssembly(int flags, [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr reference);
        }

        [DllImport("fusion.dll")]
        private static extern int CreateAssemblyCache(out IAssemblyCache cache, int reserved);

        internal static bool IsPackage(LibraryPackage package)
        {
            return package.Kind == LibraryKind.Inno &&
                   (package.Version == "15.1.4" || package.Version == "22.1.4") &&
                   package.Id == LibraryDiscovery.Id(package) &&
                   String.Equals(package.RelativePath, FileName, StringComparison.OrdinalIgnoreCase) &&
                   String.Equals(package.Sha256, Hash, StringComparison.OrdinalIgnoreCase);
        }

        private static RegistryKey Machine()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
        }

        private sealed class Receipt
        {
            internal string[] Required;
            internal HashSet<string> Owned;
            internal bool Complete;
        }

        private static Receipt Read(RegistryKey key, string version)
        {
            var data = key == null ? null : key.GetValue(version) as string[];
            if (data == null) return null;
            if (data.Length < 2 || (data[0] != "pending" && data[0] != "installed"))
                throw new InvalidDataException("Не удалось прочитать запись установки DevExpress " + version);
            return new Receipt
            {
                Complete = data[0] == "installed",
                Required = data.Skip(1).Select(line => line.Substring(1)).ToArray(),
                Owned = new HashSet<string>(data.Skip(1).Where(line => line[0] == '+').Select(line => line.Substring(1)), IdentityComparer)
            };
        }

        private static void Save(RegistryKey key, string version, Receipt receipt)
        {
            var data = new[] { receipt.Complete ? "installed" : "pending" }
                .Concat(receipt.Required.Select(identity => (receipt.Owned.Contains(identity) ? "+" : "-") + identity)).ToArray();
            key.SetValue(version, data, RegistryValueKind.MultiString);
            key.Flush();
        }

        private static string Identity(AssemblyName name)
        {
            return name.FullName + ", ProcessorArchitecture=" + name.ProcessorArchitecture;
        }

        private static string RegisteredPath(string identity)
        {
            var name = new AssemblyName(identity);
            string area = name.ProcessorArchitecture == ProcessorArchitecture.MSIL ? "GAC_MSIL" :
                name.ProcessorArchitecture == ProcessorArchitecture.Amd64 ? "GAC_64" : "GAC_32";
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string token = BitConverter.ToString(name.GetPublicKeyToken()).Replace("-", "").ToLowerInvariant();
            string culture = name.CultureInfo == null ? "" : name.CultureInfo.Name;
            string entry = name.Version + "_" + culture + "_" + token;
            var modern = Path.Combine(windows, "Microsoft.NET", "assembly", area, name.Name, "v4.0_" + entry, name.Name + ".dll");
            var legacy = Path.Combine(windows, "assembly", area, name.Name, entry, name.Name + ".dll");
            return File.Exists(modern) ? modern : File.Exists(legacy) ? legacy : null;
        }

        private static bool Registered(string identity) { return RegisteredPath(identity) != null; }

        internal static LibraryCheck Check(LibraryPackage package)
        {
            if (!IsPackage(package)) return null;
            using (var machine = Machine())
            using (var key = machine.OpenSubKey(RegistryPath))
            {
                var receipt = Read(key, package.Version);
                if (receipt == null) return null;
                if (receipt.Complete && receipt.Required.All(Registered))
                    return LibraryVersions.Known(package, package.Version, "Регистрация библиотек подтверждена");
                return new LibraryCheck { Package = package, State = LibraryState.Missing, HasProduct = true,
                    Detail = "Установка прервана или часть библиотек отсутствует. Можно восстановить установку или удалить её." };
            }
        }

        private static string Other(string version) { return version == "15.1.4" ? "22.1.4" : "15.1.4"; }

        private sealed class Legacy
        {
            internal RegistryView View;
            internal string Directory;
            internal bool MissingUninstaller;
        }

        private static Legacy LegacyRegistration(LibraryPackage package)
        {
            Legacy found = null;
            foreach (var view in LibraryDetection.Views())
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            using (var key = machine.OpenSubKey(UninstallPath + "\\" + package.UninstallKey))
            {
                if (key == null || LibraryVersions.Compare(Convert.ToString(key.GetValue("DisplayVersion")), package.Version) != 0) continue;
                string directory = Convert.ToString(key.GetValue("InstallLocation")).TrimEnd('\\');
                if (!Path.IsPathRooted(directory) || !IdentityComparer.Equals(Path.GetFileName(directory), package.Name)) continue;
                string expected = Path.Combine(directory, "unins000.exe");
                if (!IdentityComparer.Equals(Convert.ToString(key.GetValue("UninstallString")).Trim().Trim('"'), expected)) continue;
                bool missing = false;
                try { File.GetAttributes(expected); }
                catch (FileNotFoundException) { missing = true; }
                catch (DirectoryNotFoundException) { missing = true; }
                if (found != null) throw new InvalidOperationException("Найдено несколько записей установки " + package.Name + ".");
                found = new Legacy { View = view, Directory = directory, MissingUninstaller = missing };
            }
            return found;
        }

        internal static bool CanRemoveWithoutInstaller(LibraryPackage package)
        {
            if (!IsPackage(package)) return false;
            var legacy = LegacyRegistration(package);
            return legacy == null ? Directory.Exists(Path.Combine(InstallRoot, package.Name)) : legacy.MissingUninstaller;
        }

        internal static LibraryCheck CheckRemainingFolder(LibraryPackage package)
        {
            if (!CanRemoveWithoutInstaller(package) || !Directory.Exists(Path.Combine(InstallRoot, package.Name))) return null;
            return new LibraryCheck { Package = package, State = LibraryState.Missing, HasProduct = true,
                Detail = "Папка библиотек найдена. Можно установить библиотеки или удалить папку выбранной версии." };
        }

        private static void RemoveLegacyRegistration(LibraryPackage package, Legacy legacy)
        {
            if (legacy == null) return;
            var current = LegacyRegistration(package);
            if (current == null) return;
            if (!current.MissingUninstaller || current.View != legacy.View || !IdentityComparer.Equals(current.Directory, legacy.Directory))
                throw new IOException("Запись старой установки изменилась. Повтори проверку.");
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, legacy.View))
            using (var uninstall = machine.OpenSubKey(UninstallPath, true))
                uninstall.DeleteSubKeyTree(package.UninstallKey, false);
        }

        internal static LibraryInstallResult Install(LibraryPackage package, string verified, Action<string> progress)
        {
            var legacy = LegacyRegistration(package);
            if (legacy != null && !legacy.MissingUninstaller) legacy = null;
            string extracted;
            var requested = LibraryCancellation.Current;
            using (var cancel = new CancellationTokenSource())
            using (var done = new ManualResetEvent(false))
            {
                var watcher = new Timer(_ => { if (requested != null && requested()) cancel.Cancel(); }, null, 0, 100);
                try
                {
                    progress(Installing);
                    extracted = ToolArchives.Extract(verified, cancel.Token,
                        p => { if (p.CleanupWarning) progress(p.Stage); }, false, "payload", package.Name);
                }
                finally { watcher.Dispose(done); done.WaitOne(); }
            }
            try
            {
                string folder = Path.Combine(extracted, package.Name);
                var files = new Dictionary<string, string>(IdentityComparer);
                foreach (string relative in package.Version == "15.1.4" ? Files15 : Files22)
                {
                    string path = Path.Combine(folder, relative);
                    files.Add(Identity(AssemblyName.GetAssemblyName(path)), path);
                }
                using (var machine = Machine())
                using (var key = machine.CreateSubKey(RegistryPath))
                {
                    var other = Read(key, Other(package.Version));
                    var previous = Read(key, package.Version);
                    var receipt = new Receipt { Required = files.Keys.ToArray(), Owned = new HashSet<string>(IdentityComparer) };
                    foreach (string id in files.Keys)
                        if ((previous != null && previous.Owned.Contains(id)) || (other != null && other.Owned.Contains(id)) ||
                            !Registered(id) || legacy != null && SameLegacyFile(legacy, folder, files[id]))
                            receipt.Owned.Add(id);
                    Save(key, package.Version, receipt);
                    PublishFiles(package, folder);
                    IAssemblyCache cache;
                    Marshal.ThrowExceptionForHR(CreateAssemblyCache(out cache, 0));
                    try
                    {
                        foreach (var file in files)
                        {
                            LibraryCancellation.Check();
                            if (!Registered(file.Key)) Marshal.ThrowExceptionForHR(cache.InstallAssembly(0, file.Value, IntPtr.Zero));
                        }
                        if (!receipt.Required.All(Registered)) throw new IOException("Часть сборок DevExpress не зарегистрировалась.");
                        receipt.Complete = true;
                        Save(key, package.Version, receipt);
                        RemoveLegacyRegistration(package, legacy);
                    }
                    finally { Marshal.ReleaseComObject(cache); }
                }
                return Result(LibraryState.Installed, "Установка подтверждена повторной проверкой");
            }
            finally { PackageZipExtraction.CleanupFolder(extracted, "Не удалось удалить временную папку: ", p => progress(p.Stage)); }
        }

        private static bool SameLegacyFile(Legacy legacy, string folder, string archiveFile)
        {
            string file = Path.Combine(legacy.Directory, archiveFile.Substring(folder.Length + 1));
            if (!File.Exists(file)) return false;
            return SameFile(file, archiveFile);
        }

        private static bool SameFile(string first, string second)
        {
            using (var a = File.OpenRead(first))
            using (var b = File.OpenRead(second))
                return a.Length == b.Length && LibraryCancellation.Hash(a) == LibraryCancellation.Hash(b);
        }

        private static string SafePath(string root, string relative)
        {
            string full = Path.GetFullPath(Path.Combine(root, relative));
            if (!full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Недопустимый путь к файлу DevExpress.");
            for (string component = full; component != null; component = Path.GetDirectoryName(component))
            {
                try
                {
                    if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Путь DevExpress содержит ссылку: " + component);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            return full;
        }

        private static void PublishFiles(LibraryPackage package, string folder)
        {
            string target = Path.Combine(InstallRoot, package.Name);
            var copies = new Dictionary<string, string>(IdentityComparer);
            foreach (string source in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                LibraryCancellation.Check();
                string relative = source.Substring(folder.Length + 1);
                string name = Path.GetFileName(source);
                if (IdentityComparer.Equals(name, "gacutil.exe") || IdentityComparer.Equals(name, "gacutil.exe.config") ||
                    IdentityComparer.Equals(name, "gacutlrc.dll")) continue;
                string path = SafePath(target, relative);
                if (File.Exists(path))
                {
                    if (!SameFile(path, source)) throw new IOException("Файл уже существует и отличается от комплекта: " + path);
                    continue;
                }
                copies.Add(path, source);
            }
            foreach (var copy in copies)
            {
                LibraryCancellation.Check();
                Directory.CreateDirectory(Path.GetDirectoryName(copy.Key));
                string temporary = copy.Key + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.Copy(copy.Value, temporary); File.Move(temporary, copy.Key); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }

        internal static void RemoveInstalledFolder(LibraryPackage package)
        {
            if (!IsPackage(package)) throw new InvalidOperationException("Неизвестная версия DevExpress.");
            DeleteVersionFolder(InstallRoot, package.Version);
        }

        internal static void DeleteVersionFolder(string parent, string version)
        {
            if (version != "15.1.4" && version != "22.1.4") throw new ArgumentException("Неизвестная версия DevExpress.");
            string folder = SafePath(parent, "DevExpress " + version);
            if (Directory.Exists(folder)) DeleteTree(folder);
        }

        private static void DeleteTree(string folder)
        {
            bool link = (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0;
            if (!link)
                foreach (string path in Directory.EnumerateFileSystemEntries(folder))
                {
                    LibraryCancellation.Check();
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.Directory) != 0) DeleteTree(path);
                    else
                    {
                        if ((attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) == FileAttributes.ReadOnly)
                            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                        File.Delete(path);
                    }
                }
            Directory.Delete(folder, false);
        }

        private static Receipt ReadLegacyFiles(LibraryPackage package, string folder)
        {
            var identities = new HashSet<string>(IdentityComparer);
            foreach (string relative in package.Version == "15.1.4" ? Files15 : Files22)
            {
                LibraryCancellation.Check();
                string file = SafePath(folder, relative);
                if (!File.Exists(file)) continue;
                AssemblyName assembly;
                try { assembly = AssemblyName.GetAssemblyName(file); }
                catch (BadImageFormatException) { continue; }
                if (!IdentityComparer.Equals(assembly.Name, Path.GetFileNameWithoutExtension(file))) continue;
                var token = BitConverter.ToString(assembly.GetPublicKeyToken() ?? new byte[0]).Replace("-", "").ToLowerInvariant();
                if (token != "b88d1754d700e49a" && token != "31bf3856ad364e35" && token != "4c3bc44151be4b47") continue;
                string identity = Identity(assembly), installed = RegisteredPath(identity);
                if (installed != null && SameFile(file, installed)) identities.Add(identity);
            }
            return new Receipt { Required = identities.ToArray(), Owned = identities };
        }

        internal static LibraryInstallResult Remove(LibraryPackage package, Action<string> progress)
        {
            var legacy = LegacyRegistration(package);
            using (var machine = Machine())
            using (var key = machine.CreateSubKey(RegistryPath))
            {
                var receipt = Read(key, package.Version);
                if (receipt == null)
                {
                    if (!CanRemoveWithoutInstaller(package)) return null;
                    receipt = ReadLegacyFiles(package, legacy == null ? Path.Combine(InstallRoot, package.Name) : legacy.Directory);
                }
                if (legacy != null && !legacy.MissingUninstaller)
                    throw new IOException("Старый деинсталлятор снова доступен. Требуется повторная проверка.");
                var other = Read(key, Other(package.Version));
                var keep = new HashSet<string>(other == null ? new string[0] : other.Required, IdentityComparer);
                var otherPackage = LibraryCatalog.All().First(p => IsPackage(p) && p.Version == Other(package.Version));
                if (LegacyRegistration(otherPackage) != null || Directory.Exists(Path.Combine(InstallRoot, otherPackage.Name)))
                    keep.UnionWith(receipt.Required.Where(id => new AssemblyName(id).Name == "System.Windows.Interactivity"));
                progress(Removing);
                IAssemblyCache cache;
                Marshal.ThrowExceptionForHR(CreateAssemblyCache(out cache, 0));
                try
                {
                    foreach (string id in receipt.Owned)
                    {
                        LibraryCancellation.Check();
                        if (keep.Contains(id) || !Registered(id)) continue;
                        int disposition;
                        Marshal.ThrowExceptionForHR(cache.UninstallAssembly(0, id.Replace(", ", ","), IntPtr.Zero, out disposition));
                        if (Registered(id))
                            throw new IOException("Не удалось снять регистрацию сборки " + new AssemblyName(id).Name);
                    }
                    if (other != null)
                    {
                        other.Owned.UnionWith(receipt.Owned.Where(keep.Contains));
                        Save(key, Other(package.Version), other);
                    }
                    RemoveInstalledFolder(package);
                    RemoveLegacyRegistration(package, legacy);
                    key.DeleteValue(package.Version + ".files", false);
                    key.DeleteValue(package.Version, false);
                    key.Flush();
                }
                finally { Marshal.ReleaseComObject(cache); }
                return Result(LibraryState.Missing, "Регистрация библиотек снята");
            }
        }

        private static LibraryInstallResult Result(LibraryState state, string message)
        { return new LibraryInstallResult { ExitCode = 0, State = state, Message = message, LogPath = "" }; }
    }
}
