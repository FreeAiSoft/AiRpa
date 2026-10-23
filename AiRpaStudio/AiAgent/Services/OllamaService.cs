using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RPA.AiAgent.Models;

namespace RPA.AiAgent.Services
{
    /// <summary>
    /// Реализация сервиса для работы с Ollama API
    /// </summary>
    public class OllamaService : IOllamaService, IAiProvider, IDisposable
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "http://localhost:11434";

        public ProviderType Type => ProviderType.Ollama;
        public string DisplayName => "Ollama (Local)";

        public OllamaService()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(BaseUrl),
                Timeout = TimeSpan.FromMinutes(5)
            };
        }

        /// <summary>
        /// Получает список доступных моделей (для IOllamaService)
        /// </summary>
        public async Task<List<OllamaModel>> GetModelsAsync()
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync("/api/tags");
                _ = response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();
                OllamaModelsResponse modelsResponse = JsonConvert.DeserializeObject<OllamaModelsResponse>(json);

                return modelsResponse?.Models == null
                    ? new List<OllamaModel>()
                    : modelsResponse.Models.Select(m => new OllamaModel
                    {
                        Name = m.Name,
                        ModifiedAt = ParseDateTime(m.ModifiedAt),
                        Size = m.Size,
                        Digest = m.Digest,
                        Format = m.Details?.Format,
                        Family = m.Details?.Family,
                        ParameterSize = m.Details?.ParameterSize,
                        QuantizationLevel = m.Details?.QuantizationLevel
                    }).ToList();
            }
            catch (Exception ex)
            {
                throw new Exception("Не удалось получить список моделей: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Получает список доступных моделей (для IAiProvider)
        /// </summary>
        async Task<List<AiModel>> IAiProvider.GetModelsAsync()
        {
            List<OllamaModel> ollamaModels = await GetModelsAsync();
            return ollamaModels.Select(m => new AiModel
            {
                Id = m.Name,
                Name = m.Name,
                DisplayName = m.DisplayName,
                Provider = ProviderType.Ollama,
                ModifiedAt = m.ModifiedAt,
                Size = m.Size,
                Format = m.Format,
                Family = m.Family,
                ParameterSize = m.ParameterSize,
                QuantizationLevel = m.QuantizationLevel
            }).ToList();
        }

        /// <summary>
        /// Отправляет сообщения в чат и получает ответ с потоковой передачей
        /// </summary>
        public async Task ChatStreamAsync(
            string model,
            List<ChatMessage> messages,
            Action<string> onChunkReceived,
            Action<UsageInfo> onUsageReceived = null,
            CancellationToken cancellationToken = default)
        {
            OllamaRequestMessage[] requestMessages = messages.Select(m => new OllamaRequestMessage
            {
                Role = m.Role,
                Content = m.Content
            }).ToArray();

            OllamaChatRequest request = new OllamaChatRequest
            {
                Model = model,
                Messages = requestMessages,
                Stream = true
            };

            string json = JsonConvert.SerializeObject(request);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            using HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
            {
                Content = content
            };
            using HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            _ = response.EnsureSuccessStatusCode();

            using Stream stream = await response.Content.ReadAsStreamAsync();
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8, false, 8192, true);
            char[] buffer = new char[4096];
            StringBuilder lineBuffer = new StringBuilder();

            while (!cancellationToken.IsCancellationRequested)
            {
                // Читаем данные асинхронно небольшими порциями
                Task<int> readTask = reader.ReadAsync(buffer, 0, buffer.Length);
                int bytesRead = await readTask.ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    break;
                }

                // Обрабатываем прочитанные данные
                for (int i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] == '\n')
                    {
                        string line = lineBuffer.ToString().Trim();
                        _ = lineBuffer.Clear();

                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        // Парсим JSON и вызываем callback
                        try
                        {
                            OllamaResponse ollamaResponse = JsonConvert.DeserializeObject<OllamaResponse>(line);

                            if (ollamaResponse?.Message?.Content != null)
                            {
                                onChunkReceived?.Invoke(ollamaResponse.Message.Content);
                            }

                            if (ollamaResponse?.Done == true)
                            {
                                return;
                            }
                        }
                        catch
                        {
                            // Игнорируем ошибки парсинга
                        }
                    }
                    else
                    {
                        _ = lineBuffer.Append(buffer[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Проверяет, запущен ли Ollama (для IOllamaService)
        /// </summary>
        public async Task<bool> IsOllamaRunningAsync()
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync("/");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Проверяет доступность Ollama (для IAiProvider)
        /// </summary>
        async Task<bool> IAiProvider.IsAvailableAsync()
        {
            return await IsOllamaRunningAsync();
        }

        private DateTime ParseDateTime(string dateTimeString)
        {
            return DateTime.TryParse(dateTimeString, out DateTime result) ? result : DateTime.Now;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
