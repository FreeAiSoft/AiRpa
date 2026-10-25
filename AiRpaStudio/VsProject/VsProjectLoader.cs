using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using RPA.Entities;
using RPA.Enums;
using RPA.Helpers;

namespace RPA.VsProject
{
    /// <summary>Builds an FSNode tree from .sln/.csproj files.</summary>
    public static class VsProjectLoader
    {
        public static FSNode LoadSolution(string solutionPath)
        {
            VsSolution solution = VsSolutionParser.Parse(solutionPath);
            var root = new FSNode(solutionPath, FSNodeType.Solution)
            {
                Childs = new ObservableCollection<FSNode>()
            };

            string solutionDir = Path.GetDirectoryName(solutionPath);

            foreach (VsSolutionProject project in solution.Projects)
            {
                if (!VsSolutionParser.IsCsproj(project))
                    continue;

                string projectPath = Path.GetFullPath(Path.Combine(solutionDir, project.RelativePath));
                if (!File.Exists(projectPath))
                    continue;

                root.Childs.Add(LoadProject(projectPath));
            }

            return root;
        }

        public static FSNode LoadProject(string projectPath)
        {
            VsProjectInfo info = VsProjectParser.Parse(projectPath);
            var root = new FSNode(projectPath, FSNodeType.VsProject)
            {
                Name = info.Name,
                Childs = BuildFileTree(Path.GetDirectoryName(projectPath), projectPath)
            };

            return root;
        }

        private static ObservableCollection<FSNode> BuildFileTree(string directory, string excludeFilePath)
        {
            var nodes = new List<FSNode>();
            var ignoredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".vs", "packages" };

            foreach (string dir in Directory.GetDirectories(directory))
            {
                string dirName = Path.GetFileName(dir);
                if (ignoredDirs.Contains(dirName))
                    continue;

                nodes.Add(new FSNode(dir, FSNodeType.Dir)
                {
                    Childs = BuildFileTree(dir, excludeFilePath)
                });
            }

            foreach (string file in Directory.GetFiles(directory))
            {
                if (file.EndsWith(".xaml.breakpoints", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(excludeFilePath)
                    && string.Equals(file, excludeFilePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                nodes.Add(new FSNode(file, FSNodeType.File));
            }

            nodes.Sort(new FSNodeComparer());
            return new ObservableCollection<FSNode>(nodes);
        }
    }
}
