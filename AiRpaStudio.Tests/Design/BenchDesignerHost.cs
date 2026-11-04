using System;
using System.Activities;
using System.Activities.Presentation;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Linq;
using RPA.Core;
using RPA.Core.Generation;
using RPA.Helpers;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Среда конструктора для стенда: повторяет то, что делает студия при открытии файла, но без документа
/// студии и без <c>CustomWfDesigner</c> — настоящий <see cref="WorkflowDesignerEx"/> создаётся и
/// загружается на STA-потоке стенда.
/// <para>
/// Нужна, чтобы «файл открывается и вкладка активна» (этап 3 плана
/// <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>) проверялось на живом дизайнере, а не только
/// на поддельной среде <c>DesignerOpenServiceTests</c>.
/// </para>
/// <para>
/// Экземпляр создаётся и используется только внутри домена стенда
/// (<see cref="DesignerBenchProxy"/>): <c>WorkflowDesigner</c> нельзя выносить за пределы STA-потока,
/// на котором он создан.
/// </para>
/// </summary>
internal sealed class BenchDesignerHost : IWorkflowDesignerHost
{
    private readonly List<DesignerTab> _tabs = new List<DesignerTab>();

    /// <summary>Дизайнеры, созданные стендом: по их числу проверяется «второй не создавался».</summary>
    internal List<WorkflowDesignerEx> Designers { get; } = new List<WorkflowDesignerEx>();

    public DesignerTab ActiveTab { get; private set; }

    internal int OpenCount { get; private set; }

    internal int CloseCount { get; private set; }

    internal int MarkCleanCount { get; private set; }

    /// <summary>Сервис открытия поверх этой среды (маршалинг — на диспетчер стенда).</summary>
    internal DesignerOpenService CreateService()
    {
        return new DesignerOpenService(this, new DispatcherDesignerUiThread(DesignerTestHost.Shared.Dispatcher));
    }

    /// <summary>Сколько активностей в дереве активной вкладки (0, если дерева нет).</summary>
    internal int CountActiveActivities()
    {
        try
        {
            var modelService = ActiveTab?.Designer?.Context.Services.GetService<ModelService>();
            if (modelService == null)
            {
                ActivityCountInfo = "ModelService недоступен";
                return 0;
            }

            ModelItem rootItem = modelService.Root;
            if (rootItem == null)
            {
                ActivityCountInfo = "корень модели пуст";
                return 0;
            }

            object root = rootItem.GetCurrentValue();
            ActivityCountInfo = "корень: " + (root?.GetType().FullName ?? "null");

            // У процесса с x:Class корень модели — ActivityBuilder, у безымянного — сразу Activity.
            Activity activity = root as Activity ?? (root as ActivityBuilder)?.Implementation;
            if (activity == null)
            {
                ActivityCountInfo += "; это не Activity и не ActivityBuilder";
                return 0;
            }

            int count = WorkflowInspectionServices.GetActivities(activity).Count();
            ActivityCountInfo += $"; реализация: {activity.GetType().Name}; активностей: {count}";

            return count;
        }
        catch (Exception ex)
        {
            // Активности — дополнительная информация отчёта, а не предмет падения теста.
            ActivityCountInfo = "ошибка перечисления: " + ErrorText.Of(ex);
            System.Diagnostics.Debug.WriteLine(ActivityCountInfo);
            return 0;
        }
    }

    /// <summary>Что удалось узнать о дереве активной вкладки (диагностика отчёта).</summary>
    internal string ActivityCountInfo { get; private set; } = "(не определялось)";

    public IReadOnlyList<DesignerTab> GetOpenTabs()
    {
        return _tabs.ToArray();
    }

    public DesignerTab Open(string filePath)
    {
        // Среда дизайнера — та же, что у студии; сервис ошибок записывающий, иначе WF показывает
        // модальное окно и стенд виснет (см. DesignerRenderProbe).
        var validation = new RecordingValidationErrorService();
        var designer = new WorkflowDesignerEx();
        WfDesignerEnvironment.Configure(designer, validation);
        designer.Load(filePath);

        OpenCount++;
        Designers.Add(designer);

        var tab = new DesignerTab(filePath, designer.IsEdited, designer);
        _tabs.Add(tab);


        // Поверхность, как в DesignerRenderProbe: без неё DesignerView остаётся нереализованным,
        // и его отложенная раскладка срабатывает уже при выгрузке домена стенда — WF пытается
        // показать модальное окно ошибки на мёртвом кэше шрифтов (AccessViolation в stderr).
        RealizeSurface(designer);
        return tab;
    }


    /// <summary>
    /// Показывает <c>View</c> дизайнера в невидимом <see cref="HwndSource"/> и сразу освобождает его:
    /// раскладка выполняется при живом кэше шрифтов, а не при выгрузке домена стенда (иначе WF
    /// пытается показать модальное окно ошибки и падает с <c>AccessViolation</c>).
    /// </summary>
    private static void RealizeSurface(WorkflowDesignerEx designer)
    {
        if (!(designer?.View is UIElement view))
        {
            return;
        }

        HwndSource surface = null;

        try
        {
            var root = new Grid();
            root.Children.Add(view);

            var parameters = new HwndSourceParameters("AiRpaStudio-BenchOpenHost")
            {
                Width = 1200,
                Height = 900,
                WindowStyle = 0
            };

            surface = new HwndSource(parameters) { RootVisual = root };
            root.Measure(new Size(1200, 900));
            root.Arrange(new Rect(0, 0, 1200, 900));
            root.UpdateLayout();

            DesignerTestHost.Shared.Pump(TimeSpan.FromMilliseconds(300));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Поверхность дизайнера стенда не построена: " + ErrorText.Of(ex));
        }
        finally
        {
            try
            {
                if (surface != null)
                {
                    surface.RootVisual = null;
                    surface.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Поверхность дизайнера стенда не освобождена: " + ErrorText.Of(ex));
            }
        }
    }

    public void Activate(DesignerTab tab)
    {
        ActiveTab = tab;
    }

    public void Close(DesignerTab tab)
    {
        CloseCount++;

        // Останавливаем фоновую валидацию: её таймер живёт на диспетчере стенда и продолжает тикать
        // после закрытия вкладки, а при выгрузке домена пытается показать модальное окно WF.
        tab?.Designer?.DetachValidation();

        _tabs.Remove(tab);

        if (ReferenceEquals(ActiveTab, tab))
        {
            ActiveTab = null;
        }
    }

    public void MarkClean(DesignerTab tab)
    {
        MarkCleanCount++;
        tab.IsEdited = false;

        if (tab.Designer != null)
        {
            tab.Designer.IsEdited = false;
        }
    }
}
