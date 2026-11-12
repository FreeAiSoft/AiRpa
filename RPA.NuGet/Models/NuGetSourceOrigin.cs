namespace RPA.NuGet.Models
{
    /// <summary>
    /// Откуда взят источник пакетов: собственное хранилище проекта студии или внешний
    /// <c>NuGet.Config</c> (решение/пользователь/машина).
    /// </summary>
    public enum NuGetSourceOrigin
    {
        /// <summary>Источник из per-project хранилища AiRpaStudio (редактируемый).</summary>
        Project,

        /// <summary>Источник из <c>NuGet.Config</c> уровня решения.</summary>
        Solution,

        /// <summary>Источник из <c>NuGet.Config</c> уровня пользователя.</summary>
        User,

        /// <summary>Источник из <c>NuGet.Config</c> уровня машины.</summary>
        Machine
    }
}
