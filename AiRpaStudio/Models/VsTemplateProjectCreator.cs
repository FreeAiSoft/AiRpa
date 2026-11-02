using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FW.EasyMvvm.Core;
using RPA.Helpers;
using RPA.VsTemplate;

namespace RPA.Models
{
    public class VsTemplateProjectCreator : BaseModel
    {
        #region MVVM

        private string selectedPath;
        private string projectName;
        private string targetFrameworkVersion;

        public string SelectedPath
        {
            get => selectedPath;
            set
            {
                selectedPath = value;
                OnPropertyChanged();
            }
        }

        public string ProjectName
        {
            get => projectName;
            set
            {
                projectName = value;
                OnPropertyChanged();
            }
        }

        public string TargetFrameworkVersion
        {
            get => targetFrameworkVersion;
            set
            {
                targetFrameworkVersion = value;
                OnPropertyChanged();
            }
        }

        public string[] TargetFrameworkVersions => Consts.targetFrameworkVersions;

        #endregion MVVM

        public bool IsCreate { get; set; }
        public string ProjectFolder { get; set; }
        public string ProjectFilePath { get; set; }

        public VsTemplateProjectCreator()
        {
            SelectedPath = GetDefaultPath();
            TargetFrameworkVersion = Consts.defaultTargetFrameworkVersion;
            ProjectName = CoerceProjectName("ActivityLibrary", SelectedPath);
        }

        public void SetFolder()
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            DialogResult result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                SelectedPath = dialog.SelectedPath;
            }
        }

        public void CreateProject()
        {
            string error = Validate();
            if (error != null)
            {
                OnSendMessage(error);
                return;
            }

            try
            {
                string safeName = VsTemplateParameterResolver.MakeSafeProjectName(ProjectName);
                string outputDirectory = Path.Combine(SelectedPath.Trim(), safeName);

                VsTemplateInstantiationRequest request = BuildRequest(outputDirectory);
                VsTemplate.VsTemplate template = VsTemplateParser.Parse(GetProjectTemplatePath());
                VsTemplateInstantiationResult result = VsTemplateInstantiator.Instantiate(template, request);

                if (!result.Success)
                {
                    OnSendMessage(string.Join(Environment.NewLine, result.Errors));
                    return;
                }

                ProjectFolder = outputDirectory;
                ProjectFilePath = Path.Combine(outputDirectory, safeName + ".csproj");
                IsCreate = true;

                if (ViewModel?.View is System.Windows.Window)
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                OnSendMessage(ErrorText.Of(ex));
            }
        }

        /// <summary>Validates the input; returns an error message or null when valid.</summary>
        public string Validate()
        {
            return string.IsNullOrWhiteSpace(SelectedPath)
                ? LocalizeHelper.Get("lang_msg_specify_project_folder")
                : !Directory.Exists(SelectedPath)
                ? LocalizeHelper.Get("lang_msg_folder_not_exists", SelectedPath)
                : string.IsNullOrWhiteSpace(ProjectName) ? LocalizeHelper.Get("lang_msg_enter_project_name") : null;
        }

        /// <summary>Builds the instantiation request used by the .vstemplate engine.</summary>
        public VsTemplateInstantiationRequest BuildRequest(string outputDirectory)
        {
            string safeName = VsTemplateParameterResolver.MakeSafeProjectName(ProjectName);

            return new VsTemplateInstantiationRequest
            {
                ProjectName = (ProjectName ?? string.Empty).Trim(),
                SafeProjectName = safeName,
                RootNamespace = safeName,
                TargetFrameworkVersion = string.IsNullOrWhiteSpace(TargetFrameworkVersion)
                    ? Consts.defaultTargetFrameworkVersion
                    : TargetFrameworkVersion,
                RegisteredOrganization = string.Empty,
                UserName = Environment.UserName,
                MachineName = Environment.MachineName,
                OutputDirectory = outputDirectory
            };
        }

        /// <summary>Returns a unique project folder name inside <paramref name="selectedPath"/>.</summary>
        public static string CoerceProjectName(string defaultName, string selectedPath)
        {
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return defaultName;
            }

            try
            {
                HashSet<string> dirs = new HashSet<string>(
                    Directory.GetDirectories(selectedPath).Select(Path.GetFileName),
                    StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < 1000; i++)
                {
                    string candidate = i == 0 ? defaultName : defaultName + i;
                    if (!dirs.Contains(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
                // Path may not exist or may be inaccessible; fall back to the default name.
            }

            return defaultName;
        }

        private string GetProjectTemplatePath()
        {
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                Consts.vsTemplatesFolderName,
                Consts.vsProjectTemplateFolderName,
                Consts.vsProjectTemplateFileName);
        }

        private static string GetDefaultPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Consts.defaultProjectsFolderName);
        }
    }
}
