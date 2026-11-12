using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using RPA.Entities;
using RPA.Enums;
using RPA.Helpers;
using RPA.VsProject;

namespace RPA.ProjectModel
{
    /// <summary>Контейнер решения: .sln — это список проектов, а не сам проект.</summary>
    public class SolutionWorkspace
    {
        public string SolutionPath { get; set; }

        /// <summary>Единые доменные объекты проектов, входящих в решение (в порядке .sln).</summary>
        public List<Project> Projects { get; set; } = new List<Project>();
    }

    /// <summary>Результат единой загрузки проекта/решения и его дерева.</summary>
    public class ProjectLoadResult
    {
        /// <summary>Загруженный проект (null для .sln — там <see cref="Solution"/>).</summary>
        public Project Project { get; set; }

        /// <summary>Контейнер решения (не null для .sln).</summary>
        public SolutionWorkspace Solution { get; set; }

        /// <summary>Корневой узел дерева для отображения в ProjectManager.</summary>
        public FSNode Root { get; set; }

        /// <summary>Папка проекта/решения.</summary>
        public string ProjectFolder { get; set; }

        /// <summary>Нефатальные предупреждения парсинга (например, отсутствующий файл проекта в .sln).</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        public string Error { get; set; }

        public bool Success => Error == null;
    }

    /// <summary>
    /// Единая точка загрузки <see cref="Project"/> и дерева из любого источника:
    /// папка с <c>project.json</c> → native-проект; <c>.csproj</c> → VS-проект;
    /// <c>.sln</c> → контейнер <see cref="SolutionWorkspace"/> со списком проектов.
    /// </summary>
    public static class ProjectLoader
    {
        public static ProjectLoadResult Load(string path)
        {
            var result = new ProjectLoadResult();

            if (string.IsNullOrWhiteSpace(path))
            {
                result.Error = "Путь к проекту не указан.";
                return result;
            }

            // Папка с project.json — встроенный (native) проект.
            if (Directory.Exists(path))
            {
                return LoadNative(path);
            }

            if (!File.Exists(path))
            {
                result.Error = $"Файл или папка {path} не найдены.";
                return result;
            }

            if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                return LoadSolution(path);
            }

            if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return LoadVsProject(path);
            }

            result.Error = "Поддерживаются только папка с project.json, .csproj и .sln.";
            return result;
        }

        private static ProjectLoadResult LoadNative(string dirPath)
        {
            var result = new ProjectLoadResult { ProjectFolder = dirPath };

            string jsonPath = Path.Combine(dirPath, Consts.projectFileName);
            if (!File.Exists(jsonPath))
            {
                result.Error = $"Файл проекта {Consts.projectFileName} не найден.";
                return result;
            }

            Project project = ReadProject(jsonPath) ?? new Project();
            project.Format = ProjectFormat.Native;
            project.SetProjectDir(dirPath);

            FSNode root = new FSNode(dirPath, FSNodeType.Project) { Project = project };
            BuildNativeTree(root);

            result.Project = project;
            result.Root = root;
            return result;
        }

        private static ProjectLoadResult LoadVsProject(string projectPath)
        {
            var result = new ProjectLoadResult { ProjectFolder = Path.GetDirectoryName(projectPath) };

            FSNode root = VsProjectLoader.LoadProject(projectPath);

            result.Project = root.Project;
            result.Root = root;
            return result;
        }

        private static ProjectLoadResult LoadSolution(string solutionPath)
        {
            var result = new ProjectLoadResult { ProjectFolder = Path.GetDirectoryName(solutionPath) };

            FSNode root = VsProjectLoader.LoadSolution(solutionPath, out List<string> warnings);
            result.Warnings = new List<string>(warnings);

            var solution = new SolutionWorkspace { SolutionPath = solutionPath };
            CollectProjects(root, solution.Projects);

            result.Solution = solution;
            result.Root = root;
            return result;
        }

        private static void CollectProjects(FSNode node, List<Project> projects)
        {
            if (node == null)
            {
                return;
            }

            if (node.FSNodeType == FSNodeType.VsProject && node.Project != null)
            {
                projects.Add(node.Project);
            }

            if (node.Childs != null)
            {
                foreach (FSNode child in node.Childs)
                {
                    CollectProjects(child, projects);
                }
            }
        }

        private static Project ReadProject(string jsonPath)
        {
            try
            {
                string json = File.ReadAllText(jsonPath);
                return JsonConvert.DeserializeObject<Project>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Строит файловое дерево native-проекта: папки первыми, затем файлы (как в
        /// <c>ProjectManager.SetChilds</c>), игнорируя .xaml.breakpoints.
        /// </summary>
        private static void BuildNativeTree(FSNode node)
        {
            List<FSNode> children = Directory.GetDirectories(node.RuntimeFullPath)
                .Select(d => new FSNode(d, FSNodeType.Dir))
                .ToList();

            foreach (FSNode child in children)
            {
                BuildNativeTree(child);
            }

            List<FSNode> files = Directory.GetFiles(node.RuntimeFullPath)
                .Where(f => !f.EndsWith(".xaml.breakpoints", StringComparison.OrdinalIgnoreCase))
                .Select(d => new FSNode(d, FSNodeType.File))
                .ToList();

            children.AddRange(files);
            children.Sort(new FSNodeComparer());

            node.Childs = new ObservableCollection<FSNode>(children);
        }
    }
}
