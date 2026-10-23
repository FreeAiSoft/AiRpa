using System;
using System.Activities;
using System.Activities.Expressions;
using System.Activities.Presentation;
using System.Activities.Presentation.Debug;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Activities.Presentation.Validation;
using System.Activities.Statements;
using System.Activities.XamlIntegration;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xaml;
using System.Xml;
using RPA.CSharpExpressionEditor;
using RPA.Helpers;

namespace RPA.Core.Services
{
    /// <summary>
    /// Валидирует полностью дизайнер, все активности с неактивными редакторами выражений
    /// и устанавливает иконки ошибок на неактивных редакторах выражений,
    /// обеспечивает всплытие ошибок на родительские активности редакторов выражений,
    /// обеспечивает всплытие ошибок на родительские активности активностей с ошибками.
    /// Производит валидацию при загрузке дизайнера и при изменений модели.
    /// </summary>
    internal class DesignerValidator : IDisposable
    {
        private readonly WorkflowDesignerEx _workflowDesignerEx;
        private bool _isModelServiceAvailable = false;
        private bool _isModelChanged = false;
#pragma warning disable IDE0044 // Add readonly modifier
        private bool _isNeedInitValidation = true;
#pragma warning restore IDE0044 // Add readonly modifier
        private DispatcherTimer _validationTimer;
        private const int ValidationDelayMs = 1000; // Задержка перед валидацией (1.5 секунды)

        public DesignerValidator(WorkflowDesignerEx workflowDesignerEx)
        {
            _workflowDesignerEx = workflowDesignerEx;
            workflowDesignerEx.ModelChanged += WorkflowDesignerEx_ModelChanged;

            // Подписываемся на событие изменения сервисов
            _workflowDesignerEx.Context.Services.Subscribe<ModelService>(OnModelServiceAvailable);

            // Инициализация таймера для фоновой валидации
            InitializeValidationTimer();
        }

