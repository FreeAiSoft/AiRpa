using System;

namespace AiAgent.Models
{
    /// <summary>
    /// Представляет модель Ollama
    /// </summary>
    public class OllamaModel
    {
        /// <summary>
        /// Название модели
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Дата последнего изменения
        /// </summary>
        public DateTime ModifiedAt { get; set; }

        /// <summary>
        /// Размер модели в байтах
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// Отображаемое название модели
        /// </summary>
        public string DisplayName
        {
            get { return Name; }
        }

        /// <summary>
        /// Форматированный размер модели
        /// </summary>
        public string SizeFormatted
        {
            get { return FormatBytes(Size); }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return string.Format("{0:0.##} {1}", len, sizes[order]);
        }

        public override string ToString()
        {
            return string.Format("{0} ({1})", Name, SizeFormatted);
        }
    }
}