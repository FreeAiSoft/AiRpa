using System.Threading;

namespace RPA.Core.Services
{
    /// <summary>Состояние фонового прохода валидации.</summary>
    internal enum DesignerValidationState
    {
        /// <summary>Прохода нет (ещё не запускался или отменён при закрытии вкладки).</summary>
        Idle = 0,

        /// <summary>Проход выполняется в фоновой задаче.</summary>
        Running = 1,

        /// <summary>Проход выполнен, результат актуален для текущего текста документа.</summary>
        Completed = 2,

        /// <summary>Проход не выполнен (ошибка). Это <b>не</b> «ошибок нет».</summary>
        Failed = 3
    }

    /// <summary>
    /// Один фоновый проход валидации дизайнера: поколение, отмена, отбрасывание устаревшего результата.
    /// <para>
    /// Правила, без которых «фоновая валидация» портит иконки:
    /// </para>
    /// <list type="bullet">
    /// <item><b>поколение</b>: результат применяется, только если с момента запуска не начался новый
    /// проход. Иначе ошибки от старого текста встают на активности нового документа;</item>
    /// <item><b>отмена</b>: новый проход отменяет предыдущий, закрытие вкладки отменяет текущий —
    /// иначе после закрытия задача продолжает работать с уже освобождённым дизайнером;</item>
    /// <item><b>«не выполнено» ≠ «ошибок нет»</b>: падение прохода переводит состояние в
    /// <see cref="DesignerValidationState.Failed"/> с причиной, и студия не снимает иконки, делая вид,
    /// что документ чист.</item>
    /// </list>
    /// <para>
    /// Класс намеренно не знает ни о <c>ModelItem</c>, ни о <c>Dispatcher</c>: вся оркестрация — здесь
    /// и под тестами, а работу с UI-потоком делает вызывающий.
    /// </para>
    /// </summary>
    internal sealed class DesignerValidationPass
    {
        private readonly object _gate = new object();

        private CancellationTokenSource _cancellation;
        private int _generation;

        /// <summary>Текущее состояние прохода.</summary>
        internal DesignerValidationState State { get; private set; } = DesignerValidationState.Idle;

        /// <summary>Причина, по которой проход не выполнен (для <see cref="DesignerValidationState.Failed"/>).</summary>
        internal string FailureReason { get; private set; } = string.Empty;

        /// <summary>Номер последнего начатого прохода (0 — проходов не было).</summary>
        internal int Generation
        {
            get
            {
                lock (_gate)
                {
                    return _generation;
                }
            }
        }

        /// <summary>Проход выполняется прямо сейчас.</summary>
        internal bool IsRunning => State == DesignerValidationState.Running;

        /// <summary>Проход выполнен и результат актуален.</summary>
        internal bool IsCompleted => State == DesignerValidationState.Completed;

        /// <summary>
        /// Начинает новый проход: увеличивает поколение, отменяет предыдущий и отдаёт токен отмены.
        /// </summary>
        internal int Begin(out CancellationToken token)
        {
            lock (_gate)
            {
                _generation++;

                CancelTokenLocked();

                _cancellation = new CancellationTokenSource();
                token = _cancellation.Token;
                State = DesignerValidationState.Running;
                FailureReason = string.Empty;

                return _generation;
            }
        }

        /// <summary>Начинает новый проход, если токен отмены вызывающему не нужен.</summary>
        internal int Begin()
        {
            return Begin(out _);
        }

        /// <summary>Токен отмены указанного поколения (для устаревшего поколения — уже отменённый).</summary>
        internal CancellationToken TokenFor(int generation)
        {
            lock (_gate)
            {
                if (IsCurrentLocked(generation) && _cancellation != null)
                {
                    return _cancellation.Token;
                }

                return new CancellationToken(true);
            }
        }

        /// <summary>
        /// Результат этого поколения ещё актуален: применять его к иконкам можно.
        /// </summary>
        internal bool IsCurrent(int generation)
        {
            lock (_gate)
            {
                return IsCurrentLocked(generation);
            }
        }

        /// <summary>
        /// Отмечает проход выполненным. <c>false</c> — поколение устарело, результат нужно выбросить
        /// (модель успела измениться). Возврат значения обязателен вызывающему: без этой проверки
        /// иконки получают ошибки от несуществующего уже текста.
        /// </summary>
        internal bool TryComplete(int generation)
        {
            lock (_gate)
            {
                if (!IsCurrentLocked(generation))
                {
                    return false;
                }

                State = DesignerValidationState.Completed;
                FailureReason = string.Empty;
                return true;
            }
        }

        /// <summary>Отмечает проход невыполненным. <c>false</c> — поколение устарело.</summary>
        internal bool TryFail(int generation, string reason)
        {
            lock (_gate)
            {
                if (!IsCurrentLocked(generation))
                {
                    return false;
                }

                State = DesignerValidationState.Failed;
                FailureReason = reason ?? string.Empty;
                return true;
            }
        }

        /// <summary>
        /// Отменяет текущий проход и делает устаревшим всё, что ещё может прийти из фона.
        /// Вызывается при закрытии вкладки и при освобождении дизайнера.
        /// </summary>
        internal void Cancel()
        {
            lock (_gate)
            {
                // Поколение растёт: незавершённая задача больше не сможет применить свой результат.
                _generation++;
                CancelTokenLocked();

                if (State == DesignerValidationState.Running)
                {
                    State = DesignerValidationState.Idle;
                }
            }
        }

        /// <summary>Текст состояния для лога: «выполнено» / «не выполнено (причина)» / «не запускалось».</summary>
        internal string Describe()
        {
            lock (_gate)
            {
                switch (State)
                {
                    case DesignerValidationState.Completed:
                        return "валидация выражений: выполнено";
                    case DesignerValidationState.Failed:
                        return "валидация выражений: не выполнено (" + FailureReason + ")";
                    case DesignerValidationState.Running:
                        return "валидация выражений: выполняется";
                    default:
                        return "валидация выражений: не запускалось";
                }
            }
        }

        private bool IsCurrentLocked(int generation)
        {
            return generation > 0 && generation == _generation;
        }

        private void CancelTokenLocked()
        {
            if (_cancellation == null)
            {
                return;
            }

            try
            {
                _cancellation.Cancel();
            }
            catch (System.Exception)
            {
                // Отмена по определению не должна ломать проход: колбэки отмены могут бросать.
            }

            _cancellation.Dispose();
            _cancellation = null;
        }
    }
}
