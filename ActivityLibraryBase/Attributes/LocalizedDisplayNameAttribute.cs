using System;
using System.ComponentModel;
using System.Resources;

namespace ActivityLibraryBase.Attributes
{
    public class LocalizedDisplayNameAttribute : DisplayNameAttribute
    {
        private readonly string _resourceKey;
        private readonly Type _resourceType;

        public LocalizedDisplayNameAttribute(string resourceKey, Type resourceType)
        {
            _resourceKey = resourceKey;
            _resourceType = resourceType;
        }

        public override string DisplayName
        {
            get
            {
                ResourceManager resourceMan = new ResourceManager(_resourceType);
                return resourceMan.GetString(_resourceKey);
            }
        }
    }
}
