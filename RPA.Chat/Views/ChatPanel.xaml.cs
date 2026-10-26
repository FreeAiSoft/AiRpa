using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RPA.Chat.ViewModels;
using VibeCoder.Core.Models;

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

            GalleryChatMessages.ItemsSource = BuildGalleryMessages();

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

        private void OnPinnedClose(object sender, RoutedEventArgs e)
        {
            _viewModel.ShowHistoryCommand.Execute(null);
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

        private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Enter (no modifiers) sends the message; Shift+Enter inserts a new line
            // (the TextBox handles it because AcceptsReturn="True").
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

        /// <summary>
        /// Builds a static, realistic-looking transcript for the Gallery tab so it can
        /// showcase every <see cref="ChatMessageKind"/> renderer of the chat list.
        /// </summary>
        private static List<ChatMessage> BuildGalleryMessages()
        {
            var subagent = new SubagentStatusItem
            {
                Prompt = "Audit all usages of the legacy parser API and migrate them.",
                Status = "completed",
                ToolCalls = 3,
                LatestToolCall = "search_codebase → 'LegacyParser'",
                Result = "Migrated 12 call sites to the new API; no remaining references."
            };

            return new List<ChatMessage>
            {
                new ChatMessage
                {
                    Kind = ChatMessageKind.AgentText,
                    Mode = "Plan",
                    Reasoning = "The parser likely dereferences the input before validating it. I'll add a guard at the entry point and confirm with the existing test suite.",
                    Text = string.Empty
                },
                new ChatMessage
                {
                    Kind = ChatMessageKind.ToolCall,
                    Payload = new ToolCallPayload { ToolName = "read_file", Result = "Opened parser.cs (142 lines)" }
                },
                new ChatMessage
                {
                    Kind = ChatMessageKind.ToolBlocked,
                    Payload = new ToolCallPayload { ToolName = "execute_command", Result = "dotnet build", Blocked = true }
                },
                new ChatMessage
                {
                    Kind = ChatMessageKind.ResultAnswerPlan,
                    Mode = "Plan",
                    Text = "I'll add a null-check in `Parser.Parse` and a regression test in `ParserTests`."
                },
                new ChatMessage { Kind = ChatMessageKind.ModeSwitch, Text = "Mode: Act" },
                new ChatMessage
                {
                    Kind = ChatMessageKind.ToolCall,
                    Payload = new ToolCallPayload { ToolName = "edit_file", Result = "parser.cs: added null guard at method entry" }
                },
                new ChatMessage
                {
                    Kind = ChatMessageKind.ToolCall,
                    Payload = new ToolCallPayload { ToolName = "write_file", Result = "ParserTests.cs: added NullInput_Throws_Regression" }
                },
                new ChatMessage
                {
                    Kind = ChatMessageKind.ToolCall,
                    Payload = new ToolCallPayload { ToolName = "execute_command", Result = "dotnet test → 12 passed" }
                },
                new ChatMessage
                {
                    Kind = ChatMessageKind.ResultAnswerAct,
                    Mode = "Act",
                    Text = "Done: the parser now guards against `null` input and the regression test passes (12/12)."
                },
                new ChatMessage { Kind = ChatMessageKind.User, Text = "Run the full suite and commit the changes." },
                new ChatMessage
                {
                    Kind = ChatMessageKind.Subagent,
                    Mode = "Act",
                    Payload = new SubagentPayload { Item = subagent }
                },
                new ChatMessage
                {
                    Kind = ChatMessageKind.Question,
                    Mode = "Act",
                    Payload = new QuestionPayload
                    {
                        Question = "Should I also update the XML docs for the parser?",
                        Options = new List<string> { "Yes", "No", "Skip" }
                    }
                },
                new ChatMessage { Kind = ChatMessageKind.Compaction, Text = "Context compacted (summarized) to fit the context window." },
                new ChatMessage { Kind = ChatMessageKind.Error, Text = "Sample extension error for the gallery." },
                new ChatMessage { Kind = ChatMessageKind.SystemNotice, Text = "System notice: this is a sample gallery transcript." }
            };
        }
    }
}

