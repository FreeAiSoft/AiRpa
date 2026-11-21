using System;
using System.Activities;
using System.Collections.Generic;
using System.Reflection;
using ActivitiesTracker.Entities;
using ActivitiesTracker.Extensions;

namespace ActivitiesTracker.Core
{
    /// <summary>
    /// Единый источник истины для scope переменных/аргументов панели Locals.
    /// <para>
    /// Контракт зафиксирован тестами <c>LocalsScopeResolverTests</c> (Этап 1 плана
    /// <c>Locals_Scope_Correctness_Plan.md</c>). Обход учитывает <c>RelationshipToParent</c>
    /// и границу implementation: публичные переменные не протекают внутрь implementation-тела,
    /// а аргументы/implementation-переменные вложенной активности остаются видимы её телу.
    /// </para>
    /// </summary>
    public class LocalsScopeResolver
    {
        // Значения internal enum System.Activities.Activity+RelationshipType : byte.
        private const int RelationshipChild = 0x00;
        private const int RelationshipImportedChild = 0x01;
        private const int RelationshipImplementationChild = 0x02;
        private const int RelationshipDelegateHandler = 0x03;
        private const int RelationshipArgumentExpression = 0x04;
        private const int RelationshipVariableDefault = 0x05;

        private static readonly PropertyInfo ActivityParentProperty =
            typeof(Activity).GetProperty("Parent", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo ActivityRelationshipToParentProperty =
            typeof(Activity).GetProperty("RelationshipToParent", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo ActivityRuntimeVariablesProperty =
            typeof(Activity).GetProperty("RuntimeVariables", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo ActivityImplementationVariablesProperty =
            typeof(Activity).GetProperty("ImplementationVariables", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo ActivityRuntimeArgumentsProperty =
            typeof(Activity).GetProperty("RuntimeArguments", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo ActivityHandlerOfProperty =
            typeof(Activity).GetProperty("HandlerOf", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo ActivityInstanceEnvironmentProperty =
            typeof(ActivityInstance).GetProperty("Environment", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo ActivityDelegateRuntimeDelegateArgumentsProperty =
            typeof(ActivityDelegate).GetProperty("RuntimeDelegateArguments", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo VariableInternalGetLocationMethod =
            typeof(Variable).GetMethod("InternalGetLocation", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo RuntimeArgumentInternalGetLocationMethod =
            typeof(RuntimeArgument).GetMethod("InternalGetLocation", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo DelegateArgumentInternalGetLocationMethod =
            typeof(DelegateArgument).GetMethod("InternalGetLocation", BindingFlags.Instance | BindingFlags.NonPublic);

        public LocalsSnapshot Resolve(ActivityInstance instance, string state)
        {
            Activity current = instance?.Activity;
            if (current == null)
            {
                return new LocalsSnapshot(new LocalVar[0], null);
            }

            bool executing = IsExecuting(state);
            object environment = GetEnvironment(instance);
            string scope = current.DisplayName;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var locals = new List<LocalVar>();

            // 1. Собственные объявления текущей активности.
            AddArguments(current, environment, scope, state, executing, seen, locals);
            AddPublicVariables(current, environment, scope, state, seen, locals);
            AddImplementationVariables(current, environment, scope, state, seen, locals);

            ActivityDelegate currentHandler = GetHandlerOf(current);
            if (currentHandler != null)
            {
                AddDelegateArguments(currentHandler, environment, scope, state, executing, seen, locals);
            }

            // 2. Подъём по дереву Activity с учётом RelationshipToParent.
            Activity child = current;
            Activity parent = GetParent(child);
            while (parent != null)
            {
                int relationship = GetRelationship(child);

                if (relationship == RelationshipImplementationChild)
                {
                    // implementation-цепочка запечатана от публичных предков.
                    AddArguments(parent, environment, scope, state, executing, seen, locals);
                    AddImplementationVariables(parent, environment, scope, state, seen, locals);

                    ActivityDelegate parentHandler = GetHandlerOf(parent);
                    if (parentHandler != null)
                    {
                        AddDelegateArguments(parentHandler, environment, scope, state, executing, seen, locals);
                    }

                    break;
                }
                else if (relationship == RelationshipDelegateHandler)
                {
                    ActivityDelegate handler = GetHandlerOf(child);
                    if (handler != null)
                    {
                        AddDelegateArguments(handler, environment, scope, state, executing, seen, locals);
                    }
                }
                else
                {
                    // Child, ImportedChild, VariableDefault, ArgumentExpression.
                    AddPublicVariables(parent, environment, scope, state, seen, locals);
                }

                child = parent;
                parent = GetParent(parent);
            }

            // 3. Псевдопеременная this.
            AddThis(instance, scope, state, seen, locals);

            locals.Sort((left, right) => StringComparer.Ordinal.Compare(left.VarName, right.VarName));
            return new LocalsSnapshot(locals, scope, state);
        }

        private static object GetEnvironment(ActivityInstance instance)
        {
            if (instance == null || ActivityInstanceEnvironmentProperty == null)
            {
                return null;
            }

            try
            {
                return ActivityInstanceEnvironmentProperty.GetValue(instance);
            }
            catch
            {
                return null;
            }
        }

        private static Activity GetParent(Activity activity)
        {
            if (activity == null || ActivityParentProperty == null)
            {
                return null;
            }

            try
            {
                return ActivityParentProperty.GetValue(activity) as Activity;
            }
            catch
            {
                return null;
            }
        }

        private static int GetRelationship(Activity activity)
        {
            if (activity == null || ActivityRelationshipToParentProperty == null)
            {
                return RelationshipChild;
            }

            try
            {
                object value = ActivityRelationshipToParentProperty.GetValue(activity);
                return Convert.ToInt32(value);
            }
            catch
            {
                return RelationshipChild;
            }
        }

        private static ActivityDelegate GetHandlerOf(Activity activity)
        {
            if (activity == null || ActivityHandlerOfProperty == null)
            {
                return null;
            }

            try
            {
                return ActivityHandlerOfProperty.GetValue(activity) as ActivityDelegate;
            }
            catch
            {
                return null;
            }
        }

        private static IList<Variable> GetRuntimeVariables(Activity activity)
        {
            return GetPropertyValue(activity, ActivityRuntimeVariablesProperty) as IList<Variable>;
        }

        private static IList<Variable> GetImplementationVariables(Activity activity)
        {
            return GetPropertyValue(activity, ActivityImplementationVariablesProperty) as IList<Variable>;
        }

        private static IList<RuntimeArgument> GetRuntimeArguments(Activity activity)
        {
            return GetPropertyValue(activity, ActivityRuntimeArgumentsProperty) as IList<RuntimeArgument>;
        }

        private static IList<RuntimeDelegateArgument> GetRuntimeDelegateArguments(ActivityDelegate activityDelegate)
        {
            if (activityDelegate == null || ActivityDelegateRuntimeDelegateArgumentsProperty == null)
            {
                return null;
            }

            try
            {
                return ActivityDelegateRuntimeDelegateArgumentsProperty.GetValue(activityDelegate)
                    as IList<RuntimeDelegateArgument>;
            }
            catch
            {
                return null;
            }
        }

        private static object GetPropertyValue(Activity activity, PropertyInfo property)
        {
            if (activity == null || property == null)
            {
                return null;
            }

            try
            {
                return property.GetValue(activity);
            }
            catch
            {
                return null;
            }
        }

        private static void AddArguments(
            Activity activity,
            object environment,
            string scope,
            string state,
            bool executing,
            HashSet<string> seen,
            List<LocalVar> locals)
        {
            IList<RuntimeArgument> arguments = GetRuntimeArguments(activity);
            if (arguments == null)
            {
                return;
            }

            foreach (RuntimeArgument argument in arguments)
            {
                if (argument == null)
                {
                    continue;
                }

                Location location = ReadLocation(RuntimeArgumentInternalGetLocationMethod, argument, environment);
                if (location == null)
                {
                    continue;
                }

                bool isUnassigned = executing && argument.Direction == ArgumentDirection.Out;
                AddLocal(
                    argument.Name,
                    location,
                    argument.Type,
                    scope,
                    state,
                    LocalVarKind.Argument,
                    argument.Direction,
                    isUnassigned,
                    seen,
                    locals);
            }
        }

        private static void AddPublicVariables(
            Activity activity,
            object environment,
            string scope,
            string state,
            HashSet<string> seen,
            List<LocalVar> locals)
        {
            IList<Variable> variables = GetRuntimeVariables(activity);
            if (variables == null)
            {
                return;
            }

            foreach (Variable variable in variables)
            {
                if (variable == null)
                {
                    continue;
                }

                Location location = ReadLocation(VariableInternalGetLocationMethod, variable, environment);
                if (location == null)
                {
                    continue;
                }

                AddLocal(
                    variable.Name,
                    location,
                    variable.Type,
                    scope,
                    state,
                    LocalVarKind.Variable,
                    null,
                    false,
                    seen,
                    locals);
            }
        }

        private static void AddImplementationVariables(
            Activity activity,
            object environment,
            string scope,
            string state,
            HashSet<string> seen,
            List<LocalVar> locals)
        {
            IList<Variable> variables = GetImplementationVariables(activity);
            if (variables == null)
            {
                return;
            }

            foreach (Variable variable in variables)
            {
                if (variable == null)
                {
                    continue;
                }

                Location location = ReadLocation(VariableInternalGetLocationMethod, variable, environment);
                if (location == null)
                {
                    continue;
                }

                AddLocal(
                    variable.Name,
                    location,
                    variable.Type,
                    scope,
                    state,
                    LocalVarKind.Variable,
                    null,
                    false,
                    seen,
                    locals);
            }
        }

        private static void AddDelegateArguments(
            ActivityDelegate activityDelegate,
            object environment,
            string scope,
            string state,
            bool executing,
            HashSet<string> seen,
            List<LocalVar> locals)
        {
            IList<RuntimeDelegateArgument> arguments = GetRuntimeDelegateArguments(activityDelegate);
            if (arguments == null)
            {
                return;
            }

            foreach (RuntimeDelegateArgument runtimeArgument in arguments)
            {
                if (runtimeArgument == null)
                {
                    continue;
                }

                DelegateArgument boundArgument = runtimeArgument.BoundArgument;
                if (boundArgument == null)
                {
                    continue;
                }

                Location location = ReadLocation(DelegateArgumentInternalGetLocationMethod, boundArgument, environment);
                if (location == null)
                {
                    continue;
                }

                string name = string.IsNullOrEmpty(boundArgument.Name)
                    ? runtimeArgument.Name
                    : boundArgument.Name;

                bool isUnassigned = executing && boundArgument.Direction == ArgumentDirection.Out;
                AddLocal(
                    name,
                    location,
                    boundArgument.Type,
                    scope,
                    state,
                    LocalVarKind.DelegateArgument,
                    boundArgument.Direction,
                    isUnassigned,
                    seen,
                    locals);
            }
        }

        private static Location ReadLocation(MethodInfo method, object locationReference, object environment)
        {
            if (method == null || locationReference == null || environment == null)
            {
                return null;
            }

            try
            {
                return method.Invoke(locationReference, new[] { environment }) as Location;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsExecuting(string state)
        {
            return string.Equals(state, "Executing", StringComparison.Ordinal);
        }

        private static void AddLocal(
            string name,
            Location location,
            Type fallbackType,
            string scope,
            string state,
            LocalVarKind kind,
            ArgumentDirection? direction,
            bool isUnassigned,
            HashSet<string> seen,
            List<LocalVar> locals)
        {
            if (string.IsNullOrEmpty(name) || name.StartsWith("_", StringComparison.Ordinal))
            {
                return;
            }

            if (!seen.Add(name))
            {
                return;
            }

            Type type = location != null ? location.LocationType : fallbackType;
            string typeName = type != null ? type.GetFormattedName() : null;

            object value = null;
            if (!isUnassigned)
            {
                try
                {
                    value = location.Value;
                }
                catch
                {
                    // Значение могло быть уже недоступно (например, Out-аргумент до присваивания).
                }
            }

            locals.Add(new LocalVar(name, value, typeName, scope, kind, direction, state, isUnassigned));
        }

        private static void AddThis(
            ActivityInstance instance,
            string scope,
            string state,
            HashSet<string> seen,
            List<LocalVar> locals)
        {
            const string thisName = "this";
            if (!seen.Add(thisName))
            {
                return;
            }

            locals.Add(new LocalVar(thisName, instance, "System.Activities.ActivityInstance", scope, LocalVarKind.This, null, state));
        }
    }
}
