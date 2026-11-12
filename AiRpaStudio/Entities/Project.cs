using System.Collections.Generic;
using Newtonsoft.Json;
using RPA.ProjectModel;
using RPA.Enums;

namespace RPA.Entities
{
    public class Project
    {
        public string Name { get; set; }

        public string MainActivity { get; set; }

        public string Description { get; set; }

        public ProjectType ProjectType { get; set; }

        /// <summary>
        /// Load from dir
        /// </summary>
        [JsonIgnore]
        public string ProjectDirPath { get; private set; }

        /// <summary>Формат проекта: встроенный (<c>project.json</c>) или Visual Studio (<c>.csproj</c>).</summary>
        [JsonIgnore]
        public ProjectFormat Format { get; set; }

        /// <summary>Путь к файлу проекта Visual Studio (<c>.csproj</c>); null у встроенного проекта до конвертации.</summary>
        [JsonIgnore]
        public string ProjectFilePath { get; set; }

        /// <summary>Путь к файлу решения (<c>.sln</c>), если проект входит в решение.</summary>
        [JsonIgnore]
        public string SolutionFilePath { get; set; }

        // --- Метаданные Visual Studio (маппятся из/в .csproj). Не сериализуются в project.json:
        // встроенный project.json остаётся минимальным, детали проекта живут в .csproj. ---

        [JsonIgnore]
        public string RootNamespace { get; set; }

        [JsonIgnore]
        public string AssemblyName { get; set; }

        [JsonIgnore]
        public string OutputType { get; set; }

        /// <summary>
        /// TFM в формате <c>.csproj</c> (<c>"v4.7.2"</c>). Единственное VS-поле, попадающее в
        /// <c>project.json</c>: переживает round-trip конвертации (CP-10).
        /// </summary>
        public string TargetFrameworkVersion { get; set; }

        [JsonIgnore]
        public string TargetFramework { get; set; }

        [JsonIgnore]
        public string ProjectGuid { get; set; }

        [JsonIgnore]
        public bool IsSdkStyle { get; set; }

        [JsonIgnore]
        public bool IsWorkflowProject { get; set; }

        [JsonIgnore]
        public List<VsProjectPackage> Packages { get; set; } = new List<VsProjectPackage>();

        [JsonIgnore]
        public List<VsProjectItem> Items { get; set; } = new List<VsProjectItem>();

        public Project()
        {
        }

        public Project(ProjectType projectType)
        {
            ProjectType = projectType;
        }

        public void SetProjectDir(string projectDirPath)
        {
            ProjectDirPath = projectDirPath;
            ActivityLibraryBase.Helpers.DesignerHelper.ProjectDirPath = projectDirPath;
        }
    }
}
