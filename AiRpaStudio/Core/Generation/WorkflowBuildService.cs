using System;
using System.Activities;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RPA.Helpers;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Результат генерации процесса: единый формат ответа инструмента
    /// <c>{ok, workflowId, filePath, xaml, errors[], warnings[], hints[]}</c>
    /// (этап 2 конвейера генерации XAML).
    /// </summary>
    internal sealed class WorkflowBuildResult
    {
        private WorkflowBuildResult()
        {
        }

        /// <summary>Генерация успешна: файл записан, гейт валидации не нашёл ошибок.</summary>
        internal bool Ok { get; private set; }

        /// <summary>Идентификатор процесса — <c>x:Class</c> из XAML (по нему студия узнаёт файл).</summary>
        internal string WorkflowId { get; private set; }

        /// <summary>Полный путь к записанному файлу (<c>null</c>, если собирали без сохранения).</summary>
        internal string FilePath { get; private set; }

        /// <summary>Канонический текст XAML — даже при ошибке: агент видит, что именно собралось.</summary>
        internal string Xaml { get; private set; }

        /// <summary>Ошибки гейта и записи файла.</summary>
        internal List<string> Errors { get; } = new List<string>();

        /// <summary>Предупреждения валидации WF.</summary>
        internal List<string> Warnings { get; } = new List<string>();

        /// <summary>Подсказки: как исправить ошибку (канонизация, ссылки, лямбды).</summary>
        internal List<string> Hints { get; } = new List<string>();

        internal static WorkflowBuildResult Success(
            string workflowId,
            string filePath,
            string xaml,
            IEnumerable<string> warnings,
            IEnumerable<string> hints)
        {
            var result = new WorkflowBuildResult
            {
                Ok = true,
                WorkflowId = workflowId,
                FilePath = filePath,
                Xaml = xaml
            };

            result.AddWarnings(warnings);
            result.AddHints(hints);

            return result;
        }

        internal static WorkflowBuildResult Failure(
            string workflowId,
            string filePath,
            string xaml,
            IEnumerable<string> errors,
            IEnumerable<string> warnings = null,
            IEnumerable<string> hints = null)
        {
            var result = new WorkflowBuildResult
            {
                Ok = false,
                WorkflowId = workflowId,
                FilePath = filePath,
                Xaml = xaml
            };

            if (errors != null)
            {
                result.Errors.AddRange(errors.Where(error => !string.IsNullOrWhiteSpace(error)));
            }

            result.AddWarnings(warnings);
            result.AddHints(hints);

            return result;
        }

        /// <summary>Текст для сообщения об ошибке теста или для лога.</summary>
        internal string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine(Ok ? "генерация: ок" : "генерация: ОШИБКИ");
            text.AppendLine($"workflowId: {WorkflowId}");
            text.AppendLine($"файл: {FilePath ?? "(не сохранён)"}");
            text.AppendLine($"ошибок: {Errors.Count}, предупреждений: {Warnings.Count}");

            AppendBlock(text, "ошибки", Errors);
            AppendBlock(text, "предупреждения", Warnings);
            AppendBlock(text, "подсказки", Hints);

            return text.ToString();
        }

        private void AddWarnings(IEnumerable<string> warnings)
        {
            if (warnings != null)
            {
                Warnings.AddRange(warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)));
            }
        }

        private void AddHints(IEnumerable<string> hints)
        {
            if (hints == null)
            {
                return;
            }

            foreach (string hint in hints.Where(hint => !string.IsNullOrWhiteSpace(hint)))
            {
                if (!Hints.Contains(hint))
                {
                    Hints.Add(hint);
                }
            }
        }

        private static void AppendBlock(StringBuilder text, string title, IList<string> items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            text.AppendLine(title + ":");
            foreach (string item in items)
            {
                text.AppendLine("  - " + item?.Replace("\r\n", "\r\n    "));
            }
        }
    }

    /// <summary>
    /// Единый конвейер генерации процесса (этап 2 конвейера генерации XAML):
    /// <code>
    /// Activity / ActivityBuilder
    ///   → WorkflowCanonicalizer (формат студии по объектной модели)
    ///   → WorkflowXamlSerializer (сериализатор WF)
    ///   → временный файл → WorkflowValidationGate → атомарная замена целевого файла
    /// </code>
    /// <para>
    /// Все уровни (палитра, Roslyn, JSON AST — этапы 4–6) обязаны заканчиваться этим конвейером:
    /// расхождение путей сохранения даёт «работает в одном инструменте, ломается в другом».
    /// </para>
    /// <para>
    /// Сохранение атомарное: сначала пишется временный файл рядом с целевым, проверяется гейтом и только
    /// потом заменяет целевой. Невалидный или недописанный XAML в проекте не остаётся (п. 4 плана).
    /// </para>
    /// </summary>
    internal sealed class WorkflowBuildService
    {
        private const string OpenInDesignerHint =
            "файл открыт в конструкторе: студия держит его с FileShare.Read, поэтому перезапись невозможна — " +
            "закройте вкладку процесса (DesignerOpenService.Close), перезагрузите её " +
            "(DesignerOpenService.Reload) или сохраните генерацию в новый файл";

        private static readonly Encoding FileEncoding = new UTF8Encoding(false);

        private readonly IWorkflowValidationGate _gate;

        /// <summary>
        /// Проверка «файл не занят вкладкой конструктора» (этап 3 плана). Необязательна: без неё занятый
        /// файл обнаруживается только на записи — исключением <see cref="IOException"/> после валидации.
        /// </summary>
        private readonly IDesignerWriteCheck _writeCheck;

        /// <summary>
        /// Правило «нет лямбд в аргументах» (этап 5 плана): проверяется до канонизации, потому что лямбда
        /// не сериализуется и оставляет в XAML невалидный узел.
        /// </summary>
        private readonly XamlExpressionPolicy _expressionPolicy;

        internal WorkflowBuildService(
            WorkflowCanonicalizationOptions options,
            IWorkflowValidationGate gate = null,
            IDesignerWriteCheck writeCheck = null,
            XamlExpressionPolicy expressionPolicy = null)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));

            _gate = gate ?? new WorkflowValidationGate(new WorkflowValidationOptions
            {
                ProjectAssemblyName = options.ProjectAssemblyName,
                ExpectedExpressionActivityEditor = options.ExpressionActivityEditor
            });
            _writeCheck = writeCheck;
            _expressionPolicy = expressionPolicy ?? XamlExpressionPolicy.Default;
        }

        /// <summary>Параметры канонизации, с которыми работает сервис.</summary>
        internal WorkflowCanonicalizationOptions Options { get; }

        /// <summary>Приводит объектную модель к формату студии (без записи файла).</summary>
        internal ActivityBuilder CreateBuilder(Activity implementation, IList<string> hints = null)
        {
            return WorkflowCanonicalizer.Canonicalize(implementation, Options, hints);
        }

        /// <summary>Собирает XAML и проверяет его гейтом, ничего не записывая на диск.</summary>
        internal WorkflowBuildResult Build(Activity implementation, IList<string> hints = null)
        {
            var collectedHints = new List<string>();
            if (hints != null)
            {
                collectedHints.AddRange(hints);
            }

            // Правило «нет лямбд» (п. 1.2 плана) — до канонизации: лямбда не сериализуется.
            XamlExpressionPolicyReport expressions = _expressionPolicy.Check(implementation);
            if (!expressions.Ok)
            {
                return WorkflowBuildResult.Failure(
                    Options.QualifiedClassName,
                    null,
                    null,
                    expressions.Violations,
                    hints: collectedHints.Concat(expressions.Hints));
            }

            string xaml;
            try
            {
                ActivityBuilder builder = CreateBuilder(implementation, collectedHints);
                xaml = WorkflowXamlSerializer.Serialize(builder);
            }
            catch (Exception ex)
            {
                return WorkflowBuildResult.Failure(
                    Options.QualifiedClassName,
                    null,
                    null,
                    new[] { ErrorText.Describe("XAML не собран", ex) },
                    hints: collectedHints);
            }

            WorkflowValidationReport report = ValidateText(xaml);

            return report.Ok
                ? WorkflowBuildResult.Success(
                    Options.QualifiedClassName, null, xaml, report.Warnings, collectedHints.Concat(report.Hints))
                : WorkflowBuildResult.Failure(
                    Options.QualifiedClassName, null, xaml, report.Errors, report.Warnings,
                    collectedHints.Concat(report.Hints));
        }

        /// <summary>
        /// Собирает, валидирует и атомарно записывает XAML в <paramref name="filePath"/>.
        /// Целевой файл меняется только после успешного гейта.
        /// </summary>
        internal WorkflowBuildResult Save(Activity implementation, string filePath, IList<string> hints = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return WorkflowBuildResult.Failure(
                    Options.QualifiedClassName,
                    null,
                    null,
                    new[] { "xaml.path: путь к файлу процесса не задан" },
                    hints: hints);
            }

            string fullPath = Path.GetFullPath(filePath);
            string directory = Path.GetDirectoryName(fullPath);

            // Открытый в конструкторе файл держится вкладкой с FileShare.Read: File.Replace упал бы
            // с IOException уже после валидации. Проверяем заранее и отвечаем ошибкой с подсказкой
            // (этап 3 плана, п. 1.3 и 4).
            DesignerWriteCheck writeCheck = _writeCheck?.CheckWrite(fullPath);
            if (writeCheck != null && !writeCheck.CanWrite)
            {
                var writeHints = new List<string>();
                if (hints != null)
                {
                    writeHints.AddRange(hints);
                }

                writeHints.AddRange(writeCheck.Hints);
                writeHints.Add(OpenInDesignerHint);

                return WorkflowBuildResult.Failure(
                    Options.QualifiedClassName,
                    fullPath,
                    null,
                    new[] { writeCheck.Error },
                    hints: writeHints);
            }

            string temporaryPath;
            try
            {
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                temporaryPath = Path.Combine(
                    directory ?? string.Empty,
                    "." + Path.GetFileNameWithoutExtension(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp.xaml");
            }
            catch (Exception ex)
            {
                return WorkflowBuildResult.Failure(
                    Options.QualifiedClassName,
                    fullPath,
                    null,
                    new[] { ErrorText.Describe($"папка процесса недоступна: {directory}", ex) },
                    hints: hints);
            }

            WorkflowBuildResult build = Build(implementation, hints);
            if (!build.Ok)
            {
                return build;
            }

            // Отладочный символ вписывается до записи: гейт валидирует ровно тот текст, который
            // ляжет на диск, а номера строк символа соответствуют финальному файлу.
            string xamlToWrite = DebugSymbolInjector.Inject(build.Xaml, fullPath);

            try
            {
                File.WriteAllText(temporaryPath, xamlToWrite, FileEncoding);
            }
            catch (Exception ex)
            {
                DeleteSilently(temporaryPath);

                return WorkflowBuildResult.Failure(
                    Options.QualifiedClassName,
                    fullPath,
                    build.Xaml,
                    new[] { ErrorText.Describe($"не удалось записать временный файл {temporaryPath}", ex) },
                    build.Warnings,
                    build.Hints);
            }

            WorkflowValidationReport report = _gate.Validate(temporaryPath);
            if (!report.Ok)
            {
                DeleteSilently(temporaryPath);

                return WorkflowBuildResult.Failure(
                    Options.QualifiedClassName,
                    fullPath,
                    build.Xaml,
                    report.Errors,
                    report.Warnings,
                    build.Hints.Concat(report.Hints));
            }

            try
            {
                Replace(temporaryPath, fullPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                DeleteSilently(temporaryPath);

                var hintsOut = new List<string>(build.Hints);
                hintsOut.Add(OpenInDesignerHint);

                return WorkflowBuildResult.Failure(
                    Options.QualifiedClassName,
                    fullPath,
                    build.Xaml,
                    new[] { ErrorText.Describe($"не удалось заменить {fullPath}", ex) },
                    report.Warnings,
                    hintsOut);
            }
            catch (Exception ex)
            {
                DeleteSilently(temporaryPath);

                return WorkflowBuildResult.Failure(
                    Options.QualifiedClassName,
                    fullPath,
                    build.Xaml,
                    new[] { ErrorText.Describe($"не удалось заменить {fullPath}", ex) },
                    report.Warnings,
                    build.Hints);
            }

            return WorkflowBuildResult.Success(
                Options.QualifiedClassName,
                fullPath,
                build.Xaml,
                report.Warnings,
                build.Hints.Concat(report.Hints));
        }

        private WorkflowValidationReport ValidateText(string xaml)
        {
            if (_gate is WorkflowValidationGate gate)
            {
                return gate.ValidateText(xaml);
            }

            // Свой гейт (тесты, будущие проверки) умеет проверять только файл: пишем во временный.
            string temporaryPath = Path.Combine(Path.GetTempPath(), "AiRpaStudio-build-" + Guid.NewGuid().ToString("N") + ".xaml");

            try
            {
                File.WriteAllText(temporaryPath, xaml, FileEncoding);
                return _gate.Validate(temporaryPath);
            }
            catch (Exception ex)
            {
                var report = new WorkflowValidationReport();
                report.AddError("xaml.write", ErrorText.Describe("гейт валидации не выполнен", ex));

                return report;
            }
            finally
            {
                DeleteSilently(temporaryPath);
            }
        }

        /// <summary>
        /// Атомарная замена целевого файла. <c>File.Replace</c> требует существующий файл,
        /// <c>File.Move</c> — отсутствующий, поэтому ветки разделены.
        /// </summary>
        private static void Replace(string temporaryPath, string targetPath)
        {
            if (File.Exists(targetPath))
            {
                File.Replace(temporaryPath, targetPath, null);
                return;
            }

            File.Move(temporaryPath, targetPath);
        }

        private static void DeleteSilently(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                // Временный файл не должен портить ответ инструмента.
                System.Diagnostics.Debug.WriteLine("Временный файл процесса не удалён: " + ErrorText.Of(ex));
            }
        }
    }
}
