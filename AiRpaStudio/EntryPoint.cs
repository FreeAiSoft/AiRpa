using System.Collections.Generic;
using FW.EasyMvvm.Core;
using RPA.Core;
using RPA.Logging;
using RPA.Models;
using RPA.ViewModels;
using RPA.Views;

namespace RPA
{
    internal class EntryPoint
    {
        internal void Run()
        {
            // Обработчики необработанных исключений подключает конструктор App (см. App.xaml.cs):
            // подписка здесь пропускала падения при разборе App.xaml и создании первого окна,
            // а WinForms-событие Application.ThreadException для WPF-приложения не срабатывает.
            StartupTimer.Mark("EntryPoint.Run: начало");
            Models.Main main = new Models.Main();
            StartupTimer.Mark("EntryPoint.Run: new Models.Main() создан");
            CustomWfDesigner designer = CustomWfDesigner.GetInstance();
            StartupTimer.Mark("EntryPoint.Run: CustomWfDesigner.GetInstance()");
            ProjectManager projectManager = ProjectManager.GetInstance();
            StartupTimer.Mark("EntryPoint.Run: ProjectManager.GetInstance()");
            MvvmFactory.CreateWindow(main, new MainViewModel(), new MainView(), ViewMode.Show, new List<BaseModel>() { designer, projectManager });
            StartupTimer.Mark("EntryPoint.Run: MvvmFactory.CreateWindow() завершён");
        }
    }
}
