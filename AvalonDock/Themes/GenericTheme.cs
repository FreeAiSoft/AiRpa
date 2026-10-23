/************************************************************************
   AvalonDock

   Copyright (C) 2007-2013 Xceed Software Inc.

   This program is provided to you under the terms of the Microsoft Public
   License (Ms-PL) as published at https://opensource.org/licenses/MS-PL
 ************************************************************************/

using System;
using System.Windows;

namespace AvalonDock.Themes
{
    /// <inheritdoc/>
    public class GenericTheme : Theme
    {
        public override ResourceDictionary GetResourceDictionary()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public override Uri GetResourceUri()
        {
            return new Uri("/AvalonDock;component/Themes/generic.xaml", UriKind.Relative);
        }
    }
}