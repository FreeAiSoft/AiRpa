using System;
using System.Activities;
using System.Activities.XamlIntegration;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;

using ActivitiesTracker.Core;
using ActivitiesTracker.Entities;

using RPA.Helpers;

namespace RPA.Core
{
    /// <summary>Чем закончился прогон workflow.</summary>
    public enum WorkflowRunState
    {
        /// <summary>Workflow дошёл до конца: выходные аргументы готовы.</summary>
        Completed,

        /// <summary>Прогон упал на необработанном исключении.</summary>
        Faulted,

        /// <summary>Прогон прерван извне (таймаут, отказ хоста).</summary>
        Aborted,

        /// <summary>Прогон отменён через <see cref="CancellationToken"/> или <see cref="WorkflowApplication.Cancel()"/>.</summary>
        Cancelled
    }

    /// <summary>
    /// Итог прогона workflow: состояние, выходные аргументы, ошибка со стеком и события трекера.
    /// <para>
    /// Код возврата считается здесь, а не в UI: одинаковый для студии и для раннера (`OR-02`)
    /// контракт «0 — успех, 1 — любой другой исход» проверяется тестами, а не глазами.
    /// </para>
    /// </summary>
    public sealed class WorkflowRunResult
    {
        private readonly ReadOnlyDictionary<string, object> _outputs;
        private readonly ReadOnlyCollection<TrackingEventArgs> _trackingEvents;

        internal WorkflowRunResult(
            WorkflowRunState state,
            IDictionary<string, object> outputs,
            Exception error,
            bool timedOut,
            IList<TrackingEventArgs> trackingEvents)
        {
            State = state;
            _outputs = new ReadOnlyDictionary<string, object>(
                new Dictionary<string, object>(outputs ?? new Dictionary<string, object>()));
            Error = error;
            TimedOut = timedOut;
            _trackingEvents = new ReadOnlyCollection<TrackingEventArgs>(
                new List<TrackingEventArgs>(trackingEvents ?? new List<TrackingEventArgs>()));
        }

        /// <summary>Состояние прогона.</summary>
        public WorkflowRunState State { get; }

        /// <summary>Выходные аргументы workflow (например, <c>Result</c>).</summary>
        public IReadOnlyDictionary<string, object> Outputs => _outputs;

        /// <summary>Исключение, если прогон не закончился успехом. <c>null</c> при успехе.</summary>
        public Exception Error { get; }

        /// <summary>Прогон не уложился в предел времени и был остановлен движком.</summary>
        public bool TimedOut { get; }

        /// <summary>События трекера (<see cref="CustomTrackingParticipant"/>) в порядке поступления.</summary>
        public IReadOnlyList<TrackingEventArgs> TrackingEvents => _trackingEvents;

        /// <summary>Прогон завершился штатно, без ошибки и без таймаута.</summary>
        public bool IsSuccess => State == WorkflowRunState.Completed && !TimedOut && Error == null;

        /// <summary>Код возврата процесса: <c>0</c> — успех, <c>1</c> — любой другой исход.</summary>
        public int ExitCode => IsSuccess ? 0 : 1;

        /// <summary>Полный текст ошибки со стеком (<see cref="ErrorText"/>); пустая строка при успехе.</summary>
        public string ErrorText => Error == null ? string.Empty : RPA.Helpers.ErrorText.Of(Error);

        /// <summary>Значение выходного аргумента по имени или <c>default</c>, если его нет.</summary>
        public object GetOutput(string name)
        {
            if (name == null)
            {
                return null;
            }

            return _outputs.TryGetValue(name, out object value) ? value : null;
        }

        /// <summary>Однострочное описание исхода — для лога и сообщения теста.</summary>
        public string Description
        {
            get
            {
                var text = new StringBuilder();
                text.Append($"состояние: {State}");
                if (TimedOut)
                {
                    text.Append(", таймаут");
                }

                text.Append($", выходных аргументов: {_outputs.Count}, событий трекера: {_trackingEvents.Count}");

                if (Error != null)
                {
                    text.Append(Environment.NewLine).Append(ErrorText);
                }

                return text.ToString();
            }
        }
    }

    /// <summary>
    /// «Головной» движок прогона workflow без конструктора и без UI (`CP-08`, шаг D).
    /// <para>
    /// Здесь только то, что делает прогон: загрузка активности (XAML-текст, файл или собранная
    /// сборка), <see cref="WorkflowApplication"/> с трекером <see cref="CustomTrackingParticipant"/>
    /// и итог в виде <see cref="WorkflowRunResult"/>. Всё, что относится к студии — вкладки,
    /// дизайнер, подсветка текущей активности, точки останова, окно `Locals` — остаётся в
    /// <see cref="ActivityRunner"/> и <see cref="RPA.Helpers.TrackHandler"/>. Такое разделение нужно
    /// по двум причинам: прогон обязан работать без STA-потока и без окна (это основа раннера
    /// `OR-02`), и у него должен быть один контракт результата — состояние, выходные аргументы,
    /// ошибка со стеком и код возврата.
    /// </para>
    /// <para>
    /// Настройки загрузки совпадают со студийными: <c>CompileExpressions = true</c>, иначе выражения
    /// C#/VB из XAML-робота не компилируются и прогон расходится с тем, что видит пользователь.
    /// Зависимости робота ищет <see cref="RobotAssemblyResolver"/> — его ставит вызывающая сторона
    /// (студия делает это в <see cref="ActivityRunner"/> до десериализации XAML).
    /// </para>
    /// </summary>
    public static class WorkflowRunEngine
    {
        /// <summary>Предел прогона по умолчанию: робот, который не уложился, считается зависшим.</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

