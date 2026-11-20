using System;
using System.IO;
using Agent.Core.Contracts;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Хранит API-ключ отдельно от <see cref="ChatSettingsStore"/>: SettingsModel при сохранении
    /// выносит ключ в secrets store и очищает его в каноническом SettingsData (chat_settings.json),
    /// чтобы секрет не оставался в обычном файле настроек.
    /// </summary>
    public sealed class ChatSecretsStore : ISecretsStore
    {
        private static string SecretsFile => Path.Combine(ChatPaths.DataDirectory, "chat_api_key.txt");

        public string LoadApiKey()
        {
            try
            {
                return File.Exists(SecretsFile) ? File.ReadAllText(SecretsFile).Trim() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public void SaveApiKey(string apiKey)
        {
            try
            {
                ChatPaths.EnsureDirectories();
                File.WriteAllText(SecretsFile, apiKey ?? string.Empty);
            }
            catch
            {
                // ignore write errors
            }
        }

        public void ClearApiKey()
        {
            try
            {
                if (File.Exists(SecretsFile))
                    File.Delete(SecretsFile);
            }
            catch
            {
                // ignore
            }
        }
    }
}