using System;

namespace RPA.NuGet.Models
{
    /// <summary>
    /// Источник пакетов для выпадающего списка «Package source»: берётся из <c>NuGet.Config</c>
    /// (уровни solution/user/global), при отсутствии конфигурации — nuget.org.
    /// </summary>
    public class NuGetSourceDefinition
    {
        public string Name { get; set; }

        /// <summary>Адрес источника (<c>https://api.nuget.org/v3/index.json</c>).</summary>
        public string Url { get; set; }

        /// <summary>Источник подставлен самой панелью: в конфигурации ничего не нашлось.</summary>
        public bool IsFallback { get; set; }

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
