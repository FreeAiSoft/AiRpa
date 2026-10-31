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
        private bool _currentDarkTheme = true;

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

        internal void ApplyTheme(bool dark)
        {
            ApplyMaterialDesignTheme(dark);

            // Алиасы Studio.* и системные кисти пересобираются из новой палитры: в словаре должны
            // лежать настоящие Brush, иначе WPF падает на Style-сеттерах (см. StudioBrushes).
            StudioBrushes.ApplyToApplication();

            // Ключи дизайнера WF (F-05/F-04): та же точка, что и Studio.*, — смена темы перекрашивает
            // поверхность дизайнера, PropertyInspector и контекстные меню без пересоздания документов.
            WfDesignerTheme.ApplyToApplication();

            _currentDarkTheme = dark;

            ReapplyDockManagerResources();
        }

        /// <summary>
        /// Раскладывает кисти AvalonDock по ресурсам DockingManager и плавающих окон.
        /// Вызывать при смене темы и при появлении новых плавающих окон: откреплённая панель получает
        /// собственную копию темы Metro, в которой вкладки объявлены светлыми.
        /// </summary>
        internal void ReapplyDockManagerResources()
        {
            if (_dockManager == null)
            {
                return;
            }

            bool dark = _currentDarkTheme;

            // 1) Ресурсы приложения: из них кисти копирует ThemeBrushBridge в ядре AvalonDock
            //    (оверлей перетаскивания, Ctrl+Tab и плавающие окна получают тему в изолированные Resources).
            DockingThemeBrushes.Apply(Application.Current?.Resources, dark);

            // 2) Ресурсы менеджера и уже созданных плавающих окон.
            ApplyDockManagerResources(_dockManager.Resources);

            // Коллекция плавающих окон может измениться прямо во время LayoutChanged.
            try
            {
                _dockManager.FloatingWindows
                    .ToList()
                    .ForEach(floatingWindow => ApplyDockManagerResources(floatingWindow.Resources));
            }
            catch (Exception ex)
            {
                Log.Write(ex);
            }
        }

        private void ApplyDockManagerResources(ResourceDictionary target)
        {
            if (target == null)
            {
                return;
            }

            // Значения берутся только из палитры студии (StudioPalette): акцент MaterialDesign
            // (Primary/Secondary) в кисти AvalonDock не попадает.
            DockingThemeBrushes.Apply(target, _currentDarkTheme);
        }
        private static void ApplyMaterialDesignTheme(bool dark)
        {
            // M-04 плана: Primary/Secondary нейтральные и берутся из палитры студии — раньше здесь были
            // фиолетовый и лаймовый акцент, из-за чего он пробивался в фокус, выделение и скроллбары контролов.
            StudioThemeColors palette = StudioPalette.For(dark);

            PaletteHelper paletteHelper = new PaletteHelper();
            Theme newTheme = Theme.Create(
                dark ? BaseTheme.Dark : BaseTheme.Light,
                palette.SeparatorStrong,        // Primary: нейтральный серый (было фиолетовым)
                palette.ForegroundMuted);       // Secondary: нейтральный серый (было лаймовым)
            paletteHelper.SetTheme(newTheme);
        }

        #endregion Commands
    }
}
