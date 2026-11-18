using System;
using System.Windows.Forms;
using Agent.Mvvm;
using RPA.Helpers;

namespace RPA.Models
{
    public class VsProjectOpen : BaseModel
    {
        private string filePath;

        public string FilePath
        {
            get => filePath;
            set
            {
                filePath = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoadIntoStudio { get; set; }
        public bool IsOpenInVisualStudio { get; set; }

        public void SetFile()
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Visual Studio files (*.sln;*.csproj)|*.sln;*.csproj|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                FilePath = dialog.FileName;
            }
        }

        public void OpenInVisualStudio()
        {
            if (!ValidatePath())
            {
                return;
            }

            try
            {
                VsProject.VsProjectOpener.OpenInVisualStudio(FilePath);
                IsOpenInVisualStudio = true;
            }
            catch (Exception ex)
            {
                ShowDialogMessage(ErrorText.Of(ex));
                return;
            }

            ((System.Windows.Window)ViewModel.View)?.Close();
        }

        public void LoadIntoStudio()
        {
            if (!ValidatePath())
            {
                return;
            }

            IsLoadIntoStudio = true;
            ((System.Windows.Window)ViewModel.View)?.Close();
        }

        private bool ValidatePath()
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                ShowDialogMessage(LocalizeHelper.Get("lang_msg_specify_project_file"));
                return false;
            }

            return true;
        }
    }
}
