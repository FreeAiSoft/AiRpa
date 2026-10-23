using Newtonsoft.Json;

namespace RPA.AiAgent.Models
{
    /// <summary>
    /// Запрос к OpenRouter Chat API
    /// </summary>
    public class OpenRouterChatRequest
    {
        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("messages")]
        public OpenRouterMessage[] Messages { get; set; }

        [JsonProperty("stream")]
        public bool Stream { get; set; }

        [JsonProperty("temperature")]
        public double? Temperature { get; set; }

        [JsonProperty("top_p")]
        public double? TopP { get; set; }

        [JsonProperty("max_tokens")]
        public int? MaxTokens { get; set; }
    }

    /// <summary>
    /// Сообщение в запросе к OpenRouter
    /// </summary>
    public class OpenRouterMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }
    }

    /// <summary>
    /// Ответ от OpenRouter Chat API
    /// </summary>
    public class OpenRouterChatResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("choices")]
        public OpenRouterChoice[] Choices { get; set; }

        [JsonProperty("usage")]
        public OpenRouterUsage Usage { get; set; }
    }

    /// <summary>
    /// Вариант ответа в OpenRouter
    /// </summary>
    public class OpenRouterChoice
    {
        [JsonProperty("index")]
        public int Index { get; set; }

        [JsonProperty("message")]
        public OpenRouterMessage Message { get; set; }

        [JsonProperty("delta")]
        public OpenRouterMessage Delta { get; set; }

        [JsonProperty("finish_reason")]
        public string FinishReason { get; set; }
    }

    /// <summary>
    /// Информация об использовании токенов
    /// </summary>
    public class OpenRouterUsage
    {
        [JsonProperty("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonProperty("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonProperty("total_tokens")]
        public int TotalTokens { get; set; }
    }

    /// <summary>
    /// Ответ со списком моделей OpenRouter
    /// </summary>
    public class OpenRouterModelsResponse
    {
        [JsonProperty("data")]
        public OpenRouterModelInfo[] Data { get; set; }
    }

    /// <summary>
    /// Информация о модели в OpenRouter
    /// </summary>
    public class OpenRouterModelInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("context_length")]
        public int? ContextLength { get; set; }

        [JsonProperty("pricing")]
        public OpenRouterPricing Pricing { get; set; }
    }

    /// <summary>
    /// Информация о ценах модели
    /// </summary>
    public class OpenRouterPricing
    {
        [JsonProperty("prompt")]
        public string Prompt { get; set; }

        [JsonProperty("completion")]
        public string Completion { get; set; }
    }

    /// <summary>
    /// Ошибка от OpenRouter API
    /// </summary>
    public class OpenRouterError
    {
        [JsonProperty("error")]
        public OpenRouterErrorDetails Error { get; set; }
    }

    /// <summary>
    /// Детали ошибки OpenRouter
    /// </summary>
    public class OpenRouterErrorDetails
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("code")]
        public string Code { get; set; }
    }
}