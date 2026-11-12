using System;
using System.Activities;
using System.Xaml;
using System.Xaml.Schema;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Виртуальный XAML-тип для вложенной активности: резолвит <c>clr-namespace:XXX</c> (без сборки)
    /// в <see cref="DynamicActivity"/>, то есть в «тип без сборки». Экземпляр создаётся с уже
    /// заполненным <c>DynamicActivity.Name</c> — полным именем (<c>x:Class</c>).
    /// </summary>
    internal sealed class LooseActivityXamlType : XamlType
    {
        private readonly XamlTypeFacade _facade;

        internal LooseActivityXamlType(XamlTypeFacade facade, XamlSchemaContext schemaContext)
            : base(typeof(DynamicActivity), schemaContext)
        {
            _facade = facade ?? throw new ArgumentNullException(nameof(facade));
        }

        protected override XamlTypeInvoker LookupInvoker()
        {
            return new LooseActivityXamlTypeInvoker(this, _facade);
        }
    }

    /// <summary>
    /// Инвокер виртуального типа: создаёт <see cref="DynamicActivity"/> и сразу задаёт ей
    /// <c>Name = x:Class</c>, чтобы дизайнер и логика резолва видели «настоящее» имя процесса.
    /// </summary>
    internal sealed class LooseActivityXamlTypeInvoker : XamlTypeInvoker
    {
        private readonly XamlTypeFacade _facade;

        internal LooseActivityXamlTypeInvoker(XamlType xamlType, XamlTypeFacade facade)
            : base(xamlType)
        {
            _facade = facade;
        }

        public override object CreateInstance(object[] arguments)
        {
            // Name = x:Class (полное имя типа) нужно резолву ссылок и сериализации обратно в <local:SubProcessN/>.
            // DisplayName = короткое имя класса — иначе блок в конструкторе подписывается CLR-типом
            // «DynamicActivity», а не именем вложенного процесса («SubProcess1»).
            var activity = new DynamicActivity
            {
                Name = _facade.FullName,
                DisplayName = _facade.Name
            };

            foreach (XamlTypeFacadeArgument argument in _facade.Arguments)
            {
                activity.Properties.Add(new DynamicActivityProperty
                {
                    Name = argument.Name,
                    Type = ResolveArgumentType(argument.Type)
                });
            }

            return activity;
        }

        private static Type ResolveArgumentType(string rawType)
        {
            string inner = rawType ?? string.Empty;

            int open = inner.IndexOf('(');
            int close = inner.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                inner = inner.Substring(open + 1, close - open - 1).Trim();
            }

            switch (inner)
            {
                case "x:String": return typeof(string);
                case "x:Int32": return typeof(int);
                case "x:Int64": return typeof(long);
                case "x:Boolean": return typeof(bool);
                case "x:Double": return typeof(double);
                case "x:Decimal": return typeof(decimal);
                case "x:DateTime": return typeof(DateTime);
                case "x:TimeSpan": return typeof(TimeSpan);
                case "x:Object": return typeof(object);
                default:
                    Type resolved = Type.GetType(inner, throwOnError: false);
                    return resolved ?? typeof(object);
            }
        }
    }

    /// <summary>
    /// Кастомный <see cref="XamlSchemaContext"/> для loose-XAML: для namespace вида
    /// <c>clr-namespace:XXX</c> без <c>;assembly=</c> возвращает виртуальный тип
    /// <see cref="LooseActivityXamlType"/>, если имя зарегистрировано в <see cref="WorkflowTypeRegistry"/>.
    /// Остальные типы резолвит стандартно (в базовом контексте).
    /// </summary>
    internal sealed class LooseActivityXamlSchemaContext : XamlSchemaContext
    {
        private const string ClrNamespacePrefix = "clr-namespace:";

        private readonly WorkflowTypeRegistry _registry;

        internal LooseActivityXamlSchemaContext(WorkflowTypeRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        protected override XamlType GetXamlType(string xamlNamespace, string name, params XamlType[] typeArguments)
        {
            if (TryGetLocalFullName(xamlNamespace, name, out string fullName)
                && _registry.TryGet(fullName, out XamlTypeFacade facade))
            {
                return new LooseActivityXamlType(facade, this);
            }

            return base.GetXamlType(xamlNamespace, name, typeArguments);
        }

        private static bool TryGetLocalFullName(string xamlNamespace, string name, out string fullName)
        {
            fullName = null;

            if (string.IsNullOrWhiteSpace(xamlNamespace)
                || string.IsNullOrWhiteSpace(name)
                || !xamlNamespace.StartsWith(ClrNamespacePrefix, StringComparison.Ordinal)
                || xamlNamespace.IndexOf(";assembly=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            string clrNamespace = xamlNamespace.Substring(ClrNamespacePrefix.Length).Trim();
            if (clrNamespace.Length == 0)
            {
                return false;
            }

            fullName = clrNamespace + "." + name;
            return true;
        }
    }
}
