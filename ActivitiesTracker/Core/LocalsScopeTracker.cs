using System;
using System.Activities;
using System.Activities.Tracking;
using System.Collections.Generic;
using System.Reflection;
using ActivitiesTracker.Entities;
using ActivitiesTracker.Extensions;

namespace ActivitiesTracker.Core
{
    /// <summary>
    /// Стек скоупов прогона для панели «Локальные переменные».
    /// <para>
    /// Каждый <see cref="ActivityStateRecord"/> с состоянием <c>Executing</c> кладёт frame в стек,
    /// а <c>Closed</c>/<c>Canceled</c>/<c>Faulted</c> — снимает его. Помимо снимка переменных/аргументов
    /// frame обновляется по входным/выходным аргументам дочерних активностей: значение переменной
    /// родительского скоупа перезаписывается свежим значением из привязки «аргумент → переменная».
    /// </para>
    /// <para>
    /// Трекинг-профиль студии использует <c>Variables = {"*"}</c>, поэтому снимок <c>record.Variables</c>
    /// содержит только переменные, объявленные самой активностью (а не весь видимый scope). Это позволяет
    /// хранить по одному frame на скоуп и собирать видимые переменные от вершины стека с затенением имён.
    /// </para>
    /// </summary>
    public static class LocalsScopeTracker
    {
        private static readonly object Gate = new object();
        private static readonly List<Frame> Frames = new List<Frame>();

