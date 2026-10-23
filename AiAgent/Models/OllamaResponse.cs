using Newtonsoft.Json;

namespace AiAgent.Models
{
    /// <summary>
    /// Представляет ответ от Ollama API при чате
    /// </summary>
    public class OllamaResponse
    {
        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("message")]
        public OllamaMessage Message { get; set; }

        [JsonProperty("done")]
        public bool Done { get; set; }
    }

    /// <summary>
    /// Представляет сообщение в ответе Ollama
    /// </summary>
    public class OllamaMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }
    }

    /// <summary>
    /// Представляет ответ со списком моделей
    /// </summary>
    public class OllamaModelsResponse
    {
        [JsonProperty("models")]
        public OllamaModelInfo[] Models { get; set; }
    }

    /// <summary>
    /// Информация о модели в списке моделей
    /// </summary>
    public class OllamaModelInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("modified_at")]
        public string ModifiedAt { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }
    }

    /// <summary>
    /// Представляет запрос для чата с Ollama
    /// </summary>
    public class OllamaChatRequest
    {
        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("messages")]
        public OllamaRequestMessage[] Messages { get; set; }

        [JsonProperty("stream")]
        public bool Stream { get; set; }
    }

    /// <summary>
    /// Сообщение в запросе к Ollama
    /// </summary>
    public class OllamaRequestMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }
    }
}