using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;

namespace FW.EasyMvvm.Core
{
    public class MvvmFactory
    {
        public static void CreateWindow(BaseModel model, BaseViewModel viewModel, IView view, ViewMode viewMode, List<BaseModel> childModels = null)
        {
            model.Run();

            viewModel.SetModel(model, view);
            (view as MetroWindow).DataContext = viewModel;
            model.SendMessage += (object sender, string e) => view.OnSendMessage(e);
            (view as MetroWindow).Loaded += (sender, e) => model.Loaded();

            childModels?.ForEach(child => RegisterModel(model, child));

            if (viewMode == ViewMode.Show)
            {
                view.Show();
            }

            if (viewMode == ViewMode.ShowDialog)
            {
                _ = view.ShowDialog();
            }
        }

        public static void RegisterModel(BaseModel parentModel, BaseModel childModel)
        {

            if (parentModel.ViewModel == null)
            {
                // нужно, чтобы обязательно краш был в этом случае, иначе в прод можно отправить с неприсоединённой вьюмоделью
                throw new Exception("parentModel.ViewModel is null");
            }

            childModel.ViewModel = parentModel.ViewModel;
            childModel.SendMessage += (object sender, string e) => parentModel.ViewModel.View.OnSendMessage(e);
        }
    }
}
