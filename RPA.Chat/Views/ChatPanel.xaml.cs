using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RPA.Chat.ViewModels;

namespace RPA.Chat.Views
{
    /// <summary>
    /// Dockable chat panel host. Thin view: creates the <see cref="ChatViewModel"/>,
    /// assigns it as DataContext, and handles only visual concerns (auto-scroll, Enter-to-send).
    /// All business logic lives in the view model.
    /// </summary>
    public partial class ChatPanel : UserControl
    {
        private ChatViewModel _viewModel;
        private bool _chatAutoScroll = true;

        private static readonly Uri DarkThemeUri = new Uri("pack://application:,,,/RPA.Chat;component/Themes/ChatTheme.Dark.xaml", UriKind.Absolute);
        private static readonly Uri LightThemeUri = new Uri("pack://application:,,,/RPA.Chat;component/Themes/ChatTheme.Light.xaml", UriKind.Absolute);

        public ChatPanel()
        {
            InitializeComponent();

            _viewModel = new ChatViewModel();
            DataContext = _viewModel;

            _viewModel.MessageAppended += OnMessageAppended;
            _viewModel.SettingsSaved += OnSettingsSaved;
            _viewModel.ThemeChanged += OnThemeChanged;

            ChatMessages.ScrollChanged += OnChatScrollChanged;
            ChatMessages.CopyMessageRequested += OnCopyMessageRequested;
            ChatMessages.QuestionOptionSelected += OnQuestionOptionSelected;
            ChatMessages.QuestionSkipped += OnQuestionSkipped;

            ApplyTheme(_viewModel.IsDarkTheme);
        }

        public ChatViewModel ViewModel => _viewModel;

        private void OnMessageAppended()
        {
            if (_chatAutoScroll)
                ChatMessages.ScrollToEnd();
        }

        private void OnSettingsSaved()
        {
            // The host may react (e.g. show a toast); nothing visual to do here.
        }

        private void OnThemeChanged()
        {
            ApplyTheme(_viewModel.IsDarkTheme);
        }

        private void ApplyTheme(bool dark)
        {
            var dictionary = new ResourceDictionary { Source = dark ? DarkThemeUri : LightThemeUri };
            if (Resources.MergedDictionaries.Count > 0)
                Resources.MergedDictionaries[0] = dictionary;
            else
                Resources.MergedDictionaries.Add(dictionary);
        }

        private void OnChatScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeightChange > 0 && Math.Abs(e.ExtentHeight - e.ViewportHeight - e.VerticalOffset) <= 1.0)
            {
                _chatAutoScroll = true;
            }
            else if (e.ExtentHeightChange == 0 && e.ExtentHeight - e.ViewportHeight - e.VerticalOffset > 1.0)
            {
                _chatAutoScroll = false;
            }
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                if (_viewModel.SendCommand.CanExecute(null))
                    _viewModel.SendCommand.Execute(null);
            }
        }

        private void OnNoticeClose(object sender, RoutedEventArgs e)
        {
            _viewModel.ClearNotice();
        }

        private void OnCommitWarningClose(object sender, RoutedEventArgs e)
        {
            _viewModel.CommitCloseCommand.Execute(null);
        }

        private void OnCommitResultClose(object sender, RoutedEventArgs e)
        {
            _viewModel.CommitResultCloseCommand.Execute(null);
        }

        private void OnCopyMessageRequested(RPA.Chat.ChatMessage message)
        {
            _viewModel.CopyMessageCommand.Execute(message);
        }

        private void OnQuestionOptionSelected(string option)
        {
            _viewModel.AnswerQuestionCommand.Execute(option);
        }

        private void OnQuestionSkipped()
        {
            _viewModel.SkipQuestionCommand.Execute(null);
        }
    }
}

