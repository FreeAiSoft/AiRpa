using System.Collections.Generic;
using FW.EasyMvvm.Core;
using RPA.Core;
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
            Models.Main main = new Models.Main();
            CustomWfDesigner designer = CustomWfDesigner.GetInstance();
            ProjectManager projectManager = ProjectManager.GetInstance();
            MvvmFactory.CreateWindow(main, new MainViewModel(), new MainView(), ViewMode.Show, new List<BaseModel>() { designer, projectManager });
        }
    }
}
