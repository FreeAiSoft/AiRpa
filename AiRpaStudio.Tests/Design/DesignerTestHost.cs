using System;
using System.Threading;
using System.Windows.Threading;
using RPA.Helpers;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Тестовый стенд конструктора рабочих процессов (этап 1 плана
/// <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
/// <para>
/// «XAML валиден» проверяется только прогоном через настоящий <c>WorkflowDesigner</c>: синтетическое
/// дерево в юнит-тестах не воспроизводит ни шаблоны WF, ни отложенное построение поверхности.
/// Стенд даёт минимальную среду, без которой дизайнер в тестовом процессе не живёт:
/// </para>
/// <list type="bullet">
/// <item>отдельный STA-поток с циклом диспетчера — WF откладывает работу через
/// <see cref="Dispatcher.BeginInvoke(DispatcherPriority, Delegate)"/>, а на потоке NUnit цикла нет,
/// и эта работа не выполняется никогда;</item>
/// <item>метаданные дизайнера стандартных активностей WF
/// (<see cref="WfDesignerEnvironment.RegisterDesignerMetadata"/>) — та же точка входа, что у студии.</item>
/// </list>
/// <para>
/// <see cref="System.Windows.Application"/> стенд <b>не создаёт</b> намеренно: объект приложения
/// привязывается к диспетчеру своего потока, и после этого любой другой тест, читающий
/// <c>Application.Current.Resources</c>, падает с <see cref="InvalidOperationException"/> о чужом потоке
/// (проверено: 11 тестов темы переставали проходить). Дизайнеру для загрузки и отрисовки приложение
/// не нужно — оно требуется только теме, а тема не предмет этого стенда.
/// </para>
/// <para>
/// Экземпляр один на процесс (<see cref="Shared"/>): диспетчер и поток создаются один раз.
/// </para>
/// </summary>
internal sealed class DesignerTestHost : IDisposable
{
    /// <summary>Предел ожидания запуска стенда и любого маршалированного вызова.</summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private static readonly object SharedGate = new object();

    private static DesignerTestHost _shared;

    private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
    private readonly Thread _thread;

    private Dispatcher _dispatcher;
    private Exception _startupError;
    private volatile bool _disposed;

    private DesignerTestHost()
    {
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "DesignerTestHost"
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>Стенд процесса. Запускается при первом обращении, повторные вызовы возвращают тот же.</summary>
    internal static DesignerTestHost Shared
    {
        get
        {
            lock (SharedGate)
            {
                if (_shared == null || _shared._disposed)
                {
                    _shared = Start();
                }

                return _shared;
            }
        }
    }

    /// <summary>Диспетчер STA-потока стенда.</summary>
    internal Dispatcher Dispatcher => _dispatcher;

    /// <summary><c>true</c>, если поток стенда ещё жив и стенд не закрыт.</summary>
    internal bool IsAlive => !_disposed && _thread.IsAlive;

    /// <summary>Идентификатор STA-потока стенда (для проверки «код действительно выполняется там»).</summary>
    internal int ThreadId { get; private set; }

    /// <summary>Поднимает STA-поток, диспетчер и метаданные дизайнера.</summary>
    private static DesignerTestHost Start()
    {
        var host = new DesignerTestHost();
        host._thread.Start();

        if (!host._ready.Wait(DefaultTimeout))
        {
            throw new TimeoutException(
                $"Стенд конструктора не поднялся за {DefaultTimeout.TotalSeconds:0} с: " +
                "STA-поток не дошёл до цикла диспетчера.");
        }

        if (host._startupError != null)
        {
            throw new InvalidOperationException(
                $"Стенд конструктора не поднялся: {ErrorText.Of(host._startupError)}", host._startupError);
        }

        return host;
    }

    private void ThreadMain()
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            ThreadId = Thread.CurrentThread.ManagedThreadId;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(_dispatcher));

            WfDesignerEnvironment.RegisterDesignerMetadata();
        }
        catch (Exception ex)
        {
            _startupError = ex;
        }
        finally
        {
            _ready.Set();
        }

        if (_startupError != null || _dispatcher == null)
        {
            return;
        }

        // Цикл диспетчера: без него не выполняются ни BeginInvoke из WF, ни отложенная раскладка.
        Dispatcher.Run();
    }

    /// <summary>Выполняет делегат на STA-потоке стенда и возвращает результат.</summary>
    /// <exception cref="TimeoutException">
    /// Поток стенда заблокирован. Типичная причина — системный <c>MessageBox</c> от WF: он не является
    /// WPF-окном, страж стенда его не видит и не закрывает.
    /// </exception>
    internal T Invoke<T>(Func<T> func)
    {
        if (func == null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        ThrowIfDisposed();

        if (Thread.CurrentThread.ManagedThreadId == ThreadId)
        {
            return func();
        }

        DispatcherOperation<T> operation = _dispatcher.InvokeAsync(func);
        if (!operation.Task.Wait(DefaultTimeout))
        {
            throw new TimeoutException(
                $"Стенд конструктора не ответил за {DefaultTimeout.TotalSeconds:0} с: " +
                "поток диспетчера заблокирован (вероятно, показано модальное окно WF).");
        }

        return operation.Task.GetAwaiter().GetResult();
    }

    /// <summary>Выполняет делегат на STA-потоке стенда.</summary>
    internal void Invoke(Action action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        Invoke<object>(() =>
        {
            action();
            return null;
        });
    }

    /// <summary>Прокачивает очередь диспетчера заданное время (отложенная раскладка, BeginInvoke WF).</summary>
    internal void Pump(TimeSpan duration)
    {
        ThrowIfDisposed();

        DispatcherFrame frame = new DispatcherFrame();
        DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = duration < TimeSpan.Zero ? TimeSpan.Zero : duration
        };

        timer.Tick += (sender, args) =>
        {
            timer.Stop();
            frame.Continue = false;
        };

        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// Прокачивает диспетчер, пока условие не станет истинным. Возвращает последнее значение условия:
    /// <c>false</c> означает, что за отведённое время ожидаемое состояние не наступило.
    /// </summary>
    internal bool PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        if (condition == null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Pump(TimeSpan.FromMilliseconds(50));
        }

        return condition();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _dispatcher?.InvokeShutdown();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("DesignerTestHost shutdown: " + ErrorText.Of(ex));
        }

        _thread.Join(TimeSpan.FromSeconds(5));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DesignerTestHost));
        }
    }
}
