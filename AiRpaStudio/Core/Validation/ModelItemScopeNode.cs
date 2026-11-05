using System;
using System.Activities.Presentation.Model;
using System.Collections.Generic;

namespace RPA.Core.Services
{
    /// <summary>
    /// Реализация обхода (<see cref="DesignerValidationScope"/>) над настоящим <c>ModelItem</c>.
    /// <para>
    /// Чтение любого свойства модели может бросить исключение (у части активностей свойство
    /// вычисляется, у части — зависит от загруженных сборок), поэтому адаптер отдаёт «нет значения»
    /// вместо исключения: одна нечитаемая активность не должна обрывать проход по остальным.
    /// </para>
    /// </summary>
    internal sealed class ModelItemScopeNode : IDesignerScopeNode
    {
        private readonly ModelItem _item;

        internal ModelItemScopeNode(ModelItem item)
        {
            _item = item;
        }

        /// <summary>Элемент модели, к которому относится узел (для иконки и текста WF).</summary>
        internal ModelItem ModelItem => _item;

        public Type ItemType => _item?.ItemType;

        public object Identity => _item;

        public IEnumerable<IDesignerScopeProperty> Properties
        {
            get
            {
                if (_item == null)
                {
                    yield break;
                }

                ModelPropertyCollection properties;
                try
                {
                    properties = _item.Properties;
                }
                catch (Exception)
                {
                    yield break;
                }

                if (properties == null)
                {
                    yield break;
                }

                foreach (ModelProperty property in properties)
                {
                    if (property != null)
                    {
                        yield return new ModelPropertyScopeProperty(property);
                    }
                }
            }
        }
    }

    /// <summary>Свойство <c>ModelItem</c> в терминах обхода.</summary>
    internal sealed class ModelPropertyScopeProperty : IDesignerScopeProperty
    {
        private readonly ModelProperty _property;

        internal ModelPropertyScopeProperty(ModelProperty property)
        {
            _property = property;
        }

        public string Name => _property?.Name ?? string.Empty;

        public IDesignerScopeNode Value
        {
            get
            {
                try
                {
                    ModelItem value = _property?.Value;
                    return value == null ? null : new ModelItemScopeNode(value);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public IEnumerable<IDesignerScopeNode> Items
        {
            get
            {
                if (_property == null)
                {
                    yield break;
                }

                ModelItemCollection collection;
                ModelItemDictionary dictionary;
                try
                {
                    collection = _property.Collection;
                    dictionary = _property.Dictionary;
                }
                catch (Exception)
                {
                    yield break;
                }

                if (collection != null)
                {
                    foreach (ModelItem item in collection)
                    {
                        if (item != null)
                        {
                            yield return new ModelItemScopeNode(item);
                        }
                    }
                }

                if (dictionary == null)
                {
                    yield break;
                }

                foreach (KeyValuePair<ModelItem, ModelItem> pair in dictionary)
                {
                    if (pair.Value != null)
                    {
                        yield return new ModelItemScopeNode(pair.Value);
                    }
                }
            }
        }
    }
}
