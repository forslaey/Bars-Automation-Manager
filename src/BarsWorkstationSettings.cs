using System;
using System.IO;

namespace ServiceTool
{
    internal static class BarsWorkstationSettings
    {
        internal const string CatalogPassword = "BARS_RELEASE_PASSWORD";
        internal const string RemotePassword = "BARS_REMOTE_STANDARD_PASSWORD";

        internal static string Read(string name)
        {
            string configured = Environment.GetEnvironmentVariable(name) ??
                Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
            if (!String.IsNullOrEmpty(configured)) return configured;

            using (var data = typeof(BarsWorkstationSettings).Assembly.GetManifestResourceStream(name))
            {
                if (data == null) return null;
                using (var reader = new StreamReader(data))
                    return reader.ReadToEnd();
            }
        }
    }
}
