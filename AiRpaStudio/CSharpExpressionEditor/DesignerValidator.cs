using System;
using System.Activities;
using System.Activities.Expressions;
using System.Activities.Presentation;
using System.Activities.Presentation.Debug;
using System.Activities.Presentation.Model;
using System.Activities.Statements;
using System.Activities.XamlIntegration;
using System.Collections;
using System.Activities.Presentation.Services;
using System.Activities.Presentation.Validation;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    /// Валидирует дизайнер: выражения аргументов всех активностей (включая тела <c>ForEach</c>,
    /// ветки <c>Pick</c> и кастомные контейнеры) и ставит иконки ошибок на активности.
    /// <para>
    /// Проход разбит на три фазы, потому что раньше всё это делалось на UI-потоке одним куском —
    /// на каждое изменение модели и на каждый литерал дважды:
    /// </para>
    /// <list type="number">
    /// <item><b>сбор данных</b> (UI-поток): обход модели (<see cref="DesignerValidationScope"/>),
    /// снимок окружения выражений (<see cref="ExpressionEnvironment"/>) и снимки проверок
    /// (<see cref="ActivityExpressionCheck"/>) — дешёвое чтение модели;</item>
    /// <item><b>проверка выражений</b> (фоновая задача): компиляция Roslyn и сравнение типов —
    /// <see cref="ExpressionErrorEvaluator"/>, самое дорогое в проходе;</item>
    /// <item><b>применение</b> (UI-поток): иконки по адресам, только если результат ещё актуален
    /// (<see cref="DesignerValidationPass"/>).</item>
    /// </list>
    /// <para>
    /// WF-валидация (<c>ValidationService.ValidateWorkflow()</c>) остаётся на UI-потоке: она наполняет
    /// <c>ModelItem.ValidationState</c>, по которому WF рисует свои иконки, и без UI-потока работать
    /// не может. Её цена отдельно не измерялась — это шаг 0 плана
    /// <c>docs\UI02_Background_Validation_Plan.md</c>.
    /// </para>
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

        /// <summary>
        /// Подпись реализованных вьюх на момент последнего полного прохода (см. <see cref="ValidationSchedule"/>).
        /// <c>null</c> — прохода ещё не было.
        /// </summary>
        private string _lastRealizedSignature;

        /// <summary>Поколения фоновых проходов: отмена, отбрасывание устаревших результатов, «не выполнено».</summary>
        private readonly DesignerValidationPass _validationPass = new DesignerValidationPass();

        /// <summary>Компилятор выражений (в тестах подменяется через этот интерфейс).</summary>
        private readonly IExpressionCompiler _expressionCompiler = RoslynExpressionCompiler.Instance;

        /// <summary>Замеры последнего прохода (шаг 0 плана: цифры «до/после» без интерактивной студии).</summary>
        private DesignerValidationMetrics _lastMetrics;

        /// <summary>Замеры текущего прохода: заполняются в фазе снимка (UI) и в фоновой фазе.</summary>
        private DesignerValidationMetrics _pendingMetrics;

        /// <summary>Время обхода модели на последнем тике (UI-поток, фаза снимка).</summary>
        private double _modelWalkMs;

        /// <summary>Замеры последнего прохода. <c>null</c> — прохода ещё не было.</summary>
        internal DesignerValidationMetrics LastMetrics => Volatile.Read(ref _lastMetrics);

        /// <summary>Поколение последнего начатого прохода (0 — проходов не было).</summary>
        internal int PassGeneration => _validationPass.Generation;

        /// <summary>Состояние прохода: выполнено / выполняется / не выполнено (причина).</summary>
        internal string PassState => _validationPass.Describe();

        private const int ValidationDelayMs = 800; // Период сверки: полный проход — только при изменениях

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
        /// Тик таймера: полный проход выполняется, только когда он имеет смысл.
        /// <para>
        /// Иконка живёт в вьюхе активности, а вьюхи появляются позже загрузки документа (панель,
        /// раскладка, тема) и исчезают при сворачивании активности. Поэтому триггером служит не только
        /// изменение модели, но и смена подписи реализованных вьюх: без этого единственный
        /// «инициализационный» проход отрабатывал до построения поверхности, и ошибки не показывались
        /// до первой правки (баг «при загрузке и в покое ошибок нет»).
        /// </para>
        /// <para>
        /// Пока фоновый проход выполняется, новый не запускается: модель могла измениться, но второй
        /// параллельный проход только нагрузил бы процесс. Флаг изменения модели при этом не снимается,
        /// поэтому следующий тик после завершения запустит проход заново — свежим текстом.
        /// </para>
        /// </summary>
        private void ValidationTimer_Tick(object sender, EventArgs e)
        {
            if (!_isModelServiceAvailable)
            {
                return;
            }

            Stopwatch modelWalkWatch = Stopwatch.StartNew();
            IReadOnlyList<DesignerScopeEntry> scope = GetScope();
            modelWalkWatch.Stop();
            _modelWalkMs = modelWalkWatch.Elapsed.TotalMilliseconds;
            string currentSignature = scope == null ? null : BuildRealizedSignature(scope);

            bool isModelChanged = _isModelChanged;
            if (!ValidationSchedule.ShouldValidate(isModelChanged, _isNeedInitValidation, _lastRealizedSignature, currentSignature))
            {
                return;
            }

            if (_validationPass.IsRunning)
            {
                // Проход идёт: изменение модели не теряем (флаг остаётся), но второй проход не начинаем.
                return;
            }

            _isModelChanged = false;
            Validate(scope);

            // Подпись фиксируется после прохода, флаг инициализации снимается: дальше проход повторяется
            // только по изменению модели или появлению/пересозданию вьюх.
            _lastRealizedSignature = BuildRealizedSignature(scope);
            _isNeedInitValidation = false;
        }

        /// <summary>
        /// Активности рабочего процесса из модели дизайнера вместе с адресами. <c>null</c> — модель ещё
        /// не загружена (проход не имеет смысла, таймер повторит проверку на следующем тике).
        /// </summary>
        private IReadOnlyList<DesignerScopeEntry> GetScope()
        {
            try
            {
                ModelService modelService = _workflowDesignerEx.Context.Services.GetService<ModelService>();
                ModelItem rootModelItem = modelService?.Root;
                ModelItem implementationModelItem = rootModelItem?.Properties["Implementation"]?.Value;
                if (implementationModelItem == null
                    || !(implementationModelItem.GetCurrentValue() is System.Activities.Activity))
                {
                    return null;
                }

                return DesignerValidationScope.Collect(new ModelItemScopeNode(implementationModelItem));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetScope: модель недоступна: {ErrorText.Of(ex)}");
                return null;
            }
        }

        /// <summary>
        /// Подпись реализованных вьюх: для каждой активности с вьюхой — ключ с идентичностью вьюхи.
        /// Обход модели без компиляции выражений, поэтому его можно выполнять на каждом тике.
        /// </summary>
        private static string BuildRealizedSignature(IReadOnlyList<DesignerScopeEntry> scope)
        {
            if (scope == null)
            {
                return string.Empty;
            }

            List<string> keys = new List<string>(scope.Count);
            for (int index = 0; index < scope.Count; index++)
            {
                ModelItem modelItem = (scope[index].Node as ModelItemScopeNode)?.ModelItem;
                if (modelItem == null)
                {
                    continue;
                }

                object view;
                try
                {
                    view = modelItem.View;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"BuildRealizedSignature: View недоступен для {modelItem.ItemType?.Name}: {ErrorText.Of(ex)}");
                    continue;
                }

                if (view == null)
                {
                    continue;
                }

                keys.Add(ValidationSchedule.BuildViewKey(
                    index,
                    modelItem.ItemType?.Name,
                    System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(view)));
            }

            return ValidationSchedule.BuildSignature(keys);
        }

        /// <summary>
        /// Освобождает ресурсы, останавливает таймер и отменяет фоновый проход.
        /// <para>
        /// Отмена обязательна: без неё задача продолжает работать после закрытия вкладки и может
        /// применить результат к уже освобождённому дизайнеру.
        /// </para>
        /// </summary>
        public void Dispose()
        {
            if (_validationTimer != null)
            {
                _validationTimer.Stop();
                _validationTimer.Tick -= ValidationTimer_Tick;
                _validationTimer = null;
            }

            _validationPass.Cancel();

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
        /// Иконка валидации на активности (<c>WorkflowDesignerIconBorder</c> с именем
        /// <c>validationVisuals</c> из шаблона <c>WorkflowElementDesignerDefaultTemplate</c>).
        /// <para>
        /// Свои значения ставим <b>только</b> когда есть ошибки студии (выражения, типы аргументов).
        /// Если своих ошибок нет — снимаем локальные значения: локальное значение сильнее триггера
        /// шаблона, и раньше <c>Visibility = Collapsed</c> гасил нативную красную иконку WF
        /// (ошибки обязательных свойств, валидаторы активностей, «дочерняя активность некорректна»).
        /// </para>
        /// </summary>
        private void SetValidationIconDirectly(FrameworkElement validationVisuals, List<string> errors, string workflowMessage)
        {
            try
            {
                // validationVisuals это Border (WorkflowDesignerIconBorder)
                if (!(validationVisuals is Border border))
                {
                    return;
                }

                if (!ValidationIconState.ShouldDrawOwn(errors))
                {
                    // Отдаём отрисовку WF: триггеры по ModelItem.ValidationState снова работают.
                    border.ClearValue(UIElement.VisibilityProperty);
                    border.ClearValue(Border.BackgroundProperty);
                    border.ClearValue(FrameworkElement.ToolTipProperty);
                    return;
                }

                // Своя иконка: кисть палитры (красный — семантика ошибки), подсказка — свои ошибки + текст WF.
                border.Visibility = Visibility.Visible;
                border.Background = ValidationIconState.ErrorIconBrush();
                border.ToolTip = ValidationIconState.ComposeToolTip(errors, workflowMessage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetValidationIconDirectly ERROR: {ex}");
                Debug.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }

        private void WorkflowDesignerEx_ModelChanged(object sender, EventArgs e)
        {
            _isModelChanged = true;
        }

        /// <summary>
        /// Проход валидации: снимок модели и проверок на UI-потоке, затем фоновая задача.
        /// </summary>
        private void Validate(IReadOnlyList<DesignerScopeEntry> scope)
        {
            try
            {
                if (scope == null)
                {
                    return;
                }

                // WF-часть: свои иконки WF рисует по ModelItem.ValidationState, поэтому вызов остаётся здесь.
                var metrics = new DesignerValidationMetrics
                {
                    ActivityCount = scope.Count,
                    ModelWalkMs = _modelWalkMs
                };
                _pendingMetrics = metrics;

                ValidationService validationService = _workflowDesignerEx.Context.Services.GetService<ValidationService>();
                Stopwatch workflowWatch = Stopwatch.StartNew();
                validationService?.ValidateWorkflow();
                workflowWatch.Stop();
                metrics.ValidateWorkflowMs = validationService == null ? -1 : workflowWatch.Elapsed.TotalMilliseconds;

                // Блок using для выражений берётся один раз за проход: поле синглтона может измениться
                // (открыли документ с другим набором импортов), и снимок обязан быть согласованным.
                string usingNamespaces = RoslynExpressionValidator.Instance.UsingNamespaces;

                Stopwatch snapshotWatch = Stopwatch.StartNew();
                List<ActivityExpressionCheck> checks = CollectExpressionChecks(scope, usingNamespaces);
                snapshotWatch.Stop();
                metrics.SnapshotMs = snapshotWatch.Elapsed.TotalMilliseconds;
                metrics.CheckCount = checks.Count;

                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                int generation = _validationPass.Begin(out CancellationToken token);
                metrics.Generation = generation;
                Task.Run(() => RunValidation(generation, token, dispatcher, scope, checks, validationService), token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ОШИБКА в CustomValidation(): {ErrorText.Of(ex)}");
            }
        }

        /// <summary>
        /// Фоновая часть прохода: компиляция выражений и сравнение типов. Модель здесь не читается —
        /// только снимки проверок.
        /// </summary>
        private void RunValidation(
            int generation,
            CancellationToken token,
            Dispatcher dispatcher,
            IReadOnlyList<DesignerScopeEntry> scope,
            List<ActivityExpressionCheck> checks,
            ValidationService validationService)
        {
            try
            {
                DesignerValidationMetrics metrics = _pendingMetrics ?? new DesignerValidationMetrics();
                Stopwatch expressionsWatch = Stopwatch.StartNew();
                IReadOnlyList<ActivityValidationError> errors =
                    ExpressionErrorEvaluator.Evaluate(checks, _expressionCompiler, token);
                expressionsWatch.Stop();
                metrics.ExpressionsMs = expressionsWatch.Elapsed.TotalMilliseconds;

                if (token.IsCancellationRequested)
                {
                    return;
                }

                // Результат устарел (начался новый проход) — выбрасываем, иначе ошибки старого текста
                // встанут на активности нового.
                if (!_validationPass.TryComplete(generation))
                {
                    Debug.WriteLine("Валидация выражений: результат устарел, иконки не менялись.");
                    return;
                }


                dispatcher.BeginInvoke(new Action(() =>
                    ApplyValidationResult(generation, scope, errors, validationService)));
            }
            catch (OperationCanceledException)
            {
                // Отмена — не ошибка: проход устарел или вкладка закрыта.
            }
            catch (Exception ex)
            {
                _validationPass.TryFail(generation, ErrorText.Of(ex));
                Debug.WriteLine("Валидация выражений не выполнена: " + ErrorText.Of(ex));
            }
        }

        /// <summary>
        /// Применение результата к иконкам (UI-поток). Проверка поколения здесь обязательна: между
        /// постановкой в очередь диспетчера и её выполнением документ мог измениться.
        /// </summary>
        private void ApplyValidationResult(
            int generation,
            IReadOnlyList<DesignerScopeEntry> scope,
            IReadOnlyList<ActivityValidationError> errors,
            ValidationService validationService)
        {
            DesignerValidationMetrics metrics = _pendingMetrics ?? new DesignerValidationMetrics();
            Stopwatch applyWatch = Stopwatch.StartNew();
            metrics.ErrorCount = errors == null ? 0 : errors.Count;

            if (!_validationPass.IsCurrent(generation))
            {
                Debug.WriteLine("Валидация выражений: результат устарел на момент применения.");
                return;
            }

            Dictionary<string, List<string>> byAddress = GroupByAddress(errors);

            foreach (DesignerScopeEntry entry in scope)
            {
                ModelItem modelItem = (entry.Node as ModelItemScopeNode)?.ModelItem;
                if (modelItem == null || !(modelItem.GetCurrentValue() is System.Activities.Activity))
                {
                    continue;
                }

                // Одна «плохая» активность не должна обрывать проход по остальным: без try/catch
                // исключение внутри разбора аргументов оставляло часть активностей с устаревшей иконкой.
                try
                {
                    List<string> messages = byAddress.TryGetValue(entry.Address, out List<string> found)
                        ? found
                        : new List<string>();

                    if (messages.Count > 0)
                    {
                        metrics.AddErrorAddress(entry.Address);
                    }

                    SetValivationErrorsToModelItem(modelItem, messages, validationService);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Ошибка валидации активности {modelItem.ItemType?.Name} ({entry.Address}): {ErrorText.Of(ex)}");
                }
            }
            applyWatch.Stop();
            metrics.ApplyMs = applyWatch.Elapsed.TotalMilliseconds;

            // Замеры прохода: шаг 0 плана (цифры «до/после») и стенд фоновой валидации.
            Volatile.Write(ref _lastMetrics, metrics);
            Debug.WriteLine(metrics.Describe());

        }

        private static Dictionary<string, List<string>> GroupByAddress(IReadOnlyList<ActivityValidationError> errors)
        {
            Dictionary<string, List<string>> byAddress = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            if (errors == null)
            {
                return byAddress;
            }

            foreach (ActivityValidationError error in errors)
            {
                if (error == null || string.IsNullOrEmpty(error.Address))
                {
                    continue;
                }

                if (!byAddress.TryGetValue(error.Address, out List<string> messages))
                {
                    messages = new List<string>();
                    byAddress.Add(error.Address, messages);
                }

                messages.Add(error.Message);
            }

            return byAddress;
        }

        /// <summary>
        /// Снимки проверок выражений по всем активностям: читается модель (UI-поток), компиляции нет.
        /// </summary>
        private static List<ActivityExpressionCheck> CollectExpressionChecks(
            IReadOnlyList<DesignerScopeEntry> scope,
            string usingNamespaces)
        {
            List<ActivityExpressionCheck> checks = new List<ActivityExpressionCheck>();

            foreach (DesignerScopeEntry entry in scope)
            {
                ModelItem modelItem = (entry.Node as ModelItemScopeNode)?.ModelItem;
                if (modelItem == null)
                {
                    continue;
                }

                try
                {
                    CollectActivityChecks(modelItem, entry.Address, usingNamespaces, checks);
                }
                catch (Exception ex)
                {
                    // Модель могла измениться между обходом и чтением свойств: пропускаем активность,
                    // проход по остальным продолжается.
                    Debug.WriteLine($"CollectExpressionChecks: {modelItem.ItemType?.Name} ({entry.Address}): {ErrorText.Of(ex)}");
                }
            }

            return checks;
        }

        private static void CollectActivityChecks(
            ModelItem activityModelItem,
            string address,
            string usingNamespaces,
            List<ActivityExpressionCheck> checks)
        {
            ExpressionEnvironment environment = null;

            foreach (ModelProperty prop in activityModelItem.Properties)
            {
                //TODO: исследовать возможности валидации свойств
                if (!typeof(Argument).IsAssignableFrom(prop.PropertyType)
                    || prop.Value == null)
                {
                    continue;
                }

                if (environment == null)
                {
                    // Окружение одно на активность: раньше оно собиралось отдельно для каждого аргумента.
                    environment = ModelExpressionEnvironment.FromModelItem(activityModelItem, usingNamespaces);
                }

                checks.AddRange(CreateArgumentChecks(activityModelItem, address, environment, prop));
            }
        }

        /// <summary>
        /// Снимки проверок одного аргумента активности — ровно те данные, что нужны правилам
        /// <see cref="ExpressionErrorEvaluator"/>. Вынесено из прежнего <c>GetArgumentErrors</c>:
        /// там чтение модели и компиляция Roslyn были одним куском на UI-потоке.
        /// </summary>
        private static List<ActivityExpressionCheck> CreateArgumentChecks(
            ModelItem activityModelItem,
            string address,
            ExpressionEnvironment environment,
            ModelProperty argumentProperty)
        {
            List<ActivityExpressionCheck> checks = new List<ActivityExpressionCheck>();

            Type argumentGenericParameterType = argumentProperty.PropertyType.GetGenericArguments().FirstOrDefault();
            ModelItem propertyModelItem = argumentProperty.Value;
            ModelItem expressionModelItem = propertyModelItem?.Properties["Expression"]?.Value;
            if (expressionModelItem == null)
            {
                // Аргумент без выражения (только что добавленный): раньше здесь был NullReferenceException,
                // и весь проход валидации обрывался на середине.
                return checks;
            }

            // expressionModelItemType - если выражение не корректно, то тип CSharpValue, иначе Literal,
            // но рандомно может быть почему-то CSharpValue
            Type expressionModelItemType = expressionModelItem.ItemType;
            string argName = argumentProperty.Name;
            string expressionText = expressionModelItem.Properties["ExpressionText"]?.Value?.GetCurrentValue() as string ?? string.Empty;

            // Для OutArgument чекаем только, что это переменная или аргумент в зоне видимости
            if (typeof(OutArgument).IsAssignableFrom(argumentProperty.PropertyType))
            {
                checks.Add(new ActivityExpressionCheck
                {
                    Address = address,
                    ActivityTypeName = activityModelItem.ItemType?.Name,
                    ArgumentName = argName,
                    Kind = ExpressionCheckKind.OutArgument,
                    ExpressionText = expressionText,
                    CompileText = expressionText,
                    ArgumentType = argumentGenericParameterType,
                    ExpressionItemType = expressionModelItemType,
                    Environment = environment
                });

                return checks;
            }

            if (expressionModelItemType != null
                && expressionModelItemType.IsGenericType
                && expressionModelItemType.GetGenericTypeDefinition() == typeof(System.Activities.Expressions.Literal<>))
            {
                // Литерал: значение и его тип известны из модели, компилятору нужен текст в кавычках.
                dynamic literal = expressionModelItem.GetCurrentValue();
                string literalExpressionText = (string)literal.Value.ToString();
                Type literalType = literal.ResultType;
                if (literalType == typeof(string))
                {
                    literalExpressionText = $"\"{literalExpressionText}\"";
                }

                checks.Add(new ActivityExpressionCheck
                {
                    Address = address,
                    ActivityTypeName = activityModelItem.ItemType?.Name,
                    ArgumentName = argName,
                    Kind = ExpressionCheckKind.Literal,
                    ExpressionText = expressionText,
                    CompileText = literalExpressionText,
                    ArgumentType = argumentGenericParameterType,
                    ExpressionItemType = expressionModelItemType,
                    LiteralType = literalType,
                    Environment = environment
                });

                return checks;
            }

            bool isAssignValue = activityModelItem.GetCurrentValue().GetType() == typeof(System.Activities.Statements.Assign)
                && argName == "Value";

            checks.Add(new ActivityExpressionCheck
            {
                Address = address,
                ActivityTypeName = activityModelItem.ItemType?.Name,
                ArgumentName = argName,
                Kind = isAssignValue ? ExpressionCheckKind.AssignValue : ExpressionCheckKind.Expression,
                ExpressionText = expressionText,
                CompileText = expressionText,
                ArgumentType = argumentGenericParameterType,
                ExpressionItemType = expressionModelItemType,
                TargetType = isAssignValue ? TryGetAssignTargetType(activityModelItem) : null,
                Environment = environment
            });

            return checks;
        }

        /// <summary>Тип левой части <c>Assign</c> (нужен для сравнения типов при присваивании).</summary>
        private static Type TryGetAssignTargetType(ModelItem assignModelItem)
        {
            try
            {
                ModelItem toPropertyModelItem = assignModelItem.Properties["To"]?.Value;
                ModelItem toExpressionModelItem = toPropertyModelItem?.Properties["Expression"]?.Value;
                return toExpressionModelItem?.ItemType?.GenericTypeArguments.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryGetAssignTargetType: {ErrorText.Of(ex)}");
                return null;
            }
        }

        private void SetValivationErrorsToModelItem(ModelItem modelItem, List<string> errors, ValidationService validationService)
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
                // Текст WF (обязательные свойства, валидаторы активностей) — чтобы не потерять его,
                // когда свои ошибки есть и иконку рисуем мы.
                string workflowMessage = ValidationIconState.TryGetWorkflowMessage(validationService, modelItem);

                // Устанавливаем иконку напрямую на Border (обходной путь)
                SetValidationIconDirectly(validationVisuals, errors, workflowMessage);
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
                CustomWfDesigner.GetInstance().Log(ErrorText.Of(ex), 3);
            }
            return null;
        }
    }
}
