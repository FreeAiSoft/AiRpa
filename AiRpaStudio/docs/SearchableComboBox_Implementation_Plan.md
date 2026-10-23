# План реализации SearchableComboBox Control

## 1. XAML Структура (SearchableComboBox.xaml)

### Полный код XAML

```xml
<UserControl x:Class="RPA.Controls.SearchableComboBox"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" 
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:local="clr-namespace:RPA.Controls"
             xmlns:iconPacks="http://metro.mahapps.com/winfx/xaml/iconpacks"
             mc:Ignorable="d" 
             x:Name="Root"
             d:DesignHeight="40" d:DesignWidth="300">
    
    <UserControl.Resources>
        <!-- Стиль для элементов списка -->
        <Style x:Key="SearchResultItemStyle" TargetType="ListBoxItem">
            <Setter Property="Padding" Value="10,8"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ListBoxItem">
                        <Border Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}"
                                Padding="{TemplateBinding Padding}">
                            <ContentPresenter/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter Property="Background" Value="#F5F5F5"/>
                            </Trigger>
                            <Trigger Property="IsSelected" Value="True">
                                <Setter Property="Background" Value="#E3F2FD"/>
                                <Setter Property="FontWeight" Value="SemiBold"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </UserControl.Resources>
    
    <Grid>
        <!-- Текстовое поле с поиском -->
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            
            <!-- Иконка поиска -->
            <Border Grid.Column="0" 
                    Background="White"
                    BorderBrush="#CCCCCC"
                    BorderThickness="1,1,0,1"
                    Padding="8,0,4,0">
                <iconPacks:PackIconMaterial Kind="Magnify"
                                           Width="16"
                                           Height="16"
                                           VerticalAlignment="Center"
                                           Foreground="#666666"/>
            </Border>
            
            <!-- Поле ввода -->
            <TextBox x:Name="SearchTextBox"
                     Grid.Column="1"
                     Text="{Binding SearchText, RelativeSource={RelativeSource AncestorType=UserControl}, UpdateSourceTrigger=PropertyChanged}"
                     VerticalContentAlignment="Center"
                     Padding="8,6"
                     FontSize="13"
                     BorderBrush="#CCCCCC"
                     BorderThickness="0,1,0,1"
                     GotFocus="SearchTextBox_GotFocus"
                     LostFocus="SearchTextBox_LostFocus"
                     PreviewKeyDown="SearchTextBox_PreviewKeyDown">
                <TextBox.Style>
                    <Style TargetType="TextBox">
                        <Setter Property="Template">
                            <Setter.Value>
                                <ControlTemplate TargetType="TextBox">
                                    <Border Background="{TemplateBinding Background}"
                                            BorderBrush="{TemplateBinding BorderBrush}"
                                            BorderThickness="{TemplateBinding BorderThickness}">
                                        <Grid>
                                            <ScrollViewer x:Name="PART_ContentHost"
                                                         VerticalAlignment="Center"/>
                                            <TextBlock Text="{Binding Placeholder, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                                      Foreground="#999999"
                                                      Padding="8,6"
                                                      FontSize="13"
                                                      VerticalAlignment="Center"
                                                      IsHitTestVisible="False">
                                                <TextBlock.Style>
                                                    <Style TargetType="TextBlock">
                                                        <Setter Property="Visibility" Value="Collapsed"/>
                                                        <Style.Triggers>
                                                            <DataTrigger Binding="{Binding Text, RelativeSource={RelativeSource TemplatedParent}}" Value="">
                                                                <Setter Property="Visibility" Value="Visible"/>
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </TextBlock.Style>
                                            </TextBlock>
                                        </Grid>
                                    </Border>
                                    <ControlTemplate.Triggers>
                                        <Trigger Property="IsFocused" Value="True">
                                            <Setter Property="BorderBrush" Value="#007ACC"/>
                                        </Trigger>
                                    </ControlTemplate.Triggers>
                                </ControlTemplate>
                            </Setter.Value>
                        </Setter>
                    </Style>
                </TextBox.Style>
            </TextBox>
            
            <!-- Иконка dropdown -->
            <Border Grid.Column="2"
                    Background="White"
                    BorderBrush="#CCCCCC"
                    BorderThickness="0,1,1,1"
                    Padding="4,0,8,0"
                    Cursor="Hand"
                    MouseDown="DropdownIcon_MouseDown">
                <iconPacks:PackIconMaterial x:Name="DropdownIcon"
                                           Kind="ChevronDown"
                                           Width="16"
                                           Height="16"
                                           VerticalAlignment="Center"
                                           Foreground="#666666"/>
            </Border>
        </Grid>
        
        <!-- Popup с результатами -->
        <Popup x:Name="ResultsPopup"
               PlacementTarget="{Binding ElementName=SearchTextBox}"
               Placement="Bottom"
               AllowsTransparency="True"
               StaysOpen="False"
               Width="{Binding ActualWidth, RelativeSource={RelativeSource AncestorType=UserControl}}"
               MaxHeight="300">
            <Border Background="White"
                    BorderBrush="#CCCCCC"
                    BorderThickness="1"
                    CornerRadius="0,0,4,4"
                    Effect="{DynamicResource DropShadowEffect}">
                <Grid>
                    <ListBox x:Name="ResultsListBox"
                             ItemsSource="{Binding FilteredItems, RelativeSource={RelativeSource AncestorType=UserControl}}"
                             SelectedItem="{Binding SelectedItem, RelativeSource={RelativeSource AncestorType=UserControl}}"
                             BorderThickness="0"
                             MaxHeight="280"
                             ScrollViewer.VerticalScrollBarVisibility="Auto"
                             ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                             ItemContainerStyle="{StaticResource SearchResultItemStyle}"
                             SelectionChanged="ResultsListBox_SelectionChanged"
                             VirtualizingPanel.IsVirtualizing="True"
                             VirtualizingPanel.VirtualizationMode="Recycling">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding}" 
                                          TextTrimming="CharacterEllipsis"
                                          FontSize="13"/>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                    
                    <!-- Сообщение "Ничего не найдено" -->
                    <TextBlock Text="Ничего не найдено"
                              FontSize="13"
                              Foreground="#999999"
                              Padding="12"
                              HorizontalAlignment="Center"
                              VerticalAlignment="Center">
                        <TextBlock.Style>
                            <Style TargetType="TextBlock">
                                <Setter Property="Visibility" Value="Collapsed"/>
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding FilteredItems.Count, RelativeSource={RelativeSource AncestorType=UserControl}}" Value="0">
                                        <Setter Property="Visibility" Value="Visible"/>
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </TextBlock.Style>
                    </TextBlock>
                </Grid>
            </Border>
        </Popup>
    </Grid>
</UserControl>
```

