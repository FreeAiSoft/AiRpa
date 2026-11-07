using System;

namespace RPA.NuGet.Models
{
    /// <summary>
    /// Search result / package metadata returned from a package source.
    /// <para>
    /// Набор полей повторяет то, что показывает менеджер пакетов Visual Studio 2026: строка списка
    /// рисует иконку, авторов, описание и количество скачиваний, панель деталей — ссылки, теги и дату
    /// публикации. Все дополнительные поля необязательны: источник вправе их не отдать.
    /// </para>
    /// </summary>
    public class NuGetPackageInfo
    {
        public string Id { get; set; }

        public string Version { get; set; }

        public string Description { get; set; }

        public string Authors { get; set; }

        public long TotalDownloads { get; set; }

        /// <summary>Адрес иконки пакета (<c>IconUrl</c> из метаданных); может быть пустым.</summary>
        public string IconUrl { get; set; }

        public string ProjectUrl { get; set; }

        public string LicenseUrl { get; set; }

        /// <summary>Теги через пробел — в том виде, в каком их отдаёт источник.</summary>
        public string Tags { get; set; }

        /// <summary>Дата публикации версии (источник отдаёт UTC).</summary>
        public DateTimeOffset? Published { get; set; }

        /// <summary>Найденная версия — prerelease.</summary>
        public bool IsPrerelease { get; set; }
    }
}
