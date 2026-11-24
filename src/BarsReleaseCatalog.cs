using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ServiceTool
{
    internal sealed class BarsReleaseCatalog
    {
        private const string Endpoint = "https://release.relyxes.crazedns.ru";
        internal const long MinimumProgramBytes = 2L * 1024 * 1024;
        private readonly CookieContainer cookies = new CookieContainer();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

        public sealed class Product { public Release[] releases { get; set; } }
        public sealed class Release
        {
            public string version { get; set; }
            public string channel { get; set; }
            public Artifact[] artifacts { get; set; }
        }
        public sealed class Artifact
        {
            public Guid artifactId { get; set; }
            public string fileName { get; set; }
            public long sizeBytes { get; set; }
            public bool canDownload { get; set; }
            public string sha256 { get; set; }
            public string Version, Channel;
        }

        internal void Login(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string password = BarsWorkstationSettings.Read(BarsWorkstationSettings.CatalogPassword);
            if (String.IsNullOrEmpty(password))
                throw new InvalidOperationException("Доступ к каталогу Барс не настроен на этом компьютере. Обратись к администратору.");
            string body = json.Serialize(new { userName = "service", password = password });
            Read("/api/auth/login", body, token, response => true);
        }

        internal List<Artifact> Scan(bool services, CancellationToken token)
        {
            return Read("/api/products/catalog", null, token, response =>
            {
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    var products = json.Deserialize<Product[]>(reader.ReadToEnd());
                    if (products == null) throw new InvalidDataException("Сервер вернул некорректный каталог.");
                    var result = new List<Artifact>();
                    foreach (var product in products)
                    foreach (var release in product.releases ?? new Release[0])
                    foreach (var file in release.artifacts ?? new Artifact[0])
                    {
                        token.ThrowIfCancellationRequested();
                        if (!file.canDownload || file.artifactId == Guid.Empty || !ValidName(file.fileName, services) ||
                            (!services && file.sizeBytes < MinimumProgramBytes) ||
                            (services && !IsServiceArchive(file.fileName))) continue;
                        file.Version = release.version;
                        file.Channel = release.channel;
                        result.Add(file);
                    }
                    return result.GroupBy(item => item.artifactId).Select(group => group.First()).ToList();
                }
            });
        }

        internal static bool IsServiceArchive(string name)
        {
            string file = Path.GetFileNameWithoutExtension(name);
            return new[] { "service", "agent", "barsserver" }.Any(
                word => file.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static bool ValidName(string name, bool services)
        {
            if (String.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name != Path.GetFileName(name) || name != name.Trim()) return false;
            string extension = Path.GetExtension(name);
            if (!services) return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
            return new[]{".zip", ".rar", ".7z"}.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        private T Read<T>(string route, string body, CancellationToken token, Func<HttpWebResponse, T> consume)
        {
            token.ThrowIfCancellationRequested();
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(Endpoint + route);
            request.CookieContainer = cookies;
            request.AllowAutoRedirect = false;
            request.Timeout = 15000;
            request.ReadWriteTimeout = 30000;
            request.UserAgent = "Bars-Automation-Manager";
            using (token.Register(request.Abort))
            {
                try
                {
                    if (body != null)
                    {
                        var bytes = Encoding.UTF8.GetBytes(body);
                        request.Method = "POST";
                        request.ContentType = "application/json";
                        request.ContentLength = bytes.Length;
                        using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
                    }
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.StatusCode != HttpStatusCode.OK) throw new IOException("Сервер вернул неожиданный ответ.");
                        return consume(response);
                    }
                }
                catch (WebException error)
                {
                    token.ThrowIfCancellationRequested();
                    using (var response = error.Response as HttpWebResponse)
                    {
                        if (response != null && (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden))
                            throw new IOException("Сервер отклонил вход или доступ к файлу. Проверь учётную запись и разрешения.");
                    }
                    throw new IOException("Не удалось получить данные с сервера Bars.Release.", error);
                }
            }
        }

        internal void Download(Artifact item, string folder, bool services, CancellationToken token, Action<TransferProgress> report)
        {
            if (!ValidName(item.fileName, services)) throw new InvalidDataException("Некорректное имя файла.");
            string destination = YandexBundleSource.LocalPath(folder, "/" + item.fileName);
            ToolsDeployment.SafePath(folder);
            Directory.CreateDirectory(folder);
            ToolsDeployment.SafePath(destination);
            string staging = Path.Combine(folder, ".bars-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Read("/api/artifacts/" + item.artifactId + "/download", null, token, response =>
                {
                    long expected = item.sizeBytes > 0 ? item.sizeBytes : response.ContentLength;
                    if (expected > 0 && response.ContentLength > 0 && expected != response.ContentLength)
                        throw new InvalidDataException("Размер файла не совпадает с каталогом.");
                    using (var input = response.GetResponseStream())
                    using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[65536];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            token.ThrowIfCancellationRequested();
                            output.Write(buffer, 0, read);
                            report(new TransferProgress { Stage = "Скачиваем пакет", File = item.fileName, Key = item.fileName,
                                Bytes = output.Length, NetworkBytes = output.Length, Total = expected,
                                Overall = expected > 0 ? Math.Min(.99, (double)output.Length / expected) : -1 });
                        }
                        if (output.Length == 0)
                            throw new InvalidDataException(services ? "Архив пуст или повреждён." : "Файл установщика пуст.");
                        if (expected > 0 && expected != output.Length)
                            throw new InvalidDataException("Файл скачан не полностью.");
                    }
                    return true;
                });
                report(new TransferProgress { Stage = "Проверяем скачанный файл", File = item.fileName });
                if (services)
                {
                    try { ToolArchives.ValidateDownload(staging, item.fileName, token); }
                    catch (InvalidDataException error) { throw new InvalidDataException("Архив пуст или повреждён.", error); }
                }
                else ValidateExecutable(staging);
                VerifyDigest(staging, item.sha256);
                token.ThrowIfCancellationRequested();
                if (File.Exists(destination)) File.Replace(staging, destination, null);
                else File.Move(staging, destination);
            }
            finally
            {
                try { File.Delete(staging); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        internal static void ValidateExecutable(string path)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (reader.BaseStream.Length < MinimumProgramBytes || reader.ReadUInt16() != 0x5a4d)
                    throw new InvalidDataException("Полученный файл не является установщиком Windows.");
                reader.BaseStream.Position = 60;
                int header = reader.ReadInt32();
                if (header < 64 || header > reader.BaseStream.Length - 4)
                    throw new InvalidDataException("Заголовок установщика повреждён.");
                reader.BaseStream.Position = header;
                if (reader.ReadUInt32() != 0x4550) throw new InvalidDataException("Заголовок установщика повреждён.");
            }
        }

        private static readonly Dictionary<string, string> InstallFolders =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Bars.Administrator", "Administrator" },
            { "Bars.CashDesk", "CashDesk" },
            { "Bars.Cashdesk.CustomerDisplay", "Bars.Cashdesk.CustomerDisplay" },
            { "Bars.DataExchange", "DataExchange" },
            { "Bars.Exporter", "Exporter" },
            { "Bars.Lease", "Lease" },
            { "Bars.Lease.Reservation.Manager", "Lease.Reservation.Manager" },
            { "Bars.Pos.Kiosk", "Pos.Kiosk" },
            { "Bars.Trainers", "Bars.Trainers" },
            { "Bars.Trainers.Monitor", "Bars.Trainers.Monitor" },
            { "Bars.Video", "Video" },
        };

        private static readonly Dictionary<string, string> KnownInstallerHashes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Bars.Administrator_v2.12.2.168.exe", "A95CEF3ED08055F832BD083AFD903F9F66AD6A68A836D19310D247EADCA5C19D" },
            { "Bars.CashDesk_v2.12.2.168.exe", "62EADD9891C1B2D49E805FC5A90D6A9C2C17CCB8F4EBB330B6A4D296B2631BF2" },
            { "Bars.Cashdesk.CustomerDisplay_v2.12.2.168.exe", "3E0CC9DDCE41CC39E2E42773B96B7E27483D485482C16C09D3BB2818D7A2D011" },
            { "Bars.DataExchange_v2.12.2.168.exe", "91DEF01BE0BB91216C0B5D45E0127FC9AB184F80B2D84B51DBA4C789E8480746" },
            { "Bars.Exporter_v2.12.2.168.exe", "B3A4CA1FAA71297506C9880D8328C094CAA9AC1FEAC7B9424FC2E88B1307CCDC" },
            { "Bars.Lease_v2.12.2.168.exe", "F5C4B8D83C920722EE986F0962BB7AA2F007CA5C1A9D1BAC7A675F9A7C2E5243" },
            { "Bars.Lease.Reservation.Manager_v2.12.2.168.exe", "E833FFBB017F1DB3BBF74A28F6D2CA30452F44E0F2E98043370AB36B09624DAD" },
            { "Bars.Pos.Kiosk_v2.12.2.168.exe", "7390205662BC12F8C37CB99D34142F57892148B58C7441A37F1E736F74242417" },
            { "Bars.Trainers_v2.12.2.168.exe", "AFBC5EFFF75C19A9749031DBC0F0D253918C02197F388E59EF4FFC11E17CDEF7" },
            { "Bars.Trainers.Monitor_v2.12.2.168.exe", "C9440785843C3106A59DDC048D660FBD4877BA0EB3388147289FDCCB22C1FAED" },
            { "Bars.Video_v2.12.2.168.exe", "2DBB4F3BCBA114E8B453AE0B31B4F05EA8A591BF0D878808019CC3E730EFEAB0" },
        };

        private static string ProductKey(string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(name ?? "",
                @"^(Bars\.[A-Za-z0-9]+(?:\.[A-Za-z0-9]+)*)_v\d+(?:\.\d+){1,3}\.exe$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "";
        }

        internal static bool HasReferenceDigest(string name, string publishedHash)
        {
            return !String.IsNullOrWhiteSpace(publishedHash) || KnownInstallerHashes.ContainsKey(name);
        }

        internal static bool HasSilentProfile(string name) { return InstallFolders.ContainsKey(ProductKey(name)); }

        internal static string InstallFolderName(string name)
        {
            string folder;
            if (!InstallFolders.TryGetValue(ProductKey(name), out folder))
                throw new InvalidDataException("Для установщика нет профиля программы.");
            return folder;
        }

        internal static void VerifyInstalledProgram(string installer, string destination)
        {
            string name = Path.GetFileName(installer);
            string product = ProductKey(name);
            InstallFolderName(name);
            string versionText = Path.GetFileNameWithoutExtension(name).Substring(product.Length + 2);
            Version expected;
            if (!Version.TryParse(versionText, out expected))
                throw new InvalidDataException("Не удалось определить версию выбранной программы.");
            string executable = Path.Combine(destination, product + ".exe");
            ToolsDeployment.SafePath(executable);
            if (!File.Exists(executable))
                throw new IOException("В выбранной папке не найден файл программы " + product + ".exe.");
            var metadata = System.Diagnostics.FileVersionInfo.GetVersionInfo(executable);
            var fileVersion = new Version(metadata.FileMajorPart, metadata.FileMinorPart, metadata.FileBuildPart, metadata.FilePrivatePart);
            var productVersion = new Version(metadata.ProductMajorPart, metadata.ProductMinorPart, metadata.ProductBuildPart, metadata.ProductPrivatePart);
            expected = new Version(expected.Major, expected.Minor, Math.Max(0, expected.Build), Math.Max(0, expected.Revision));
            if (fileVersion != expected && productVersion != expected)
                throw new IOException("Не удалось подтвердить установленную версию " + expected + " по файлу " + product + ".exe.");
        }

        private static void VerifyDigest(string path, string expected)
        {
            if (String.IsNullOrWhiteSpace(expected)) return;
            using (var file = File.OpenRead(path))
            using (var hash = SHA256.Create())
                if (!String.Equals(BitConverter.ToString(hash.ComputeHash(file)).Replace("-", ""),
                    expected.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 файла не совпадает с опубликованной контрольной суммой.");
        }

        internal static void VerifySilentProfile(string path, string publishedHash = null)
        {
            string name = Path.GetFileName(path);
            InstallFolderName(name);
            ValidateExecutable(path);
            string expected;
            VerifyDigest(path, publishedHash);
            if (String.IsNullOrWhiteSpace(publishedHash) && KnownInstallerHashes.TryGetValue(name, out expected))
            {
                using (var file = File.OpenRead(path))
                using (var hash = SHA256.Create())
                    if (!String.Equals(BitConverter.ToString(hash.ComputeHash(file)).Replace("-", ""), expected, StringComparison.Ordinal))
                        throw new InvalidDataException("Файл отличается от ранее проверенного установщика.");
                return;
            }

            // Versioned files share the product profile. Confirm the setup format independently.
            using (var file = File.OpenRead(path))
            using (var reader = new BinaryReader(file))
            {
                var header = reader.ReadBytes((int)Math.Min(file.Length, 256 * 1024));
                if (Encoding.ASCII.GetString(header).IndexOf("Inno Setup Setup Data (", StringComparison.Ordinal) < 0)
                    throw new InvalidDataException("Не удалось подтвердить формат установщика Inno Setup.");
            }
        }
    }
}
