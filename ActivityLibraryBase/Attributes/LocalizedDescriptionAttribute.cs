using System;
using System.ComponentModel;
using System.Resources;

namespace ActivityLibraryBase.Attributes
{
    public class LocalizedDescriptionAttribute : DescriptionAttribute
    {
        private readonly string _resourceKey;
        private readonly Type _resourceType;
        private readonly ResourceManager _resourceManager;

        public LocalizedDescriptionAttribute(string resourceKey, Type resourceType)
        {
            _resourceKey = resourceKey;
            _resourceType = resourceType;
            _resourceManager = new ResourceManager(_resourceType);
        }

        public override string Description
        {
            get
            {
                return _resourceManager.GetString(_resourceKey);
            }
        }
    }
}
