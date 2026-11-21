using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ActivitiesTracker.Entities;

namespace RPA.Locals
{
    public class ValueInspector
    {
        public const int DefaultMaxDepth = 10;
        public const int DefaultMaxItems = 100;

        public int MaxDepth { get; set; } = DefaultMaxDepth;

        public int MaxItems { get; set; } = DefaultMaxItems;

        public IReadOnlyList<LocalsNode> BuildNodes(IEnumerable<LocalVar> locals)
        {
            if (locals == null)
            {
                return Array.Empty<LocalsNode>();
            }

            List<LocalsNode> nodes = new List<LocalsNode>();
            foreach (LocalVar local in locals)
            {
                nodes.Add(BuildNode(local));
            }

            return nodes;
        }

        public LocalsNode BuildNode(string name, object value)
        {
            return BuildNode(name, value, 0, Array.Empty<object>());
        }
        public LocalsNode BuildNode(LocalVar local)
        {
            if (local == null)
            {
                return BuildNode(null, null);
            }

            if (local.IsUnassigned)
            {
                return new LocalsNode(local.VarName, "не присвоен", local.TypeName, LocalsNodeKind.Null, false, null);
            }

            return BuildNode(local.VarName, local.Data);
        }


        private LocalsNode BuildNode(string name, object value, int depth, IReadOnlyList<object> ancestors)
        {
            if (value is DBNull)
            {
                return new LocalsNode(name, "DBNull", "DBNull", LocalsNodeKind.Null, false, null);
            }

            if (value == null)
            {
                return new LocalsNode(name, "null", null, LocalsNodeKind.Null, false, null);
            }

            Type type = value.GetType();
            string typeName = value is Type ? "Type" : FormatTypeName(type);

            if (IsScalar(value))
            {
                return new LocalsNode(name, FormatScalar(value), typeName, LocalsNodeKind.Scalar, false, null, value);
            }

            if (depth >= MaxDepth)
            {
                return new LocalsNode(name, "(max depth)", typeName, GetKind(value), false, null);
            }

            if (ContainsReference(ancestors, value))
            {
                return new LocalsNode(name, "(cycle)", typeName, LocalsNodeKind.Error, false, null);
            }

            IReadOnlyList<object> childAncestors = Append(ancestors, value);
            Func<IReadOnlyList<LocalsNode>> loader = () => BuildChildren(value, depth + 1, childAncestors);

            return new LocalsNode(name, GetSummary(value), typeName, GetKind(value), true, loader, value);
        }

        private IReadOnlyList<LocalsNode> BuildChildren(object value, int childDepth, IReadOnlyList<object> ancestors)
        {
            switch (GetKind(value))
            {
                case LocalsNodeKind.Table:
                    return BuildTableChildren(value, childDepth, ancestors);
                case LocalsNodeKind.Dictionary:
                    return BuildDictionaryChildren((IDictionary)value, childDepth, ancestors);
                case LocalsNodeKind.Collection:
                    return BuildCollectionChildren((IEnumerable)value, childDepth, ancestors);
                default:
                    return BuildObjectChildren(value, childDepth, ancestors);
            }
        }

        private static LocalsNodeKind GetKind(object value)
        {
            if (value == null || value is DBNull)
            {
                return LocalsNodeKind.Null;
            }

            if (IsScalar(value))
            {
                return LocalsNodeKind.Scalar;
            }

            if (value is DataTable || value is DataView)
            {
                return LocalsNodeKind.Table;
            }

            if (value is IDictionary)
            {
                return LocalsNodeKind.Dictionary;
            }

            if (value is IEnumerable)
            {
                return LocalsNodeKind.Collection;
            }

            return LocalsNodeKind.Object;
        }

        private static bool IsScalar(object value)
        {
            if (value == null)
            {
                return false;
            }

            Type type = value.GetType();

            return type.IsPrimitive
                || value is string
                || value is Enum
                || value is decimal
                || value is DateTime
                || value is TimeSpan
                || value is Guid
                || value is DateTimeOffset
                || value is Type;
        }

