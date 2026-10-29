#pragma warning disable
using System;
using System.Activities;
using System.Activities.Presentation.Expressions;
using System.Activities.Presentation.Hosting;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.View;
using System.Activities.Statements;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Differencing;
using NLog;
using RoslynPad.Editor;
using RoslynPad.Roslyn;
using RpaCommon;

namespace RPA.CSharpExpressionEditor
{
    /// <summary>
    /// Interaction logic for RoslynExpressionEditor.xaml
    /// </summary>
    public partial class RoslynExpressionEditor : TextualExpressionEditor
    {
        private static readonly Logger _perfLogger = LogManager.GetLogger("RoslynExpressionEditor.Performance");
        //UselocationExpression
        //        Что такое Location Expression?
        //В контексте Windows Workflow Foundation существует два типа выражений:
        //1. Value Expression(обычное выражение) - UseLocationExpression = false
        //Вычисляется до значения(r-value)
        //Используется для чтения данных
        //Примеры: x + 5, DateTime.Now, "Hello " + userName
        //Применение: InArgument, вычисляемые значения
        //2. Location Expression(выражение-местоположение) - UseLocationExpression = true
        //Представляет место в памяти(l-value), куда можно записать значение
        //Используется для записи данных
        //Примеры: myVariable, myObject.Property, array[0]
        //Применение: OutArgument, InOutArgument, переменные для записи результата

        private const int validationWaitTime = 10000;
        private TextBlock textBlock;
        private bool isBeginEditPending;
        private readonly List<Type> _primitiveTypes = new List<Type>
        {
            typeof(string),
            typeof(int),
            typeof(decimal),
            typeof(double),
            typeof(float),
            typeof(byte),
            typeof(long),
            typeof(short),
            typeof(char),
            typeof(bool),
        };
        private RoslynHost _host;
        private RoslynCodeEditor _roslynCodeEditor;

        #region Properties

        internal static DependencyProperty TextProperty { get; } = DependencyProperty.Register("Text", typeof(string), typeof(RoslynExpressionEditor));

        internal static DependencyProperty EditingStateProperty { get; } = DependencyProperty.Register("EditingState", typeof(EditingState), typeof(RoslynExpressionEditor), new PropertyMetadata(EditingState.Idle));

        internal static DependencyProperty HasValidationErrorProperty { get; } = DependencyProperty.Register("HasValidationError", typeof(bool), typeof(RoslynExpressionEditor), new PropertyMetadata(false));

        internal static DependencyProperty ValidationErrorMessageProperty { get; } = DependencyProperty.Register("ValidationErrorMessage", typeof(string), typeof(RoslynExpressionEditor), new PropertyMetadata(null));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        internal bool HasErrors
        {
            get
            {
                bool hasErrors = false;
                return hasErrors;
            }
        }

        internal bool HasValidationError
        {
            get => (bool)GetValue(HasValidationErrorProperty);
            set => SetValue(HasValidationErrorProperty, value);
        }

        internal string ValidationErrorMessage
        {
            get => (string)GetValue(ValidationErrorMessageProperty);
            set => SetValue(ValidationErrorMessageProperty, value);
        }

        internal EditingState EditingState
        {
            get => (EditingState)GetValue(EditingStateProperty);
            set => SetValue(EditingStateProperty, value);
        }

        public double BlockHeight { get; set; } = double.NaN;

        public double BlockWidth { get; set; } = double.NaN;

        public static int ValidationWaitTime => validationWaitTime;

        public bool IsEditorLoaded { get; set; } = false;

        public string PreviousText { get; set; } = null;

        public IExpressionEditorService ExpressionEditorService { get; set; }

        public IExpressionEditorInstance ExpressionEditorInstance { get; set; }

        public System.Windows.Controls.Control HostControl { get; set; }

        public string EditorName { get; set; }

        public System.Windows.Controls.TextBox EditingTextBox { get; set; }

        public Type InferredType { get; set; }

        public BackgroundWorker Validator { get; set; } = null;

        #endregion Properties

        public RoslynExpressionEditor()
        {
            var sw = Stopwatch.StartNew();
            _perfLogger.Info($"[RoslynExpressionEditor] Constructor START");

            InitializeComponent();
            _perfLogger.Info($"[RoslynExpressionEditor] InitializeComponent completed in {sw.ElapsedMilliseconds}ms");

            MinHeight = FontSize + 4; /* 4 pixels for border*/

            ContentTemplate = (DataTemplate)FindResource("textblock");
            _perfLogger.Info($"[RoslynExpressionEditor] ContentTemplate loaded in {sw.ElapsedMilliseconds}ms");

            innerControl.ContentTemplate = ContentTemplate;
            HintText = "Enter C# Expression";
            Console.WriteLine(OwnerActivity);

            // Load saved size from settings
            LoadSavedSize();
            _perfLogger.Info($"[RoslynExpressionEditor] Constructor COMPLETE in {sw.ElapsedMilliseconds}ms");
        }

