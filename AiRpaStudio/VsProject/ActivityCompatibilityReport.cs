using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RPA.VsProject
{
    /// <summary>Уровень проблемы совместимости.</summary>
    public enum ActivityCompatibilitySeverity
    {
        /// <summary>Блокирует использование проекта в студии.</summary>
        Error,

        /// <summary>Желательно исправить; проект может работать, но с риском.</summary>
        Warning
    }

    /// <summary>
    /// Что может исправить автоматически кнопка «Исправить автоматически».
    /// Сейчас это только точечная перезапись версий пакетов — безопасная операция,
    /// не затрагивающая структуру проекта и исходники.
    /// </summary>
    public enum ActivityCompatibilityFixTarget
    {
        /// <summary>Автофикс не предусмотрен (нужна ручная правка).</summary>
        None,

        /// <summary>Версия в <c>packages.config</c> (<c>&lt;package version="..."&gt;</c>).</summary>
        PackagesConfigVersion,

        /// <summary>Версия в <c>&lt;PackageReference Version="..."&gt;</c>.</summary>
        PackageReferenceVersion,

        /// <summary>Версия в <c>HintPath</c> (<c>packages\id.версия\lib\...</c>).</summary>
        HintPathVersion,

        /// <summary>Версия в <c>&lt;dependency version="..."&gt;</c> файла <c>.nuspec</c>.</summary>
        NuspecDependencyVersion
    }

    /// <summary>Одна проблема совместимости проекта активностей со студией.</summary>
    public class ActivityCompatibilityIssue
    {
        /// <summary>Стабильный код проблемы (для тестов и журнала).</summary>
        public string Code { get; set; }

        public ActivityCompatibilitySeverity Severity { get; set; }

        /// <summary>Человекочитаемое описание проблемы.</summary>
        public string Message { get; set; }

        /// <summary>Что сделать, чтобы проект стал совместимым.</summary>
        public string FixHint { get; set; }

        /// <summary>Абсолютный путь к файлу, в котором найдена проблема (когда применимо).</summary>
        public string FilePath { get; set; }

        /// <summary>Идентификатор пакета, если проблема относится к версии пакета.</summary>
        public string PackageId { get; set; }

        /// <summary>Текущая (неверная) версия пакета.</summary>
        public string CurrentVersion { get; set; }

        /// <summary>Эталонная версия пакета, к которой нужно привести.</summary>
        public string PinnedVersion { get; set; }

        /// <summary>Способ автофикса.</summary>
        public ActivityCompatibilityFixTarget FixTarget { get; set; }

        /// <summary>Может ли эта проблема быть исправлена автоматически.</summary>
        public bool CanFix => FixTarget != ActivityCompatibilityFixTarget.None;
    }

    /// <summary>Результат проверки проекта активностей.</summary>
    public class ActivityCompatibilityReport
    {
        public string ProjectPath { get; set; }
        public string ProjectName { get; set; }
        public List<ActivityCompatibilityIssue> Issues { get; } = new List<ActivityCompatibilityIssue>();

        public IReadOnlyList<ActivityCompatibilityIssue> Errors =>
            Issues.Where(i => i.Severity == ActivityCompatibilitySeverity.Error).ToList();

        public IReadOnlyList<ActivityCompatibilityIssue> Warnings =>
            Issues.Where(i => i.Severity == ActivityCompatibilitySeverity.Warning).ToList();

        public IReadOnlyList<ActivityCompatibilityIssue> FixableIssues =>
            Issues.Where(i => i.CanFix).ToList();

        /// <summary>Проект полностью совместим (нет ошибок).</summary>
        public bool IsCompatible => Errors.Count == 0;

        /// <summary>Есть ли что исправлять автоматически.</summary>
        public bool HasFixableIssues => FixableIssues.Count > 0;

        /// <summary>Человекочитаемый многострочный отчёт для окна и копирования.</summary>
        public string BuildText()
        {
            StringBuilder builder = new StringBuilder();
            _ = builder.Append("Проект: ").AppendLine(ProjectName ?? "<без имени>");
            _ = builder.Append("Путь:   ").AppendLine(ProjectPath ?? string.Empty);
            _ = builder.AppendLine();

            if (Issues.Count == 0)
            {
                _ = builder.AppendLine("Проект совместим со студией.");
                return builder.ToString();
            }

            _ = builder.Append("Проверка завершена: ");
            _ = builder.Append(Errors.Count).Append(" ошибок, ").Append(Warnings.Count).AppendLine(" предупреждений.");
            _ = builder.AppendLine();

            AppendGroup(builder, "ОШИБКИ", Errors);
            AppendGroup(builder, "ПРЕДУПРЕЖДЕНИЯ", Warnings);

            if (HasFixableIssues)
            {
                _ = builder.AppendLine();
                _ = builder.AppendLine("Автоматически можно исправить несовпадения версий пакетов — нажмите «Исправить автоматически».");
            }

            return builder.ToString();
        }

        private static void AppendGroup(StringBuilder builder, string title, IReadOnlyList<ActivityCompatibilityIssue> issues)
        {
            if (issues.Count == 0)
            {
                return;
            }

            _ = builder.AppendLine(title);
            for (int i = 0; i < issues.Count; i++)
            {
                ActivityCompatibilityIssue issue = issues[i];
                _ = builder.Append(i + 1).Append(". ").AppendLine(issue.Message);
                if (!string.IsNullOrWhiteSpace(issue.FixHint))
                {
                    _ = builder.Append("   Как исправить: ").AppendLine(issue.FixHint);
                }

                if (!string.IsNullOrWhiteSpace(issue.FilePath))
                {
                    _ = builder.Append("   Файл: ").AppendLine(issue.FilePath);
                }

                _ = builder.AppendLine();
            }
        }
    }
}