        private static string FormatScalar(object value)
        {
            if (value is Type type)
            {
                return type.FullName;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static string FormatTypeName(Type type)
        {
            if (type == null)
            {
                return null;
            }

            if (type.IsArray)
            {
                return FormatTypeName(type.GetElementType()) + "[]";
            }

            if (type.IsGenericType)
            {
                string name = type.Name;
                int tick = name.IndexOf('`');
                if (tick >= 0)
                {
                    name = name.Substring(0, tick);
                }

                string arguments = string.Join(", ", type.GetGenericArguments().Select(FormatTypeName));
                return name + "<" + arguments + ">";
            }

            return type.Name;
        }

        private static string GetSummary(object value)
        {
            switch (GetKind(value))
            {
                case LocalsNodeKind.Collection:
                    return GetCountSummary(value as ICollection);
                case LocalsNodeKind.Dictionary:
                    return GetCountSummary(value as ICollection);
                case LocalsNodeKind.Table:
                    return GetTableSummary(value);
                default:
                    return "{" + FormatTypeName(value.GetType()) + "}";
            }
        }

        private static string GetCountSummary(ICollection collection)
        {
            if (collection == null)
            {
                return "(enumerable)";
            }

            try
            {
                return "Count = " + collection.Count;
            }
            catch
            {
                return "(enumerable)";
            }
        }

        private static string GetTableSummary(object value)
        {
            try
            {
                if (value is DataTable table)
                {
                    return "Rows = " + table.Rows.Count + ", Columns = " + table.Columns.Count;
                }

                if (value is DataView view)
                {
                    int columns = view.Table?.Columns.Count ?? 0;
                    return "Rows = " + view.Count + ", Columns = " + columns;
                }
            }
            catch
            {
                // fall through to default
            }

            return "(table)";
        }

        private IReadOnlyList<LocalsNode> BuildCollectionChildren(IEnumerable enumerable, int childDepth, IReadOnlyList<object> ancestors)
        {
            List<LocalsNode> nodes = new List<LocalsNode>();
            bool truncated = false;
            int index = 0;

            try
            {
                foreach (object item in enumerable)
                {
                    if (index >= MaxItems)
                    {
                        truncated = true;
                        break;
                    }

                    nodes.Add(BuildNode("[" + index + "]", item, childDepth, ancestors));
                    index++;
                }
            }
            catch (Exception ex)
            {
                return SingleError(ex);
            }

            if (truncated)
            {
                nodes.Add(TruncationIndicator());
            }

            return nodes;
        }

        private IReadOnlyList<LocalsNode> BuildDictionaryChildren(IDictionary dictionary, int childDepth, IReadOnlyList<object> ancestors)
        {
            List<LocalsNode> nodes = new List<LocalsNode>();
            bool truncated = false;
            int index = 0;

            try
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (index >= MaxItems)
                    {
                        truncated = true;
                        break;
                    }

                    nodes.Add(BuildDictionaryEntryNode(index, entry, childDepth, ancestors));
                    index++;
                }
            }
            catch (Exception ex)
            {
                return SingleError(ex);
            }

            if (truncated)
            {
                nodes.Add(TruncationIndicator());
            }

            return nodes;
        }

        private LocalsNode BuildDictionaryEntryNode(int index, DictionaryEntry entry, int entryDepth, IReadOnlyList<object> ancestors)
        {
            string key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "null";
            Func<IReadOnlyList<LocalsNode>> loader = () => new List<LocalsNode>
            {
                BuildNode("Key", entry.Key, entryDepth + 1, ancestors),
                BuildNode("Value", entry.Value, entryDepth + 1, ancestors)
            };

            return new LocalsNode("[" + key + "]", string.Empty, "DictionaryEntry", LocalsNodeKind.Dictionary, true, loader);
        }

        private IReadOnlyList<LocalsNode> BuildObjectChildren(object value, int childDepth, IReadOnlyList<object> ancestors)
        {
            MemberInfo[] members = GetMembers(value.GetType());
            List<LocalsNode> nodes = new List<LocalsNode>();

            int count = Math.Min(members.Length, MaxItems);
            for (int i = 0; i < count; i++)
            {
                MemberInfo member = members[i];
                object childValue;
                try
                {
                    childValue = ReadMember(member, value);
                }
                catch (Exception ex)
                {
                    nodes.Add(ErrorNode(member.Name, ex));
                    continue;
                }

                nodes.Add(BuildNode(member.Name, childValue, childDepth, ancestors));
            }

            if (members.Length > count)
            {
                nodes.Add(TruncationIndicator(members.Length - count));
            }

            return nodes;
        }

        private IReadOnlyList<LocalsNode> BuildTableChildren(object value, int childDepth, IReadOnlyList<object> ancestors)
        {
            if (value is DataView dataView)
            {
                return BuildDataViewChildren(dataView, childDepth, ancestors);
            }

            DataTable table = (DataTable)value;
            List<LocalsNode> nodes = new List<LocalsNode>();
            bool truncated = false;
            int index = 0;

            foreach (DataRow row in table.Rows)
            {
                if (index >= MaxItems)
                {
                    truncated = true;
                    break;
                }

                nodes.Add(BuildRowNode(index, row, table, childDepth, ancestors));
                index++;
            }

            if (truncated)
            {
                nodes.Add(TruncationIndicator());
            }

            return nodes;
        }

