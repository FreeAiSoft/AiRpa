using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using RPA.NuGet.Models;
using RPA.NuGet.ViewModels;

namespace RPA.NuGet.Views
{
    /// <summary>
    /// Dockable NuGet package manager panel: search, view, install, update and
    /// uninstall packages for the current project (packages.config or PackageReference).
    /// </summary>
    public partial class NuGetPanel : UserControl
    {
        private readonly NuGetPanelViewModel _viewModel;

        public NuGetPanel()
        {
            InitializeComponent();
            _viewModel = new NuGetPanelViewModel();
            DataContext = _viewModel;
        }

        /// <summary>Sets the panel's project from a .csproj path.</summary>
        public bool SetProject(string projectPath)
        {
            return _viewModel.LoadProject(projectPath);
        }

        /// <summary>Auto-detects a .csproj inside the given folder.</summary>
        public bool SetProjectFolder(string folder)
        {
            return _viewModel.TryDetectProject(folder);
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.SearchAsync();
        }

        private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await _viewModel.SearchAsync();
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is NuGetPackageInfo package)
                await _viewModel.InstallAsync(package);
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is NuGetPackageReference package)
                await _viewModel.UpdateAsync(package);
        }

        private void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is NuGetPackageReference package)
                _viewModel.Uninstall(package);
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select project",
                Filter = "Project files (*.csproj)|*.csproj|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
                _viewModel.LoadProject(dialog.FileName);
        }
    }
}
