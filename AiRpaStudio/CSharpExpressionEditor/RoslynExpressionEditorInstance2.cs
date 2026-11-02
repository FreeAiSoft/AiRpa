using System;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.View;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.CodeAnalysis;
using RoslynPad.Editor;
using RoslynPad.Roslyn;
using RPA.Helpers;

namespace RPA.CSharpExpressionEditor
{
    public class RoslynExpressionEditorInstance2 : RoslynCodeEditor, IExpressionEditorInstance
    {
        public Control HostControl => this;

        public int MaxLines { get; set; }

        public int MinLines { get; set; }

        public bool HasAggregateFocus => true;

        public bool AcceptsReturn { get; set; }

        public bool AcceptsTab { get; set; }

        public event EventHandler LostAggregateFocus;
        public event EventHandler GotAggregateFocus;
        public event EventHandler Closing;

        private string _variableDeclarations;
        private RoslynHost _roslynHost;
        private DocumentId _baseDocumentId; // ID базового документа с переменными
        private ICSharpCode.AvalonEdit.Document.TextDocument _baseTextDocument; // Текстовый документ для базового документа
        private bool _isInitialized; // Флаг завершения инициализации
        private readonly string _initialText;

        public RoslynExpressionEditorInstance2(string initialText)
        {
            _initialText = initialText;
            SetSettings();
            DeleteLeftFeatures();
            // Create and set context menu for the TextArea
            TextArea.ContextMenu = CreateEditorContextMenu();
            AddEventHandlers();
        }

        public RoslynExpressionEditorInstance2(Size initialSize, string initialText) : this(initialText)
        {
            Width = initialSize.Width;
            Height = initialSize.Height;
        }

        private void SetSettings()
        {
            SyntaxHighlighting = StudioEditorTheme.GetDefinition("C#");
            FontFamily = StudioFonts.CodeFont;
            FontSize = 12;

            _roslynHost = new RoslynHost(additionalAssemblies: new[]
                {
                    Assembly.Load("RoslynPad.Roslyn.Windows"),
                    Assembly.Load("RoslynPad.Editor.Windows")
                }, references: RoslynHostReferences.NamespaceDefault.With(new[]
                {
                    // Явные ссылки на файлы для .NET Framework 4.8
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Text.RegularExpressions.Regex).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
                }));

            // Инициализация редактора будет выполнена позже в FinalizeInitialization
            // после того как переменные будут добавлены через UpdateInstance
        }

        private void AddEventHandlers()
        {
            // Handle MouseRightButtonDown (not Preview) to show menu and prevent bubbling
            MouseRightButtonDown += OnMouseRightButtonDown;

            // Handle ContextMenuOpening to prevent bubbling to parent/designer
            ContextMenuOpening += OnContextMenuOpening;

            // Handle on TextArea to ensure menu shows and doesn't bubble up
            TextArea.ContextMenuOpening += OnTextAreaContextMenuOpening;

            // DEBUG: Add focus tracking to diagnose focus loss issue
            LostFocus += OnLostFocus;
            LostKeyboardFocus += OnLostKeyboardFocus;
            TextArea.LostFocus += OnTextAreaLostFocus;
            TextArea.LostKeyboardFocus += OnTextAreaLostKeyboardFocus;
        }

        private void DeleteLeftFeatures()
        {
            // Удаляем визуальный элемент номеров строк из TextArea, 
            // если он успел создаться (для версии 1.2.0 это частое решение)
            UIElement lineNumbersMargin = TextArea.LeftMargins
                .FirstOrDefault(m => m is ICSharpCode.AvalonEdit.Editing.LineNumberMargin);

            if (lineNumbersMargin != null)
            {
                _ = TextArea.LeftMargins.Remove(lineNumbersMargin);
            }

            foreach (UIElement margin in TextArea.LeftMargins)
            {
                // Скрываем все элементы левой панели (номера и иконки)
                if (margin is UIElement element)
                {
                    element.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void OnTextAreaContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Prevent event from bubbling to designer (which would open designer's context menu)
            e.Handled = true;

            // Manually open the TextArea's context menu since e.Handled=true prevents automatic opening
            if (TextArea.ContextMenu != null && !TextArea.ContextMenu.IsOpen)
            {
                // Store current caret position
                int caretOffset = TextArea.Caret.Offset;

                TextArea.ContextMenu.PlacementTarget = TextArea;
                TextArea.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;

                // Handle Closed event to restore focus and caret position
                void closedHandler(object s, RoutedEventArgs args)
                {
                    TextArea.ContextMenu.Closed -= closedHandler;

                    // Restore focus and caret position immediately after menu closes
                    if (!TextArea.IsFocused)
                    {
                        _ = TextArea.Focus();
                    }

                    // Restore caret position (ensure it's within valid bounds)
                    if (TextArea.Caret.Offset != caretOffset)
                    {
                        int validOffset = Math.Min(caretOffset, TextArea.Document.TextLength);
                        TextArea.Caret.Offset = validOffset;
                    }
                }

                TextArea.ContextMenu.Closed += closedHandler;

                TextArea.ContextMenu.IsOpen = true;
            }
        }

        private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            e.Handled = true;
        }

        private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Prevent right-click from bubbling up to designer
            // This keeps the editor focused and prevents designer menu
            e.Handled = true;
        }

        // DEBUG: Focus tracking handlers
        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            // Check if focus is truly lost (not just moved to a child control)
            if (!IsKeyboardFocusWithin)
            {
                RaiseLostAggregateFocusEvent();
            }
        }

