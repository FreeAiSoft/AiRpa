using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using RPA.Enums;

namespace RPA.Entities
{
    public class FSNode : INotifyPropertyChanged
    {
        private bool isEditing;
        private bool isNew;
        private bool isExpanded;
        private string name;
        private string originalName;

        public FSNodeType FSNodeType { get; set; }

        /// <summary>
        /// Indicates if this is a newly created node that hasn't been saved yet
        /// </summary>
        public bool IsNew
        {
            get => isNew;
            set
            {
                if (isNew != value)
                {
                    isNew = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Name
        {
            get => name;
            set
            {
                if (name != value)
                {
                    name = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Actual only in runtime
        /// </summary>
        public string RuntimeFullPath { get; set; }

        public bool IsExpanded
        {
            get => isExpanded;
            set
            {
                if (isExpanded != value)
                {
                    isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Stores the original name before editing starts
        /// </summary>
        public string OriginalName
        {
            get => originalName;
            set
            {
                if (originalName != value)
                {
                    originalName = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsEditing
        {
            get => isEditing;
            set
            {
                if (isEditing != value)
                {
                    // Store original name when entering edit mode
                    if (value && !isEditing)
                    {
                        OriginalName = Name;
                    }
                    isEditing = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<FSNode> Childs { get; set; }

        /// <summary>
        /// True when the solution entry points to a project file that is missing on disk (CP-06):
        /// the node stays in the tree and is reported instead of being silently dropped.
        /// </summary>
        public bool IsMissing { get; set; }

        public FSNode(string fullPath, FSNodeType fSNodeType = FSNodeType.Project)
        {
            RuntimeFullPath = fullPath;

            if (fSNodeType == FSNodeType.File)
            {
                Name = Path.GetFileName(fullPath);
            }
            else if (fSNodeType == FSNodeType.Dir)
            {
                string[] ss = fullPath.Split('\\');
                Name = ss[ss.Length - 1];
            }
            else if (fSNodeType == FSNodeType.Project)
            {
                // Для проекта читаем имя из project.json
                string projectFilePath = Path.Combine(fullPath, Consts.projectFileName);
                if (File.Exists(projectFilePath))
                {
                    try
                    {
                        string json = File.ReadAllText(projectFilePath);
                        Project project = JsonConvert.DeserializeObject<Project>(json);
                        Name = project?.Name ?? Path.GetFileName(fullPath);
                    }
                    catch
                    {
                        // Если не удалось прочитать, используем имя папки
                        string[] ss = fullPath.Split('\\');
                        Name = ss[ss.Length - 1];
                    }
                }
                else
                {
                    // Если файл project.json не найден, используем имя папки
                    string[] ss = fullPath.Split('\\');
                    Name = ss[ss.Length - 1];
                }
            }

            else if (fSNodeType == FSNodeType.Solution || fSNodeType == FSNodeType.VsProject)
            {
                Name = Path.GetFileNameWithoutExtension(fullPath);
            }

            FSNodeType = fSNodeType;
        }

        public override string ToString()
        {
            return $"{FSNodeType} {RuntimeFullPath}";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
