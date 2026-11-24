using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

namespace ServiceTool
{
    internal static class VcArchivePackage
    {
        internal const string Password = "Dk3651340";
        private static readonly Regex ArchivePattern = new Regex(
            @"^0\.Microsoft Visual C\+\+ 2005-20\d{2} Redistributable (?<version>\d+(?:\.\d+){1,3})\.7z$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private const long MaximumPayload = 512L * 1024 * 1024;

        internal static bool Matches(string path)
        {
            Version version;
            return Version.TryParse(ArchivePattern.Match(Path.GetFileName(path) ?? "").Groups["version"].Value, out version);
        }

        internal static LibraryPackage Describe(string relative)
        {
            var version = ArchivePattern.Match(Path.GetFileName(relative)).Groups["version"].Value;
            var package = new LibraryPackage { RelativePath = relative, Kind = LibraryKind.VcBundle,
                Name = "Visual C++ v14 (2015–2026) + Lite", Group = "Visual C++",
                Version = version, Architecture = "both", Order = 400,
                ProductCode = "", UpgradeCode = "", RuntimeFamily = "", UninstallKey = "" };
            package.Id = LibraryDiscovery.Id(package);
            return package;
        }

        internal static void ValidateContainer(string path)
        {
            using (var file = File.OpenRead(path))
            {
                if (file.Length < 32) throw new InvalidDataException("Архив Visual C++ пуст или повреждён.");
                foreach (byte magic in new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c })
                    if (file.ReadByte() != magic) throw new InvalidDataException("Файл Visual C++ не является архивом 7z.");
                using (var header = new BinaryReader(file))
                {
                    int major = header.ReadByte();
                    header.ReadByte(); // Minor format version.
                    uint startCrc = header.ReadUInt32();
                    if (major != 0 || ZipIntegrity.ReadCrc(file, 20) != startCrc)
                        throw new InvalidDataException("Начальный заголовок архива Visual C++ повреждён.");
                    file.Position = 12;
                    ulong offset = header.ReadUInt64();
                    ulong size = header.ReadUInt64();
                    uint nextCrc = header.ReadUInt32();
                    ulong available = (ulong)(file.Length - 32);
                    if (offset == 0 || offset > available || size == 0 || size > available - offset || size > 32 * 1024 * 1024)
                        throw new InvalidDataException("Каталог архива Visual C++ отсутствует или повреждён.");
                    file.Position = 32 + (long)offset;
                    int headerKind = file.ReadByte();
                    file.Position--;
                    if ((headerKind != 0x01 && headerKind != 0x17) || ZipIntegrity.ReadCrc(file, (int)size) != nextCrc)
                        throw new InvalidDataException("Каталог архива Visual C++ повреждён.");
                }
            }
        }

        internal static LibraryPackage Prepare(string archive, string staging, LibraryPackage profile,
            string password, Func<bool> cancelled)
        {
            string executableName = Path.GetFileNameWithoutExtension(archive) + ".exe";
            string executable = Path.Combine(staging, executableName);
            string extractor = Path.Combine(staging, "7za.exe");
            using (var file = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                LibrarySource.VerifyStream(file, profile, cancelled);
                if (!File.Exists(executable))
                {
                    if (!File.Exists(extractor))
                    using (var resource = typeof(VcArchivePackage).Assembly.GetManifestResourceStream("BARS.VcArchive7za"))
                    {
                        if (resource == null) throw new IOException("В сборке отсутствует распаковщик 7-Zip.");
                        using (var output = new FileStream(extractor, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            resource.CopyTo(output);
                    }
                    var start = new ProcessStartInfo(extractor, "e -so -spd -bd -bb0 -- " +
                        LibraryPlan.Quote(archive) + " " + LibraryPlan.Quote(executableName))
                    {
                        UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = staging,
                        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
                    };
                    using (var process = Process.Start(start))
                    {
                        if (process == null) throw new IOException("Не удалось запустить распаковщик Visual C++.");
                        process.ErrorDataReceived += delegate { };
                        process.BeginErrorReadLine();
                        var copy = Task.Run(() =>
                        {
                            using (var output = new FileStream(executable, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            {
                                var input = process.StandardOutput.BaseStream;
                                var buffer = new byte[65536];
                                int count;
                                while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                                {
                                    if (output.Length + count > MaximumPayload)
                                        throw new InvalidDataException("Распакованный установщик Visual C++ превышает допустимый размер.");
                                    output.Write(buffer, 0, count);
                                }
                            }
                        });
                        try
                        {
                            process.StandardInput.WriteLine(password);
                            process.StandardInput.Close();
                            var clock = Stopwatch.StartNew();
                            while (!process.WaitForExit(100))
                            {
                                LibraryCancellation.Check(cancelled);
                                if (copy.IsFaulted) copy.GetAwaiter().GetResult();
                                if (clock.Elapsed.TotalMinutes >= 5) throw new IOException("Истекло время распаковки Visual C++.");
                            }
                            LibraryCancellation.Check(cancelled);
                            if (process.ExitCode != 0) throw new InvalidDataException("Не удалось распаковать установщик Visual C++. Проверь пароль и целостность архива.");
                            copy.GetAwaiter().GetResult();
                        }
                        finally
                        {
                            if (!process.HasExited)
                            {
                                try { process.Kill(); process.WaitForExit(5000); }
                                catch (InvalidOperationException) { }
                            }
                            try { copy.GetAwaiter().GetResult(); }
                            catch (IOException) { }
                            catch (InvalidDataException) { }
                        }
                    }
                }
            }
            var prepared = LibraryCatalog.Deserialize(LibraryCatalog.Serialize(profile));
            ToolsDeployment.SafePath(executable);
            var detected = LibraryDiscovery.Describe(executable, executableName);
            Version detectedVersion, expectedVersion;
            if (detected == null || detected.Kind != LibraryKind.VcBundle ||
                !Version.TryParse(detected.Version, out detectedVersion) ||
                !Version.TryParse(profile.Version, out expectedVersion) || detectedVersion != expectedVersion)
                throw new InvalidDataException("В архиве нет установщика Visual C++ заявленной версии.");
            prepared.RelativePath = executableName;
            using (var input = File.OpenRead(executable)) prepared.Sha256 = LibraryCancellation.Hash(input, cancelled);
            return prepared;
        }
    }
}
