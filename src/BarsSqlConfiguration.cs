using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Sql;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace ServiceTool
{
    internal sealed class BarsSqlConfiguration
    {
        internal static BarsSqlConfiguration ForPackage(string packageName, string deployed)
        {
            var match = Regex.Match(packageName ?? "", @"^(Bars\.[A-Za-z0-9]+(?:\.[A-Za-z0-9]+)*?)(?:[_ -]v?\d+(?:\.\d+){1,4})?\.exe$", RegexOptions.IgnoreCase);
            if (!match.Success) throw new InvalidOperationException("Выбери программу Барс в формате EXE.");
            string product = match.Groups[1].Value;
            string shortName = product.Substring("Bars.".Length);
            string profile = BarsReleaseCatalog.HasSilentProfile(packageName) ? BarsReleaseCatalog.InstallFolderName(packageName) : shortName;
            var roots = new[] { @"C:\Bars", @"C:\DataKrat" };
            var candidates = new[] { deployed }.Concat(roots.SelectMany(root =>
                new[] { profile, product, shortName }.Select(name => System.IO.Path.Combine(root, name))));
            if (String.IsNullOrWhiteSpace(deployed))
                candidates = candidates.Concat(roots.Where(Directory.Exists)
                    .SelectMany(root => Directory.EnumerateDirectories(root))
                    .Where(directory => File.Exists(System.IO.Path.Combine(directory, product + ".exe"))));
            foreach (string directory in candidates.Where(p => !String.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                string preferred = String.Equals(product, "Bars.Administrator", StringComparison.OrdinalIgnoreCase) ? "SkiLocal.config" :
                    String.Equals(product, "Bars.CashDesk", StringComparison.OrdinalIgnoreCase) ? "local.config" : null;
                var configuration = preferred == null ? null : TryRead(System.IO.Path.Combine(directory, preferred), false);
                if (configuration == null)
                {
                    var matches = new List<BarsSqlConfiguration>();
                    foreach (string path in Directory.EnumerateFiles(directory, "*.config"))
                    {
                        string name = System.IO.Path.GetFileName(path);
                        if (name.EndsWith(".dll.config", StringComparison.OrdinalIgnoreCase)) continue;
                        bool ownExe = String.Equals(name, product + ".exe.config", StringComparison.OrdinalIgnoreCase);
                        if (!ownExe && name.EndsWith(".exe.config", StringComparison.OrdinalIgnoreCase)) continue;
                        var candidate = TryRead(path, !ownExe);
                        if (candidate != null) matches.Add(candidate);
                    }
                    if (matches.Count > 1) throw new InvalidOperationException("Найдено несколько файлов подключения SQL. Сохранение требует однозначного файла настроек.");
                    configuration = matches.SingleOrDefault();
                }
                if (configuration == null) continue;
                if (configuration.Entries.Length > 1)
                    throw new InvalidOperationException("В настройках этой программы несколько подключений SQL. Автоматический выбор пока недоступен.");
                configuration.cashDeskPool = String.Equals(product, "Bars.CashDesk", StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(System.IO.Path.GetFileName(configuration.Path), "local.config", StringComparison.OrdinalIgnoreCase);
                return configuration;
            }
            throw new InvalidOperationException("Для выбранной программы не найден поддерживаемый файл подключения SQL. Проверь, установлена ли она.");
        }

        private static BarsSqlConfiguration TryRead(string path, bool localConfigOnly)
        {
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
            try
            {
                var result = new BarsSqlConfiguration(path);
                return !localConfigOnly || result.document.DocumentElement.Name == "LocalConfig" ? result : null;
            }
            catch (InvalidDataException) { return null; }
            catch (XmlException) { return null; }
        }

        internal sealed class Entry
        {
            internal string Name;
            internal XmlNode Value;
            public override string ToString() { return Name; }
            internal SqlConnectionStringBuilder Read() { return new SqlConnectionStringBuilder(Value.InnerText); }
        }

        private readonly byte[] original;
        private readonly XmlDocument document;
        private bool cashDeskPool;
        internal readonly Entry[] Entries;
        internal readonly string Path;

        internal BarsSqlConfiguration(string path)
        {
            Path = System.IO.Path.GetFullPath(path);
            original = ReadFile(Path);
            document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            using (var input = new MemoryStream(original))
            using (var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2000000 }))
                document.Load(reader);

            var entries = new List<Entry>();
            foreach (XmlElement node in document.SelectNodes("/configuration/connectionStrings/add[@connectionString]"))
            {
                string provider = node.GetAttribute("providerName");
                if (provider == "" || provider == "System.Data.SqlClient")
                    Add(entries, node.GetAttribute("name"), node.Attributes["connectionString"]);
            }
            foreach (XmlElement node in document.SelectNodes("/configuration/appSettings/add[@key='ConnectionString'][@value]"))
                Add(entries, node.GetAttribute("key"), node.Attributes["value"]);
            foreach (XmlElement node in document.SelectNodes("/LocalConfig/ConnectionString"))
                if (!node.ChildNodes.Cast<XmlNode>().Any(child => child.NodeType == XmlNodeType.Element))
                    Add(entries, node.ParentNode.Name + " / " + node.Name, node);
            Entries = entries.ToArray();
            if (Entries.Length == 0) throw new InvalidDataException("В этом файле нет поддерживаемой настройки подключения SQL Server.");
        }

        private static byte[] ReadFile(string path)
        {
            using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (file.Length > 2000000) throw new InvalidDataException("Файл конфигурации слишком большой.");
                using (var buffer = new MemoryStream()) { file.CopyTo(buffer); return buffer.ToArray(); }
            }
        }

        private static void Add(List<Entry> entries, string name, XmlNode value)
        {
            try
            {
                var settings = new SqlConnectionStringBuilder(value.InnerText);
                if (!String.IsNullOrWhiteSpace(settings.DataSource))
                    entries.Add(new Entry { Name = name, Value = value });
            }
            catch (ArgumentException) { }
        }

        internal void Save(Entry entry, SqlConnectionStringBuilder settings)
        {
            if (!Entries.Contains(entry)) throw new InvalidOperationException("Сначала выбери подключение.");
            if (!original.SequenceEqual(ReadFile(Path))) throw new IOException("Файл изменился. Открой его повторно.");
            string temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string previous = entry.Value.InnerText;
            try
            {
                if (cashDeskPool) settings.MaxPoolSize = 2000;
                entry.Value.InnerText = settings.ConnectionString;
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) document.Save(output);
                if (File.Exists(Path) && original.SequenceEqual(ReadFile(Path))) File.Replace(temporary, Path, null);
                else throw new IOException("Файл изменился. Открой его повторно.");
            }
            finally
            {
                entry.Value.InnerText = previous;
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        internal static string[] FindServers()
        {
            using (DataTable data = SqlDataSourceEnumerator.Instance.GetDataSources())
                return data.Rows.Cast<DataRow>().Select(row =>
                {
                    string host = Convert.ToString(row["ServerName"]);
                    string instance = Convert.ToString(row["InstanceName"]);
                    return String.IsNullOrWhiteSpace(host) || String.IsNullOrWhiteSpace(instance) ? host : host + "\\" + instance;
                }).Where(name => !String.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal static async Task<string[]> Connect(SqlConnectionStringBuilder settings, bool listDatabases, CancellationToken token)
        {
            var probe = new SqlConnectionStringBuilder(settings.ConnectionString) { ConnectTimeout = 6, Pooling = false };
            if (listDatabases) probe.InitialCatalog = "master";
            using (var connection = new SqlConnection(probe.ConnectionString))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                if (!listDatabases) return new string[0];
                using (var command = new SqlCommand("SELECT name FROM sys.databases WHERE state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name", connection) { CommandTimeout = 6 })
                using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    var names = new List<string>();
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) names.Add(reader.GetString(0));
                    return names.ToArray();
                }
            }
        }
    }
}
