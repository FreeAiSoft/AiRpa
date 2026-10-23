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

            BrushConverter converter = new BrushConverter();

            string accent = Application.Current.Resources["MahApps.Brushes.Accent"].ToString();
            string accent2 = Application.Current.Resources["MahApps.Brushes.Accent2"].ToString();

            resourceDictionary.Add("AvalonDock_ThemeMetro_BaseColor1", (Brush)converter.ConvertFromString("#FFFFFF"));
            resourceDictionary.Add("AvalonDock_ThemeMetro_BaseColor2", (Brush)converter.ConvertFromString("#000000"));
            resourceDictionary.Add("AvalonDock_ThemeMetro_BaseColor3", (Brush)converter.ConvertFromString(accent));
            resourceDictionary.Add("AvalonDock_ThemeMetro_BaseColor4", (Brush)converter.ConvertFromString(accent2));
            resourceDictionary.Add("AvalonDock_ThemeMetro_BaseColor5", (Brush)converter.ConvertFromString("#333333"));
            resourceDictionary.Add("AvalonDock_ThemeMetro_BaseColor6", (Brush)converter.ConvertFromString("#8C8E94"));
            resourceDictionary.Add("AvalonDock_ThemeMetro_BaseColor7", (Brush)converter.ConvertFromString("#EEEEEE"));
            resourceDictionary.Add("AvalonDock_Base", (Brush)converter.ConvertFromString("#FAFAFA"));
            resourceDictionary.Add("AvalonDock_Hover", (Brush)converter.ConvertFromString("#DDDDDD"));
            resourceDictionary.Add("AvalonDock_Active", (Brush)converter.ConvertFromString("#E0E0E0"));

            return resourceDictionary;
        }
    }
}
