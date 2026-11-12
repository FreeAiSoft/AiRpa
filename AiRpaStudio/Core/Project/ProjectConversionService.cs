using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json;
using RPA.Entities;
using RPA.Enums;
using RPA.VsProject;
using RPA.VsTemplate;

namespace RPA.ProjectModel
{
    /// <summary>Параметры конвертации единого проекта между форматами.</summary>
    public class ProjectConversionOptions
    {
        /// <summary>Генерировать файл решения (.sln) при конвертации во встроенный → VS.</summary>
        public bool GenerateSolution { get; set; } = true;

        /// <summary>TFM для генерируемого .csproj (формат "4.7.2"); по умолчанию CP-10.</summary>
        public string TargetFrameworkVersion { get; set; } = Consts.defaultTargetFrameworkVersion;
    }

    /// <summary>Результат конвертации: список ошибок и пути созданных файлов.</summary>
    public class ProjectConversionResult
    {
        public bool Success => Errors.Count == 0;

        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>Путь к .csproj (конвертация в VS).</summary>
        public string ProjectFilePath { get; set; }

        /// <summary>Путь к .sln (конвертация в VS, если включена генерация решения).</summary>
        public string SolutionFilePath { get; set; }

        /// <summary>Путь к project.json (конвертация во встроенный формат).</summary>
        public string ProjectJsonPath { get; set; }
    }

    /// <summary>
    /// Программная конвертация одного и того же объекта <see cref="Project"/> между встроенным
    /// форматом (<c>project.json</c>) и Visual Studio (<c>.csproj</c>/<c>.sln</c>).
    /// <para>
    /// Целевой формат .csproj — legacy Windows Workflow Foundation-проект (ToolsVersion +
    /// TargetFrameworkVersion + ProjectTypeGuids WF + XamlAppDef), как в шаблоне ActivityLibrary2010.
    /// VS-проект всегда является activity library: <see cref="ProjectType.Library"/>.
    /// </para>
    /// </summary>
    public static class ProjectConversionService
    {
        private const string WorkflowProjectTypeGuid = "32f31d43-81cc-4c15-9de6-3fc5453562b6";
        private const string CSharpProjectTypeGuid = "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";
        private const string CSharpSlnProjectTypeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";

        private static readonly XNamespace MsBuildNs = "http://schemas.microsoft.com/developer/msbuild/2003";

        private static readonly string[] ReferenceNames =
        {
            "Microsoft.CSharp",
            "PresentationCore",
            "PresentationFramework",
            "WindowsBase",
            "System",
            "System.Activities",
            "System.Activities.Core.Presentation",
            "System.Activities.Presentation",
            "System.ComponentModel.Composition",
            "System.Core",
            "System.Data",
            "System.Runtime.Serialization",
            "System.ServiceModel",
            "System.ServiceModel.Activities",
            "System.Xaml",
            "System.Xml",
            "System.Xml.Linq"
        };

        private static readonly HashSet<string> IgnoredDirectories =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".vs", "packages" };

