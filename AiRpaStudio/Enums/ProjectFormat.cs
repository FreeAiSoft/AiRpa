namespace RPA.Enums
{
    /// <summary>
    /// Формат проекта студии: встроенный (<c>project.json</c>) или Visual Studio (<c>.csproj</c>).
    /// <para>
    /// Оба формата представляют один и тот же объект <see cref="RPA.Entities.Project"/> и
    /// конвертируются между собой программно.
    /// </para>
    /// </summary>
    public enum ProjectFormat
    {
        /// <summary>Встроенный проект студии (источник — <c>project.json</c>).</summary>
        Native = 0,

        /// <summary>Проект Visual Studio (источник — <c>.csproj</c>).</summary>
        VsProject = 1
    }
}
