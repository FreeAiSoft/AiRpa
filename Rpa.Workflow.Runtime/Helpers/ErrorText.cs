using System;

namespace RPA.Helpers
{
    /// <summary>
    /// Единый формат текста ошибки: «контекст: сообщение» и ниже — полный
    /// <see cref="Exception.ToString()"/> со стеком вызовов и вложенными исключениями.
    /// <para>
    /// Правило студии: любая ошибка, которую видит пользователь (окно ошибки
    /// <c>AiRpaStudio\Views\ErrorView.xaml</c>) или читает разработчик (<c>log.txt</c>, лог
    /// дизайнера, <c>Debug</c>/<c>Console</c>, ответ MCP), обязана содержать стек. Текст вида
    /// ««System.Windows.Controls.RowDefinition» не является Visual или Visual3D» без стека
    /// не поддаётся диагностике: непонятно ни место падения, ни то, какой обход дерева его вызвал.
    /// </para>
    /// </summary>
    public static class ErrorText
    {
        /// <summary>Полный текст исключения: тип, сообщение, стек и внутренние исключения.</summary>
        public static string Of(Exception ex)
        {
            return ex?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// «контекст: сообщение» и ниже — полное исключение со стеком. Конечное двоеточие контекста
        /// убирается, поэтому локализованные шаблоны вида «Не удалось загрузить в дизайнер: {0}»
        /// подходят как есть (параметр подставляется пустой строкой).
        /// </summary>
        public static string Describe(string context, Exception ex)
        {
            string trimmedContext = context?.Trim().TrimEnd(':');

            if (ex == null)
            {
                return trimmedContext ?? string.Empty;
            }

            string head = string.IsNullOrEmpty(trimmedContext)
                ? ex.Message
                : $"{trimmedContext}: {ex.Message}";

            return $"{head}\r\n\r\n{Of(ex)}";
        }
    }
}
