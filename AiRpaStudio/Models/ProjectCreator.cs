using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FW.EasyMvvm.Core;
using RPA.Entities;
using RPA.Enums;
using RPA.Helpers;

namespace RPA.Models
{
    public class ProjectCreator : BaseModel
    {
        #region MVVM

        private string selectedPath;

        public Project Project { get; set; }

        public string SelectedPath
        {
            get => selectedPath;
            set
            {
                selectedPath = value;
                OnPropertyChanged();
            }
        }

        #endregion MVVM

        public string RootFolder => Path.Combine(SelectedPath, Project.Name);

        public bool IsCreate { get; set; }

        public ProjectCreator(ProjectType projectType)
        {
            Project = new Project(projectType);

            try
            {
                string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Consts.defaultProjectsFolderName);
                if (!Directory.Exists(defaultPath))
                {
                    _ = Directory.CreateDirectory(defaultPath);
                }
                SelectedPath = defaultPath;
            }
            catch (Exception ex)
            {
                OnSendMessage(ex.Message);
                return;
            }

            Project.Description = Consts.defaultProjectDescription;
            Project.Name = CoerceProjectName(Consts.defaultProjectName, SelectedPath);
        }

        private string CoerceProjectName(string defaultProjectName, string selectedPath)
        {
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return defaultProjectName;
            }

            try
            {
                IEnumerable<string> dirs = Directory.GetDirectories(selectedPath).Select(d =>
                {
                    string[] parts = d.Split('\\');
                    return parts[parts.Length - 1];
                });

                for (int i = 0; i < 1000; i++)
                {
                    string currentDir = i == 0 ? defaultProjectName : $"{defaultProjectName}{i}";
                    if (!dirs.Contains(currentDir))
                    {
                        return currentDir;
                    }
                }
            }
            catch (Exception ex)
            {
                OnSendMessage(ex.Message);
            }

            return defaultProjectName;
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

        /// <summary>
        /// Create project
        /// </summary>
        /// <param name="isStart">If true - first start</param>
        public void CreateProject()
        {
            bool isStart = true;
            SelectedPath = SelectedPath.Trim();

            if (string.IsNullOrWhiteSpace(SelectedPath))
            {
                OnSendMessage($"Укажи папку проекта.");
                return;
            }

            if (!Directory.Exists(SelectedPath))
            {
                OnSendMessage($"Папка {SelectedPath} не существует. Укажи существующую папку проекта.");
                return;
            }

            if (string.IsNullOrWhiteSpace(Project.Name))
            {
                OnSendMessage($"Введи имя проекта.");
                return;
            }

            Project.MainActivity = Consts.defaultMainActivityFileName;

            try
            {
                Project.Name = Project.Name.Trim();
                Project.SetProjectDir(RootFolder);
                _ = Directory.CreateDirectory(RootFolder);
                ObjectHelper.SaveObject(Path.Combine(RootFolder, Consts.projectFileName), Project);
                CreateMainActivityFile(RootFolder);
                IsCreate = true;
            }
            catch (Exception ex)
            {
                OnSendMessage(ex.Message);
                return;
            }

            if (!isStart)
            {
                Close();
            }
        }

        private void CreateMainActivityFile(string projectDir)
        {
            File.Copy(Consts.defaultMainActivityFileName, Path.Combine(projectDir, Consts.defaultMainActivityFileName));
        }
    }
}