## 2. Code-Behind (SearchableComboBox.xaml.cs)

### Полный код C#

```csharp
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RPA.Controls
{
    /// <summary>
    /// ComboBox с функцией поиска
    /// </summary>
    public partial class SearchableComboBox : UserControl
    {
        public SearchableComboBox()
        {
            InitializeComponent();
            FilteredItems = new ObservableCollection<string>();
        }

        #region Dependency Properties

        /// <summary>
        /// Источник данных
        /// </summary>
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(ObservableCollection<string>),
                typeof(SearchableComboBox),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public ObservableCollection<string> ItemsSource
        {
            get => (ObservableCollection<string>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (SearchableComboBox)d;
            
            // Отписываемся от старой коллекции
            if (e.OldValue is ObservableCollection<string> oldCollection)
            {
                oldCollection.CollectionChanged -= control.ItemsSource_CollectionChanged;
            }
            
            // Подписываемся на новую коллекцию
            if (e.NewValue is ObservableCollection<string> newCollection)
            {
                newCollection.CollectionChanged += control.ItemsSource_CollectionChanged;
                control.UpdateFilteredItems();
            }
        }

        private void ItemsSource_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateFilteredItems();
        }

        /// <summary>
        /// Выбранный элемент
        /// </summary>
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(
                nameof(SelectedItem),
                typeof(string),
                typeof(SearchableComboBox),
                new FrameworkPropertyMetadata(null, 
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedItemChanged));

        public string SelectedItem
        {
            get => (string)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (SearchableComboBox)d;
            var newValue = e.NewValue as string;
            
            if (!string.IsNullOrEmpty(newValue))
            {
                control.SearchText = newValue;
            }
            
            control.SelectionChanged?.Invoke(control, new SelectionChangedEventArgs(
                Selector.SelectionChangedEvent,
                e.OldValue != null ? new[] { e.OldValue } : Array.Empty<object>(),
                e.NewValue != null ? new[] { e.NewValue } : Array.Empty<object>()));
        }

        /// <summary>
        /// Placeholder текст
        /// </summary>
        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register(
                nameof(Placeholder),
                typeof(string),
                typeof(SearchableComboBox),
                new PropertyMetadata("Search..."));

        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        /// <summary>
        /// Текст поиска
        /// </summary>
        public static readonly DependencyProperty SearchTextProperty =
            DependencyProperty.Register(
                nameof(SearchText),
                typeof(string),
                typeof(SearchableComboBox),
                new PropertyMetadata(string.Empty, OnSearchTextChanged));

        public string SearchText
        {
            get => (string)GetValue(SearchTextProperty);
            set => SetValue(SearchTextProperty, value);
        }

        private static void OnSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (SearchableComboBox)d;
            control.UpdateFilteredItems();
        }

        /// <summary>
        /// Отфильтрованные элементы
        /// </summary>
        public static readonly DependencyProperty FilteredItemsProperty =
            DependencyProperty.Register(
                nameof(FilteredItems),
                typeof(ObservableCollection<string>),
                typeof(SearchableComboBox),
                new PropertyMetadata(null));

        public ObservableCollection<string> FilteredItems
        {
            get => (ObservableCollection<string>)GetValue(FilteredItemsProperty);
            set => SetValue(FilteredItemsProperty, value);
        }

        #endregion

        #region Events

        /// <summary>
        /// Событие изменения выбора
        /// </summary>
        public event SelectionChangedEventHandler SelectionChanged;

        #endregion

        #region Private Methods

        /// <summary>
        /// Обновляет отфильтрованный список
        /// </summary>
        private void UpdateFilteredItems()
        {
            if (ItemsSource == null)
            {
                FilteredItems.Clear();
                return;
            }

            var searchText = SearchText ?? string.Empty;
            
            if (string.IsNullOrWhiteSpace(searchText))
            {
                // Показываем все элементы
                FilteredItems.Clear();
                foreach (var item in ItemsSource)
                {
                    FilteredItems.Add(item);
                }
            }
            else
            {
                // Фильтруем по началу строки (case-insensitive)
                FilteredItems.Clear();
                var filtered = ItemsSource
                    .Where(item => item.StartsWith(searchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                foreach (var item in filtered)
                {
                    FilteredItems.Add(item);
                }
            }
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Обработчик получения фокуса текстовым полем
        /// </summary>
        private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            ResultsPopup.IsOpen = true;
            SearchTextBox.SelectAll();
        }

        /// <summary>
        /// Обработчик потери фокуса текстовым полем
        /// </summary>
        private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Popup закроется автоматически через StaysOpen="False"
        }

        /// <summary>
        /// Обработчик нажатия клавиш в текстовом поле
        /// </summary>
        private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && ResultsListBox.Items.Count > 0)
            {
                // Перемещение вниз по списку
                ResultsListBox.Focus();
                ResultsListBox.SelectedIndex = 0;
                var item = ResultsListBox.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                item?.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // Закрытие popup
                ResultsPopup.IsOpen = false;
                SearchTextBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && ResultsListBox.SelectedItem != null)
            {
                // Выбор элемента
                SelectedItem = ResultsListBox.SelectedItem as string;
                ResultsPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        /// <summary>
        /// Обработчик изменения выбора в списке
        /// </summary>
        private void ResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                SelectedItem = e.AddedItems[0] as string;
                ResultsPopup.IsOpen = false;
            }
        }

        /// <summary>
        /// Обработчик клика на иконку dropdown
        /// </summary>
        private void DropdownIcon_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ResultsPopup.IsOpen)
            {
                ResultsPopup.IsOpen = false;
            }
            else
            {
                SearchTextBox.Focus();
            }
        }

        #endregion
    }
}
```

