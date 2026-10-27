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

        private static readonly Uri DarkThemeUri = new Uri("pack://application:,,,/AiRpaStudio;component/Themes/StudioTheme.Dark.xaml", UriKind.Absolute);
        private static readonly Uri LightThemeUri = new Uri("pack://application:,,,/AiRpaStudio;component/Themes/StudioTheme.Light.xaml", UriKind.Absolute);

        internal void ApplyTheme(bool dark)
        {
            ApplyStudioThemeDictionary(dark);

            if (_dockManager == null)
            {
                return;
            }

            BrushConverter converter = new BrushConverter();

            string accent = "#DC143C";
            string accent2 = "#FF8C00";

            string background = dark ? "#2D2D30" : "#FFFFFF";
            string foreground = dark ? "#F1F1F1" : "#FF222222";
            string baseColor5 = dark ? "#3E3E42" : "#333333";
            string baseColor7 = dark ? "#3F3F46" : "#EEEEEE";

            _dockManager.Resources["AvalonDock_ThemeMetro_BaseColor1"] = (Brush)converter.ConvertFromString(background); // background
            _dockManager.Resources["AvalonDock_ThemeMetro_BaseColor2"] = (Brush)converter.ConvertFromString(foreground); // foreground
            _dockManager.Resources["AvalonDock_ThemeMetro_BaseColor3"] = (Brush)converter.ConvertFromString(accent);
            _dockManager.Resources["AvalonDock_ThemeMetro_BaseColor4"] = (Brush)converter.ConvertFromString(accent2);
            _dockManager.Resources["AvalonDock_ThemeMetro_BaseColor5"] = (Brush)converter.ConvertFromString(baseColor5);
            _dockManager.Resources["AvalonDock_ThemeMetro_BaseColor6"] = (Brush)converter.ConvertFromString("#8C8E94");
            _dockManager.Resources["AvalonDock_ThemeMetro_BaseColor7"] = (Brush)converter.ConvertFromString(baseColor7);

            _dockManager.FloatingWindows.ToList().ForEach(floatingWindow =>
            {
                floatingWindow.Resources["AvalonDock_ThemeMetro_BaseColor1"] = (Brush)converter.ConvertFromString(background); // background
                floatingWindow.Resources["AvalonDock_ThemeMetro_BaseColor2"] = (Brush)converter.ConvertFromString(foreground); // foreground
                floatingWindow.Resources["AvalonDock_ThemeMetro_BaseColor3"] = (Brush)converter.ConvertFromString(accent);
                floatingWindow.Resources["AvalonDock_ThemeMetro_BaseColor4"] = (Brush)converter.ConvertFromString(accent2);
                floatingWindow.Resources["AvalonDock_ThemeMetro_BaseColor5"] = (Brush)converter.ConvertFromString(baseColor5);
                floatingWindow.Resources["AvalonDock_ThemeMetro_BaseColor6"] = (Brush)converter.ConvertFromString("#8C8E94");
                floatingWindow.Resources["AvalonDock_ThemeMetro_BaseColor7"] = (Brush)converter.ConvertFromString(baseColor7);
            });
        }

        private static void ApplyStudioThemeDictionary(bool dark)
        {
            if (Application.Current == null)
            {
                return;
            }

            ResourceDictionary dictionary = new ResourceDictionary { Source = dark ? DarkThemeUri : LightThemeUri };

            var merged = Application.Current.Resources.MergedDictionaries;
            ResourceDictionary old = merged.FirstOrDefault(r => r.Contains("Studio.ThemeMarker"));
            if (old != null)
            {
                int index = merged.IndexOf(old);
                merged.Remove(old);
                merged.Insert(index, dictionary);
            }
            else
            {
                merged.Add(dictionary);
            }
        }

        #endregion Commands
    }
}
