using System.Collections.Generic;

namespace RPA.VsTemplate
{
    public enum VsTemplateType
    {
        Project,
        Item
    }

    /// <summary>Represents a single file to be generated from a template.</summary>
    public class VsTemplateFile
    {
        /// <summary>Source file path relative to the template root (e.g. "Activity1.xaml").</summary>
        public string SourceFile { get; set; }

        /// <summary>Target file name; may contain template parameters ($safeprojectname$, etc.). Null means use the source file name.</summary>
        public string TargetFileName { get; set; }

        /// <summary>Whether the file content should undergo parameter/condition substitution.</summary>
        public bool ReplaceParameters { get; set; }

        /// <summary>Whether the file should be opened in the editor after creation.</summary>
        public bool OpenInEditor { get; set; }

        /// <summary>Target sub-folder (may contain parameters); null/empty for the project root.</summary>
        public string TargetFolder { get; set; }

        /// <summary>True when this file is the project file itself (e.g. the .csproj).</summary>
        public bool IsProjectFile { get; set; }
    }

    /// <summary>Parsed .vstemplate document.</summary>
    public class VsTemplate
    {
        public string TemplatePath { get; set; }
        public string TemplateDirectory { get; set; }
        public VsTemplateType Type { get; set; }
        public string Version { get; set; }
        public string TemplateId { get; set; }
        public string ProjectType { get; set; }
        public string DefaultName { get; set; }
        public string Name { get; set; }
        public string RequiredFrameworkVersion { get; set; }

        public List<VsTemplateFile> Files { get; set; } = new List<VsTemplateFile>();
        public List<string> References { get; set; } = new List<string>();
    }
}
