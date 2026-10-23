using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RPA.AiAgent.Models;

namespace RPA.AiAgent.Services
{
    /// <summary>
    /// Реализация хранилища диалогов в JSON файлах
    /// </summary>
    public class JsonConversationStorage : IConversationStorage
    {
        private readonly string _storageDirectory;

        public JsonConversationStorage()
        {
            _storageDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AiAgent",
                "Conversations"
            );

            // Создаем директорию, если её нет
            if (!Directory.Exists(_storageDirectory))
            {
                _ = Directory.CreateDirectory(_storageDirectory);
            }
        }

        /// <summary>
        /// Сохраняет диалог
        /// </summary>
        public Task SaveConversationAsync(Conversation conversation)
        {
            return Task.Run(() =>
            {
                if (conversation == null)
                {
                    throw new ArgumentNullException(nameof(conversation));
                }

                conversation.UpdatedAt = DateTime.Now;

                string filePath = GetFilePath(conversation.Id);
                string json = JsonConvert.SerializeObject(conversation, Formatting.Indented);
                File.WriteAllText(filePath, json);
            });
        }

        /// <summary>
        /// Загружает диалог по идентификатору
        /// </summary>
        public Task<Conversation> LoadConversationAsync(Guid id)
        {
            return Task.Run(() =>
            {
                string filePath = GetFilePath(id);

                if (!File.Exists(filePath))
                {
                    return null;
                }

                string json = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<Conversation>(json);
            });
        }

        /// <summary>
        /// Получает список всех диалогов
        /// </summary>
        public Task<List<Conversation>> GetAllConversationsAsync()
        {
            return Task.Run(() =>
            {
                List<Conversation> conversations = new List<Conversation>();

                if (!Directory.Exists(_storageDirectory))
                {
                    return conversations;
                }

                string[] files = Directory.GetFiles(_storageDirectory, "*.json");

                foreach (string file in files)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        Conversation conversation = JsonConvert.DeserializeObject<Conversation>(json);
                        if (conversation != null)
                        {
                            conversations.Add(conversation);
                        }
                    }
                    catch
                    {
                        // Пропускаем поврежденные файлы
                    }
                }

                return conversations.OrderByDescending(c => c.UpdatedAt).ToList();
            });
        }

        /// <summary>
        /// Удаляет диалог
        /// </summary>
        public Task DeleteConversationAsync(Guid id)
        {
            return Task.Run(() =>
            {
                string filePath = GetFilePath(id);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            });
        }

        private string GetFilePath(Guid id)
        {
            return Path.Combine(_storageDirectory, string.Format("{0}.json", id));
        }
    }
}
