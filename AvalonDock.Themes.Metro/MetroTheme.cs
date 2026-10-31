/************************************************************************
   AvalonDock

   Copyright (C) 2007-2013 Xceed Software Inc.

   This program is provided to you under the terms of the Microsoft Public
   License (Ms-PL) as published at https://opensource.org/licenses/MS-PL
 ************************************************************************/

using System;
using System.Windows;
using System.Windows.Media;

namespace AvalonDock.Themes
{
	/// <inheritdoc/>
	public class MetroTheme : Theme
	{
		/// <inheritdoc/>
		public override Uri GetResourceUri()
		{
			return new Uri(
				"/AvalonDock.Themes.Metro;component/Theme.xaml",
				UriKind.Relative);
		}

        public override ResourceDictionary GetResourceDictionary()
        {
            ResourceDictionary resourceDictionary = new ResourceDictionary 
            { 
                Source = new Uri("/AvalonDock.Themes.Metro;component/Theme.xaml", UriKind.Relative)
            };

            // Нейтральные fallback-значения тёмной палитры студии: красный и оранжевый акцент отсюда убраны
            // (docs\Vs2026_Theme_Plan.md §2). Реальные значения пишет студия (DockingThemeBrushes) в
            // ресурсы DockingManager — они приоритетнее объединённых словарей, поэтому эти значения
            // видны только до первого применения темы.
            resourceDictionary.Add("AvalonDock_ThemeMetro_BaseColor1", Neutral(0x27, 0x27, 0x27)); // Surface
            resourceDictionary.Add("AvalonDock_ThemeMetro_BaseColor2", Neutral(0xF1, 0xF1, 0xF1)); // Foreground
            resourceDictionary.Add("AvalonDock_ThemeMetro_BaseColor3", Neutral(0x40, 0x40, 0x46)); // Selected
            resourceDictionary.Add("AvalonDock_ThemeMetro_BaseColor4", Neutral(0x33, 0x36, 0x38)); // Hover
            resourceDictionary.Add("AvalonDock_ThemeMetro_BaseColor5", Neutral(0x2E, 0x2E, 0x2F)); // SurfaceAlt
            resourceDictionary.Add("AvalonDock_ThemeMetro_BaseColor6", Neutral(0x48, 0x48, 0x4F)); // SeparatorStrong
            resourceDictionary.Add("AvalonDock_ThemeMetro_BaseColor7", Neutral(0x37, 0x37, 0x37)); // Separator
            resourceDictionary.Add("AvalonDock_Base", Neutral(0x2E, 0x2E, 0x2F));
            resourceDictionary.Add("AvalonDock_Hover", Neutral(0x33, 0x36, 0x38));
            resourceDictionary.Add("AvalonDock_Active", Neutral(0x27, 0x27, 0x27));

            return resourceDictionary;
        }

        /// <summary>Нейтральная замороженная кисть: fallback-значение без акцента (байты RGB).</summary>
        private static Brush Neutral(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
