using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;

namespace ServiceTool
{
    // Shared package identity: use the same metadata rules as the installers.
    internal sealed class BundlePackageIdentity
    {
        internal string Key, Version;
        internal static BundlePackageIdentity Read(string path,string relative)
        {
            if(relative.StartsWith("/_tools/",StringComparison.OrdinalIgnoreCase)||BundleRemoteFile.IsBarsPackage(relative))return null;
            if (relative.StartsWith("/_libraries/", StringComparison.OrdinalIgnoreCase) && VcArchivePackage.Matches(relative))
            {
                var archive = VcArchivePackage.Describe(relative);
                var profileId = archive.Id + "|x86";
                return new BundlePackageIdentity { Key = "library|" + profileId + "|" + archive.Architecture, Version = archive.Version };
            }
            string ext=Path.GetExtension(relative);
            if(!ext.Equals(".exe",StringComparison.OrdinalIgnoreCase)&&!ext.Equals(".msi",StringComparison.OrdinalIgnoreCase))return null;
            // Staging names end in .part: MSI metadata still needs its real extension.
            string metadata=path, alias=null, copy=null;
            try
            {
                if(!Path.GetFileName(path).Equals(Path.GetFileName(relative),StringComparison.OrdinalIgnoreCase))
                {
                    alias = Path.Combine(Path.GetTempPath(), "bmi-" + Guid.NewGuid().ToString("N").Substring(0, 12));
                    Directory.CreateDirectory(alias);
                    copy = Path.Combine(alias, Path.GetFileName(relative));
                    File.Copy(path, copy, false);
                    metadata = copy;
                }
                string id=SoftwareCatalog.PackageId(relative);
                if(!String.IsNullOrEmpty(id))
                {
                        var p=SoftwareCatalog.Describe(id,metadata,"Комплект");
                        return new BundlePackageIdentity {Key="software|"+id+"|"+p.Architecture,Version=p.Version};
                }
                var library=LibraryDiscovery.Describe(metadata,relative);
                if(library!=null&&(library.Kind==LibraryKind.Access||library.Kind==LibraryKind.Framework||library.Kind==LibraryKind.Inno||library.Kind==LibraryKind.VcBundle))
                {
                    string arch=ExecutableArchitecture.Read(metadata);library.Id+="|"+arch;
                }
                if(library==null||LibraryVersions.Number(library.Version)==null)return null;
                return new BundlePackageIdentity {Key="library|"+library.Id+"|"+library.Architecture,Version=library.Version};
            }
            catch(Exception ex){if(ex is IOException||ex is ArgumentException||ex is System.Runtime.InteropServices.COMException||ex is UnauthorizedAccessException)return null;throw;}
            finally
            {
                if (alias != null)
                    try
                    {
                        if (copy != null) File.Delete(copy);
                        if (Directory.Exists(alias)) Directory.Delete(alias);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
            }
        }
        internal static void ValidateUpgrade(string oldFile,string newFile,string relative)
        {
            var before=Read(oldFile,relative);var after=Read(newFile,relative);
            if(before==null||after==null||before.Key!=after.Key||!LibraryVersions.SameChannel(before.Version,after.Version)||LibraryVersions.Compare(after.Version,before.Version)<0)
                throw new IOException("Продукт, архитектура, ветка или версия обновления не подтверждены; локальный файл сохранён");
        }
    }
    internal static class BundlePreferences
    {
        static string FileName {get{return ApplicationStorage.FilePath("bundle-root.txt");}}
        private static readonly System.Collections.Generic.Dictionary<BundleSection,string> discovered =
            new System.Collections.Generic.Dictionary<BundleSection,string>();

        internal static string RememberedRoot()
        {
            try
            {
                if(File.Exists(FileName))
                {
                    string value=File.ReadAllText(FileName).Trim();
                    if(Path.IsPathRooted(value))
                    {
                        string trimmed=value.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
                        string root=BundleFolders.BundleRoot(trimmed.Length>Path.GetPathRoot(value).Length?trimmed:value);
                        if(!String.Equals(Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)),
                            "Библиотеки", StringComparison.OrdinalIgnoreCase)) return root;
                    }
                }
            }
            catch(IOException){}
            catch(UnauthorizedAccessException){}
            catch(ArgumentException){}
            return null;
        }

        internal static string DiscoveredRoot(BundleSection section)
        {
            string value;
            lock (discovered) return discovered.TryGetValue(section, out value) && Directory.Exists(value) ? value : null;
        }

