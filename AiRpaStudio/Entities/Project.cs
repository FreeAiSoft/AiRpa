using Newtonsoft.Json;
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
