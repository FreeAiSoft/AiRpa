using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using RPA.Core.Generation;
using RPA.Entities;
using RPA.ProjectModel;
using Project = RPA.Entities.Project;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Компилирует только C#-файлы проекта (code-behind) в in-memory сборку и строит
    /// словарь <c>Type.FullName → Type</c>. Нужен для резолва ссылок вида
    /// <c>TargetType="local:Code"</c> в loose-XAML без сборки проекта и без компиляции XAML.
    /// <para>
    /// Ошибки компиляции не бросаются: каталог остаётся пустым, диагностика сохраняется
    /// в <see cref="Diagnostics"/>. XAML по-прежнему не компилируется — каталог покрывает
    /// только code-behind ссылки на CLR-типы.
    /// </para>
    /// </summary>
    internal sealed class ProjectCodeTypeCatalog
    {
        private readonly Dictionary<string, Type> _types;
        private readonly List<Type> _typeList;
        private readonly List<string> _diagnostics;

        private ProjectCodeTypeCatalog(
            Dictionary<string, Type> types,
            List<Type> typeList,
            IEnumerable<string> diagnostics)
        {
            _types = types;
            _typeList = typeList;
            _diagnostics = diagnostics.ToList();
        }

        /// <summary>Скомпилированные типы по <c>Type.FullName</c>.</summary>
        internal int Count => _types.Count;

        /// <summary>Диагностика компиляции/загрузки (строки, удобные для логов).</summary>
        internal IReadOnlyList<string> Diagnostics => _diagnostics;

        /// <summary>Все скомпилированные типы (в порядке первого обнаружения).</summary>
        internal IReadOnlyList<Type> Types => _typeList;

        /// <summary>
        /// Строит каталог по <c>&lt;Compile Include&gt;</c> проекта. При отсутствии проекта
        /// или отсутствии <c>.cs</c>-файлов возвращает пустой каталог без исключений.
        /// </summary>
        internal static ProjectCodeTypeCatalog Build(Project project)
        {
            if (project == null)
            {
                return Empty();
            }

            List<CodeFile> codeFiles = ReadCodeFiles(project);

            if (codeFiles.Count == 0)
            {
                return Empty();
            }

            var diagnostics = new List<string>();

            var trees = new List<SyntaxTree>();
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);

            foreach (CodeFile codeFile in codeFiles)
            {
                trees.Add(CSharpSyntaxTree.ParseText(codeFile.Text, parseOptions, codeFile.Path));
            }

            string assemblyName = SyntheticAssemblyName(project, codeFiles);

            var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: false,
                assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default);

            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName,
                trees,
                CreateReferences(project),
                options);

            using (var stream = new MemoryStream())
            {
                EmitResult emit = compilation.Emit(stream);

                diagnostics.AddRange(emit.Diagnostics
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.ToString()));

                if (!emit.Success)
                {
                    return new ProjectCodeTypeCatalog(
                        new Dictionary<string, Type>(StringComparer.Ordinal),
                        new List<Type>(),
                        diagnostics);
                }

                Assembly assembly;

                try
                {
                    assembly = Assembly.Load(stream.ToArray());
                }
                catch (Exception ex)
                {
                    diagnostics.Add("Не удалось загрузить in-memory сборку code-behind: " + ex.Message);
                    return new ProjectCodeTypeCatalog(
                        new Dictionary<string, Type>(StringComparer.Ordinal),
                        new List<Type>(),
                        diagnostics);
                }

                (Dictionary<string, Type> types, List<Type> typeList) = IndexTypes(assembly, diagnostics);
                return new ProjectCodeTypeCatalog(types, typeList, diagnostics);
            }
        }

        internal bool TryGetType(string fullName, out Type type)
        {
            return _types.TryGetValue(fullName ?? string.Empty, out type);
        }

        private static ProjectCodeTypeCatalog Empty()
        {
            return new ProjectCodeTypeCatalog(
                new Dictionary<string, Type>(StringComparer.Ordinal),
                new List<Type>(),
                Array.Empty<string>());
        }

        private static List<CodeFile> ReadCodeFiles(Project project)
        {
            var codeFiles = new List<CodeFile>();

            if (project.Items == null || string.IsNullOrWhiteSpace(project.ProjectDirPath))
            {
                return codeFiles;
            }

            foreach (VsProjectItem item in project.Items)
            {
                if (item == null
                    || !string.Equals(item.ItemType, "Compile", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(item.Include)
                    || !item.Include.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string fullPath = Path.IsPathRooted(item.Include)
                    ? item.Include
                    : Path.Combine(project.ProjectDirPath, item.Include);

                if (IsExcluded(fullPath) || !File.Exists(fullPath))
                {
                    continue;
                }

                string text;

                try
                {
                    text = File.ReadAllText(fullPath);
                }
                catch (Exception)
                {
                    // Недоступный файл не должен ронять каталог: пропускаем.
                    continue;
                }

                codeFiles.Add(new CodeFile(fullPath, text));
            }

            return codeFiles;
        }

        private static bool IsExcluded(string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);

            // В AssemblyInfo.cs только assembly-атрибуты; типы из него не нужны и могут мешать.
            if (string.Equals(Path.GetFileName(fullPath), "AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string directory = Path.GetDirectoryName(fullPath);

            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            string[] segments = directory.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            return segments.Any(segment =>
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "packages", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, ".vs", StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<MetadataReference> CreateReferences(Project project)
        {
            var byName = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

            // Минимальный набор, достаточный для компиляции простого code-behind.
            foreach (string always in new[] { "mscorlib", "System", "System.Core" })
            {
                AddReference(always, null, project?.ProjectDirPath, byName);
            }

            if (project?.Items == null)
            {
                return byName.Values;
            }

            foreach (VsProjectItem item in project.Items)
            {
                if (item == null
                    || !string.Equals(item.ItemType, "Reference", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(item.Include))
                {
                    continue;
                }

                string simpleName = SimpleAssemblyName(item.Include);

                if (string.IsNullOrEmpty(simpleName))
                {
                    continue;
                }

                AddReference(simpleName, item.HintPath, project.ProjectDirPath, byName);
            }

            return byName.Values;
        }

        private static string SimpleAssemblyName(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return null;
            }

            string trimmed = reference.Trim();
            int comma = trimmed.IndexOf(',');

            string name = (comma >= 0 ? trimmed.Substring(0, comma) : trimmed).Trim();
            return name.Length == 0 ? null : name;
        }


        private static void AddReference(
            string name,
            string hintPath,
            string projectDir,
            Dictionary<string, MetadataReference> byName)
        {
            if (byName.ContainsKey(name))
            {
                return;
            }

            string location = ResolveHintPath(hintPath, projectDir);

            if (string.IsNullOrEmpty(location) || !File.Exists(location))
            {
                location = RoslynAssemblyCatalog.FindLocation(name);
            }

            if (string.IsNullOrEmpty(location) || !File.Exists(location))
            {
                return;
            }

            try
            {
                byName[name] = MetadataReference.CreateFromFile(location);
            }
            catch (Exception)
            {
                // Одна недоступная сборка не должна ронять каталог.
            }
        }

        private static string ResolveHintPath(string hintPath, string projectDir)
        {
            if (string.IsNullOrWhiteSpace(hintPath))
            {
                return null;
            }

            if (Path.IsPathRooted(hintPath))
            {
                return hintPath;
            }

            if (string.IsNullOrWhiteSpace(projectDir))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(Path.Combine(projectDir, hintPath));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string SyntheticAssemblyName(Project project, IReadOnlyList<CodeFile> codeFiles)
        {
            string root = project.RootNamespace;

            if (string.IsNullOrWhiteSpace(root))
            {
                root = project.AssemblyName;
            }

            if (string.IsNullOrWhiteSpace(root))
            {
                root = project.Name;
            }

            if (string.IsNullOrWhiteSpace(root))
            {
                root = "Project";
            }

            string key = string.Join(
                "\n",
                codeFiles.Select(codeFile => codeFile.Path + ":" + codeFile.Text.Length + ":" + Hash(codeFile.Text)));

            return "RpaLooseCode_" + SanitizeAssemblyName(root) + "_" + Hash(key);
        }

        private static string SanitizeAssemblyName(string name)
        {
            var builder = new StringBuilder(name.Length);

            foreach (char c in name)
            {
                builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            }

            string result = builder.ToString();

            if (result.Length == 0)
            {
                return "Project";
            }

            if (char.IsDigit(result[0]))
            {
                result = "_" + result;
            }

            return result;
        }

        private static string Hash(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                return BitConverter.ToString(bytes).Replace("-", string.Empty);
            }
        }

        private static (Dictionary<string, Type> Types, List<Type> TypeList) IndexTypes(
            Assembly assembly,
            List<string> diagnostics)
        {
            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types ?? Array.Empty<Type>();

                if (ex.LoaderExceptions != null)
                {
                    foreach (Exception loaderException in ex.LoaderExceptions)
                    {
                        if (loaderException != null)
                        {
                            diagnostics.Add("Тип code-behind не загружен: " + loaderException.Message);
                        }
                    }
                }
            }

            var dictionary = new Dictionary<string, Type>(StringComparer.Ordinal);
            var list = new List<Type>();

            foreach (Type type in types)
            {
                if (type == null || string.IsNullOrEmpty(type.FullName))
                {
                    continue;
                }

                // Первый победил: дубликат FullName не перезаписывает уже зарегистрированный тип.
                if (dictionary.ContainsKey(type.FullName))
                {
                    continue;
                }

                dictionary.Add(type.FullName, type);
                list.Add(type);
            }

            return (dictionary, list);
        }

        private sealed class CodeFile
        {
            internal CodeFile(string path, string text)
            {
                Path = path;
                Text = text;
            }

            internal string Path { get; }

            internal string Text { get; }
        }
    }
}