        /// <summary>Конвертирует встроенный проект (project.json) в проект Visual Studio (.csproj + .sln).</summary>
        public static ProjectConversionResult ConvertToVsProject(Project project, ProjectConversionOptions options = null)
        {
            options = options ?? new ProjectConversionOptions();
            var result = new ProjectConversionResult();

            string error = Validate(project);
            if (error != null)
            {
                result.Errors.Add(error);
                return result;
            }

            string projectDir = project.ProjectDirPath;
            string safeName = VsTemplateParameterResolver.MakeSafeProjectName(project.Name);
            string csprojPath = Path.Combine(projectDir, safeName + ".csproj");
            string projectJsonPath = Path.Combine(projectDir, Consts.projectFileName);

            if (File.Exists(csprojPath))
            {
                if (project.Format == ProjectFormat.VsProject
                    && string.Equals(project.ProjectFilePath, csprojPath, StringComparison.OrdinalIgnoreCase))
                {
                    // Уже сконвертирован: идемпотентный повтор.
                    result.ProjectFilePath = csprojPath;
                    result.SolutionFilePath = project.SolutionFilePath;
                    result.ProjectJsonPath = projectJsonPath;
                    return result;
                }

                result.Errors.Add($"Файл проекта {safeName}.csproj уже существует в папке проекта.");
                return result;
            }

            var xamlFiles = new List<string>();
            var csFiles = new List<string>();
            var noneFiles = new List<string>();
            CollectFiles(projectDir, projectDir, xamlFiles, csFiles, noneFiles);

            // AssemblyInfo.cs создаётся ниже, но обязан попасть в список Compile.
            string assemblyInfoInclude = @"Properties\AssemblyInfo.cs";
            if (!csFiles.Contains(assemblyInfoInclude, StringComparer.OrdinalIgnoreCase))
            {
                csFiles.Add(assemblyInfoInclude);
            }

            string tvm = NormalizeTargetFrameworkVersion(options.TargetFrameworkVersion);
            string projectGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();

            XDocument csproj = BuildCsproj(safeName, tvm, projectGuid, xamlFiles, csFiles, noneFiles);
            csproj.Save(csprojPath);

            WriteAssemblyInfoIfMissing(projectDir, safeName, projectGuid);

            string slnPath = null;
            if (options.GenerateSolution)
            {
                slnPath = Path.Combine(projectDir, safeName + ".sln");
                File.WriteAllText(slnPath, BuildSolution(safeName, safeName + ".csproj", projectGuid));
            }

            // Единый объект: дополняем тем же экземпляром, второй объект не создаём.
            project.Format = ProjectFormat.VsProject;
            project.ProjectFilePath = csprojPath;
            project.SolutionFilePath = slnPath;
            project.ProjectType = ProjectType.Library; // VS-проект — всегда library.
            project.RootNamespace = safeName;
            project.AssemblyName = safeName;
            project.OutputType = "Library";
            project.TargetFrameworkVersion = "v" + tvm;
            project.TargetFramework = VsProjectParser.NormalizeTargetFramework("v" + tvm);
            project.ProjectGuid = projectGuid;
            project.IsSdkStyle = false;
            project.IsWorkflowProject = true;

            // project.json синхронизируем с объектом (ProjectType=Library, TFM), чтобы оба
            // представления описывали один и тот же проект.
            File.WriteAllText(projectJsonPath, JsonConvert.SerializeObject(project, Formatting.Indented));

            result.ProjectFilePath = csprojPath;
            result.SolutionFilePath = slnPath;
            result.ProjectJsonPath = projectJsonPath;

            return result;
        }

        /// <summary>Конвертирует проект Visual Studio (.csproj) во встроенный формат (project.json).</summary>
        public static ProjectConversionResult ConvertToNative(Project project)
        {
            var result = new ProjectConversionResult();

            string error = Validate(project);
            if (error != null)
            {
                result.Errors.Add(error);
                return result;
            }

            string projectDir = project.ProjectDirPath;
            string projectJsonPath = Path.Combine(projectDir, Consts.projectFileName);

            // VS-проект всегда library: эвристик «есть Main.xaml → Process» нет.
            project.ProjectType = ProjectType.Library;
            project.Format = ProjectFormat.Native;
            project.ProjectFilePath = null;
            project.SolutionFilePath = null;

            File.WriteAllText(projectJsonPath, JsonConvert.SerializeObject(project, Formatting.Indented));

            result.ProjectJsonPath = projectJsonPath;
            return result;
        }

        private static string Validate(Project project)
        {
            if (project == null)
            {
                return "Проект не указан.";
            }

            if (string.IsNullOrWhiteSpace(project.Name))
            {
                return "Имя проекта не указано.";
            }

            if (string.IsNullOrWhiteSpace(project.ProjectDirPath) || !Directory.Exists(project.ProjectDirPath))
            {
                return "Папка проекта не найдена.";
            }

            return null;
        }

        private static string NormalizeTargetFrameworkVersion(string raw)
        {
            string value = (raw ?? Consts.defaultTargetFrameworkVersion).Trim().TrimStart('v', 'V');
            return string.IsNullOrWhiteSpace(value) ? Consts.defaultTargetFrameworkVersion : value;
        }

