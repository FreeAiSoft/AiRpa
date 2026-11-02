using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RPA.Core
{
    /// <summary>
    /// Документ, открытый в сессии студии: вкладка дизайнера WF (<c>.xaml</c>) или вкладка редактора кода.
    /// </summary>
    public sealed class WorkflowSessionDocument
    {
        /// <summary>Тип документа: вкладка дизайнера рабочего процесса.</summary>
        public const string WorkflowKind = "workflow";

        /// <summary>Тип документа: вкладка редактора кода.</summary>
        public const string CodeKind = "code";

        /// <summary>Полный путь к открытому файлу.</summary>
        [JsonProperty("path")]
        public string FilePath { get; set; }

        /// <summary><see cref="WorkflowKind"/> или <see cref="CodeKind"/>.</summary>
        [JsonProperty("kind")]
        public string Kind { get; set; }

        /// <summary>Открыт ли файл в дизайнере WF (иначе — редактор кода).</summary>
        [JsonIgnore]
        public bool IsWorkflow => string.Equals(Kind, WorkflowKind, StringComparison.OrdinalIgnoreCase);

        /// <summary>Документ по пути: тип определяется расширением (как в <c>Main.CmdTryLoadFile</c>).</summary>
        public static WorkflowSessionDocument ForPath(string filePath)
        {
            return new WorkflowSessionDocument
            {
                FilePath = filePath,
                Kind = KindForPath(filePath)
            };
        }

        /// <summary>Тип вкладки по расширению файла.</summary>
        public static string KindForPath(string filePath)
        {
            return !string.IsNullOrWhiteSpace(filePath)
                   && filePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                ? WorkflowKind
                : CodeKind;
        }
    }

    /// <summary>
    /// Состояние вкладок студии (баг «не персистится состояние вкладок»): список открытых документов,
    /// порядок вкладок и активная вкладка.
    /// <para>
    /// Логика намеренно отделена от WPF и от <c>Properties.Settings</c>: сериализация, чтение старого
    /// формата (<c>DesignerFiles</c> — просто JSON-массив путей), отбрасывание несуществующих файлов и
    /// файлов чужого проекта — всё проверяется юнит-тестами
    /// (<c>AiRpaStudio.Tests\WorkflowSessionStateTests.cs</c>).
    /// </para>
    /// <para>
    /// Хранится в существующей настройке <c>Properties.Settings.Default.DesignerFiles</c>: новая настройка
    /// потребовала бы правки конструктора настроек, а формат старого массива читается как fallback.
    /// </para>
    /// </summary>
    public sealed class WorkflowSessionState
    {
        /// <summary>Версия формата: позволяет отличить новый объект от старого массива путей.</summary>
        public const int FormatVersion = 1;

        /// <summary>Ключ настройки, в которой живёт состояние (имя оставлено ради совместимости).</summary>
        public const string SettingsPropertyName = "DesignerFiles";

        /// <summary>Проект, к которому относится сессия (папка проекта или файл .sln/.csproj).</summary>
        [JsonProperty("project")]
        public string ProjectPath { get; set; }

        /// <summary>Активная вкладка (путь) или <c>null</c>.</summary>
        [JsonProperty("active")]
        public string ActiveDocument { get; set; }

        /// <summary>Открытые документы в порядке вкладок.</summary>
        [JsonProperty("documents")]
        public List<WorkflowSessionDocument> Documents { get; } = new List<WorkflowSessionDocument>();

        /// <summary>Пустое состояние проекта — открытых вкладок нет.</summary>
        public static WorkflowSessionState Empty(string projectPath)
        {
            return new WorkflowSessionState { ProjectPath = projectPath };
        }

        /// <summary>
        /// Собирает состояние из путей вкладок (порядок сохраняется) и активного документа.
        /// <paramref name="workflowFiles"/> — вкладки дизайнера, <paramref name="codeFiles"/> — вкладки кода.
        /// </summary>
        public static WorkflowSessionState Create(
            string projectPath,
            IEnumerable<string> workflowFiles,
            IEnumerable<string> codeFiles,
            string activeDocument)
        {
            WorkflowSessionState state = Empty(projectPath);

            foreach (string path in workflowFiles ?? Enumerable.Empty<string>())
            {
                state.Documents.Add(new WorkflowSessionDocument
                {
                    FilePath = path,
                    Kind = WorkflowSessionDocument.WorkflowKind
                });
            }

            foreach (string path in codeFiles ?? Enumerable.Empty<string>())
            {
                state.Documents.Add(new WorkflowSessionDocument
                {
                    FilePath = path,
                    Kind = WorkflowSessionDocument.CodeKind
                });
            }

            state.ActiveDocument = activeDocument;
            return state.Normalize();
        }

        /// <summary>Состояние в виде JSON (пустое состояние — пустая строка, чтобы старые сборки не падали).</summary>
        public string Serialize()
        {
            if (Documents.Count == 0)
            {
                return string.Empty;
            }

            JObject json = new JObject
            {
                ["version"] = FormatVersion,
                ["project"] = ProjectPath,
                ["active"] = ActiveDocument
            };

            JArray documents = new JArray();
            foreach (WorkflowSessionDocument document in Documents)
            {
                documents.Add(new JObject
                {
                    ["path"] = document.FilePath,
                    ["kind"] = document.Kind
                });
            }

            json["documents"] = documents;
            return json.ToString(Formatting.None);
        }

        /// <summary>Читает состояние из JSON. Битый JSON и <c>null</c> дают пустое состояние, а не исключение.</summary>
        public static WorkflowSessionState Deserialize(string json, string projectPath)
        {
            return Deserialize(json, projectPath, File.Exists);
        }

        /// <summary>
        /// Читает состояние из JSON и нормализует его: <paramref name="fileExists"/> отбрасывает
        /// несуществующие файлы (по умолчанию — <see cref="File.Exists(string)"/>).
        /// </summary>
        public static WorkflowSessionState Deserialize(string json, string projectPath, Func<string, bool> fileExists)
        {
            WorkflowSessionState state = Parse(json, projectPath);
            return state.Normalize(fileExists ?? File.Exists);
        }

        /// <summary>
        /// Проверяет и чистит состояние: пустые пути и дубликаты (без учёта регистра) отбрасываются
        /// всегда, активная вкладка обязана быть среди документов.
        /// <para>
        /// Если задан <paramref name="fileExists"/>, дополнительно отбрасываются несуществующие файлы и
        /// файлы вне папки проекта — это путь <b>чтения</b> сессии. Без него (путь сохранения открытых
        /// сейчас вкладок) фильтрация по диску не нужна: файл мог быть только что создан.
        /// </para>
        /// </summary>
        public WorkflowSessionState Normalize(Func<string, bool> fileExists = null)
        {
            List<WorkflowSessionDocument> normalized = new List<WorkflowSessionDocument>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (WorkflowSessionDocument document in Documents)
            {
                string path = document?.FilePath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (!seen.Add(path))
                {
                    continue;
                }

                if (fileExists != null && (!IsInProject(ProjectPath, path) || !fileExists(path)))
                {
                    seen.Remove(path);
                    continue;
                }

                normalized.Add(new WorkflowSessionDocument
                {
                    FilePath = path,
                    Kind = string.Equals(document.Kind, WorkflowSessionDocument.WorkflowKind, StringComparison.OrdinalIgnoreCase)
                        ? WorkflowSessionDocument.WorkflowKind
                        : WorkflowSessionDocument.CodeKind
                });
            }

            Documents.Clear();
            Documents.AddRange(normalized);

            ActiveDocument = ResolveActiveDocument(ActiveDocument);
            return this;
        }

        private string ResolveActiveDocument(string active)
        {
            if (!string.IsNullOrWhiteSpace(active))
            {
                WorkflowSessionDocument match = Documents.FirstOrDefault(
                    d => string.Equals(d.FilePath, active, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    // Приводим к написанию из списка документов: в настройках путь мог «поехать» по регистру.
                    return match.FilePath;
                }
            }

            return Documents.Count > 0 ? Documents[0].FilePath : null;
        }

        private static WorkflowSessionState Parse(string json, string projectPath)
        {
            WorkflowSessionState state = Empty(projectPath);

            if (string.IsNullOrWhiteSpace(json))
            {
                return state;
            }

            try
            {
                JToken token = JToken.Parse(json);

                if (token is JArray legacy)
                {
                    // Старый формат — просто массив путей без активной вкладки и без типа документа.
                    foreach (JToken item in legacy)
                    {
                        string path = item.Type == JTokenType.String ? item.Value<string>() : null;
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            state.Documents.Add(WorkflowSessionDocument.ForPath(path));
                        }
                    }

                    return state;
                }

                if (!(token is JObject obj))
                {
                    return state;
                }

                state.ProjectPath = obj.Value<string>("project") ?? projectPath;
                state.ActiveDocument = obj.Value<string>("active");

                if (obj["documents"] is JArray documents)
                {
                    foreach (JToken item in documents)
                    {
                        WorkflowSessionDocument document = ReadDocument(item);
                        if (document != null)
                        {
                            state.Documents.Add(document);
                        }
                    }
                }

                return state;
            }
            catch (Exception)
            {
                // Битый JSON (обрезанный файл настроек, ручная правка) — просто пустая сессия.
                return Empty(projectPath);
            }
        }

        private static WorkflowSessionDocument ReadDocument(JToken item)
        {
            if (item is JObject obj)
            {
                string path = obj.Value<string>("path");
                if (string.IsNullOrWhiteSpace(path))
                {
                    return null;
                }

                string kind = obj.Value<string>("kind");
                return new WorkflowSessionDocument
                {
                    FilePath = path,
                    Kind = string.IsNullOrWhiteSpace(kind) ? WorkflowSessionDocument.KindForPath(path) : kind
                };
            }

            if (item.Type == JTokenType.String)
            {
                string path = item.Value<string>();
                return string.IsNullOrWhiteSpace(path) ? null : WorkflowSessionDocument.ForPath(path);
            }

            return null;
        }

        /// <summary>
        /// Лежит ли файл внутри проекта. Папка проекта — это либо папка классического проекта, либо папка
        /// файла .sln/.csproj: подтягивать вкладки чужого проекта при переключении проекта нельзя.
        /// </summary>
        internal static bool IsInProject(string projectPath, string filePath)
        {
            if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            string root = ProjectFolder(projectPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            try
            {
                string normalizedRoot = Path.GetFullPath(root);
                string normalizedFile = Path.GetFullPath(filePath);

                if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                {
                    normalizedRoot += Path.DirectorySeparatorChar;
                }

                return normalizedFile.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                // Невалидные символы в пути — документ не восстанавливаем.
                return false;
            }
        }

        private static string ProjectFolder(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return null;
            }

            bool isFile = projectPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                          || projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                          || projectPath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
                          || (File.Exists(projectPath) && !Directory.Exists(projectPath));

            return isFile ? Path.GetDirectoryName(projectPath) : projectPath;
        }
    }
}
