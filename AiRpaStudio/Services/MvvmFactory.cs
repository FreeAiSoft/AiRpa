using System;
using System.Collections.Generic;
using System.Windows;

namespace Agent.Mvvm
{
    /// <summary>
    /// Host-side (WPF) wiring between a model, its view model and its view.
    /// The shared Agent.Mvvm package is UI-free, so the window wiring lives here in the studio.
    /// </summary>
    public class MvvmFactory
    {
        public static void CreateWindow(BaseModel model, BaseViewModel viewModel, IView view, ViewMode viewMode, List<BaseModel> childModels = null)
        {
            model.Run();

            viewModel.SetModel(model, view);
            (view as Window).DataContext = viewModel;
            model.ShowDialog += (object sender, string e) => view.ShowDialogMessage(e);
            (view as Window).Loaded += (sender, e) => model.Loaded();

            childModels?.ForEach(child => RegisterModel(model, child));

            if (viewMode == ViewMode.Show)
                view.Show();

            if (viewMode == ViewMode.ShowDialog)
                _ = view.ShowDialog();
        }

        public static void RegisterModel(BaseModel parentModel, BaseModel childModel)
        {
            if (parentModel.ViewModel == null)
            {
                // Must crash here: shipping a model without a view model is a bug.
                throw new Exception("parentModel.ViewModel is null");
            }

            childModel.ViewModel = parentModel.ViewModel;
            childModel.ShowDialog += (object sender, string e) => parentModel.ViewModel.View.ShowDialogMessage(e);
        }
    }
}