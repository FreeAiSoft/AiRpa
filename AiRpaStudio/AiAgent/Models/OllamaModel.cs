using System;

namespace RPA.AiAgent.Models
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
        /// Digest модели
        /// </summary>
        public string Digest { get; set; }

        /// <summary>
        /// Формат модели (например, gguf)
        /// </summary>
        public string Format { get; set; }

        /// <summary>
        /// Семейство модели
        /// </summary>
        public string Family { get; set; }

        /// <summary>
        /// Размер параметров (например, "4.3B")
        /// </summary>
        public string ParameterSize { get; set; }

        /// <summary>
        /// Уровень квантизации (например, "Q4_K_M")
        /// </summary>
        public string QuantizationLevel { get; set; }

        /// <summary>
        /// Отображаемое название модели
        /// </summary>
        public string DisplayName => Name;

        /// <summary>
        /// Форматированный размер модели
        /// </summary>
        public string SizeFormatted => FormatBytes(Size);

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return string.Format("{0:0.##} {1}", len, sizes[order]);
        }

        public override string ToString()
        {
            return string.Format("{0} ({1})", Name, SizeFormatted);
        }
    }
}
