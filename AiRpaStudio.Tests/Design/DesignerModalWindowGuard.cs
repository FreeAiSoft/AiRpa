using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using RPA.Helpers;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Страж модальных окон стенда: WPF-окно (в том числе диалог WF «Конструктор рабочих процессов»),
/// появившееся во время загрузки или отрисовки, запоминается и закрывается.
/// <para>
/// Закрывать обязательно: <see cref="Window.ShowDialog"/> держит диспетчер, и стенд не смог бы
/// продолжить прогон — тест упал бы по таймауту без объяснения причины.
/// </para>
/// <para>
/// Два независимых механизма:
/// <list type="number">
/// <item>обработчик класса <see cref="FrameworkElement.LoadedEvent"/> с <c>handledEventsToo</c> —
/// без этого флага обработчик не вызывается, если событие уже помечено обработанным;</item>
/// <item>опрос живых окон приложения на <see cref="DispatcherTimer"/> — работает и внутри вложенного
/// цикла диспетчера, который прокручивает <see cref="Window.ShowDialog"/>; если
/// <see cref="Application"/> в процессе нет (стенд его намеренно не создаёт),
/// опрос просто ничего не находит.</item>
/// </list>
/// </para>
/// <para>
/// Ограничение: системный <c>MessageBox</c> — не WPF-окно и здесь не перехватывается. Запрет
/// <c>MessageBox</c> в студии стерегут исходники (<c>ErrorWindowTests</c>), а стенд закрывает ту дорогу,
/// по которой WF показывает ошибки валидации: публикует собственный
/// <see cref="RecordingValidationErrorService"/> вместо студийного сервиса ошибок.
/// </para>
/// </summary>
internal sealed class DesignerModalWindowGuard : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly object Gate = new object();
    private static readonly RoutedEventHandler ClassHandler = OnWindowLoaded;

    private static DesignerModalWindowGuard _active;
    private static bool _handlerRegistered;

    private readonly HashSet<Window> _seen = new HashSet<Window>();
    private readonly List<string> _windows = new List<string>();
    private readonly DispatcherTimer _timer;

    private bool _disposed;

    private DesignerModalWindowGuard()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher)
        {
            Interval = PollInterval
        };

        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>Начинает слежение. Окна, появившиеся до вызова, не учитываются.</summary>
    internal static DesignerModalWindowGuard Attach()
    {
        lock (Gate)
        {
            EnsureHandler();

            var guard = new DesignerModalWindowGuard();
            _active = guard;
            return guard;
        }
    }

    /// <summary>Найденные модальные окна: тип и заголовок в порядке появления.</summary>
    internal IReadOnlyList<string> Windows => _windows;

    /// <summary>
    /// Снимает список живых окон приложения и закрывает новые. Вызывать на потоке диспетчера стенда.
    /// </summary>
    internal void Poll()
    {
        WindowCollection windows;
        try
        {
            windows = Application.Current?.Windows;
        }
        catch (Exception ex)
        {
            // Application, созданный другим потоком (или уже закрытый): опрос недоступен, остаётся
            // обработчик класса. Ошибка чужого потока не должна ронять прогон.
            System.Diagnostics.Debug.WriteLine("Modal window guard: " + ErrorText.Of(ex));
            return;
        }

        if (windows == null)
        {
            return;
        }

        foreach (Window window in windows.Cast<Window>().ToArray())
        {
            Record(window);
        }
    }

    private static void EnsureHandler()
    {
        if (_handlerRegistered)
        {
            return;
        }

        // handledEventsToo: событие Loaded к моменту вызова может быть уже помечено обработанным,
        // и без этого флага обработчик не вызывается вовсе (проверено на реальном окне).
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, ClassHandler, true);
        _handlerRegistered = true;
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs args)
    {
        DesignerModalWindowGuard guard;
        lock (Gate)
        {
            guard = _active;
        }

        guard?.Record(sender as Window);
    }

    private void Record(Window window)
    {
        if (window == null || !_seen.Add(window))
        {
            return;
        }

        lock (Gate)
        {
            _windows.Add(Describe(window));
        }

        // Закрытие откладывается: во время Loaded окно ещё не готово к Close (ShowDialog не вернулся).
        window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Close(window)));
    }

    private void OnTick(object sender, EventArgs args)
    {
        try
        {
            Poll();
        }
        catch (Exception ex)
        {
            // Страж не имеет права ронять прогон: его задача — сообщить об окне, а не упасть самому.
            System.Diagnostics.Debug.WriteLine("Modal window guard: " + ErrorText.Of(ex));
        }
    }

    private static void Close(Window window)
    {
        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Modal window was not closed: " + ErrorText.Of(ex));
        }
    }

    private static string Describe(Window window)
    {
        string title = string.IsNullOrWhiteSpace(window.Title) ? "(без заголовка)" : window.Title;
        return $"{window.GetType().FullName} \"{title}\"";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;

        lock (Gate)
        {
            if (ReferenceEquals(_active, this))
            {
                _active = null;
            }
        }
    }
}
