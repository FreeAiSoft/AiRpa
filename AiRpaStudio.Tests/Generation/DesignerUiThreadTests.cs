using System.Windows.Threading;
using AiRpaStudio.Tests.Design;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Маршалинг на UI-поток студии (<see cref="DispatcherDesignerUiThread"/>), этап 3 плана
/// конвейер генерации XAML.
/// <para>
/// Без этого <c>DesignerOpenService</c>, вызванный из MCP-потока, создавал бы
/// <c>WorkflowDesigner</c> в фоновом потоке: WPF-исключения и «пустая» диаграмма.
/// </para>
/// </summary>
[TestFixture]
public class DesignerUiThreadTests
{
    [Test]
    public void Invoke_RunsWorkOnDispatcherThread()
    {
        DesignerTestHost bench = DesignerTestHost.Shared;
        var uiThread = new DispatcherDesignerUiThread(bench.Dispatcher);

        int threadId = uiThread.Invoke(() => Thread.CurrentThread.ManagedThreadId);

        Assert.Multiple(() =>
        {
            Assert.That(threadId, Is.EqualTo(bench.ThreadId), "работа обязана выполняться на потоке диспетчера");
            Assert.That(Thread.CurrentThread.ManagedThreadId, Is.Not.EqualTo(bench.ThreadId),
                "тест вызывает сервис из чужого потока — иначе маршалинг не проверяется");
        });
    }

    [Test]
    public void Invoke_RunsInlineWhenCalledFromUiThread()
    {
        DesignerTestHost bench = DesignerTestHost.Shared;
        var uiThread = new DispatcherDesignerUiThread(bench.Dispatcher);

        // Вызов с самого UI-потока не должен уходить в очередь диспетчера: иначе ожидание внутри
        // обработчика UI-события даёт самоблокировку.
        (bool isUiThread, int threadId) = bench.Invoke(() =>
            (uiThread.IsUiThread, uiThread.Invoke(() => Thread.CurrentThread.ManagedThreadId)));

        Assert.Multiple(() =>
        {
            Assert.That(isUiThread, Is.True);
            Assert.That(threadId, Is.EqualTo(bench.ThreadId));
        });
    }

    /// <summary>
    /// Занятый UI-поток (модальное окно конструктора) — это таймаут с понятным текстом,
    /// а не вечное ожидание MCP-вызова.
    /// </summary>
    [Test]
    public void Invoke_ThrowsClearTimeoutWhenUiThreadIsBusy()
    {
        using (var busyThread = new BusyUiThread(TimeSpan.FromSeconds(1)))
        {
            var uiThread = new DispatcherDesignerUiThread(busyThread.Dispatcher, TimeSpan.FromMilliseconds(200));

            var exception = Assert.Throws<TimeoutException>(() => uiThread.Invoke(() => 1));

            Assert.Multiple(() =>
            {
                Assert.That(exception.Message, Does.Contain("UI-поток"));
                Assert.That(exception.Message, Does.Contain("0,2"), "в тексте указан таймаут");
            });
        }
    }

    /// <summary>STA-поток с диспетчером, занятым длинной операцией.</summary>
    private sealed class BusyUiThread : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);

        private Dispatcher _dispatcher;

        internal BusyUiThread(TimeSpan busy)
        {
            _thread = new Thread(() =>
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(_dispatcher));

                _dispatcher.BeginInvoke(new Action(() => Thread.Sleep(busy)));
                _ready.Set();

                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "DesignerUiThreadTests.Busy"
            };

            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            if (!_ready.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("STA-поток теста не поднялся");
            }
        }

        internal Dispatcher Dispatcher => _dispatcher;

        public void Dispose()
        {
            try
            {
                _dispatcher?.InvokeShutdown();
            }
            catch (Exception)
            {
                // Диспетчер уже остановлен — ничего делать не нужно.
            }

            _thread.Join(TimeSpan.FromSeconds(10));
        }
    }
}
