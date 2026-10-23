using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Material.Icons;

namespace RPA.Helpers
{
    internal class PropertyWindowStyleHelper
    {
        private UIElement propertyInspector;
        private readonly MaterialIconDataProvider materialIconData = new MaterialIconDataProvider();

        public void SetPropertyStyles(UIElement propertyInspector)
        {
            this.propertyInspector = propertyInspector;
            ApplyTop();
            ApplyToolbar();
            ApplyPropertyInspectorButtonsStyle();
            ApplyPropertyInspectorFont();
            RemoveBorder();
        }

        private void ApplyToolbar()
        {
            if (propertyInspector == null)
            {
                return;
            }

            SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FAFAFA"));

            // Navigate: PropertyInspector -> Border -> Grid -> StackPanel -> PropertyToolbar
            Border firstBorder = FindVisualChildren<Border>(propertyInspector).FirstOrDefault();
            if (firstBorder != null)
            {
                Grid grid = FindVisualChildren<Grid>(firstBorder).FirstOrDefault();
                if (grid != null)
                {
                    StackPanel stackPanel = FindVisualChildren<StackPanel>(grid).FirstOrDefault();
                    if (stackPanel != null)
                    {
                        // Find PropertyToolbar element by name
                        FrameworkElement propertyToolbar = FindVisualChildren<FrameworkElement>(stackPanel)
                            .FirstOrDefault(e => e.Name == "_propertyToolBar");

                        if (propertyToolbar != null)
                        {
                            // Set background for PropertyToolbar
                            if (propertyToolbar is Panel panel)
                            {
                                panel.Background = brush;
                            }
                            else if (propertyToolbar is Control control)
                            {
                                control.Background = brush;
                            }
                            System.Diagnostics.Debug.WriteLine("Set background #FAFAFA for PropertyInspector/Border/Grid/StackPanel/PropertyToolbar");

                            // Set background for its child Border
                            Border childBorder = FindVisualChildren<Border>(propertyToolbar).FirstOrDefault();
                            if (childBorder != null)
                            {
                                childBorder.Background = brush;
                                System.Diagnostics.Debug.WriteLine("Set background #FAFAFA for PropertyToolbar's child Border");

                                // Set background for Border's child Grid
                                Grid childGrid = FindVisualChildren<Grid>(childBorder).FirstOrDefault();
                                if (childGrid != null)
                                {
                                    childGrid.Background = brush;
                                    System.Diagnostics.Debug.WriteLine("Set background #FAFAFA for Border's child Grid");
                                }
                            }
                        }
                    }
                }
            }
        }

        private void ApplyTop()
        {
            if (propertyInspector == null)
            {
                return;
            }

            SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FAFAFA"));

            // Navigate: PropertyInspector -> Border -> Grid -> StackPanel -> Border
            Border firstBorder = FindVisualChildren<Border>(propertyInspector).FirstOrDefault();
            if (firstBorder != null)
            {
                Grid grid = FindVisualChildren<Grid>(firstBorder).FirstOrDefault();
                if (grid != null)
                {
                    StackPanel stackPanel = FindVisualChildren<StackPanel>(grid).FirstOrDefault();
                    if (stackPanel != null)
                    {
                        Border targetBorder = FindVisualChildren<Border>(stackPanel).FirstOrDefault();
                        if (targetBorder != null)
                        {
                            // Set background for the Border
                            targetBorder.Background = brush;
                            System.Diagnostics.Debug.WriteLine("Set background #FAFAFA for PropertyInspector/Border/Grid/StackPanel/Border");

                            // Set background for its child Grid
                            Grid childGrid = FindVisualChildren<Grid>(targetBorder).FirstOrDefault();
                            if (childGrid != null)
                            {
                                childGrid.Background = brush;
                                System.Diagnostics.Debug.WriteLine("Set background #FAFAFA for child Grid");
                            }
                        }
                    }
                }
            }
        }

