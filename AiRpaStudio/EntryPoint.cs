using System;
using System.Collections.Generic;
using System.Threading;
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
            AppDomain.CurrentDomain.UnhandledException +=
                new UnhandledExceptionEventHandler(Log.AppDomain_CurrentDomain_UnhandledException);
            System.Windows.Forms.Application.ThreadException +=
                new ThreadExceptionEventHandler(Log.Application_ThreadException);

            Models.Main main = new Models.Main();
            CustomWfDesigner designer = CustomWfDesigner.GetInstance();
            ProjectManager projectManager = ProjectManager.GetInstance();
            MvvmFactory.CreateWindow(main, new MainViewModel(), new MainView(), ViewMode.Show, new List<BaseModel>() { designer, projectManager });
        }
    }
}