        private static void CollectFiles(string root, string current, List<string> xamlFiles, List<string> csFiles, List<string> noneFiles)
        {
            foreach (string dir in Directory.GetDirectories(current))
            {
                if (IgnoredDirectories.Contains(Path.GetFileName(dir)))
                {
                    continue;
                }

                CollectFiles(root, dir, xamlFiles, csFiles, noneFiles);
            }

            foreach (string file in Directory.GetFiles(current))
            {
                if (IsIgnoredFile(file))
                {
                    continue;
                }

                string relative = VsProjectWriter.GetRelativeIncludePath(root, file);
                string extension = Path.GetExtension(file);

                if (string.Equals(extension, ".xaml", StringComparison.OrdinalIgnoreCase))
                {
                    xamlFiles.Add(relative);
                }
                else if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
                {
                    csFiles.Add(relative);
                }
                else
                {
                    noneFiles.Add(relative);
                }
            }
        }

        private static bool IsIgnoredFile(string file)
        {
            string fileName = Path.GetFileName(file);

            if (string.Equals(fileName, Consts.projectFileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string extension = Path.GetExtension(file);
            return string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
                   || file.EndsWith(".xaml.breakpoints", StringComparison.OrdinalIgnoreCase);
        }

        private static XDocument BuildCsproj(string safeName, string targetFrameworkVersion, string projectGuid, List<string> xamlFiles, List<string> csFiles, List<string> noneFiles)
        {
            XNamespace ns = MsBuildNs;

            var root = new XElement(ns + "Project",
                new XAttribute("ToolsVersion", "15.0"),
                new XAttribute("DefaultTargets", "Build"));

            root.Add(new XElement(ns + "PropertyGroup",
                new XElement(ns + "Configuration", new XAttribute("Condition", " '$(Configuration)' == '' "), "Debug"),
                new XElement(ns + "Platform", new XAttribute("Condition", " '$(Platform)' == '' "), "AnyCPU"),
                new XElement(ns + "ProjectGuid", projectGuid),
                new XElement(ns + "ProjectTypeGuids", "{" + WorkflowProjectTypeGuid + "};{" + CSharpProjectTypeGuid + "}"),
                new XElement(ns + "OutputType", "Library"),
                new XElement(ns + "AppDesignerFolder", "Properties"),
                new XElement(ns + "RootNamespace", safeName),
                new XElement(ns + "AssemblyName", safeName),
                new XElement(ns + "TargetFrameworkVersion", "v" + targetFrameworkVersion),
                new XElement(ns + "FileAlignment", "512")));

            root.Add(new XElement(ns + "PropertyGroup",
                new XAttribute("Condition", " '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' "),
                new XElement(ns + "DebugSymbols", "true"),
                new XElement(ns + "DebugType", "full"),
                new XElement(ns + "Optimize", "false"),
                new XElement(ns + "OutputPath", @"bin\Debug\"),
                new XElement(ns + "DefineConstants", "DEBUG;TRACE"),
                new XElement(ns + "ErrorReport", "prompt"),
                new XElement(ns + "WarningLevel", "4")));

            root.Add(new XElement(ns + "PropertyGroup",
                new XAttribute("Condition", " '$(Configuration)|$(Platform)' == 'Release|AnyCPU' "),
                new XElement(ns + "DebugType", "pdbonly"),
                new XElement(ns + "Optimize", "true"),
                new XElement(ns + "OutputPath", @"bin\Release\"),
                new XElement(ns + "DefineConstants", "TRACE"),
                new XElement(ns + "ErrorReport", "prompt"),
                new XElement(ns + "WarningLevel", "4")));

            var references = new XElement(ns + "ItemGroup");
            foreach (string reference in ReferenceNames)
            {
                references.Add(new XElement(ns + "Reference", new XAttribute("Include", reference)));
            }

            root.Add(references);

            var compile = new XElement(ns + "ItemGroup");
            foreach (string cs in csFiles)
            {
                compile.Add(new XElement(ns + "Compile", new XAttribute("Include", cs)));
            }

            root.Add(compile);

            var xaml = new XElement(ns + "ItemGroup");
            foreach (string xamlFile in xamlFiles)
            {
                xaml.Add(new XElement(ns + "XamlAppDef", new XAttribute("Include", xamlFile),
                    new XElement(ns + "SubType", "Designer"),
                    new XElement(ns + "Generator", "MSBuild:Compile")));
            }

            root.Add(xaml);

            if (noneFiles.Count > 0)
            {
                var none = new XElement(ns + "ItemGroup");
                foreach (string file in noneFiles)
                {
                    none.Add(new XElement(ns + "None", new XAttribute("Include", file)));
                }

                root.Add(none);
            }

            root.Add(new XElement(ns + "Import",
                new XAttribute("Project", @"$(MSBuildToolsPath)\Microsoft.CSharp.targets")));

            return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }

        private static void WriteAssemblyInfoIfMissing(string projectDir, string safeName, string projectGuid)
        {
            string propertiesDir = Path.Combine(projectDir, "Properties");
            if (!Directory.Exists(propertiesDir))
            {
                Directory.CreateDirectory(propertiesDir);
            }

            string assemblyInfoPath = Path.Combine(propertiesDir, "AssemblyInfo.cs");
            if (File.Exists(assemblyInfoPath))
            {
                return;
            }

            string guidD = projectGuid.Trim('{', '}');
            var sb = new StringBuilder();
            sb.AppendLine("using System.Reflection;");
            sb.AppendLine("using System.Runtime.InteropServices;");
            sb.AppendLine();
            sb.AppendLine("[assembly: AssemblyTitle(\"" + safeName + "\")]");
            sb.AppendLine("[assembly: AssemblyDescription(\"\")]");
            sb.AppendLine("[assembly: AssemblyConfiguration(\"\")]");
            sb.AppendLine("[assembly: AssemblyCompany(\"\")]");
            sb.AppendLine("[assembly: AssemblyProduct(\"" + safeName + "\")]");
            sb.AppendLine("[assembly: AssemblyCopyright(\"\")]");
            sb.AppendLine("[assembly: AssemblyTrademark(\"\")]");
            sb.AppendLine("[assembly: AssemblyCulture(\"\")]");
            sb.AppendLine();
            sb.AppendLine("[assembly: ComVisible(false)]");
            sb.AppendLine();
            sb.AppendLine("[assembly: Guid(\"" + guidD + "\")]");
            sb.AppendLine();
            sb.AppendLine("[assembly: AssemblyVersion(\"1.0.0.0\")]");
            sb.AppendLine("[assembly: AssemblyFileVersion(\"1.0.0.0\")]");

            File.WriteAllText(assemblyInfoPath, sb.ToString());
        }

        private static string BuildSolution(string solutionName, string csprojRelativePath, string projectGuid)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
            sb.AppendLine("# Visual Studio Version 17");
            sb.AppendLine("VisualStudioVersion = 17.0.31903.59");
            sb.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");
            sb.AppendLine("Project(\"" + CSharpSlnProjectTypeGuid + "\") = \"" + solutionName + "\", \"" + csprojRelativePath + "\", \"" + projectGuid + "\"");
            sb.AppendLine("EndProject");
            sb.AppendLine("Global");
            sb.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
            sb.AppendLine("\t\tDebug|Any CPU = Debug|Any CPU");
            sb.AppendLine("\t\tRelease|Any CPU = Release|Any CPU");
            sb.AppendLine("\tEndGlobalSection");
            sb.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
            sb.AppendLine("\t\t" + projectGuid + ".Debug|Any CPU.ActiveCfg = Debug|Any CPU");
            sb.AppendLine("\t\t" + projectGuid + ".Debug|Any CPU.Build.0 = Debug|Any CPU");
            sb.AppendLine("\t\t" + projectGuid + ".Release|Any CPU.ActiveCfg = Release|Any CPU");
            sb.AppendLine("\t\t" + projectGuid + ".Release|Any CPU.Build.0 = Release|Any CPU");
            sb.AppendLine("\tEndGlobalSection");
            sb.AppendLine("\tGlobalSection(SolutionProperties) = preSolution");
            sb.AppendLine("\t\tHideSolutionNode = FALSE");
            sb.AppendLine("\tEndGlobalSection");
            sb.AppendLine("EndGlobal");
            return sb.ToString();
        }
    }
}
