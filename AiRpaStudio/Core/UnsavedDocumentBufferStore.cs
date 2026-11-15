using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RPA.Core
{
    /// <summary>
    /// Тип документа, чей несохранённый буфер хранится в коллекции: редактор кода/XAML или дизайнер WF.
    /// </summary>
    public enum UnsavedDocumentBufferKind
    {
        /// <summary>Вкладка редактора кода/XAML (<see cref="Models.CodeDocument"/>).</summary>
        Code,

        /// <summary>Вкладка дизайнера рабочего процесса (<see cref="WorkflowDesignerEx"/>).</summary>
        Workflow
    }

    /// <summary>
    /// Несохранённый буфер закрытого документа: текст и тип вкладки, из которой он был взят.
    /// Буфер не пишет файл сам — запись происходит при «Сохранить»/«Сохранить всё» владельцем
    /// коллекции (<see cref="Models.Main"/>).
    /// </summary>
    public sealed class UnsavedDocumentBuffer
    {
        /// <summary>Полный (абсолютный) путь к файлу.</summary>
        public string FilePath { get; }

        /// <summary>Содержимое буфера на момент закрытия вкладки.</summary>
        public string Text { get; }

        /// <summary>Тип закрытой вкладки.</summary>
        public UnsavedDocumentBufferKind Kind { get; }

        /// <summary>Буфер принадлежит вкладке редактора кода/XAML.</summary>
        public bool IsCode => Kind == UnsavedDocumentBufferKind.Code;

        /// <summary>Буфер принадлежит вкладке дизайнера WF.</summary>
        public bool IsWorkflow => Kind == UnsavedDocumentBufferKind.Workflow;

        public UnsavedDocumentBuffer(string filePath, string text, UnsavedDocumentBufferKind kind)
        {
            FilePath = filePath;
            Text = text ?? string.Empty;
            Kind = kind;
        }
    }

    /// <summary>
    /// Коллекция несохранённых буферов <b>закрытых</b> документов.
    /// <para>
    /// Инвариант «один владелец буфера»: здесь лежат только буферы документов, у которых нет открытой
    /// вкладки. При повторном открытии файла буфер переносится во вкладку и удаляется из коллекции;
    /// при «Сохранить всё» — записывается в файл и удаляется. Так избегаем двойной записи одного
    /// содержимого (особенно для дизайнеров WF) и никогда не «воскрешаем» удалённый файл.
    /// </para>
    /// <para>
    /// Ключ — полный путь (<see cref="Path.GetFullPath(string)"/>, без учёта регистра), как и
    /// идентификация вкладок по пути в студии.
    /// </para>
    /// </summary>
    public sealed class UnsavedDocumentBufferStore
    {
        private readonly Dictionary<string, UnsavedDocumentBuffer> buffers =
            new Dictionary<string, UnsavedDocumentBuffer>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Количество хранимых буферов.</summary>
        public int Count => buffers.Count;

        /// <summary>
        /// Кладёт (или перезаписывает) буфер для файла. Путь обязан быть абсолютным: несохранённые
        /// новые документы без пути сюда не попадают, потому что сохранить их в прежний файл нельзя.
        /// </summary>
        public void Set(string filePath, string text, UnsavedDocumentBufferKind kind)
        {
            string normalized = NormalizePath(filePath);
            buffers[normalized] = new UnsavedDocumentBuffer(normalized, text, kind);
        }

        /// <summary>Возвращает буфер по пути (без учёта регистра), не бросая для пустого/относительного пути.</summary>
        public bool TryGet(string filePath, out UnsavedDocumentBuffer buffer)
        {
            buffer = null;

            string normalized = TryNormalizePath(filePath);
            if (normalized == null)
            {
                return false;
            }

            return buffers.TryGetValue(normalized, out buffer);
        }

        /// <summary>Есть ли буфер для указанного файла.</summary>
        public bool Contains(string filePath)
        {
            return TryGet(filePath, out _);
        }

        /// <summary>Удаляет буфер по пути. Возвращает <c>true</c>, если запись была и удалена.</summary>
        public bool Remove(string filePath)
        {
            string normalized = TryNormalizePath(filePath);
            if (normalized == null)
            {
                return false;
            }

            return buffers.Remove(normalized);
        }

        /// <summary>Копия всех буферов (для безопасного перебора во время записи на диск).</summary>
        public IReadOnlyList<UnsavedDocumentBuffer> Snapshot()
        {
            return buffers.Values.ToList();
        }

        /// <summary>Очищает коллекцию (сценарий «Не сохранять» в диалоге несохранённых изменений).</summary>
        public void Clear()
        {
            buffers.Clear();
        }

        private static string NormalizePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Path is required.", nameof(filePath));
            }

            if (!Path.IsPathRooted(filePath))
            {
                throw new ArgumentException($"Path must be absolute: {filePath}", nameof(filePath));
            }

            try
            {
                return Path.GetFullPath(filePath);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Path is not valid: {filePath}", nameof(filePath), ex);
            }
        }

        private static string TryNormalizePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !Path.IsPathRooted(filePath))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(filePath);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
