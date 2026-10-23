using System;

namespace RPA.AiAgent.Models
{
    /// <summary>
    /// Унифицированная модель для представления AI моделей от разных провайдеров
    /// </summary>
    public class AiModel
    {
        /// <summary>
        /// Уникальный идентификатор модели
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Название модели
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Отображаемое название модели
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Провайдер модели
        /// </summary>
        public ProviderType Provider { get; set; }

        /// <summary>
        /// Описание модели
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Дата последнего изменения (для Ollama)
        /// </summary>
        public DateTime? ModifiedAt { get; set; }

        /// <summary>
        /// Размер модели в байтах (для Ollama)
        /// </summary>
        public long? Size { get; set; }

        /// <summary>
        /// Дополнительные метаданные провайдера
        /// </summary>
        public object Metadata { get; set; }

        /// <summary>
        /// Размер контекста в токенах
        /// </summary>
        public int? ContextLength { get; set; }

        /// <summary>
        /// Цена за prompt токены (за миллион токенов)
        /// </summary>
        public decimal? PromptPrice { get; set; }

        /// <summary>
        /// Цена за completion токены (за миллион токенов)
        /// </summary>
        public decimal? CompletionPrice { get; set; }

        /// <summary>
        /// Формат модели (для Ollama, например "gguf")
        /// </summary>
        public string Format { get; set; }

        /// <summary>
        /// Семейство модели (для Ollama, например "gemma3")
        /// </summary>
        public string Family { get; set; }

        /// <summary>
        /// Размер параметров (для Ollama, например "4.3B")
        /// </summary>
        public string ParameterSize { get; set; }

        /// <summary>
        /// Уровень квантизации (для Ollama, например "Q4_K_M")
        /// </summary>
        public string QuantizationLevel { get; set; }

        /// <summary>
        /// Форматированный размер модели
        /// </summary>
        public string SizeFormatted => !Size.HasValue ? string.Empty : FormatBytes(Size.Value);

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
            return !string.IsNullOrEmpty(SizeFormatted) ? string.Format("{0} ({1})", DisplayName ?? Name, SizeFormatted) : DisplayName ?? Name;
        }
    }
}