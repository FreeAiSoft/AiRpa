using Agent.Core.Services;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Совместимая статическая точка чтения/записи канонических настроек чата. Логику хранения
    /// (миграция <c>Theme</c>/<c>UseDarkTheme</c> → <c>BaseTheme</c>, нормализация) содержит
    /// <see cref="ChatSettingsStore"/>.
    /// </summary>
    public static class SettingsStore
    {
        public static SettingsData Load() => new ChatSettingsStore().Load();

        public static void Save(SettingsData settings) => new ChatSettingsStore().Save(settings);
    }
}
