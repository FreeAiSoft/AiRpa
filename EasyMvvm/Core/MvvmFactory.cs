using MahApps.Metro.Controls;
using System;

namespace EasyMvvm.Core
{
    public class MvvmFactory
    {
        public static void CreateWindow(BaseModel model, BaseViewModel viewModel, IView view, ViewMode viewMode)
        {
            model.Run();

            viewModel.SetModel(model, view);
            (view as MetroWindow).DataContext = viewModel;
            model.SendMessage += (sender, e) => view.OnSendMessageAsync(e);
            (view as MetroWindow).Closing += (sender, e) => model.Closing();
            model.Loaded();

            if (viewMode == ViewMode.Show)
            {
                view.Show();
            }

            if (viewMode == ViewMode.ShowDialog)
            {
                _ = view.ShowDialog();
            }

            model.AfterLoaded();

            _ = model.AfterLoadedAsync();
        }

        private static void MvvmFactory_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            throw new NotImplementedException();
        }

        public static void RegisterModel(BaseModel parentModel, BaseModel childModel)
        {
            if (parentModel.ViewModel == null)
            {
                // нужно, чтобы обязательно краш был в этом случае, иначе в прод можно отправить с неприсоединённой вьюмоделью
                throw new Exception("parentModel.ViewModel is null");
            }

            childModel.ViewModel = parentModel.ViewModel;
            childModel.SendMessage += (sender, e) => parentModel.ViewModel.View.OnSendMessageAsync(e);
        }
    }
}
