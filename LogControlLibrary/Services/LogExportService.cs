using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LogControlLibrary.Models;

namespace LogControlLibrary.Services
{
    /// <summary>
    /// Сервис для экспорта логов в различные форматы
    /// </summary>
    public class LogExportService
    {
        /// <summary>
        /// Формат экспорта
        /// </summary>
        public enum ExportFormat
        {
            /// <summary>
            /// Текстовый формат
            /// </summary>
            Text,
            
            /// <summary>
            /// CSV формат
            /// </summary>
            Csv,
            
            /// <summary>
            /// JSON формат
            /// </summary>
            Json
        }

        /// <summary>
        /// Экспорт логов в файл
        /// </summary>
        /// <param name="logs">Коллекция логов для экспорта</param>
        /// <param name="filePath">Путь к файлу</param>
        /// <param name="format">Формат экспорта</param>
        /// <param name="encoding">Кодировка файла (по умолчанию UTF-8)</param>
        /// <returns>Task для асинхронного выполнения</returns>
        public async Task ExportLogsAsync(IEnumerable<ILogEntry> logs, string filePath, ExportFormat format, Encoding encoding = null)
        {
            if (logs == null)
                throw new ArgumentNullException(nameof(logs));
            
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Путь к файлу не может быть пустым", nameof(filePath));

            encoding = encoding ?? Encoding.UTF8;
            
            // Создаем директорию если она не существует
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            switch (format)
            {
                case ExportFormat.Text:
                    await ExportToTextAsync(logs, filePath, encoding);
                    break;
                case ExportFormat.Csv:
                    await ExportToCsvAsync(logs, filePath, encoding);
                    break;
                case ExportFormat.Json:
                    await ExportToJsonAsync(logs, filePath, encoding);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, "Неподдерживаемый формат экспорта");
            }
        }

        /// <summary>
        /// Экспорт логов в текстовый формат
        /// </summary>
        /// <param name="logs">Коллекция логов</param>
        /// <param name="filePath">Путь к файлу</param>
        /// <param name="encoding">Кодировка</param>
        /// <returns>Task для асинхронного выполнения</returns>
        private async Task ExportToTextAsync(IEnumerable<ILogEntry> logs, string filePath, Encoding encoding)
        {
            using (var writer = new StreamWriter(filePath, false, encoding))
            {
            
                // Записываем заголовок
                await writer.WriteLineAsync($"# Экспорт логов - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                await writer.WriteLineAsync($"# Всего записей: {logs.Count()}");
                await writer.WriteLineAsync();
    
                // Записываем логи
                foreach (var log in logs)
                {
                    var line = $"[{log.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{log.Level,-7}] {log.Message}";
                    await writer.WriteLineAsync(line);
                }
            }
        }

        /// <summary>
        /// Экспорт логов в CSV формат
        /// </summary>
        /// <param name="logs">Коллекция логов</param>
        /// <param name="filePath">Путь к файлу</param>
        /// <param name="encoding">Кодировка</param>
        /// <returns>Task для асинхронного выполнения</returns>
        private async Task ExportToCsvAsync(IEnumerable<ILogEntry> logs, string filePath, Encoding encoding)
        {
            using (var writer = new StreamWriter(filePath, false, encoding))
            {
            
                // Записываем заголовки столбцов
                await writer.WriteLineAsync("Время,Уровень,Сообщение");
    
                // Записываем данные
                foreach (var log in logs)
                {
                    var timestamp = log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var level = log.Level.ToString();
                    var message = EscapeCsvField(log.Message);
                    
                    await writer.WriteLineAsync($"{timestamp},{level},{message}");
                }
            }
        }

        /// <summary>
        /// Экспорт логов в JSON формат
        /// </summary>
        /// <param name="logs">Коллекция логов</param>
        /// <param name="filePath">Путь к файлу</param>
        /// <param name="encoding">Кодировка</param>
        /// <returns>Task для асинхронного выполнения</returns>
        private async Task ExportToJsonAsync(IEnumerable<ILogEntry> logs, string filePath, Encoding encoding)
        {
            var exportData = new
            {
                ExportDate = DateTime.Now,
                TotalRecords = logs.Count(),
                Logs = logs.Select(log => new
                {
                    Id = log.Id,
                    Timestamp = log.Timestamp,
                    Level = log.Level.ToString(),
                    Message = log.Message,
                    Tag = log.Tag?.ToString()
                }).ToArray()
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(exportData, options);
            
            using (var writer = new StreamWriter(filePath, false, encoding))
            {
                await writer.WriteAsync(json);
            }
        }

        /// <summary>
        /// Экранирование поля для CSV формата
        /// </summary>
        /// <param name="field">Поле для экранирования</param>
        /// <returns>Экранированное поле</returns>
        private string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;

            // Экранируем кавычки и переносы строк
            var needsQuoting = field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r');
            
            if (needsQuoting)
            {
                field = field.Replace("\"", "\"\"");
                return $"\"{field}\"";
            }

            return field;
        }

        /// <summary>
        /// Получение рекомендуемого расширения файла для формата
        /// </summary>
        /// <param name="format">Формат экспорта</param>
        /// <returns>Расширение файла</returns>
        public string GetFileExtension(ExportFormat format)
        {
            switch (format)
            {
                case ExportFormat.Text:
                    return ".txt";
                case ExportFormat.Csv:
                    return ".csv";
                case ExportFormat.Json:
                    return ".json";
                default:
                    return ".txt";
            }
        }

        /// <summary>
        /// Получение фильтра файлов для диалога сохранения
        /// </summary>
        /// <returns>Строка фильтра для диалога</returns>
        public string GetFileFilter()
        {
            return "Текстовые файлы (*.txt)|*.txt|CSV файлы (*.csv)|*.csv|JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*";
        }

        /// <summary>
        /// Определение формата по расширению файла
        /// </summary>
        /// <param name="filePath">Путь к файлу</param>
        /// <returns>Формат экспорта</returns>
        public ExportFormat GetFormatFromExtension(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return ExportFormat.Text;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            
            switch (extension)
            {
                case ".csv":
                    return ExportFormat.Csv;
                case ".json":
                    return ExportFormat.Json;
                case ".txt":
                    return ExportFormat.Text;
                default:
                    return ExportFormat.Text;
            }
        }

        /// <summary>
        /// Экспорт логов с автоматическим определением формата по расширению файла
        /// </summary>
        /// <param name="logs">Коллекция логов для экспорта</param>
        /// <param name="filePath">Путь к файлу</param>
        /// <param name="encoding">Кодировка файла (по умолчанию UTF-8)</param>
        /// <returns>Task для асинхронного выполнения</returns>
        public async Task ExportLogsAsync(IEnumerable<ILogEntry> logs, string filePath, Encoding encoding = null)
        {
            var format = GetFormatFromExtension(filePath);
            await ExportLogsAsync(logs, filePath, format, encoding);
        }

        /// <summary>
        /// Проверка валидности пути для экспорта
        /// </summary>
        /// <param name="filePath">Путь к файлу</param>
        /// <returns>True, если путь валидный</returns>
        public bool IsValidExportPath(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return false;

                var directory = Path.GetDirectoryName(filePath);
                var fileName = Path.GetFileName(filePath);

                // Проверяем валидность имени файла
                if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    return false;

                // Проверяем валидность пути к директории
                if (!string.IsNullOrEmpty(directory) && directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}