        /// <summary>Сколько ждать отмены/прерывания перед тем, как объявить прогон зависшим.</summary>
        private static readonly TimeSpan StopGracePeriod = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Загружает активность из текста XAML теми же настройками, что и студия
        /// (<c>CompileExpressions = true</c>). Исключения загрузки не глотаются: текст ошибки нужен
        /// вызывающей стороне, чтобы показать его пользователю.
        /// </summary>
        public static Activity LoadActivityFromText(string xamlText)
        {
            if (string.IsNullOrWhiteSpace(xamlText))
            {
                throw new ArgumentException("Пустой текст XAML — загружать нечего.", nameof(xamlText));
            }

            using (var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(xamlText)))
            {
                return ActivityXamlServices.Load(
                    stream,
                    new ActivityXamlServicesSettings { CompileExpressions = true });
            }
        }

        /// <summary>Загружает активность из XAML-файла.</summary>
        public static Activity LoadActivityFromFile(string xamlPath)
        {
            if (string.IsNullOrWhiteSpace(xamlPath))
            {
                throw new ArgumentException("Пустой путь к XAML — загружать нечего.", nameof(xamlPath));
            }

            if (!System.IO.File.Exists(xamlPath))
            {
                throw new System.IO.FileNotFoundException("XAML-файл не найден.", xamlPath);
            }

            return LoadActivityFromText(System.IO.File.ReadAllText(xamlPath));
        }

        /// <summary>
        /// Пробует загрузить активность из текста XAML и возвращает текст ошибки вместо исключения —
        /// для путей, где падение загрузки является ожидаемым исходом (проверка файла, `OR-02`).
        /// </summary>
        public static bool TryLoadActivityFromText(string xamlText, out Activity activity, out string error)
        {
            try
            {
                activity = LoadActivityFromText(xamlText);
                error = null;

                return activity != null;
            }
            catch (Exception ex)
            {
                activity = null;
                error = ErrorText.Of(ex);

                return false;
            }
        }

        /// <summary>Пробует загрузить активность из XAML-файла (см. <see cref="TryLoadActivityFromText"/>).</summary>
        public static bool TryLoadActivityFromFile(string xamlPath, out Activity activity, out string error)
        {
            try
            {
                activity = LoadActivityFromFile(xamlPath);
                error = null;

                return activity != null;
            }
            catch (Exception ex)
            {
                activity = null;
                error = ErrorText.Of(ex);

                return false;
            }
        }

