using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AvalonDock;
using FW.EasyMvvm.Core;
using RPA.Logging;

namespace RPA.Models
{
    public class AppSettings : BaseModel
    {
        private DockingManager _dockManager;

        #region Singleton

        private static readonly AppSettings instance = new AppSettings();

        public static AppSettings GetInstance()
        {
            return instance;
        }

        public AppSettings()
        {
        }

        #endregion Singleton

        #region Commands

        public void OpenLog()
        {
            string logFilePath = "log.txt";
            if (File.Exists(logFilePath))
            {
                try
                {
                    _ = Process.Start(@"C:\Program Files (x86)\Notepad++\notepad++.exe", logFilePath);
                }
                catch (Exception)
                {
                    try
                    {
                        _ = Process.Start(@"C:\Windows\system32\notepad.exe", logFilePath);
                    }
                    catch (Exception ex2)
                    {
                        Log.Write(ex2);
                    }
                }
            }
            else
            {
                OnSendMessage("Файл не найден");
            }
        }

        internal void SetDockingManager(DockingManager dockManager)
        {
            _dockManager = dockManager;
        }

        internal void SetTheme()
        {
            if (_dockManager == null)
            {
                return;
            }

            BrushConverter converter = new BrushConverter();

            string accent = "#DC143C";
            string accent2 = "#FF8C00";

            string background = "#FFFFFF";
            string foreground = "#FF222222";

            _dockManager.Resources["AvalonDock_ThemeMetro_BaseColor1"] = (Brush)converter.ConvertFromString(background); // background
            _dockManager.Resources["AvalonDock_ThemeMetro_BaseColor2"] = (Brush)converter.ConvertFromString(foreground); // foreground
            _dockManager.Resources["AvalonDock_ThemeMetro_BaseColor3"] = (Brush)converter.ConvertFromString(accent);
            _dockManager.Resources["AvalonDock_ThemeMetro_BaseColor4"] = (Brush)converter.ConvertFromString(accent2);
            _dockManager.Resources["AvalonDock_ThemeMetro_BaseColor5"] = (Brush)converter.ConvertFromString("#333333");
            _dockManager.Resources["AvalonDock_ThemeMetro_BaseColor6"] = (Brush)converter.ConvertFromString("#8C8E94");
            _dockManager.Resources["AvalonDock_ThemeMetro_BaseColor7"] = (Brush)converter.ConvertFromString("#EEEEEE");

            _dockManager.FloatingWindows.ToList().ForEach(floatingWindow =>
            {
                floatingWindow.Resources["AvalonDock_ThemeMetro_BaseColor1"] = (Brush)converter.ConvertFromString(background); // background
                floatingWindow.Resources["AvalonDock_ThemeMetro_BaseColor2"] = (Brush)converter.ConvertFromString(foreground); // foreground
                floatingWindow.Resources["AvalonDock_ThemeMetro_BaseColor3"] = (Brush)converter.ConvertFromString(accent);
                floatingWindow.Resources["AvalonDock_ThemeMetro_BaseColor4"] = (Brush)converter.ConvertFromString(accent2);
                floatingWindow.Resources["AvalonDock_ThemeMetro_BaseColor5"] = (Brush)converter.ConvertFromString("#333333");
                floatingWindow.Resources["AvalonDock_ThemeMetro_BaseColor6"] = (Brush)converter.ConvertFromString("#8C8E94");
                floatingWindow.Resources["AvalonDock_ThemeMetro_BaseColor7"] = (Brush)converter.ConvertFromString("#EEEEEE");
            });
        }

        #endregion Commands
    }
}
