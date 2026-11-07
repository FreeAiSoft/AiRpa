using System;

namespace RPA.Helpers
{
    /// <summary>
    /// Правила видимости пунктов контекстного меню дерева проекта (панель «Проект»).
    /// <para>
    /// Одна точка решения: <c>ProjectManagerControl.ContextMenu_Opened</c> не должно знать,
    /// какой пункт с каким файлом совместим. Иначе появление нового пункта (например
    /// «Открыть в редакторе кода») молча ломает скрытие остальных у <c>project.json</c>:
    /// меню этого файла оставляет только «Открыть содержащую папку».
    /// </para>
    /// <para>
    /// Имена — те же строки, что в <c>x:Name</c> элементов <c>ProjectManagerControl.xaml</c>:
    /// WPF не даёт ссылаться на них из кода без самого элемента, а тесты сверяют имена по XAML.
    /// </para>
    /// </summary>
    public static class ProjectTreeMenuRules
    {
        /// <summary>Пункт «Открыть» (активность — в дизайнер, остальное — по расширению).</summary>
        public const string OpenFileMenuItem = "OpenFileMenuItem";

        /// <summary>Пункт «Открыть в редакторе кода» (встроенный AvalonEdit).</summary>
        public const string OpenInCodeEditorMenuItem = "OpenInCodeEditorMenuItem";

        /// <summary>Пункт «Открыть в Visual Studio» (внешний процесс).</summary>
        public const string OpenInVsMenuItem = "OpenInVsMenuItem";

        /// <summary>Пункт «Собрать с помощью MSBuild».</summary>
        public const string BuildWithMsBuildMenuItem = "BuildWithMsBuildMenuItem";

        /// <summary>Пункт «Удалить файл».</summary>
        public const string DeleteFileMenuItem = "DeleteFileMenuItem";

        /// <summary>Пункт «Открыть содержащую папку» (проводник с выделением файла).</summary>
        public const string OpenFolderContainingFileMenuItem = "OpenFolderContainingFileMenuItem";

        /// <summary>Имя файла проекта самой студии (нативный формат).</summary>
        public const string NativeProjectFileName = "project.json";

        /// <summary>
        /// Виден ли пункт <paramref name="menuItemName"/> в контекстном меню файла
        /// <paramref name="fileName"/>. Имя файла сравнивается без пути и без учёта регистра.
        /// Незнакомые пункты не скрываются: правило обязано быть безопасным для меню,
        /// которые оно не описывает (папки, корень нативного проекта).
        /// </summary>
        public static bool IsVisibleForFile(string menuItemName, string fileName)
        {
            if (IsNativeProjectFile(fileName))
            {
                // project.json — файл проекта студии: открывать его в VS или собирать бессмысленно,
                // поэтому видны только «посмотреть содержимое» и «показать в проводнике».
                return string.Equals(menuItemName, OpenFolderContainingFileMenuItem, StringComparison.Ordinal)
                    || string.Equals(menuItemName, OpenInCodeEditorMenuItem, StringComparison.Ordinal);
            }

            if (string.Equals(menuItemName, OpenInVsMenuItem, StringComparison.Ordinal)
                || string.Equals(menuItemName, BuildWithMsBuildMenuItem, StringComparison.Ordinal))
            {
                // Действия Visual Studio/MSBuild имеют смысл только для файла решения или проекта.
                return IsVsProjectFile(fileName);
            }

            return true;
        }

        /// <summary>Является ли файл проектом студии (<c>project.json</c>).</summary>
        public static bool IsNativeProjectFile(string fileName)
        {
            return string.Equals(FileNameOf(fileName), NativeProjectFileName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Является ли файл решением или проектом Visual Studio (<c>.sln</c>/<c>.csproj</c>).</summary>
        public static bool IsVsProjectFile(string fileName)
        {
            string name = FileNameOf(fileName);
            return name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Имя файла из имени узла или полного пути. Разбор вручную вместо <see cref="System.IO.Path.GetFileName(string)"/>:
        /// узел дерева может прийти с недопустимыми для пути символами, а исключение здесь сорвало бы открытие меню.
        /// </summary>
        private static string FileNameOf(string fileNameOrPath)
        {
            if (string.IsNullOrEmpty(fileNameOrPath))
            {
                return string.Empty;
            }

            int separator = fileNameOrPath.LastIndexOfAny(new[] { '\\', '/' });
            return separator >= 0 && separator + 1 < fileNameOrPath.Length
                ? fileNameOrPath.Substring(separator + 1)
                : fileNameOrPath;
        }
    }
}
