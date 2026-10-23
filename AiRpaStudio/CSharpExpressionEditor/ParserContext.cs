using System;
using System.Activities;
using System.Activities.Presentation;
using System.Activities.Presentation.Hosting;
using System.Activities.Presentation.Model;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Markup;
using System.Xaml;

namespace RPA.CSharpExpressionEditor
{
    internal class ParserContext : LocationReferenceEnvironment, IValueSerializerContext, IXamlNameResolver, INamespacePrefixLookup, IXamlNamespaceResolver
    {
        private ModelItem baseModelItem;
        private EditingContext context;
        private IDictionary<string, string> namespaceLookup;

        public IContainer Container => null;

        public object Instance
        {
            get;
            internal set;
        }

        public PropertyDescriptor PropertyDescriptor
        {
            get;
            internal set;
        }

        public override Activity Root => null;

        private IDictionary<string, string> NamespaceLookup
        {
            get
            {
                namespaceLookup ??= new Dictionary<string, string>();
                return namespaceLookup;
            }
        }

        public bool IsFixupTokenAvailable => false;

        internal IEnumerable<string> Namespaces
        {
            get
            {
                HashSet<string> namespacesToReturn = new HashSet<string>();

                //with custom ones, defined in user provided assemblies
                if (null != namespaceLookup)
                {
                    foreach (string nameSpace in namespaceLookup.Keys)
                    {
                        //out of full namespace declaration (i.e. "clr-namespace:<namespace>;assembly=<assembly>"
                        //get clear namespace name
                        int startIndex = nameSpace.IndexOf(":", StringComparison.Ordinal);
                        int endIndex = nameSpace.IndexOf(";", StringComparison.Ordinal);
                        if (startIndex >= 0 && endIndex >= 0)
                        {
                            string clrNamespace = nameSpace.Substring(startIndex + 1, endIndex - startIndex - 1);
                            _ = namespacesToReturn.Add(clrNamespace);
                        }
                    }
                }

                ImportedNamespaceContextItem importedNamespaces = context.Items.GetValue<ImportedNamespaceContextItem>();
                namespacesToReturn.UnionWith(importedNamespaces.ImportedNamespaces);
                //return all namespaces
                return namespacesToReturn;
            }
        }

        public ParserContext()
        {
        }

        public ParserContext(ModelItem modelItem)
        {
            _ = Initialize(modelItem);
        }

        public bool Initialize(ModelItem modelItem)
        {
            baseModelItem = modelItem;
            if (null != modelItem)
            {
                context = modelItem.GetEditingContext();
            }
            return null != baseModelItem;
        }

        public override bool IsVisible(LocationReference reference)
        {
            object other = Resolve(reference.Name);

            return ReferenceEquals(other, reference);
        }

        public override bool TryGetLocationReference(string name, out LocationReference result)
        {
            result = (LocationReference)Resolve(name);
            return result != null;
        }


        public string GetNamespace(string prefix)
        {
            string nameSpace = NamespaceLookup
                .Where(p => string.Equals(p.Value, prefix))
                .Select(p => p.Key)
                .FirstOrDefault();

            return nameSpace;
        }

        public IEnumerable<NamespaceDeclaration> GetNamespacePrefixes()
        {
            List<NamespaceDeclaration> namespacePrefixes = new List<NamespaceDeclaration>();
            LoadNameSpace(namespacePrefixes);
            return namespacePrefixes;
        }

        public override IEnumerable<LocationReference> GetLocationReferences()
        {
            List<LocationReference> toReturn = new List<LocationReference>();
            if (baseModelItem != null)
            {
                List<ModelItem> declaredVariables = CSharpExpressionHelper.GetVariablesInScope(baseModelItem);

                foreach (ModelItem modelItem in declaredVariables)
                {
                    toReturn.Add(modelItem.GetCurrentValue() as LocationReference);
                }
            }
            return toReturn;
        }

        public object Resolve(string name)
        {
            IEnumerable<LocationReference> variables = GetLocationReferences();
            return variables.FirstOrDefault(p =>
            {
                return p != null && p.Name != null && p.Name.Equals(name);
            });
        }

        public object Resolve(string name, out bool isFullyInitialized)
        {
            object result = Resolve(name);
            isFullyInitialized = result != null;
            return result;
        }

        public object GetFixupToken(IEnumerable<string> names)
        {
            return null;
        }

        public object GetFixupToken(IEnumerable<string> names, bool canAssignDirectly)
        {
            return null;
        }

        public object GetService(Type serviceType)
        {
            return serviceType == typeof(IXamlNameResolver)
                || serviceType == typeof(INamespacePrefixLookup)
                || serviceType == typeof(IXamlNamespaceResolver)
                ? this
                : (object)null;
        }

        public ValueSerializer GetValueSerializerFor(Type type)
        {
            return null;
        }

        public ValueSerializer GetValueSerializerFor(PropertyDescriptor descriptor)
        {
            return null;
        }

        public string LookupPrefix(string ns)
        {
            //get reference to namespace lookup dictionary (create one if necessary)
            IDictionary<string, string> lookupTable = NamespaceLookup;
            //check if given namespace is already registered
            if (!lookupTable.TryGetValue(ns, out string prefix))
            {
                //no, create a unique prefix
                prefix = string.Format(CultureInfo.InvariantCulture, "__{0}", Guid.NewGuid().ToString().Replace("-", "").Substring(0, 5));
                //and store value in the dictionary
                lookupTable[ns] = prefix;
            }
            //return prefix
            return prefix;
        }

        public void OnComponentChanged()
        {
            throw new NotSupportedException("Not Supported");
        }

        public bool OnComponentChanging()
        {
            throw new NotSupportedException("Not Supported");
        }

        private void LoadNameSpace(List<NamespaceDeclaration> result)
        {
            if (null == context)
            {
                //Fx.Assert("EditingContext is null");
                return;
            }
            AssemblyContextControlItem assemblyContext = context.Items.GetValue<AssemblyContextControlItem>();
            if (null == assemblyContext)
            {
                //Fx.Assert("AssemblyContextControlItem not defined in EditingContext.Items");
                return;
            }
            if (null != assemblyContext.LocalAssemblyName)
            {
                result.Add(GetEntry(assemblyContext.LocalAssemblyName));
            }
            if (null != assemblyContext.ReferencedAssemblyNames)
            {
                foreach (AssemblyName name in assemblyContext.ReferencedAssemblyNames)
                {
                    result.Add(GetEntry(name));
                }
            }
        }

        private NamespaceDeclaration GetEntry(AssemblyName name)
        {
            string ns =
                string.Format(CultureInfo.InvariantCulture, "clr-namespace:{0};assembly={1}",
                Guid.NewGuid().ToString().Replace('-', '_'), name.Name);
            return new NamespaceDeclaration(ns, Guid.NewGuid().ToString());
        }

        IEnumerable<KeyValuePair<string, object>> IXamlNameResolver.GetAllNamesAndValuesInScope()
        {
            return null;
        }

        event EventHandler IXamlNameResolver.OnNameScopeInitializationComplete
        {
            add { }
            remove { }
        }

    }
}