        public override bool Commit(bool isExplicitCommit)
        {
            var sw = Stopwatch.StartNew();
            _perfLogger.Info($"[RoslynExpressionEditor] Commit START - isExplicitCommit: {isExplicitCommit}");

            bool committed = false;
            //only generate and validate the expression when when we don't require explicit commit change
            //or when the commit is explicit
            if (!ExplicitCommit || isExplicitCommit)
            {
                // Generate and validate the expression.
                // Get the text from the snapshot and set it to the Text property
                PreviousText = null;

                if (ExpressionEditorInstance != null)
                {
                    PreviousText = Text;
                    Text = ExpressionEditorInstance.GetCommittedText();
                    _perfLogger.Info($"[RoslynExpressionEditor] Commit: Got committed text from editor in {sw.ElapsedMilliseconds}ms");
                }
                if (Expression != null)
                {
                    System.Activities.Activity expression = Expression.GetCurrentValue() as System.Activities.Activity;
                    // if expression is null, GetExpressionString will return null
                    PreviousText = ExpressionHelper.GetExpressionString(expression, OwnerActivity);
                    _perfLogger.Info($"[RoslynExpressionEditor] Commit: Got expression string in {sw.ElapsedMilliseconds}ms");
                }
                else
                {
                    PreviousText = null;
                }

                EditingTextBox?.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty).UpdateSource();

                // If the Text is null, or equal to the previous value, or changed from null to empty, don't bother generating the expression
                // We still need to generate the expression when it is changed from other value to EMPTY however - otherwise
                // the case where you had an expression (valid or invalid), then deleted the whole thing will not be evaluated.
                if (ShouldGenerateExpression(PreviousText, Text))
                {
                    _perfLogger.Info($"[RoslynExpressionEditor] Commit: Generating expression...");
                    GenerateExpression();
                    _perfLogger.Info($"[RoslynExpressionEditor] Commit: Expression generated in {sw.ElapsedMilliseconds}ms");
                    committed = true;
                }
            }
            if (!ContentTemplate.Equals((DataTemplate)FindResource("textblock")))
            {
                ContentTemplate = (DataTemplate)FindResource("textblock");
                _perfLogger.Info($"[RoslynExpressionEditor] Commit: Reset ContentTemplate in {sw.ElapsedMilliseconds}ms");
            }
            _perfLogger.Info($"[RoslynExpressionEditor] Commit COMPLETE in {sw.ElapsedMilliseconds}ms - committed: {committed}");
            return committed;
        }

        internal static bool ShouldGenerateExpression(string oldText, string newText)
        {
            return newText != null && !string.Equals(newText, oldText) && !(oldText == null && newText.Equals(string.Empty));
        }

        private void GenerateExpression()
        {
            var sw = Stopwatch.StartNew();
            _perfLogger.Info($"[RoslynExpressionEditor] GenerateExpression START - Text: '{Text}', UseLocationExpression: {UseLocationExpression}");

            // Улучшенная логика определения типа
            if (ExpressionType == null)
            {
                // Для LocationExpression (поле "To" в Assign) определяем тип по имени переменной из Text
                InferredType = UseLocationExpression && !string.IsNullOrEmpty(Text) ? GetVariableTypeByName(Text) : typeof(object);
                _perfLogger.Info($"[RoslynExpressionEditor] GenerateExpression: Inferred type '{InferredType?.Name}' in {sw.ElapsedMilliseconds}ms");
            }

            Type resultType = (ExpressionType ?? InferredType) ?? typeof(object);

            if (OwnerActivity.ItemType == typeof(Assign) && !UseLocationExpression)
            {
                ModelItem to = OwnerActivity.Properties["To"].Value;
                if (to != null)
                {
                    string variableName = to.Properties["Expression"].Value.Properties["ExpressionText"].Value.GetCurrentValue().ToString().Trim();
                    resultType = CSharpExpressionHelper.GetVarOrArgType(OwnerActivity, variableName) ?? resultType;
                }
                _perfLogger.Info($"[RoslynExpressionEditor] GenerateExpression: Result type '{resultType?.Name}' in {sw.ElapsedMilliseconds}ms");
            }

            // If the text is null we don't need to bother generating the expression (this would be the case the
            // first time you enter an ETB. We still need to generate the expression when it is EMPTY however - otherwise
            // the case where you had an expression (valid or invalid), then deleted the whole thing will not be evaluated.
            if (Text != null && OwnerActivity != null)
            {
                _perfLogger.Info($"[RoslynExpressionEditor] GenerateExpression: Beginning edit scope...");
                using ModelEditingScope scope = OwnerActivity.BeginEdit("Property Change");
                EditingState = EditingState.Validating;
                // we set the expression to null
                // a) when the expressionText is empty AND it's a reference expression or
                // b) when the expressionText is empty AND the DefaultValue property is null
                if (Text.Length == 0 &&
                    (UseLocationExpression || (DefaultValue == null)))
                {
                    Expression = null;
                    _perfLogger.Info($"[RoslynExpressionEditor] GenerateExpression: Set expression to null in {sw.ElapsedMilliseconds}ms");
                }
                else
                {
                    if (Text.Length == 0)
                    {
                        Text = DefaultValue;
                    }

                    _perfLogger.Info($"[RoslynExpressionEditor] GenerateExpression: Creating expression from string...");
                    ModelTreeManager modelTreeManager = Context.Services.GetService<ModelTreeManager>();
                    _perfLogger.Info($"[RoslynExpressionEditor] GenerateExpression: Got ModelTreeManager in {sw.ElapsedMilliseconds}ms");

                    ActivityWithResult newExpression = CSharpExpressionHelper.CreateExpressionFromString(Text, UseLocationExpression, resultType);
                    _perfLogger.Info($"[RoslynExpressionEditor] GenerateExpression: Created expression from string in {sw.ElapsedMilliseconds}ms");

                    ModelItem expressionItem = modelTreeManager.CreateModelItem(null, newExpression);
                    _perfLogger.Info($"[RoslynExpressionEditor] GenerateExpression: Created model item in {sw.ElapsedMilliseconds}ms");

                    Expression = expressionItem;
                }
                scope.Complete();
                _perfLogger.Info($"[RoslynExpressionEditor] GenerateExpression: Scope completed in {sw.ElapsedMilliseconds}ms");
            }
            _perfLogger.Info($"[RoslynExpressionEditor] GenerateExpression COMPLETE in {sw.ElapsedMilliseconds}ms");
        }

