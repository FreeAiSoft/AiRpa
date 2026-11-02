using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Search;
using RPA.Helpers;
using RPA.Models;

namespace RPA.Controls
{
    /// <summary>
    /// A reusable code editor view backed by AvalonEdit with syntax highlighting,
    /// folding and search support.
    /// </summary>
    public partial class CodeEditorView : UserControl
    {
        private FoldingManager foldingManager;
        private XmlFoldingStrategy foldingStrategy;
        private readonly DispatcherTimer foldingTimer;
        private bool isLoading;
        private string currentLanguage;

        public CodeDocument Document { get; private set; }

        public CodeEditorView()
        {
            InitializeComponent();

            _ = SearchPanel.Install(editor.TextArea);
            editor.Options.ConvertTabsToSpaces = true;
            editor.Options.IndentationSize = 4;
            editor.TextChanged += Editor_TextChanged;

            foldingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            foldingTimer.Tick += (s, e) =>
            {
                foldingTimer.Stop();
                RefreshFoldings();
            };
        }

        public void LoadFile(string filePath)
        {
            Document = CodeDocument.Load(filePath);

            isLoading = true;
            try
            {
                editor.Text = Document.Text;
                ApplyLanguage(Document.Language);
                editor.ScrollToHome();
            }
            finally
            {
                isLoading = false;
            }

            Document.PropertyChanged += Document_PropertyChanged;
        }

        public void Save()
        {
            if (Document == null)
            {
                return;
            }

            Document.Save();
        }

        /// <summary>Перекрашивает редактор после смены темы (фон, текст, подсветка).</summary>
        public void ApplyTheme()
        {
            StudioEditorTheme.Apply(editor, currentLanguage);
        }

        private void ApplyLanguage(string language)
        {
            currentLanguage = language;
            StudioEditorTheme.Apply(editor, language);
            foldingStrategy = CodeLanguageProvider.CreateFoldingStrategy(language);

            if (foldingStrategy != null && foldingManager == null)
            {
                foldingManager = FoldingManager.Install(editor.TextArea);
            }

            RefreshFoldings();
        }

        private void RefreshFoldings()
        {
            if (foldingStrategy == null || foldingManager == null)
            {
                return;
            }

            foldingStrategy.UpdateFoldings(foldingManager, editor.Document);
        }

        private void Editor_TextChanged(object sender, EventArgs e)
        {
            if (isLoading || Document == null)
            {
                return;
            }

            Document.Text = editor.Text;

            if (foldingStrategy != null)
            {
                foldingTimer.Stop();
                foldingTimer.Start();
            }
        }

        private void Document_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(CodeDocument.Text))
            {
                return;
            }

            if (!string.Equals(editor.Text, Document.Text, StringComparison.Ordinal))
            {
                isLoading = true;
                try
                {
                    editor.Text = Document.Text;
                }
                finally
                {
                    isLoading = false;
                }
            }
        }
    }
}
