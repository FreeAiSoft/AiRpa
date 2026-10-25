using System;

namespace RPA.VsTemplate
{
    /// <summary>Input values used to instantiate a template.</summary>
    public class VsTemplateInstantiationRequest
    {
        public string ProjectName { get; set; } = string.Empty;
        public string SafeProjectName { get; set; } = string.Empty;
        public string RootNamespace { get; set; } = string.Empty;
        public string TargetFrameworkVersion { get; set; } = "4.8";
        public string RegisteredOrganization { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public string SafeItemName { get; set; } = string.Empty;
        public string FileInputName { get; set; } = string.Empty;
        public string OutputDirectory { get; set; } = string.Empty;

        public string Year { get; set; }
        public string Time { get; set; }

        public VsTemplateInstantiationRequest()
        {
            Year = DateTime.Now.Year.ToString();
            Time = DateTime.Now.ToString("HH:mm:ss");
        }
    }
}