        private void StartValidator()
        {
            if (Validator == null)
            {
                // валидатор в BackgroundWorker, по окончанию выполнения сам себя перезапускает
                Validator = new BackgroundWorker
                {
                    WorkerReportsProgress = true,
                    WorkerSupportsCancellation = true
                };
                Validator.DoWork += ValidatorDoWork;
                Validator.RunWorkerCompleted += Validator_RunWorkerCompleted;
                Validator.ProgressChanged += Validator_ProgressChanged;
                Validator.RunWorkerAsync(new ExpressionValidationContext(this));
            }
        }

        private void Validator_ProgressChanged(object sender, ProgressChangedEventArgs args)
        {
            string error = args.UserState as string;
            _ = Dispatcher.BeginInvoke(new Action<string>(UpdateValidationError), error);
        }

        private void Validator_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs args)
        {
            if (!args.Cancelled)
            {
                if (args.Result is ExpressionValidationContext validationContext)
                {
                    _ = Dispatcher.BeginInvoke(new Action<ExpressionValidationContext>(target =>
                    {
                        //validator could be null by the time we try to validate again or
                        //if it's already busy
                        if (Validator != null && !Validator.IsBusy)
                        {
                            target.Update(this);
                            Validator.RunWorkerAsync(target);
                        }
                    }), validationContext);
                }
            }
        }

        private void ValidatorDoWork(object obj, DoWorkEventArgs args)
        {
            BackgroundWorker worker = obj as BackgroundWorker;
            if (worker.CancellationPending)
            {
                args.Cancel = true;
                return;
            }
            if (args.Argument is ExpressionValidationContext validationContext)
            {
                bool hasError = HasError(validationContext, out string errorMessage);
                if (hasError)
                {
                    worker.ReportProgress(0, errorMessage);
                }
                else
                {
                    worker.ReportProgress(0, null);
                }

                //sleep
                if (worker.CancellationPending)
                {
                    args.Cancel = true;
                    return;
                }

                Thread.Sleep(ValidationWaitTime);
                args.Result = validationContext;
            }
        }

        private void KillValidator()
        {
            if (Validator != null)
            {
                Validator.CancelAsync();
                Validator.Dispose();
                Validator = null;
            }
        }

        private static void ValidateExpression(RoslynExpressionEditor etb)
        {
            if (etb.HasError(new ExpressionValidationContext(etb), out string errorMessage))
            {
                etb.UpdateValidationError(errorMessage);
            }
        }

        private void UpdateValidationError(string errorMessage)
        {
            if (!string.IsNullOrEmpty(errorMessage))
            {
                //report error
                HasValidationError = true;
                ValidationErrorMessage = errorMessage;
            }
            else
            {
                HasValidationError = false;
                ValidationErrorMessage = null;
            }
        }

        /// <summary>
        /// Если возвращает true - это ошибка
        /// </summary>
        /// <param name="validationContext"></param>
        /// <param name="errorMessage"></param>
        /// <returns></returns>
        private bool HasError(ExpressionValidationContext validationContext, out string errorMessage)
        {
            errorMessage = null;

            //validate
            //if the text is empty we clear the error message
            if (string.IsNullOrEmpty(validationContext.ExpressionText))
            {
                return false;
            }

            // if the expression text is different from the last time we run the validation we run the validation
            if (!string.Equals(validationContext.ExpressionText, validationContext.ValidatedExpressionText))
            {
                try
                {
                    //TODO: Add logic to validate expression
                    string error = null;
                    Dispatcher.Invoke(() =>
                    {
                        if (!UseLocationExpression)
                        {
                            // этой ветке чекаем только правую часть, т.к. !UseLocationExpression
                            if (OwnerActivity.ItemType == typeof(Assign))
                            {
                                error = ValidateRightType(validationContext);
                            }
                            else if (OwnerActivity.ItemType == typeof(Delay))
                            {
                                CallResult literalResult = CSharpExpressionHelper.TryCreateLiteral(typeof(TimeSpan), validationContext.ExpressionText);
                                if (literalResult.Success)
                                {
                                    return;
                                }
                                // если из литерала не получилось, то ищем в переменных
                                Dictionary<string, Type> availableParametersAndVars = CSharpExpressionHelper.GetEnvironment(OwnerActivity);
                                bool isExist = availableParametersAndVars.TryGetValue(validationContext.ExpressionText.Trim(), out Type envType);
                                if (isExist && typeof(TimeSpan).IsAssignableFrom(envType))
                                {
                                    return;
                                }
                                error = literalResult.Error.Message;
                            }
                            else
                            {
                                try
                                {
                                    (Type type, List<string> errors) = RoslynExpressionValidator.Instance.CheckExpression(validationContext.ExpressionText, OwnerActivity);

                                    // Если создание прошло успешно, считаем что это допустимое выражение типа object
                                    error = type != null && ExpressionType.IsAssignableFrom(type) ? null : string.Join(Environment.NewLine, errors);
                                }
                                catch (Exception ex)
                                {
                                    error = $"Не удалось определить тип выражения: {ex.Message}";
                                }
                            }
                        }
                        else
                        {
                            if (OwnerActivity.ItemType == typeof(Assign))
                            {
                                ModelItem to = OwnerActivity.Properties["To"].Value;
                                Type leftType = null;
                                if (to != null)
                                {
                                    string variableCandidatName = (string)to.Properties["Expression"].Value.Properties["ExpressionText"].Value.GetCurrentValue();
                                    leftType = CSharpExpressionHelper.GetVarOrArgType(OwnerActivity, variableCandidatName);
                                    if (leftType == null)
                                    {
                                        error = $"Переменной с именем {variableCandidatName} в зоне видимости не найдено";
                                    }
                                }
                            }
                        }
                    });

                    if (error != null)
                    {
                        errorMessage = error;
                        return true;
                    }
                }
                catch (Exception err)
                {
                    errorMessage = err.Message;
                    return true;
                }

                return false;
            }
            else
            {
                string error = null;
                Dispatcher.Invoke(() =>
                {
                    error = ValidationErrorMessage;
                });
                errorMessage = error;
                return !string.IsNullOrEmpty(errorMessage);
            }
        }

        private string ValidateRightType(ExpressionValidationContext validationContext)
        {
            string error = null;
            // в Assign левый и правый типы всегда String
            // левая часть валидна только если она есть в переменных или в аргументах
            ModelItem to = OwnerActivity.Properties["To"].Value;
            Type leftType = null;
            if (to != null)
            {
                string variableName = to.Properties["Expression"].Value.Properties["ExpressionText"].Value.GetCurrentValue().ToString().Trim();
                leftType = CSharpExpressionHelper.GetVarOrArgType(OwnerActivity, variableName);
            }

            // Определяем тип правой части выражения
            Type rightType = null;
            string rightTypeError = null;

            // 1. Сначала пробуем определить тип как литерал для примитивных типов
            foreach (Type primitiveType in _primitiveTypes)
            {
                CallResult literalResult = CSharpExpressionHelper.TryCreateLiteral(primitiveType, validationContext.ExpressionText);
                if (literalResult.Success)
                {
                    rightType = primitiveType;
                    break;
                }
            }

            // 2. Если не литерал, проверяем, является ли это переменной или аргументом
            if (rightType == null)
            {
                // Проверяем переменные
                rightType = CSharpExpressionHelper.GetVarOrArgType(OwnerActivity, validationContext.ExpressionText);
            }

            // 3. Если не смогли определить тип, пробуем создать C# выражение 
            if (rightType == null)
            {
                try
                {
                    // Пытаемся создать выражение как object, чтобы проверить корректность синтаксиса
                    (Type type, List<string> errors) = RoslynExpressionValidator.Instance.CheckExpression(validationContext.ExpressionText, OwnerActivity);

                    // Если создание прошло успешно, считаем что это допустимое выражение типа object
                    if (type != null)
                    {
                        rightType = type;
                    }
                    else
                    {
                        rightTypeError = string.Join(Environment.NewLine, errors);
                    }
                }
                catch (Exception ex)
                {
                    rightTypeError = $"Не удалось определить тип выражения: {ex.Message}";
                }
            }

            // 4. Валидация совместимости типов
            if (rightType != null && leftType != null)
            {
                // Проверяем, совместимы ли типы
                if (!leftType.IsAssignableFrom(rightType) && rightType != typeof(object))
                {
                    error = $"Несовместимые типы: нельзя присвоить значение типа '{rightType.Name}' переменной типа '{leftType.Name}'";
                }
            }
            else if (rightTypeError != null)
            {
                error = rightTypeError;
            }

            return error;
        }

        /// <summary>
        /// Получает тип переменной или аргумента по имени
        /// </summary>
        /// <param name="variableName">Имя переменной для поиска</param>
        /// <returns>Тип переменной или null, если не найдена</returns>
        private Type GetVariableTypeByName(string variableName)
        {
            if (string.IsNullOrEmpty(variableName) || OwnerActivity == null)
            {
                return null;
            }

            // Поиск в переменных в области видимости
            List<ModelItem> declaredVariables = CSharpExpressionHelper.GetVariablesInScope(OwnerActivity);
            foreach (ModelItem modelItemVar in declaredVariables)
            {
                if (modelItemVar.GetCurrentValue() is LocationReference locationRef && locationRef.Name == variableName)
                {
                    return locationRef.Type;
                }
            }

            // Поиск в аргументах workflow
            KeyedCollection<string, DynamicActivityProperty> declaredArguments = CSharpExpressionHelper.GetArguments(OwnerActivity);
            if (declaredArguments != null)
            {
                foreach (DynamicActivityProperty argItem in declaredArguments)
                {
                    if (argItem.Name == variableName)
                    {
                        Type type = argItem.Type;
                        // Для Argument<T> извлекаем T
                        return typeof(Argument).IsAssignableFrom(type) ? type.GenericTypeArguments[0] : type;
                    }
                }
            }

            return null;
        }

        #region Events

        private void OnEditorLostAggregateFocus(object sender, EventArgs e)
        {
            KillValidator();

            ValidateExpression(this);

            if (Context != null)
            {   // Unselect if this is the currently selected one.
                ExpressionSelection current = Context.Items.GetValue<ExpressionSelection>();
                if (current != null && current.ModelItem == Expression)
                {
                    ExpressionSelection emptySelection = new ExpressionSelection(null);
                    Context.Items.SetValue(emptySelection);
                }
            }

            // Generate and validate the expression.
            // Get the text from the snapshot and set it to the Text property
            ExpressionEditorInstance?.ClearSelection();
            if (!ExplicitCommit)
            {
                //commit change and let the commit change code do the revert
                bool committed = Commit(false);

                //reset the error icon if we didn't get to set it in the commit
                if (!committed || IsIndependentExpression)
                {
                    EditingState = EditingState.Idle;
                    // Switch the control back to a textbox -
                    // but give it the text from the editor (textbox should be bound to the Text property, so should
                    // automatically be filled with the correct text, from when we set the Text property earlier)
                    if (!ContentTemplate.Equals((DataTemplate)FindResource("textblock")))
                    {
                        ContentTemplate = (DataTemplate)FindResource("textblock");
                    }
                }
            }

            //raise EditorLostLogical focus - in case when some clients need to do explicit commit
            RaiseEvent(new RoutedEventArgs(ExpressionTextBox.EditorLostLogicalFocusEvent, this));
        }

        private void OnEditorClosing(object sender, EventArgs e)
        {
            if (ExpressionEditorInstance != null)
            {
                //these events are expected to be unregistered during lost focus event, but
                //we are unregistering them during unload just in case.  Ideally we want to
                //do this in the CloseExpressionEditor method
                ExpressionEditorInstance.LostAggregateFocus -= new EventHandler(OnEditorLostAggregateFocus);

                ExpressionEditorInstance.Closing -= new EventHandler(OnEditorClosing);
                ExpressionEditorInstance = null;
            }

            if (HostControl.Parent is Border boarder)
            {
                boarder.Child = null;
            }

            HostControl = null;
            EditorName = null;
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            switch (e.Property.Name)
            {
                case "Expression":
                    OnExpressionChanged();
                    break;
                default:
                    break;
            }

            base.OnPropertyChanged(e);
        }

        private void OnTextBlockLoaded(object sender, RoutedEventArgs e)
        {
            textBlock = sender as TextBlock;
            if (isBeginEditPending)
            {
                BeginEdit();
            }
        }

        private void OnExpressionChanged()
        {
            IsSupportedExpression = true;
            if (Expression == null)
            {
                Text = null;
            }
            else
            {
                // This is a necessary work-around for design-time validation to work properly on Variable/Argument designer. For ETB in Variable/Argument designer,
                // this.Expression is actually a FakeModelItemImpl. For non-editing scenario, when the validation is done, it actually updates the validation related
                // attached properties of the real ModelItem. So if we hook on this.Expression.PropertyChanged directly, we cannot get the property change notification.
                // As the result, the UI of Variable/Argument designer won't be updated when the validation status is changed.

                ActivityWithResult expression = Expression.GetCurrentValue() as ActivityWithResult;
                Text = ExpressionHelper.GetExpressionString(expression);
            }
        }

        private void OnTextBlockMouseLeftButtonDown(object sender, RoutedEventArgs e)
        {
            if (!IsReadOnly)
            {
                if (sender is TextBlock textBlock)
                {
                    _ = Keyboard.Focus(textBlock);
                    e.Handled = true;
                }
            }
        }

        private void OnGotTextBlockFocus(object sender, RoutedEventArgs e)
        {
            var sw = Stopwatch.StartNew();
            _perfLogger.Info($"[RoslynExpressionEditor] OnGotTextBlockFocus START");

            if (Context == null)
            {
                _perfLogger.Info($"[RoslynExpressionEditor] OnGotTextBlockFocus: Context is null, returning");
                return;
            }

            DesignerView designerView = Context.Services.GetService<DesignerView>();
            _perfLogger.Info($"[RoslynExpressionEditor] OnGotTextBlockFocus: Got DesignerView in {sw.ElapsedMilliseconds}ms");

            if (!designerView.IsMultipleSelectionMode)
            {
                bool isInReadOnlyMode = IsReadOnly;
                if (Context != null)
                {
                    ReadOnlyState readOnlyState = Context.Items.GetValue<ReadOnlyState>();
                    isInReadOnlyMode |= readOnlyState.IsReadOnly;
                }
                _perfLogger.Info($"[RoslynExpressionEditor] OnGotTextBlockFocus: Checked readonly mode in {sw.ElapsedMilliseconds}ms");

                if (sender is TextBlock textBlock)
                {
                    BlockHeight = textBlock.ActualHeight;
                    BlockHeight = Math.Max(BlockHeight, textBlock.MinHeight);
                    BlockHeight = Math.Min(BlockHeight, textBlock.MaxHeight);
                    BlockWidth = textBlock.ActualWidth;
                    BlockWidth = Math.Max(BlockWidth, textBlock.MinWidth);
                    BlockWidth = Math.Min(BlockWidth, textBlock.MaxWidth);

                    _perfLogger.Info($"[RoslynExpressionEditor] OnGotTextBlockFocus: Calculated block size in {sw.ElapsedMilliseconds}ms");

                    // If it's already an editor, don't need to switch it/reload it (don't create another editor/grid if we don't need to)
                    // Also don't create editor when we are in read only mode
                    if (ContentTemplate.Equals((DataTemplate)FindResource("textblock")) && !isInReadOnlyMode)
                    {
                        if (Context != null)
                        {
                            // Get the ExpressionEditorService
                            ExpressionEditorService = Context.Services.GetService<IExpressionEditorService>();
                            _perfLogger.Info($"[RoslynExpressionEditor] OnGotTextBlockFocus: Got ExpressionEditorService in {sw.ElapsedMilliseconds}ms");
                        }

                        // If the service exists, use the editor template - else switch to the textbox template
                        if (ExpressionEditorService != null)
                        {
                            _perfLogger.Info($"[RoslynExpressionEditor] OnGotTextBlockFocus: Setting ContentTemplate to expressioneditor");
                            ContentTemplate = (DataTemplate)FindResource("expressioneditor");
                            _perfLogger.Info($"[RoslynExpressionEditor] OnGotTextBlockFocus: ContentTemplate set in {sw.ElapsedMilliseconds}ms");
                        }
                    }
                }

                if (!isInReadOnlyMode)
                {
                    //disable the error icon
                    StartValidator();
                    _perfLogger.Info($"[RoslynExpressionEditor] OnGotTextBlockFocus: Validator started in {sw.ElapsedMilliseconds}ms");
                    EditingState = EditingState.Editing;
                    e.Handled = true;
                }
            }
            _perfLogger.Info($"[RoslynExpressionEditor] OnGotTextBlockFocus COMPLETE in {sw.ElapsedMilliseconds}ms");
        }

        private void OnEditorLoaded(object sender, RoutedEventArgs e)
        {
            var sw = Stopwatch.StartNew();
            _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded START");

            if (IsEditorLoaded)
            {
                _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: Editor already loaded, returning");
                return;
            }

            // If the service exists, create an expression editor and add it to the grid - else switch to the textbox data template
            if (ExpressionEditorService == null)
            {
                _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: ExpressionEditorService is null, returning");
                return;
            }

            Border border = (Border)sender;
            _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: Got border in {sw.ElapsedMilliseconds}ms");

            // Get the references and variables in scope
            AssemblyContextControlItem assemblies = (AssemblyContextControlItem)Context.Items.GetValue(typeof(AssemblyContextControlItem));
            _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: Got assemblies in {sw.ElapsedMilliseconds}ms");

            List<ModelItem> declaredVariables = CSharpExpressionHelper.GetEnvironmentModelItems(OwnerActivity, Context);
            _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: Got declared variables ({declaredVariables?.Count ?? 0} items) in {sw.ElapsedMilliseconds}ms");

            ImportedNamespaceContextItem importedNamespaces = Context.Items.GetValue<ImportedNamespaceContextItem>();
            importedNamespaces.EnsureInitialized(Context);
            _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: Initialized namespaces in {sw.ElapsedMilliseconds}ms");

            //if the expression text is empty and the expression type is set, then we initialize the text to prompt text
            if (string.Equals(Text, string.Empty, StringComparison.OrdinalIgnoreCase) && ExpressionType != null)
            {
                Text = TypeToPromptTextConverter.GetPromptText(ExpressionType);
            }

            //this is a hack
            BlockWidth = Math.Max(ActualWidth - 8, 0);  //8 is the margin
            if (HasErrors)
            {
                BlockWidth = Math.Max(BlockWidth - 16, 0); //give 16 for error icon
            }
            _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: Calculated dimensions in {sw.ElapsedMilliseconds}ms");

            try
            {
                _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: Creating expression editor instance...");
                // Use double.NaN for both width and height to allow full stretching
                ExpressionEditorInstance = ExpressionType != null
                    ? ExpressionEditorService.CreateExpressionEditor(assemblies, importedNamespaces, declaredVariables, Text, ExpressionType, new Size(double.NaN, double.NaN))
                    : ExpressionEditorService.CreateExpressionEditor(assemblies, importedNamespaces, declaredVariables, Text, new Size(double.NaN, double.NaN));
                _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: Expression editor instance CREATED in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: ERROR creating editor instance in {sw.ElapsedMilliseconds}ms: {ex.Message}");
                Console.WriteLine(ex.ToString());
                //if exception is thrown, it will fault the wf execution
                // throw ex;
            }

            if (ExpressionEditorInstance == null)
            {
                _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: ExpressionEditorInstance is null after creation, returning");
                return;
            }
            try
            {
                _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: Configuring editor instance properties...");
                ExpressionEditorInstance.VerticalScrollBarVisibility = VerticalScrollBarVisibility;
                ExpressionEditorInstance.HorizontalScrollBarVisibility = HorizontalScrollBarVisibility;

                ExpressionEditorInstance.AcceptsReturn = AcceptsReturn;
                ExpressionEditorInstance.AcceptsTab = AcceptsTab;

                // Add the expression editor to the text panel, at column 1
                HostControl = ExpressionEditorInstance.HostControl;
                _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: Got host control in {sw.ElapsedMilliseconds}ms");

                // Subscribe to this event to change scrollbar visibility on the fly for auto, and to resize the hostable editor
                // as necessary
                ExpressionEditorInstance.LostAggregateFocus += new EventHandler(OnEditorLostAggregateFocus);
                ExpressionEditorInstance.Closing += new EventHandler(OnEditorClosing);

                // Set up Hostable Editor properties
                ExpressionEditorInstance.MinLines = MinLines;
                ExpressionEditorInstance.MaxLines = MaxLines;

                ExpressionEditorInstance.HostControl.Style = (Style)FindResource("editorStyle");
                _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: Applied editor style in {sw.ElapsedMilliseconds}ms");

                border.Child = HostControl;
                _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: Added editor to border in {sw.ElapsedMilliseconds}ms");

                ExpressionEditorInstance.Focus();
                _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: Focused editor in {sw.ElapsedMilliseconds}ms");
            }
            catch (KeyNotFoundException)
            {
                _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded: KeyNotFoundException in {sw.ElapsedMilliseconds}ms");
                _ = new ApplicationException("Unable to find editor with the following editor name: " + EditorName);
            }
            IsEditorLoaded = true;
            _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoaded COMPLETE in {sw.ElapsedMilliseconds}ms");
        }

        private void OnEditorUnloaded(object sender, RoutedEventArgs e)
        {
            // Blank the editorSession and the expressionEditor so as not to use up memory
            // Destroy both as you can only ever spawn one editor per session
            if (ExpressionEditorInstance != null)
            {
                //if we are unloaded during editing, this means we got here by someone clicking breadcrumb, we should try to commit
                if (EditingState == EditingState.Editing)
                {
                    _ = Commit(false);
                }
                ExpressionEditorInstance.Close();
            }
            else
            {
                EditingTextBox = null;
            }

            IsEditorLoaded = false;
        }

        private void OnGotEditingFocus(object sender, RoutedEventArgs e)
        {
            //disable the error icon
            EditingState = EditingState.Editing;
            StartValidator();
        }

        private void OnBorderContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Prevent context menu from bubbling up to designer
            // This ensures the editor's context menu is shown instead of the designer's menu
            e.Handled = true;
        }

        private void OnMainBorderRightClick(object sender, MouseButtonEventArgs e)
        {
            // Prevent right-click from bubbling to designer while allowing child elements to handle it
            // Only mark as handled if the click originated on this border (not from child controls)
            if (e.Source == sender)
            {
                e.Handled = true;
            }
        }

        private void OnEditorBorderRightClick(object sender, MouseButtonEventArgs e)
        {
            // Prevent right-click from bubbling to designer
            // The editor instance will handle showing its own context menu
            e.Handled = true;
        }

        #endregion Events

        #region Size Persistence

        private void LoadSavedSize()
        {
            _perfLogger.Info($"[RoslynExpressionEditor] LoadSavedSize: Subscribing to Loaded event");
            // Subscribe to Loaded event to apply size after control is loaded
            Loaded += OnEditorLoadedForSize;
        }

        private void OnEditorLoadedForSize(object sender, RoutedEventArgs e)
        {
            var sw = Stopwatch.StartNew();
            _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoadedForSize START");

            // Use Dispatcher to apply size before window is fully rendered to avoid flicker
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoadedForSize: Dispatcher action START in {sw.ElapsedMilliseconds}ms");

                // Find parent EditorDialog window
                Window parentWindow = Window.GetWindow(this);
                _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoadedForSize: Got parent window in {sw.ElapsedMilliseconds}ms");

                if (parentWindow != null)
                {
                    string windowTypeName = parentWindow.GetType().Name;
                    _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoadedForSize: Parent window type: {windowTypeName}");

                    // Check if parent is WorkflowElementDialogWindow or EditorDialog
                    if (windowTypeName == "WorkflowElementDialogWindow" ||
                        windowTypeName == "EditorDialog" ||
                        parentWindow.GetType().BaseType?.Name == "WorkflowElementDialog")
                    {
                        // Load saved size from settings
                        double savedWidth = Properties.Settings.Default.ExpressionEditorWidth;
                        double savedHeight = Properties.Settings.Default.ExpressionEditorHeight;
                        _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoadedForSize: Loaded saved size {savedWidth}x{savedHeight} in {sw.ElapsedMilliseconds}ms");

                        // Apply saved size to parent window if valid (greater than 0)
                        if (savedWidth > 0 && savedHeight > 0)
                        {
                            // Temporarily hide window to prevent flicker
                            bool wasVisible = parentWindow.IsVisible;
                            if (wasVisible)
                            {
                                parentWindow.Opacity = 0;
                            }

                            parentWindow.Width = savedWidth;
                            parentWindow.Height = savedHeight;
                            _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoadedForSize: Applied saved size in {sw.ElapsedMilliseconds}ms");
                            // Restore visibility after a short delay
                            if (wasVisible)
                            {
                                _ = Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    parentWindow.Opacity = 1;
                                    _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoadedForSize: Restored opacity in {sw.ElapsedMilliseconds}ms");
                                }), System.Windows.Threading.DispatcherPriority.Render);
                            }
                        }

                        // Subscribe to parent window's SizeChanged event
                        parentWindow.SizeChanged += OnParentWindowSizeChanged;
                        _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoadedForSize: Subscribed to SizeChanged in {sw.ElapsedMilliseconds}ms");
                    }
                }
                _perfLogger.Info($"[RoslynExpressionEditor] OnEditorLoadedForSize COMPLETE in {sw.ElapsedMilliseconds}ms");
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void OnParentWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!(sender is Window))
            {
                return;
            }

            // Only save if the size change is from user interaction (not initial layout)
            if (e.NewSize.Width > 0 && e.NewSize.Height > 0 && e.NewSize != e.PreviousSize)
            {
                // Save with a small delay to avoid saving during initialization
                if (e.PreviousSize.Width > 0 && e.PreviousSize.Height > 0)
                {
                    SaveCurrentSize(e.NewSize.Width, e.NewSize.Height);
                }
            }
        }

        private void SaveCurrentSize(double width, double height)
        {
            // Save current size to settings
            Properties.Settings.Default.ExpressionEditorWidth = width;
            Properties.Settings.Default.ExpressionEditorHeight = height;
            Properties.Settings.Default.Save();
        }

        #endregion Size Persistence

        internal sealed class TypeToPromptTextConverter : IValueConverter
        {
            #region IValueConverter Members

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return GetPromptText(value);
            }

            internal static string GetPromptText(object value)
            {
                Type expressionType = value as Type;
                return value == DependencyProperty.UnsetValue || expressionType == null || !expressionType.IsValueType
                    ? string.Empty
                    : Activator.CreateInstance(expressionType).ToString();
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }

            #endregion
        }

        public override void BeginEdit()
        {
            if (textBlock != null)
            {
                _ = Keyboard.Focus(textBlock);
                isBeginEditPending = false;
            }
            else
            {
                isBeginEditPending = true;
            }
        }

        private class ValidationState
        {
            bool IsError => !string.IsNullOrEmpty(Error);

            string Error { get; set; }
        }
    }
}