        private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
        }

        private void OnTextAreaLostFocus(object sender, RoutedEventArgs e)
        {
        }

        private void OnTextAreaLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
        }

        private void RaiseLostAggregateFocusEvent()
        {
            LostAggregateFocus?.Invoke(this, EventArgs.Empty);
        }

        private ContextMenu CreateEditorContextMenu()
        {
            ContextMenu contextMenu = new ContextMenu
            {
                Focusable = false, // Prevent menu from stealing focus from TextArea
                StaysOpen = false,
                Background = Brushes.White
            };

            // Prevent keyboard navigation from stealing focus
            KeyboardNavigation.SetTabNavigation(contextMenu, KeyboardNavigationMode.None);
            KeyboardNavigation.SetDirectionalNavigation(contextMenu, KeyboardNavigationMode.None);
            KeyboardNavigation.SetControlTabNavigation(contextMenu, KeyboardNavigationMode.None);

            MenuItem cutMenuItem = new MenuItem
            {
                Header = "Cut",
                Command = ApplicationCommands.Cut,
                InputGestureText = "Ctrl+X",
                Focusable = false
            };

            MenuItem copyMenuItem = new MenuItem
            {
                Header = "Copy",
                Command = ApplicationCommands.Copy,
                InputGestureText = "Ctrl+C",
                Focusable = false
            };

            MenuItem pasteMenuItem = new MenuItem
            {
                Header = "Paste",
                Command = ApplicationCommands.Paste,
                InputGestureText = "Ctrl+V",
                Focusable = false
            };

            MenuItem undoMenuItem = new MenuItem
            {
                Header = "Undo",
                Command = ApplicationCommands.Undo,
                InputGestureText = "Ctrl+Z",
                Focusable = false
            };

            MenuItem redoMenuItem = new MenuItem
            {
                Header = "Redo",
                Command = ApplicationCommands.Redo,
                InputGestureText = "Ctrl+Y",
                Focusable = false
            };

            MenuItem selectAllMenuItem = new MenuItem
            {
                Header = "Select All",
                Command = ApplicationCommands.SelectAll,
                InputGestureText = "Ctrl+A",
                Focusable = false
            };

            _ = contextMenu.Items.Add(cutMenuItem);
            _ = contextMenu.Items.Add(copyMenuItem);
            _ = contextMenu.Items.Add(pasteMenuItem);
            _ = contextMenu.Items.Add(undoMenuItem);
            _ = contextMenu.Items.Add(redoMenuItem);
            _ = contextMenu.Items.Add(selectAllMenuItem);

            return contextMenu;
        }

        #region Base methods

        public bool CanCompleteWord()
        {
            return true;
        }

        public bool CanCopy()
        {
            return true;
        }

        public bool CanCut()
        {
            return true;
        }

        public bool CanDecreaseFilterLevel()
        {
            return true;
        }

        public bool CanGlobalIntellisense()
        {
            return true;
        }

        public bool CanIncreaseFilterLevel()
        {
            return true;
        }

        public bool CanParameterInfo()
        {
            return true;
        }

        public bool CanPaste()
        {
            return true;
        }

        public bool CanQuickInfo()
        {
            return true;
        }

        public void ClearSelection()
        {

        }

        public void Close()
        {
            CloseBaseDocument();
        }

        public bool CompleteWord()
        {
            return true;
        }

        public bool DecreaseFilterLevel()
        {
            return true;
        }

        public string GetCommittedText()
        {
            return Text;
        }

        public bool GlobalIntellisense()
        {
            return true;
        }

        public bool IncreaseFilterLevel()
        {
            return true;
        }

        public bool ParameterInfo()
        {
            return true;
        }

        public bool QuickInfo()
        {
            return true;
        }

        #endregion Base methods

        #region IExpressionEditorInstance explicit

        void IExpressionEditorInstance.Focus()
        {
            _ = Focus();
        }

        bool IExpressionEditorInstance.Cut()
        {
            Cut();
            return true;
        }

        bool IExpressionEditorInstance.Copy()
        {
            Copy();
            return true;
        }

        bool IExpressionEditorInstance.Paste()
        {
            Paste();
            return true;
        }

        bool IExpressionEditorInstance.Undo()
        {
            return Undo();
        }

        bool IExpressionEditorInstance.Redo()
        {
            return Redo();
        }

        bool IExpressionEditorInstance.CanUndo()
        {
            return CanUndo;
        }

        bool IExpressionEditorInstance.CanRedo()
        {
            return CanRedo;
        }

        event EventHandler IExpressionEditorInstance.TextChanged
        {
            add => TextChanged += value;
            remove => TextChanged -= value;
        }

        string IExpressionEditorInstance.Text
        {
            get => Text;
            set => Text = value;
        }

        ScrollBarVisibility IExpressionEditorInstance.VerticalScrollBarVisibility
        {
            get => VerticalScrollBarVisibility;
            set => VerticalScrollBarVisibility = value;
        }

        ScrollBarVisibility IExpressionEditorInstance.HorizontalScrollBarVisibility
        {
            get => HorizontalScrollBarVisibility;
            set => HorizontalScrollBarVisibility = value;
        }

        #endregion

        #region Variable Context Management

        /// <summary>
        /// Создает базовый документ с объявлениями переменных.
        /// Этот документ не виден пользователю, но переменные доступны в контексте компиляции.
        /// </summary>
        /// <returns>DocumentId базового документа или null если нет переменных</returns>
        private DocumentId CreateBaseDocument()
        {
            if (string.IsNullOrEmpty(_variableDeclarations))
            {
                return null;
            }

            string workingDirectory = Directory.GetCurrentDirectory();

            // Создаем текстовый документ для базового контекста
            _baseTextDocument = new ICSharpCode.AvalonEdit.Document.TextDocument(_variableDeclarations);
            AvalonEditTextContainer baseContainer = new AvalonEditTextContainer(_baseTextDocument);

            DocumentCreationArgs baseDocArgs = new DocumentCreationArgs(
                baseContainer,
                workingDirectory,
                onDiagnosticsUpdated: null,
                onTextUpdated: null,
                name: "__VariablesContext__"
            );

            return _roslynHost.AddDocument(baseDocArgs);
        }

        /// <summary>
        /// Обработчик события создания документа.
        /// Если есть базовый документ с переменными, создает связанный документ.
        /// </summary>
        private void OnCreatingDocument(object sender, CreatingDocumentEventArgs e)
        {
            if (_baseDocumentId != null)
            {
                string workingDirectory = Directory.GetCurrentDirectory();

                e.DocumentId = _roslynHost.AddRelatedDocument(
                    _baseDocumentId,
                    new DocumentCreationArgs(
                        e.TextContainer,
                        workingDirectory,
                        e.ProcessDiagnostics,
                        text => e.TextContainer.UpdateText(text)
                    )
                );
            }
        }

        /// <summary>
        /// Завершает инициализацию редактора с учетом переменных.
        /// Должен вызываться ПОСЛЕ UpdateInstance.
        /// </summary>
        public void FinalizeInitialization()
        {
            if (_isInitialized)
            {
                return; // Уже инициализирован
            }

            // Создаем базовый документ если есть переменные
            _baseDocumentId = CreateBaseDocument();

            // Подписываемся на событие создания документа
            CreatingDocument += OnCreatingDocument;

            // Теперь инициализируем редактор
            string workingDirectory = Directory.GetCurrentDirectory();
            string initialText = _initialText ?? string.Empty;
            _ = Initialize(_roslynHost, new ClassificationHighlightColors(),
                workingDirectory, initialText);
            DeleteLeftFeatures();
            _isInitialized = true;
        }

        /// <summary>
        /// Закрывает базовый документ при закрытии редактора.
        /// </summary>
        private void CloseBaseDocument()
        {
            if (_baseDocumentId != null)
            {
                try
                {
                    _roslynHost.CloseDocument(_baseDocumentId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error closing base document: {ex}");
                }
                _baseDocumentId = null;
            }

            _baseTextDocument = null;
        }

        #endregion

        #region Old

        public void UpdateInstance(List<ModelItem> variables)
        {
            if (variables != null)
            {
                try
                {
                    if (variables.Count > 0)
                    {
                        _variableDeclarations = string.Join("", variables.Select(v =>
                        {
                            object currentValue = v.GetCurrentValue();

                            // Log what types we're getting
                            if (currentValue is System.Activities.Variable c)
                            {
                                return c.Type.FullName + " " + c.Name + ";\n";
                            }
#pragma warning disable IDE0046 // Convert to conditional expression
                            else if (currentValue is System.Activities.DelegateArgument d)
                            {
                                return d.Type.FullName + " " + d.Name + ";\n";
                            }
                            else if (currentValue is System.Activities.RuntimeArgument ra)
                            {
                                return ra.Type.FullName + " " + ra.Name + ";\n";
                            }
                            else
                            {
                                return null;
                            }
#pragma warning restore IDE0046 // Convert to conditional expression
                        }).ToArray());
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        #endregion
    }
}