## 3. Интеграция в проект

### 3.1. Добавление в RPA.csproj

```xml
<Page Include="Controls\SearchableComboBox.xaml">
  <SubType>Designer</SubType>
  <Generator>MSBuild:Compile</Generator>
</Page>
```

```xml
<Compile Include="Controls\SearchableComboBox.xaml.cs">
  <DependentUpon>SearchableComboBox.xaml</DependentUpon>
</Compile>
```

### 3.2. Использование в AiAgentChatControl

#### Шаг 1: Добавить namespace в XAML
```xml
xmlns:controls="clr-namespace:RPA.Controls"
```

#### Шаг 2: Заменить ComboBox на SearchableComboBox

**До:**
```xml
<ComboBox Width="250"
          ItemsSource="{Binding AvailableModels}"
          SelectedItem="{Binding SelectedModel}"
          DisplayMemberPath="DisplayName"
          VerticalAlignment="Center"
          ToolTip="Выберите модель AI"/>
```

**После (вариант с адаптером):**
```xml
<controls:SearchableComboBox 
    Width="250"
    ItemsSource="{Binding ModelDisplayNames}"
    SelectedItem="{Binding SelectedModelDisplayName}"
    Placeholder="Поиск модели..."
    VerticalAlignment="Center"
    ToolTip="Выберите модель AI"/>
```

