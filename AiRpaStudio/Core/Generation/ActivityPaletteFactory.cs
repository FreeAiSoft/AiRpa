using System;
using System.Activities.Expressions;
using System.Activities;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.CSharp.Activities;
using RPA.Helpers;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Значение аргумента в запросе палитры: C#-выражение или литерал
    /// (этап 4 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// Лямбд нет и быть не может: выражение всегда превращается в
    /// <see cref="CSharpValue{T}"/>/<see cref="CSharpReference{T}"/>, иначе узел не переживает
    /// сериализацию и ломает отображение (п. 1.2 плана).
    /// </para>
    /// </summary>
    /// <summary>
    /// Язык текстового выражения (карточка <c>AG-05</c>): C# или Visual Basic. Документ, пришедший
    /// с VB-выражениями, нельзя править C#-выражениями — конструктор такой узел не загрузит.
    /// </summary>
    internal enum PaletteExpressionLanguage
    {
        CSharp,

        VisualBasic
    }

    internal sealed class PaletteValue
    {
        private PaletteValue()
        {
        }

        /// <summary>Язык выражения (<see cref="PaletteExpressionLanguage.CSharp"/> по умолчанию).</summary>
        internal PaletteExpressionLanguage Language { get; private set; }

        /// <summary>Текст C#-выражения для <c>CSharpValue&lt;T&gt;</c>/<c>CSharpReference&lt;T&gt;</c>.</summary>
        internal string Expression { get; private set; }

        /// <summary>Литеральное значение (строка, число, <c>true</c>/<c>false</c>).</summary>
        internal object Literal { get; private set; }

        /// <summary>Задан литерал (в том числе <c>null</c>).</summary>
        internal bool HasLiteral { get; private set; }

        /// <summary>
        /// Имя типа значения для необобщённых <c>InArgument</c>/<c>OutArgument</c>
        /// (<c>Assign.To</c>): <c>String</c>, <c>Int32</c> и т. п.
        /// </summary>
        internal string TypeName { get; private set; }

        internal bool IsExpression => !string.IsNullOrWhiteSpace(Expression);

        internal static PaletteValue FromExpression(
            string expression,
            string typeName = null,
            PaletteExpressionLanguage language = PaletteExpressionLanguage.CSharp)
        {
            return new PaletteValue
            {
                Expression = expression?.Trim(),
                TypeName = string.IsNullOrWhiteSpace(typeName) ? null : typeName.Trim(),
                Language = language
            };
        }

        internal static PaletteValue FromLiteral(object literal, string typeName = null)
        {
            return new PaletteValue
            {
                Literal = literal,
                HasLiteral = true,
                TypeName = string.IsNullOrWhiteSpace(typeName) ? null : typeName.Trim()
            };
        }

        public override string ToString()
        {
            return HasLiteral ? $"{Literal ?? "null"}" : Expression;
        }
    }

    /// <summary>
    /// Переменная процесса в описании активности (этап 6 плана
    /// <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// Тип — имя из whitelist <see cref="PaletteTypeNames"/> (тип вне списка в XAML не попадает);
    /// значение по умолчанию подставляется в <c>Variable&lt;T&gt;.Default</c> как <c>CSharpValue&lt;T&gt;</c>
    /// (выражение) или как литерал.
    /// </para>
    /// </summary>
    internal sealed class PaletteVariableSpec
    {
        internal PaletteVariableSpec(string name, string typeName, PaletteValue defaultValue = null)
        {
            Name = name?.Trim();
            TypeName = typeName?.Trim();
            Default = defaultValue;
        }

        internal string Name { get; }

        /// <summary>Имя типа из whitelist (<c>String</c>, <c>Int32</c>, …).</summary>
        internal string TypeName { get; }

        /// <summary>Значение по умолчанию (<c>null</c> — переменная без инициализатора).</summary>
        internal PaletteValue Default { get; }

        public override string ToString()
        {
            return $"{TypeName} {Name}" + (Default == null ? string.Empty : " = " + Default);
        }
    }


    /// <summary>Запрос «собери активность»: тип, подпись, аргументы и вложенные активности.</summary>
    internal sealed class PaletteActivitySpec
    {
        internal PaletteActivitySpec(string activityType)
        {
            ActivityType = activityType;
            Arguments = new Dictionary<string, PaletteValue>(StringComparer.OrdinalIgnoreCase);
            Children = new Dictionary<string, List<PaletteActivitySpec>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Имя активности палитры (<c>WriteLine</c>, <c>If</c>, <c>ForEach</c>).</summary>
        internal string ActivityType { get; }

        /// <summary>Подпись активности в конструкторе (<c>DisplayName</c>).</summary>
        internal string DisplayName { get; set; }

        /// <summary>Аргументы типа для обобщённых активностей (<c>ForEach&lt;T&gt;</c>): имена типов.</summary>
        internal IList<string> TypeArguments { get; } = new List<string>();

        internal IDictionary<string, PaletteValue> Arguments { get; }

        /// <summary>Вложенные активности по имени контейнера (<c>Activities</c>, <c>Then</c>, <c>Body</c>).</summary>
        internal IDictionary<string, List<PaletteActivitySpec>> Children { get; }

        /// <summary>
        /// Переменные, объявленные в этой активности (этап 6 плана: <c>variables</c> JSON AST →
        /// <c>Sequence.Variables</c>). Активность без коллекции <c>Variables</c> их не принимает —
        /// для такой активности это ошибка запроса, а не «молча потерянная» переменная.
        /// </summary>
        internal IList<PaletteVariableSpec> Variables { get; } = new List<PaletteVariableSpec>();

        internal PaletteActivitySpec WithVariable(PaletteVariableSpec variable)
        {
            if (variable != null && !string.IsNullOrWhiteSpace(variable.Name))
            {
                Variables.Add(variable);
            }

            return this;
        }

        internal PaletteActivitySpec WithArgument(string name, PaletteValue value)
        {
            if (!string.IsNullOrWhiteSpace(name) && value != null)
            {
                Arguments[name.Trim()] = value;
            }

            return this;
        }

        internal PaletteActivitySpec WithChild(string containerName, PaletteActivitySpec child)
        {
            if (string.IsNullOrWhiteSpace(containerName) || child == null)
            {
                return this;
            }

            string key = containerName.Trim();
            if (!Children.TryGetValue(key, out List<PaletteActivitySpec> children))
            {
                children = new List<PaletteActivitySpec>();
                Children.Add(key, children);
            }

            children.Add(child);
            return this;
        }

        public override string ToString()
        {
            return $"{ActivityType}{(Arguments.Count == 0 ? string.Empty : "(" + string.Join(", ", Arguments.Keys) + ")")}";
        }
    }

    /// <summary>Результат сборки активности по описанию палитры.</summary>
    internal sealed class PaletteBuildResult
    {
        private PaletteBuildResult(Activity activity, IEnumerable<string> errors, IEnumerable<string> hints)
        {
            Activity = activity;

            if (errors != null)
            {
                Errors.AddRange(errors.Where(error => !string.IsNullOrWhiteSpace(error)));
            }

            if (hints != null)
            {
                Hints.AddRange(hints.Where(hint => !string.IsNullOrWhiteSpace(hint)));
            }
        }

        internal Activity Activity { get; }

        internal bool Ok => Activity != null && Errors.Count == 0;

        internal List<string> Errors { get; } = new List<string>();

        internal List<string> Hints { get; } = new List<string>();

        internal static PaletteBuildResult Success(Activity activity, IEnumerable<string> hints = null)
        {
            return new PaletteBuildResult(activity, null, hints);
        }

        internal static PaletteBuildResult Failure(IEnumerable<string> errors, IEnumerable<string> hints = null)
        {
            return new PaletteBuildResult(null, errors, hints);
        }

        internal string Describe()
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine(Ok
                ? "сборка активности: ок — " + Activity.GetType().Name
                : "сборка активности: ОШИБКИ");

            foreach (string error in Errors)
            {
                text.AppendLine("  ошибка: " + error);
            }

            foreach (string hint in Hints)
            {
                text.AppendLine("  подсказка: " + hint);
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Сборка <see cref="Activity"/> по описанию палитры: единственное место, где запрос агента
    /// превращается в объектную модель WF (этап 4 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// Значения аргументов становятся <see cref="CSharpValue{T}"/> (In) и <see cref="CSharpReference{T}"/>
    /// (Out/InOut) — тем, что конструктор показывает в редакторе выражений и что переживает сериализацию.
    /// Вложенные активности кладутся в реальные контейнеры: коллекция <c>Activities</c>, свойство-активность
    /// (<c>If.Then</c>) или обработчик <c>ActivityAction</c> (<c>ForEach.Body</c>).
    /// </para>
    /// </summary>
    internal sealed class ActivityPaletteFactory
    {
        /// <summary>Правило студии: только C#-выражения, без лямбд и делегатов.</summary>
        internal const string ExpressionPolicyHint =
            "значения аргументов задаются C#-выражением (CSharpValue<T>/CSharpReference<T>): " +
            "лямбды и делегаты запрещены — они не переживают сериализацию";

        private readonly ActivityPaletteRegistry _registry;

        internal ActivityPaletteFactory(ActivityPaletteRegistry registry = null)
        {
            _registry = registry ?? ActivityPaletteRegistry.Default;
        }

        internal ActivityPaletteRegistry Registry => _registry;

        /// <summary>Собирает активность; ошибки возвращаются в результате, а не исключением.</summary>
        internal PaletteBuildResult Build(PaletteActivitySpec spec)
        {
            var errors = new List<string>();
            var hints = new List<string>();

            Activity activity = BuildOne(spec, errors, hints, depth: 0);
            if (activity == null || errors.Count > 0)
            {
                return PaletteBuildResult.Failure(errors, hints);
            }

            return PaletteBuildResult.Success(activity, hints);
        }

        /// <summary>
        /// Собирает значение аргумента для уже существующей активности (сценарий
        /// <c>xaml.set_argument</c>): то же правило, что и при создании активности.
        /// </summary>
        internal bool TryCreateArgumentValue(
            PaletteActivityDescriptor descriptor,
            string argumentName,
            PaletteValue value,
            out object argumentValue,
            out List<string> errors,
            out List<string> hints)
        {
            argumentValue = null;
            errors = new List<string>();
            hints = new List<string>();

            if (descriptor == null)
            {
                errors.Add("xaml.activity: тип активности не опознан — аргумент задать нельзя");
                return false;
            }

            PaletteArgumentDescriptor argument = descriptor.FindArgument(argumentName);
            if (argument == null)
            {
                errors.Add($"у активности «{descriptor.Key}» нет аргумента «{argumentName}»; {descriptor.DescribeArguments()}");
                return false;
            }

            if (value == null)
            {
                errors.Add($"аргумент «{argument.Name}»: значение не задано");
                return false;
            }

            argumentValue = CreateArgumentValue(argument, value, errors, hints);
            return argumentValue != null && errors.Count == 0;
        }

        private Activity BuildOne(PaletteActivitySpec spec, List<string> errors, List<string> hints, int depth)
        {
            if (depth > 32)
            {
                errors.Add("xaml.activity: вложенность активностей глубже 32 уровней — вероятен цикл в запросе");
                return null;
            }

            if (spec == null || string.IsNullOrWhiteSpace(spec.ActivityType))
            {
                errors.Add("xaml.activity: не задан тип активности (activityType)");
                return null;
            }

            PaletteActivityDescriptor descriptor = _registry.Find(spec.ActivityType);
            if (descriptor == null)
            {
                errors.Add(
                    $"активность «{spec.ActivityType}» не найдена в палитре; известные: {_registry.KnownActivities()}");
                hints.Add("имя активности — короткое (Sequence, If, WriteLine) или полное имя типа активности");
                return null;
            }

            Type activityType = CloseType(descriptor, spec, errors, hints);

            // У обобщённых активностей аргументы и контейнеры описываются по закрытому типу:
            // у ForEach<T> свойство Body имеет тип ActivityAction<T>, и параметр T из открытого
            // определения в typeof(ActivityAction<>) не подставить.
            PaletteActivityDescriptor effectiveDescriptor = activityType == null || activityType == descriptor.Type
                ? descriptor
                : _registry.Describe(activityType, descriptor.Category) ?? descriptor;
            if (activityType == null)
            {
                return null;
            }

            Activity activity;
            try
            {
                activity = (Activity)Activator.CreateInstance(activityType);
            }
            catch (Exception ex)
            {
                errors.Add(ErrorText.Describe($"активность «{descriptor.Key}» не создана", ex));
                return null;
            }

            if (!string.IsNullOrWhiteSpace(spec.DisplayName))
            {
                activity.DisplayName = spec.DisplayName.Trim();
            }

            ApplyArguments(activity, activityType, effectiveDescriptor, spec, errors, hints);
            ApplyVariables(activity, activityType, descriptor, spec, errors, hints);
            ApplyChildren(activity, activityType, effectiveDescriptor, spec, errors, hints, depth);

            return activity;
        }

        private static Type CloseType(
            PaletteActivityDescriptor descriptor,
            PaletteActivitySpec spec,
            List<string> errors,
            List<string> hints)
        {
            if (!descriptor.IsGeneric)
            {
                return descriptor.Type;
            }

            if (spec.TypeArguments.Count != descriptor.GenericArity)
            {
                errors.Add(
                    $"активность «{descriptor.Key}» обобщённая: нужны аргументы типа " +
                    $"({descriptor.GenericArity} шт., например typeArguments: [\"String\"])");
                hints.Add("доступные имена типов: " + PaletteTypeNames.KnownNames());
                return null;
            }

            var typeArguments = new Type[descriptor.GenericArity];
            for (int index = 0; index < descriptor.GenericArity; index++)
            {
                string name = spec.TypeArguments[index];
                if (!PaletteTypeNames.TryResolve(name, out Type type))
                {
                    errors.Add($"тип «{name}» не разрешён для аргумента типа активности «{descriptor.Key}»");
                    hints.Add("доступные имена типов: " + PaletteTypeNames.KnownNames());
                    return null;
                }

                typeArguments[index] = type;
            }

            try
            {
                return descriptor.Type.MakeGenericType(typeArguments);
            }
            catch (Exception ex)
            {
                errors.Add(ErrorText.Describe($"тип активности «{descriptor.Key}» не собран", ex));
                return null;
            }
        }

        /// <summary>
        /// Переменные активности (этап 6 плана): <c>Sequence.Variables</c> и другие коллекции переменных.
        /// Тип берётся только из whitelist, иначе переменная не попала бы в XAML читаемым типом.
        /// </summary>
        private static void ApplyVariables(
            Activity activity,
            Type activityType,
            PaletteActivityDescriptor descriptor,
            PaletteActivitySpec spec,
            List<string> errors,
            List<string> hints)
        {
            if (spec.Variables.Count == 0)
            {
                return;
            }

            // Переменные ищутся через FindVariablesProperty: одноимённое свойство WF скрывает базовое,
            // и Type.GetProperty("Variables") на Sequence бросает AmbiguousMatchException.
            object collection = FindVariablesProperty(activityType)?.GetValue(activity);

            if (!(collection is IList variables))
            {
                errors.Add(
                    $"активность «{descriptor.Key}» не объявляет переменные: variables допустимы только у контейнеров " +
                    "(Sequence, Parallel, CompensableActivity)");
                hints.Add(
                    "объявите переменные в корне процесса: " +
                    "{\"variables\": [{\"name\": \"output\", \"type\": \"String\"}]}");
                return;
            }

            foreach (PaletteVariableSpec variableSpec in spec.Variables)
            {
                if (!PaletteTypeNames.TryResolve(variableSpec.TypeName, out Type variableType))
                {
                    errors.Add($"переменная «{variableSpec.Name}»: тип «{variableSpec.TypeName}» не разрешён");
                    hints.Add("доступные имена типов: " + PaletteTypeNames.KnownNames());
                    continue;
                }

                try
                {
                    Type closedVariableType = typeof(Variable<>).MakeGenericType(variableType);
                    object variable = Activator.CreateInstance(closedVariableType, variableSpec.Name);

                    if (variableSpec.Default != null)
                    {
                        object defaultValue = CreateVariableDefault(variableSpec, variableType, errors);
                        if (defaultValue != null)
                        {
                            FindProperty(closedVariableType, "Default")?.SetValue(variable, defaultValue);
                        }
                    }

                    variables.Add(variable);
                }
                catch (Exception ex)
                {
                    errors.Add(ErrorText.Describe($"переменная «{variableSpec.Name}» не создана", ex));
                }
            }
        }

        /// <summary>
        /// Коллекция переменных активности (этап 6 плана): <c>Sequence.Variables</c> и аналогичные.
        /// <para>
        /// Через <see cref="Type.GetProperty(string, BindingFlags)"/> не ищется: у <c>Sequence</c> свойство
        /// <c>Variables</c> скрывает одноимённое базовое, и поиск по имени бросает
        /// <see cref="AmbiguousMatchException"/>. Поэтому перебираются все свойства, а предпочтение отдаётся
        /// объявленному в самом типе.
        /// </para>
        /// </summary> 
        internal static PropertyInfo FindVariablesProperty(Type activityType)
        {
            return FindProperty(activityType, "Variables");
        }

        /// <summary>
        /// Свойство по имени без <see cref="AmbiguousMatchException"/>: WF прячет одноимённые свойства
        /// (<c>Sequence.Variables</c>, <c>Variable&lt;T&gt;.Default</c>), и <c>Type.GetProperty(name)</c>
        /// на них падает. Предпочтение — свойству, объявленному в самом типе.
        /// </summary>
        internal static PropertyInfo FindProperty(Type type, string name)
        {
            if (type == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            PropertyInfo[] candidates = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            return candidates.FirstOrDefault(property =>
                       string.Equals(property.Name, name, StringComparison.Ordinal)
                       && property.DeclaringType == type)
                   ?? candidates.FirstOrDefault(property =>
                       string.Equals(property.Name, name, StringComparison.Ordinal));
        }

        /// <summary>
        /// Значение по умолчанию переменной. <c>Variable&lt;T&gt;.Default</c> имеет тип <c>Activity&lt;T&gt;</c>,
        /// поэтому выражение даёт <c>CSharpValue&lt;T&gt;</c>, а литерал — <c>Literal&lt;T&gt;</c>.
        /// </summary>
        private static object CreateVariableDefault(
            PaletteVariableSpec spec,
            Type variableType,
            List<string> errors)
        {
            PaletteValue value = spec.Default;

            if (value.IsExpression)
            {
                return CreateCSharpValue(variableType, value.Expression);
            }

            if (!value.HasLiteral)
            {
                return null;
            }

            object literal = value.Literal;
            if (literal != null && !variableType.IsInstanceOfType(literal))
            {
                try
                {
                    literal = variableType == typeof(string)
                        ? Convert.ToString(literal, CultureInfo.InvariantCulture)
                        : Convert.ChangeType(literal, variableType, CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    errors.Add(ErrorText.Describe(
                        $"переменная «{spec.Name}»: значение по умолчанию не приводится к типу " +
                        PaletteTypeNames.NameOf(variableType), ex));
                    return null;
                }
            }

            try
            {
                return Activator.CreateInstance(typeof(Literal<>).MakeGenericType(variableType), literal);
            }
            catch (Exception ex)
            {
                errors.Add(ErrorText.Describe(
                    $"переменная «{spec.Name}»: значение по умолчанию не подставлено", ex));
                return null;
            }
        }

        private void ApplyArguments(
            Activity activity,
            Type activityType,
            PaletteActivityDescriptor descriptor,
            PaletteActivitySpec spec,
            List<string> errors,
            List<string> hints)
        {
            foreach (KeyValuePair<string, PaletteValue> pair in spec.Arguments)
            {
                PaletteArgumentDescriptor argument = descriptor.FindArgument(pair.Key);
                if (argument == null)
                {
                    errors.Add(
                        $"у активности «{descriptor.Key}» нет аргумента «{pair.Key}»; {descriptor.DescribeArguments()}");
                    continue;
                }

                PropertyInfo property = activityType.GetProperty(argument.Name, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !property.CanWrite)
                {
                    errors.Add($"аргумент «{argument.Name}» активности «{descriptor.Key}» нельзя задать");
                    continue;
                }

                object value = CreateArgumentValue(argument, pair.Value, errors, hints);
                if (value == null)
                {
                    continue;
                }

                try
                {
                    property.SetValue(activity, value);
                }
                catch (Exception ex)
                {
                    errors.Add(ErrorText.Describe(
                        $"аргумент «{argument.Name}» активности «{descriptor.Key}» не задан", ex));
                }
            }
        }

        private static object CreateArgumentValue(
            PaletteArgumentDescriptor argument,
            PaletteValue value,
            List<string> errors,
            List<string> hints)
        {
            if (value == null)
            {
                errors.Add($"аргумент «{argument.Name}»: значение не задано");
                return null;
            }

            if (argument.IsExpressionProperty)
            {
                // Условие While/DoWhile — Activity<T>: само значение является выражением,
                // поэтому InArgument<T> здесь не годится (иначе в XAML попал бы пустой узел).
                return CreateExpressionPropertyValue(argument, value, errors);
            }

            if (argument.Direction == PaletteArgumentDirection.Property)
            {
                return CreatePlainValue(argument, value, errors);
            }

            Type valueType = argument.ValueType ?? PaletteTypeNames.Resolve(value.TypeName, typeof(object));

            if (argument.IsReference)
            {
                if (!value.IsExpression)
                {
                    errors.Add(
                        $"аргумент «{argument.Name}» — ссылка ({argument.DirectionName}): нужно C#-выражение " +
                        "(expression), литерал недопустим");
                    hints.Add("пример: expression: \"output\" — имя переменной или поля");
                    return null;
                }

                return CreateReferenceArgument(argument, valueType, value, errors);
            }

            if (value.IsExpression)
            {
                return CreateValueArgument(argument, valueType, value, errors);
            }

            if (!value.HasLiteral)
            {
                errors.Add($"аргумент «{argument.Name}»: не задано ни expression, ни value");
                return null;
            }

            object literal = ConvertLiteral(value.Literal, valueType, argument, errors);
            if (errors.Count > 0 && literal == null)
            {
                return null;
            }

            try
            {
                return Activator.CreateInstance(typeof(InArgument<>).MakeGenericType(valueType), literal);
            }
            catch (Exception ex)
            {
                errors.Add(ErrorText.Describe($"аргумент «{argument.Name}»: литерал не подставлен", ex));
                hints.Add(ExpressionPolicyHint);
                return null;
            }
        }

        /// <summary>
        /// Значение свойства-выражения (<c>Activity&lt;T&gt;</c>, например <c>While.Condition</c>):
        /// выражение становится <see cref="CSharpValue{T}"/>, литерал — <see cref="Literal{T}"/>.
        /// </summary>
        private static object CreateExpressionPropertyValue(
            PaletteArgumentDescriptor argument,
            PaletteValue value,
            List<string> errors)
        {
            Type valueType = argument.ValueType ?? typeof(object);

            if (!value.IsExpression && !value.HasLiteral)
            {
                errors.Add(
                    $"аргумент «{argument.Name}»: у свойства-выражения нужно expression (C#-выражение) или value (литерал)");
                return null;
            }

            try
            {
                if (value.IsExpression)
                {
                    return CreateTextExpression(valueType, value.Expression, value.Language, reference: false);
                }

                object literal = ConvertLiteral(value.Literal, valueType, argument, errors);
                if (errors.Count > 0 && literal == null)
                {
                    return null;
                }

                return Activator.CreateInstance(typeof(Literal<>).MakeGenericType(valueType), literal);
            }
            catch (Exception ex)
            {
                errors.Add(ErrorText.Describe(
                    $"аргумент «{argument.Name}»: выражение не подставлено", ex));
                return null;
            }
        }


        private static object CreateValueArgument(
            PaletteArgumentDescriptor argument,
            Type valueType,
            PaletteValue value,
            List<string> errors)
        {
            try
            {
                object expressionActivity = CreateTextExpression(valueType, value.Expression, value.Language, reference: false);
                return Activator.CreateInstance(typeof(InArgument<>).MakeGenericType(valueType), expressionActivity);
            }
            catch (Exception ex)
            {
                errors.Add(ErrorText.Describe(
                    $"аргумент «{argument.Name}»: выражение не подставлено", ex));
                return null;
            }
        }

        private static object CreateReferenceArgument(
            PaletteArgumentDescriptor argument,
            Type valueType,
            PaletteValue value,
            List<string> errors)
        {
            try
            {
                object reference = CreateTextExpression(valueType, value.Expression, value.Language, reference: true);
                Type argumentType = argument.Direction == PaletteArgumentDirection.InOut
                    ? typeof(InOutArgument<>).MakeGenericType(valueType)
                    : typeof(OutArgument<>).MakeGenericType(valueType);

                return Activator.CreateInstance(argumentType, reference);
            }
            catch (Exception ex)
            {
                errors.Add(ErrorText.Describe(
                    $"аргумент «{argument.Name}»: ссылка не подставлена", ex));
                return null;
            }
        }

        /// <summary>
        /// Текстовое выражение на языке документа (карточка <c>AG-05</c>): C# или Visual Basic.
        /// Ссылка (<c>Out</c>/<c>InOut</c>) — <c>CSharpReference&lt;T&gt;</c>/<c>VisualBasicReference&lt;T&gt;</c>.
        /// </summary>
        private static object CreateTextExpression(
            Type valueType,
            string expression,
            PaletteExpressionLanguage language,
            bool reference)
        {
            Type openType;

            if (language == PaletteExpressionLanguage.VisualBasic)
            {
                openType = reference
                    ? typeof(Microsoft.VisualBasic.Activities.VisualBasicReference<>)
                    : typeof(Microsoft.VisualBasic.Activities.VisualBasicValue<>);
            }
            else
            {
                openType = reference ? typeof(CSharpReference<>) : typeof(CSharpValue<>);
            }

            return Activator.CreateInstance(openType.MakeGenericType(valueType), expression);
        }

        private static object CreateCSharpValue(Type valueType, string expression)
        {
            return CreateTextExpression(valueType, expression, PaletteExpressionLanguage.CSharp, reference: false);
        }

        private static object CreatePlainValue(
            PaletteArgumentDescriptor argument,
            PaletteValue value,
            List<string> errors)
        {
            object literal = value.HasLiteral ? value.Literal : value.Expression;
            if (literal == null)
            {
                errors.Add($"свойство «{argument.Name}»: пустое значение");
                return null;
            }

            return ConvertLiteral(literal, argument.PropertyType, argument, errors);
        }

        private static object ConvertLiteral(
            object literal,
            Type targetType,
            PaletteArgumentDescriptor argument,
            List<string> errors)
        {
            if (targetType == null || targetType == typeof(object))
            {
                return literal;
            }

            if (literal == null)
            {
                return null;
            }

            if (targetType.IsInstanceOfType(literal))
            {
                return literal;
            }

            try
            {
                if (targetType.IsEnum)
                {
                    return Enum.Parse(targetType, literal.ToString(), ignoreCase: true);
                }

                if (targetType == typeof(string))
                {
                    return Convert.ToString(literal, CultureInfo.InvariantCulture);
                }

                return Convert.ChangeType(literal, targetType, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                errors.Add(ErrorText.Describe(
                    $"аргумент «{argument.Name}»: значение «{literal}» не приводится к типу " +
                    PaletteTypeNames.NameOf(targetType), ex));
                return null;
            }
        }

        private void ApplyChildren(
            Activity activity,
            Type activityType,
            PaletteActivityDescriptor descriptor,
            PaletteActivitySpec spec,
            List<string> errors,
            List<string> hints,
            int depth)
        {
            foreach (KeyValuePair<string, List<PaletteActivitySpec>> pair in spec.Children)
            {
                PaletteContainerDescriptor container = descriptor.FindContainer(pair.Key);
                if (container == null)
                {
                    errors.Add(
                        $"у активности «{descriptor.Key}» нет контейнера «{pair.Key}»; {descriptor.DescribeContainers()}");
                    continue;
                }

                if (container.NeedsFlowStep)
                {
                    errors.Add(
                        $"контейнер «{container.Name}» активности «{descriptor.Key}» в базовом MVP не поддерживается: " +
                        "узлы блок-схемы требуют обёртки FlowStep");
                    hints.Add("используйте Sequence/If/While — их контейнеры поддерживаются");
                    continue;
                }

                switch (container.Kind)
                {
                    case PaletteContainerKind.ActivityCollection:
                        AddToCollection(activity, activityType, descriptor, container, pair.Value, errors, hints, depth);
                        break;

                    case PaletteContainerKind.ActivityChild:
                    case PaletteContainerKind.ActivityAction:
                        SetSingleChild(activity, activityType, descriptor, container, pair.Value, errors, hints, depth);
                        break;
                }
            }
        }

        private void AddToCollection(
            Activity activity,
            Type activityType,
            PaletteActivityDescriptor descriptor,
            PaletteContainerDescriptor container,
            List<PaletteActivitySpec> children,
            List<string> errors,
            List<string> hints,
            int depth)
        {
            PropertyInfo property = activityType.GetProperty(container.Name, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !(property.GetValue(activity) is IList collection))
            {
                errors.Add($"контейнер «{container.Name}» активности «{descriptor.Key}» недоступен");
                return;
            }

            foreach (PaletteActivitySpec childSpec in children ?? new List<PaletteActivitySpec>())
            {
                Activity child = BuildOne(childSpec, errors, hints, depth + 1);
                if (child != null)
                {
                    collection.Add(child);
                }
            }
        }

        private void SetSingleChild(
            Activity activity,
            Type activityType,
            PaletteActivityDescriptor descriptor,
            PaletteContainerDescriptor container,
            List<PaletteActivitySpec> children,
            List<string> errors,
            List<string> hints,
            int depth)
        {
            if (children == null || children.Count == 0)
            {
                return;
            }

            if (children.Count > 1)
            {
                errors.Add(
                    $"контейнер «{container.Name}» активности «{descriptor.Key}» принимает одну активность, " +
                    $"а передано {children.Count}: оберните их в Sequence");
                return;
            }

            Activity child = BuildOne(children[0], errors, hints, depth + 1);
            if (child == null)
            {
                return;
            }

            PropertyInfo property = activityType.GetProperty(container.Name, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite)
            {
                errors.Add($"контейнер «{container.Name}» активности «{descriptor.Key}» недоступен");
                return;
            }

            if (container.Kind == PaletteContainerKind.ActivityChild)
            {
                property.SetValue(activity, child);
                return;
            }

            object action = CreateActivityAction(container, child, errors);
            if (action != null)
            {
                property.SetValue(activity, action);
            }
        }

        private static object CreateActivityAction(
            PaletteContainerDescriptor container,
            Activity child,
            List<string> errors)
        {
            try
            {
                if (container.ChildValueType == null)
                {
                    return new ActivityAction { Handler = child };
                }

                Type actionType = typeof(ActivityAction<>).MakeGenericType(container.ChildValueType);
                object action = Activator.CreateInstance(actionType);

                Type delegateType = typeof(DelegateInArgument<>).MakeGenericType(container.ChildValueType);
                object delegateArgument = Activator.CreateInstance(delegateType, "item");

                actionType.GetProperty("Argument", BindingFlags.Public | BindingFlags.Instance)
                    ?.SetValue(action, delegateArgument);
                actionType.GetProperty("Handler", BindingFlags.Public | BindingFlags.Instance)
                    ?.SetValue(action, child);

                return action;
            }
            catch (Exception ex)
            {
                errors.Add(ErrorText.Describe(
                    $"обработчик «{container.Name}» не создан", ex));
                return null;
            }
        }
    }
}
