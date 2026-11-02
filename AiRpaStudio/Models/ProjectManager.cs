using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using FW.EasyMvvm.Core;
using Newtonsoft.Json;
using RPA.Core;
using RPA.Entities;
using RPA.Enums;
using RPA.Helpers;
using RPA.VsProject;
using RpaCommon;

namespace RPA.Models
{
    public class ProjectManager : BaseModel
    {
        private ObservableCollection<FSNode> fSNodes;
        private string projectFolder;

        public ObservableCollection<FSNode> FSNodes
        {
            get => fSNodes;
            set
            {
                fSNodes = value;
                OnPropertyChanged();
            }
        }

        public string ProjectFolder
        {
            get => projectFolder;
            set
            {
                projectFolder = value;
                OnPropertyChanged();
            }
        }

        #region Singleton

        private static readonly ProjectManager instance = new ProjectManager();

        private ProjectManager() { }

        public static ProjectManager GetInstance()
        {
            return instance;
        }

        #endregion Singleton

        public CallResult InitFileTree(string dirPath)
        {
            // проверяем есть ли папка
            if (!Directory.Exists(dirPath))
            {
                return new CallResult() { Error = new Error($"Директория проекта {dirPath} не найдена.") };
            }

            // проверяем есть ли файл проекта
            if (!File.Exists(Path.Combine(dirPath, Consts.projectFileName)))
            {
                return new CallResult() { Error = new Error($"Файл проекта {Consts.projectFileName} не найден.") };
            }

            ProjectFolder = dirPath;

            // корневой элемент
            FSNode tempFSNode = new FSNode(dirPath);

            //устанавливаем потомков
            SetChilds(tempFSNode);

            // устанавливаем коллекцию
            FSNodes = new ObservableCollection<FSNode>() { tempFSNode };

            return new CallResult();
        }

        /// <summary>
        /// Loads a Visual Studio .sln/.csproj into the file tree.
        /// </summary>
        public CallResult InitVsProjectTree(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new CallResult { Error = new Error("Путь к проекту не указан.") };
            }

            if (!File.Exists(path))
            {
                return new CallResult { Error = new Error($"Файл {path} не найден.") };
            }

            try
            {
                FSNode root;
                if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    root = VsProjectLoader.LoadSolution(path);
                }
                else if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    root = VsProjectLoader.LoadProject(path);
                }
                else
                {
                    return new CallResult { Error = new Error("Поддерживаются только файлы .sln и .csproj.") };
                }

                ProjectFolder = Path.GetDirectoryName(path);
                FSNodes = new ObservableCollection<FSNode> { root };

