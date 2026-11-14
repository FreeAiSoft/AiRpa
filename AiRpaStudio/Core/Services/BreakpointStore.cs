using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace RPA.Core.Services
{
    /// <summary>
    /// Персистентность точек останова в пользовательском локальном store <b>вне проекта</b>
    /// (вариант 1): <c>%LOCALAPPDATA%\AiRpaStudio\Breakpoints\bp_&lt;sha256&gt;.json</c>.
    /// <para>
    /// Проект остаётся чистым — рядом с XAML не создаётся никаких побочных файлов. Одна точка
    /// хранит координаты, тип (<see cref="BreakpointType"/> = <c>BreakpointTypes</c> как int) и
    /// стабильный якорь <c>ActivityId</c>/<c>DisplayName</c>, по которому точка переживает сдвиг
    /// строк после правки документа. Запись атомарная (temp + подмена) с резервной копией <c>.bak</c>.
    /// </para>
    /// </summary>
    internal static class BreakpointStore
    {
        private const int SchemaVersion = 1;
        private static readonly object WriteLock = new object();

        /// <summary>Точка переопределения корня store для тестов (изолирует прогон от реальных данных).</summary>
        internal static string RootOverride { get; set; }

        internal static string RootDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AiRpaStudio",
            "Breakpoints");

        private static string DirectoryPath => RootOverride ?? RootDirectory;

        /// <summary>
        /// Возвращает путь файла store для воркфлоу. Только абсолютные пути сохраняются на диск;
        /// несохранённый документ (относительный путь) хранит точки только в памяти.
        /// </summary>
        internal static bool TryGetStorePath(string workflowPath, out string path)
        {
            path = null;

            if (string.IsNullOrWhiteSpace(workflowPath) || !Path.IsPathRooted(workflowPath))
            {
                return false;
            }

            string normalized;
            try
            {
                normalized = Path.GetFullPath(workflowPath);
            }
            catch (Exception)
            {
                return false;
            }

            string key = Hash(normalized.ToUpperInvariant());
            path = Path.Combine(DirectoryPath, "bp_" + key + ".json");
            return true;
        }

        /// <summary>
        /// Читает точки из store. Сначала основной файл, затем резервная копия <c>.bak</c>
        /// (если основной битый). Возвращает <c>null</c>, если файлов нет или они не читаются.
        /// </summary>
        internal static List<BreakpointRecord> Load(string workflowPath)
        {
            if (!TryGetStorePath(workflowPath, out string path))
            {
                return null;
            }

            List<BreakpointRecord> records = LoadFile(path);
            if (records != null)
            {
                return records;
            }

            return LoadFile(path + ".bak");
        }

        /// <summary>
        /// Атомарно записывает точки в store. Пустой список не удаляет файл, а пишет пустой store:
        /// наличие файла означает «этот документ уже мигрирован», что защищает от повторного импорта
        /// старого sidecar после удаления всех точек.
        /// </summary>
        internal static void Save(string workflowPath, IReadOnlyList<BreakpointRecord> records)
        {
            if (!TryGetStorePath(workflowPath, out string path))
            {
                return;
            }

            try
            {
                var file = new BreakpointFile
                {
                    SchemaVersion = SchemaVersion,
                    WorkflowPath = workflowPath,
                    Breakpoints = (records ?? new List<BreakpointRecord>()).ToList()
                };

                string json = JsonConvert.SerializeObject(file, Formatting.Indented);
                Directory.CreateDirectory(DirectoryPath);

                lock (WriteLock)
                {
                    string temp = path + ".tmp";
                    File.WriteAllText(temp, json, Encoding.UTF8);

                    // Текущий файл — в .bak, новый — на его место.
                    if (File.Exists(path))
                    {
                        string bak = path + ".bak";
                        DeleteFileSafely(bak);
                        try
                        {
                            File.Move(path, bak);
                        }
                        catch (Exception)
                        {
                            // Не удалось сделать бэкап — пишем поверх, но не роняем сохранение.
                        }
                    }

                    File.Move(temp, path);
                }
            }
            catch (Exception)
            {
                // Ошибка записи store не должна ронять работу студии.
                TryDeleteSafely(path + ".tmp");
            }
        }

        private static List<BreakpointRecord> LoadFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                BreakpointFile file = JsonConvert.DeserializeObject<BreakpointFile>(File.ReadAllText(path, Encoding.UTF8));
                if (file == null || file.SchemaVersion != SchemaVersion || file.Breakpoints == null)
                {
                    return null;
                }

                return file.Breakpoints.Where(bp => bp != null).ToList();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Hash(string input)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }

        private static void DeleteFileSafely(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Занятый файл не должен ронять удаление.
            }
        }

        private static void TryDeleteSafely(string path)
        {
            DeleteFileSafely(path);
        }

        internal class BreakpointRecord
        {
            /// <summary>Стабильный якорь: <c>Activity.Id</c> (QualifiedId в дереве дизайнера).</summary>
            public string ActivityId { get; set; }

            /// <summary>Вторичный якорь: <c>Activity.DisplayName</c>.</summary>
            public string DisplayName { get; set; }

            public int StartLine { get; set; }
            public int StartColumn { get; set; }
            public int EndLine { get; set; }
            public int EndColumn { get; set; }

            /// <summary><c>BreakpointTypes</c> (Bounded/Enabled/Conditional) как int.</summary>
            public int BreakpointType { get; set; }
        }

        private class BreakpointFile
        {
            public int SchemaVersion { get; set; }
            public string WorkflowPath { get; set; }
            public List<BreakpointRecord> Breakpoints { get; set; }
        }
    }
}
