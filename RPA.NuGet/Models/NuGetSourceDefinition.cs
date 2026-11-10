using System;

namespace RPA.NuGet.Models
{
    /// <summary>
    /// Источник пакетов для выпадающего списка «Package source»: берётся из <c>NuGet.Config</c>
    /// (уровни solution/user/global).
    /// </summary>
    public class NuGetSourceDefinition
    {
        public string Name { get; set; }

        /// <summary>Адрес источника (или путь к локальной папке для offline-пакетов).</summary>
        public string Url { get; set; }

        /// <summary>Источник добавляется только через конфигурацию (NuGet.Config).</summary>
        // IsFallback удалён: панель больше не подставляет nuget.org в памяти.

        /// <summary>Состояние галочки «Enabled» в оверлее источников.</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Источник доступен по сети. Локальные папки-источники (offline-пакеты) в списке видны
        /// и ищутся через NuGet.Protocol: пустой запрос показывает все пакеты из папки.
        /// </summary>
        public bool IsRemote
        {
            get
            {
                if (!Uri.TryCreate(Url, UriKind.Absolute, out Uri uri))
                    return false;

                return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
            }
        }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Name) ? Url : Name;
        }
    }
}
