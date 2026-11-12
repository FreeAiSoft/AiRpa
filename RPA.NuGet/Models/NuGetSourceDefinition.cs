using System;

namespace RPA.NuGet.Models
{
    /// <summary>
    /// Источник пакетов для выпадающего списка «Package source»: берётся из per-project хранилища студии или <c>NuGet.Config</c>
    /// (уровни solution/user/global).
    /// </summary>
    public class NuGetSourceDefinition
    {
        public string Name { get; set; }

        /// <summary>Адрес источника (или путь к локальной папке для offline-пакетов).</summary>
        public string Url { get; set; }

        /// <summary>Источник добавляется/редактируется только через оверлей «Package sources».</summary>
        // IsFallback удалён: панель больше не подставляет nuget.org в памяти.

        /// <summary>Состояние галочки «Enabled» в оверлее источников.</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>Откуда взят источник: собственное хранилище проекта или внешний NuGet.Config.</summary>
        public NuGetSourceOrigin Origin { get; set; } = NuGetSourceOrigin.Project;

        /// <summary>
        /// Путь к <c>NuGet.Config</c>, из которого взят внешний источник (Solution/User/Machine).
        /// Для источников per-project хранилища — <c>null</c>.
        /// </summary>
        public string SourceConfigPath { get; set; }

        /// <summary>
        /// Человекочитаемая метка происхождения для группировки в секции импорта
        /// («Machine»/«User»/«Solution»/«Project»).
        /// </summary>
        public string OriginLabel
        {
            get
            {
                switch (Origin)
                {
                    case NuGetSourceOrigin.Machine:
                        return "Machine";
                    case NuGetSourceOrigin.User:
                        return "User";
                    case NuGetSourceOrigin.Solution:
                        return "Solution";
                    default:
                        return "Project";
                }
            }
        }


        /// <summary>
        /// Внешние источники (Solution/User/Machine) менять нельзя — их можно только импортировать
        /// копией в источники проекта.
        /// </summary>
        public bool IsReadOnly => Origin != NuGetSourceOrigin.Project;


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
