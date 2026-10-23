using System.ComponentModel;

namespace RPA.AiAgent.Models
{
    /// <summary>
    /// Типы AI провайдеров
    /// </summary>
    public enum ProviderType
    {
        /// <summary>
        /// Локальный провайдер Ollama
        /// </summary>
        [Description("Ollama")]
        Ollama,

        /// <summary>
        /// Облачный провайдер OpenRouter
        /// </summary>
        [Description("OpenRouter")]
        OpenRouter
    }
}