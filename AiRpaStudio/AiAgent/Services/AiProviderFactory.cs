using System;
using System.Collections.Generic;
using System.Linq;
using RPA.AiAgent.Models;

namespace RPA.AiAgent.Services
{
    /// <summary>
    /// Фабрика для создания и управления экземплярами AI провайдеров
    /// </summary>
    public class AiProviderFactory : IDisposable
    {
        private readonly Dictionary<ProviderType, IAiProvider> _providers;
        private bool _disposed = false;

        public AiProviderFactory()
        {
            _providers = new Dictionary<ProviderType, IAiProvider>
            {
                { ProviderType.Ollama, new OllamaService() },
                { ProviderType.OpenRouter, new OpenRouterService() }
            };
        }

        /// <summary>
        /// Получает провайдер по типу
        /// </summary>
        /// <param name="type">Тип провайдера</param>
        /// <returns>Экземпляр провайдера</returns>
        public IAiProvider GetProvider(ProviderType type)
        {
            return _providers.ContainsKey(type) ? _providers[type] : throw new ArgumentException($"Провайдер {type} не найден");
        }

        /// <summary>
        /// Получает список всех доступных типов провайдеров
        /// </summary>
        /// <returns>Список типов провайдеров</returns>
        public List<ProviderType> GetAvailableProviders()
        {
            return _providers.Keys.ToList();
        }

        /// <summary>
        /// Проверяет, доступен ли указанный провайдер
        /// </summary>
        /// <param name="type">Тип провайдера</param>
        /// <returns>True если провайдер доступен</returns>
        public async System.Threading.Tasks.Task<bool> IsProviderAvailableAsync(ProviderType type)
        {
            if (!_providers.ContainsKey(type))
            {
                return false;
            }

            IAiProvider provider = _providers[type];
            return await provider.IsAvailableAsync();
        }

        /// <summary>
        /// Пытается получить провайдер по умолчанию (из настроек или первый доступный)
        /// </summary>
        /// <returns>Провайдер по умолчанию или null</returns>
        public async System.Threading.Tasks.Task<IAiProvider> GetDefaultProviderAsync()
        {
            // Пытаемся получить сохраненного провайдера из настроек
            string savedProvider = Properties.Settings.Default.SelectedAiProvider;

            if (!string.IsNullOrEmpty(savedProvider) && Enum.TryParse(savedProvider, out ProviderType providerType))
            {
                if (_providers.ContainsKey(providerType))
                {
                    IAiProvider provider = _providers[providerType];
                    bool isAvailable = await provider.IsAvailableAsync();

                    if (isAvailable)
                    {
                        return provider;
                    }
                }
            }

            // Если сохраненный провайдер недоступен, ищем первый доступный
            foreach (KeyValuePair<ProviderType, IAiProvider> kvp in _providers)
            {
                bool isAvailable = await kvp.Value.IsAvailableAsync();
                if (isAvailable)
                {
                    return kvp.Value;
                }
            }

            // Если ни один не доступен, возвращаем Ollama по умолчанию
            return _providers[ProviderType.Ollama];
        }

        /// <summary>
        /// Сохраняет выбранного провайдера в настройках
        /// </summary>
        /// <param name="type">Тип провайдера</param>
        public void SaveSelectedProvider(ProviderType type)
        {
            Properties.Settings.Default.SelectedAiProvider = type.ToString();
            Properties.Settings.Default.Save();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (IAiProvider provider in _providers.Values)
                {
                    if (provider is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                _providers.Clear();
                _disposed = true;
            }
        }
    }
}
