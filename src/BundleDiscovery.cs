using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ServiceTool
{
    [Flags]
    internal enum BundleSection
    {
        Any = 0, Libraries = 1, Software = 2, Tools = 4, Wallpapers = 8,
        Framework = 16, Sql = 32, BarsPrograms = 64, BarsServices = 128
    }

    internal sealed class BundleCandidate
    {
        internal string Root;
        internal BundleSection Sections;

        internal string Description
        {
            get { return String.Join(", ", BundleDiscovery.Sections.Where(section => (Sections & section.Kind) != 0).Select(section => section.Label)); }
        }
    }

    internal static class BundleDiscovery
    {
        internal sealed class Marker
        {
            internal BundleSection Kind;
            internal string Path, Label;
            internal int Depth;
            internal Func<string,bool> Matches;

            internal Marker(BundleSection kind, string path, string label, int depth, Func<string,bool> matches)
            { Kind = kind; Path = path; Label = label; Depth = depth; Matches = matches; }
        }

        internal static readonly Marker[] Sections = {
            new Marker(BundleSection.Libraries, "_libraries", "Библиотеки", 2, path => HasExtension(path, ".exe", ".msi", ".7z", ".msu", ".cab")),
            new Marker(BundleSection.Software, "_soft", "Прикладные программы", 0, path => HasExtension(path, ".exe", ".msi")),
            new Marker(BundleSection.Tools, "_tools", "Утилиты и прошивки", 2, path => ToolArchives.Supports(path) || HasExtension(path, ".exe", ".cmd", ".bat", ".msi", ".bin", ".hex")),
            new Marker(BundleSection.Wallpapers, "_wallpers", "Обои", 1, path => HasExtension(path, ".png", ".jpg", ".jpeg", ".bmp")),
            new Marker(BundleSection.Framework, "_barsmanager", ".NET Framework", 0, path => Path.GetFileName(path).Equals("NDP48-x86-x64-AllOS-ENU.exe", StringComparison.OrdinalIgnoreCase)),
            new Marker(BundleSection.Sql, @"_soft\_SQL", "SQL Server и SSMS", 2, path => HasExtension(path, ".exe", ".msi", ".iso", ".zip")),
            new Marker(BundleSection.BarsPrograms, @"_soft\_Bars\_Soft", "Программы Барс", 2, path => BarsReleaseCatalog.ValidName(Path.GetFileName(path), false) && new FileInfo(path).Length >= BarsReleaseCatalog.MinimumProgramBytes),
            new Marker(BundleSection.BarsServices, @"_soft\_Bars\_Services", "Службы и агенты Барс", 2, path => BarsReleaseCatalog.ValidName(Path.GetFileName(path), true))
        };

        private static readonly string[] Landmarks = { "_libraries", "_soft", "_tools", "_wallpers", "_barsmanager" };

        private static bool HasExtension(string path, params string[] extensions)
        {
            return extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsOrdinaryDirectory(string path)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static IEnumerable<string> Entries(string path, bool directories, int limit, CancellationToken token)
        {
            try
            {
                return (directories ? Directory.EnumerateDirectories(path) : Directory.EnumerateFiles(path)).Take(limit).Select(entry => { token.ThrowIfCancellationRequested(); return entry; }).ToArray();
            }
            catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
        }

        private static bool HasPayload(string path, Marker marker, int depth, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!IsOrdinaryDirectory(path)) return false;
            foreach (string file in Entries(path, false, 4096, token))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0 && new FileInfo(file).Length > 0 && marker.Matches(file)) return true;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (depth == 0) return false;
            foreach (string child in Entries(path, true, 1024, token))
            {
                if (Path.GetFileName(child).Equals("_custom", StringComparison.OrdinalIgnoreCase)) continue;
                if (HasPayload(child, marker, depth - 1, token)) return true;
            }
            return false;
        }

        private static bool HasSection(string root, Marker marker, CancellationToken token)
        {
            string path = root;
            foreach (string part in marker.Path.Split(Path.DirectorySeparatorChar))
            {
                path = Path.Combine(path, part);
                if (!IsOrdinaryDirectory(path)) return false;
            }
            return HasPayload(path, marker, marker.Depth, token);
        }

        private static bool TryCandidate(string root, BundleSection section, CancellationToken token, out BundleCandidate result)
        {
            result = null;
            if (!IsOrdinaryDirectory(root)) return false;
            if (section == BundleSection.Any && Landmarks.Count(name => IsOrdinaryDirectory(Path.Combine(root, name))) < 2)
                return false;
            if (section != BundleSection.Any)
            {
                Marker required = Sections.First(marker => marker.Kind == section);
                if (!HasSection(root, required, token)) return false;
            }

            BundleSection found = BundleSection.Any;
            foreach (Marker marker in Sections)
                if (marker.Kind == section || HasSection(root, marker, token))
                    found |= marker.Kind;
            if (found == BundleSection.Any) return false;
            result = new BundleCandidate { Root = root, Sections = found };
            return true;
        }

        internal static string SectionFolder(string root, BundleSection section)
        {
            return Path.Combine(root, Sections.First(marker => marker.Kind == section).Path);
        }

        internal static IEnumerable<string> LocalDrives()
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                string path = null;
                try
                {
                    if ((drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable) && drive.IsReady)
                        path = drive.RootDirectory.FullName;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                if (path != null) yield return path;
            }
        }

        internal static List<BundleCandidate> Find(BundleSection section, string remembered, string executable,
            string standard, IEnumerable<string> drives, CancellationToken token)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var found = new List<BundleCandidate>();
            Action<string> visit = root =>
            {
                token.ThrowIfCancellationRequested();
                if (String.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root)) return;
                string full;
                try { full = Path.GetFullPath(root); }
                catch (ArgumentException) { return; }
                catch (IOException) { return; }
                if (full.Length > Path.GetPathRoot(full).Length) full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                BundleCandidate candidate;
                if (seen.Add(full) && TryCandidate(full, section, token, out candidate)) found.Add(candidate);
            };

            visit(remembered);
            if (found.Count != 0) return found;
            string directory = String.IsNullOrWhiteSpace(executable) ? null : Path.GetFullPath(executable);
            if (directory != null && directory.Length > Path.GetPathRoot(directory).Length)
                directory = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string parent = directory == null ? null : Path.GetDirectoryName(directory);
            visit(executable);
            visit(parent);
            visit(standard);
            foreach (string root in new[] { executable, parent }.Concat(drives ?? new string[0]).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                if (String.IsNullOrWhiteSpace(root) || !IsOrdinaryDirectory(root)) continue;
                visit(root);
                visit(Path.Combine(root, "Менеджер автоматизации Барс"));
                foreach (string child in Entries(root, true, 4096, token)) visit(child);
            }
            return found;
        }
    }
}
