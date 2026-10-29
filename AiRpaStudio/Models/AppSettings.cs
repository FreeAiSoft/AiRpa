using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AvalonDock;
using FW.EasyMvvm.Core;
using MaterialDesignThemes.Wpf;
using RPA.Helpers;
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

        private const string FallbackPrimaryHex = "#673AB7";   // DeepPurple — соответствует BundledTheme в App.xaml
        private const string FallbackSecondaryHex = "#CDDC39";  // Lime — соответствует BundledTheme в App.xaml

        internal void ApplyTheme(bool dark)
        {
            ApplyMaterialDesignTheme(dark);

            // Алиасы Studio.* и системные кисти пересобираются из новой палитры: в словаре должны
            // лежать настоящие Brush, иначе WPF падает на Style-сеттерах (см. StudioBrushes).
            StudioBrushes.ApplyToApplication();

            if (_dockManager == null)
            {
                return;
            }

            BrushConverter converter = new BrushConverter();

            string background = GetResourceHex("MaterialDesign.Brush.Background", dark ? "#2D2D30" : "#FFFFFF");
            string foreground = GetResourceHex("MaterialDesign.Brush.Foreground", dark ? "#F1F1F1" : "#FF222222");
            string accent = GetResourceHex("MaterialDesign.Brush.Primary", FallbackPrimaryHex);
            string accent2 = GetResourceHex("MaterialDesign.Brush.Secondary", FallbackSecondaryHex);
            string baseColor5 = GetResourceHex("MaterialDesign.Brush.ToolBar.Background", dark ? "#3E3E42" : "#EEEEEE");
            string baseColor7 = GetResourceHex("MaterialDesign.Brush.Card.Border", dark ? "#3F3F46" : "#EEEEEE");

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

        private static void ApplyMaterialDesignTheme(bool dark)
        {
            PaletteHelper paletteHelper = new PaletteHelper();
            Theme newTheme = Theme.Create(
                dark ? BaseTheme.Dark : BaseTheme.Light,
                Color.FromRgb(103, 58, 183),   // DeepPurple
                Color.FromRgb(205, 220, 57));  // Lime
            paletteHelper.SetTheme(newTheme);
        }

        private static string GetResourceHex(string key, string fallback)
        {
            if (Application.Current?.Resources[key] is SolidColorBrush brush)
            {
                return brush.Color.ToString();
            }

            return fallback;
        }

        #endregion Commands
    }
}
