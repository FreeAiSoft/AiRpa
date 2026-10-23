using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RPA.AiAgent.Models;

namespace RPA.AiAgent.Services
{
    /// <summary>
    /// Реализация сервиса для работы с OpenRouter API
    /// </summary>
    public class OpenRouterService : IAiProvider
    {
        private readonly string _apiKey;
        private const string BaseUrl = "https://openrouter.ai/api/v1";
        private const string SiteName = "RPA Bot";
        private const string SiteUrl = "https://github.com/yourusername/rpa-bot";

        public ProviderType Type => ProviderType.OpenRouter;
        public string DisplayName => "OpenRouter";

        public OpenRouterService()
        {
            // Получаем API ключ из настроек
            _apiKey = Properties.Settings.Default.OpenRouterApiKey;
        }

        /// <summary>
        /// Получает список доступных моделей от OpenRouter
        /// </summary>
        public async Task<List<AiModel>> GetModelsAsync()
        {
            try
            {
                // OpenRouter использует эндпоинт /models, для которого нужен HttpClient
                using (HttpClient httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
                    httpClient.DefaultRequestHeaders.Add("HTTP-Referer", SiteUrl);
                    httpClient.DefaultRequestHeaders.Add("X-Title", SiteName);

                    HttpResponseMessage response = await httpClient.GetAsync($"{BaseUrl}/models");
                    string json = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"OpenRouter API error: {response.StatusCode} - {json}");
                    }

                    OpenRouterModelsResponse modelsResponse = JsonConvert.DeserializeObject<OpenRouterModelsResponse>(json);

                    return modelsResponse?.Data == null
                        ? new List<AiModel>()
                        : modelsResponse.Data.Select(m => new AiModel
                        {
                            Id = m.Id,
                            Name = m.Name ?? m.Id,
                            DisplayName = m.Name ?? m.Id,
                            Provider = ProviderType.OpenRouter,
                            Description = m.Description,
                            ContextLength = m.ContextLength,
                            PromptPrice = ParsePrice(m.Pricing?.Prompt),
                            CompletionPrice = ParsePrice(m.Pricing?.Completion),
                            Metadata = m
                        }).ToList();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Не удалось получить список моделей от OpenRouter: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Отправляет сообщения в чат и получает ответ с потоковой передачей
        /// </summary>
        //public async Task ChatStreamAsync(
        //    string model,
        //    List<Models.ChatMessage> messages,
        //    Action<string> onChunkReceived,
        //    Action<UsageInfo> onUsageReceived = null,
        //    CancellationToken cancellationToken = default)
        //{
        //    if (string.IsNullOrEmpty(_apiKey) || _client == null)
        //    {
        //        throw new InvalidOperationException("OpenRouter API ключ не настроен. Установите ключ в настройках приложения.");
        //    }

        //    try
        //    {
        //        ChatClient chatClient = _client.GetChatClient(model);

        //        // Конвертируем наши сообщения в формат OpenAI
        //        List<OpenAI.Chat.ChatMessage> chatMessages = messages.Select(m =>
        //        {
        //            OpenAI.Chat.ChatMessage chatMsg;
        //            switch (m.Role)
        //            {
        //                case "system":
        //                    chatMsg = new SystemChatMessage(m.Content);
        //                    break;
        //                case "user":
        //                    chatMsg = new UserChatMessage(m.Content);
        //                    break;
        //                case "assistant":
        //                    chatMsg = new AssistantChatMessage(m.Content);
        //                    break;
        //                default:
        //                    chatMsg = new UserChatMessage(m.Content);
        //                    break;
        //            }
        //            return chatMsg;
        //        }).ToList();

        //        ChatCompletionOptions options = new ChatCompletionOptions();

        //        // Отправляем запрос с потоковой передачей
        //        AsyncCollectionResult<StreamingChatCompletionUpdate> streamingResult = chatClient.CompleteChatStreamingAsync(chatMessages, options, cancellationToken);
        //        IAsyncEnumerator<StreamingChatCompletionUpdate> enumerator = streamingResult.GetAsyncEnumerator(cancellationToken);

        //        int promptTokens = 0;
        //        int completionTokens = 0;
        //        bool usageInfoSent = false;

        //        try
        //        {
        //            while (await enumerator.MoveNextAsync())
        //            {
        //                StreamingChatCompletionUpdate update = enumerator.Current;

        //                // Обрабатываем контент
        //                foreach (ChatMessageContentPart contentPart in update.ContentUpdate)
        //                {
        //                    if (!string.IsNullOrEmpty(contentPart.Text))
        //                    {
        //                        onChunkReceived?.Invoke(contentPart.Text);
        //                    }
        //                }

        //                // Получаем информацию об использовании токенов
        //                if (update.Usage != null)
        //                {
        //                    promptTokens = update.Usage.InputTokenCount;
        //                    completionTokens = update.Usage.OutputTokenCount;

        //                    // Передаем информацию об использовании сразу, как только получили
        //                    if (onUsageReceived != null && !usageInfoSent && (promptTokens > 0 || completionTokens > 0))
        //                    {
        //                        onUsageReceived(new UsageInfo
        //                        {
        //                            PromptTokens = promptTokens,
        //                            CompletionTokens = completionTokens,
        //                            TotalTokens = promptTokens + completionTokens
        //                        });
        //                        usageInfoSent = true;
        //                    }
        //                }
        //            }

        //            // Если информация о токенах не была отправлена в процессе стриминга, отправляем в конце
        //            if (onUsageReceived != null && !usageInfoSent && (promptTokens > 0 || completionTokens > 0))
        //            {
        //                onUsageReceived(new UsageInfo
        //                {
        //                    PromptTokens = promptTokens,
        //                    CompletionTokens = completionTokens,
        //                    TotalTokens = promptTokens + completionTokens
        //                });
        //            }
        //        }
        //        finally
        //        {
        //            await enumerator.DisposeAsync();
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        throw new Exception($"Ошибка при отправке сообщения в OpenRouter: {ex.Message}", ex);
        //    }
        //}

        /// <summary>
        /// Проверяет, доступен ли OpenRouter (и настроен ли API ключ)
        /// </summary>
        public async Task<bool> IsAvailableAsync()
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                return false;
            }

