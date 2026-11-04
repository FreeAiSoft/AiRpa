namespace RPA
{
    internal class Consts
    {
        public const string defaultMainActivityFileName = "Main.xaml";
        public const string projectFileName = "project.json";
        public const string defaultProjectsFolderName = "AiRpaStudio";
        public const string defaultProjectDescription = "Empty process";
        public const string defaultProjectName = "Process";
        public const string defaultActivityName = "Activity";

        // Visual Studio templates
        public const string vsTemplatesFolderName = "VSTemplates";
        public const string vsProjectTemplateFolderName = "ActivityLibrary2010";
        public const string vsItemTemplateFolderName = "Activity";
        public const string vsProjectTemplateFileName = "ActivityLibrary.vstemplate";
        public const string vsItemTemplateFileName = "Activity.vstemplate";

        // File filters
        public const string solutionFileFilter = "*.sln";
        public const string projectFileFilter = "*.csproj";
        public const string vsTemplateFileFilter = "*.vstemplate";

        // Target framework versions
        public const string defaultTargetFrameworkVersion = "4.7.2";
        public static readonly string[] targetFrameworkVersions = new string[] { "4.7.2", "4.8", "4.8.1", "4.7" };
    }
}