        private static readonly PropertyInfo RuntimeArgumentsProperty =
            typeof(Activity).GetProperty("RuntimeArguments", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo BoundArgumentProperty =
            typeof(RuntimeArgument).GetProperty("BoundArgument", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo RelationshipToParentProperty =
            typeof(Activity).GetProperty("RelationshipToParent", BindingFlags.Instance | BindingFlags.NonPublic);

        // Значение internal enum System.Activities.Activity+RelationshipType.ImplementationChild.
        private const byte RelationshipImplementationChild = 0x02;

        /// <summary>
        /// Применяет трекинг-запись к стеку: push на <c>Executing</c>, pop на
        /// <c>Closed</c>/<c>Canceled</c>/<c>Faulted</c>, затем обновляет значения переменных
        /// скоупов из входных/выходных аргументов записи по привязке «аргумент → переменная».
        /// </summary>
        public static void Observe(ActivityStateRecord record)
        {
            if (record == null)
            {
                return;
            }

            lock (Gate)
            {
                try
                {
                    ApplyState(record);
                }
                catch
                {
                    // Обновление стека не должно ломать трекинг.
                }

                try
                {
                    RefreshVariablesFromArguments(record);
                }
                catch
                {
                    // Обновление значений — best effort; недоступный internal-API не мешает отладке.
                }
            }
        }

        /// <summary>
        /// Очищает стек скоупов. Вызывается раннером на границах прогона.
        /// </summary>
        public static void Reset()
        {
            lock (Gate)
            {
                Frames.Clear();
            }
        }

        /// <summary>
        /// Собирает видимый scope от вершины стека с затенением имён.
        /// Если текущая запись не была в стеке — фолбэк на снимок самой записи.
        /// </summary>
        public static List<LocalVar> GetVisibleLocals(ActivityStateRecord record)
        {
            if (record == null)
            {
                return new List<LocalVar>();
            }

            lock (Gate)
            {
                string instanceId = record.Activity?.InstanceId;
                if (string.IsNullOrEmpty(instanceId) || FindFrameIndex(instanceId) < 0)
                {
                    return LocalsHelper.GetLocalsAndArgs(record);
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                var locals = new List<LocalVar>();

                for (int i = Frames.Count - 1; i >= 0; i--)
                {
                    Frame frame = Frames[i];

                    if (i < Frames.Count - 1 && Frames[i + 1].IsImplementationChild)
                    {
                        // implementation-граница: у родителя видны только аргументы,
                        // публичные предки дальше не видны (Этап 6 плана).
                        AddLocals(frame.Arguments, frame.Name, seen, locals);
                        break;
                    }

                    AddLocals(frame.Variables, frame.Name, seen, locals);
                    AddLocals(frame.Arguments, frame.Name, seen, locals);
                }

                return locals;
            }
        }

        private static void ApplyState(ActivityStateRecord record)
        {
            string state = record.State;
            string instanceId = record.Activity?.InstanceId;

            if (string.Equals(state, "Executing", StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(instanceId) && FindFrameIndex(instanceId) < 0)
                {
                    Activity activity = GetActivity(record.Activity);
                    byte relationshipToParent = (byte)GetRelationshipToParent(activity);

                    Frames.Add(new Frame(
                        instanceId,
                        record.Activity?.Name,
                        CopyVariables(record),
                        new Dictionary<string, object>(LocalsHelper.GetArgsFromRecord(record)),
                        relationshipToParent));
                }
            }
            else if (IsClosingState(state))
            {
                RemoveFrame(instanceId);
            }
        }

        private static bool IsClosingState(string state)
        {
            return string.Equals(state, "Closed", StringComparison.Ordinal)
                || string.Equals(state, "Canceled", StringComparison.Ordinal)
                || string.Equals(state, "Faulted", StringComparison.Ordinal);
        }

        private static void RefreshVariablesFromArguments(ActivityStateRecord record)
        {
            Dictionary<string, object> arguments = LocalsHelper.GetArgsFromRecord(record);
            if (arguments.Count == 0)
            {
                return;
            }

            Activity activity = GetActivity(record.Activity);
            List<ArgumentBinding> bindings = GetArgumentBindings(activity);
            if (bindings.Count == 0)
            {
                return;
            }

            bool executing = string.Equals(record.State, "Executing", StringComparison.Ordinal);

            foreach (ArgumentBinding binding in bindings)
            {
                if (!arguments.TryGetValue(binding.Name, out object value))
                {
                    continue;
                }

                bool apply = executing
                    ? binding.Direction != ArgumentDirection.Out
                    : binding.Direction != ArgumentDirection.In;

                if (!apply || string.IsNullOrEmpty(binding.VariableName))
                {
                    continue;
                }

                UpdateVariable(binding.VariableName, value);
            }
        }

        private static void UpdateVariable(string variableName, object value)
        {
            for (int i = Frames.Count - 1; i >= 0; i--)
            {
                Frame frame = Frames[i];
                if (frame.Variables.ContainsKey(variableName))
                {
                    frame.Variables[variableName] = value;
                    return;
                }
            }
        }

        private static void AddLocals(
            IDictionary<string, object> source,
            string scope,
            HashSet<string> seen,
            List<LocalVar> locals)
        {
            if (source == null)
            {
                return;
            }

            foreach (KeyValuePair<string, object> pair in source)
            {
                if (!seen.Add(pair.Key))
                {
                    continue;
                }

                Type type = pair.Value?.GetType();
                locals.Add(new LocalVar(pair.Key, pair.Value, type?.GetFormattedName(), scope));
            }
        }

        private static Dictionary<string, object> CopyVariables(ActivityStateRecord record)
        {
            var copy = new Dictionary<string, object>();
            IDictionary<string, object> variables = record.Variables;
            if (variables == null)
            {
                return copy;
            }

            foreach (KeyValuePair<string, object> pair in variables)
            {
                copy[pair.Key] = pair.Value;
            }

            return copy;
        }

        private static Activity GetActivity(ActivityInfo activityInfo)
        {
            if (activityInfo == null)
            {
                return null;
            }

            try
            {
                return activityInfo.GetProperty<Activity>("Activity");
            }
            catch
            {
                return null;
            }
        }

        private static int GetRelationshipToParent(Activity activity)
        {
            if (activity == null || RelationshipToParentProperty == null)
            {
                return 0; // RelationshipType.Child
            }

            try
            {
                object value = RelationshipToParentProperty.GetValue(activity, null);
                return Convert.ToInt32(value);
            }
            catch
            {
                return 0; // RelationshipType.Child
            }
        }

        private static List<ArgumentBinding> GetArgumentBindings(Activity activity)
        {
            var bindings = new List<ArgumentBinding>();

            if (activity == null)
            {
                return bindings;
            }

            IList<RuntimeArgument> runtimeArguments = GetRuntimeArguments(activity);
            if (runtimeArguments != null && runtimeArguments.Count > 0)
            {
                foreach (RuntimeArgument runtimeArgument in runtimeArguments)
                {
                    Argument argument = GetBoundArgument(runtimeArgument);
                    if (argument == null)
                    {
                        continue;
                    }

                    string variableName = TryGetVariableName(argument.Expression);
                    if (string.IsNullOrEmpty(variableName))
                    {
                        continue;
                    }

                    bindings.Add(new ArgumentBinding(runtimeArgument.Name, runtimeArgument.Direction, variableName));
                }

                if (bindings.Count > 0)
                {
                    return bindings;
                }
            }

            // Фолбэк для code-first активностей, у которых RuntimeArguments ещё не построены
            // (например, при ручном прогоне Observe в тестах без CacheMetadata).
            foreach (PropertyInfo property in activity.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!typeof(Argument).IsAssignableFrom(property.PropertyType))
                {
                    continue;
                }

                if (!(property.GetValue(activity, null) is Argument argument))
                {
                    continue;
                }

                string variableName = TryGetVariableName(argument.Expression);
                if (string.IsNullOrEmpty(variableName))
                {
                    continue;
                }

                bindings.Add(new ArgumentBinding(property.Name, argument.Direction, variableName));
            }

            return bindings;
        }

        private static IList<RuntimeArgument> GetRuntimeArguments(Activity activity)
        {
            if (RuntimeArgumentsProperty == null)
            {
                return null;
            }

            try
            {
                return RuntimeArgumentsProperty.GetValue(activity, null) as IList<RuntimeArgument>;
            }
            catch
            {
                return null;
            }
        }

        private static Argument GetBoundArgument(RuntimeArgument runtimeArgument)
        {
            if (BoundArgumentProperty == null)
            {
                return null;
            }

            try
            {
                return BoundArgumentProperty.GetValue(runtimeArgument, null) as Argument;
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetVariableName(object expression)
        {
            if (expression == null)
            {
                return null;
            }

            // Прямые ссылки на переменную: VariableValue<T> / VariableReference<T>.
            try
            {
                PropertyInfo variableProperty = expression.GetType()
                    .GetProperty("Variable", BindingFlags.Instance | BindingFlags.Public);
                if (variableProperty != null && typeof(Variable).IsAssignableFrom(variableProperty.PropertyType))
                {
                    if (variableProperty.GetValue(expression, null) is Variable variable
                        && !string.IsNullOrEmpty(variable.Name))
                    {
                        return variable.Name;
                    }
                }
            }
            catch
            {
                // best effort
            }

            // VB/C#-выражения: "varName" (возможно, в квадратных скобках).
            try
            {
                PropertyInfo textProperty = expression.GetType()
                    .GetProperty("ExpressionText", BindingFlags.Instance | BindingFlags.Public);
                if (textProperty != null && textProperty.PropertyType == typeof(string))
                {
                    string identifier = NormalizeIdentifier(textProperty.GetValue(expression, null) as string);
                    if (!string.IsNullOrEmpty(identifier))
                    {
                        return identifier;
                    }
                }
            }
            catch
            {
                // best effort
            }

            return null;
        }

        private static string NormalizeIdentifier(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            text = text.Trim();

            if (text.Length >= 2 && text[0] == '[' && text[text.Length - 1] == ']')
            {
                text = text.Substring(1, text.Length - 2).Trim();
            }

            return IsIdentifier(text) ? text : null;
        }

        private static bool IsIdentifier(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            if (!(char.IsLetter(text[0]) || text[0] == '_'))
            {
                return false;
            }

            for (int i = 1; i < text.Length; i++)
            {
                if (!(char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    return false;
                }
            }

            return true;
        }

        private static int FindFrameIndex(string instanceId)
        {
            for (int i = 0; i < Frames.Count; i++)
            {
                if (string.Equals(Frames[i].InstanceId, instanceId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void RemoveFrame(string instanceId)
        {
            int index = FindFrameIndex(instanceId);
            if (index >= 0)
            {
                Frames.RemoveAt(index);
            }
        }

        private sealed class Frame
        {
            public Frame(
                string instanceId,
                string name,
                Dictionary<string, object> variables,
                Dictionary<string, object> arguments,
                byte relationshipToParent)
            {
                InstanceId = instanceId;
                Name = name;
                Variables = variables;
                Arguments = arguments;
                RelationshipToParent = relationshipToParent;
            }

            public string InstanceId { get; }

            public string Name { get; }

            public Dictionary<string, object> Variables { get; }

            public Dictionary<string, object> Arguments { get; }

            public byte RelationshipToParent { get; }

            public bool IsImplementationChild => RelationshipToParent == RelationshipImplementationChild;
        }

        private sealed class ArgumentBinding
        {
            public ArgumentBinding(string name, ArgumentDirection direction, string variableName)
            {
                Name = name;
                Direction = direction;
                VariableName = variableName;
            }

            public string Name { get; }

            public ArgumentDirection Direction { get; }

            public string VariableName { get; }
        }
    }
}
