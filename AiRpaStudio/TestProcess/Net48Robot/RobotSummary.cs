using System;
using Newtonsoft.Json;

namespace Net48Robot
{
    /// <summary>
    /// CP-09: эталонный робот на SDK-style net48. Логика та же, что у Net472Robot
    /// (Sequence → If → WriteLine), но зависимости приходят через <c>PackageReference</c>,
    /// поэтому сборка проверяет restore на чистой машине.
    /// </summary>
    public static class RobotSummary
    {
        /// <summary>Возвращает результат робота: верхний регистр входа (пустая строка для пустого входа).</summary>
        public static string BuildResult(string inputText)
        {
            return string.IsNullOrEmpty(inputText) ? string.Empty : inputText.ToUpperInvariant();
        }

        /// <summary>Сериализует результат — подтверждает, что пакет Newtonsoft.Json действительно подключён.</summary>
        public static string Serialize(string inputText)
        {
            return JsonConvert.SerializeObject(new { input = inputText, result = BuildResult(inputText) });
        }
    }
}
