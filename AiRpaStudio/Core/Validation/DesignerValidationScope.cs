using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace RPA.Core.Services
{
    /// <summary>
    /// Узел дерева дизайнера, по которому идёт обход валидации.
    /// <para>
    /// Интерфейс нужен, чтобы правила обхода (<see cref="DesignerValidationScope"/>) проверялись
    /// юнит-тестами на синтетическом дереве: настоящий <c>ModelItem</c> требует живого
    /// <c>WorkflowDesigner</c> с STA-потоком, а правила «спускаться в тело <c>ForEach</c>, в ветки
    /// <c>Pick</c>, в кастомный контейнер» — это чистая логика. Реализация над <c>ModelItem</c> —
    /// <see cref="ModelItemScopeNode"/>.
    /// </para>
    /// </summary>
    internal interface IDesignerScopeNode
    {
        /// <summary>Тип элемента модели (<c>ModelItem.ItemType</c>).</summary>
        Type ItemType { get; }

        /// <summary>Идентичность узла — только для защиты обхода от циклов (ссылка на <c>ModelItem</c>).</summary>
        object Identity { get; }

        /// <summary>Свойства узла. Исключения чтения свойств не должны ломать обход.</summary>
        IEnumerable<IDesignerScopeProperty> Properties { get; }
    }

    /// <summary>Свойство узла модели в терминах обхода (значение, коллекция, словарь).</summary>
    internal interface IDesignerScopeProperty
    {
        /// <summary>Имя свойства модели.</summary>
        string Name { get; }

        /// <summary>Значение-одиночка (или <c>null</c>).</summary>
        IDesignerScopeNode Value { get; }

        /// <summary>Элементы коллекции и значения словаря (пустая последовательность, если их нет).</summary>
        IEnumerable<IDesignerScopeNode> Items { get; }
    }

    /// <summary>Активность, найденная обходом: адрес в дереве модели плюс сам узел.</summary>
    internal sealed class DesignerScopeEntry
    {
        internal DesignerScopeEntry(string address, IDesignerScopeNode node)
        {
            Address = address ?? string.Empty;
            Node = node;
        }

        /// <summary>Адрес элемента: <c>root/Activities[0]/Body/Handler</c>. Уникален внутри обхода.</summary>
        internal string Address { get; }

        /// <summary>Узел модели.</summary>
        internal IDesignerScopeNode Node { get; }
    }

    /// <summary>
    /// Полный обход модели дизайнера: все активности, включая тела <c>ActivityAction</c>, ветки
    /// <c>Pick</c>, узлы <c>Flowchart</c>, обработчики <c>Catch</c>, состояния <c>StateMachine</c>
    /// и контейнеры кастомных активностей.
    /// <para>
    /// Заменяет прежний <c>DesignerValidator.GetModelItemChilds</c>, где в обходе коллекции стоял
    /// <c>else { break; }</c>: первый элемент коллекции «незнакомого» типа (например, тело
    /// <c>ForEach</c> — это <c>ActivityAction</c>, а не активность) обрывал обход всей коллекции.
    /// Из-за этого не валидировались тела <c>ForEach</c>/<c>While</c>, ветки <c>Pick</c>, обработчики
    /// <c>Catch</c> и содержимое кастомных контейнеров — то есть карточка «валидация кастомных
    /// активностей» не выполнялась.
    /// </para>
    /// <para>
    /// Правила обхода:
    /// <list type="bullet">
    /// <item>активность (<c>System.Activities.Activity</c>) — попадает в результат и обходится дальше;</item>
    /// <item>не-активность из пространства имён <c>System.Activities*</c> — контейнер
    /// (<c>ActivityAction</c>, <c>ActivityDelegate</c>, <c>FlowNode</c>, <c>Catch</c>, <c>State</c>,
    /// <c>PickBranch</c>, <c>Transition</c> и любые будущие обёртки WF): обходится без добавления в
    /// результат, поэтому новые типы WF не требуют правки обхода;</item>
    /// <item><c>Variable</c>/<c>LocationReference</c>/<c>Argument</c> и всё, что вне WF
    /// (строки, числа, домены данных), в обход не попадает: активности внутри них не объявляются;</item>
    /// <item>циклы (импортированный потомок, <c>Transition</c> между состояниями) отсекаются по
    /// идентичности узла — обход завершается, а не зависает.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal static class DesignerValidationScope
    {
        /// <summary>Адрес корня: обход начинается с реализации процесса (<c>Implementation</c>).</summary>
        internal const string RootAddress = "root";

        /// <summary>Все активности модели в порядке обхода. Первым идёт корень.</summary>
        internal static IReadOnlyList<DesignerScopeEntry> Collect(IDesignerScopeNode root)
        {
            List<DesignerScopeEntry> entries = new List<DesignerScopeEntry>();
            if (root != null)
            {
                VisitChild(root, RootAddress, entries, new HashSet<object>(NodeIdentityComparer.Instance));
            }

            return entries;
        }

        /// <summary>Тип является активностью WF.</summary>
        internal static bool IsActivityType(Type type)
        {
            return type != null && typeof(System.Activities.Activity).IsAssignableFrom(type);
        }

        /// <summary>
        /// Тип — контейнер WF, в который нужно спускаться, хотя сам он не активность.
        /// </summary>
        internal static bool IsContainerType(Type type)
        {
            if (type == null)
            {
                return false;
            }

            if (IsActivityType(type))
            {
                return true;
            }

            if (typeof(System.Activities.Variable).IsAssignableFrom(type)
                || typeof(System.Activities.LocationReference).IsAssignableFrom(type)
                || typeof(System.Activities.Argument).IsAssignableFrom(type))
            {
                return false;
            }

            string ns = type.Namespace;
            return ns != null && ns.StartsWith("System.Activities", StringComparison.Ordinal);
        }

        private static void Visit(
            IDesignerScopeNode node,
            string address,
            List<DesignerScopeEntry> entries,
            HashSet<object> visited)
        {
            if (node == null)
            {
                return;
            }

            object identity = node.Identity ?? node;
            if (!visited.Add(identity))
            {
                // Цикл в модели (Transition между состояниями, импортированный потомок): не зависаем.
                return;
            }

            IEnumerable<IDesignerScopeProperty> properties;
            try
            {
                properties = node.Properties;
            }
            catch (Exception)
            {
                // Узел, чьи свойства не читаются, не должен обрывать обход остальных.
                return;
            }

            if (properties == null)
            {
                return;
            }

            foreach (IDesignerScopeProperty property in properties)
            {
                if (property == null)
                {
                    continue;
                }

                string propertyAddress = address + "/" + property.Name;

                IDesignerScopeNode value;
                IEnumerable<IDesignerScopeNode> items;
                try
                {
                    value = property.Value;
                    items = property.Items;
                }
                catch (Exception)
                {
                    continue;
                }

                VisitChild(value, propertyAddress, entries, visited);

                if (items == null)
                {
                    continue;
                }

                int index = 0;
                foreach (IDesignerScopeNode item in items)
                {
                    VisitChild(
                        item,
                        propertyAddress + "[" + index.ToString(CultureInfo.InvariantCulture) + "]",
                        entries,
                        visited);
                    index++;
                }
            }
        }

        private static void VisitChild(
            IDesignerScopeNode child,
            string address,
            List<DesignerScopeEntry> entries,
            HashSet<object> visited)
        {
            if (child == null)
            {
                return;
            }

            Type itemType;
            try
            {
                itemType = child.ItemType;
            }
            catch (Exception)
            {
                return;
            }

            if (IsActivityType(itemType))
            {
                entries.Add(new DesignerScopeEntry(address, child));
                Visit(child, address, entries, visited);
                return;
            }

            if (IsContainerType(itemType))
            {
                // Тело ActivityAction, ветка Pick, узел Flowchart, Catch, State и любые обёртки WF.
                Visit(child, address, entries, visited);
            }
        }

        /// <summary>
        /// Сравнение узлов по ссылке: <c>ModelItem</c> переопределяет равенство, а защита от циклов
        /// должна отличать разные элементы модели, а не «равные по значению».
        /// </summary>
        private sealed class NodeIdentityComparer : IEqualityComparer<object>
        {
            internal static readonly NodeIdentityComparer Instance = new NodeIdentityComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        /// <summary>Человекочитаемая форма адреса (для сообщений и подсказок иконки).</summary>
        internal static string Describe(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                return RootAddress;
            }

            return address.Replace("/", " › ");
        }

        /// <summary>Склеивает адрес и подпись свойства (для сборки адресов вне обхода).</summary>
        internal static string Combine(string address, string propertyName)
        {
            StringBuilder builder = new StringBuilder(address ?? RootAddress);
            builder.Append('/').Append(propertyName ?? string.Empty);
            return builder.ToString();
        }
    }
}
