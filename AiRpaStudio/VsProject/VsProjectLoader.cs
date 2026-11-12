using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using RPA.Entities;
using RPA.Enums;
using RPA.Helpers;
using RPA.ProjectModel;

namespace RPA.VsProject
{
    /// <summary>Builds an FSNode tree from .sln/.csproj files.</summary>
    public static class VsProjectLoader
    {
        /// <summary>Loads a solution into a tree; parse problems are dropped (use the overload with warnings).</summary>
        public static FSNode LoadSolution(string solutionPath)
        {
            return LoadSolution(solutionPath, out _);
        }

        /// <summary>
        /// Loads a solution into a tree that mirrors the .sln: solution folders become
        /// <see cref="FSNodeType.SolutionFolder"/> nodes (nesting from NestedProjects), projects keep their
        /// order, and a project whose file is missing stays in the tree with
        /// <see cref="FSNode.IsMissing"/> = true and a warning instead of disappearing silently (CP-06).
        /// </summary>
        public static FSNode LoadSolution(string solutionPath, out List<string> warnings)
        {
            VsSolution solution = VsSolutionParser.Parse(solutionPath);
            warnings = new List<string>(solution.Warnings);

            FSNode root = new FSNode(solutionPath, FSNodeType.Solution)
            {
                Childs = new ObservableCollection<FSNode>()
            };

            var nodes = new Dictionary<string, FSNode>(StringComparer.OrdinalIgnoreCase);

            foreach (VsSolutionProject project in solution.Projects)
            {
                FSNode node = CreateNode(project, warnings);

                if (node == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(project.ProjectGuid))
                {
                    nodes[project.ProjectGuid] = node;
                }
            }

            foreach (VsSolutionProject project in solution.Projects)
            {
                if (!nodes.TryGetValue(project.ProjectGuid ?? string.Empty, out FSNode node))
                {
                    continue;
                }

                FSNode parent = root;

                if (!string.IsNullOrWhiteSpace(project.ParentGuid)
                    && nodes.TryGetValue(project.ParentGuid, out FSNode parentNode))
                {
                    parent = parentNode;
                }

                parent.Childs = parent.Childs ?? new ObservableCollection<FSNode>();
                parent.Childs.Add(node);
            }

            return root;
        }

        public static FSNode LoadProject(string projectPath)
        {
            VsProjectInfo info = VsProjectParser.Parse(projectPath);
            ObservableCollection<FSNode> children = BuildFileTree(Path.GetDirectoryName(projectPath), projectPath);

            // Узел зависимостей идёт первым, как «References» в дереве проекта Visual Studio.
            FSNode references = BuildReferencesNode(info);
            if (references != null)
            {
                children.Insert(0, references);
            }

            Project project = ProjectMapper.FromVsProjectInfo(info);
            project.SetProjectDir(Path.GetDirectoryName(projectPath));

            FSNode root = new FSNode(projectPath, FSNodeType.VsProject)
            {
                Name = info.Name,
                Project = project,
                Childs = children
            };

            return root;
        }

        /// <summary>
        /// Собирает виртуальный узел «References» с зависимостями проекта: NuGet-пакеты
        /// (PackageReference/packages.config), затем ProjectReference и Reference из .csproj.
        /// У синтетических узлов нет файла на диске, поэтому <see cref="FSNode.RuntimeFullPath"/> = null.
        /// </summary>
        private static FSNode BuildReferencesNode(VsProjectInfo info)
        {
            var children = new ObservableCollection<FSNode>();

            foreach (VsProjectPackage package in info.Packages)
            {
                string name = string.IsNullOrWhiteSpace(package.Version)
                    ? package.Id
                    : package.Id + " (" + package.Version + ")";

                children.Add(new FSNode(null, FSNodeType.Reference) { Name = name });
            }

            foreach (VsProjectItem item in info.Items)
            {
                if (string.Equals(item.ItemType, "ProjectReference", StringComparison.OrdinalIgnoreCase))
                {
                    children.Add(new FSNode(null, FSNodeType.Reference) { Name = Path.GetFileNameWithoutExtension(item.Include) });
                }
                else if (string.Equals(item.ItemType, "Reference", StringComparison.OrdinalIgnoreCase))
                {
                    children.Add(new FSNode(null, FSNodeType.Reference) { Name = item.Include });
                }
            }

            return new FSNode(null, FSNodeType.References)
            {
                Name = "References",
                Childs = children
            };
        }

        private static FSNode CreateNode(VsSolutionProject project, List<string> warnings)
        {
            if (project.IsSolutionFolder)
            {
                string folderName = string.IsNullOrWhiteSpace(project.Name)
                    ? Path.GetFileName(project.RelativePath ?? string.Empty)
                    : project.Name;

                return new FSNode(folderName, FSNodeType.SolutionFolder)
                {
                    Name = folderName,
                    Childs = new ObservableCollection<FSNode>()
                };
            }

            if (!VsSolutionParser.IsCsproj(project))
            {
                warnings.Add("пропущен не-C# проект решения: " + project.Name);
                return null;
            }

            if (!project.FileExists || string.IsNullOrWhiteSpace(project.FullPath))
            {
                warnings.Add("файл проекта не найден: " + project.RelativePath + " — узел оставлен в дереве с пометкой");

                return new FSNode(project.FullPath ?? project.RelativePath, FSNodeType.VsProject)
                {
                    Name = project.Name,
                    IsMissing = true,
                    Childs = new ObservableCollection<FSNode>()
                };
            }

            return LoadProject(project.FullPath);
        }

        private static ObservableCollection<FSNode> BuildFileTree(string directory, string excludeFilePath)
        {
            List<FSNode> nodes = new List<FSNode>();
            HashSet<string> ignoredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".vs", "packages" };

            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return new ObservableCollection<FSNode>(nodes);
            }

            foreach (string dir in Directory.GetDirectories(directory))
            {
                string dirName = Path.GetFileName(dir);
                if (ignoredDirs.Contains(dirName))
                {
                    continue;
                }

                nodes.Add(new FSNode(dir, FSNodeType.Dir)
                {
                    Childs = BuildFileTree(dir, excludeFilePath)
                });
            }

            foreach (string file in Directory.GetFiles(directory))
            {
                if (file.EndsWith(".xaml.breakpoints", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(excludeFilePath)
                    && string.Equals(file, excludeFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                nodes.Add(new FSNode(file, FSNodeType.File));
            }

            nodes.Sort(new FSNodeComparer());
            return new ObservableCollection<FSNode>(nodes);
        }
    }
}
