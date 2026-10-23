using System;
using Microsoft.Win32;

namespace RPA.Helpers
{
    internal class IOHelper
    {
        internal static string GetFilePath(string fileFilter = null)
        {
            string path = null;
            try
            {
                OpenFileDialog dialog = new OpenFileDialog()
                {
                    Filter = fileFilter
                };
                bool? result = dialog.ShowDialog();

                if (result == true)
                {
                    path = dialog.FileName;
                }
            }
            catch (Exception)
            {

            }
            return path;
        }
    }
}
