using System;
using System.Collections.Generic;

namespace RPA.NuGet.Models
{
    /// <summary>
    /// Полные сведения о пакете для правой панели деталей: описание, авторы, лицензия, дата публикации,
    /// ссылки, теги, README и список версий.
    /// <para>
    /// Данные собираются из того, что уже умеет <see cref="Services.NuGetSearchService"/>
    /// (поиск по идентификатору + список версий), а README — из самого <c>.nupkg</c>: отдельного
    /// metadata- ресурса здесь нет намеренно, чтобы не зависеть от версии NuGet.Protocol.
    /// </para>
    /// </summary>
    public class NuGetPackageDetails
    {
        public string Id { get; set; }

        public string Version { get; set; }

        public string Description { get; set; }

        public string Authors { get; set; }

        public string LicenseUrl { get; set; }

        public string ProjectUrl { get; set; }

        public string ReportAbuseUrl { get; set; }

        public string Tags { get; set; }

        public DateTimeOffset? Published { get; set; }

        public long TotalDownloads { get; set; }

        public string IconUrl { get; set; }

        /// <summary>Текст README из пакета; <c>null</c> — README не найден или ещё не загружен.</summary>
        public string Readme { get; set; }

        public bool IsPrerelease { get; set; }

        /// <summary>Доступные версии (по убыванию), заполняется панелью деталей.</summary>
        public List<NuGetPackageVersionInfo> Versions { get; } = new List<NuGetPackageVersionInfo>();
    }
}
