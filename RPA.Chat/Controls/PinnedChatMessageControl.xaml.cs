using System.Windows;
using System.Windows.Controls;

namespace RPA.Chat.Controls
{
    /// <summary>
    /// Pinned first chat message shown above the main <see cref="ChatMessagesControl"/>.
    /// Renders the user's opening message together with a context-window progress bar,
    /// so the prompt stays visible while the rest of the transcript scrolls below it.
    /// </summary>
    public partial class PinnedChatMessageControl : UserControl
    {
        public static readonly DependencyProperty MessageTextProperty = DependencyProperty.Register(
            nameof(MessageText), typeof(string), typeof(PinnedChatMessageControl),
            new PropertyMetadata(string.Empty, OnMessageTextChanged));

        private static readonly DependencyPropertyKey FirstLinePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(FirstLine), typeof(string), typeof(PinnedChatMessageControl),
            new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty FirstLineProperty = FirstLinePropertyKey.DependencyProperty;

        public static readonly DependencyProperty ProgressValueProperty = DependencyProperty.Register(
            nameof(ProgressValue), typeof(double), typeof(PinnedChatMessageControl),
            new PropertyMetadata(0.0));

        public static readonly DependencyProperty ProgressMaximumProperty = DependencyProperty.Register(
            nameof(ProgressMaximum), typeof(double), typeof(PinnedChatMessageControl),
            new PropertyMetadata(100.0));

        public static readonly DependencyProperty ContextTextProperty = DependencyProperty.Register(
            nameof(ContextText), typeof(string), typeof(PinnedChatMessageControl),
            new PropertyMetadata(string.Empty));

        public PinnedChatMessageControl()
        {
            InitializeComponent();
        }

        private static void OnMessageTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((PinnedChatMessageControl)d).FirstLine = GetFirstLine(e.NewValue as string);
        }

        private static string GetFirstLine(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            int newline = text.IndexOfAny(new[] { '\r', '\n' });
            return newline >= 0 ? text.Substring(0, newline) : text;
        }

        /// <summary>The pinned message text (typically the user's opening message, e.g. "You: ...").</summary>
        public string MessageText
        {
            get => (string)GetValue(MessageTextProperty);
            set => SetValue(MessageTextProperty, value);
        }

        /// <summary>Only the first line of <see cref="MessageText"/>, shown while collapsed.</summary>
        public string FirstLine
        {
            get => (string)GetValue(FirstLineProperty);
            private set => SetValue(FirstLinePropertyKey, value);
        }

        /// <summary>Current context-window fill, in <see cref="ProgressMaximum"/> units.</summary>
        public double ProgressValue
        {
            get => (double)GetValue(ProgressValueProperty);
            set => SetValue(ProgressValueProperty, value);
        }

        /// <summary>Upper bound of the context-window progress bar.</summary>
        public double ProgressMaximum
        {
            get => (double)GetValue(ProgressMaximumProperty);
            set => SetValue(ProgressMaximumProperty, value);
        }

        /// <summary>Human-readable context usage caption (e.g. "Context window: 12.4k / 200k").</summary>
        public string ContextText
        {
            get => (string)GetValue(ContextTextProperty);
            set => SetValue(ContextTextProperty, value);
        }

        /// <summary>Raised when the user clicks the close button; the host decides what "close chat" means.</summary>
        public event RoutedEventHandler? CloseRequested;

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, new RoutedEventArgs());
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            var text = MessageText;
            if (string.IsNullOrEmpty(text))
                return;
            try { Clipboard.SetText(text); }
            catch { /* clipboard busy — ignore */ }
        }
    }
}