            try
            {
                // Пробуем получить список моделей для проверки доступности
                _ = await GetModelsAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Парсит цену из строки (цена в API указана за токен, конвертируем в цену за миллион токенов)
        /// </summary>
        private static decimal? ParsePrice(string priceString)
        {
            return string.IsNullOrEmpty(priceString)
                ? null
                : decimal.TryParse(priceString, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal price)
                ? price * 1000000m  // Конвертируем из цены за токен в цену за миллион токенов
                : (decimal?)null;
        }

        /// <summary>
        /// Отправляет сообщения в чат и получает ответ с потоковой передачей
        /// </summary>
        public async Task ChatStreamAsync(string model, List<ChatMessage> messages, Action<string> onChunkReceived, Action<UsageInfo> onUsageReceived = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new InvalidOperationException("OpenRouter API ключ не настроен. Установите ключ в настройках приложения.");
            }

            try
            {
                using (HttpClient httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromMinutes(5);
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
                    httpClient.DefaultRequestHeaders.Add("HTTP-Referer", SiteUrl);
                    httpClient.DefaultRequestHeaders.Add("X-Title", SiteName);

                    // Формируем тело запроса
                    var requestBody = new
                    {
                        model,
                        messages = messages.Select(m => new
                        {
                            role = m.Role,
                            content = m.Content
                        }).ToList(),
                        stream = true
                    };

                    string jsonRequest = JsonConvert.SerializeObject(requestBody);
                    StringContent httpContent = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                    // Создаем HTTP запрос
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions")
                    {
                        Content = httpContent
                    };

                    // Отправляем запрос с потоковой передачей
                    HttpResponseMessage response = await httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        throw new Exception($"OpenRouter API error: {response.StatusCode} - {errorContent}");
                    }

                    // Читаем поток ответа
                    using (Stream stream = await response.Content.ReadAsStreamAsync())
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            // SSE формат: строки начинаются с "data: "
                            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                            {
                                continue;
                            }

                            // Убираем префикс "data: "
                            string jsonData = line.Substring(6);

                            // "[DONE]" сигнализирует об окончании потока
                            if (jsonData.Trim() == "[DONE]")
                            {
                                break;
                            }

                            try
                            {
                                JObject chunk = JObject.Parse(jsonData);

                                // Извлекаем текст из delta
                                JToken delta = chunk.SelectToken("choices[0].delta.content");
                                if (delta != null && delta.Type != JTokenType.Null)
                                {
                                    string chunkText = delta.ToString();
                                    if (!string.IsNullOrEmpty(chunkText))
                                    {
                                        onChunkReceived?.Invoke(chunkText);
                                    }
                                }

                                // Извлекаем информацию об использовании токенов
                                JToken usage = chunk.SelectToken("usage");
                                if (usage != null && onUsageReceived != null)
                                {
                                    int promptTokens = usage.Value<int?>("prompt_tokens") ?? 0;
                                    int completionTokens = usage.Value<int?>("completion_tokens") ?? 0;
                                    int totalTokens = usage.Value<int?>("total_tokens") ?? (promptTokens + completionTokens);

                                    if (promptTokens > 0 || completionTokens > 0)
                                    {
                                        onUsageReceived(new UsageInfo
                                        {
                                            PromptTokens = promptTokens,
                                            CompletionTokens = completionTokens,
                                            TotalTokens = totalTokens
                                        });
                                    }
                                }
                            }
                            catch (JsonException)
                            {
                                // Игнорируем ошибки парсинга отдельных чанков
                                continue;
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Операция была отменена пользователем
                throw;
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка при отправке сообщения в OpenRouter: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Вспомогательный класс для добавления кастомных заголовков OpenRouter
        /// </summary>
        private class OpenRouterHeadersPolicy : System.ClientModel.Primitives.PipelinePolicy
        {
            private readonly string _siteUrl;
            private readonly string _siteName;

            public OpenRouterHeadersPolicy(string siteUrl, string siteName)
            {
                _siteUrl = siteUrl;
                _siteName = siteName;
            }

            public override void Process(System.ClientModel.Primitives.PipelineMessage message,
                IReadOnlyList<System.ClientModel.Primitives.PipelinePolicy> pipeline,
                int currentIndex)
            {
                AddHeaders(message);
                ProcessNext(message, pipeline, currentIndex);
            }

            public override async ValueTask ProcessAsync(
                System.ClientModel.Primitives.PipelineMessage message,
                IReadOnlyList<System.ClientModel.Primitives.PipelinePolicy> pipeline,
                int currentIndex)
            {
                AddHeaders(message);
                await ProcessNextAsync(message, pipeline, currentIndex);
            }

            private void AddHeaders(System.ClientModel.Primitives.PipelineMessage message)
            {
                if (message.Request?.Headers != null)
                {
                    message.Request.Headers.Set("HTTP-Referer", _siteUrl);
                    message.Request.Headers.Set("X-Title", _siteName);
                }
            }
        }
    }
}