        private IReadOnlyList<LocalsNode> BuildDataViewChildren(DataView dataView, int childDepth, IReadOnlyList<object> ancestors)
        {
            List<LocalsNode> nodes = new List<LocalsNode>();
            bool truncated = false;
            int index = 0;

            foreach (DataRowView rowView in dataView)
            {
                if (index >= MaxItems)
                {
                    truncated = true;
                    break;
                }

                nodes.Add(BuildRowViewNode(index, rowView, childDepth, ancestors));
                index++;
            }

            if (truncated)
            {
                nodes.Add(TruncationIndicator());
            }

            return nodes;
        }

        private LocalsNode BuildRowNode(int rowIndex, DataRow row, DataTable table, int rowDepth, IReadOnlyList<object> ancestors)
        {
            IReadOnlyList<object> rowAncestors = Append(ancestors, row);
            Func<IReadOnlyList<LocalsNode>> loader = () => BuildRowCells(table.Columns, col => row[col], rowDepth + 1, rowAncestors);

            return new LocalsNode("[" + rowIndex + "]", "Row", "DataRow", LocalsNodeKind.Object, true, loader);
        }

        private LocalsNode BuildRowViewNode(int rowIndex, DataRowView rowView, int rowDepth, IReadOnlyList<object> ancestors)
        {
            IReadOnlyList<object> rowAncestors = Append(ancestors, rowView);
            DataColumnCollection columns = rowView.Row?.Table?.Columns;
            Func<IReadOnlyList<LocalsNode>> loader = () => BuildRowCells(columns, col => rowView[col.ColumnName], rowDepth + 1, rowAncestors);

            return new LocalsNode("[" + rowIndex + "]", "Row", "DataRowView", LocalsNodeKind.Object, true, loader);
        }

        private IReadOnlyList<LocalsNode> BuildRowCells(DataColumnCollection columns, Func<DataColumn, object> cellReader, int cellDepth, IReadOnlyList<object> ancestors)
        {
            List<LocalsNode> nodes = new List<LocalsNode>();
            if (columns == null)
            {
                return nodes;
            }

            foreach (DataColumn column in columns)
            {
                object cell;
                try
                {
                    cell = cellReader(column);
                }
                catch (Exception ex)
                {
                    nodes.Add(ErrorNode(column.ColumnName, ex));
                    continue;
                }

                nodes.Add(BuildNode(column.ColumnName, cell, cellDepth, ancestors));
            }

            return nodes;
        }

        private static MemberInfo[] GetMembers(Type type)
        {
            MemberInfo[] properties = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Cast<MemberInfo>()
                .ToArray();

            MemberInfo[] fields = type
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Cast<MemberInfo>()
                .ToArray();

            return properties.Concat(fields).ToArray();
        }

        private static object ReadMember(MemberInfo member, object instance)
        {
            switch (member)
            {
                case PropertyInfo property:
                    return property.GetValue(instance, null);
                case FieldInfo field:
                    return field.GetValue(instance);
                default:
                    return null;
            }
        }

        private static LocalsNode ErrorNode(string name, Exception exception)
        {
            return new LocalsNode(name, "(error: " + GetExceptionMessage(exception) + ")", null, LocalsNodeKind.Error, false, null);
        }

        private static string GetExceptionMessage(Exception exception)
        {
            if (exception is TargetInvocationException tie && tie.InnerException != null)
            {
                return tie.InnerException.Message;
            }

            return exception?.Message ?? "unknown";
        }

        private static LocalsNode TruncationIndicator(int remaining = -1)
        {
            string message = remaining >= 0 ? "(" + remaining + " more)" : "(more items)";
            return new LocalsNode("…", message, null, LocalsNodeKind.Error, false, null);
        }

        private static IReadOnlyList<LocalsNode> SingleError(Exception exception)
        {
            return new List<LocalsNode> { ErrorNode("(enumeration)", exception) };
        }

        private static IReadOnlyList<object> Append(IReadOnlyList<object> ancestors, object value)
        {
            List<object> list = new List<object>(ancestors) { value };
            return list;
        }

        private static bool ContainsReference(IReadOnlyList<object> ancestors, object value)
        {
            for (int i = 0; i < ancestors.Count; i++)
            {
                if (ReferenceEquals(ancestors[i], value))
                {
                    return true;
                }
            }

            return false;
        }
    }
}