        internal static void UseDiscoveredRoot(BundleSection section, string root)
        {
            lock (discovered)
            {
                if (root == null) discovered.Remove(section);
                else discovered[section] = root;
            }
        }

        internal static string Load() { return Load(BundleSection.Any); }
        internal static string Load(BundleSection section)
        {
            return DiscoveredRoot(section) ?? RememberedRoot() ?? BundleFolders.DefaultRoot;
        }

        internal static void Save(string root)
        {
            Directory.CreateDirectory(ApplicationStorage.DirectoryPath);
            File.WriteAllText(FileName,Path.GetFullPath(root));
            lock (discovered) discovered.Clear();
        }
    }

    internal sealed class BundleOldFile
    {internal string Path,Hash;}
    internal static class BundleMaintenance
    {
        internal static System.Collections.Generic.IEnumerable<string> PackageFiles(string root,CancellationToken token)
        {
            string full=Path.GetFullPath(root);YandexBundleSource.LocalPath(full,"/containment-check");
            if(!Directory.Exists(full))yield break;
            var pending=new System.Collections.Generic.Stack<string>();pending.Push(full);int count=0;
            while(pending.Count>0)
            {
                token.ThrowIfCancellationRequested();string dir=pending.Pop();
                foreach(string child in Directory.GetDirectories(dir))
                {
                    string name=Path.GetFileName(child);
                    if(name.Equals(".bars-download",StringComparison.OrdinalIgnoreCase)||name.Equals("_tools",StringComparison.OrdinalIgnoreCase)||name.Equals("Журнал установки библиотек",StringComparison.OrdinalIgnoreCase)||(File.GetAttributes(child)&FileAttributes.ReparsePoint)!=0)continue;
                    pending.Push(child);
                }
                foreach(string path in Directory.GetFiles(dir))
                {
                    token.ThrowIfCancellationRequested();if(++count>20000)throw new IOException("Превышен предел локального каталога");
                    if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)continue;
                    string ext=Path.GetExtension(path);if(ext.Equals(".exe",StringComparison.OrdinalIgnoreCase)||ext.Equals(".msi",StringComparison.OrdinalIgnoreCase))yield return path;
                }
            }
        }
        internal sealed class IdentityCache
        {
            private sealed class Entry
            {
                internal long Length;
                internal DateTime Written, Created;
                internal BundlePackageIdentity Identity;
            }

            private readonly System.Collections.Generic.Dictionary<string, Entry> entries =
                new System.Collections.Generic.Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

            // The publication gate owns this instance while local candidates are checked.
            internal BundlePackageIdentity Read(string path, string relative)
            {
                var file = new FileInfo(path);
                long length = file.Length;
                var written = file.LastWriteTimeUtc;
                var created = file.CreationTimeUtc;
                Entry entry;
                if (entries.TryGetValue(path, out entry) && entry.Length == length &&
                    entry.Written == written && entry.Created == created) return entry.Identity;
                var identity = BundlePackageIdentity.Read(path, relative);
                if (identity == null) entries.Remove(path);
                else entries[path] = new Entry { Length = length, Written = written, Created = created, Identity = identity };
                return identity;
            }
        }

        internal static void ValidateAgainstLocal(string root,string staged,BundleRemoteFile file,CancellationToken token, IdentityCache identities=null)
        {
            if(file.Group=="Утилиты")return;
            var incoming=BundlePackageIdentity.Read(staged,file.Path);if(incoming==null)return;
            foreach(string local in PackageFiles(root,token))
            {
                token.ThrowIfCancellationRequested();if((File.GetAttributes(local)&FileAttributes.ReparsePoint)!=0)continue;
                string relative="/"+local.Substring(Path.GetFullPath(root).TrimEnd('\\','/').Length).TrimStart('\\','/').Replace('\\','/');
                var existing=identities == null ? BundlePackageIdentity.Read(local,relative) : identities.Read(local,relative);
                if(existing!=null&&existing.Key==incoming.Key&&LibraryVersions.SameChannel(existing.Version,incoming.Version)&&LibraryVersions.Compare(existing.Version,incoming.Version)>0)
                    throw new IOException("В комплекте уже есть более новая версия продукта; понижение заблокировано: "+relative);
            }
        }
        internal static System.Collections.Generic.List<BundleOldFile> OldFiles(string root,System.Collections.Generic.IEnumerable<BundleRemoteFile> source,CancellationToken token)
        {
            var current=source.ToList();var known=new System.Collections.Generic.Dictionary<string,System.Collections.Generic.List<BundlePackageIdentity>>();
            foreach(var file in current.Where(f=>f.Group!="Утилиты"))
            {
                token.ThrowIfCancellationRequested();string path=BundleFolders.PackageDestination(root,file.Path);
                if(!File.Exists(path)||YandexBundleSource.Hash(path,token)!=file.Sha256)continue;
                var identity=BundlePackageIdentity.Read(path,file.Path);if(identity==null)continue;
                System.Collections.Generic.List<BundlePackageIdentity> list;if(!known.TryGetValue(identity.Key,out list))known[identity.Key]=list=new System.Collections.Generic.List<BundlePackageIdentity>();list.Add(identity);
            }
            var result=new System.Collections.Generic.List<BundleOldFile>();
            foreach(string path in PackageFiles(root,token))
            {
                    string relative="/"+path.Substring(Path.GetFullPath(root).TrimEnd('\\','/').Length).TrimStart('\\','/').Replace('\\','/');
                    if(current.Any(f=>String.Equals(BundleFolders.DownloadPath(f.Path),relative,StringComparison.OrdinalIgnoreCase))||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)continue;
                    string ext=Path.GetExtension(path);if(!ext.Equals(".exe",StringComparison.OrdinalIgnoreCase)&&!ext.Equals(".msi",StringComparison.OrdinalIgnoreCase))continue;
                    string hash=YandexBundleSource.Hash(path,token);var remote=new BundleRemoteFile {Path=relative,Sha256=hash};
                    if(!new BundleReceipts(root).CanReplace(remote,hash))continue;
                    var identity=BundlePackageIdentity.Read(path,relative);System.Collections.Generic.List<BundlePackageIdentity> replacements;
                    if(identity!=null&&known.TryGetValue(identity.Key,out replacements)&&replacements.Any(p=>LibraryVersions.SameChannel(identity.Version,p.Version)&&LibraryVersions.Compare(p.Version,identity.Version)>0))result.Add(new BundleOldFile {Path=relative,Hash=hash});
            }
            return result;
        }
        internal static void RemoveOld(string root,BundleOldFile old)
        {
            string path=YandexBundleSource.LocalPath(root,old.Path);
            using(var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
            {
                using(var hash=System.Security.Cryptography.SHA256.Create())if(!String.Equals(BitConverter.ToString(hash.ComputeHash(held)).Replace("-",""),old.Hash,StringComparison.OrdinalIgnoreCase))throw new IOException("Старая копия изменилась; сохранена: "+old.Path);
                File.Delete(path);
            }
        }
        // Enumerate without following junctions, symlinks or unknown directories.
        internal static string[] Parts(string root)
        {
            string stage=YandexBundleSource.LocalPath(root,"/.bars-download/placeholder");stage=Path.GetDirectoryName(stage);
            if(!Directory.Exists(stage))return new string[0];
            var result=new System.Collections.Generic.List<string>();
            var ownedName = new System.Text.RegularExpressions.Regex(@"^(?:[0-9a-f]{24}\.part|[0-9a-f]{16}\.bad)$");
            result.AddRange(Directory.GetFiles(stage).Where(file => ownedName.IsMatch(Path.GetFileName(file)) &&
                (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0));
            foreach(string dir in Directory.GetDirectories(stage))
            {
                string name=Path.GetFileName(dir);
                if(name.Length!=64||name.Any(c=>!Uri.IsHexDigit(c))||(File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)continue;
                var pending=new System.Collections.Generic.Stack<string>();pending.Push(dir);
                while(pending.Count>0)
                {
                    string current=pending.Pop();
                    foreach(string child in Directory.GetDirectories(current))if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)pending.Push(child);
                    foreach(string file in Directory.GetFiles(current))if((File.GetAttributes(file)&FileAttributes.ReparsePoint)==0&&(file.EndsWith(".part",StringComparison.OrdinalIgnoreCase)||System.Text.RegularExpressions.Regex.IsMatch(file,@"\.part\.invalid-[0-9a-f]{32}$")))result.Add(file);
                }
            }
            return result.ToArray();
        }
        internal static long CleanParts(string root)
        {
            long bytes=0;foreach(string path in Parts(root)){string relative="/"+path.Substring(Path.GetFullPath(root).TrimEnd('\\','/').Length).TrimStart('\\','/').Replace('\\','/');string safe=YandexBundleSource.LocalPath(root,relative);bytes+=new FileInfo(safe).Length;File.Delete(safe);}return bytes;
        }
    }
}
