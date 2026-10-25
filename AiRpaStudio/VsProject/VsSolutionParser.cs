using System;
using System.IO;
using System.Text.RegularExpressions;

namespace RPA.VsProject
{
    /// <summary>Parses Visual Studio solution (.sln) files.</summary>
    public static class VsSolutionParser
    {
        private static readonly Regex ProjectLineRegex = new Regex(
            @"^\s*Project\(""(\{[^}]+\})""\)\s*=\s*""([^""]+)"",\s*""([^""]+)"",\s*""(\{[^}]+\})""",
            RegexOptions.Compiled);

        public static VsSolution Parse(string solutionPath)
        {
            if (string.IsNullOrWhiteSpace(solutionPath))
                throw new ArgumentException("Solution path is required.", nameof(solutionPath));

            string fullPath = Path.GetFullPath(solutionPath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Solution file not found.", fullPath);

            var solution = new VsSolution
            {
                SolutionPath = fullPath,
                Name = Path.GetFileNameWithoutExtension(fullPath)
            };

            foreach (string line in File.ReadAllLines(fullPath))
            {
                Match match = ProjectLineRegex.Match(line);
                if (!match.Success)
                    continue;

                solution.Projects.Add(new VsSolutionProject
                {
                    TypeGuid = match.Groups[1].Value,
                    Name = match.Groups[2].Value,
                    RelativePath = match.Groups[3].Value,
                    ProjectGuid = match.Groups[4].Value
                });
            }

            return solution;
        }

        /// <summary>True if the project entry points to a C# project file.</summary>
        public static bool IsCsproj(VsSolutionProject project)
        {
            if (project == null || string.IsNullOrWhiteSpace(project.RelativePath))
                return false;

            return project.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
        }
    }
}
