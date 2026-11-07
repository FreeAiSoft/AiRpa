namespace RPA.NuGet.Models
{
    /// <summary>
    /// Одна версия пакета в выпадающем списке панели деталей (как «Version: Latest stable 10.0.14»
    /// в менеджере пакетов Visual Studio 2026).
    /// </summary>
    public class NuGetPackageVersionInfo
    {
        public string Version { get; set; }

        public bool IsPrerelease { get; set; }

        /// <summary>Версия уже подключена к проекту.</summary>
        public bool IsInstalled { get; set; }

        /// <summary>Самая новая версия источника (включая prerelease).</summary>
        public bool IsLatest { get; set; }

        /// <summary>Самая новая стабильная версия источника.</summary>
        public bool IsLatestStable { get; set; }

        /// <summary>Подпись строки списка версий («Latest stable 10.0.14», «13.0.4», «9.0.0 (installed)»).</summary>
        public string Label { get; set; }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Label) ? Version : Label;
        }
    }
}