        /// <summary>
        /// Инициализирует таймер для фоновой валидации
        /// </summary>
        private void InitializeValidationTimer()
        {
            _validationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ValidationDelayMs)
            };
            _validationTimer.Tick += ValidationTimer_Tick;
            _validationTimer.Start();
        }

        /// <summary>
        /// Обработчик тика таймера - запускает валидацию если были изменения
        /// </summary>
        private void ValidationTimer_Tick(object sender, EventArgs e)
        {
            if ((_isModelChanged || _isNeedInitValidation) && _isModelServiceAvailable)
            {
                _isModelChanged = false;
                Validate();
            }
        }

        /// <summary>
        /// Освобождает ресурсы, останавливает таймер
        /// </summary>
        public void Dispose()
        {
            if (_validationTimer != null)
            {
                _validationTimer.Stop();
                _validationTimer.Tick -= ValidationTimer_Tick;
                _validationTimer = null;
            }

            if (_workflowDesignerEx != null)
            {
                _workflowDesignerEx.ModelChanged -= WorkflowDesignerEx_ModelChanged;
            }
        }

        private void OnModelServiceAvailable(ModelService modelService)
        {
            if (modelService != null)
            {
                _isModelServiceAvailable = true;
            }
        }

        /// <summary>
        /// Устанавливает иконку валидации напрямую на Border (обходной путь)
        /// Это имитирует эффект срабатывания триггера при ValidationState.Error
        /// </summary>
        private void SetValidationIconDirectly(FrameworkElement validationVisuals, List<string> errors)
        {
            try
            {
                // validationVisuals это Border (WorkflowDesignerIconBorder)
                if (!(validationVisuals is Border border))
                {
                    return;
                }

                // Получаем тип WorkflowDesignerIcons из System.Activities.Presentation
                Type workflowDesignerIconsType = Type.GetType("System.Activities.Presentation.WorkflowDesignerIcons, System.Activities.Presentation");

                if (workflowDesignerIconsType == null)
                {
                    return;
                }

                // Получаем вложенный класс DesignerItems
                Type designerItemsType = workflowDesignerIconsType.GetNestedType("DesignerItems", BindingFlags.Public | BindingFlags.Static);

                if (designerItemsType == null)
                {
                    return;
                }

                // Получаем статическое поле ActivityErrorValidation
                PropertyInfo activityErrorValidationField = designerItemsType.GetProperty("ActivityErrorValidation",
                    BindingFlags.Public | BindingFlags.Static);

                if (activityErrorValidationField == null)
                {
                    return;
                }

                // Получаем значение иконки (это Brush)
                object errorIconBrush = activityErrorValidationField.GetValue(null);

                if (errorIconBrush is Brush brush)
                {
                    if (errors.Count == 0)
                    {
                        // Если ошибок нет, скрываем иконку
                        border.Visibility = Visibility.Collapsed;
                        border.Background = null;
                        border.ToolTip = null;
                    }
                    else
                    {
                        // Устанавливаем Background для Border
                        border.Visibility = Visibility.Visible;
                        border.Background = brush;

                        // Устанавливаем ToolTip для отображения сообщения об ошибке
                        border.ToolTip = string.Join(Environment.NewLine, errors);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetValidationIconDirectly ERROR: {ex.Message}");
                Debug.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }

        private void WorkflowDesignerEx_ModelChanged(object sender, EventArgs e)
        {
            _isModelChanged = true;
        }

        private void Validate()
        {
            try
            {
                DebuggerService debuggerService = (DebuggerService)_workflowDesignerEx.DebugManagerView;
                ModelService modelService = _workflowDesignerEx.Context.Services.GetService<ModelService>();
                if (modelService == null)
                {
                    return;
                }
                ModelItem rootModelItem = modelService.Root;
                ModelItem implementationModelItem = rootModelItem.Properties["Implementation"]?.Value;
                if (implementationModelItem == null || !(implementationModelItem.GetCurrentValue() is System.Activities.Activity rootActivity))
                {
                    return;
                }

                // Получаем ValidationService для установки кастомных ошибок
                ValidationService validationService = _workflowDesignerEx.Context.Services.GetService<ValidationService>();
                validationService?.ValidateWorkflow();

                // Получаем словарь ошибок валидации
                //Dictionary<dynamic, dynamic> validationErrors = GetNativeErrors(validationService);

                // Получение всех потомков типа ModelItem
                List<ModelItem> modelItems = GetModelItemChilds(implementationModelItem);

                foreach (ModelItem modelItem in modelItems)
                {
                    // Получаем Activity объект из ModelItem
                    if (!(modelItem.GetCurrentValue() is System.Activities.Activity activity))
                    {
                        continue;
                    }

                    List<string> errors = GetActivityErrors(modelItem);

                    SetValivationErrorsToModelItem(modelItem, errors);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ОШИБКА в CustomValidation(): {ex}");
            }
        }

        private static List<string> GetActivityErrors(ModelItem modelItem)
        {

            List<string> errors = new List<string>();

            foreach (ModelProperty prop in modelItem.Properties)
            {
                //TODO: исследовать возможности валидации свойств
                if (!typeof(Argument).IsAssignableFrom(prop.PropertyType)
                    || prop.Value == null)
                {
                    continue;
                }

                GetArgumentErrors(modelItem, errors, prop);
            }

            return errors;
        }

        private static void GetArgumentErrors(ModelItem activityModelItem, List<string> errors, ModelProperty argumentProperty)
        {
            Type argumentGenericParameterType = argumentProperty.PropertyType.GetGenericArguments()[0];
            ModelItem propertyModelItem = argumentProperty.Value;
            ModelItem expressionModelItem = propertyModelItem.Properties["Expression"].Value;
            // expressionModelItemType - если выражение не корректно, то тип CSharpValue, иначе Literal,
            // но рандомно может быть почему-то CSharpValue
            Type expressionModelItemType = expressionModelItem.ItemType;
            string argName = argumentProperty.Name;
            string expressionText = (string)expressionModelItem.Properties["ExpressionText"]?.Value.GetCurrentValue();

            // Для OutArgument чекаем только, что это переменная или аргумент в зоне видимости
            if (typeof(OutArgument).IsAssignableFrom(argumentProperty.PropertyType))
            {
                Dictionary<string, Type> availableParametersAndVars = CSharpExpressionHelper.GetEnvironment(activityModelItem);
                bool isExist = availableParametersAndVars.TryGetValue(expressionText.Trim(), out _);
                if (!isExist)
                {
                    errors.Add($"{argName}: {expressionText} не существует в текущем контексте.");
                }
                return;
            }

            // выражение для получения ошибок по тексту и ModelItem
            (Type expressionType, List<string> compilationErrors) = RoslynExpressionValidator.Instance.CheckExpression(expressionText, activityModelItem);
            if (expressionModelItemType.IsGenericType && expressionModelItemType.GetGenericTypeDefinition() == typeof(Literal<>))
            {
                // Если литерал нужного типа, то значит всё ок и проверки не нужны
                // Замечено, что если литерал создан, то он уже нужного типа,
                // но есть ньюанс: строковой литерал создаётся на некорректной строке с обратным слешем,
                // например - Tests\CheckWebRTC.xaml
                // Поэтому чекаем тип и корректность на всякий случай: сравниваем генерик параметр аргумента argumentGenericParameterType с генерик параметром Literal и типом,
                // который выдаёт компилятор для выражения

                dynamic literal = expressionModelItem.GetCurrentValue();
                string literalExpressionText = (string)literal.Value.ToString();
                Type literalType = literal.ResultType;
                // Если тип string, то оборачиваем в кавычки
                if (literalType == typeof(string))
                {
                    literalExpressionText = $"\"{literalExpressionText}\"";
                }

                (Type literalExpressionType, List<string> literalCompilationErrors) = RoslynExpressionValidator.Instance.CheckExpression(literalExpressionText, activityModelItem);
                literalCompilationErrors?.ForEach(e => errors.Add($"{argName}: {e}"));
                if (errors.Count == 0)
                {
                    if (literalType != argumentGenericParameterType)
                    {
                        errors.Add($"{argName}: тип литерала {literalType} не соответствует типу аргумента {argumentGenericParameterType}");
                    }
                    if (literalExpressionType != argumentGenericParameterType)
                    {
                        errors.Add($"{argName}: тип литерала {literalExpressionType} (выведенный компилятором) не соответствует типу аргумента {argumentGenericParameterType}");
                    }
                }
            }
            else
            {
                if (expressionType != null)
                {
                    Type activityType = activityModelItem.GetCurrentValue().GetType();
                    if (activityType == typeof(Assign) && argName == "Value")
                    {
                        // сравниваем тип по типу левой части, если она заполнена
                        ModelProperty toProperty = activityModelItem.Properties["To"];
                        ModelItem toPropertyModelItem = toProperty.Value;
                        ModelItem toExpressionModelItem = toPropertyModelItem.Properties["Expression"].Value;

                        Dictionary<string, Type> availableParametersAndVars = CSharpExpressionHelper.GetEnvironment(activityModelItem);
                        bool isExist = availableParametersAndVars.TryGetValue(expressionText.Trim(), out _);
                        if (isExist)
                        {
                            Type rightType = availableParametersAndVars[expressionText.Trim()];
                            Type leftType = toExpressionModelItem.ItemType.GenericTypeArguments[0];
                            if (!expressionModelItemType.IsAssignableFrom(rightType))
                            {
                                errors.Add($"{argName}: не возможно присвоить тип {rightType} переменной типа {leftType}.");
                            }
                        }
                        else
                        {
                            errors.Add($"{argName}: {expressionText} не существует в текущем контексте.");
                        }
                    }
                    else
                    {
                        if (!argumentGenericParameterType.IsAssignableFrom(expressionType))
                        {
                            errors.Add($"{argName}: {expressionType} невозможно присвоить типу {argumentGenericParameterType}.");
                        }
                    }
                }
                compilationErrors?.ForEach(e => errors.Add($"{argName}: {e}"));
            }
        }

        private List<ModelItem> GetModelItemChilds(ModelItem parentModelItem)
        {
            List<ModelItem> modelItems = new List<ModelItem>();

            if (typeof(System.Activities.Activity).IsAssignableFrom(parentModelItem.ItemType))
            {
                ModelPropertyCollection properties = parentModelItem.Properties;
                foreach (ModelProperty prop in properties)
                {
                    ModelItem childItem = prop.Value;
                    if (childItem != null)
                    {
                        if (typeof(System.Activities.Activity).IsAssignableFrom(childItem.ItemType))
                        {
                            AddToModelitemsColl(modelItems, childItem);
                        }
                    }
                    // parentModelItem.ItemType == typeof(Sequence)
                    // parentModelItem.Properties["Activities"]
                    if (prop?.Collection != null)
                    {
                        // Получаем коллекцию дочерних ModelItem'ов
                        foreach (ModelItem child in prop.Collection)
                        {
                            // проверяем, что в коллекции активности, а не прочее, вроде переменных
                            if (typeof(System.Activities.Activity).IsAssignableFrom(child.ItemType))
                            {
                                AddToModelitemsColl(modelItems, child);
                            }
                            else if (typeof(FlowStep).IsAssignableFrom(child.ItemType))
                            {
                                AddToModelitemsColl(modelItems, child.Content.Value);
                            }
                            else if (typeof(Catch).IsAssignableFrom(child.ItemType))
                            {
                                AddToModelitemsColl(modelItems, child.Content.Value.Properties["Handler"].Value);
                            }
                            else if (typeof(State).IsAssignableFrom(child.ItemType))
                            {
                                ModelItem entry = child.Properties["Entry"].Value;
                                ModelItem exit = child.Properties["Exit"].Value;
                                if (entry != null)
                                {
                                    AddToModelitemsColl(modelItems, entry);
                                }
                                if (exit != null)
                                {
                                    AddToModelitemsColl(modelItems, exit);
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    if (prop.Dictionary != null)
                    {
                        IEnumerator<KeyValuePair<ModelItem, ModelItem>> en = prop.Dictionary.GetEnumerator();
                        while (en.MoveNext())
                        {
                            ModelItem child = en.Current.Value;
                            AddToModelitemsColl(modelItems, child);
                        }
                    }
                }
            }

            return modelItems;
        }

        private void AddToModelitemsColl(List<ModelItem> modelItems, ModelItem childItem)
        {
            modelItems.Add(childItem);
            List<ModelItem> children = GetModelItemChilds(childItem);
            modelItems.AddRange(children);
        }

        private void SetValivationErrorsToModelItem(ModelItem modelItem, List<string> errors)
        {
            // Получаем UI элемент, связанный с ModelItem
            UIElement activityUI = (UIElement)modelItem.View;

            if (activityUI == null)
            {
                Debug.WriteLine($"  View = null для {modelItem.ItemType.Name}");
                return;
            }

            // Найти потомка с типом WorkflowDesignerIconBorder и x:Name="validationVisuals"
            FrameworkElement validationVisuals =
                XamlHelper.FindVisualChildByTypeAndName(activityUI, "WorkflowDesignerIconBorder", "validationVisuals");

            if (validationVisuals != null)
            {
                Debug.WriteLine($"  ✓ Найден validationVisuals для: {modelItem.ItemType.Name}");

                // Устанавливаем иконку напрямую на Border (обходной путь)
                SetValidationIconDirectly(validationVisuals, errors);

                Debug.WriteLine($"  ✓ Установлена иконка валидации для: {modelItem.ItemType.Name}");
            }
            else
            {
                Debug.WriteLine($"  ✗ validationVisuals НЕ найден для: {modelItem.ItemType.Name}");
            }
        }

        private Dictionary<dynamic, dynamic> GetNativeErrors(ValidationService validationService)
        {
            if (validationService == null)
            {
                return null;
            }

            Type validationServiceType = validationService.GetType();
            PropertyInfo validationErrorsProperty = validationServiceType.GetProperty("ValidationErrors",
                BindingFlags.NonPublic | BindingFlags.Instance);

            IDictionary result = validationErrorsProperty?.GetValue(validationService) as IDictionary;

            Dictionary<dynamic, dynamic> errors = new Dictionary<dynamic, dynamic>();
            IEnumerator en = result.Keys.GetEnumerator();
            while (en.MoveNext())
            {
                dynamic activity = en.Current;
                dynamic error = result[activity];
                errors.Add(activity, error);
            }
            return errors;
        }

        private Dictionary<TextExpressionCompilerError, string> GetCompileErrors()
        {
            try
            {
                _workflowDesignerEx.Flush();
                string sourceFile = ((WorkflowFileItem)_workflowDesignerEx.Context.Items.First(i => i.ItemType == typeof(WorkflowFileItem))).LoadedFile;
                WorkflowDesignerEx workflowDesignerEx = new WorkflowDesignerEx();
                workflowDesignerEx.Context.Services.GetService<DesignerConfigurationService>().TargetFrameworkName = new System.Runtime.Versioning.FrameworkName(".NETFramework", new Version(4, 5));
                workflowDesignerEx.Context.Services.GetService<DesignerConfigurationService>().LoadingFromUntrustedSourceEnabled = true;
                workflowDesignerEx.Text = _workflowDesignerEx.Text;
                //workflowDesignerEx.Load(sourceFile);
                workflowDesignerEx.Load();
                DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(workflowDesignerEx);


                _workflowDesignerEx.Flush();
                MemoryStream workflowStream = new MemoryStream(Encoding.UTF8.GetBytes(_workflowDesignerEx.Text));
                System.Activities.Activity dynamicActivity =
                    ActivityXamlServices.Load(workflowStream, new ActivityXamlServicesSettings { CompileExpressions = false });

                MemoryStream workflowStream2 = new MemoryStream(Encoding.UTF8.GetBytes(_workflowDesignerEx.Text));
                using XmlReader xmlReader = XmlReader.Create(workflowStream2, new XmlReaderSettings
                {
                    XmlResolver = null
                });
                // Получаем тип ActivityXamlServices
                Type activityXamlServicesType = typeof(ActivityXamlServices);

                // Получаем вложенный приватный класс DynamicActivityReaderSchemaContext
                Type dynamicActivityReaderSchemaContextType = activityXamlServicesType.GetNestedType(
                    "DynamicActivityReaderSchemaContext",
                    BindingFlags.NonPublic) ?? throw new InvalidOperationException("Cannot find DynamicActivityReaderSchemaContext type");

                // Создаем экземпляр приватного класса через рефлексию
                XamlSchemaContext dynamicActivityReaderSchemaContext =
                    (XamlSchemaContext)Activator.CreateInstance(dynamicActivityReaderSchemaContextType);
                using XamlXmlReader xamlReader = new XamlXmlReader(xmlReader, dynamicActivityReaderSchemaContext);
                //string name = dynamicActivity.Name;
                //int num = dynamicActivity.Name.LastIndexOf('.');
                //int length = dynamicActivity.Name.Length;
                //string text = ((num > 0) ? dynamicActivity.Name.Substring(num + 1) : dynamicActivity.Name);
                //text += "_CompiledExpressionRoot";
#pragma warning disable IDE0004 // Remove Unnecessary Cast
                TextExpressionCompilerSettings compilerSettings = new TextExpressionCompilerSettings
                {
                    AlwaysGenerateSource = true,
                    ForImplementation = true,
                    Activity = (System.Activities.Activity)dynamicActivity,
                    ActivityName = "Main.xaml",
                    Language = "C#"
                };
#pragma warning restore IDE0004 // Remove Unnecessary Cast
                TextExpressionCompiler compilerService = new TextExpressionCompiler(compilerSettings);
                //MemoryStream workflowStream2 = new MemoryStream(Encoding.UTF8.GetBytes(_workflowDesignerEx.Text));
                TextExpressionCompilerResults compiledActivity = compilerService.Compile();
            }
            catch (InvalidOperationException ex)
            {
                Dictionary<TextExpressionCompilerError, string> errors = new Dictionary<TextExpressionCompilerError, string>();
                IEnumerator en = ex.Data.Keys.GetEnumerator();
                while (en.MoveNext())
                {
                    TextExpressionCompilerError error = (TextExpressionCompilerError)en.Current;
                    string mess = (string)ex.Data[error];
                    errors.Add(error, mess);
                }
                return errors;
            }
            catch (Exception ex)
            {
                CustomWfDesigner.GetInstance().Log(ex.Message, 3);
            }
            return null;
        }
    }
}