        private void ApplyPropertyInspectorButtonsStyle()
        {
            if (propertyInspector != null)
            {
                // Get SVG icon data
                string iconSvgShapePlusOutline = materialIconData.ProvideData(MaterialIconKind.ShapePlusOutline);
                string iconSvgOrderAlphabeticalAscending = materialIconData.ProvideData(MaterialIconKind.OrderAlphabeticalAscending);
                string iconSvgClose = materialIconData.ProvideData(MaterialIconKind.Close);

                // Find all controls (including RadioButtons, Buttons, TextBoxes) in the PropertyInspectorView
                List<Control> allControls = FindVisualChildren<Control>(propertyInspector).ToList();

                System.Diagnostics.Debug.WriteLine($"Found {allControls.Count} controls in PropertyInspectorView");

                // Find the specific view mode buttons by their names
                Control byCategoryButton = allControls.FirstOrDefault(c => c.Name == "PART_ByCategoryViewButton");
                Control alphaViewButton = allControls.FirstOrDefault(c => c.Name == "PART_AlphaViewButton");
                Button clearButton = (Button)allControls.FirstOrDefault(c => c.Name == "PART_SearchClearButton");

                // Find the search TextBox
                TextBox searchTextBox = allControls.OfType<TextBox>().FirstOrDefault();

                // Find and apply margin to the parent container of the buttons
                if (byCategoryButton != null && alphaViewButton != null)
                {
                    if (VisualTreeHelper.GetParent(byCategoryButton) is FrameworkElement parent)
                    {
                        parent.Margin = new Thickness(5);
                        System.Diagnostics.Debug.WriteLine($"Applied 5px margin to button container: {parent.GetType().Name}");

                        // Find and style the separator (vertical divider) near the buttons
                        if (VisualTreeHelper.GetParent(parent) is DependencyObject parentOfContainer)
                        {
                            // Look for Border or Separator elements that might be the vertical divider
                            foreach (Border border in FindVisualChildren<Border>(parentOfContainer))
                            {
                                // Check if it's a vertical separator (thin width or specific name)
                                if (border.Width <= 2 || border.Name?.Contains("Separator") == true || border.Name?.Contains("Divider") == true)
                                {
                                    border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDDDDD"));
                                    border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDDDDD"));
                                    System.Diagnostics.Debug.WriteLine($"Set separator color to #DDDDDD: {border.Name ?? "unnamed"}");
                                }
                            }

                            // Also check for Rectangle elements that might be separators
                            foreach (Rectangle rect in FindVisualChildren<Rectangle>(parentOfContainer))
                            {
                                if (rect.Width <= 2 || rect.Name?.Contains("Separator") == true || rect.Name?.Contains("Divider") == true)
                                {
                                    rect.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDDDDD"));
                                    System.Diagnostics.Debug.WriteLine($"Set separator rectangle color to #DDDDDD: {rect.Name ?? "unnamed"}");
                                }
                            }
                        }
                    }
                }

                // Apply custom style
                SolidColorBrush buttonBackgroundBrush = new SolidColorBrush(Colors.Transparent);
                SolidColorBrush buttonBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDDDDD"));

                // Set SVG icon for PART_ByCategoryViewButton (ShapePlusOutline)
                if (byCategoryButton != null)
                {
                    Border byCategoryBorder = null;

                    if (byCategoryButton is ContentControl contentControl1)
                    {
                        Viewbox iconViewbox = CreateSvgViewbox(iconSvgShapePlusOutline);
                        if (iconViewbox != null)
                        {
                            // Wrap icon in a Border to ensure border visibility
                            byCategoryBorder = new Border
                            {
                                Width = 28,
                                Height = 28,
                                Background = buttonBackgroundBrush,
                                BorderBrush = buttonBorderBrush,
                                BorderThickness = new Thickness(1),
                                Child = iconViewbox
                            };

                            contentControl1.Content = byCategoryBorder;
                            System.Diagnostics.Debug.WriteLine("Set PART_ByCategoryViewButton content to SVG icon with border");
                        }
                    }

                    byCategoryButton.Width = 28;
                    byCategoryButton.Height = 28;
                    byCategoryButton.Background = Brushes.Transparent;
                    byCategoryButton.BorderBrush = Brushes.Transparent;
                    byCategoryButton.BorderThickness = new Thickness(0);
                    byCategoryButton.Padding = new Thickness(0);
                    byCategoryButton.Margin = new Thickness(0);

                    // Add Checked/Unchecked and Hover event handlers for active state
                    if (byCategoryButton is System.Windows.Controls.Primitives.ToggleButton toggleButton1 && byCategoryBorder != null)
                    {
                        SolidColorBrush activeColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"));
                        SolidColorBrush hoverColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFEFEF"));

                        toggleButton1.Checked += (s, e) =>
                        {
                            byCategoryBorder.Background = activeColor;
                            System.Diagnostics.Debug.WriteLine("PART_ByCategoryViewButton checked - background #E0E0E0");
                        };

                        toggleButton1.Unchecked += (s, e) =>
                        {
                            byCategoryBorder.Background = buttonBackgroundBrush;
                            System.Diagnostics.Debug.WriteLine("PART_ByCategoryViewButton unchecked - background transparent");
                        };

                        // Add hover effects
                        toggleButton1.MouseEnter += (s, e) =>
                        {
                            if (toggleButton1.IsChecked != true)
                            {
                                byCategoryBorder.Background = hoverColor;
                                System.Diagnostics.Debug.WriteLine("PART_ByCategoryViewButton hover - background #EFEFEF");
                            }
                        };

                        toggleButton1.MouseLeave += (s, e) =>
                        {
                            if (toggleButton1.IsChecked != true)
                            {
                                byCategoryBorder.Background = buttonBackgroundBrush;
                                System.Diagnostics.Debug.WriteLine("PART_ByCategoryViewButton leave - background transparent");
                            }
                        };

                        // Set initial state
                        if (toggleButton1.IsChecked == true)
                        {
                            byCategoryBorder.Background = activeColor;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine("Applied style to PART_ByCategoryViewButton");
                }

                // Set SVG icon for PART_AlphaViewButton (OrderAlphabeticalAscending)
                if (alphaViewButton != null)
                {
                    Border alphaViewBorder = null;

                    if (alphaViewButton is ContentControl contentControl2)
                    {
                        Viewbox iconViewbox = CreateSvgViewbox(iconSvgOrderAlphabeticalAscending);
                        if (iconViewbox != null)
                        {
                            // Wrap icon in a Border to ensure border visibility
                            alphaViewBorder = new Border
                            {
                                Width = 28,
                                Height = 28,
                                Background = buttonBackgroundBrush,
                                BorderBrush = buttonBorderBrush,
                                BorderThickness = new Thickness(1),
                                Child = iconViewbox
                            };

                            contentControl2.Content = alphaViewBorder;
                            System.Diagnostics.Debug.WriteLine("Set PART_AlphaViewButton content to SVG icon with border");
                        }
                    }

                    alphaViewButton.Width = 28;
                    alphaViewButton.Height = 28;
                    alphaViewButton.Background = Brushes.Transparent;
                    alphaViewButton.BorderBrush = Brushes.Transparent;
                    alphaViewButton.BorderThickness = new Thickness(0);
                    alphaViewButton.Padding = new Thickness(0);
                    alphaViewButton.Margin = new Thickness(0);

                    // Add Checked/Unchecked and Hover event handlers for active state
                    if (alphaViewButton is System.Windows.Controls.Primitives.ToggleButton toggleButton2 && alphaViewBorder != null)
                    {
                        SolidColorBrush activeColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"));
                        SolidColorBrush hoverColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFEFEF"));

                        toggleButton2.Checked += (s, e) =>
                        {
                            alphaViewBorder.Background = activeColor;
                            System.Diagnostics.Debug.WriteLine("PART_AlphaViewButton checked - background #E0E0E0");
                        };

                        toggleButton2.Unchecked += (s, e) =>
                        {
                            alphaViewBorder.Background = buttonBackgroundBrush;
                            System.Diagnostics.Debug.WriteLine("PART_AlphaViewButton unchecked - background transparent");
                        };

                        // Add hover effects
                        toggleButton2.MouseEnter += (s, e) =>
                        {
                            if (toggleButton2.IsChecked != true)
                            {
                                alphaViewBorder.Background = hoverColor;
                                System.Diagnostics.Debug.WriteLine("PART_AlphaViewButton hover - background #EFEFEF");
                            }
                        };

                        toggleButton2.MouseLeave += (s, e) =>
                        {
                            if (toggleButton2.IsChecked != true)
                            {
                                alphaViewBorder.Background = buttonBackgroundBrush;
                                System.Diagnostics.Debug.WriteLine("PART_AlphaViewButton leave - background transparent");
                            }
                        };

                        // Set initial state
                        if (toggleButton2.IsChecked == true)
                        {
                            alphaViewBorder.Background = activeColor;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine("Applied style to PART_AlphaViewButton");
                }

                // Style the Clear button (Очистить)
                if (clearButton != null)
                {
                    Viewbox iconViewbox = CreateSvgViewbox(iconSvgClose, 12, "#666666");

                    Border buttonBorder = null;

                    if (iconViewbox != null)
                    {
                        // Wrap icon in a Border
                        buttonBorder = new Border
                        {
                            Width = 24,
                            Height = 24,
                            Background = buttonBackgroundBrush,
                            BorderBrush = buttonBorderBrush,
                            BorderThickness = new Thickness(1),
                            Child = iconViewbox
                        };

                        clearButton.Content = buttonBorder;
                    }
                    else
                    {
                        // Fallback: just show "X" text
                        buttonBorder = new Border
                        {
                            Width = 24,
                            Height = 24,
                            Background = buttonBackgroundBrush,
                            BorderBrush = buttonBorderBrush,
                            BorderThickness = new Thickness(1),
                            Child = new TextBlock
                            {
                                Text = "✕",
                                FontSize = 16,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center,
                                Foreground = Brushes.Black
                            }
                        };
                        clearButton.Content = buttonBorder;
                    }

                    // Override button properties to ensure content is visible
                    clearButton.Width = 24;
                    clearButton.Height = 24;
                    clearButton.BorderBrush = Brushes.Transparent;
                    clearButton.BorderThickness = new Thickness(0);
                    clearButton.Padding = new Thickness(0);
                    clearButton.Margin = new Thickness(0, 0, 5, 0);

                    // Add hover and pressed effects
                    clearButton.MouseEnter += (s, e) =>
                    {
                        buttonBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F5F5"));
                    };

                    clearButton.MouseLeave += (s, e) =>
                    {
                        if (!clearButton.IsPressed)
                        {
                            buttonBorder.Background = Brushes.Transparent;
                        }
                    };

                    clearButton.PreviewMouseDown += (s, e) =>
                    {
                        buttonBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"));
                    };

                    clearButton.PreviewMouseUp += (s, e) =>
                    {
                        buttonBorder.Background = clearButton.IsMouseOver ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F5F5")) : (Brush)Brushes.Transparent;
                    };

                    // Force the button to display content by using a simple template
                    ControlTemplate template = new ControlTemplate(typeof(Button));
                    FrameworkElementFactory factory = new FrameworkElementFactory(typeof(ContentPresenter));
                    factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                    factory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                    template.VisualTree = factory;
                    clearButton.Template = template;
                    clearButton.Background = Brushes.Transparent;

                    System.Diagnostics.Debug.WriteLine("Applied style, template and hover effects to Clear button");
                }

                // Style the search TextBox
                if (searchTextBox != null)
                {
                    // Find all Border elements that are ancestors or children of the TextBox
                    List<Border> textBoxBorders = new List<Border>();

                    // Check parent borders
                    DependencyObject current = searchTextBox;
                    for (int i = 0; i < 5; i++) // Check up to 5 levels up
                    {
                        current = VisualTreeHelper.GetParent(current);
                        if (current == null)
                        {
                            break;
                        }

                        if (current is Border border)
                        {
                            textBoxBorders.Add(border);
                        }
                    }

                    // Also find child borders (like PART_ContentHost border)
                    foreach (Border childBorder in FindVisualChildren<Border>(searchTextBox))
                    {
                        textBoxBorders.Add(childBorder);
                    }

                    System.Diagnostics.Debug.WriteLine($"Found {textBoxBorders.Count} borders around TextBox");

                    // Set initial styling
                    searchTextBox.BorderBrush = buttonBorderBrush;
                    searchTextBox.BorderThickness = new Thickness(1);
                    searchTextBox.Height = 24;
                    searchTextBox.VerticalContentAlignment = VerticalAlignment.Center;
                    System.Diagnostics.Debug.WriteLine("Set search TextBox vertical content alignment to Center");

                    foreach (Border border in textBoxBorders)
                    {
                        border.BorderBrush = buttonBorderBrush;
                        border.BorderThickness = new Thickness(1);
                        System.Diagnostics.Debug.WriteLine($"Set initial border color for border: {border.Name ?? "unnamed"}");
                    }

                    // Add focus event handlers
                    searchTextBox.GotFocus += (s, e) =>
                    {
                        SolidColorBrush focusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDDDDD"));
                        searchTextBox.BorderBrush = focusColor;
                        foreach (Border border in textBoxBorders)
                        {
                            border.BorderBrush = focusColor;
                        }
                        System.Diagnostics.Debug.WriteLine($"TextBox got focus - changed {textBoxBorders.Count + 1} borders to #DDDDDD");
                    };

                    searchTextBox.LostFocus += (s, e) =>
                    {
                        searchTextBox.BorderBrush = buttonBorderBrush;
                        foreach (Border border in textBoxBorders)
                        {
                            border.BorderBrush = buttonBorderBrush;
                        }
                        System.Diagnostics.Debug.WriteLine($"TextBox lost focus - changed {textBoxBorders.Count + 1} borders to #DDDDDD");
                    };

                    System.Diagnostics.Debug.WriteLine("Applied style and focus handlers to search TextBox");
                }
            }
        }

        private void ApplyPropertyInspectorFont()
        {
            if (propertyInspector != null)
            {
                FontFamily comicSans = new FontFamily("Consolas");

                // Apply font to all Control elements
                foreach (Control control in FindVisualChildren<Control>(propertyInspector))
                {
                    control.FontFamily = comicSans;
                }

                // Apply font to all TextBlock elements (for activity class name at the top)
                foreach (TextBlock textBlock in FindVisualChildren<TextBlock>(propertyInspector))
                {
                    textBlock.FontFamily = comicSans;
                    System.Diagnostics.Debug.WriteLine($"Set font for TextBlock: {textBlock.Name ?? "unnamed"}, Text: {textBlock.Text}");
                }

                System.Diagnostics.Debug.WriteLine("Applied Comic Sans MS font to all controls and TextBlocks in PropertyInspectorView");
            }
        }

        private void RemoveBorder()
        {
            if (propertyInspector != null)
            {
                // Find the first Border child element
                Border firstBorder = FindVisualChildren<Border>(propertyInspector).FirstOrDefault();
                if (firstBorder != null)
                {
                    firstBorder.BorderThickness = new Thickness(0);
                    System.Diagnostics.Debug.WriteLine("Set BorderThickness=0 for root border in PropertyInspectorView");
                }
            }
        }

        private Viewbox CreateSvgViewbox(string svgData, double size = 20, string colorHex = "#000000")
        {
            try
            {
                // Extract path data from SVG string
                string pathData = svgData;

                // If the string contains SVG markup, extract the path data
                if (svgData.Contains("<path"))
                {
                    int startIndex = svgData.IndexOf("d=\"");
                    if (startIndex >= 0)
                    {
                        startIndex += 3; // Skip 'd="'
                        int endIndex = svgData.IndexOf("\"", startIndex);
                        if (endIndex > startIndex)
                        {
                            pathData = svgData.Substring(startIndex, endIndex - startIndex);
                        }
                    }
                }

                // Create a Path element with the SVG data
                Path path = new Path
                {
                    Data = Geometry.Parse(pathData),
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
                    Stretch = Stretch.Uniform,
                    Width = size,
                    Height = size,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Create a Viewbox to scale the icon properly
                Viewbox viewbox = new Viewbox
                {
                    Width = size,
                    Height = size,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = path
                };

                System.Diagnostics.Debug.WriteLine($"Created SVG Viewbox (size: {size}, color: {colorHex}) with path data: {pathData.Substring(0, Math.Min(50, pathData.Length))}...");
                return viewbox;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating SVG Viewbox: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"SVG Data: {svgData}");
            }

            return null;
        }

        private IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                yield break;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild)
                {
                    yield return typedChild;
                }

                foreach (T descendant in FindVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
