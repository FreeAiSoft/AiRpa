using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FW.EasyMvvm.Core;

namespace RPA.Models
{
    public class ActivityCreator : BaseModel
    {
        #region MVVM

        private string selectedPath;
        private string activityName;

        public string SelectedPath
        {
            get => selectedPath;
            set
            {
                selectedPath = value;
                OnPropertyChanged();
            }
        }

        public string ActivityName
        {
            get => activityName;
            set
            {
                activityName = value;
                OnPropertyChanged();
            }
        }

        #endregion MVVM

        public bool IsCreate { get; set; }
        public string FilePath { get; set; }

        public ActivityCreator(string folderPath = null)
        {
            SelectedPath = folderPath ?? Properties.Settings.Default.LastLoadedProjectPath;
            ActivityName = CoerceProjectName(Consts.defaultActivityName, SelectedPath);
        }

        private string CoerceProjectName(string defaultActivityName, string selectedPath)
        {
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return $"{defaultActivityName}.xaml";
            }

            try
            {
                IEnumerable<string> files = Directory.GetFiles(selectedPath).Select(f => Path.GetFileName(f));

                for (int i = 0; i < 1000; i++)
                {
                    string currentFileName = i == 0 ? $"{defaultActivityName}.xaml" : $"{defaultActivityName}{i}.xaml";
                    if (!files.Contains(currentFileName))
                    {
                        return currentFileName;
                    }
                }
            }
            catch (Exception ex)
            {
                OnSendMessage(ex.Message);
            }

            return $"{defaultActivityName}.xaml";
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

        public void CreateActivity()
        {
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

            if (!SelectedPath.Contains(Properties.Settings.Default.LastLoadedProjectPath.Trim().TrimEnd('\\')))
            {
                OnSendMessage($"Файл активности должен располагаться в папке проекта.");
                return;
            }

            try
            {
                string className = activityName.Replace(" ", "");
                string filePath = Path.Combine(SelectedPath, className);
                FilePath = filePath.Contains(".xaml") ? filePath : $"{filePath}.xaml";
                CreateActivityFromTemplate(className);
                IsCreate = true;
            }
            catch (Exception ex)
            {
                OnSendMessage(ex.Message);
                return;
            }

            Close();
        }

        private void CreateActivityFromTemplate(string className)
        {
            string template = File.ReadAllText("Main.xaml").Replace("x:Class=\"Main\"", $"x:Class=\"{className.Replace(".xaml", "")}\"");
            File.WriteAllText(FilePath, template);
        }

        internal void Create()
        {
            string className = activityName.Replace(" ", "");
            string filePath = Path.Combine(SelectedPath, className);
            FilePath = filePath.Contains(".xaml") ? filePath : $"{filePath}.xaml";
            CreateActivityFromTemplate(className);
            IsCreate = true;
        }
    }
}
