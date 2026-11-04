using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using RPA.Helpers;
using Newtonsoft.Json.Linq;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Проверенное описание процесса из JSON AST (этап 6 плана
    /// <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// Документ существует только после успешной строгой валидации: в нём уже нет «подозрительных»
    /// полей и неизвестных активностей, поэтому транспатору в объектную модель не нужно ничего
    /// перепроверять — он лишь собирает WF-дерево.
    /// </para>
    /// </summary>
    internal sealed class WorkflowJsonDocument
    {
        /// <summary>Имя класса процесса из описания (<c>null</c> — выводится из пути к файлу).</summary>
        internal string ClassName { get; set; }

        internal string NamespaceName { get; set; }

        internal string ProjectName { get; set; }

        internal string Description { get; set; }

        /// <summary>Дополнительные пространства имён C#-выражений из описания.</summary>
        internal IList<string> Namespaces { get; } = new List<string>();

        /// <summary>Дополнительные ссылки на сборки из описания.</summary>
        internal IList<string> References { get; } = new List<string>();

        /// <summary>Корень процесса: <c>Sequence</c> для формы <c>activities</c> либо явный <c>root</c>.</summary>
        internal PaletteActivitySpec Root { get; set; }

        /// <summary>Сколько активностей описано в JSON (без неявного корневого <c>Sequence</c>).</summary>
        internal int ActivityCount { get; set; }

        /// <summary>Имена объявленных переменных — для ответа инструмента.</summary>
        internal IList<string> VariableNames { get; } = new List<string>();
    }

    /// <summary>
    /// Результат строгой проверки JSON AST: либо документ, либо ошибки с путём до поля
    /// (<c>$.activities[2].arguments.To</c>) и подсказками.
    /// </summary>
    internal sealed class WorkflowJsonValidation
    {
        private WorkflowJsonValidation(
            WorkflowJsonDocument document,
            IEnumerable<string> errors,
            IEnumerable<string> hints)
        {
            Document = document;

            if (errors != null)
            {
                Errors.AddRange(errors.Where(error => !string.IsNullOrWhiteSpace(error)));
            }

            if (hints != null)
            {
                Hints.AddRange(hints.Where(hint => !string.IsNullOrWhiteSpace(hint)).Distinct());
            }
        }

        internal WorkflowJsonDocument Document { get; }

        internal bool Ok => Document != null && Errors.Count == 0;

        internal List<string> Errors { get; } = new List<string>();

        internal List<string> Hints { get; } = new List<string>();

        internal static WorkflowJsonValidation Success(WorkflowJsonDocument document, IEnumerable<string> hints = null)
        {
            return new WorkflowJsonValidation(document, null, hints);
        }

        internal static WorkflowJsonValidation Failure(IEnumerable<string> errors, IEnumerable<string> hints = null)
        {
            return new WorkflowJsonValidation(null, errors, hints);
        }

        internal string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine(Ok ? "json ast: ок" : "json ast: ОШИБКИ");
            text.AppendLine($"активностей в описании: {Document?.ActivityCount ?? 0}");
            text.AppendLine($"переменных: {Document?.VariableNames.Count ?? 0}");
            text.AppendLine($"ошибок: {Errors.Count}");

            if (Errors.Count > 0)
            {
                text.AppendLine("ошибки:");
                foreach (string error in Errors)
                {
                    text.AppendLine("  - " + error);
                }
            }

            if (Hints.Count > 0)
            {
                text.AppendLine("подсказки:");
                foreach (string hint in Hints)
                {
                    text.AppendLine("  - " + hint);
                }
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Строгая валидация JSON AST до построения модели (этап 6 плана
    /// <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// Почему проверки идут отдельным проходом, а не «по ходу сборки»: сборка идёт рефлексией по WF
    /// и падает исключением на первом же несоответствии, из-за чего агент видит одну ошибку вместо
    /// списка. Здесь собираются все ошибки сразу, каждая — с путём до поля, и только потом что-то
    /// строится. Отдельно проверяются whitelist активностей и whitelist типов переменных.
    /// </para>
    /// <para>
    /// Отдельное правило — <c>Assign</c>: у него <c>To</c>/<c>Value</c> необобщённые
    /// (<c>InArgument</c>/<c>OutArgument</c>), тип значения WF берёт из выражения. Тип выводится по
    /// объявленной переменной (<c>"To": "output"</c>) или по «соседнему» аргументу, а если вывести
    /// не из чего — это ошибка запроса, а не молчаливый <c>object</c>.
    /// </para>
    /// </summary>
    internal sealed class WorkflowJsonValidator
    {
        private static readonly Regex IdentifierPattern =
            new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

        private readonly ActivityPaletteRegistry _registry;
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _hints = new List<string>();
        private readonly List<string> _variableNames = new List<string>();

        /// <summary>Объявленные переменные: имя → тип (для вывода типа аргументов <c>Assign</c>).</summary>
        private readonly Dictionary<string, string> _variables = new Dictionary<string, string>(StringComparer.Ordinal);

        private int _activityCount;

        internal WorkflowJsonValidator(ActivityPaletteRegistry registry = null)
        {
            _registry = registry ?? ActivityPaletteRegistry.Default;
        }

        /// <summary>Проверяет описание, полученное текстом (агент присылает JSON-строку).</summary>
        internal WorkflowJsonValidation Validate(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return WorkflowJsonValidation.Failure(
                    new[] { "xaml.ast: описание процесса не задано" },
                    new[] { WorkflowJsonSchema.Describe(), "пример: " + WorkflowJsonSchema.Example() });
            }

            if (json.Length > WorkflowJsonSchema.MaxJsonChars)
            {
                return WorkflowJsonValidation.Failure(new[]
                {
                    $"xaml.ast: описание больше {WorkflowJsonSchema.MaxJsonChars} символов — " +
                    "передавайте только описание процесса, а не файл целиком"
                });
            }

            JToken token;
            try
            {
                token = JToken.Parse(json);
            }
            catch (JsonReaderException ex)
            {
                return WorkflowJsonValidation.Failure(
                    new[]
                    {
                        ErrorText.Describe(
                            $"xaml.ast: JSON не разобран (строка {ex.LineNumber}, позиция {ex.LinePosition})", ex)
                    },
                    new[] { WorkflowJsonSchema.Describe(), "пример: " + WorkflowJsonSchema.Example() });
            }

            return Validate(token);
        }

        /// <summary>Проверяет уже разобранное описание.</summary>
        internal WorkflowJsonValidation Validate(JToken token)
        {
            _errors.Clear();
            _hints.Clear();
            _variables.Clear();
            _variableNames.Clear();
            _activityCount = 0;

            if (!(token is JObject document))
            {
                return WorkflowJsonValidation.Failure(
                    new[] { "xaml.ast: ожидался JSON-объект с описанием процесса" },
                    new[] { WorkflowJsonSchema.Describe() });
            }

            CheckFields(document, WorkflowJsonSchema.DocumentFields, "$", "процесса");

            var result = new WorkflowJsonDocument
            {
                ClassName = ReadText(document, "$", "name", "className"),
                NamespaceName = ReadText(document, "$", "namespace"),
                ProjectName = ReadText(document, "$", "project"),
                Description = ReadText(document, "$", "description")
            };

            ReadTextArray(document, "$", "namespaces", result.Namespaces);
            ReadTextArray(document, "$", "references", result.References);

            // Переменные процесса — до дерева: их типы нужны для вывода типа аргументов Assign.
            List<PaletteVariableSpec> rootVariables = document["variables"] == null
                ? new List<PaletteVariableSpec>()
                : ValidateVariables(document["variables"], "$.variables");

            JToken explicitRoot = document["root"] ?? document["activity"];
            JToken activitiesToken = document["activities"];
            JArray activities = activitiesToken as JArray;

            if (activitiesToken != null && activities == null)
            {
                AddError("$.activities: ожидался массив активностей");
            }

            if (explicitRoot != null && activitiesToken != null)
            {
                AddError("$: укажите либо activities, либо root — не оба сразу");
            }

            PaletteActivitySpec root = null;

            if (explicitRoot != null)
            {
                root = ValidateActivity(explicitRoot, "$.root", 0, out PaletteActivityDescriptor rootDescriptor);

                if (root != null && rootVariables.Count > 0 && !SupportsVariables(rootDescriptor))
                {
                    AddError(
                        "$.variables: корневая активность не объявляет переменные — " +
                        "используйте activities (корень Sequence) или объявите variables внутри контейнера");
                }
            }
            else if (activities != null)
            {
                root = new PaletteActivitySpec("Sequence");

                for (int index = 0; index < activities.Count; index++)
                {
                    PaletteActivitySpec child = ValidateActivity(activities[index], $"$.activities[{index}]", 1, out _);
                    if (child != null)
                    {
                        root.WithChild("Activities", child);
                    }
                }

                if (activities.Count == 0)
                {
                    AddError("$.activities: опишите хотя бы одну активность");
                }
            }
            else if (activitiesToken == null)
            {
                AddError("$: нет ни activities, ни root — опишите шаги процесса");
                AddHint("пример: " + WorkflowJsonSchema.Example());
            }

            if (root != null)
            {
                foreach (PaletteVariableSpec variable in rootVariables)
                {
                    root.WithVariable(variable);
                }
            }

            result.Root = root;
            result.ActivityCount = _activityCount;

            foreach (string name in _variableNames)
            {
                if (!result.VariableNames.Contains(name))
                {
                    result.VariableNames.Add(name);
                }
            }

            if (result.Root == null)
            {
                AddError("xaml.ast: дерево активностей не собрано");
            }

            return _errors.Count == 0
                ? WorkflowJsonValidation.Success(result, _hints)
                : WorkflowJsonValidation.Failure(_errors, _hints);
        }

        /// <summary>Проверяет одну активность и возвращает её описание для сборки.</summary>
        private PaletteActivitySpec ValidateActivity(
            JToken token,
            string path,
            int depth,
            out PaletteActivityDescriptor descriptor)
        {
            descriptor = null;

            if (depth > WorkflowJsonSchema.MaxDepth)
            {
                AddError($"{path}: вложенность активностей глубже {WorkflowJsonSchema.MaxDepth} уровней — вероятен цикл");
                return null;
            }

            if (!(token is JObject node))
            {
                AddError($"{path}: ожидался объект активности");
                return null;
            }

            CheckFields(node, WorkflowJsonSchema.ActivityFields, path, "активности");

            string type = ReadText(node, path, "type", "activityType");
            if (type == null)
            {
                AddError($"{path}.type: тип активности не задан (например \"WriteLine\")");
                AddHint("доступные активности: " + _registry.KnownActivities());
                return null;
            }

            descriptor = _registry.Find(type);
            if (descriptor == null)
            {
                AddError($"{path}.type: активность «{type}» отсутствует в whitelist палитры");
                AddHint("доступные активности: " + _registry.KnownActivities());
                return null;
            }

            _activityCount++;
            if (_activityCount > WorkflowJsonSchema.MaxActivities)
            {
                AddError($"{path}: активностей больше {WorkflowJsonSchema.MaxActivities} — описание отклонено");
                return null;
            }

            var spec = new PaletteActivitySpec(descriptor.Key)
            {
                DisplayName = ReadText(node, path, "displayName")
            };

            ValidateTypeArguments(node, path, descriptor, spec);

            // Переменные — до аргументов: их типы участвуют в выводе типа Assign.
            if (node["variables"] != null)
            {
                List<PaletteVariableSpec> variables = ValidateVariables(node["variables"], path + ".variables");

                if (variables.Count > 0 && !SupportsVariables(descriptor))
                {
                    AddError(
                        $"{path}.variables: активность «{descriptor.Key}» не объявляет переменные — " +
                        "variables допустимы только у контейнеров (Sequence, Parallel)");
                }
                else
                {
                    foreach (PaletteVariableSpec variable in variables)
                    {
                        spec.WithVariable(variable);
                    }
                }
            }

            if (node["arguments"] != null)
            {
                if (!(node["arguments"] is JObject arguments))
                {
                    AddError($"{path}.arguments: ожидался объект «аргумент → значение»");
                }
                else
                {
                    ValidateArguments(arguments, descriptor, spec, path + ".arguments");
                }
            }

            if (node["children"] != null)
            {
                if (!(node["children"] is JObject children))
                {
                    AddError($"{path}.children: ожидался объект «контейнер → массив активностей»");
                }
                else
                {
                    ValidateChildren(children, descriptor, spec, path + ".children", depth);
                }
            }

            return spec;
        }

        private void ValidateTypeArguments(
            JObject node,
            string path,
            PaletteActivityDescriptor descriptor,
            PaletteActivitySpec spec)
        {
            JToken token = node["typeArguments"];

            if (token == null)
            {
                if (descriptor.IsGeneric)
                {
                    AddError(
                        $"{path}.typeArguments: активность «{descriptor.Key}» обобщённая — укажите аргументы типа " +
                        $"({descriptor.GenericArity} шт., например [\"String\"])");
                    AddHint("доступные типы: " + PaletteTypeNames.KnownNames());
                }

                return;
            }

            if (!(token is JArray typeArguments))
            {
                AddError($"{path}.typeArguments: ожидался массив имён типов");
                return;
            }

            if (!descriptor.IsGeneric)
            {
                AddError($"{path}.typeArguments: активность «{descriptor.Key}» не обобщённая — аргументы типа не нужны");
                return;
            }

            if (typeArguments.Count != descriptor.GenericArity)
            {
                AddError(
                    $"{path}.typeArguments: активность «{descriptor.Key}» ждёт {descriptor.GenericArity} аргументов типа, " +
                    $"передано {typeArguments.Count}");
                return;
            }

            foreach (JToken item in typeArguments)
            {
                if (item.Type != JTokenType.String)
                {
                    AddError($"{path}.typeArguments: имена типов задаются строками");
                    return;
                }

                string name = item.Value<string>()?.Trim();
                if (!PaletteTypeNames.TryResolve(name, out _))
                {
                    AddError($"{path}.typeArguments: тип «{name}» не разрешён");
                    AddHint("доступные типы: " + PaletteTypeNames.KnownNames());
                    return;
                }

                spec.TypeArguments.Add(name);
            }
        }

        private void ValidateArguments(
            JObject arguments,
            PaletteActivityDescriptor descriptor,
            PaletteActivitySpec spec,
            string path)
        {
            foreach (JProperty property in arguments.Properties())
            {
                string argumentPath = path + "." + property.Name;

                PaletteArgumentDescriptor argument = descriptor.FindArgument(property.Name);
                if (argument == null)
                {
                    AddError(
                        $"{argumentPath}: у активности «{descriptor.Key}» нет аргумента «{property.Name}»; " +
                        descriptor.DescribeArguments());
                    continue;
                }

                PaletteValue value = ParseValue(property.Value, argumentPath, argument.IsReference, out bool ok);
                if (ok && value != null)
                {
                    spec.WithArgument(argument.Name, value);
                }
            }

            if (string.Equals(descriptor.Key, "Assign", StringComparison.OrdinalIgnoreCase))
            {
                InferAssignTypes(spec, path);
            }
        }

        /// <summary>
        /// Тип значений <c>Assign</c>: у необобщённых <c>InArgument</c>/<c>OutArgument</c> тип задаёт
        /// вызывающий. Он выводится из объявленной переменной или из соседнего аргумента; если вывести
        /// не из чего — ошибка запроса (иначе в XAML попал бы <c>object</c> и WF дал бы ошибку типов).
        /// </summary>
                private void InferAssignTypes(PaletteActivitySpec spec, string path)
        {
            PaletteValue to = FindArgument(spec, "To");
            PaletteValue value = FindArgument(spec, "Value");

            if (to == null || value == null)
            {
                return;
            }

            string toType = TypeNameOf(to) ?? TypeNameOf(value);
            string valueType = TypeNameOf(value) ?? toType;

            if (toType == null && valueType == null)
            {
                AddError(
                    $"{path}: у Assign тип To/Value не определён — укажите \"type\" (например String) " +
                    "или объявите переменную в variables");
                AddHint("пример: \"To\": {\"expression\": \"output\", \"type\": \"String\"}");
                return;
            }

            // Тип проставляется обоим аргументам: To/Value у Assign необобщённые, и без явного типа
            // сериализатор WF пишет x:Object — такой узел невалиден.
            spec.WithArgument("To", WithTypeName(to, toType ?? valueType));
            spec.WithArgument("Value", WithTypeName(value, valueType ?? toType));
        }

        private string TypeNameOf(PaletteValue value)
        {
            if (value == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(value.TypeName))
            {
                return value.TypeName;
            }

            if (!value.IsExpression)
            {
                return null;
            }

            string expression = value.Expression.Trim();
            if (!IdentifierPattern.IsMatch(expression))
            {
                return null;
            }

            return _variables.TryGetValue(expression, out string typeName) ? typeName : null;
        }

        private static PaletteValue WithTypeName(PaletteValue value, string typeName)
        {
            return value.IsExpression
                ? PaletteValue.FromExpression(value.Expression, typeName)
                : PaletteValue.FromLiteral(value.Literal, typeName);
        }

        private static PaletteValue FindArgument(PaletteActivitySpec spec, string name)
        {
            return spec.Arguments.TryGetValue(name, out PaletteValue value) ? value : null;
        }

        private void ValidateChildren(
            JObject children,
            PaletteActivityDescriptor descriptor,
            PaletteActivitySpec spec,
            string path,
            int depth)
        {
            foreach (JProperty property in children.Properties())
            {
                string containerPath = path + "." + property.Name;

                PaletteContainerDescriptor container = descriptor.FindContainer(property.Name);
                if (container == null)
                {
                    AddError(
                        $"{containerPath}: у активности «{descriptor.Key}» нет контейнера «{property.Name}»; " +
                        descriptor.DescribeContainers());
                    continue;
                }

                if (container.NeedsFlowStep)
                {
                    AddError(
                        $"{containerPath}: контейнер «{container.Name}» активности «{descriptor.Key}» в JSON AST " +
                        "не поддерживается: узлы блок-схемы требуют обёртки FlowStep");
                    AddHint("используйте Sequence/If/While — их контейнеры поддерживаются");
                    continue;
                }

                if (!(property.Value is JArray items))
                {
                    AddError($"{containerPath}: ожидался массив активностей");
                    continue;
                }

                for (int index = 0; index < items.Count; index++)
                {
                    PaletteActivitySpec child = ValidateActivity(
                        items[index], $"{containerPath}[{index}]", depth + 1, out _);

                    if (child != null)
                    {
                        spec.WithChild(container.Name, child);
                    }
                }
            }
        }

        private List<PaletteVariableSpec> ValidateVariables(JToken token, string path)
        {
            var specs = new List<PaletteVariableSpec>();

            if (!(token is JArray array))
            {
                AddError($"{path}: ожидался массив переменных");
                return specs;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);

            for (int index = 0; index < array.Count; index++)
            {
                string itemPath = $"{path}[{index}]";

                if (!(array[index] is JObject variable))
                {
                    AddError($"{itemPath}: ожидался объект переменной");
                    continue;
                }

                CheckFields(variable, WorkflowJsonSchema.VariableFields, itemPath, "переменной");

                string name = ReadText(variable, itemPath, "name");
                if (name == null)
                {
                    AddError($"{itemPath}.name: имя переменной не задано");
                    continue;
                }

                if (!IdentifierPattern.IsMatch(name))
                {
                    AddError($"{itemPath}.name: «{name}» не является идентификатором C# (латиница, цифры, «_»; не с цифры)");
                    continue;
                }

                if (!names.Add(name))
                {
                    AddError($"{itemPath}.name: переменная «{name}» объявлена дважды в одной области видимости");
                    continue;
                }

                string typeName = ReadText(variable, itemPath, "type", "typeName");
                if (typeName == null)
                {
                    AddError($"{itemPath}.type: тип переменной не задан");
                    AddHint("доступные типы: " + PaletteTypeNames.KnownNames());
                    continue;
                }

                if (!PaletteTypeNames.TryResolve(typeName, out _))
                {
                    AddError($"{itemPath}.type: тип «{typeName}» не разрешён для переменной");
                    AddHint("доступные типы: " + PaletteTypeNames.KnownNames());
                    continue;
                }

                PaletteValue defaultValue = null;
                if (variable["default"] != null)
                {
                    defaultValue = ParseValue(variable["default"], itemPath + ".default", false, out bool defaultOk);
                    if (!defaultOk)
                    {
                        continue;
                    }
                }

                specs.Add(new PaletteVariableSpec(name, typeName, defaultValue));
                _variables[name] = typeName;

                if (!_variableNames.Contains(name))
                {
                    _variableNames.Add(name);
                }
            }

            return specs;
        }

        /// <summary>
        /// Значение аргумента: строка — C#-выражение, число/логическое — литерал, объект — явная форма
        /// (<c>expression</c> или <c>value</c> с необязательным <c>type</c>).
        /// </summary>
        private PaletteValue ParseValue(JToken token, string path, bool reference, out bool ok)
        {
            ok = false;

            if (token == null || token.Type == JTokenType.Null)
            {
                AddError($"{path}: значение не задано");
                return null;
            }

            if (token is JObject valueObject)
            {
                return ParseValueObject(valueObject, path, reference, out ok);
            }

            if (token is JArray)
            {
                AddError($"{path}: массив как значение аргумента не поддерживается");
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                string expression = token.Value<string>();

                if (string.IsNullOrWhiteSpace(expression))
                {
                    AddError($"{path}: пустое C#-выражение");
                    return null;
                }

                ok = true;
                return PaletteValue.FromExpression(expression);
            }

            if (reference)
            {
                AddError($"{path}: аргумент-ссылка (Out/InOut) принимает только C#-выражение, не литерал");
                AddHint("пример: {\"To\": {\"expression\": \"output\", \"type\": \"String\"}}");
                return null;
            }

            ok = true;
            return PaletteValue.FromLiteral(LiteralOf(token, path, out ok));
        }

        private PaletteValue ParseValueObject(JObject valueObject, string path, bool reference, out bool ok)
        {
            ok = false;

            CheckFields(valueObject, WorkflowJsonSchema.ValueFields, path, "значения аргумента");

            string expression = ReadText(valueObject, path, "expression", "expr");
            string typeName = ReadText(valueObject, path, "type", "typeName");
            bool hasValue = valueObject["value"] != null;
            // Явный литерал null тоже считается заданным значением: {"value": null}.

            if (typeName != null && !PaletteTypeNames.TryResolve(typeName, out _))
            {
                AddError($"{path}.type: тип «{typeName}» не разрешён");
                AddHint("доступные типы: " + PaletteTypeNames.KnownNames());
                return null;
            }

            if (expression != null && hasValue)
            {
                AddError($"{path}: укажите либо expression, либо value — не оба сразу");
                return null;
            }

            if (expression != null)
            {
                ok = true;
                return PaletteValue.FromExpression(expression, typeName);
            }

            if (!hasValue)
            {
                AddError($"{path}: нужен expression (C#-выражение) или value (литерал)");
                AddHint("пример: {\"value\": \"готово\", \"type\": \"String\"} — литерал, " +
                        "{\"expression\": \"output\"} — выражение");
                return null;
            }

            if (reference)
            {
                AddError($"{path}: аргумент-ссылка (Out/InOut) принимает только expression, не value");
                return null;
            }

            object literal = LiteralOf(valueObject["value"], path + ".value", out ok);
            if (!ok)
            {
                return null;
            }

            ok = true;
            return PaletteValue.FromLiteral(literal, typeName);
        }

        private object LiteralOf(JToken token, string path, out bool ok)
        {
            ok = true;

            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token is JValue value)
            {
                return value.Value;
            }

            ok = false;
            AddError($"{path}: литерал задаётся строкой, числом или логическим значением");
            return null;
        }

        private void ReadTextArray(JObject document, string path, string field, IList<string> target)
        {
            JToken token = document[field];
            if (token == null)
            {
                return;
            }

            if (!(token is JArray array))
            {
                AddError($"{path}.{field}: ожидался массив строк");
                return;
            }

            foreach (JToken item in array)
            {
                if (item.Type != JTokenType.String)
                {
                    AddError($"{path}.{field}: ожидались строки");
                    return;
                }

                string value = item.Value<string>()?.Trim();
                if (!string.IsNullOrWhiteSpace(value) && !target.Contains(value))
                {
                    target.Add(value);
                }
            }
        }

        private string ReadText(JObject token, string path, params string[] names)
        {
            foreach (string name in names)
            {
                JToken value = token[name];

                if (value == null || value.Type == JTokenType.Null)
                {
                    continue;
                }

                if (value.Type != JTokenType.String)
                {
                    AddError($"{path}.{name}: ожидалась строка");
                    return null;
                }

                string text = value.Value<string>();
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }

            return null;
        }

        private void CheckFields(JObject token, IReadOnlyList<string> allowed, string path, string kind)
        {
            foreach (JProperty property in token.Properties())
            {
                bool known = allowed.Any(name =>
                    string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase));

                if (known)
                {
                    continue;
                }

                AddError(
                    $"{path}.{property.Name}: неизвестное поле в описании {kind}; " +
                    "допустимые: " + string.Join(", ", allowed));
                AddHint("схема строгая: лишние поля отклоняются, чтобы опечатка в имени поля не терялась молча");
            }
        }

        private static bool SupportsVariables(PaletteActivityDescriptor descriptor)
        {
            return ActivityPaletteFactory.FindVariablesProperty(descriptor?.Type) != null;
        }

        private void AddError(string message)
        {
            if (!string.IsNullOrWhiteSpace(message) && !_errors.Contains(message))
            {
                _errors.Add(message);
            }
        }

        private void AddHint(string hint)
        {
            if (!string.IsNullOrWhiteSpace(hint) && !_hints.Contains(hint))
            {
                _hints.Add(hint);
            }
        }
    }
}
