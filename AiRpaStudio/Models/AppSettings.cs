using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AvalonDock;
using ControlzEx.Theming;
using FW.EasyMvvm.Core;
using RPA.Logging;
using RPA.Properties;

namespace RPA.Models
{
    public class AppSettings : BaseModel
    {
        public event Action<string> ChangeTheme;

        #region MVVM

        #region Fields

        private string lightOrDark;
        private string selectedTheme;

        #endregion Fields

        #region Properties

        public string LightOrDark
        {
            get => string.IsNullOrEmpty(lightOrDark) ? "Light" : lightOrDark;
            set
            {
                lightOrDark = value;
                UpdateSettings();
                OnPropertyChanged();
                OnPropertyChanged(nameof(AdditionalColor));
            }
        }

        public string AdditionalColor => LightOrDark == "Light" ? "#FFE5E5E5" : "#FF313131";

        public List<string> Themes { get; set; } = new List<string>();

        public string SelectedTheme
        {
            get => selectedTheme;
            set
            {
                selectedTheme = value;
                UpdateSettings();
                OnPropertyChanged();
            }
        }

        #endregion Properties

        #endregion MVVM

        private DockingManager _dockManager;

        #region Singleton

        private static readonly AppSettings instance = new AppSettings();

        public static AppSettings GetInstance()
        {
            return instance;
        }

        public AppSettings()
        {
            ThemeManager.Current.Themes
                .Where(t => t.BaseColorScheme == "Light")
                .Select(t => t.ColorScheme)
                .ToList()
                .ForEach(t => Themes.Add(t)); //просто получаем список тем

            lightOrDark = Settings.Default.LightOrDark;
            selectedTheme = string.IsNullOrEmpty(Settings.Default.Theme) ? "Steel" : Settings.Default.Theme;
            SetSettings();
        }

        #endregion Singleton

        private void UpdateSettings()
        {
            if (LightOrDark != null)
            {
                Settings.Default.LightOrDark = LightOrDark;
                Settings.Default.Theme = SelectedTheme;
            }
            Settings.Default.Save();
            SetSettings();
        }

        public void SetSettings()
        {
            if (string.IsNullOrEmpty(LightOrDark) || string.IsNullOrEmpty(SelectedTheme))
            {
                return;
            }

            string theme = $"{LightOrDark}.{SelectedTheme}";
            _ = ThemeManager.Current.ChangeTheme(Application.Current, theme);
            SetTheme();
            ChangeTheme?.Invoke("AdditionalColor");
        }

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

            string accent = Application.Current.Resources["MahApps.Brushes.Accent"].ToString();
            string accent2 = Application.Current.Resources["MahApps.Brushes.Accent2"].ToString();

            string background = LightOrDark == "Light" ? "#FFFFFF" : "#FF222222";
            string foreground = LightOrDark == "Light" ? "#FF222222" : "#FFFFFF";

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
