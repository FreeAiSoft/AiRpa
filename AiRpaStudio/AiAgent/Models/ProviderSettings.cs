namespace RPA.AiAgent.Models
{
    /// <summary>
    /// Настройки для конкретного провайдера
    /// </summary>
    public class ProviderSettings
    {
        /// <summary>
        /// ID выбранной модели для этого провайдера
        /// </summary>
        public string SelectedModelId { get; set; }
    }

    /// <summary>
    /// Коллекция настроек для всех провайдеров
    /// </summary>
    public class AllProviderSettings
    {
        /// <summary>
        /// Словарь настроек провайдеров: ключ - имя провайдера (enum.ToString()), значение - настройки
        /// </summary>
        public System.Collections.Generic.Dictionary<string, ProviderSettings> Providers { get; set; }

        public AllProviderSettings()
        {
            Providers = new System.Collections.Generic.Dictionary<string, ProviderSettings>();
        }

        /// <summary>
        /// Получает настройки для указанного провайдера
        /// </summary>
        public ProviderSettings GetProviderSettings(ProviderType providerType)
        {
            string key = providerType.ToString();
            if (!Providers.TryGetValue(key, out ProviderSettings settings))
            {
                settings = new ProviderSettings();
                Providers[key] = settings;
            }
            return settings;
        }

        /// <summary>
        /// Устанавливает выбранную модель для провайдера
        /// </summary>
        public void SetSelectedModel(ProviderType providerType, string modelId)
        {
            string key = providerType.ToString();
            if (!Providers.TryGetValue(key, out ProviderSettings settings))
            {
                settings = new ProviderSettings();
                Providers[key] = settings;
            }
            settings.SelectedModelId = modelId;
        }

        /// <summary>
        /// Получает ID выбранной модели для провайдера
        /// </summary>
        public string GetSelectedModelId(ProviderType providerType)
        {
            string key = providerType.ToString();
            return Providers.TryGetValue(key, out ProviderSettings settings) ? settings.SelectedModelId : null;
        }
    }
}