        /// <summary>
        /// Создаёт активность из собранной сборки робота — путь `CP-09`: сборка 4.7.2 грузится
        /// в домен хоста 4.8. Сборка читается в память и грузится из байтов, чтобы не блокировать
        /// файл (иначе автосборка падает с MSB3021/MSB3027) и не отдавать устаревшую копию из
        /// LoadFrom-контекста при повторном прогоне после пересборки.
        /// </summary>
        public static Activity LoadActivityFromAssembly(string assemblyPath, string typeName)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                throw new ArgumentException("Пустой путь к сборке — загружать нечего.", nameof(assemblyPath));
            }

            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw new ArgumentException("Не указан тип активности в сборке.", nameof(typeName));
            }

            object handle = System.Reflection.Assembly.Load(System.IO.File.ReadAllBytes(assemblyPath)).CreateInstance(typeName);

            if (handle is Activity activity)
            {
                return activity;
            }

            throw new InvalidOperationException(
                $"Тип «{typeName}» в сборке «{assemblyPath}» не является WF-активностью.");
        }

        /// <summary>
        /// Готовит <see cref="WorkflowApplication"/> с уже подключённым трекером. Студия берёт этот
        /// метод, чтобы прогон в конструкторе и прогон в раннере отличались только обработчиками
        /// отображения, а не настройкой приложения.
        /// </summary>
        public static WorkflowApplication CreateApplication(
            Activity activity,
            IDictionary<string, object> arguments,
            out CustomTrackingParticipant trackParticipant)
        {
            if (activity == null)
            {
                throw new ArgumentNullException(nameof(activity));
            }

            trackParticipant = new CustomTrackingParticipant();

            WorkflowApplication application = arguments == null
                ? new WorkflowApplication(activity)
                : new WorkflowApplication(activity, arguments);

            application.Extensions.Add(trackParticipant);

            return application;
        }

        /// <summary>
        /// Прогоняет активность без конструктора и без UI и возвращает итог.
        /// <para>
        /// Прогон ограничен по времени (<paramref name="timeout"/>): зависший робот сначала
        /// отменяется, потом прерывается, и в итоге помечается <see cref="WorkflowRunResult.TimedOut"/> —
        /// иначе сценарий без выхода останавливал бы раннер навсегда.
        /// </para>
        /// </summary>
        public static WorkflowRunResult Run(
            Activity activity,
            IDictionary<string, object> arguments = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default,
            Action<TrackingEventArgs> onTracked = null)
        {
            if (activity == null)
            {
                throw new ArgumentNullException(nameof(activity));
            }

            var trackedEvents = new List<TrackingEventArgs>();
            var trackedGate = new object();
            var finished = new ManualResetEventSlim(false);
            var outputs = new Dictionary<string, object>();

            var state = WorkflowRunState.Aborted;
            Exception error = null;
            var timedOut = false;

            WorkflowApplication application = CreateApplication(activity, arguments, out CustomTrackingParticipant trackParticipant);

            trackParticipant.TrackingEventArgsRecieved += (sender, args) =>
            {
                lock (trackedGate)
                {
                    trackedEvents.Add(args);
                }

                onTracked?.Invoke(args);
            };

            application.Completed = args =>
            {
                try
                {
                    switch (args.CompletionState)
                    {
                        case ActivityInstanceState.Closed:
                            state = WorkflowRunState.Completed;
                            foreach (KeyValuePair<string, object> output in args.Outputs)
                            {
                                outputs[output.Key] = output.Value;
                            }

                            break;
                        case ActivityInstanceState.Canceled:
                            state = WorkflowRunState.Cancelled;
                            break;
                        default:
                            state = WorkflowRunState.Faulted;
                            error = error ?? args.TerminationException;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // Чтение выходных аргументов само может упасть (например, на битом значении) —
                    // прогон от этого не должен «зависнуть» в глазах вызывающей стороны.
                    error = error ?? ex;
                }
                finally
                {
                    finished.Set();
                }
            };

            application.OnUnhandledException = args =>
            {
                error = error ?? Flatten(args.UnhandledException);

                return UnhandledExceptionAction.Terminate;
            };

            application.Aborted = args =>
            {
                error = error ?? args.Reason;
                finished.Set();
            };

            TimeSpan limit = timeout ?? DefaultTimeout;

            using (cancellationToken.Register(() => TryStop(application, cancel: true)))
            {
                // Прогон идёт на своём потоке: активность может уйти в ожидание (Delay, закладка),
                // и тогда Run() возвращается не по завершении workflow, а по простою. Ждать итог
                // нужно по событию завершения, иначе таймаут и отмена не сработают.
                var runner = new Thread(() =>
                {
                    try
                    {
                        application.Run();
                    }
                    catch (Exception ex)
                    {
                        error = error ?? ex;
                        state = WorkflowRunState.Faulted;
                        finished.Set();
                    }
                })
                {
                    IsBackground = true,
                    Name = "WorkflowRunEngine"
                };

                runner.Start();

                if (!finished.Wait(limit))
                {
                    timedOut = true;
                    TryStop(application, cancel: true);

                    if (!finished.Wait(StopGracePeriod))
                    {
                        TryStop(application, cancel: false);
                        _ = finished.Wait(StopGracePeriod);
                    }
                }
            }

            if (timedOut && error == null)
            {
                error = new TimeoutException($"Прогон не завершился за {limit.TotalSeconds:0} с и был остановлен движком.");
            }

            List<TrackingEventArgs> snapshot;
            lock (trackedGate)
            {
                snapshot = new List<TrackingEventArgs>(trackedEvents);
            }

            return new WorkflowRunResult(state, outputs, error, timedOut, snapshot);
        }

        /// <summary>Загружает XAML-файл и прогоняет его (сокращение для раннера и тестов).</summary>
        public static WorkflowRunResult RunFile(
            string xamlPath,
            IDictionary<string, object> arguments = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default,
            Action<TrackingEventArgs> onTracked = null)
        {
            return Run(LoadActivityFromFile(xamlPath), arguments, timeout, cancellationToken, onTracked);
        }

        /// <summary>
        /// Отмена или прерывание прогона. Исключения не пробрасываются: прогон мог уже завершиться,
        /// и тогда отменять нечего — это штатный исход, а не ошибка вызывающей стороны.
        /// </summary>
        private static void TryStop(WorkflowApplication application, bool cancel)
        {
            if (application == null)
            {
                return;
            }

            try
            {
                if (cancel)
                {
                    application.Cancel();
                }
                else
                {
                    application.Abort("Прогон остановлен движком: превышен предел времени.");
                }
            }
            catch (Exception)
            {
                // Уже завершено/прервано — останавливать нечего.
            }
        }

        /// <summary>
        /// Достаёт исходное исключение из обёрток, которыми WF сообщает об ошибке
        /// (та же логика, что в <c>ActivityRunner.WFOnUnhandledException</c>).
        /// </summary>
        private static Exception Flatten(Exception exception)
        {
            if (!(exception is AggregateException aggregate))
            {
                return exception;
            }

            if (aggregate.InnerException?.InnerException != null)
            {
                return aggregate.InnerException.InnerException;
            }

            return aggregate.InnerException ?? aggregate;
        }
    }
}
