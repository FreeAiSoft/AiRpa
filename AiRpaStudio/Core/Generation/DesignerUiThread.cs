using System;
using System.Windows;
using System.Windows.Threading;
using RPA.Helpers;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Поток, на котором разрешено работать с конструктором (этап 3 плана
    /// конвейер генерации XAML).
    /// <para>
    /// Абстракция нужна, чтобы <see cref="DesignerOpenService"/> не зависел от <c>Application.Current</c>:
    /// в тестах используется диспетчер стенда конструктора.
    /// </para>
    /// </summary>
    internal interface IDesignerUiThread
    {
        /// <summary>Текущий поток — это UI-поток (вызов можно выполнять без маршалинга).</summary>
        bool IsUiThread { get; }

        /// <summary>Выполняет делегат на UI-потоке и возвращает результат.</summary>
        T Invoke<T>(Func<T> func);
    }

    /// <summary>
    /// Маршалинг на UI-поток студии: <c>WorkflowDesigner</c>, созданный в фоновом (MCP) потоке, даёт
    /// исключения WPF о чужом потоке и «пустую» диаграмму, а не открытую вкладку.
    /// <para>
    /// Вызов с самого UI-потока выполняется напрямую: иначе ожидание задачи внутри обработчика
    /// UI-события даёт самоблокировку. Ожидание чужого потока ограничено таймаутом — занятый диспетчер
    /// (модальное окно WF) возвращает ошибку инструменту, а не вешает MCP-вызов навсегда.
    /// </para>
    /// </summary>
    internal sealed class DispatcherDesignerUiThread : IDesignerUiThread
    {
        /// <summary>Предел ожидания UI-потока по умолчанию.</summary>
        internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        private readonly Dispatcher _dispatcher;
        private readonly TimeSpan _timeout;

        internal DispatcherDesignerUiThread(Dispatcher dispatcher, TimeSpan? timeout = null)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _timeout = timeout ?? DefaultTimeout;
        }

        public bool IsUiThread => _dispatcher.CheckAccess();

        /// <summary>UI-поток запущенной студии.</summary>
        /// <exception cref="InvalidOperationException">Приложение не запущено — UI-потока нет.</exception>
        internal static DispatcherDesignerUiThread ForStudio()
        {
            Dispatcher dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                throw new InvalidOperationException(
                    "UI-поток студии недоступен: Application.Current равен null. " +
                    "Конструктор можно открыть только при запущенном приложении.");
            }

            return new DispatcherDesignerUiThread(dispatcher);
        }

        public T Invoke<T>(Func<T> func)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            if (IsUiThread)
            {
                return func();
            }

            DispatcherOperation<T> operation;
            try
            {
                operation = _dispatcher.InvokeAsync(func);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "UI-поток студии недоступен: " + ErrorText.Of(ex), ex);
            }

            if (!operation.Task.Wait(_timeout))
            {
                throw new TimeoutException(
                    $"UI-поток студии не ответил за {_timeout.TotalSeconds:0.#} с: диспетчер занят " +
                    "(вероятно, открыто модальное окно конструктора)");
            }

            return operation.Task.GetAwaiter().GetResult();
        }
    }
}
