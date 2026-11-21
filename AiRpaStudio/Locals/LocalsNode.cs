using System;
using System.Collections.Generic;

namespace RPA.Locals
{
    public class LocalsNode
    {
        private readonly Func<IReadOnlyList<LocalsNode>> childrenLoader;
        private IReadOnlyList<LocalsNode> children;

        public string Name { get; }

        public string DisplayValue { get; }

        public string TypeName { get; }

        public LocalsNodeKind Kind { get; }

        public object Value { get; }

        public bool IsExpandable { get; }

        public bool IsExpanded { get; set; }

        public IReadOnlyList<LocalsNode> Children
        {
            get
            {
                if (children == null && childrenLoader != null)
                {
                    children = childrenLoader() ?? Array.Empty<LocalsNode>();
                }

                return children ?? Array.Empty<LocalsNode>();
            }
        }

        public LocalsNode(
            string name,
            string displayValue,
            string typeName,
            LocalsNodeKind kind,
            bool isExpandable,
            Func<IReadOnlyList<LocalsNode>> childrenLoader,
            object value = null)
        {
            Name = name;
            DisplayValue = displayValue;
            TypeName = typeName;
            Kind = kind;
            Value = value;
            IsExpandable = isExpandable;
            this.childrenLoader = childrenLoader;
        }
    }
}