                return new CallResult();
            }
            catch (Exception ex)
            {
                return new CallResult { Error = new Error(ex) };
            }
        }

        private void SetChilds(FSNode fSNode)
        {
            List<FSNode> nodes = Directory.GetDirectories(fSNode.RuntimeFullPath)
                .Select(d => new FSNode(d, FSNodeType.Dir))
                .ToList();

            nodes.ForEach(n => SetChilds(n));

            List<FSNode> files = Directory.GetFiles(fSNode.RuntimeFullPath)
                .Where(f => !f.EndsWith(".xaml.breakpoints"))
                .Select(d => new FSNode(d, FSNodeType.File))
                .ToList();

            files.ForEach(n => nodes.Add(n));

            // Sort nodes: folders first (alphabetically), then files (alphabetically)
            nodes.Sort(new FSNodeComparer());

            fSNode.Childs = new ObservableCollection<FSNode>(nodes);
        }

        internal void AddFile(string filePath)
        {
            if (FSNodes == null)
            {
                OnSendMessage("Нельзя добавить активность в пустой проект");
                return;
            }

            if (FSNodes.Count == 0)
            {
                OnSendMessage("Проект не содержит корневого элемента");
                return;
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                OnSendMessage("Путь к файлу не указан");
                return;
            }

            string fullPath = Path.GetFullPath(filePath);
            string folder = Path.GetDirectoryName(fullPath);

            if (!Directory.Exists(folder))
            {
                OnSendMessage($"Папка файла {folder} не существует");
                return;
            }

            // Корень дерева может быть как папкой (project.json), так и файлом проекта (.csproj/.sln),
            // поэтому границы проекта и родительскую папку определяем через ProjectPathResolver,
            // а не разбором относительного пути по именам узлов.
            FSNode root = FSNodes[0];
            string rootFolder = ProjectPathResolver.GetNodeFolder(root);
            if (!ProjectPathResolver.IsInsideProject(fullPath, rootFolder ?? root.RuntimeFullPath))
            {
                OnSendMessage($"Указанный путь к файлу {fullPath} вне папки проекта {rootFolder}");
                return;
            }

            FSNode parentNode = FindFolderNode(root, folder) ?? AddFolderChain(root, folder);
            if (parentNode == null)
            {
                OnSendMessage($"Не удалось найти папку {folder} в дереве проекта");
                return;
            }

            if (parentNode.Childs == null)
            {
                parentNode.Childs = new ObservableCollection<FSNode>();
            }

            bool exists = parentNode.Childs.Any(n => ProjectPathResolver.AreSamePath(n.RuntimeFullPath, fullPath));
            if (!exists)
            {
                parentNode.Childs.Add(new FSNode(fullPath, FSNodeType.File));
                SortChildren(parentNode);
            }

            parentNode.IsExpanded = true;
        }

        /// <summary>
        /// Ищет в дереве узел-контейнер, которому соответствует указанная папка.
        /// </summary>
        private static FSNode FindFolderNode(FSNode node, string folder)
        {
            if (node == null || string.IsNullOrWhiteSpace(folder))
            {
                return null;
            }

            if (ProjectPathResolver.IsContainerNode(node)
                && string.Equals(ProjectPathResolver.GetNodeFolder(node), folder, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            if (node.Childs == null)
            {
                return null;
            }

            foreach (FSNode child in node.Childs)
            {
                FSNode found = FindFolderNode(child, folder);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Достраивает в дереве отсутствующие узлы папок от ближайшего существующего родителя.
        /// </summary>
        private FSNode AddFolderChain(FSNode root, string folder)
        {
            string rootFolder = ProjectPathResolver.GetNodeFolder(root);
            if (string.IsNullOrWhiteSpace(rootFolder))
            {
                return null;
            }

            List<string> missingFolders = new List<string>();
            string current = folder;
            while (!string.IsNullOrEmpty(current) && !ProjectPathResolver.AreSamePath(current, rootFolder))
            {
                if (!ProjectPathResolver.IsInsideProject(current, rootFolder))
                {
                    return null;
                }

                missingFolders.Add(current);
                current = Path.GetDirectoryName(current);
            }

            FSNode parent = FindFolderNode(root, rootFolder);
            for (int i = missingFolders.Count - 1; i >= 0; i--)
            {
                FSNode existing = FindFolderNode(root, missingFolders[i]);
                if (existing != null)
                {
                    parent = existing;
                    continue;
                }

                if (parent == null)
                {
                    return null;
                }

                if (parent.Childs == null)
                {
                    parent.Childs = new ObservableCollection<FSNode>();
                }

                FSNode created = new FSNode(missingFolders[i], FSNodeType.Dir)
                {
                    Childs = new ObservableCollection<FSNode>()
                };
                parent.Childs.Add(created);
                SortChildren(parent);
                parent = created;
            }

            return parent;
        }

        internal void DeleteFile(FSNode fSNode)
        {
            if (fSNode == null)
            {
                OnSendMessage("Узел файла не может быть null");
                return;
            }

            if (fSNode.FSNodeType != FSNodeType.File)
            {
                OnSendMessage("Указанный узел не является файлом");
                return;
            }

            try
            {
                if (!File.Exists(fSNode.RuntimeFullPath))
                {
                    OnSendMessage($"Файл {fSNode.RuntimeFullPath} не существует");
                    return;
                }

                string filePath = fSNode.RuntimeFullPath;

                // Закрываем дизайнер для этого файла, если он был открыт (особенно важно для .xaml активностей)
                if (filePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                {
                    CustomWfDesigner.GetInstance().RemoveDesignerByLoadedFile(filePath);
                }

                File.Delete(filePath);

                // Удаление из дерева путем обхода
                if (FSNodes != null && FSNodes.Count > 0)
                {
                    _ = RemoveFileNode(FSNodes[0], filePath);
                }
            }
            catch (IOException ex)
            {
                OnSendMessage($"Ошибка ввода-вывода при удалении файла: {ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                OnSendMessage($"Нет доступа для удаления файла: {ex}");
            }
            catch (Exception ex)
            {
                OnSendMessage($"Ошибка при удалении файла: {ex}");
            }
        }

        private bool RemoveFileNode(FSNode currentNode, string filePath)
        {
            if (currentNode.Childs == null)
            {
                return false;
            }

            // Пытаемся найти и удалить файл в текущем узле
            FSNode fileNode = currentNode.Childs.FirstOrDefault(n => n.FSNodeType == FSNodeType.File && n.RuntimeFullPath == filePath);
            if (fileNode != null)
            {
                return currentNode.Childs.Remove(fileNode);
            }

            // Если не нашли в текущем узле, рекурсивно ищем в подкаталогах
            foreach (FSNode child in currentNode.Childs)
            {
                if (child.FSNodeType == FSNodeType.Dir && RemoveFileNode(child, filePath))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Переименовывает файл в дереве проекта
        /// </summary>
        internal CallResult RenameFile(FSNode fileNode, string newName)
        {
            if (fileNode.FSNodeType != FSNodeType.File)
            {
                return new CallResult { Error = new Error("Указанный узел не является файлом") };
            }

            try
            {
                string oldPath = fileNode.RuntimeFullPath;
                string parentPath = Path.GetDirectoryName(oldPath);
                string newPath = Path.Combine(parentPath, newName);

                if (File.Exists(newPath))
                {
                    return new CallResult { Error = new Error($"Файл с именем '{newName}' уже существует") };
                }

                File.Move(oldPath, newPath);

                // Обновить FSNode
                fileNode.RuntimeFullPath = newPath;
                fileNode.Name = newName;

                return new CallResult();
            }
            catch (Exception ex)
            {
                return new CallResult { Error = new Error($"Ошибка при переименовании файла: {ex}") };
            }
        }

        /// <summary>
        /// Переименовывает проект (обновляет имя в project.json и в дереве)
        /// </summary>
        internal CallResult RenameProject(FSNode projectNode, string newName)
        {
            if (projectNode.FSNodeType != FSNodeType.Project)
            {
                return new CallResult { Error = new Error("Указанный узел не является проектом") };
            }

            try
            {
                // Обновить поле Name в project.json
                string projectFilePath = Path.Combine(projectNode.RuntimeFullPath, Consts.projectFileName);
                if (File.Exists(projectFilePath))
                {
                    try
                    {
                        string json = File.ReadAllText(projectFilePath);
                        Project project = JsonConvert.DeserializeObject<Project>(json);
                        if (project != null)
                        {
                            project.Name = newName;
                            string updatedJson = JsonConvert.SerializeObject(project, Formatting.Indented);
                            File.WriteAllText(projectFilePath, updatedJson);
                        }
                    }
                    catch (Exception ex)
                    {
                        return new CallResult { Error = new Error($"Ошибка при обновлении файла проекта: {ex}") };
                    }
                }
                else
                {
                    return new CallResult { Error = new Error($"Файл проекта {Consts.projectFileName} не найден") };
                }

                // Обновить отображаемое имя в дереве
                projectNode.Name = newName;

                return new CallResult();
            }
            catch (Exception ex)
            {
                return new CallResult { Error = new Error($"Ошибка при переименовании проекта: {ex}") };
            }
        }

        /// <summary>
        /// Переименовывает проект Visual Studio: переименовывается только файл проекта (.csproj)
        /// и ссылка на него в .sln; содержимое проекта не меняется.
        /// </summary>
        internal CallResult RenameVsProject(FSNode projectNode, string newName)
        {
            if (projectNode == null)
            {
                return new CallResult { Error = new Error("Узел проекта не указан") };
            }

            if (projectNode.FSNodeType != FSNodeType.VsProject)
            {
                return new CallResult { Error = new Error("Указанный узел не является проектом Visual Studio") };
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                return new CallResult { Error = new Error("Имя проекта не может быть пустым") };
            }

            string oldPath = projectNode.RuntimeFullPath;

            try
            {
                string newPath = VsProjectWriter.RenameProjectFile(oldPath, newName);

                projectNode.RuntimeFullPath = newPath;
                projectNode.Name = Path.GetFileNameWithoutExtension(newPath);

                UpdateLastLoadedProjectPath(oldPath, newPath);

                return new CallResult();
            }
            catch (Exception ex)
            {
                return new CallResult { Error = new Error($"Ошибка при переименовании файла проекта: {ex}") };
            }
        }

        /// <summary>
        /// Регистрирует новую активность в файле проекта Visual Studio (элемент XamlAppDef),
        /// чтобы созданный .xaml собирался вместе с библиотекой.
        /// </summary>
        internal CallResult RegisterFileInVsProject(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || FSNodes == null || FSNodes.Count == 0)
            {
                return new CallResult();
            }

            try
            {
                string fullPath = Path.GetFullPath(filePath);
                FSNode projectNode = FindOwningVsProject(FSNodes[0], fullPath);
                if (projectNode == null)
                {
                    // Проект студии (project.json) — регистрировать активность не нужно.
                    return new CallResult();
                }

                string projectPath = projectNode.RuntimeFullPath;
                string projectFolder = Path.GetDirectoryName(projectPath);
                string include = VsProjectWriter.GetRelativeIncludePath(projectFolder, fullPath);
                _ = VsProjectWriter.AddXamlItem(projectPath, include);

                return new CallResult();
            }
            catch (Exception ex)
            {
                return new CallResult { Error = new Error($"Не удалось добавить активность в файл проекта: {ex}") };
            }
        }

        private static FSNode FindOwningVsProject(FSNode node, string filePath)
        {
            if (node == null)
            {
                return null;
            }

            if (node.FSNodeType == FSNodeType.VsProject
                && ProjectPathResolver.IsInsideProject(filePath, ProjectPathResolver.GetNodeFolder(node)))
            {
                return node;
            }

            if (node.Childs == null)
            {
                return null;
            }

            foreach (FSNode child in node.Childs)
            {
                FSNode found = FindOwningVsProject(child, filePath);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// После переименования файла проекта обновляет сохранённый путь, чтобы проект
        /// автоматически открылся при следующем запуске студии.
        /// </summary>
        private static void UpdateLastLoadedProjectPath(string oldPath, string newPath)
        {
            try
            {
                string lastLoaded = Properties.Settings.Default.LastLoadedProjectPath;
                if (string.IsNullOrWhiteSpace(lastLoaded)
                    || string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase)
                    || !ProjectPathResolver.AreSamePath(lastLoaded, oldPath))
                {
                    return;
                }

                Properties.Settings.Default.LastLoadedProjectPath = newPath;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                // Настройки не критичны для переименования: путь просто останется прежним.
                CustomWfDesigner.GetInstance().Log($"Не удалось обновить сохранённый путь проекта: {ex}", (int)RpaCommon.Enums.LogLevel.Warn);
            }
        }

        /// <summary>
        /// Переименовывает папку в дереве проекта
        /// </summary>
        internal CallResult RenameFolder(FSNode folderNode, string newName)
        {
            if (folderNode.FSNodeType != FSNodeType.Dir)
            {
                return new CallResult { Error = new Error("Указанный узел не является папкой") };
            }

            try
            {
                string oldPath = folderNode.RuntimeFullPath;
                string parentPath = Path.GetDirectoryName(oldPath);
                string newPath = Path.Combine(parentPath, newName);

                if (Directory.Exists(newPath))
                {
                    return new CallResult { Error = new Error($"Папка с именем '{newName}' уже существует") };
                }

                Directory.Move(oldPath, newPath);

                // Обновить FSNode
                folderNode.RuntimeFullPath = newPath;
                folderNode.Name = newName;

                // Рекурсивно обновить все дочерние узлы
                UpdateChildPaths(folderNode, oldPath, newPath);

                return new CallResult();
            }
            catch (Exception ex)
            {
                return new CallResult { Error = new Error($"Ошибка при переименовании папки: {ex}") };
            }
        }

        /// <summary>
        /// Удаляет папку и удаляет её из дерева
        /// </summary>
        internal CallResult DeleteFolder(FSNode folderNode)
        {
            if (folderNode.FSNodeType != FSNodeType.Dir)
            {
                return new CallResult { Error = new Error("Указанный узел не является папкой") };
            }

            try
            {
                // Закрываем все дизайнеры для файлов в удаляемой папке
                CloseDesignersInFolder(folderNode);

                Directory.Delete(folderNode.RuntimeFullPath, true);

                // Удалить из дочерних элементов родителя
                FSNode parent = FindParentNode(FSNodes[0], folderNode);
                if (parent != null)
                {
                    _ = parent.Childs.Remove(folderNode);
                }

                return new CallResult();
            }
            catch (Exception ex)
            {
                return new CallResult { Error = new Error($"Ошибка при удалении папки: {ex}") };
            }
        }

        /// <summary>
        /// Рекурсивно закрывает все дизайнеры для .xaml файлов в папке и её подпапках
        /// </summary>
        private void CloseDesignersInFolder(FSNode folderNode)
        {
            if (folderNode.Childs == null)
            {
                return;
            }

            foreach (FSNode child in folderNode.Childs)
            {
                if (child.FSNodeType == FSNodeType.File && child.RuntimeFullPath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                {
                    CustomWfDesigner.GetInstance().RemoveDesignerByLoadedFile(child.RuntimeFullPath);
                }
                else if (child.FSNodeType == FSNodeType.Dir)
                {
                    CloseDesignersInFolder(child);
                }
            }
        }

        /// <summary>
        /// Создает новую папку внутри указанного узла папки
        /// </summary>
        internal CallResult AddFolder(FSNode parentNode, string folderName)
        {
            if (parentNode.FSNodeType != FSNodeType.Dir && parentNode.FSNodeType != FSNodeType.Project)
            {
                return new CallResult { Error = new Error("Родительский узел должен быть папкой или проектом") };
            }

            try
            {
                string newFolderPath = Path.Combine(parentNode.RuntimeFullPath, folderName);

                if (Directory.Exists(newFolderPath))
                {
                    return new CallResult { Error = new Error($"Папка '{folderName}' уже существует") };
                }

                _ = Directory.CreateDirectory(newFolderPath);

                FSNode newNode = new FSNode(newFolderPath, FSNodeType.Dir)
                {
                    Childs = new ObservableCollection<FSNode>()
                };

                parentNode.Childs.Add(newNode);

                // Sort children after adding new folder
                SortChildren(parentNode);

                return new CallResult();
            }
            catch (Exception ex)
            {
                return new CallResult { Error = new Error($"Ошибка при создании папки: {ex}") };
            }
        }

        /// <summary>
        /// Вспомогательный метод для обновления путей всех дочерних узлов после переименования
        /// </summary>
        private void UpdateChildPaths(FSNode node, string oldBasePath, string newBasePath)
        {
            if (node.Childs == null)
            {
                return;
            }

            foreach (FSNode child in node.Childs)
            {
                child.RuntimeFullPath = child.RuntimeFullPath.Replace(oldBasePath, newBasePath);
                UpdateChildPaths(child, oldBasePath, newBasePath);
            }
        }

        /// <summary>
        /// Вспомогательный метод для поиска родительского узла
        /// </summary>
        private FSNode FindParentNode(FSNode current, FSNode target)
        {
            if (current.Childs == null)
            {
                return null;
            }

            if (current.Childs.Contains(target))
            {
                return current;
            }

            foreach (FSNode child in current.Childs)
            {
                FSNode result = FindParentNode(child, target);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        /// <summary>
        /// Sorts the children of a node: folders first (alphabetically), then files (alphabetically)
        /// </summary>
        private void SortChildren(FSNode node)
        {
            if (node?.Childs == null || node.Childs.Count <= 1)
            {
                return;
            }

            List<FSNode> sortedList = node.Childs.OrderBy(n => n, new FSNodeComparer()).ToList();

            // Move items to their correct positions instead of clearing and re-adding
            for (int i = 0; i < sortedList.Count; i++)
            {
                FSNode item = sortedList[i];
                int currentIndex = node.Childs.IndexOf(item);

                if (currentIndex != i)
                {
                    node.Childs.Move(currentIndex, i);
                }
            }
        }
    }
}
