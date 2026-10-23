using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
            SearchableComboBox control = (SearchableComboBox)d;

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
            SearchableComboBox control = (SearchableComboBox)d;
            string newValue = e.NewValue as string;

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
            SearchableComboBox control = (SearchableComboBox)d;
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

            string searchText = SearchText ?? string.Empty;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                // Показываем все элементы
                FilteredItems.Clear();
                foreach (string item in ItemsSource)
                {
                    FilteredItems.Add(item);
                }
            }
            else
            {
                // Фильтруем по началу строки (case-insensitive)
                FilteredItems.Clear();
                System.Collections.Generic.List<string> filtered = ItemsSource
                    .Where(item => item.StartsWith(searchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (string item in filtered)
                {
                    FilteredItems.Add(item);
                }
            }
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Обработчик нажатия клавиш в текстовом поле
        /// </summary>
        private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && ResultsListBox.Items.Count > 0)
            {
                // Перемещение вниз по списку
                _ = ResultsListBox.Focus();
                ResultsListBox.SelectedIndex = 0;
                ListBoxItem item = ResultsListBox.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                _ = (item?.Focus());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // Закрытие popup
                ResultsPopup.IsOpen = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && ResultsListBox.SelectedItem != null)
            {
                // Выбор элемента
                SelectedItem = ResultsListBox.SelectedItem as string;
                ResultsPopup.IsOpen = false;
                SearchText = string.Empty;
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
                SearchText = string.Empty;
            }
        }

        /// <summary>
        /// Обработчик клика на кнопку выбора
        /// </summary>
        private void SelectionButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ResultsPopup.IsOpen)
            {
                ResultsPopup.IsOpen = false;
            }
            else
            {
                // Открываем popup и фокусируемся на поле поиска
                ResultsPopup.IsOpen = true;
                SearchText = string.Empty;

                // Устанавливаем фокус на поле поиска с небольшой задержкой
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    _ = SearchTextBox.Focus();
                    SearchTextBox.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
            e.Handled = true;
        }

        #endregion
    }
}
