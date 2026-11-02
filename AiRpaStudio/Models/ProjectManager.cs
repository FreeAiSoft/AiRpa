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

            string rootPath = FSNodes[0].RuntimeFullPath;

            if (!filePath.Contains(rootPath))
            {
                OnSendMessage($"Указанный путь к файлу {filePath} вне папки проекта {rootPath}");
                return;
            }

            if (!Directory.Exists(Path.GetDirectoryName(filePath)))
            {
                OnSendMessage($"Папка файла {Path.GetDirectoryName(filePath)} не существует");
                return;
            }

            string[] parts = filePath.Replace(rootPath, "").Trim('\\').Split('\\');
            Queue<string> partsStack = new Queue<string>(parts);

            AddFileNode(partsStack, FSNodes[0], filePath);
        }

        private void AddFileNode(Queue<string> partsStack, FSNode fSNode, string filePath)
        {
            string name = partsStack.Dequeue();
            if (partsStack.Count == 0)
            {
                fSNode.Childs.Add(new FSNode(filePath, FSNodeType.File));
                // Sort children after adding new file
                SortChildren(fSNode);
            }
            else
            {
                FSNode node = fSNode.Childs.FirstOrDefault(n => n.FSNodeType == FSNodeType.Dir && n.Name == name);
                if (node == null)
                {
                    OnSendMessage($"Не удалось найти папку {name}");
                    return;
                }

                AddFileNode(partsStack, node, filePath);
            }
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
