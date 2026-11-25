using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace ServiceTool
{
    internal static class BarsPackagePaths
    {
        internal static string Branch(bool services)
        {
            return services ? "/_soft/_Bars/_Services" : "/_soft/_Bars/_Soft";
        }

        internal static bool Include(string path)
        {
            return !path.Split('/').Any(part => part.Equals("_custom", StringComparison.OrdinalIgnoreCase));
        }

        internal static string Relative(string path, bool services)
        {
            string prefix = Branch(services) + "/";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Include(path))
                throw new InvalidDataException("Файл вне каталога Барс.");
            return path.Substring(prefix.Length);
        }

        internal static IEnumerable<string> LocalFiles(string root, bool services, CancellationToken token)
        {
            if (!Directory.Exists(root)) yield break;
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            ToolsDeployment.SafePath(root);
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string directory = pending.Pop();
                foreach (string child in Directory.EnumerateDirectories(directory))
                {
                    token.ThrowIfCancellationRequested();
                    if (Path.GetFileName(child).Equals("_custom", StringComparison.OrdinalIgnoreCase) ||
                        (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                    pending.Push(child);
                }
                foreach (string path in Directory.EnumerateFiles(directory))
                {
                    token.ThrowIfCancellationRequested();
                    if (BarsReleaseCatalog.ValidName(Path.GetFileName(path), services) &&
                        (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                        yield return path.Substring(root.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
                }
            }
        }

        internal static Version VersionOf(string text)
        {
            var matches = Regex.Matches(text ?? "", @"(?<![\d.])\d+(?:\.\d+){1,3}(?![\d.])");
            Version version;
            return matches.Count > 0 && Version.TryParse(matches[matches.Count - 1].Value, out version) ? version : null;
        }

        internal static Version PackageVersion(string name, string catalogVersion)
        {
            var fromName = VersionOf(Path.GetFileNameWithoutExtension(name));
            return fromName != null && fromName.Revision >= 0 ? fromName : VersionOf(catalogVersion) ?? fromName;
        }

        internal static string Official(string name, string catalogVersion, bool services)
        {
            Version version = PackageVersion(name, catalogVersion);
            if (version == null || version.Revision < 0 || (services &&
                (name.IndexOf("build", StringComparison.OrdinalIgnoreCase) >= 0 || name.StartsWith("HttpService", StringComparison.OrdinalIgnoreCase))))
                return name;
            string release = version.Revision + "." + version.Build;
            return services ? "_bin/" + release + "/" + name :
                version.Major + "." + version.Minor + ".xx." + version.Revision + "/" + release + "/" + name;
        }
    }
}
