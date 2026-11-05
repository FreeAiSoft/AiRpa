using System;
using System.Diagnostics;
using System.Windows.Threading;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Измеряет, насколько долго UI-поток стенда не отвечает: таймер диспетчера тикает каждые 10 мс,
/// а разрыв между тиками и есть пауза диспетчера (шаг 0 плана
/// <c>AiRpaStudio\docs\UI02_Background_Validation_Plan.md</c>).
/// <para>
/// Прямой замер времени метода здесь не годится: вопрос не «сколько длится моя функция», а
/// «получает ли UI-поток управление, пока идёт проход валидации». Поэтому измеряется отзывчивость,
/// а не длительность: пауза 300 мс означает подвисший на треть секунды конструктор.
/// </para>
/// </summary>
internal sealed class DispatcherGapProbe : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _watch = new Stopwatch();

    private long _lastTick;
    private double _maxGapMs;

    internal DispatcherGapProbe(Dispatcher dispatcher, TimeSpan interval)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher) { Interval = interval };
        _timer.Tick += OnTick;
    }

    /// <summary>Наибольшая пауза между тиками, мс.</summary>
    internal double MaxGapMs => _maxGapMs;

    /// <summary>Сколько тиков успело пройти (0 — диспетчер не прокачивался).</summary>
    internal int TickCount { get; private set; }

    internal void Start()
    {
        _watch.Start();
        _lastTick = _watch.ElapsedTicks;
        _maxGapMs = 0;
        TickCount = 0;
        _timer.Start();
    }

    internal void Stop()
    {
        _timer.Stop();
        _watch.Stop();
    }

    private void OnTick(object sender, EventArgs e)
    {
        long now = _watch.ElapsedTicks;
        double gapMs = (now - _lastTick) * 1000.0 / Stopwatch.Frequency;
        _lastTick = now;

        TickCount++;

        // Первый тик включает задержку запуска таймера — в паузу прохода он не входит.
        if (TickCount > 1 && gapMs > _maxGapMs)
        {
            _maxGapMs = gapMs;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