#### Шаг 3: Добавить свойства в ViewModel

```csharp
// В AiAgentChatViewModel.cs

/// <summary>
/// Список имен моделей для SearchableComboBox
/// </summary>
public ObservableCollection<string> ModelDisplayNames { get; }

/// <summary>
/// Выбранное имя модели
/// </summary>
public string SelectedModelDisplayName
{
    get => SelectedModel?.DisplayName;
    set
    {
        if (!string.IsNullOrEmpty(value))
        {
            var model = AvailableModels.FirstOrDefault(m => m.DisplayName == value);
            if (model != null)
            {
                SelectedModel = model;
            }
        }
    }
}

// В конструкторе
ModelDisplayNames = new ObservableCollection<string>();

// В LoadModelsAsync после загрузки моделей
Application.Current.Dispatcher.Invoke(() =>
{
    ModelDisplayNames.Clear();
    foreach (var model in models)
    {
        ModelDisplayNames.Add(model.DisplayName);
    }
});
```

## 4. Тестовый пример

### TestWindow.xaml
```xml
<Window x:Class="RPA.TestWindow"
        xmlns:controls="clr-namespace:RPA.Controls"
        Title="SearchableComboBox Test" Height="300" Width="400">
    <StackPanel Margin="20">
        <TextBlock Text="Выберите модель:" Margin="0,0,0,8"/>
        
        <controls:SearchableComboBox 
            x:Name="TestComboBox"
            Placeholder="Начните вводить название модели..."
            SelectionChanged="TestComboBox_SelectionChanged"/>
        
        <TextBlock Margin="0,20,0,0">
            <Run Text="Выбрано: "/>
            <Run Text="{Binding SelectedItem, ElementName=TestComboBox}" FontWeight="Bold"/>
        </TextBlock>
    </StackPanel>
</Window>
```

### TestWindow.xaml.cs
```csharp
public partial class TestWindow : Window
{
    public TestWindow()
    {
        InitializeComponent();
        
        // Тестовые данные
        var models = new ObservableCollection<string>
        {
            "anthropic/claude-sonnet-4.5",
            "anthropic/claude-3.5-sonnet",
            "anthropic/claude-3-opus",
            "openai/gpt-4-turbo",
            "openai/gpt-4",
            "openai/gpt-3.5-turbo",
            "meta-llama/llama-3.1-70b",
            "meta-llama/llama-3.1-8b",
            "google/gemini-pro",
            "mistralai/mistral-large"
        };
        
        TestComboBox.ItemsSource = models;
    }
    
    private void TestComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MessageBox.Show($"Выбрана модель: {TestComboBox.SelectedItem}");
    }
}
```

## 5. Чек-лист реализации

- [ ] Создать файл `Controls/SearchableComboBox.xaml`
- [ ] Создать файл `Controls/SearchableComboBox.xaml.cs`
- [ ] Добавить ссылки на файлы в `RPA.csproj`
- [ ] Добавить DropShadowEffect в Resources (если не существует)
- [ ] Протестировать контрол с тестовыми данными
- [ ] Добавить адаптер в AiAgentChatViewModel
- [ ] Заменить ComboBox в AiAgentChatControl.xaml
- [ ] Протестировать интеграцию с реальными данными
- [ ] Проверить keyboard navigation (стрелки, Enter, Escape)
- [ ] Проверить производительность с большим списком (1000+ элементов)

## 6. Возможные проблемы и решения

### Проблема: Popup не закрывается при клике вне контрола
**Решение**: Уже реализовано через `StaysOpen="False"`

### Проблема: Виртуализация не работает
**Решение**: Уже добавлено:
```xml
VirtualizingPanel.IsVirtualizing="True"
VirtualizingPanel.VirtualizationMode="Recycling"
```

### Проблема: Медленная фильтрация на больших списках
**Решение**: Использовать Task.Run для асинхронной фильтрации:
```csharp
await Task.Run(() => {
    var filtered = ItemsSource
        .Where(item => item.StartsWith(searchText, StringComparison.OrdinalIgnoreCase))
        .ToList();
    
    Dispatcher.Invoke(() => {
        FilteredItems.Clear();
        foreach (var item in filtered)
            FilteredItems.Add(item);
    });
});
```

## 7. Следующие шаги

После одобрения этого плана:
1. Переключиться в **Code** режим
2. Создать файлы согласно плану
3. Протестировать базовую функциональность
4. Интегрировать в AiAgentChatControl
5. Провести финальное тестирование