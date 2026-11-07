using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace RPA.NuGet.Commands
{
    /// <summary>
    /// Асинхронная команда панели: поиск, установка, обновление. Повторный запуск запрещён, пока
    /// предыдущий не завершился (защита от двойного клика по «Install»), а исключение не уходит в
    /// диспетчер — его обрабатывает сама команда, чтобы панель осталась живой.
    /// </summary>
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<object, Task> _execute;
        private readonly Func<object, bool> _canExecute;
        private bool _isRunning;

        public AsyncRelayCommand(Func<object, Task> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return !_isRunning && (_canExecute == null || _canExecute(parameter));
        }

        public async void Execute(object parameter)
        {
            if (!CanExecute(parameter))
                return;

            _isRunning = true;
            RaiseCanExecuteChanged();
            try
            {
                await _execute(parameter);
            }
            catch (Exception ex)
            {
                // Панель показывает ошибку в статусе сама; здесь — страховка от падения диспетчера.
                ExecutionFailed?.Invoke(this, ex);
            }
            finally
            {
                _isRunning = false;
                RaiseCanExecuteChanged();
            }
        }

        /// <summary>Последняя необработанная ошибка выполнения (панель пишет её в статус).</summary>
        public event EventHandler<Exception> ExecutionFailed;

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
