using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace RPA.VsProject
{
    /// <summary>
    /// Правки файла проекта Visual Studio (<c>.csproj</c>) без пересоздания: регистрация новой
    /// активности (<c>XamlAppDef</c>) и переименование самого файла проекта.
    /// <para>
    /// Содержимое проекта (<c>AssemblyName</c>, <c>RootNamespace</c>, ссылки) не меняется: студия
    /// не переписывает за пользователем его проект, а только поддерживает его в рабочем состоянии
    /// (новый <c>.xaml</c> попадает в сборку, переименованный файл остаётся корректно подключён к
    /// <c>.sln</c>).
    /// </para>
    /// </summary>
    public static class VsProjectWriter
    {
        private const string XamlItemType = "XamlAppDef";

        /// <summary>
        /// Добавляет в проект элемент <c>XamlAppDef</c> (активность WF), если его там ещё нет.
        /// Возвращает <c>true</c>, если файл проекта изменён.
        /// </summary>
        public static bool AddXamlItem(string projectPath, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                throw new ArgumentException("Путь к файлу проекта не указан.", nameof(projectPath));
            }

            string fullProjectPath = Path.GetFullPath(projectPath);
            if (!File.Exists(fullProjectPath))
            {
                throw new FileNotFoundException("Файл проекта не найден.", fullProjectPath);
            }

            string include = NormalizeInclude(relativePath);
            XDocument document = LoadPreservingFormat(fullProjectPath);
            XElement root = document.Root ?? throw new InvalidDataException("Файл проекта пуст.");

            bool alreadyIncluded = root
                .Descendants()
                .Where(e => string.Equals(e.Name.LocalName, XamlItemType, StringComparison.OrdinalIgnoreCase))
                .Any(e => string.Equals(NormalizeInclude((string)e.Attribute("Include")), include, StringComparison.OrdinalIgnoreCase));
            if (alreadyIncluded)
            {
                return false;
            }

            XElement newItem = new XElement(root.Name.Namespace + XamlItemType);
            newItem.SetAttributeValue("Include", include);
            newItem.Add(new XElement(root.Name.Namespace + "SubType", "Designer"));
            newItem.Add(new XElement(root.Name.Namespace + "Generator", "MSBuild:Compile"));

            XElement itemGroup = FindXamlItemGroup(root)
                                 ?? FindItemGroup(root, "Compile")
                                 ?? AddItemGroup(root);

            AppendChild(itemGroup, newItem, root);
            SavePreservingFormat(document, fullProjectPath);
            return true;
        }

        /// <summary>
        /// Переименовывает файл проекта (<c>*.csproj</c>) и правит ссылку на него в найденных <c>.sln</c>.
        /// Возвращает новый путь к файлу проекта. Имя сборки и содержимое проекта не меняются.
        /// </summary>
        public static string RenameProjectFile(string projectPath, string newName)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                throw new ArgumentException("Путь к файлу проекта не указан.", nameof(projectPath));
            }

            string fullProjectPath = Path.GetFullPath(projectPath);
            if (!File.Exists(fullProjectPath))
            {
                throw new FileNotFoundException("Файл проекта не найден.", fullProjectPath);
            }

            string fileName = (newName ?? string.Empty).Trim();
            if (fileName.Length == 0)
            {
                throw new ArgumentException("Имя проекта не может быть пустым.", nameof(newName));
            }

            if (!fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".csproj";
            }

            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Имя проекта '{newName}' содержит недопустимые символы.", nameof(newName));
            }

            string projectFolder = Path.GetDirectoryName(fullProjectPath);
            string newPath = Path.Combine(projectFolder, fileName);

            if (string.Equals(newPath, fullProjectPath, StringComparison.OrdinalIgnoreCase))
            {
                return fullProjectPath;
            }

            if (File.Exists(newPath))
            {
                throw new IOException($"Файл проекта '{fileName}' уже существует.");
            }

            File.Move(fullProjectPath, newPath);
            UpdateSolutionReferences(fullProjectPath, newPath);
            MoveUserFile(fullProjectPath, newPath);

            return newPath;
        }

        /// <summary>
        /// Правит ссылки на переименованный файл проекта в <c>.sln</c> (папка проекта и её предки).
        /// Возвращает список обновлённых файлов решения.
        /// </summary>
        public static IReadOnlyList<string> UpdateSolutionReferences(string oldProjectPath, string newProjectPath)
        {
            List<string> updated = new List<string>();

            if (string.IsNullOrWhiteSpace(oldProjectPath) || string.IsNullOrWhiteSpace(newProjectPath))
            {
                return updated;
            }

            string oldFullPath = Path.GetFullPath(oldProjectPath);
            string newFullPath = Path.GetFullPath(newProjectPath);
            if (string.Equals(oldFullPath, newFullPath, StringComparison.OrdinalIgnoreCase))
            {
                return updated;
            }

            string projectFolder = Path.GetDirectoryName(newFullPath);

            foreach (string solutionPath in FindSolutionFiles(projectFolder))
            {
                string solutionFolder = Path.GetDirectoryName(solutionPath);
                string oldRelative = GetRelativeIncludePath(solutionFolder, oldFullPath);
                string newRelative = GetRelativeIncludePath(solutionFolder, newFullPath);

                if (string.Equals(oldRelative, newRelative, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string text = File.ReadAllText(solutionPath);
                string pattern = "\"" + BuildPathPattern(oldRelative) + "\"";
                string replaced = Regex.Replace(
                    text,
                    pattern,
                    _ => "\"" + newRelative + "\"",
                    RegexOptions.IgnoreCase);

                if (!string.Equals(text, replaced, StringComparison.Ordinal))
                {
                    File.WriteAllText(solutionPath, replaced, DetectEncoding(solutionPath, out bool _));
                    updated.Add(solutionPath);
                }
            }

            return updated;
        }

        /// <summary>Относительный путь (с разделителями <c>\</c>) от папки к файлу — для <c>Include</c>.</summary>
        public static string GetRelativeIncludePath(string fromDirectory, string path)
        {
            if (string.IsNullOrWhiteSpace(fromDirectory))
            {
                throw new ArgumentException("Папка не указана.", nameof(fromDirectory));
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Путь не указан.", nameof(path));
            }

            Uri fromUri = new Uri(AppendSeparator(Path.GetFullPath(fromDirectory)));
            Uri toUri = new Uri(Path.GetFullPath(path));

            if (!string.Equals(fromUri.Scheme, toUri.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(path);
            }

            string relative = Uri.UnescapeDataString(fromUri.MakeRelativeUri(toUri).ToString());
            return relative.Replace('/', '\\');
        }

        private static string NormalizeInclude(string include)
        {
            if (string.IsNullOrWhiteSpace(include))
            {
                return string.Empty;
            }

            return include.Trim().Replace('/', '\\');
        }

        private static XElement FindXamlItemGroup(XElement root)
        {
            return root
                .Elements()
                .Where(e => e.Name.LocalName == "ItemGroup")
                .FirstOrDefault(group => group.Elements().Any(e => string.Equals(e.Name.LocalName, XamlItemType, StringComparison.OrdinalIgnoreCase)));
        }

        private static XElement FindItemGroup(XElement root, string itemType)
        {
            return root
                .Elements()
                .Where(e => e.Name.LocalName == "ItemGroup")
                .FirstOrDefault(group => group.Elements().Any(e => string.Equals(e.Name.LocalName, itemType, StringComparison.OrdinalIgnoreCase)));
        }

        private static XElement AddItemGroup(XElement root)
        {
            XElement group = new XElement(root.Name.Namespace + "ItemGroup");
            string rootChildIndent = GetRootChildIndent(root);
            string rootClosingIndent = GetRootClosingIndent(root);

            // В legacy-проектах ItemGroup обязан идти до <Import> целей MSBuild.
            XElement lastImport = root.Elements().LastOrDefault(e => e.Name.LocalName == "Import");
            if (lastImport != null)
            {
                lastImport.AddBeforeSelf(group);
                group.AddAfterSelf(new XText(rootChildIndent));
                return group;
            }

            root.Add(new XText(rootChildIndent), group, new XText(rootClosingIndent));
            return group;
        }

        private static void AppendChild(XElement itemGroup, XElement newItem, XElement root)
        {
            string closingIndent = (itemGroup.Nodes().LastOrDefault() as XText)?.Value ?? GetRootChildIndent(root);
            XElement referenceItem = itemGroup.Elements().FirstOrDefault();
            string itemIndent = (referenceItem?.PreviousNode as XText)?.Value;

            if (itemIndent == null)
            {
                itemIndent = closingIndent + "    ";
            }

            // Отступы дочерних элементов берём у соседнего элемента, иначе новый .xaml выглядел бы
            // одной строкой посреди аккуратно отформатированного файла проекта.
            string childIndent = (referenceItem?.Elements().FirstOrDefault()?.PreviousNode as XText)?.Value;
            string childClosingIndent = (referenceItem?.Nodes().LastOrDefault() as XText)?.Value;
            if (childIndent == null || childClosingIndent == null)
            {
                childIndent = itemIndent + "    ";
                childClosingIndent = itemIndent;
            }

            ApplyChildIndentation(newItem, childIndent, childClosingIndent);

            XText closing = itemGroup.Nodes().LastOrDefault() as XText;
            if (closing != null)
            {
                closing.AddBeforeSelf(new XText(itemIndent), newItem);
            }
            else
            {
                itemGroup.Add(new XText(itemIndent), newItem, new XText(closingIndent));
            }
        }

        /// <summary>
        /// Раскладывает дочерние элементы нового элемента по строкам с теми же отступами,
        /// что и у соседних элементов файла проекта.
        /// </summary>
        private static void ApplyChildIndentation(XElement item, string childIndent, string closingIndent)
        {
            List<XNode> children = item.Nodes().ToList();
            if (children.Count == 0)
            {
                return;
            }

            item.RemoveNodes();
            foreach (XNode child in children)
            {
                item.Add(new XText(childIndent), child);
            }

            item.Add(new XText(closingIndent));
        }

        private static string GetRootChildIndent(XElement root)
        {
            XElement firstChild = root.Elements().FirstOrDefault();
            return (firstChild?.PreviousNode as XText)?.Value ?? "\r\n  ";
        }

        private static string GetRootClosingIndent(XElement root)
        {
            return (root.Nodes().LastOrDefault() as XText)?.Value ?? "\r\n";
        }

        private static void MoveUserFile(string oldProjectPath, string newProjectPath)
        {
            string oldUserPath = oldProjectPath + ".user";
            if (!File.Exists(oldUserPath))
            {
                return;
            }

            try
            {
                string newUserPath = newProjectPath + ".user";
                if (!File.Exists(newUserPath))
                {
                    File.Move(oldUserPath, newUserPath);
                }
            }
            catch (IOException)
            {
                // Пользовательский файл не критичен для загрузки проекта.
            }
            catch (UnauthorizedAccessException)
            {
                // Пользовательский файл не критичен для загрузки проекта.
            }
        }

        private static IEnumerable<string> FindSolutionFiles(string projectFolder)
        {
            List<string> solutions = new List<string>();
            DirectoryInfo directory = new DirectoryInfo(projectFolder);

            // Решение обычно лежит в папке проекта или на один-два уровня выше.
            for (int level = 0; level < 3 && directory != null; level++)
            {
                try
                {
                    solutions.AddRange(Directory.GetFiles(directory.FullName, "*.sln"));
                }
                catch (IOException)
                {
                    break;
                }
                catch (UnauthorizedAccessException)
                {
                    break;
                }

                directory = directory.Parent;
            }

            return solutions.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static string BuildPathPattern(string relativePath)
        {
            string[] parts = relativePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(@"[\\/]", parts.Select(Regex.Escape));
        }

        private static string AppendSeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static XDocument LoadPreservingFormat(string path)
        {
            return XDocument.Load(path, LoadOptions.PreserveWhitespace);
        }

        private static void SavePreservingFormat(XDocument document, string path)
        {
            Encoding encoding = DetectEncoding(path, out bool _);
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = encoding,
                Indent = false,
                OmitXmlDeclaration = document.Declaration == null,
                // Replace + актуальный перевод строки файла: NewLineHandling.None складывает CRLF в LF
                // и файл проекта после правки перестаёт совпадать с тем, что пишет Visual Studio.
                NewLineHandling = NewLineHandling.Replace,
                NewLineChars = DetectNewLine(path)
            };

            using (XmlWriter writer = XmlWriter.Create(path, settings))
            {
                document.Save(writer);
            }
        }

        private static string DetectNewLine(string path)
        {
            try
            {
                return File.ReadAllText(path).Contains("\r\n") ? "\r\n" : "\n";
            }
            catch (IOException)
            {
                return "\r\n";
            }
        }

        private static Encoding DetectEncoding(string path, out bool hasBom)
        {
            hasBom = false;

            try
            {
                byte[] header = new byte[3];
                using (FileStream stream = File.OpenRead(path))
                {
                    int read = stream.Read(header, 0, header.Length);
                    if (read == 3 && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF)
                    {
                        hasBom = true;
                    }
                }
            }
            catch (IOException)
            {
                // Не смогли прочитать заголовок — пишем в UTF-8 без BOM.
            }

            // Visual Studio пишет файлы проекта в UTF-8 с BOM; сохраняем привычный вид файла.
            return new UTF8Encoding(hasBom);
        }
    }
}
